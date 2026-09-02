namespace RevitEtabsValidator.Core.Geometry
{
    /// <summary>
    /// Circular angle comparison helpers. Plain Math.Abs(a - b) is WRONG for
    /// angles: 359 deg vs 1 deg is a 2-degree difference in reality, not 358.
    /// Every rotation/orientation comparison in this codebase must go through
    /// here, not raw subtraction.
    /// </summary>
    public static class AngleMath
    {
        /// <summary>
        /// Shortest angular distance between two angles, both expressed in
        /// degrees on a circle of the given period (default 360). Pass
        /// period=180 for undirected lines (e.g. a beam's plan angle, where
        /// 0 deg and 180 deg describe the same physical orientation).
        /// </summary>
        public static double CircularDeltaDegrees(double a, double b, double period = 360.0)
        {
            double diff = System.Math.Abs(Normalize(a, period) - Normalize(b, period));
            return System.Math.Min(diff, period - diff);
        }

        public static double Normalize(double angleDegrees, double period = 360.0)
        {
            double result = angleDegrees % period;
            if (result < 0) result += period;
            return result;
        }

        /// <summary>
        /// Angle of the Start->End line in the XY plane, normalized to
        /// [0, 180) since a line has no inherent direction. Shared by
        /// RevitBeamReader and EtabsBeamReader so neither vendor project has
        /// to reference the other — both depend only on Core, per the same
        /// separation this codebase already uses for IRevitConnector /
        /// IEtabsConnector.
        /// </summary>
        public static double PlanRotationDegrees(Point3D start, Point3D end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double angle = System.Math.Atan2(dy, dx) * 180.0 / System.Math.PI;
            return Normalize(angle, 180.0);
        }
    }
}
