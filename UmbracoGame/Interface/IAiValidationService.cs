using System;
using System.Collections.Generic;
using System.Text;
using UmbracoGame.Models;

namespace UmbracoGame.Interface
{
    public interface IAiValidationService
    {
        Task<AiValidationResult> ValidateRunAsync(RunReportData payload);
    }
}
