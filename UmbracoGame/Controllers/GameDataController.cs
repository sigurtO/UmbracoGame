using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Controllers;
using System.Text.Json;
using Microsoft.Extensions.Configuration; // user secrets
using UmbracoGame.Models;

namespace UmbracoGame.Controllers
{
    [ApiController]
    [Route("api/gamedata")]
    public class GameDataController : ControllerBase
    {
        private readonly IContentService _contentService;
        private readonly HttpClient _httpClient; // call API for validation

        private readonly string _googleApiKey;

        public GameDataController(IContentService contentService, HttpClient httpClient)
        {
            _contentService = contentService;
            _httpClient = httpClient;

        }

        [HttpPost("submitrun")]
        public async Task<IActionResult> SubmitRun([FromBody] RunReportData payload)
        {
            if (payload == null) return BadRequest("No payload received.");

            var leaderboardPage = _contentService.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == "leaderboardPage");

            if (leaderboardPage == null)
                return NotFound("Could not find the Leaderboard page.");

            var aiResult = await ValidateWithGoogleAI(payload); // check with AI is the result legit

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
        private async Task<AiValidationResult> ValidateWithGoogleAI(RunReportData payload)
        {
            try
            {
                // De 5 P'er (Lektion 02) indbygget direkte i koden!
                string prompt = $@"
                Persona: Du er en erfaren anti-cheat sikkerhedsanalytiker for et turbaseret kortspil.
                Problem: Vurder om indsendte data fra et spil-run er legitime eller manipuleret med memory-editing værktøjer.
                Plan: Analysér forholdet mellem encountersSurvived, timeSpentInBattle og highestSingleTurnDamage.
                Product: Returner UDELUKKENDE et gyldigt JSON-objekt med nøglerne 'LegitimacyPercentage' (0-100) og 'FlagReason' (kort tekst).

                Parameters:
                - En gennemsnitlig kamp bør tage mindst 5-10 sekunder.
                - Hvis highestSingleTurnDamage er over 9999, er det sandsynligvis manipuleret.
                - Svar KUN med JSON, ingen formatering, ingen markdown tags.

                EKSEMPLER PÅ TIDLIGERE RUNS (Few-Shot Data):
                [Eksempel 1 - Legitimt]: Tid: 45s, Encounters: 5, MaxDmg: 120, TotalDmgTaken: 40. (Dette er et normalt, menneskeligt run).
                [Eksempel 2 - Legitimt Speedrun]: Tid: 28s, Encounters: 5, MaxDmg: 250, TotalDmgTaken: 10. (Dette er en meget dygtig spiller, høj skade, lav tid - men stadig matematisk muligt).
                [Eksempel 3 - Manipuleret/Snyd]: Tid: 2s, Encounters: 10, MaxDmg: 999999, TotalDmgTaken: 0. (Cheat engine brugt til at ændre damage-variablen og fryse tiden).
                [Eksempel 4 - Manipuleret/Snyd]: Tid: 15s, Encounters: 50, MaxDmg: 100, TotalDmgTaken: 0. (For mange encounters på for kort tid, umuligt scenarie).

                DATA TIL ANALYSE (Dette er det NYE run, du skal vurdere ud fra ovenstående baseline):
                Tid spillet (sekunder): {payload.timeSpentInBattle}
                Encounters overlevet: {payload.encountersSurvived}
                Max skade på én tur: {payload.highestSingleTurnDamage}
                Total skade taget: {payload.totalDamageTaken}
                ";

                // Formater request til Googles Gemini API format
                var requestBody = new
                {
                    contents = new[] {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var url = $"[https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=](https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=){_googleApiKey}";

                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();

                // Trækker selve teksten ud af Googles ret komplekse JSON-struktur
                using var jsonDoc = JsonDocument.Parse(responseString);
                var aiTextResponse = jsonDoc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString();

                // Konverter tekstsvaret fra AI til vores C# model
                return JsonSerializer.Deserialize<AiValidationResult>(aiTextResponse.Trim());
            }
            catch (Exception ex)
            {
                // Availability/Tilgængelighed (Lektion 1)
                // Hvis Google er nede, returnerer vi en sikkerheds-score på 0 (Pending), 
                // så dataen ikke går tabt, men ej heller bliver auto-publiceret.
                return new AiValidationResult
                {
                    LegitimacyPercentage = 0,
                    FlagReason = "System fejl / AI utilgængelig: " + ex.Message
                };
            }
        }
    }

}
