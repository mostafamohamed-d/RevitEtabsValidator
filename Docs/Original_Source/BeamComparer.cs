using System.Collections.Generic;
using System.Linq;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Core.Comparison
{
    /// <summary>
    /// Matches Revit beams against ETABS beams using story + midpoint plan
    /// position as the primary key, then checks section size and plan
    /// orientation — matching the requested criteria (size + position +
    /// orientation/rotation), following ColumnComparer's exact structure.
    ///
    /// Orientation uses BeamElement.PlanRotation (angle of the beam's own
    /// line, mod 180) compared with AngleMath.CircularDeltaDegrees(..., 180)
    /// rather than 360, because Revit and ETABS may store the same physical
    /// beam with Start/End (or I/J) reversed — that must read as identical
    /// orientation, not a 180-degree mismatch.
    /// </summary>
    public class BeamComparer : IElementComparer<BeamElement>
    {
        public List<ValidationResult> Compare(
            List<BeamElement> revitElements,
            List<BeamElement> etabsElements,
            ValidationTolerance tolerance)
        {
            var results = new List<ValidationResult>();
            var etabsRemaining = new List<BeamElement>(etabsElements);

            foreach (var revitBeam in revitElements)
            {
                var candidates = etabsRemaining
                    .Where(e => e.LevelName == revitBeam.LevelName)
                    .ToList();

                if (candidates.Count == 0)
                    candidates = etabsRemaining;

                BeamElement best = null;
                double bestDistance = double.MaxValue;
                foreach (var candidate in candidates)
                {
                    double distance = revitBeam.CenterPoint.PlanDistanceTo(candidate.CenterPoint);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }

                if (best == null)
                {
                    results.Add(new ValidationResult
                    {
                        RevitElementId = revitBeam.Id,
                        EtabsElementId = null,
                        ElementType = "Beam",
                        StoryOrLevel = revitBeam.LevelName,
                        Status = ValidationStatus.MissingInEtabs,
                        Severity = Severity.Critical,
                        Message = $"Beam {revitBeam.Name} exists in Revit but no ETABS beam was found on any story."
                    });
                    continue;
                }

                var result = BuildResult(revitBeam, best, bestDistance, tolerance);
                results.Add(result);
                etabsRemaining.Remove(best);
            }

            foreach (var extra in etabsRemaining)
            {
                results.Add(new ValidationResult
                {
                    RevitElementId = null,
                    EtabsElementId = extra.Id,
                    ElementType = "Beam",
                    StoryOrLevel = extra.LevelName,
                    Status = ValidationStatus.MissingInRevit,
                    Severity = Severity.Critical,
                    Message = $"ETABS beam {extra.Name} has no corresponding Revit beam."
                });
            }

            return results;
        }

        private ValidationResult BuildResult(
            BeamElement revitBeam, BeamElement etabsBeam, double planDistance, ValidationTolerance tolerance)
        {
            var result = new ValidationResult
            {
                RevitElementId = revitBeam.Id,
                EtabsElementId = etabsBeam.Id,
                ElementType = "Beam",
                StoryOrLevel = revitBeam.LevelName
            };

            bool positionOk = planDistance <= tolerance.PositionToleranceMm;

            double widthDelta = System.Math.Abs(revitBeam.Width - etabsBeam.Width);
            double depthDelta = System.Math.Abs(revitBeam.Depth - etabsBeam.Depth);
            bool sectionOk = widthDelta <= tolerance.DimensionToleranceMm && depthDelta <= tolerance.DimensionToleranceMm;

            // period=180: an undirected line, see class remarks.
            double rotationDelta = AngleMath.CircularDeltaDegrees(revitBeam.PlanRotation, etabsBeam.PlanRotation, period: 180.0);
            bool rotationOk = rotationDelta <= tolerance.AngleToleranceDegrees;

            double lengthDelta = System.Math.Abs(revitBeam.LengthMm - etabsBeam.LengthMm);
            bool lengthOk = lengthDelta <= tolerance.PositionToleranceMm;

            result.Differences["PositionDelta"] = $"{planDistance:F1} mm";
            result.Differences["WidthDelta"] = $"{widthDelta:F1} mm";
            result.Differences["DepthDelta"] = $"{depthDelta:F1} mm";
            result.Differences["RotationDelta"] = $"{rotationDelta:F1} deg";
            result.Differences["LengthDelta"] = $"{lengthDelta:F1} mm";

            if (positionOk && sectionOk && rotationOk && lengthOk)
            {
                result.Status = ValidationStatus.Matched;
                result.Severity = Severity.Info;
                result.Message = "Beam matched within tolerance.";
            }
            else if (!sectionOk)
            {
                result.Status = ValidationStatus.SectionMismatch;
                result.Severity = Severity.Error;
                result.Message = $"Section differs: Revit {revitBeam.Width:F0}x{revitBeam.Depth:F0} vs ETABS {etabsBeam.Width:F0}x{etabsBeam.Depth:F0}.";
            }
            else if (!positionOk || !lengthOk)
            {
                result.Status = ValidationStatus.PositionMismatch;
                result.Severity = Severity.Warning;
                result.Message = $"Position/length differs (midpoint {planDistance:F1} mm, length {lengthDelta:F1} mm).";
            }
            else
            {
                result.Status = ValidationStatus.RotationMismatch;
                result.Severity = Severity.Warning;
                result.Message = $"Orientation differs by {rotationDelta:F1} degrees.";
            }

            double positionScore = ScoreWithinTolerance(planDistance, tolerance.PositionToleranceMm);
            double sectionScore = ScoreWithinTolerance(System.Math.Max(widthDelta, depthDelta), tolerance.DimensionToleranceMm);
            double lengthScore = ScoreWithinTolerance(lengthDelta, tolerance.PositionToleranceMm);
            double rotationScore = ScoreWithinTolerance(rotationDelta, tolerance.AngleToleranceDegrees);
            result.Confidence = positionScore * 0.35 + sectionScore * 0.3 + lengthScore * 0.2 + rotationScore * 0.15;

            return result;
        }

        private double ScoreWithinTolerance(double delta, double tolerance)
        {
            if (tolerance <= 0) return delta == 0 ? 100 : 0;
            double ratio = delta / tolerance;
            double score = 100.0 * (1.0 - System.Math.Min(ratio, 1.0));
            return System.Math.Max(0, score);
        }
    }
}
