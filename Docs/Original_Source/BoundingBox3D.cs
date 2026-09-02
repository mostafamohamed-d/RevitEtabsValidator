namespace RevitEtabsValidator.Core.Geometry
{
    // RECONSTRUCTED FROM USAGE: new BoundingBox3D(column.BasePoint, column.TopPoint)
    public class BoundingBox3D
    {
        public Point3D Min { get; }
        public Point3D Max { get; }

        public BoundingBox3D(Point3D a, Point3D b)
        {
            Min = new Point3D(
                System.Math.Min(a.X, b.X),
                System.Math.Min(a.Y, b.Y),
                System.Math.Min(a.Z, b.Z));
            Max = new Point3D(
                System.Math.Max(a.X, b.X),
                System.Math.Max(a.Y, b.Y),
                System.Math.Max(a.Z, b.Z));
        }
    }
}
