using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitEtabsValidator.Core.Enums;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.Revit
{
    /// <summary>
    /// Extracts structural columns from a Revit document into the internal
    /// ColumnElement model. Revit's internal length unit is always decimal
    /// feet regardless of project display units — every dimension read here
    /// goes through UnitConverter.FeetToMm before leaving this class.
    ///
    /// FIXED vs original: "Base Level"/"Top Level"/"Base Offset"/"Top Offset"
    /// were being read via LookupParameter(displayName), which matches on the
    /// UI display string. That breaks silently on any non-English Revit UI
    /// (e.g. Arabic) or a renamed parameter — LookupParameter just returns
    /// null, baseElevationFt defaults to 0, and every column reports a false
    /// elevation mismatch with no error. Switched to get_Parameter(BuiltInParameter)
    /// which resolves by internal ID and is language-independent.
    ///
    /// VERIFY BEFORE RELYING ON THIS: column families are not standardized
    /// across offices/templates. This reader uses the "b"/"h" (or "Width"/
    /// "Depth") type parameters as a best-effort default for section
    /// dimensions, falling back to the bounding box if neither is found.
    /// Confirm your project's actual family parameter names before trusting
    /// downstream section comparisons.
    /// </summary>
    public class RevitColumnReader
    {
        private readonly Document _doc;

        public RevitColumnReader(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public List<ColumnElement> ReadColumns()
        {
            var results = new List<ColumnElement>();

            var collector = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (!(element is FamilyInstance instance)) continue;

                var column = ReadSingleColumn(instance);
                if (column != null)
                    results.Add(column);
            }

            return results;
        }

        private ColumnElement ReadSingleColumn(FamilyInstance instance)
        {
            if (!(instance.Location is LocationPoint locationPoint))
            {
                // Slanted/sketch-based columns may use LocationCurve instead —
                // not handled in this Phase 2 pass; flag rather than silently skip.
                return null;
            }

            Level baseLevel = GetLevelParam(instance, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
            Level topLevel = GetLevelParam(instance, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);

            double baseOffsetFt = GetDoubleParam(instance, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
            double topOffsetFt = GetDoubleParam(instance, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);

            double baseElevationFt = (baseLevel?.Elevation ?? 0.0) + baseOffsetFt;
            double topElevationFt = (topLevel?.Elevation ?? 0.0) + topOffsetFt;

            var origin = locationPoint.Point; // feet
            double rotationRadians = locationPoint.Rotation;

            var (widthFt, depthFt) = TryGetSectionDimensions(instance);

            var column = new ColumnElement
            {
                Id = instance.Id.IntegerValue.ToString(),
                Name = instance.Symbol?.Family?.Name ?? instance.Name,
                Source = SourceApplication.Revit,
                SectionName = instance.Symbol?.Name,
                MaterialName = TryGetMaterialName(instance),
                LevelName = baseLevel?.Name,

                BasePoint = new Point3D(
                    UnitConverter.FeetToMm(origin.X),
                    UnitConverter.FeetToMm(origin.Y),
                    UnitConverter.FeetToMm(baseElevationFt)),

                TopPoint = new Point3D(
                    UnitConverter.FeetToMm(origin.X),
                    UnitConverter.FeetToMm(origin.Y),
                    UnitConverter.FeetToMm(topElevationFt)),

                BaseElevation = UnitConverter.FeetToMm(baseElevationFt),
                TopElevation = UnitConverter.FeetToMm(topElevationFt),

                Width = UnitConverter.FeetToMm(widthFt),
                Depth = UnitConverter.FeetToMm(depthFt),
                Rotation = rotationRadians * 180.0 / Math.PI
            };

            column.CenterPoint = column.BasePoint; // plan position identical top/bottom for a straight column
            column.BoundingBox = new BoundingBox3D(column.BasePoint, column.TopPoint);

            return column;
        }

        private Level GetLevelParam(FamilyInstance instance, BuiltInParameter bip)
        {
            var param = instance.get_Parameter(bip);
            if (param == null || param.AsElementId() == ElementId.InvalidElementId) return null;
            return _doc.GetElement(param.AsElementId()) as Level;
        }

        private double GetDoubleParam(FamilyInstance instance, BuiltInParameter bip)
        {
            var param = instance.get_Parameter(bip);
            return param?.AsDouble() ?? 0.0;
        }

        private (double width, double depth) TryGetSectionDimensions(FamilyInstance instance)
        {
            var symbol = instance.Symbol;
            if (symbol == null) return (0, 0);

            // Common Revit structural-column family parameter names — verify
            // against your actual family templates, these vary by office/template.
            var widthParam = symbol.LookupParameter("b") ?? symbol.LookupParameter("Width");
            var depthParam = symbol.LookupParameter("h") ?? symbol.LookupParameter("Depth");

            if (widthParam != null && depthParam != null)
                return (widthParam.AsDouble(), depthParam.AsDouble());

            // Fall back to the instance bounding box if named parameters aren't found.
            var bbox = instance.get_BoundingBox(null);
            if (bbox != null)
            {
                double w = bbox.Max.X - bbox.Min.X;
                double d = bbox.Max.Y - bbox.Min.Y;
                return (w, d);
            }

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
