using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitEtabsValidator.Core.Enums;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.Revit
{
    /// <summary>
    /// Extracts structural framing (beams) from a Revit document into the
    /// internal BeamElement model. Unlike columns, structural framing uses
    /// LocationCurve, not LocationPoint — its endpoints already encode real
    /// 3D position (level + any start/end offsets are baked into the curve
    /// by Revit), so no separate offset-parameter lookup is needed here.
    ///
    /// VERIFY BEFORE RELYING ON THIS: section dimension parameter names
    /// ("b"/"h" or "Width"/"Depth") follow the same convention as
    /// RevitColumnReader but are NOT guaranteed to match your beam family
    /// templates — confirm against your actual families. The bounding-box
    /// fallback used for columns is deliberately NOT used here: a beam's
    /// bounding box spans its full length, so Max-Min doesn't represent
    /// cross-section width/depth the way it does for a short column stub.
    /// If both named parameters are missing, width/depth come back 0 and
    /// that element will show as a SectionMismatch — treat that as "family
    /// needs checking", not a real geometry discrepancy.
    /// </summary>
    public class RevitBeamReader
    {
        private readonly Document _doc;

        public RevitBeamReader(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public List<BeamElement> ReadBeams()
        {
            var results = new List<BeamElement>();

            var collector = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (!(element is FamilyInstance instance)) continue;

                var beam = ReadSingleBeam(instance);
                if (beam != null)
                    results.Add(beam);
            }

            return results;
        }

        private BeamElement ReadSingleBeam(FamilyInstance instance)
        {
            if (!(instance.Location is LocationCurve locationCurve))
            {
                // Non-linear (arc) beams aren't handled by this reader —
                // flag rather than silently skip.
                return null;
            }

            var startFt = locationCurve.Curve.GetEndPoint(0);
            var endFt = locationCurve.Curve.GetEndPoint(1);

            Level refLevel = _doc.GetElement(
                instance.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId()
                ?? ElementId.InvalidElementId) as Level;

            var (widthFt, depthFt) = TryGetSectionDimensions(instance);

            var startPoint = new Point3D(
                UnitConverter.FeetToMm(startFt.X),
                UnitConverter.FeetToMm(startFt.Y),
                UnitConverter.FeetToMm(startFt.Z));

            var endPoint = new Point3D(
                UnitConverter.FeetToMm(endFt.X),
                UnitConverter.FeetToMm(endFt.Y),
                UnitConverter.FeetToMm(endFt.Z));

            var beam = new BeamElement
            {
                Id = instance.Id.IntegerValue.ToString(),
                Name = instance.Symbol?.Family?.Name ?? instance.Name,
                Source = SourceApplication.Revit,
                SectionName = instance.Symbol?.Name,
                MaterialName = TryGetMaterialName(instance),
                LevelName = refLevel?.Name,

                StartPoint = startPoint,
                EndPoint = endPoint,
                Width = UnitConverter.FeetToMm(widthFt),
                Depth = UnitConverter.FeetToMm(depthFt),
                PlanRotation = AngleMath.PlanRotationDegrees(startPoint, endPoint)
            };

            beam.CenterPoint = startPoint.Midpoint(endPoint);
            beam.BoundingBox = new BoundingBox3D(startPoint, endPoint);

            return beam;
        }

        private (double width, double depth) TryGetSectionDimensions(FamilyInstance instance)
        {
            var symbol = instance.Symbol;
            if (symbol == null) return (0, 0);

            var widthParam = symbol.LookupParameter("b") ?? symbol.LookupParameter("Width");
            var depthParam = symbol.LookupParameter("h") ?? symbol.LookupParameter("Depth");

            if (widthParam != null && depthParam != null)
                return (widthParam.AsDouble(), depthParam.AsDouble());

            return (0, 0);
        }

        private string TryGetMaterialName(FamilyInstance instance)
        {
            var materialParam = instance.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
            if (materialParam == null) return null;

            var materialId = materialParam.AsElementId();
            if (materialId == null || materialId == ElementId.InvalidElementId) return null;

            return (_doc.GetElement(materialId) as Material)?.Name;
        }
    }
}
