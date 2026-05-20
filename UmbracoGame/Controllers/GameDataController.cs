using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Controllers;
using UmbracoGame.Interface;
using UmbracoGame.Models;
using UmbracoGame.Service;

namespace UmbracoGame.Controllers
{
    [ApiController]
    [Route("api/gamedata")]
    public class GameDataController : ControllerBase
    {
        private readonly IContentService _contentService;
        private readonly IAiValidationService _aiValidationService; // Vores nye interface injiceres

        public GameDataController(IContentService contentService, IAiValidationService aiValidationService)
        {
            _contentService = contentService;
            _aiValidationService = aiValidationService;
        }

        [HttpPost("submitrun")]
        public async Task<IActionResult> SubmitRun([FromBody] RunReportData payload)
        {
            if (payload == null) return BadRequest("No payload received.");

            //finds leaderboard page in Umbraco, where we will save the run data
            var leaderboardPage = _contentService.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == "leaderboardPage");

            if (leaderboardPage == null)
                return NotFound("Could not find the Leaderboard page.");

            // Call servce to handle AI-validering af data
            var aiResult = await _aiValidationService.ValidateRunAsync(payload);

            // 2. Gem data i Umbraco
            string nodeName = $"Run - {payload.playerName} - Encounters: {payload.encountersSurvived}";
            var newRun = _contentService.Create(nodeName, leaderboardPage.Id, "runRecord");

            newRun.SetValue("playerName", payload.playerName);
            newRun.SetValue("enemiesKilled", payload.enemiesKilled);
            newRun.SetValue("encountersSurvived", payload.encountersSurvived);
            newRun.SetValue("totalDamageTaken", payload.totalDamageTaken);
            newRun.SetValue("damageAbsorbed", payload.damageAbsorbed);
            newRun.SetValue("timeSpentInBattle", payload.timeSpentInBattle);
            newRun.SetValue("perfectFights", payload.perfectFights);
            newRun.SetValue("highestSingleTurnDamage", payload.highestSingleTurnDamage);

            newRun.SetValue("aiScore", aiResult.LegitimacyPercentage);
            newRun.SetValue("aiReason", aiResult.FlagReason);

            if (System.DateTime.TryParse(payload.runDate, out System.DateTime parsedDate))
            {
                newRun.SetValue("runDate", parsedDate);
            }

            // 3. Human-in-the-loop udgivelse
            if (aiResult.LegitimacyPercentage >= 80)
            {
                _contentService.Save(newRun);
                _contentService.Publish(newRun, new[] { "*" });
                return Ok(new { status = "Success", message = $"Combat record published for {payload.playerName}!" });
            }
            else
            {
                _contentService.Save(newRun);
                return Ok(new { status = "Pending", message = $"Record saved, but requires admin review for {payload.playerName}." });
            }
        }
    }
}