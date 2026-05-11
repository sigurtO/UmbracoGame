using System;
using System.Collections.Generic;
using System.Text;

namespace UmbracoGame.Models
{
    public class RunReportData
    {
        public string playerName { get; set; }
        public float timeSurvived { get; set; }
        public int finalLevel { get; set; }
        public float totalXP { get; set; }
        public string causeOfDeath { get; set; }
        public string runDate { get; set; }
    }
}
