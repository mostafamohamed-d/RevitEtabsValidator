using System.Collections.Generic;

namespace RevitEtabsValidator.Core.Validation
{
    // RECONSTRUCTED FROM USAGE (every property set inside ColumnComparer.BuildResult
    // and the MissingInRevit/MissingInEtabs branches).
    public class ValidationResult
    {
        public string RevitElementId { get; set; }
        public string EtabsElementId { get; set; }
        public string ElementType { get; set; }
        public string StoryOrLevel { get; set; }
        public ValidationStatus Status { get; set; }
        public Severity Severity { get; set; }
        public string Message { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, string> Differences { get; set; } = new Dictionary<string, string>();
    }
}
