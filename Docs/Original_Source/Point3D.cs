namespace RevitEtabsValidator.Core.Geometry
{
    // RECONSTRUCTED FROM USAGE. Confirmed call sites:
    //   new Point3D(x, y, z)                — RevitColumnReader, EtabsColumnReader
    //   revitCol.CenterPoint.PlanDistanceTo(candidate.CenterPoint) — ColumnComparer
    // PlanDistanceTo is assumed to ignore Z (plan-view distance only), since
    // ColumnComparer uses it for story-matched candidates where Z is already
    // effectively pinned by the story filter. Verify against your real file —
    // if PlanDistanceTo is actually 3D, story-based candidate filtering still
    // makes it behave the same in practice, but confirm before trusting output.
    public readonly struct Point3D
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Point3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double PlanDistanceTo(Point3D other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        public double DistanceTo(Point3D other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            double dz = Z - other.Z;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public Point3D Midpoint(Point3D other) =>
            new Point3D((X + other.X) / 2.0, (Y + other.Y) / 2.0, (Z + other.Z) / 2.0);
    }
}
