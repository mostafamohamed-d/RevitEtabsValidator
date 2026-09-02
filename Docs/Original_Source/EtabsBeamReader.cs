using System;
using System.Collections.Generic;
using CSiAPIv1;
using RevitEtabsValidator.Core.Enums;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.Etabs
{
    /// <summary>
    /// Extracts beam frame objects from an ETABS model into the internal
    /// BeamElement model. Mirrors EtabsColumnReader's structure, filtered to
    /// eFrameDesignOrientation.Beam instead of Column.
    ///
    /// Orientation (BeamElement.PlanRotation) is derived from the I/J point
    /// geometry itself, mod 180 — NOT from FrameObj.GetLocalAxes. This is
    /// deliberate: I and J may be swapped relative to Revit's Start/End for
    /// the same physical beam, and a line-angle-mod-180 comparison sidesteps
    /// that ambiguity entirely, unlike a raw local-axis roll angle would.
    ///
    /// VERIFY BEFORE RELYING ON THIS: same CSiAPIv1 version caveats as
    /// EtabsColumnReader — confirm signatures against your installed version.
    /// </summary>
    public class EtabsBeamReader
    {
        private readonly cSapModel _sapModel;
        private const double MaxStoryMatchDeltaMm = 500.0;

        public EtabsBeamReader(cSapModel sapModel)
        {
            _sapModel = sapModel ?? throw new ArgumentNullException(nameof(sapModel));
        }

        public List<BeamElement> ReadBeams()
        {
            var results = new List<BeamElement>();

            int numberNames = 0;
            string[] frameNames = null;
            int ret = _sapModel.FrameObj.GetNameList(ref numberNames, ref frameNames);
            if (ret != 0 || frameNames == null) return results;

            eUnits currentUnits = _sapModel.GetPresentUnits();
            string lengthUnitName = MapUnitsToLengthName(currentUnits);

            foreach (var name in frameNames)
            {
                eFrameDesignOrientation orientation = eFrameDesignOrientation.Other;
                int orientationRet = _sapModel.FrameObj.GetDesignOrientation(name, ref orientation);
                if (orientationRet != 0 || orientation != eFrameDesignOrientation.Beam)
                    continue; // not a beam — skip (columns handled by EtabsColumnReader)

                var beam = ReadSingleBeam(name, lengthUnitName);
                if (beam != null)
                    results.Add(beam);
            }

            return results;
        }

        private BeamElement ReadSingleBeam(string frameName, string lengthUnitName)
        {
            string pointI = "", pointJ = "";
            int pointRet = _sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
            if (pointRet != 0) return null;

            double xi = 0, yi = 0, zi = 0, xj = 0, yj = 0, zj = 0;
            _sapModel.PointObj.GetCoordCartesian(pointI, ref xi, ref yi, ref zi);
            _sapModel.PointObj.GetCoordCartesian(pointJ, ref xj, ref yj, ref zj);

            string sectionName = "", sAuto = "";
            _sapModel.FrameObj.GetSection(frameName, ref sectionName, ref sAuto);

            double widthRaw = 0, depthRaw = 0;
            TryGetRectangularDimensions(sectionName, ref widthRaw, ref depthRaw);

            var startPoint = new Point3D(
                UnitConverter.EtabsLengthToMm(xi, lengthUnitName),
                UnitConverter.EtabsLengthToMm(yi, lengthUnitName),
                UnitConverter.EtabsLengthToMm(zi, lengthUnitName));

            var endPoint = new Point3D(
                UnitConverter.EtabsLengthToMm(xj, lengthUnitName),
                UnitConverter.EtabsLengthToMm(yj, lengthUnitName),
                UnitConverter.EtabsLengthToMm(zj, lengthUnitName));

            string storyName = TryGetStoryForElevation(zi, lengthUnitName);

            var beam = new BeamElement
            {
                Id = frameName,
                Name = frameName,
                Source = SourceApplication.Etabs,
                SectionName = sectionName,
                LevelName = storyName,

                StartPoint = startPoint,
                EndPoint = endPoint,
                Width = UnitConverter.EtabsLengthToMm(widthRaw, lengthUnitName),
                Depth = UnitConverter.EtabsLengthToMm(depthRaw, lengthUnitName),
                PlanRotation = AngleMath.PlanRotationDegrees(startPoint, endPoint)
            };

            beam.CenterPoint = startPoint.Midpoint(endPoint);
            beam.BoundingBox = new BoundingBox3D(startPoint, endPoint);

            return beam;
        }

        private void TryGetRectangularDimensions(string sectionName, ref double width, ref double depth)
        {
            string fileName = "", matProp = "";
            double t3 = 0, t2 = 0, color = 0;
            string notes = "", guid = "";
            int ret = _sapModel.PropFrame.GetRectangle(
                sectionName, ref fileName, ref matProp, ref t3, ref t2, ref color, ref notes, ref guid);

            if (ret == 0)
            {
                depth = t3;
                width = t2;
            }
        }

        private string TryGetStoryForElevation(double elevationRaw, string lengthUnitName)
        {
            int numberNames = 0;
            string[] storyNames = null;
            double[] storyElevations = null;
            double[] storyHeights = null;
            bool[] isMasterStory = null;
            string[] similarToStory = null;
            bool[] spliceAbove = null;
            double[] spliceHeight = null;
            int[] color = null;

            int ret = _sapModel.Story.GetStories_2(
                ref numberNames, ref storyNames, ref storyElevations, ref storyHeights,
                ref isMasterStory, ref similarToStory, ref spliceAbove, ref spliceHeight, ref color);

            if (ret != 0 || storyNames == null) return null;

            string closest = null;
            double smallestDeltaRaw = double.MaxValue;
            for (int i = 0; i < storyNames.Length; i++)
            {
                double delta = Math.Abs(storyElevations[i] - elevationRaw);
                if (delta < smallestDeltaRaw)
                {
                    smallestDeltaRaw = delta;
                    closest = storyNames[i];
                }
            }

            double smallestDeltaMm = UnitConverter.EtabsLengthToMm(smallestDeltaRaw, lengthUnitName);
            if (smallestDeltaMm > MaxStoryMatchDeltaMm) return null;

            return closest;
        }

        private string MapUnitsToLengthName(eUnits units)
        {
            switch (units)
            {
                case eUnits.kN_mm_C:
                case eUnits.N_mm_C:
                case eUnits.kgf_mm_C:
                    return "mm";
                case eUnits.kN_m_C:
                case eUnits.N_m_C:
                case eUnits.kgf_m_C:
                case eUnits.Ton_m_C:
                    return "m";
                case eUnits.kip_in_F:
                case eUnits.lb_in_F:
                    return "in";
                case eUnits.kip_ft_F:
                case eUnits.lb_ft_F:
                    return "ft";
                default:
                    throw new NotSupportedException(
                        $"Unhandled eUnits value '{units}'. Verify against CSiAPIv1 and extend this mapping.");
            }
        }
    }
}
