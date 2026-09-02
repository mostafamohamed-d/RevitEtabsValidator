namespace RevitEtabsValidator.Core.Validation
{
    // RECONSTRUCTED FROM USAGE (ColumnComparer: MissingInEtabs, MissingInRevit,
    // Matched, SectionMismatch, PositionMismatch, ElevationMismatch,
    // RotationMismatch). If your real enum has more members, keep them —
    // this is additive-safe since BeamComparer only uses the members below.
    public enum ValidationStatus
    {
        Matched,
        MissingInRevit,
        MissingInEtabs,
        PositionMismatch,
        ElevationMismatch,
        SectionMismatch,
        RotationMismatch
    }
}
