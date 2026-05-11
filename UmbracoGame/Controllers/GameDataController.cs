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

        // 3. We explicitly name the route for this specific action
        [HttpPost("submitrun")]
        public IActionResult SubmitRun([FromBody] RunReportData payload)
        {
            if (payload == null) return BadRequest("No payload received.");

            var leaderboardPage = _contentService.GetRootContent().FirstOrDefault(x => x.ContentType.Alias == "leaderboardPage");

            if (leaderboardPage == null)
                return NotFound("Could not find the Leaderboard page in Umbraco to attach the score to.");

            string nodeName = $"Run - {payload.playerName} - Level {payload.finalLevel}";
            var newRun = _contentService.Create(nodeName, leaderboardPage.Id, "runRecord");

            newRun.SetValue("playerName", payload.playerName);
            newRun.SetValue("timeSurvived", payload.timeSurvived);
            newRun.SetValue("finalLevel", payload.finalLevel);
            newRun.SetValue("totalXP", payload.totalXP);
            newRun.SetValue("causeOfDeath", payload.causeOfDeath);

            if (System.DateTime.TryParse(payload.runDate, out System.DateTime parsedDate))
            {
                newRun.SetValue("runDate", parsedDate);
            }

            _contentService.Save(newRun);

            _contentService.Publish(newRun, new[] { "*" });

            return Ok(new { status = "Success", message = $"Run saved for {payload.playerName}!" });
        }
    }
}
