using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed partial class EtabsParametricModellingService
{
    private const double CityCoordinateTolerance = 0.000001;

    public CityOfTomorrowDrawResult DrawCityOfTomorrow(CityOfTomorrowDrawRequest request)
    {
        var warnings = new List<string>();
        var appliedTopChordLoads = new List<CityAppliedTopChordLoad>();
        try
        {
            CityOfTomorrowModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0) throw new InvalidOperationException("No City of Tomorrow geometry was provided.");
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(GetEtabsObject(request.EtabsInstanceId));
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCityTopChordLoadPattern(sapModel, model, warnings);
                string groupName = EnsureEtabsDrawGroup(sapModel, model.GroupName, warnings);
                int existing = GetCityAssignments(sapModel, groupName).Count;
                if (!request.ReplaceExistingStructure && existing > 0)
                    throw new InvalidOperationException($"Group '{groupName}' already contains {existing} object(s). Use Regenerate This Structure or change Structure ID.");
                if (request.ReplaceExistingStructure) DeleteCityObjects(sapModel, groupName, warnings);

                var points = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (CityNode node in model.Nodes)
                {
                    string pointName = "";
                    string preferred = EtabsNameUtility.BuildSafeName("", $"{model.StructureId}_{node.Key}");
                    int ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref pointName, preferred, "Global", true, 0);
                    if (ret != 0)
                    {
                        pointName = "";
                        ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref pointName, "", "Global", true, 0);
                    }
                    if (ret != 0 || string.IsNullOrWhiteSpace(pointName))
                    {
                        warnings.Add($"ETABS could not create point '{node.Key}'. Return code: {ret}.");
                        continue;
                    }
                    points[node.Key] = pointName;
                    TryAssignPointToEtabsGroup(sapModel, pointName, groupName, node.Key, warnings);
                }

                var objects = new List<string>();
                var frameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int tensionCount = 0;
                foreach (CityMember member in model.Members)
                {
                    if (!points.TryGetValue(member.StartNodeKey, out string? pi) || !points.TryGetValue(member.EndNodeKey, out string? pj))
                    {
                        warnings.Add($"Skipped '{member.Id}': shared endpoints were unavailable.");
                        continue;
                    }
                    string frameName = "";
                    int ret = sapModel.FrameObj.AddByPoint(pi, pj, ref frameName, member.SectionName, EtabsNameUtility.BuildSafeName("", member.Id));
                    if (ret != 0)
                    {
                        frameName = "";
                        ret = sapModel.FrameObj.AddByPoint(pi, pj, ref frameName, member.SectionName, "");
                    }
                    if (ret != 0 || string.IsNullOrWhiteSpace(frameName))
                    {
                        warnings.Add($"ETABS could not draw '{member.Id}'. Return code: {ret}.");
                        continue;
                    }
                    TryAssignFrameSection(sapModel, frameName, member.Id, member.SectionName, warnings);
                    TryAssignFrameToEtabsGroup(sapModel, frameName, groupName, member.Id, warnings);
                    TryApplyCityReleasePreset(sapModel, frameName, member, warnings);
                    if (member.IsTensionOnly)
                    {
                        int limitRet = sapModel.FrameObj.SetTCLimits(frameName, true, 0, false, 0, EtabsObjects);
                        if (limitRet != 0) warnings.Add($"Could not assign zero compression capacity to '{member.Id}'. Return code: {limitRet}.");
                        tensionCount++;
                    }
                    frameMap[member.Id] = frameName;
                    objects.Add(frameName);
                }

                foreach (CityNode support in model.Nodes.Where(n => n.IsSupport))
                    if (points.TryGetValue(support.Key, out string? pointName))
                        TrySetPointRestraint(sapModel, pointName, [true, true, true, false, false, false], $"City support '{support.Key}'", warnings);

                List<string> topChordFrames = GetCityTopChordFrameNames(model, frameMap, warnings);
                List<string> topChordJoints = GetCityTopChordJointPointNames(model, points, warnings);
                TryApplyCityTopChordLoadsToEtabs(sapModel, model.Input, topChordFrames, topChordJoints, warnings);
                appliedTopChordLoads = ReadCityTopChordLoads(sapModel, topChordFrames, topChordJoints, warnings);

                try
                {
                    CityOfTomorrowManifestRepository.Save(new CityOfTomorrowGenerationManifest
                    {
                        StructureId = model.StructureId, GeneratedAtUtc = DateTime.UtcNow, EtabsGroupName = groupName,
                        InputSnapshot = model.Input, EtabsPointNames = points.Values.Distinct().ToList(), EtabsFrameNames = objects
                    });
                }
                catch (Exception ex) { warnings.Add("Geometry was drawn, but the local manifest could not be saved: " + ex.Message); }

                TryRefreshEtabsView(sapModel);
                return new CityOfTomorrowDrawResult
                {
                    IsError = objects.Count == 0, Message = $"Drawn {objects.Count} City of Tomorrow objects in group '{groupName}', including {tensionCount} tension-only members.",
                    FrameCount = objects.Count, TensionOnlyCount = tensionCount, GroupName = groupName, ObjectNames = objects, AppliedTopChordLoads = appliedTopChordLoads, Warnings = warnings
                };
            }
            finally { if (originalUnits != null) TryRestorePresentUnits(sapModel, originalUnits.Value); }
        }
        catch (Exception ex) { return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, AppliedTopChordLoads = appliedTopChordLoads, Warnings = warnings }; }
    }

    public CityOfTomorrowDrawResult UpdateCityOfTomorrowLoads(CityOfTomorrowLoadUpdateRequest request)
    {
        var warnings = new List<string>();
        var appliedTopChordLoads = new List<CityAppliedTopChordLoad>();
        try
        {
            CityOfTomorrowModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No City of Tomorrow geometry was provided.");

            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(GetEtabsObject(request.EtabsInstanceId));
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            bool refreshViewAfterUpdate = false;
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCityTopChordLoadPattern(sapModel, model, warnings);

                int existing = GetCityAssignments(sapModel, model.GroupName).Count;
                if (existing == 0)
                    throw new InvalidOperationException($"No generated City of Tomorrow objects were found in group '{model.GroupName}'. Generate the structure before updating loads.");

                List<string> topChordFrames = GetCityTopChordFrameNamesFromEtabs(sapModel, model, warnings);
                if (topChordFrames.Count == 0)
                    throw new InvalidOperationException($"No City of Tomorrow top-chord frame targets were found in group '{model.GroupName}'. Regenerate the structure, then update loads again.");

                List<string> topChordJoints = GetCityTopChordJointPointNamesFromEtabs(sapModel, topChordFrames, warnings);
                TryApplyCityTopChordLoadsToEtabs(sapModel, model.Input, topChordFrames, topChordJoints, warnings);
                appliedTopChordLoads = ReadCityTopChordLoads(sapModel, topChordFrames, topChordJoints, warnings);
                refreshViewAfterUpdate = true;

                return new CityOfTomorrowDrawResult
                {
                    Message = BuildCityTopChordLoadUpdateMessage(model),
                    FrameCount = topChordFrames.Count,
                    GroupName = model.GroupName,
                    ObjectNames = topChordFrames,
                    AppliedTopChordLoads = appliedTopChordLoads,
                    Warnings = warnings
                };
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
                if (refreshViewAfterUpdate)
                    TryRefreshEtabsView(sapModel);
            }
        }
        catch (Exception ex)
        {
            return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, AppliedTopChordLoads = appliedTopChordLoads, Warnings = warnings };
        }
    }

    private static void ValidateCityTopChordLoadPattern(ETABSv1.cSapModel sapModel, CityOfTomorrowModel model, List<string> warnings)
    {
        CityOfTomorrowInput input = model.Input;
        if (input.TopChordLoadType == CityTopChordLoadType.None)
            return;

        string loadPattern = (input.TopChordLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
            throw new InvalidOperationException("Select an ETABS load pattern before applying a City of Tomorrow top-chord load.");

        if (input.TopChordLoadType == CityTopChordLoadType.Udl &&
            (!double.IsFinite(input.TopChordUdlKnPerM) || input.TopChordUdlKnPerM <= CityCoordinateTolerance))
        {
            throw new InvalidOperationException("City of Tomorrow top-chord UDL must be greater than zero.");
        }

        if (input.TopChordLoadType == CityTopChordLoadType.PointLoadAtJoints &&
            (!double.IsFinite(input.TopChordPointLoadKn) || input.TopChordPointLoadKn <= CityCoordinateTolerance))
        {
            throw new InvalidOperationException("City of Tomorrow top-chord point load per joint must be greater than zero.");
        }

        HashSet<string> availableLoadPatterns = GetLoadPatternNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (availableLoadPatterns.Count > 0 && !availableLoadPatterns.Contains(loadPattern))
            throw new InvalidOperationException($"City of Tomorrow generation stopped because load pattern '{loadPattern}' does not exist in ETABS.");
    }

    private static string BuildCityTopChordLoadUpdateMessage(CityOfTomorrowModel model)
    {
        CityOfTomorrowInput input = model.Input;
        return input.TopChordLoadType switch
        {
            CityTopChordLoadType.Udl => $"Updated City of Tomorrow top-chord loading in group '{model.GroupName}' to {input.TopChordUdlKnPerM:0.###} kN/m downward UDL.",
            CityTopChordLoadType.PointLoadAtJoints => $"Updated City of Tomorrow top-chord loading in group '{model.GroupName}' to {input.TopChordPointLoadKn:0.###} kN downward point load at each top-chord joint.",
            _ => $"No City of Tomorrow top-chord load type was selected; existing loading in group '{model.GroupName}' was not changed."
        };
    }

    private static List<string> GetCityTopChordFrameNames(
        CityOfTomorrowModel model,
        Dictionary<string, string> frameMap,
        List<string> warnings)
    {
        var frameNames = new List<string>();
        foreach (CityMember member in model.Members.Where(IsCityTopChordMember))
        {
            if (!frameMap.TryGetValue(member.Id, out string? frameName) || string.IsNullOrWhiteSpace(frameName))
            {
                warnings.Add($"Skipped City of Tomorrow top-chord load target '{member.Id}': ETABS frame name was not found.");
                continue;
            }

            frameNames.Add(frameName);
        }

        return frameNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCityTopChordJointPointNames(
        CityOfTomorrowModel model,
        Dictionary<string, string> pointMap,
        List<string> warnings)
    {
        var pointNames = new List<string>();
        foreach (string nodeKey in model.Members
            .Where(IsCityTopChordMember)
            .SelectMany(member => new[] { member.StartNodeKey, member.EndNodeKey })
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!pointMap.TryGetValue(nodeKey, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
            {
                warnings.Add($"Skipped City of Tomorrow top-chord joint load target '{nodeKey}': ETABS point name was not found.");
                continue;
            }

            pointNames.Add(pointName);
        }

        return pointNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCityTopChordFrameNamesFromEtabs(ETABSv1.cSapModel sapModel, CityOfTomorrowModel model, List<string> warnings)
    {
        List<string> generatedFrames = GetCityAssignments(sapModel, model.GroupName)
            .Where(assignment => assignment.Type == EtabsSelectedFrameObjectType)
            .Select(assignment => assignment.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (generatedFrames.Count == 0)
        {
            warnings.Add($"No ETABS frame objects were found in City of Tomorrow group '{model.GroupName}'.");
            return [];
        }

        HashSet<string> expectedNames = model.Members
            .Where(IsCityTopChordMember)
            .Select(member => EtabsNameUtility.BuildSafeName("", member.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var topChordFrames = generatedFrames
            .Where(frameName => expectedNames.Contains(frameName))
            .ToList();

        List<CityTopChordSegment> targetSegments = BuildCityTopChordSegments(model, warnings);
        foreach (string frameName in generatedFrames.Except(topChordFrames, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (IsCityFrameMatchingAnySegment(sapModel, frameName, targetSegments))
                    topChordFrames.Add(frameName);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not inspect City of Tomorrow frame '{frameName}' while resolving top-chord load targets: {ex.Message}");
            }
        }

        if (topChordFrames.Count == 0)
            warnings.Add("No City of Tomorrow top-chord frame objects were resolved from the generated group.");

        return topChordFrames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCityTopChordJointPointNamesFromEtabs(ETABSv1.cSapModel sapModel, IReadOnlyList<string> topChordFrameNames, List<string> warnings)
    {
        var pointNames = new List<string>();
        foreach (string frameName in topChordFrameNames)
        {
            try
            {
                string pointI = "";
                string pointJ = "";
                int ret = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
                if (ret != 0)
                {
                    warnings.Add($"Could not read end points for City of Tomorrow top-chord frame '{frameName}'. Return code: {ret}.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(pointI))
                    pointNames.Add(pointI);
                if (!string.IsNullOrWhiteSpace(pointJ))
                    pointNames.Add(pointJ);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not inspect City of Tomorrow top-chord frame '{frameName}' end points: {ex.Message}");
            }
        }

        return pointNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<CityTopChordSegment> BuildCityTopChordSegments(CityOfTomorrowModel model, List<string> warnings)
    {
        Dictionary<string, CityNode> nodes = model.Nodes.ToDictionary(node => node.Key, StringComparer.OrdinalIgnoreCase);
        var segments = new List<CityTopChordSegment>();
        foreach (CityMember member in model.Members.Where(IsCityTopChordMember))
        {
            if (!nodes.TryGetValue(member.StartNodeKey, out CityNode? start) ||
                !nodes.TryGetValue(member.EndNodeKey, out CityNode? end))
            {
                warnings.Add($"City of Tomorrow top-chord member '{member.Id}' references missing node(s).");
                continue;
            }

            segments.Add(new CityTopChordSegment(ToCityPoint(start), ToCityPoint(end)));
        }

        return segments;
    }

    private static bool IsCityFrameMatchingAnySegment(ETABSv1.cSapModel sapModel, string frameName, IReadOnlyList<CityTopChordSegment> segments)
    {
        if (segments.Count == 0)
            return false;

        string pointI = "";
        string pointJ = "";
        int ret = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
        if (ret != 0 || string.IsNullOrWhiteSpace(pointI) || string.IsNullOrWhiteSpace(pointJ))
            return false;

        CityPoint i = ToCityPoint(GetPointCoordinates(sapModel, pointI));
        CityPoint j = ToCityPoint(GetPointCoordinates(sapModel, pointJ));
        return segments.Any(segment =>
            (CityPointsMatch(i, segment.Start) && CityPointsMatch(j, segment.End)) ||
            (CityPointsMatch(i, segment.End) && CityPointsMatch(j, segment.Start)));
    }

    private static void TryApplyCityTopChordLoadsToEtabs(
        ETABSv1.cSapModel sapModel,
        CityOfTomorrowInput input,
        IReadOnlyList<string> topChordFrameNames,
        IReadOnlyList<string> topChordJointPointNames,
        List<string> warnings)
    {
        if (input.TopChordLoadType == CityTopChordLoadType.None)
            return;

        string loadPattern = (input.TopChordLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
        {
            warnings.Add("Skipped City of Tomorrow top-chord load: no load pattern was selected.");
            return;
        }

        TryClearCityTopChordLoads(sapModel, topChordFrameNames, topChordJointPointNames, loadPattern, warnings);

        if (input.TopChordLoadType == CityTopChordLoadType.Udl)
        {
            TryApplyCityTopChordUdl(sapModel, topChordFrameNames, loadPattern, -Math.Abs(input.TopChordUdlKnPerM), warnings);
            return;
        }

        if (input.TopChordLoadType == CityTopChordLoadType.PointLoadAtJoints)
            TryApplyCityTopChordJointPointLoads(sapModel, topChordJointPointNames, loadPattern, -Math.Abs(input.TopChordPointLoadKn), warnings);
    }

    private static void TryClearCityTopChordLoads(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> topChordFrameNames,
        IReadOnlyList<string> topChordJointPointNames,
        string loadPattern,
        List<string> warnings)
    {
        foreach (string frameName in topChordFrameNames)
        {
            TryClearFrameDistributedLoads(sapModel, frameName, warnings, loadPattern);
            TryClearFramePointLoads(sapModel, frameName, warnings, loadPattern);
        }

        foreach (string pointName in topChordJointPointNames)
            TryClearPointForceLoads(sapModel, pointName, warnings, loadPattern);
    }

    private static void TryApplyCityTopChordUdl(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> topChordFrameNames,
        string loadPattern,
        double loadKnPerM,
        List<string> warnings)
    {
        if (topChordFrameNames.Count == 0)
        {
            warnings.Add("Skipped City of Tomorrow top-chord UDL: no top-chord frame targets were found.");
            return;
        }

        int direction = ToEtabsDistributedLoadDirection("GlobalZ");
        foreach (string frameName in topChordFrameNames)
        {
            try
            {
                int ret = sapModel.FrameObj.SetLoadDistributed(
                    frameName,
                    loadPattern,
                    1,
                    direction,
                    0,
                    1,
                    loadKnPerM,
                    loadKnPerM,
                    "Global",
                    true,
                    true,
                    EtabsObjects);

                if (ret != 0)
                    warnings.Add($"ETABS could not assign City of Tomorrow top-chord UDL to frame '{frameName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"City of Tomorrow top-chord UDL assignment failed on frame '{frameName}': {ex.Message}");
            }
        }
    }

    private static void TryApplyCityTopChordJointPointLoads(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> topChordJointPointNames,
        string loadPattern,
        double loadKn,
        List<string> warnings)
    {
        if (topChordJointPointNames.Count == 0)
        {
            warnings.Add("Skipped City of Tomorrow top-chord point load: no top-chord joint targets were found.");
            return;
        }

        foreach (string pointName in topChordJointPointNames)
        {
            double[] values = [0, 0, loadKn, 0, 0, 0];
            try
            {
                int ret = sapModel.PointObj.SetLoadForce(pointName, loadPattern, ref values, true, "Global", EtabsObjects);
                if (ret != 0)
                    warnings.Add($"ETABS could not assign City of Tomorrow top-chord point load to point '{pointName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"City of Tomorrow top-chord point load assignment failed at point '{pointName}': {ex.Message}");
            }
        }
    }

    private static List<CityAppliedTopChordLoad> ReadCityTopChordLoads(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> topChordFrameNames,
        IReadOnlyList<string> topChordJointPointNames,
        List<string> warnings)
    {
        var seeds = new List<CityAppliedTopChordLoadSeed>();

        foreach (string frameName in topChordFrameNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryAppendCityTopChordDistributedLoads(sapModel, frameName, seeds, warnings);
        }

        foreach (string pointName in topChordJointPointNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryAppendCityTopChordPointLoads(sapModel, pointName, seeds, warnings);
        }

        return seeds
            .Where(seed => !string.IsNullOrWhiteSpace(seed.LoadPattern))
            .GroupBy(seed => new
            {
                Pattern = seed.LoadPattern.Trim().ToUpperInvariant(),
                seed.LoadType,
                seed.ValueText,
                seed.TargetSingular,
                seed.TargetPlural
            })
            .OrderBy(group => group.First().LoadPattern, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.First().LoadType, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                CityAppliedTopChordLoadSeed first = group.First();
                int count = group.Count();
                return new CityAppliedTopChordLoad
                {
                    LoadPattern = first.LoadPattern,
                    LoadType = first.LoadType,
                    ValueText = first.ValueText,
                    TargetText = $"{count} {(count == 1 ? first.TargetSingular : first.TargetPlural)}"
                };
            })
            .ToList();
    }

    private static void TryAppendCityTopChordDistributedLoads(
        ETABSv1.cSapModel sapModel,
        string frameName,
        List<CityAppliedTopChordLoadSeed> loads,
        List<string> warnings)
    {
        try
        {
            int numberItems = 0;
            string[] frameNames = [];
            string[] loadPatterns = [];
            int[] loadTypes = [];
            string[] coordinateSystems = [];
            int[] directions = [];
            double[] relativeDistance1 = [];
            double[] relativeDistance2 = [];
            double[] distance1 = [];
            double[] distance2 = [];
            double[] value1 = [];
            double[] value2 = [];

            int ret = sapModel.FrameObj.GetLoadDistributed(
                frameName,
                ref numberItems,
                ref frameNames,
                ref loadPatterns,
                ref loadTypes,
                ref coordinateSystems,
                ref directions,
                ref relativeDistance1,
                ref relativeDistance2,
                ref distance1,
                ref distance2,
                ref value1,
                ref value2,
                EtabsObjects);

            if (ret != 0 || loadPatterns.Length == 0)
                return;

            int count = Math.Min(numberItems, loadPatterns.Length);
            for (int index = 0; index < count; index++)
            {
                if (index < loadTypes.Length && loadTypes[index] != 1)
                    continue;

                double startValue = index < value1.Length ? value1[index] : 0;
                double endValue = index < value2.Length ? value2[index] : startValue;
                if (Math.Abs(startValue) <= CityCoordinateTolerance && Math.Abs(endValue) <= CityCoordinateTolerance)
                    continue;

                int direction = index < directions.Length ? directions[index] : 0;
                loads.Add(new CityAppliedTopChordLoadSeed(
                    loadPatterns[index],
                    "UDL",
                    FormatCityDistributedLoadValue(startValue, endValue, direction),
                    "top segment",
                    "top segments"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"City of Tomorrow top-chord distributed loads could not be read on frame '{frameName}': {ex.Message}");
        }
    }

    private static void TryAppendCityTopChordPointLoads(
        ETABSv1.cSapModel sapModel,
        string pointName,
        List<CityAppliedTopChordLoadSeed> loads,
        List<string> warnings)
    {
        try
        {
            int numberItems = 0;
            string[] pointNames = [];
            string[] loadPatterns = [];
            int[] stepTypes = [];
            string[] coordinateSystems = [];
            double[] f1 = [];
            double[] f2 = [];
            double[] f3 = [];
            double[] m1 = [];
            double[] m2 = [];
            double[] m3 = [];

            int ret = sapModel.PointObj.GetLoadForce(
                pointName,
                ref numberItems,
                ref pointNames,
                ref loadPatterns,
                ref stepTypes,
                ref coordinateSystems,
                ref f1,
                ref f2,
                ref f3,
                ref m1,
                ref m2,
                ref m3,
                EtabsObjects);

            if (ret != 0 || loadPatterns.Length == 0)
                return;

            int count = Math.Min(numberItems, loadPatterns.Length);
            for (int index = 0; index < count; index++)
            {
                double verticalLoad = index < f3.Length ? f3[index] : 0;
                if (Math.Abs(verticalLoad) <= CityCoordinateTolerance)
                    continue;

                loads.Add(new CityAppliedTopChordLoadSeed(
                    loadPatterns[index],
                    "Point",
                    FormatCitySignedVerticalLoadValue(verticalLoad, "kN"),
                    "joint",
                    "joints"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"City of Tomorrow top-chord point loads could not be read at point '{pointName}': {ex.Message}");
        }
    }

    private static string FormatCityDistributedLoadValue(double startValue, double endValue, int direction)
    {
        if (IsCityVerticalDistributedDirection(direction))
        {
            bool positiveIsDown = IsCityGravityPositiveDirection(direction);
            if (Math.Abs(startValue - endValue) <= CityCoordinateTolerance)
                return FormatCitySignedVerticalLoadValue(startValue, "kN/m", positiveIsDown);

            return $"{FormatCitySignedVerticalLoadValue(startValue, "kN/m", positiveIsDown)} to {FormatCitySignedVerticalLoadValue(endValue, "kN/m", positiveIsDown)}";
        }

        string directionText = direction switch
        {
            4 => "Global X",
            5 => "Global Y",
            7 => "Projected Global X",
            8 => "Projected Global Y",
            _ => $"Direction {direction}"
        };

        if (Math.Abs(startValue - endValue) <= CityCoordinateTolerance)
            return $"{startValue:0.###} kN/m ({directionText})";

        return $"{startValue:0.###} to {endValue:0.###} kN/m ({directionText})";
    }

    private static bool IsCityVerticalDistributedDirection(int direction) => direction is 6 or 9 or 10 or 11;

    private static bool IsCityGravityPositiveDirection(int direction) => direction is 10 or 11;

    private static string FormatCitySignedVerticalLoadValue(double value, string unit, bool positiveIsDown = false)
    {
        string sense = positiveIsDown
            ? value > CityCoordinateTolerance ? "down" : "up"
            : value < -CityCoordinateTolerance ? "down" : "up";
        return $"{Math.Abs(value):0.###} {unit} {sense}";
    }

    private static bool IsCityTopChordMember(CityMember member) =>
        string.Equals(member.Group, CityMemberGroups.TopChord, StringComparison.OrdinalIgnoreCase);

    private static void TryApplyCityReleasePreset(ETABSv1.cSapModel sapModel, string frameName, CityMember member, List<string> warnings)
    {
        if (member.ReleasePreset == CityMemberReleasePreset.FullyContinuous)
            return;

        TryAssignTrussReleases(sapModel, frameName, member.Id, warnings);
    }

    private static CityPoint ToCityPoint(CityNode node) => new(node.X, node.Y, node.Z);

    private static CityPoint ToCityPoint((double X, double Y, double Z) point) => new(point.X, point.Y, point.Z);

    private static bool CityPointsMatch(CityPoint first, CityPoint second) =>
        Math.Abs(first.X - second.X) <= CityCoordinateTolerance &&
        Math.Abs(first.Y - second.Y) <= CityCoordinateTolerance &&
        Math.Abs(first.Z - second.Z) <= CityCoordinateTolerance;

    private sealed record CityPoint(double X, double Y, double Z);

    private sealed record CityTopChordSegment(CityPoint Start, CityPoint End);

    private sealed record CityAppliedTopChordLoadSeed(
        string LoadPattern,
        string LoadType,
        string ValueText,
        string TargetSingular,
        string TargetPlural);

    public CityOfTomorrowDrawResult ClearCityOfTomorrow(CityOfTomorrowClearRequest request)
    {
        var warnings = new List<string>();
        try
        {
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(GetEtabsObject(request.EtabsInstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);
            int deleted = DeleteCityObjects(sapModel, request.GroupName, warnings);
            try { sapModel.GroupDef.Delete(request.GroupName); } catch { }
            try { CityOfTomorrowManifestRepository.Delete(request.StructureId); } catch (Exception ex) { warnings.Add("Manifest cleanup failed: " + ex.Message); }
            TryRefreshEtabsView(sapModel);
            return new CityOfTomorrowDrawResult { Message = deleted == 0 ? $"No generated objects found in '{request.GroupName}'." : $"Cleared {deleted} generated object(s) from '{request.GroupName}'.", GroupName = request.GroupName, Warnings = warnings };
        }
        catch (Exception ex) { return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, Warnings = warnings }; }
    }

    private static List<(int Type, string Name)> GetCityAssignments(ETABSv1.cSapModel sapModel, string groupName)
    {
        int count = 0; int[] types = []; string[] names = [];
        if (sapModel.GroupDef.GetAssignments(groupName, ref count, ref types, ref names) != 0) return [];
        return Enumerable.Range(0, Math.Min(count, Math.Min(types.Length, names.Length))).Select(i => (types[i], names[i])).Where(x => !string.IsNullOrWhiteSpace(x.Item2)).ToList();
    }

    private static int DeleteCityObjects(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        var assignments = GetCityAssignments(sapModel, groupName);
        int deleted = 0;
        foreach (string frame in assignments.Where(x => x.Type == EtabsSelectedFrameObjectType).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int ret = sapModel.FrameObj.Delete(frame, EtabsObjects);
            if (ret == 0) deleted++; else warnings.Add($"Could not delete generated frame '{frame}'. Return code: {ret}.");
        }
        foreach (string point in assignments.Where(x => x.Type == EtabsSelectedPointObjectType).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                TrySetPointRestraint(sapModel, point, [false, false, false, false, false, false], $"generated point '{point}'", warnings);
                if (sapModel.PointObj.DeleteSpecialPoint(point, EtabsObjects) == 0) deleted++;
            }
            catch { }
        }
        return deleted;
    }
}
