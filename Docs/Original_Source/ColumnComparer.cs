using System.Collections.Generic;
using System.Linq;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Core.Comparison
{
    /// <summary>
    /// Matches Revit columns against ETABS columns using story + plan
    /// position as the primary key.
    ///
    /// FIXED vs original: rotation delta used Math.Abs(a - b), which is
    /// wrong across the 0/360 boundary (359 vs 1 degree computed as 358
    /// instead of 2). Now uses AngleMath.CircularDeltaDegrees. This was
    /// latent/harmless while column Rotation was hardcoded to 0, but
    /// EtabsColumnReader now returns real rotation values, so this fix is
    /// load-bearing as of this change.
    /// </summary>
    public class ColumnComparer : IElementComparer<ColumnElement>
    {
        public List<ValidationResult> Compare(
            List<ColumnElement> revitElements,
            List<ColumnElement> etabsElements,
            ValidationTolerance tolerance)
        {
            var results = new List<ValidationResult>();
            var etabsRemaining = new List<ColumnElement>(etabsElements);

            foreach (var revitCol in revitElements)
            {
                var candidates = etabsRemaining
                    .Where(e => e.LevelName == revitCol.LevelName)
                    .ToList();

                if (candidates.Count == 0)
                    candidates = etabsRemaining;

                ColumnElement best = null;
                double bestDistance = double.MaxValue;
                foreach (var candidate in candidates)
                {
                    double distance = revitCol.CenterPoint.PlanDistanceTo(candidate.CenterPoint);
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
                        RevitElementId = revitCol.Id,
                        EtabsElementId = null,
                        ElementType = "Column",
                        StoryOrLevel = revitCol.LevelName,
                        Status = ValidationStatus.MissingInEtabs,
                        Severity = Severity.Critical,
                        Message = $"Column {revitCol.Name} exists in Revit but no ETABS column was found on any story."
                    });
                    continue;
                }

                var result = BuildResult(revitCol, best, bestDistance, tolerance);
                results.Add(result);
                etabsRemaining.Remove(best);
            }

            foreach (var extra in etabsRemaining)
            {
                results.Add(new ValidationResult
                {
                    RevitElementId = null,
                    EtabsElementId = extra.Id,
                    ElementType = "Column",
                    StoryOrLevel = extra.LevelName,
                    Status = ValidationStatus.MissingInRevit,
                    Severity = Severity.Critical,
                    Message = $"ETABS column {extra.Name} has no corresponding Revit column."
                });
            }

            return results;
        }

        private ValidationResult BuildResult(
            ColumnElement revitCol, ColumnElement etabsCol, double planDistance, ValidationTolerance tolerance)
        {
            var result = new ValidationResult
            {
                RevitElementId = revitCol.Id,
                EtabsElementId = etabsCol.Id,
                ElementType = "Column",
                StoryOrLevel = revitCol.LevelName
            };

            bool positionOk = planDistance <= tolerance.PositionToleranceMm;
            double elevationDelta = System.Math.Abs(revitCol.BaseElevation - etabsCol.BaseElevation);
            bool elevationOk = elevationDelta <= tolerance.ElevationToleranceMm;
            double widthDelta = System.Math.Abs(revitCol.Width - etabsCol.Width);
            double depthDelta = System.Math.Abs(revitCol.Depth - etabsCol.Depth);
            bool sectionOk = widthDelta <= tolerance.DimensionToleranceMm && depthDelta <= tolerance.DimensionToleranceMm;

            // Columns are symmetric under 360-degree wraparound (full circle,
            // not a line), so period stays at the default 360.
            double rotationDelta = AngleMath.CircularDeltaDegrees(revitCol.Rotation, etabsCol.Rotation);
            bool rotationOk = rotationDelta <= tolerance.AngleToleranceDegrees;

            result.Differences["PositionDelta"] = $"{planDistance:F1} mm";
            result.Differences["ElevationDelta"] = $"{elevationDelta:F1} mm";
            result.Differences["WidthDelta"] = $"{widthDelta:F1} mm";
            result.Differences["DepthDelta"] = $"{depthDelta:F1} mm";
            result.Differences["RotationDelta"] = $"{rotationDelta:F1} deg";

            if (positionOk && elevationOk && sectionOk && rotationOk)
            {
                result.Status = ValidationStatus.Matched;
                result.Severity = Severity.Info;
                result.Message = "Column matched within tolerance.";
            }
            else if (!sectionOk)
            {
                result.Status = ValidationStatus.SectionMismatch;
                result.Severity = Severity.Error;
                result.Message = $"Section differs: Revit {revitCol.Width:F0}x{revitCol.Depth:F0} vs ETABS {etabsCol.Width:F0}x{etabsCol.Depth:F0}.";
            }
            else if (!positionOk)
            {
                result.Status = ValidationStatus.PositionMismatch;
                result.Severity = Severity.Warning;
                result.Message = $"Position differs by {planDistance:F1} mm.";
            }
            else if (!elevationOk)
            {
                result.Status = ValidationStatus.ElevationMismatch;
                result.Severity = Severity.Warning;
                result.Message = $"Base elevation differs by {elevationDelta:F1} mm.";
            }
            else
            {
                result.Status = ValidationStatus.RotationMismatch;
                result.Severity = Severity.Warning;
                result.Message = $"Rotation differs by {rotationDelta:F1} degrees.";
            }

            double positionScore = ScoreWithinTolerance(planDistance, tolerance.PositionToleranceMm);
            double sectionScore = ScoreWithinTolerance(System.Math.Max(widthDelta, depthDelta), tolerance.DimensionToleranceMm);
            double elevationScore = ScoreWithinTolerance(elevationDelta, tolerance.ElevationToleranceMm);
            double rotationScore = ScoreWithinTolerance(rotationDelta, tolerance.AngleToleranceDegrees);
            result.Confidence = positionScore * 0.4 + sectionScore * 0.3 + elevationScore * 0.2 + rotationScore * 0.1;

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
