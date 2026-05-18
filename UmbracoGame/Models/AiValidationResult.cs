using System;
using System.Collections.Generic;
using System.Text;

namespace UmbracoGame.Models
{
    public class AiValidationResult
    {
        public int LegitimacyPercentage { get; set; }
        public string? FlagReason { get; set; }
    }
}
