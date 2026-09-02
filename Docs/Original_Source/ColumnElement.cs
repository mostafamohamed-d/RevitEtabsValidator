using RevitEtabsValidator.Core.Enums;
using RevitEtabsValidator.Core.Geometry;

namespace RevitEtabsValidator.Core.Models
{
    // RECONSTRUCTED FROM USAGE (every property set in RevitColumnReader.ReadSingleColumn
    // and EtabsColumnReader.ReadSingleColumn). All lengths are mm, Rotation is degrees.
    public class ColumnElement
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public SourceApplication Source { get; set; }
        public string SectionName { get; set; }
        public string MaterialName { get; set; }
        public string LevelName { get; set; }

        public Point3D BasePoint { get; set; }
        public Point3D TopPoint { get; set; }
        public Point3D CenterPoint { get; set; }
        public BoundingBox3D BoundingBox { get; set; }

        public double BaseElevation { get; set; }
        public double TopElevation { get; set; }
        public double Width { get; set; }
        public double Depth { get; set; }

        /// <summary>Cross-section rotation about the column's own vertical
        /// axis, in degrees. Meaningful only for non-square sections.</summary>
        public double Rotation { get; set; }
    }
}
