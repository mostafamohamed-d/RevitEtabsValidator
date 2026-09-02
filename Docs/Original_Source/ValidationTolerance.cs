namespace RevitEtabsValidator.Core.Validation
{
    // RECONSTRUCTED FROM USAGE (ColumnComparer reads PositionToleranceMm,
    // ElevationToleranceMm, DimensionToleranceMm, AngleToleranceDegrees).
    // The default values below are ENGINEERING GUESSES, not taken from your
    // real file — verify/replace before relying on Matched/Mismatch output.
    public class ValidationTolerance
    {
        public double PositionToleranceMm { get; set; } = 150.0;
        public double ElevationToleranceMm { get; set; } = 50.0;
        public double DimensionToleranceMm { get; set; } = 25.0;
        public double AngleToleranceDegrees { get; set; } = 2.0;
    }
}
