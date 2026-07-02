using CSIModellingTools.Models;
using static CSIModellingTools.Services.EtabsParametricModellingService;

namespace CSIModellingTools.Services;

internal sealed class EtabsSnapshotExtractor
{
    private const ETABSv1.eUnits CanonicalUnits = ETABSv1.eUnits.kN_m_C;
    private const int EtabsSelectedFrameObjectType = 2;
    private const int EtabsSelectedAreaObjectType = 5;

    private readonly ETABSv1.cSapModel _sapModel;
    private readonly string _sourceModelFileName;

    public EtabsSnapshotExtractor(ETABSv1.cSapModel sapModel, string sourceModelFileName)
    {
        _sapModel = sapModel ?? throw new ArgumentNullException(nameof(sapModel));
        _sourceModelFileName = sourceModelFileName ?? "";
    }

    public EtabsSnapshotExtractionResult Extract()
    {
        var framePropertyWarnings = new List<string>();
        bool framePropertyNamesRead = TryGetFrameSectionNamesForSnapshot(_sapModel, framePropertyWarnings, out List<string> frameSectionNames);
        List<ModelCompareFramePropertySnapshot> frameProperties = framePropertyNamesRead
            ? GetModelCompareFrameProperties(_sapModel, framePropertyWarnings, frameSectionNames)
            : [];
        Dictionary<string, string> materialBySection = frameProperties
            .Where(section => !string.IsNullOrWhiteSpace(section.SectionName))
            .GroupBy(section => section.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().MaterialName, StringComparer.OrdinalIgnoreCase);

        var groupWarnings = new List<string>();
        bool groupNamesRead = TryGetGroupNamesForSnapshot(_sapModel, groupWarnings, out List<string> groupNames);
        Dictionary<string, List<string>> groupsByFrameName = groupNamesRead
            ? GetModelCompareFrameGroups(_sapModel, groupWarnings, groupNames)
            : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> groupsByAreaName = groupNamesRead
            ? GetModelCompareAreaGroups(_sapModel, groupWarnings, groupNames)
            : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // One bulk read of every point coordinate. Used to resolve area corner coordinates without a
        // per-corner COM call. If it is unavailable the per-object reads below remain as a fallback.
        Dictionary<string, (double X, double Y, double Z)> pointCoordinates = TryGetAllPointCoordinates(_sapModel);

        var frameWarnings = new List<string>();
        if (!framePropertyNamesRead)
            frameWarnings.Add("Frame section material data is unavailable because frame property definitions could not be read.");

        // Fast path: pull all frame geometry, sections, stories, endpoints and coordinates in a single
        // GetAllFrames call. Falls back to the per-frame reads if the bulk call is unavailable.
        Dictionary<string, string> frameLabels = TryGetFrameLabels(_sapModel);
        bool frameNamesRead = TryGetAllFramesSnapshot(
            _sapModel,
            materialBySection,
            groupsByFrameName,
            frameLabels,
            frameWarnings,
            out List<ModelCompareFrameSnapshot> frames,
            out int expectedFrameCount);
        if (!frameNamesRead)
        {
            frameNamesRead = TryGetAllFrameNamesForSnapshot(_sapModel, frameWarnings, out List<string> frameNames);
            frames = (frameNamesRead ? frameNames : [])
                .Select(frameName => ReadModelCompareFrameSnapshot(_sapModel, frameName, materialBySection, groupsByFrameName, frameWarnings))
                .Where(frame => frame != null)
                .Select(frame => frame!)
                .OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            expectedFrameCount = frameNamesRead ? frameNames.Count : 0;
        }
        bool framesRead = frameNamesRead && frames.Count == expectedFrameCount;
        if (frameNamesRead && frames.Count != expectedFrameCount)
            frameWarnings.Add($"Frame extraction was marked failed because {expectedFrameCount - frames.Count} of {expectedFrameCount} listed ETABS frames had unreadable geometry.");

        // End releases and persistent member IDs are not part of the bulk frame read, so they are read
        // together in a single per-frame pass. Missing tracking IDs are stamped in-place, but only if the
        // model is already unlocked, so extraction never clears the user's analysis results.
        bool modelUnlocked = TryIsModelUnlocked(_sapModel);
        PopulateFrameReleasesAndIds(_sapModel, frames, modelUnlocked, frameWarnings);

        var areaPropertyWarnings = new List<string>();
        bool areaPropertyNamesRead = TryGetAreaPropertyNamesForSnapshot(_sapModel, areaPropertyWarnings, out List<string> areaPropertyNames);
        List<ModelCompareAreaPropertySnapshot> areaProperties = areaPropertyNamesRead
            ? GetAreaPropertyRows(_sapModel, areaPropertyWarnings, areaPropertyNames)
                .Select(MapModelCompareAreaProperty)
                .ToList()
            : [];
        Dictionary<string, ModelCompareAreaPropertySnapshot> areaPropertyByName = areaProperties
            .Where(property => !string.IsNullOrWhiteSpace(property.PropertyName))
            .GroupBy(property => property.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var areaWarnings = new List<string>();
        bool areaNamesRead = TryGetAllAreaNamesForSnapshot(_sapModel, areaWarnings, out List<string> areaNames);
        if (!areaPropertyNamesRead)
            areaWarnings.Add("Area material and thickness data are unavailable because area property definitions could not be read.");
        List<ModelCompareAreaObjectSnapshot> areas = (areaNamesRead ? areaNames : [])
            .Select(areaName => ReadModelCompareAreaSnapshot(_sapModel, areaName, areaPropertyByName, groupsByAreaName, pointCoordinates, areaWarnings))
            .Where(area => area != null)
            .Select(area => area!)
            .OrderBy(area => area.AreaName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool areasRead = areaNamesRead && areas.Count == areaNames.Count;
        if (areaNamesRead && areas.Count != areaNames.Count)
            areaWarnings.Add($"Area extraction was marked failed because {areaNames.Count - areas.Count} of {areaNames.Count} listed ETABS areas had unreadable geometry.");

        // Persistent tracking IDs for areas (stamped in place only when the model is already unlocked).
        PopulateAreaIds(_sapModel, areas, modelUnlocked, areaWarnings);

        var materialWarnings = new List<string>();
        bool materialNamesRead = TryGetMaterialNamesForSnapshot(_sapModel, materialWarnings, out List<string> materialNames);
        List<ModelCompareMaterialSnapshot> materials = materialNamesRead
            ? GetMaterialPropertyRows(_sapModel, materialWarnings, materialNames)
                .Select(MapModelCompareMaterial)
                .ToList()
            : [];

        var jointWarnings = new List<string>();
        bool jointsRead = TryGetJointSnapshots(_sapModel, pointCoordinates, jointWarnings, out List<ModelCompareJointSnapshot> joints);

        var extractionWarnings = new List<string>();
        extractionWarnings.AddRange(frameWarnings);
        extractionWarnings.AddRange(areaWarnings);
        extractionWarnings.AddRange(framePropertyWarnings);
        extractionWarnings.AddRange(areaPropertyWarnings);
        extractionWarnings.AddRange(materialWarnings);
        extractionWarnings.AddRange(groupWarnings);
        extractionWarnings.AddRange(jointWarnings);
        extractionWarnings = extractionWarnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var snapshot = new ModelCompareSnapshot
        {
            Metadata = new ModelCompareSnapshotMetadata
            {
                SchemaVersion = ModelCompareSchema.CurrentVersion,
                ProductName = TryGetEtabsProductName(_sapModel),
                SourceModelFileName = _sourceModelFileName,
                SnapshotCreatedAt = DateTimeOffset.Now,
                Units = CanonicalUnits.ToString(),
                LengthUnit = "m",
                ForceUnit = "kN",
                StressUnit = "MPa",
                UnitWeightUnit = "kN/m^3",
                UnitWeightConvention = "Weight per unit volume",
                FramesReadStatus = BuildModelCompareReadStatus(framesRead, frameWarnings),
                AreasReadStatus = BuildModelCompareReadStatus(areasRead, areaWarnings),
                FramePropertiesReadStatus = BuildModelCompareReadStatus(framePropertyNamesRead, framePropertyWarnings),
                AreaPropertiesReadStatus = BuildModelCompareReadStatus(areaPropertyNamesRead, areaPropertyWarnings),
                MaterialsReadStatus = BuildModelCompareReadStatus(materialNamesRead, materialWarnings),
                GroupsReadStatus = BuildModelCompareReadStatus(groupNamesRead, groupWarnings),
                JointsReadStatus = BuildModelCompareReadStatus(jointsRead, jointWarnings),
                ExtractionWarnings = extractionWarnings
            },
            Frames = frames,
            Areas = areas,
            Joints = joints,
            FrameProperties = frameProperties,
            AreaProperties = areaProperties,
            Materials = materials
        };

        return new EtabsSnapshotExtractionResult
        {
            Snapshot = snapshot,
            Warnings = extractionWarnings
        };
    }

    private static ModelCompareSnapshotReadStatus BuildModelCompareReadStatus(bool readSucceeded, IReadOnlyCollection<string> warnings)
    {
        if (!readSucceeded)
            return ModelCompareSnapshotReadStatus.Failed;

        return warnings.Count == 0
            ? ModelCompareSnapshotReadStatus.Success
            : ModelCompareSnapshotReadStatus.SuccessWithWarnings;
    }

    private static bool TryIsModelUnlocked(ETABSv1.cSapModel sapModel)
    {
        try
        {
            return !sapModel.GetModelIsLocked();
        }
        catch
        {
            // If the lock state is unknown, do not write to the model.
            return false;
        }
    }

    private static void PopulateFrameReleasesAndIds(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<ModelCompareFrameSnapshot> frames,
        bool assignMissingIds,
        List<string> warnings)
    {
        int releaseFailureCount = 0;
        int assignedIdCount = 0;
        int missingIdCount = 0;
        foreach (ModelCompareFrameSnapshot frame in frames)
        {
            bool[] ii = [];
            bool[] jj = [];
            double[] startValue = [];
            double[] endValue = [];

            try
            {
                int ret = sapModel.FrameObj.GetReleases(frame.FrameName, ref ii, ref jj, ref startValue, ref endValue);
                if (ret != 0 || ii.Length < 6 || jj.Length < 6)
                {
                    releaseFailureCount++;
                }
                else
                {
                    frame.ReleaseAxialI = ii[0];
                    frame.ReleaseShear2I = ii[1];
                    frame.ReleaseShear3I = ii[2];
                    frame.ReleaseTorsionI = ii[3];
                    frame.ReleaseMoment2I = ii[4];
                    frame.ReleaseMoment3I = ii[5];
                    frame.ReleaseAxialJ = jj[0];
                    frame.ReleaseShear2J = jj[1];
                    frame.ReleaseShear3J = jj[2];
                    frame.ReleaseTorsionJ = jj[3];
                    frame.ReleaseMoment2J = jj[4];
                    frame.ReleaseMoment3J = jj[5];
                }
            }
            catch
            {
                releaseFailureCount++;
            }

            try
            {
                // Capture any GUID present (tool-assigned or from a Revit/IFC import) as the identity.
                string guid = "";
                int guidRet = sapModel.FrameObj.GetGUID(frame.FrameName, ref guid);
                if (guidRet == 0 && !string.IsNullOrWhiteSpace(guid))
                {
                    frame.Uid = guid.Trim();
                }
                else if (assignMissingIds)
                {
                    // Stamp a tracking ID so this member can be followed across future revisions.
                    string newGuid = ModelCompareMemberId.Prefix + Guid.NewGuid().ToString("N");
                    int setRet = sapModel.FrameObj.SetGUID(frame.FrameName, newGuid);
                    if (setRet == 0)
                    {
                        frame.Uid = newGuid;
                        assignedIdCount++;
                    }
                    else
                    {
                        missingIdCount++;
                    }
                }
                else
                {
                    missingIdCount++;
                }
            }
            catch
            {
                // Persistent member IDs are optional; a missing one simply falls back to geometry matching.
                missingIdCount++;
            }
        }

        if (releaseFailureCount > 0)
            warnings.Add($"ETABS end releases could not be read for {releaseFailureCount} frame(s); those frames are treated as fully continuous.");
        if (assignedIdCount > 0)
            warnings.Add($"Assigned tracking IDs to {assignedIdCount} previously untracked frame(s). Save the ETABS model so these IDs persist for future comparisons.");
        if (missingIdCount > 0)
            warnings.Add($"{missingIdCount} frame(s) have no tracking ID (the model is locked or the ID write failed), so they are matched by geometry. Unlock the model and re-snapshot, or use Assign Member IDs, then save, to enable ID tracking.");
    }

    private static bool TryGetJointSnapshots(
        ETABSv1.cSapModel sapModel,
        IReadOnlyDictionary<string, (double X, double Y, double Z)> pointCoordinates,
        List<string> warnings,
        out List<ModelCompareJointSnapshot> joints)
    {
        joints = [];

        // Resolve the list of point names from the bulk coordinate read; fall back to the name list.
        List<string> pointNames;
        if (pointCoordinates.Count > 0)
        {
            pointNames = pointCoordinates.Keys.ToList();
        }
        else
        {
            int numberNames = 0;
            string[] rawNames = [];
            try
            {
                int ret = sapModel.PointObj.GetNameList(ref numberNames, ref rawNames);
                if (ret != 0)
                {
                    warnings.Add($"ETABS point object list could not be loaded for joint restraint extraction. Return code: {ret}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                warnings.Add("ETABS point object list could not be loaded for joint restraint extraction: " + ex.Message);
                return false;
            }

            pointNames = rawNames
                .Take(Math.Min(numberNames, rawNames.Length))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToList();
        }

        int failureCount = 0;
        var collected = new List<ModelCompareJointSnapshot>();
        foreach (string pointName in pointNames)
        {
            bool[] restraint = [];
            try
            {
                int ret = sapModel.PointObj.GetRestraint(pointName, ref restraint);
                if (ret != 0 || restraint.Length < 6)
                {
                    failureCount++;
                    continue;
                }
            }
            catch
            {
                failureCount++;
                continue;
            }

            // Only capture joints that are actually restrained; unrestrained mesh nodes are not useful here.
            if (!(restraint[0] || restraint[1] || restraint[2] || restraint[3] || restraint[4] || restraint[5]))
                continue;

            (double X, double Y, double Z) coordinate = pointCoordinates.TryGetValue(pointName, out (double X, double Y, double Z) cached)
                ? cached
                : TryReadPointCoordinate(sapModel, pointName);

            collected.Add(new ModelCompareJointSnapshot
            {
                PointName = pointName,
                X = coordinate.X,
                Y = coordinate.Y,
                Z = coordinate.Z,
                RestraintUX = restraint[0],
                RestraintUY = restraint[1],
                RestraintUZ = restraint[2],
                RestraintRX = restraint[3],
                RestraintRY = restraint[4],
                RestraintRZ = restraint[5]
            });
        }

        joints = collected
            .OrderBy(joint => joint.PointName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (failureCount > 0)
            warnings.Add($"ETABS restraint data could not be read for {failureCount} point(s); those points were omitted from the joint comparison.");

        return true;
    }

    private static (double X, double Y, double Z) TryReadPointCoordinate(ETABSv1.cSapModel sapModel, string pointName)
    {
        try
        {
            return GetPointCoordinates(sapModel, pointName);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private static bool TryGetAllFrameNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.FrameObj.GetNameList(ref numberNames, ref rawNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS frame object list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS frame object list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetAllAreaNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.AreaObj.GetNameList(ref numberNames, ref rawNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS area object list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS area object list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetAreaPropertyNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.PropArea.GetNameList(ref numberNames, ref rawNames, 0);
            if (ret != 0)
            {
                warnings.Add($"ETABS area property list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS area property list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetGroupNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.GroupDef.GetNameList(ref numberNames, ref rawNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS group list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS group list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetFrameSectionNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        var collectedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int successfulCalls = 0;

        foreach (ETABSv1.eFramePropType propType in Enum.GetValues(typeof(ETABSv1.eFramePropType)).Cast<ETABSv1.eFramePropType>())
        {
            int numberNames = 0;
            string[] rawNames = [];
            try
            {
                int ret = sapModel.PropFrame.GetNameList(ref numberNames, ref rawNames, propType);
                if (ret != 0)
                    continue;

                successfulCalls++;
                foreach (string name in NormalizeModelCompareNames(numberNames, rawNames))
                    collectedNames.Add(name);
            }
            catch
            {
                // Missing property families are expected; the category fails only if every family call fails.
            }
        }

        names = collectedNames.ToList();
        if (successfulCalls > 0)
            return true;

        warnings.Add("ETABS frame property definition list could not be loaded for snapshot extraction.");
        return false;
    }

    private static bool TryGetMaterialNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        var collectedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int successfulCalls = 0;

        foreach (ETABSv1.eMatType materialType in Enum.GetValues(typeof(ETABSv1.eMatType)).Cast<ETABSv1.eMatType>())
        {
            int numberNames = 0;
            string[] rawNames = [];
            try
            {
                int ret = sapModel.PropMaterial.GetNameList(ref numberNames, ref rawNames, materialType);
                if (ret != 0)
                    continue;

                successfulCalls++;
                foreach (string name in NormalizeModelCompareNames(numberNames, rawNames))
                    collectedNames.Add(name);
            }
            catch
            {
                // Missing material families are expected; the category fails only if every family call fails.
            }
        }

        names = collectedNames.ToList();
        if (successfulCalls > 0)
            return true;

        warnings.Add("ETABS material definition list could not be loaded for snapshot extraction.");
        return false;
    }

    private static List<string> NormalizeModelCompareNames(int numberNames, string[] names)
    {
        return names
            .Take(Math.Min(numberNames, names.Length))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, (double X, double Y, double Z)> TryGetAllPointCoordinates(ETABSv1.cSapModel sapModel)
    {
        var coordinates = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);
        int numberNames = 0;
        string[] names = [];
        double[] x = [];
        double[] y = [];
        double[] z = [];

        try
        {
            int ret = sapModel.PointObj.GetAllPoints(ref numberNames, ref names, ref x, ref y, ref z, "Global");
            if (ret != 0)
                return coordinates;

            int count = Math.Min(numberNames, Math.Min(names.Length, Math.Min(x.Length, Math.Min(y.Length, z.Length))));
            for (int index = 0; index < count; index++)
            {
                string pointName = (names[index] ?? "").Trim();
                if (pointName.Length == 0)
                    continue;

                coordinates[pointName] = (x[index], y[index], z[index]);
            }
        }
        catch
        {
            // Bulk point coordinates are an optional accelerator; per-object reads remain as a fallback.
        }

        return coordinates;
    }

    private static Dictionary<string, string> TryGetFrameLabels(ETABSv1.cSapModel sapModel)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int numberNames = 0;
        string[] names = [];
        string[] labelValues = [];
        string[] stories = [];

        try
        {
            int ret = sapModel.FrameObj.GetLabelNameList(ref numberNames, ref names, ref labelValues, ref stories);
            if (ret != 0)
                return labels;

            int count = Math.Min(numberNames, Math.Min(names.Length, labelValues.Length));
            for (int index = 0; index < count; index++)
            {
                string name = (names[index] ?? "").Trim();
                if (name.Length == 0)
                    continue;

                labels[name] = (labelValues[index] ?? "").Trim();
            }
        }
        catch
        {
            // Labels are optional display/search fields; missing labels do not affect comparison.
        }

        return labels;
    }

    private static bool TryGetAllFramesSnapshot(
        ETABSv1.cSapModel sapModel,
        IReadOnlyDictionary<string, string> materialBySection,
        IReadOnlyDictionary<string, List<string>> groupsByFrameName,
        IReadOnlyDictionary<string, string> frameLabels,
        List<string> warnings,
        out List<ModelCompareFrameSnapshot> frames,
        out int expectedCount)
    {
        frames = [];
        expectedCount = 0;

        int numberNames = 0;
        string[] names = [];
        string[] propNames = [];
        string[] stories = [];
        string[] pointName1 = [];
        string[] pointName2 = [];
        double[] point1X = [];
        double[] point1Y = [];
        double[] point1Z = [];
        double[] point2X = [];
        double[] point2Y = [];
        double[] point2Z = [];
        double[] angle = [];
        double[] offset1X = [];
        double[] offset2X = [];
        double[] offset1Y = [];
        double[] offset2Y = [];
        double[] offset1Z = [];
        double[] offset2Z = [];
        int[] cardinalPoint = [];

        try
        {
            int ret = sapModel.FrameObj.GetAllFrames(
                ref numberNames,
                ref names,
                ref propNames,
                ref stories,
                ref pointName1,
                ref pointName2,
                ref point1X,
                ref point1Y,
                ref point1Z,
                ref point2X,
                ref point2Y,
                ref point2Z,
                ref angle,
                ref offset1X,
                ref offset2X,
                ref offset1Y,
                ref offset2Y,
                ref offset1Z,
                ref offset2Z,
                ref cardinalPoint,
                "Global");
            if (ret != 0)
            {
                warnings.Add($"ETABS bulk frame read (GetAllFrames) returned code {ret}; falling back to per-frame extraction.");
                return false;
            }
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS bulk frame read (GetAllFrames) was unavailable; falling back to per-frame extraction: " + ex.Message);
            return false;
        }

        int count = Math.Min(numberNames, names.Length);
        if (propNames.Length < count ||
            stories.Length < count ||
            pointName1.Length < count ||
            pointName2.Length < count ||
            point1X.Length < count || point1Y.Length < count || point1Z.Length < count ||
            point2X.Length < count || point2Y.Length < count || point2Z.Length < count)
        {
            warnings.Add("ETABS bulk frame read (GetAllFrames) returned inconsistent array sizes; falling back to per-frame extraction.");
            return false;
        }

        var builtFrames = new List<ModelCompareFrameSnapshot>(count);
        int validNames = 0;
        for (int index = 0; index < count; index++)
        {
            string frameName = (names[index] ?? "").Trim();
            if (frameName.Length == 0)
                continue;

            validNames++;

            string pointI = (pointName1[index] ?? "").Trim();
            string pointJ = (pointName2[index] ?? "").Trim();
            if (pointI.Length == 0 || pointJ.Length == 0)
            {
                warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because its endpoint names could not be read.");
                continue;
            }

            if (!double.IsFinite(point1X[index]) || !double.IsFinite(point1Y[index]) || !double.IsFinite(point1Z[index]) ||
                !double.IsFinite(point2X[index]) || !double.IsFinite(point2Y[index]) || !double.IsFinite(point2Z[index]))
            {
                warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because ETABS returned non-finite endpoint coordinates.");
                continue;
            }

            (double X, double Y, double Z) pointICoord = (point1X[index], point1Y[index], point1Z[index]);
            (double X, double Y, double Z) pointJCoord = (point2X[index], point2Y[index], point2Z[index]);
            string sectionName = (propNames[index] ?? "").Trim();
            string materialName = materialBySection.TryGetValue(sectionName, out string? sectionMaterial)
                ? sectionMaterial
                : "";

            builtFrames.Add(new ModelCompareFrameSnapshot
            {
                FrameName = frameName,
                Label = frameLabels.TryGetValue(frameName, out string? label) ? label : "",
                Story = (stories[index] ?? "").Trim(),
                PointIName = pointI,
                PointJName = pointJ,
                IX = pointICoord.X,
                IY = pointICoord.Y,
                IZ = pointICoord.Z,
                JX = pointJCoord.X,
                JY = pointJCoord.Y,
                JZ = pointJCoord.Z,
                Length = CalculateFrameLength(pointICoord, pointJCoord),
                SectionName = sectionName,
                MaterialName = materialName,
                GroupNames = groupsByFrameName.TryGetValue(frameName, out List<string>? groupNames)
                    ? groupNames.ToList()
                    : []
            });
        }

        frames = builtFrames
            .OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        expectedCount = validNames;
        return true;
    }

    private static ModelCompareFrameSnapshot? ReadModelCompareFrameSnapshot(
        ETABSv1.cSapModel sapModel,
        string frameName,
        IReadOnlyDictionary<string, string> materialBySection,
        IReadOnlyDictionary<string, List<string>> groupsByFrameName,
        List<string> warnings)
    {
        string label = "";
        string story = "";
        try
        {
            sapModel.FrameObj.GetLabelFromName(frameName, ref label, ref story);
        }
        catch
        {
            // Label/story are optional display fields.
        }

        string currentSection = "";
        try
        {
            string autoSelectList = "";
            int ret = sapModel.FrameObj.GetSection(frameName, ref currentSection, ref autoSelectList);
            if (ret != 0)
                warnings.Add($"ETABS could not read current section for frame '{frameName}'. Return code: {ret}.");

            if (string.IsNullOrWhiteSpace(currentSection))
                currentSection = autoSelectList;
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS could not read current section for frame '{frameName}': {ex.Message}");
        }

        string pointI = "";
        string pointJ = "";
        int pointsRet;
        try
        {
            pointsRet = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
        }
        catch (Exception ex)
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because its endpoint names could not be read: {ex.Message}");
            return null;
        }

        if (pointsRet != 0 || string.IsNullOrWhiteSpace(pointI) || string.IsNullOrWhiteSpace(pointJ))
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because its endpoint names could not be read. Return code: {pointsRet}.");
            return null;
        }

        (double X, double Y, double Z) pointICoord;
        (double X, double Y, double Z) pointJCoord;
        try
        {
            pointICoord = GetPointCoordinates(sapModel, pointI);
            pointJCoord = GetPointCoordinates(sapModel, pointJ);
        }
        catch (Exception ex)
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because its endpoint coordinates could not be read: {ex.Message}");
            return null;
        }

        if (!double.IsFinite(pointICoord.X) || !double.IsFinite(pointICoord.Y) || !double.IsFinite(pointICoord.Z) ||
            !double.IsFinite(pointJCoord.X) || !double.IsFinite(pointJCoord.Y) || !double.IsFinite(pointJCoord.Z))
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because ETABS returned non-finite endpoint coordinates.");
            return null;
        }

        string materialName = materialBySection.TryGetValue(currentSection, out string? sectionMaterial)
            ? sectionMaterial
            : "";

        return new ModelCompareFrameSnapshot
        {
            FrameName = frameName,
            Label = label,
            Story = story,
            PointIName = pointI,
            PointJName = pointJ,
            IX = pointICoord.X,
            IY = pointICoord.Y,
            IZ = pointICoord.Z,
            JX = pointJCoord.X,
            JY = pointJCoord.Y,
            JZ = pointJCoord.Z,
            Length = CalculateFrameLength(pointICoord, pointJCoord),
            SectionName = currentSection,
            MaterialName = materialName,
            GroupNames = groupsByFrameName.TryGetValue(frameName, out List<string>? groupNames)
                ? groupNames.ToList()
                : []
        };
    }

    private static Dictionary<string, List<string>> GetModelCompareFrameGroups(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetModelCompareFrameGroups(sapModel, warnings, GetGroupNames(sapModel, warnings));
    }

    private static Dictionary<string, List<string>> GetModelCompareFrameGroups(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        IReadOnlyList<string> groupNames)
    {
        var groupsByFrameName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string groupName in groupNames)
        {
            int numberItems = 0;
            int[] objectTypes = [];
            string[] objectNames = [];

            try
            {
                int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
                if (ret != 0)
                {
                    warnings.Add($"ETABS group '{groupName}' assignments could not be read for model compare snapshot. Return code: {ret}.");
                    continue;
                }

                int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
                for (int index = 0; index < count; index++)
                {
                    if (objectTypes[index] != EtabsSelectedFrameObjectType)
                        continue;

                    string frameName = (objectNames[index] ?? "").Trim();
                    if (frameName.Length == 0)
                        continue;

                    if (!groupsByFrameName.TryGetValue(frameName, out List<string>? frameGroups))
                    {
                        frameGroups = [];
                        groupsByFrameName[frameName] = frameGroups;
                    }

                    if (!frameGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                        frameGroups.Add(groupName);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS group '{groupName}' assignments could not be read for model compare snapshot: {ex.Message}");
            }
        }

        foreach (List<string> frameGroups in groupsByFrameName.Values)
            frameGroups.Sort(StringComparer.OrdinalIgnoreCase);

        return groupsByFrameName;
    }

    private static List<string> GetAllAreaNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            int ret = sapModel.AreaObj.GetNameList(ref numberNames, ref names);
            if (ret != 0)
            {
                warnings.Add($"ETABS area object list could not be loaded. Return code: {ret}.");
                return [];
            }

            return names
                .Take(Math.Min(numberNames, names.Length))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS area object list could not be loaded: " + ex.Message);
            return [];
        }
    }

    private static ModelCompareAreaObjectSnapshot? ReadModelCompareAreaSnapshot(
        ETABSv1.cSapModel sapModel,
        string areaName,
        IReadOnlyDictionary<string, ModelCompareAreaPropertySnapshot> areaPropertyByName,
        IReadOnlyDictionary<string, List<string>> groupsByAreaName,
        IReadOnlyDictionary<string, (double X, double Y, double Z)> pointCoordinates,
        List<string> warnings)
    {
        string label = "";
        string story = "";
        try
        {
            sapModel.AreaObj.GetLabelFromName(areaName, ref label, ref story);
        }
        catch
        {
            // Label/story are optional display fields.
        }

        string propertyName = "";
        try
        {
            int ret = sapModel.AreaObj.GetProperty(areaName, ref propertyName);
            if (ret != 0)
                warnings.Add($"ETABS could not read area property assignment for area '{areaName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS area property assignment read failed for area '{areaName}': {ex.Message}");
        }

        List<ModelComparePointSnapshot> corners = ReadModelCompareAreaCorners(sapModel, areaName, pointCoordinates, warnings);
        if (corners.Count < 3)
        {
            warnings.Add($"ETABS area '{areaName}' was skipped in model compare snapshot because fewer than three corner points were readable.");
            return null;
        }

        bool isOpening = false;
        try
        {
            sapModel.AreaObj.GetOpening(areaName, ref isOpening);
        }
        catch
        {
            // Opening flag is optional; a non-opening default is safe.
        }

        areaPropertyByName.TryGetValue(propertyName, out ModelCompareAreaPropertySnapshot? areaProperty);

        return new ModelCompareAreaObjectSnapshot
        {
            AreaName = areaName,
            Label = label,
            Story = story,
            PropertyName = propertyName,
            MaterialName = areaProperty?.MaterialName ?? "",
            Thickness = areaProperty?.Thickness ?? 0,
            IsOpening = isOpening,
            Corners = corners,
            GroupNames = groupsByAreaName.TryGetValue(areaName, out List<string>? groupNames)
                ? groupNames.ToList()
                : []
        };
    }

    private static void PopulateAreaIds(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<ModelCompareAreaObjectSnapshot> areas,
        bool assignMissingIds,
        List<string> warnings)
    {
        int assignedIdCount = 0;
        int missingIdCount = 0;
        foreach (ModelCompareAreaObjectSnapshot area in areas)
        {
            try
            {
                string guid = "";
                int guidRet = sapModel.AreaObj.GetGUID(area.AreaName, ref guid);
                if (guidRet == 0 && !string.IsNullOrWhiteSpace(guid))
                {
                    area.Uid = guid.Trim();
                }
                else if (assignMissingIds)
                {
                    string newGuid = ModelCompareMemberId.Prefix + Guid.NewGuid().ToString("N");
                    int setRet = sapModel.AreaObj.SetGUID(area.AreaName, newGuid);
                    if (setRet == 0)
                    {
                        area.Uid = newGuid;
                        assignedIdCount++;
                    }
                    else
                    {
                        missingIdCount++;
                    }
                }
                else
                {
                    missingIdCount++;
                }
            }
            catch
            {
                missingIdCount++;
            }
        }

        if (assignedIdCount > 0)
            warnings.Add($"Assigned tracking IDs to {assignedIdCount} previously untracked area(s). Save the ETABS model so these IDs persist for future comparisons.");
        if (missingIdCount > 0)
            warnings.Add($"{missingIdCount} area(s) have no tracking ID (the model is locked or the ID write failed), so they are matched by geometry. Unlock the model and re-snapshot, or use Assign Member IDs, then save, to enable ID tracking.");
    }

    private static List<ModelComparePointSnapshot> ReadModelCompareAreaCorners(
        ETABSv1.cSapModel sapModel,
        string areaName,
        IReadOnlyDictionary<string, (double X, double Y, double Z)> pointCoordinates,
        List<string> warnings)
    {
        int numberPoints = 0;
        string[] pointNames = [];
        try
        {
            int ret = sapModel.AreaObj.GetPoints(areaName, ref numberPoints, ref pointNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS could not read corner points for area '{areaName}'. Return code: {ret}.");
                return [];
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS corner point read failed for area '{areaName}': {ex.Message}");
            return [];
        }

        var corners = new List<ModelComparePointSnapshot>();
        int count = Math.Min(numberPoints, pointNames.Length);
        for (int index = 0; index < count; index++)
        {
            string pointName = (pointNames[index] ?? "").Trim();
            if (pointName.Length == 0)
                continue;

            try
            {
                (double X, double Y, double Z) point = pointCoordinates.TryGetValue(pointName, out (double X, double Y, double Z) cached)
                    ? cached
                    : GetPointCoordinates(sapModel, pointName);
                corners.Add(new ModelComparePointSnapshot
                {
                    PointName = pointName,
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z
                });
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS corner point '{pointName}' coordinates for area '{areaName}' could not be read: {ex.Message}");
            }
        }

        return corners;
    }

    private static Dictionary<string, List<string>> GetModelCompareAreaGroups(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetModelCompareAreaGroups(sapModel, warnings, GetGroupNames(sapModel, warnings));
    }

    private static Dictionary<string, List<string>> GetModelCompareAreaGroups(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        IReadOnlyList<string> groupNames)
    {
        var groupsByAreaName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string groupName in groupNames)
        {
            int numberItems = 0;
            int[] objectTypes = [];
            string[] objectNames = [];

            try
            {
                int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
                if (ret != 0)
                {
                    warnings.Add($"ETABS group '{groupName}' assignments could not be read for model compare area snapshot. Return code: {ret}.");
                    continue;
                }

                int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
                for (int index = 0; index < count; index++)
                {
                    if (objectTypes[index] != EtabsSelectedAreaObjectType)
                        continue;

                    string areaName = (objectNames[index] ?? "").Trim();
                    if (areaName.Length == 0)
                        continue;

                    if (!groupsByAreaName.TryGetValue(areaName, out List<string>? areaGroups))
                    {
                        areaGroups = [];
                        groupsByAreaName[areaName] = areaGroups;
                    }

                    if (!areaGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                        areaGroups.Add(groupName);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS group '{groupName}' area assignments could not be read for model compare snapshot: {ex.Message}");
            }
        }

        foreach (List<string> areaGroups in groupsByAreaName.Values)
            areaGroups.Sort(StringComparer.OrdinalIgnoreCase);

        return groupsByAreaName;
    }

    private static List<ModelCompareFramePropertySnapshot> GetModelCompareFrameProperties(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        IReadOnlyList<string>? frameSectionNames = null)
    {
        return (frameSectionNames ?? GetFrameSectionNames(sapModel, warnings))
            .Select(name =>
            {
                ETABSv1.eFramePropType propType = ETABSv1.eFramePropType.General;
                string materialName = "";

                try
                {
                    int typeRet = sapModel.PropFrame.GetTypeOAPI(name, ref propType);
                    if (typeRet != 0)
                        warnings.Add($"ETABS could not read frame section shape for '{name}' in model compare snapshot. Return code: {typeRet}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS frame section shape read failed for '{name}' in model compare snapshot: {ex.Message}");
                }

                try
                {
                    int matRet = sapModel.PropFrame.GetMaterial(name, ref materialName);
                    if (matRet != 0)
                        warnings.Add($"ETABS could not read frame section material for '{name}' in model compare snapshot. Return code: {matRet}.");
                }
                catch
                {
                    // Imported or auto-select properties may not expose one simple material name.
                }

                (double Depth, double Width, double FlangeThickness, double WebThickness) dimensions = GetFrameSectionDimensions(sapModel, name, propType);

                return new ModelCompareFramePropertySnapshot
                {
                    SectionName = name,
                    SectionType = FormatFramePropType(propType),
                    MaterialName = materialName,
                    Depth = dimensions.Depth,
                    Width = dimensions.Width,
                    FlangeThickness = dimensions.FlangeThickness,
                    WebThickness = dimensions.WebThickness,
                    SummaryText = GetFrameSectionSummary(sapModel, name)
                };
            })
            .OrderBy(row => row.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModelCompareAreaPropertySnapshot MapModelCompareAreaProperty(EtabsAreaPropertyRow row)
    {
        return new ModelCompareAreaPropertySnapshot
        {
            PropertyName = row.Name,
            AreaType = row.AreaType,
            ShellType = row.ShellType,
            MaterialName = row.MaterialName,
            Thickness = row.Thickness
        };
    }

    private static ModelCompareMaterialSnapshot MapModelCompareMaterial(EtabsMaterialPropertyRow row)
    {
        return new ModelCompareMaterialSnapshot
        {
            MaterialName = row.Name,
            MaterialType = row.MaterialType,
            ElasticModulus = row.ElasticModulusMpa,
            PoissonRatio = row.PoissonRatio,
            UnitWeight = row.UnitWeightKnPerM3,
            DesignSummary = row.DesignSummary
        };
    }

    private static string TryGetEtabsProductName(ETABSv1.cSapModel sapModel)
    {
        try
        {
            dynamic dynamicSapModel = sapModel;
            string version = "";
            int ret = dynamicSapModel.GetVersion(ref version);
            if (ret == 0 && !string.IsNullOrWhiteSpace(version))
            {
                string trimmedVersion = version.Trim();
                return trimmedVersion.Contains("ETABS", StringComparison.OrdinalIgnoreCase)
                    ? trimmedVersion
                    : $"ETABS {trimmedVersion}";
            }
        }
        catch
        {
            // Some ETABS API versions do not expose version information through SapModel.
        }

        return "ETABS";
    }
}

internal sealed class EtabsSnapshotExtractionResult
{
    public ModelCompareSnapshot Snapshot { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
}
