using RevitEtabsValidator.Core.Enums;
using RevitEtabsValidator.Core.Geometry;

namespace RevitEtabsValidator.Core.Models
{
    /// <summary>
    /// Internal representation of a beam/frame element, mirroring
    /// ColumnElement's conventions: all lengths in mm, all angles in degrees.
    ///
    /// PlanRotation is the angle of the StartPoint-to-EndPoint line in the
    /// XY plane, normalized to [0, 180). It's normalized to 180 (not 360)
    /// because a beam line has no inherent direction — Revit and ETABS may
    /// store the same physical beam with I/J or Start/End swapped, and that
    /// must not register as a 180-degree "mismatch". Compare it with
    /// AngleMath.CircularDeltaDegrees(a, b, period: 180).
    /// </summary>
    public class BeamElement
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public SourceApplication Source { get; set; }
        public string SectionName { get; set; }
        public string MaterialName { get; set; }
        public string LevelName { get; set; }

        public Point3D StartPoint { get; set; }
        public Point3D EndPoint { get; set; }
        public Point3D CenterPoint { get; set; }
        public BoundingBox3D BoundingBox { get; set; }

        public double Width { get; set; }
        public double Depth { get; set; }
        public double PlanRotation { get; set; }

        public double LengthMm =>
            StartPoint.DistanceTo(EndPoint);
    }
}
