using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;
using RevitEtabsValidator.Core.Geometry;
namespace RevitEtabsValidator.Core.Comparison;

public sealed class ModelComparer
{
    public ValidationReport CompareColumns(IReadOnlyList<ColumnElement> revit, IReadOnlyList<ColumnElement> etabs, ValidationTolerance tol)
        => Compare(revit, etabs, tol, "Column", ColumnScore, ColumnResult);
    public ValidationReport CompareBeams(IReadOnlyList<BeamElement> revit, IReadOnlyList<BeamElement> etabs, ValidationTolerance tol)
        => Compare(revit, etabs, tol, "Beam", BeamScore, BeamResult);

    private delegate double ScoreDelegate<T>(T r, T e, ValidationTolerance t) where T:ElementBase;
    private delegate ValidationResult ResultDelegate<T>(T r, T e, double position, ValidationTolerance t) where T:ElementBase;

    private ValidationReport Compare<T>(IReadOnlyList<T> revit, IReadOnlyList<T> etabs, ValidationTolerance tol, string type,
        ScoreDelegate<T> score, ResultDelegate<T> make) where T:ElementBase
    {
        var report = new ValidationReport();
        var remaining = new HashSet<T>(etabs);
        foreach (var r in revit)
        {
            var candidates = remaining.Where(e => string.Equals(e.LevelName, r.LevelName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0) candidates = remaining.ToList();
            if (candidates.Count == 0)
            {
                report.Results.Add(new ValidationResult { RevitElementId=r.Id, RevitName=r.Name, ElementType=type, StoryOrLevel=r.LevelName, Status=ValidationStatus.MissingInEtabs, Severity=Severity.Critical, Confidence=0, Message=$"{type} exists in Revit but no ETABS counterpart was found."});
                continue;
            }
            var ranked = candidates.Select(e => (e, s: score(r,e,tol))).OrderBy(x=>x.s).ToList();
            var best = ranked[0];
            if (ranked.Count > 1 && Math.Abs(ranked[1].s-best.s) < tol.AmbiguousScoreGap)
            {
                report.Results.Add(new ValidationResult { RevitElementId=r.Id, RevitName=r.Name, EtabsElementId=best.e.Id, EtabsName=best.e.Name, ElementType=type, StoryOrLevel=r.LevelName, Status=ValidationStatus.AmbiguousMatch, Severity=Severity.Error, Confidence=0, Message=$"Ambiguous {type} match. Best candidates are too close in matching score."});
                remaining.Remove(best.e);
                continue;
            }
            report.Results.Add(make(r,best.e,best.s,tol));
            remaining.Remove(best.e);
        }
        foreach (var e in remaining)
            report.Results.Add(new ValidationResult { EtabsElementId=e.Id, EtabsName=e.Name, ElementType=type, StoryOrLevel=e.LevelName, Status=ValidationStatus.MissingInRevit, Severity=Severity.Critical, Confidence=0, Message=$"ETABS {type} exists with no corresponding Revit element."});
        return report;
    }

    private static double ColumnScore(ColumnElement r, ColumnElement e, ValidationTolerance t)
    {
        var pos=r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        var elev=Math.Abs(r.BaseElevation-e.BaseElevation)+Math.Abs(r.TopElevation-e.TopElevation);
        var sec=Math.Abs(r.Width-e.Width)+Math.Abs(r.Depth-e.Depth);
        var rot=AngleMath.CircularDeltaDegrees(r.Rotation,e.Rotation,180);
        return pos + elev*0.5 + sec*0.5 + rot*t.DimensionToleranceMm;
    }
    private static double BeamScore(BeamElement r, BeamElement e, ValidationTolerance t)
    {
        var pos=r.CenterPoint.PlanDistanceTo(e.CenterPoint);
        var elev=Math.Abs(r.StartPoint.Z-e.StartPoint.Z)+Math.Abs(r.EndPoint.Z-e.EndPoint.Z);
        var sec=Math.Abs(r.Width-e.Width)+Math.Abs(r.Depth-e.Depth);
        var len=Math.Abs(r.LengthMm-e.LengthMm);
        var rot=AngleMath.CircularDeltaDegrees(r.Rotation,e.Rotation,180);
        return pos + elev*0.5 + sec*0.5 + len*0.25 + rot*t.DimensionToleranceMm;
    }
    private static ValidationResult ColumnResult(ColumnElement r, ColumnElement e,double _,ValidationTolerance t)
    {
        var p=r.CenterPoint.PlanDistanceTo(e.CenterPoint); var eb=Math.Abs(r.BaseElevation-e.BaseElevation); var et=Math.Abs(r.TopElevation-e.TopElevation);
        var wd=Math.Abs(r.Width-e.Width); var dd=Math.Abs(r.Depth-e.Depth); var rot=AngleMath.CircularDeltaDegrees(r.Rotation,e.Rotation,180);
        var okP=p<=t.PositionToleranceMm; var okE=eb<=t.ElevationToleranceMm&&et<=t.ElevationToleranceMm; var okS=wd<=t.DimensionToleranceMm&&dd<=t.DimensionToleranceMm; var okR=rot<=t.AngleToleranceDegrees;
        var res=Base(r,e,"Column"); res.PositionDeltaMm=p; res.ElevationDeltaMm=Math.Max(eb,et); res.WidthDeltaMm=wd; res.DepthDeltaMm=dd; res.RotationDeltaDeg=rot;
        res.Status=okP&&okE&&okS&&okR?ValidationStatus.Matched:!okS?ValidationStatus.SectionMismatch:!okP?ValidationStatus.PositionMismatch:!okE?ValidationStatus.ElevationMismatch:ValidationStatus.RotationMismatch;
        res.Severity=res.Status==ValidationStatus.Matched?Severity.Info:res.Status==ValidationStatus.SectionMismatch?Severity.Error:Severity.Warning;
        res.Confidence=Confidence(new[]{p/t.PositionToleranceMm, Math.Max(eb,et)/t.ElevationToleranceMm, Math.Max(wd,dd)/t.DimensionToleranceMm, rot/t.AngleToleranceDegrees});
        res.Message=res.Status==ValidationStatus.Matched?"Column matches within configured tolerances.":$"{res.Status}: Revit {r.Width:F0}x{r.Depth:F0}, ETABS {e.Width:F0}x{e.Depth:F0}; Δpos {p:F1} mm; Δrot {rot:F1}°.";
        AddDiffs(res); return res;
    }
    private static ValidationResult BeamResult(BeamElement r, BeamElement e,double _,ValidationTolerance t)
    {
        var p=r.CenterPoint.PlanDistanceTo(e.CenterPoint); var elev=Math.Max(Math.Abs(r.StartPoint.Z-e.StartPoint.Z),Math.Abs(r.EndPoint.Z-e.EndPoint.Z));
        var wd=Math.Abs(r.Width-e.Width); var dd=Math.Abs(r.Depth-e.Depth); var len=Math.Abs(r.LengthMm-e.LengthMm); var rot=AngleMath.CircularDeltaDegrees(r.Rotation,e.Rotation,180);
        var okP=p<=t.PositionToleranceMm; var okE=elev<=t.ElevationToleranceMm; var okS=wd<=t.DimensionToleranceMm&&dd<=t.DimensionToleranceMm; var okL=len<=t.LengthToleranceMm; var okR=rot<=t.AngleToleranceDegrees;
        var res=Base(r,e,"Beam"); res.PositionDeltaMm=p; res.ElevationDeltaMm=elev; res.WidthDeltaMm=wd; res.DepthDeltaMm=dd; res.LengthDeltaMm=len; res.RotationDeltaDeg=rot;
        res.Status=okP&&okE&&okS&&okL&&okR?ValidationStatus.Matched:!okS?ValidationStatus.SectionMismatch:!okP||!okL?ValidationStatus.PositionMismatch:!okE?ValidationStatus.ElevationMismatch:ValidationStatus.RotationMismatch;
        res.Severity=res.Status==ValidationStatus.Matched?Severity.Info:res.Status==ValidationStatus.SectionMismatch?Severity.Error:Severity.Warning;
        res.Confidence=Confidence(new[]{p/t.PositionToleranceMm,elev/t.ElevationToleranceMm,Math.Max(wd,dd)/t.DimensionToleranceMm,len/t.LengthToleranceMm,rot/t.AngleToleranceDegrees});
        res.Message=res.Status==ValidationStatus.Matched?"Beam matches within configured tolerances.":$"{res.Status}: Revit {r.Width:F0}x{r.Depth:F0}, ETABS {e.Width:F0}x{e.Depth:F0}; Δpos {p:F1} mm; ΔL {len:F1} mm.";
        AddDiffs(res); return res;
    }
    private static ValidationResult Base(ElementBase r,ElementBase e,string type)=>new(){RevitElementId=r.Id,EtabsElementId=e.Id,RevitName=r.Name,EtabsName=e.Name,ElementType=type,StoryOrLevel=string.IsNullOrWhiteSpace(r.LevelName)?e.LevelName:r.LevelName};
    private static double Confidence(IEnumerable<double> ratios)=>Math.Max(0,Math.Min(100,100*(1-ratios.Select(x=>Math.Min(1,Math.Abs(x))).Average())));
    private static void AddDiffs(ValidationResult r){r.Differences["Position"]=$"{r.PositionDeltaMm:F1} mm";r.Differences["Elevation"]=$"{r.ElevationDeltaMm:F1} mm";r.Differences["Width"]=$"{r.WidthDeltaMm:F1} mm";r.Differences["Depth"]=$"{r.DepthDeltaMm:F1} mm";r.Differences["Length"]=$"{r.LengthDeltaMm:F1} mm";r.Differences["Rotation"]=$"{r.RotationDeltaDeg:F1} deg";}
}
