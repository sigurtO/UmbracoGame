using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Services;
using UmbracoGame.Interface;
using UmbracoGame.Models;

namespace UmbracoGame.Service
{
    public class MistralAiValidationService : IAiValidationService
    {
        private readonly IContentService _contentService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly string _mistralApiKey;

        public MistralAiValidationService(IContentService contentService, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _contentService = contentService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _mistralApiKey = _configuration["MistralAi:ApiKey"];
        }

        public async Task<AiValidationResult> ValidateRunAsync(RunReportData payload)
        {
            string cleanApiKey = _mistralApiKey?.Trim();
            if (string.IsNullOrEmpty(cleanApiKey)) // makes sure everything still works even if the api key is missing
            {
                return new AiValidationResult { LegitimacyPercentage = 0, FlagReason = "LOKAL FEJL: Mistral API-nøgle mangler." };
            }

            // --- finds leaderboard ---
            var leaderboardPage = _contentService.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == "leaderboardPage");

            if (leaderboardPage == null)
                return new AiValidationResult { LegitimacyPercentage = 0, FlagReason = "Leaderboard ikke fundet, kunne ikke hente læringsdata." };
            // gets all runs
            var allSavedRuns = _contentService.GetPagedChildren(leaderboardPage.Id, 0, 100, out long totalRecords);
            //bruger kun runs som er marked by admin
            var aiImprovementRuns = allSavedRuns
                .Where(x => x.GetValue<bool>("sendToAiForImprovements") == true)
                .Take(10);

            StringBuilder dynamicExamples = new StringBuilder();
            foreach (var historicalRun in aiImprovementRuns) // reads through each run
            {
                bool isLegit = historicalRun.GetValue<bool>("isAiRight");
                string statusText = isLegit ? "BEKRÆFTET LEGITIMT AF MENNESKELIG ADMIN" : "BEKRÆFTET SNYD/MANIPULERET AF MENNESKELIG ADMIN";
                string adminNote = historicalRun.GetValue<string>("adminComment") ?? "Intet noteret.";

                dynamicExamples.AppendLine($@"[Særligt Læringseksempel - {statusText}]:
                Encounters: {historicalRun.GetValue<int>("encountersSurvived")}, Kills: {historicalRun.GetValue<int>("enemiesKilled")}, 
                TotalDmg: {historicalRun.GetValue<int>("totalDamageTaken")}, Absorbed: {historicalRun.GetValue<int>("damageAbsorbed")}, 
                MaxTurnDmg: {historicalRun.GetValue<int>("highestSingleTurnDamage")}, PerfectFights: {historicalRun.GetValue<int>("perfectFights")}, 
                Time: {historicalRun.GetValue<decimal>("timeSpentInBattle")}s.
                Forklaring fra admin på hvorfor dette run er markeret som {(isLegit ? "legitimt" : "snyd")}: {adminNote}
                -----------------------------------");
            }

            if (dynamicExamples.Length == 0)
            {
                dynamicExamples.AppendLine("[Ingen specielle manuelle læringseksempler tilgængelige endnu. Stol udelukkende på dine standard grænseværdier].");
            }

            try // anti cheat agent prompt
            {
                string prompt = $@"
                Persona: Du er en senior anti-cheat sikkerhedsanalytiker specialiseret i turbaserede kortspil.
                Opgave: Vurder om indsendte data fra et spil-run er legitime eller manipuleret via memory-editing (f.eks. Cheat Engine).

                Analyse-Parametre & Grænseværdier (Fundamentale Regler):
                1. Skade-Lofte (Hard Cap): Spilleren starter med 100 HP og kan max heale 90 HP totalt (3 rest sites á 30%). Enhver 'totalDamageTaken' over 189 uden at dø er matematisk umulig og tyder på HP-hacks.
                2. Damage-Output: En gennemsnitlig fjende har 50 HP. 'highestSingleTurnDamage' over 100 er ekstremt mistænkeligt og tyder på damage-multipliers.
                3. Tids-Audit: (timeSpentInBattle / encountersSurvived). Hvis gennemsnittet er under 6 sekunder pr. kamp, er det fysisk umuligt for et menneske at læse kortene og tage beslutninger.
                4. Korrelations-Check: 
                   - Mange 'Perfect Fights' skal korrelere med lav 'totalDamageTaken'.
                   - Høj 'totalDamageTaken' kombineret med 0 'Perfect Fights' og overlevelse tyder på manipulation af HP-værdien under kampen.

                Instruktion:
                Returner UDELUKKENDE et gyldigt JSON-objekt. 
                Du SKAL bruge præcis disse to nøgler: 'LegitimacyPercentage' (som et rent heltal mellem 0 og 100, uden %-tegn) og 'FlagReason' (som en kort tekst).
                Eksempel på forventet output: {{""LegitimacyPercentage"": 95, ""FlagReason"": ""Alt ser normalt ud.""}}

                SÆRLIGE UNDTAGELSER OG LÆRINGSDATA (Brug disse manuelle admin-afgørelser til at finjustere din vurdering):
                {dynamicExamples.ToString()}

                NYT RUN DER SKAL ANALYSERES NU:
                Encounters overlevet: {payload.encountersSurvived}
                Enemies dræbt: {payload.enemiesKilled}
                Total skade taget: {payload.totalDamageTaken}
                Skade absorberet: {payload.damageAbsorbed}
                Max skade på én tur: {payload.highestSingleTurnDamage}
                Perfect fights: {payload.perfectFights}
                Tid spillet (sekunder): {payload.timeSpentInBattle}
                ";

                var requestBody = new
                {
                    model = "mistral-small-latest",
                    messages = new[] { new { role = "user", content = prompt } },
                    response_format = new { type = "json_object" } // force json returns
                };

                string jsonString = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var client = _httpClientFactory.CreateClient(); //sends request to mistral
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanApiKey);

                var response = await client.PostAsync("https://api.mistral.ai/v1/chat/completions", content);

                if (!response.IsSuccessStatusCode) // if the api call fails, we return 0% legitimacy and the error message from mistral for easier debugging
                {
                    string mistralError = await response.Content.ReadAsStringAsync();
                    return new AiValidationResult { LegitimacyPercentage = 0, FlagReason = $"Mistral API Fejl {response.StatusCode}: {mistralError}" };
                }

                var responseString = await response.Content.ReadAsStringAsync();

                using var jsonDoc = JsonDocument.Parse(responseString);
                var aiTextResponse = jsonDoc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content").GetString();

                string cleanedResponse = aiTextResponse?.Replace("```json", "").Replace("```", "").Trim() ?? "{}";
                var result = JsonSerializer.Deserialize<AiValidationResult>(cleanedResponse);

                return result ?? new AiValidationResult { LegitimacyPercentage = 0, FlagReason = "Kunne ikke parse JSON" };
            }
            catch (Exception ex)
            {
                return new AiValidationResult { LegitimacyPercentage = 0, FlagReason = "System fejl / C# Exception: " + ex.Message };
            }
        }
    }
}