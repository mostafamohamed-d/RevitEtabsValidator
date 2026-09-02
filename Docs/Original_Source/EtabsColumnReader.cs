using System;
using System.Collections.Generic;
using CSiAPIv1;
using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.Etabs
{
    /// <summary>
    /// Extracts column frame objects from an ETABS model into the internal
    /// ColumnElement model.
    ///
    /// FIXED vs original: Rotation was hardcoded to 0 with a "Phase 3" TODO.
    /// Now read via FrameObj.GetLocalAxes, which returns the rotation angle
    /// (degrees) of the local 2/3 axes about the local 1 (member) axis. This
    /// matters for any non-square column section, and is required baseline
    /// for the beam comparer, which reuses the same GetLocalAxes call shape.
    ///
    /// FIXED: TryGetStoryForElevation previously picked the nearest story
    /// with no sanity threshold, so a garbage elevation would silently
    /// attach to whatever story happened to be closest. Now returns null
    /// (no story) if the nearest match is farther than maxDeltaMm.
    ///
    /// VERIFY BEFORE RELYING ON THIS: the exact CSiAPIv1 method names/
    /// signatures below are based on the general shape of the OAPI as
    /// documented by CSI, but signatures have changed across ETABS releases.
    /// Confirm each against the API documentation installed with your
    /// specific ETABS version before trusting the output.
    /// </summary>
    public class EtabsColumnReader
    {
        private readonly cSapModel _sapModel;

        // How far (in the model's native length unit, converted to mm before
        // comparing) an element's elevation can be from the nearest story
        // before we treat it as "no story found" rather than guessing wrong.
        private const double MaxStoryMatchDeltaMm = 500.0;

        public EtabsColumnReader(cSapModel sapModel)
        {
            _sapModel = sapModel ?? throw new ArgumentNullException(nameof(sapModel));
        }

        public List<ColumnElement> ReadColumns()
        {
            var results = new List<ColumnElement>();

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
                if (orientationRet != 0 || orientation != eFrameDesignOrientation.Column)
                    continue; // not a column — skip (beams handled by EtabsBeamReader)

                var column = ReadSingleColumn(name, lengthUnitName);
                if (column != null)
                    results.Add(column);
            }

            return results;
        }

        private ColumnElement ReadSingleColumn(string frameName, string lengthUnitName)
        {
            string pointI = "", pointJ = "";
            int pointRet = _sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
            if (pointRet != 0) return null;

            double xi = 0, yi = 0, zi = 0, xj = 0, yj = 0, zj = 0;
            _sapModel.PointObj.GetCoordCartesian(pointI, ref xi, ref yi, ref zi);
            _sapModel.PointObj.GetCoordCartesian(pointJ, ref xj, ref yj, ref zj);

            bool iIsBase = zi <= zj;
            double baseX = iIsBase ? xi : xj;
            double baseY = iIsBase ? yi : yj;
            double baseZ = iIsBase ? zi : zj;
            double topZ = iIsBase ? zj : zi;

            string sectionName = "", sAuto = "";
            _sapModel.FrameObj.GetSection(frameName, ref sectionName, ref sAuto);

            double widthRaw = 0, depthRaw = 0;
            TryGetRectangularDimensions(sectionName, ref widthRaw, ref depthRaw);

            double rotationDegrees = TryGetRotation(frameName);

            string storyName = TryGetStoryForElevation(baseZ, lengthUnitName);

            var column = new ColumnElement
            {
                Id = frameName,
                Name = frameName,
                Source = SourceApplication.Etabs,
                SectionName = sectionName,
                LevelName = storyName,

                BasePoint = new Point3D(
                    UnitConverter.EtabsLengthToMm(baseX, lengthUnitName),
                    UnitConverter.EtabsLengthToMm(baseY, lengthUnitName),
                    UnitConverter.EtabsLengthToMm(baseZ, lengthUnitName)),

                TopPoint = new Point3D(
                    UnitConverter.EtabsLengthToMm(baseX, lengthUnitName),
                    UnitConverter.EtabsLengthToMm(baseY, lengthUnitName),
                    UnitConverter.EtabsLengthToMm(topZ, lengthUnitName)),

                BaseElevation = UnitConverter.EtabsLengthToMm(baseZ, lengthUnitName),
                TopElevation = UnitConverter.EtabsLengthToMm(topZ, lengthUnitName),

                Width = UnitConverter.EtabsLengthToMm(widthRaw, lengthUnitName),
                Depth = UnitConverter.EtabsLengthToMm(depthRaw, lengthUnitName),
                Rotation = rotationDegrees
            };

            column.CenterPoint = column.BasePoint;
            column.BoundingBox = new BoundingBox3D(column.BasePoint, column.TopPoint);

            return column;
        }

        /// <summary>
        /// FrameObj.GetLocalAxes(name, ref angle, ref advanced) returns the
        /// rotation (degrees) of local axes 2/3 about local axis 1, measured
        /// from ETABS' default auto-orientation. 'advanced' is true if
        /// advanced local axes (not a simple angle) are in use — in that
        /// case a single angle can't fully describe orientation, so we fall
        /// back to 0 and rely on position/section matching alone.
        /// </summary>
        private double TryGetRotation(string frameName)
        {
            double angle = 0;
            bool advanced = false;
            int ret = _sapModel.FrameObj.GetLocalAxes(frameName, ref angle, ref advanced);
            if (ret != 0 || advanced) return 0.0;
            return angle;
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
                depth = t3; // t3 = depth (local 3-axis)
                width = t2; // t2 = width (local 2-axis)
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
            if (smallestDeltaMm > MaxStoryMatchDeltaMm) return null; // no story close enough — don't guess

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
