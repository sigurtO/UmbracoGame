using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Controllers;
using System.Linq;
using UmbracoGame.Models; // Match your models namespace

namespace UmbracoGame.Controllers
{
    [ApiController]
    [Route("api/gamedata")]
    public class GameDataController : ControllerBase
    {
        private readonly IContentService _contentService;

        public GameDataController(IContentService contentService)
        {
            _contentService = contentService;
        }

        [HttpPost("submitrun")]
        public IActionResult SubmitRun([FromBody] RunReportData payload)
        {
            if (payload == null) return BadRequest("No payload received.");

            var leaderboardPage = _contentService.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == "leaderboardPage");

            if (leaderboardPage == null)
                return NotFound("Could not find the Leaderboard page.");

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

            if (System.DateTime.TryParse(payload.runDate, out System.DateTime parsedDate))
            {
                newRun.SetValue("runDate", parsedDate);
            }

            _contentService.Save(newRun);
            _contentService.Publish(newRun, new[] { "*" });

            return Ok(new { status = "Success", message = $"Combat record saved for {payload.playerName}!" });
        }
    }
}
