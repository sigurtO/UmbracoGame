using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Controllers;
using System.Text.Json;
using Microsoft.Extensions.Configuration; // user secrets
using System.Net.Http;
using System.Net.Http.Headers;
using UmbracoGame.Models;

namespace UmbracoGame.Controllers
{
    [ApiController]
    [Route("api/gamedata")]
    public class GameDataController : ControllerBase
    {
        private readonly IContentService _contentService;
        private readonly IHttpClientFactory _httpClientFactory; // call API for validation
        private readonly IConfiguration _configuration;

        private readonly string _mistralApiKey;

        public GameDataController(IContentService contentService, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _contentService = contentService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _mistralApiKey = _configuration["MistralAi:ApiKey"];
        }

        [HttpPost("submitrun")]
        public async Task<IActionResult> SubmitRun([FromBody] RunReportData payload)
        {
            if (payload == null) return BadRequest("No payload received.");

            var leaderboardPage = _contentService.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == "leaderboardPage");

            if (leaderboardPage == null)
                return NotFound("Could not find the Leaderboard page.");

            var aiResult = await ValidateWithMistralAI(payload); // check with AI is the result legit

            // Updated node naming to reflect the new metrics
            string nodeName = $"Run - {payload.playerName} - Encounters: {payload.encountersSurvived}";
            var newRun = _contentService.Create(nodeName, leaderboardPage.Id, "runRecord");

            // Mapping the new properties
            newRun.SetValue("playerName", payload.playerName);
            newRun.SetValue("enemiesKilled", payload.enemiesKilled);
            newRun.SetValue("encountersSurvived", payload.encountersSurvived);
            newRun.SetValue("totalDamageTaken", payload.totalDamageTaken);
            newRun.SetValue("damageAbsorbed", payload.damageAbsorbed);
            newRun.SetValue("timeSpentInBattle", payload.timeSpentInBattle);
            newRun.SetValue("perfectFights", payload.perfectFights);
            newRun.SetValue("highestSingleTurnDamage", payload.highestSingleTurnDamage);

            newRun.SetValue("AiScore", aiResult.LegitimacyPercentage);
            newRun.SetValue("aiReason", aiResult.FlagReason);

            if (System.DateTime.TryParse(payload.runDate, out System.DateTime parsedDate))
            {
                newRun.SetValue("runDate", parsedDate);
            }

            //human in the loop
            if (aiResult.LegitimacyPercentage >= 80)
            {
                // Hvis den er fin, udgiv den til hjemmesiden med det samme
                _contentService.Save(newRun);
                _contentService.Publish(newRun, new[] { "*" });
                return Ok(new { status = "Success", message = $"Combat record published for {payload.playerName}!" });
            }
            else
            {
                // Hvis mistænkelig (eller hvis API'et var nede), gem den KUN som upubliceret kladde
                _contentService.Save(newRun);
                return Ok(new { status = "Pending", message = $"Record saved, but requires admin review for {payload.playerName}." });
            }

            //_contentService.Save(newRun);
            //_contentService.Publish(newRun, new[] { "*" }); //old save and publish without AI check
            //return Ok(new { status = "Success", message = $"Combat record saved for {payload.playerName}!" });
        }

        // --- AI LOGIKKEN (DE 5 P'ER) ---
        private async Task<AiValidationResult> ValidateWithMistralAI(RunReportData payload)
        {
            string cleanApiKey = _mistralApiKey?.Trim();
            if (string.IsNullOrEmpty(cleanApiKey))
            {
                return new AiValidationResult { LegitimacyPercentage = 0, FlagReason = "LOKAL FEJL: Mistral API-nøgle mangler." };
            }

            // --- 1. DYNAMISK HENTNING AF CURATED EDGE-CASES (Læringsdata) ---
            var leaderboardPage = _contentService.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == "leaderboardPage");
            var allSavedRuns = _contentService.GetPagedChildren(leaderboardPage.Id, 0, 100, out long totalRecords);

            // Vi henter KUN de runs, hvor admin aktivt har vinket af, at dataen skal sendes med som forbedring
            var aiImprovementRuns = allSavedRuns
                .Where(x => x.GetValue<bool>("sendToAiForImprovements") == true)
                .Take(10); // max 10 for at holde prompten inden for grænserne

            StringBuilder dynamicExamples = new StringBuilder();
            foreach (var historicalRun in aiImprovementRuns)
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

            try
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

                NYT RUN DER SKAL ANALYSERES NU (Hold dette op mod både de fundamentale regler og læringsdataen):
                Encounters overlevet: {payload.encountersSurvived}
                Enemies dræbt: {payload.enemiesKilled}
                Total skade taget: {payload.totalDamageTaken}
                Skade absorberet: {payload.damageAbsorbed}
                Max skade på én tur: {payload.highestSingleTurnDamage}
                Perfect fights: {payload.perfectFights}
                Tid spillet (sekunder): {payload.timeSpentInBattle}
                ";

                // Mistrals specifikke JSON struktur
                var requestBody = new
                {
                    model = "mistral-small-latest", // Mistrals hurtigste og billigste model
                    messages = new[]
                    {
                new { role = "user", content = prompt }
            },
                    response_format = new { type = "json_object" } // Dette tvinger Mistral til altid at svare i perfekt JSON!
                };

                string jsonString = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json");

                var client = _httpClientFactory.CreateClient();

                // Mistral bruger Bearer godkendelse i stedet for en API-nøgle direkte i URL'en
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanApiKey);

                // Mistrals Endpoint URL
                var url = "https://api.mistral.ai/v1/chat/completions";

                var response = await client.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string mistralError = await response.Content.ReadAsStringAsync();
                    return new AiValidationResult { LegitimacyPercentage = 0, FlagReason = $"Mistral API Fejl {response.StatusCode}: {mistralError}" };
                }

                var responseString = await response.Content.ReadAsStringAsync();

                // Mistrals svar-struktur er anderledes at pakke ud end Googles
                using var jsonDoc = JsonDocument.Parse(responseString);
                var aiTextResponse = jsonDoc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content").GetString();

                // Renser og konverterer til C# objekt
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