using System;
using System.Collections.Generic;
using System.Text;

namespace UmbracoGame.Models
{
    public class RunReportData
    {
        // Core Leaderboard Info
        public string playerName { get; set; }
        public string runDate { get; set; }

        // New Combat Metrics
        public int enemiesKilled { get; set; }
        public int encountersSurvived { get; set; }
        public int totalDamageTaken { get; set; }
        public int damageAbsorbed { get; set; }
        public float timeSpentInBattle { get; set; }
        public int perfectFights { get; set; }
        public int highestSingleTurnDamage { get; set; }
    }
}
