using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed partial class EtabsParametricModellingService
{
    private const double CotArchCoordinateTolerance = 0.000001;

    public CotArchDrawResult DrawCotArch(CotArchDrawRequest request)
    {
        var warnings = new List<string>();
        var createdFrames = new List<string>();
        var createdPoints = new List<string>();
        var reusedPoints = new List<string>();
        var frameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var appliedUpperBeamLoads = new List<CotArchAppliedUpperBeamLoad>();

        try
        {
            CotArchModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No CoT Arch geometry was provided.");

            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(GetEtabsObject(request.EtabsInstanceId));
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            bool refreshViewAfterDraw = false;

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCotArchFrameSections(sapModel, model, warnings);
                ValidateCotArchLoadPattern(sapModel, model, warnings);

                string mainGroup = EnsureEtabsDrawGroup(sapModel, model.GroupName, warnings);
                Dictionary<CotArchMemberKind, string> memberGroups = EnsureCotArchGroups(sapModel, model, warnings);

                int existing = GetCotArchGroupAssignmentCount(sapModel, mainGroup);
                if (!request.ReplaceExistingStructure && existing > 0)
                    throw new InvalidOperationException($"Group '{mainGroup}' already contains {existing} object(s). Use Regenerate ETABS Structure or change the model prefix.");
                if (request.ReplaceExistingStructure)
                    DeleteCotArchObjects(sapModel, mainGroup, warnings);

                HashSet<string> preExistingPoints = ReadEtabsPointNames(sapModel, warnings);
                var pointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (CotArchNode node in model.Nodes)
                {
                    string pointName = CreateCotArchPoint(sapModel, node, preExistingPoints, createdPoints, reusedPoints, warnings);
                    pointMap[node.Id] = pointName;
                    AssignCotArchPointToGroup(sapModel, pointName, mainGroup, node.Id);
                    AssignCotArchPointToGroup(sapModel, pointName, model.PointGroupName, node.Id);
                }

                foreach (CotArchMember member in model.Members)
                {
                    if (!pointMap.TryGetValue(member.StartNodeId, out string? startPoint) ||
                        !pointMap.TryGetValue(member.EndNodeId, out string? endPoint))
                    {
                        throw new InvalidOperationException($"CoT Arch member '{member.Id}' references missing point object(s).");
                    }

                    string frameName = CreateCotArchFrame(sapModel, member, startPoint, endPoint, warnings);
                    createdFrames.Add(frameName);
                    frameMap[member.Id] = frameName;
                    AssignCotArchFrameToGroup(sapModel, frameName, mainGroup, member.Id);
                    AssignCotArchFrameToGroup(sapModel, frameName, memberGroups[member.Kind], member.Id);
                    TryApplyCotArchReleasePreset(sapModel, frameName, member, warnings);
                    if (member.Kind == CotArchMemberKind.TensionTie)
                        TryAssignCotArchTensionOnlyLimit(sapModel, frameName, member.Id, warnings);
                }

                ApplyCotArchRestraints(sapModel, model, pointMap);
                List<string> upperBeamFrames = GetCotArchUpperBeamFrameNames(model, frameMap, warnings);
                List<string> upperJointPoints = GetCotArchUpperJointPointNames(model, pointMap, warnings);
                TryApplyCotArchLoadsToEtabs(sapModel, model.Input, upperBeamFrames, upperJointPoints, warnings);
                appliedUpperBeamLoads = ReadCotArchUpperBeamLoads(sapModel, upperBeamFrames, upperJointPoints, warnings);
                refreshViewAfterDraw = createdFrames.Count > 0 || pointMap.Count > 0;

                try
                {
                    CotArchManifestRepository.Save(new CotArchGenerationManifest
                    {
                        ModelPrefix = model.ModelPrefix,
                        GenerationId = Guid.NewGuid(),
                        GeneratedAtUtc = DateTime.UtcNow,
                        EtabsGroupName = mainGroup,
                        EtabsGroupNames = CotArchAllGroupNames(model).ToList(),
                        InputSnapshot = model.Input,
                        CreatedPointNames = createdPoints.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        ReusedPointNames = reusedPoints.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        CreatedFrameNames = createdFrames.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add("Geometry was drawn, but the local CoT Arch manifest could not be saved: " + ex.Message + " Regenerate/Clear can still use the CoT Arch ETABS subgroups as a recovery fallback.");
                }

                ValidateWrittenCotArch(sapModel, model, pointMap, frameMap, warnings);

                return new CotArchDrawResult
                {
                    Message = BuildCotArchDrawMessage(model, createdFrames.Count, mainGroup),
                    FrameCount = createdFrames.Count,
                    GroupName = mainGroup,
                    FrameObjectNames = createdFrames,
                    PointObjectNames = pointMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    AppliedUpperBeamLoads = appliedUpperBeamLoads,
                    Warnings = warnings
                };
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
                if (refreshViewAfterDraw)
                    TryRefreshCotArchViewAfterDraw(sapModel);
            }
        }
        catch (Exception ex)
        {
            return new CotArchDrawResult
            {
                IsError = true,
                Message = ex.Message,
                FrameCount = createdFrames.Count,
                FrameObjectNames = createdFrames,
                PointObjectNames = createdPoints.Concat(reusedPoints).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                AppliedUpperBeamLoads = appliedUpperBeamLoads,
                Warnings = warnings
            };
        }
    }

    public CotArchDrawResult UpdateCotArchLoads(CotArchLoadUpdateRequest request)
    {
        var warnings = new List<string>();
        var appliedUpperBeamLoads = new List<CotArchAppliedUpperBeamLoad>();
        try
        {
            CotArchModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No CoT Arch geometry was provided.");

            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(GetEtabsObject(request.EtabsInstanceId));
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            bool refreshViewAfterUpdate = false;

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCotArchLoadPattern(sapModel, model, warnings);

                int existing = GetCotArchGroupAssignmentCount(sapModel, model.GroupName);
                if (existing == 0)
                    throw new InvalidOperationException($"No generated CoT Arch objects were found in group '{model.GroupName}'. Generate the structure before updating loads.");

                List<string> upperBeamFrames = GetCotArchUpperBeamFrameNamesFromEtabs(sapModel, model, warnings);
                List<string> upperJointPoints = GetCotArchUpperJointPointNamesFromEtabs(sapModel, model, warnings);
                if (upperBeamFrames.Count == 0 && upperJointPoints.Count == 0)
                    throw new InvalidOperationException($"No CoT Arch upper-beam load targets were found in group '{model.GroupName}'. Regenerate the structure, then update loads again.");

                TryApplyCotArchLoadsToEtabs(sapModel, model.Input, upperBeamFrames, upperJointPoints, warnings);
                appliedUpperBeamLoads = ReadCotArchUpperBeamLoads(sapModel, upperBeamFrames, upperJointPoints, warnings);
                refreshViewAfterUpdate = true;

                return new CotArchDrawResult
                {
                    Message = BuildCotArchLoadUpdateMessage(model),
                    FrameCount = upperBeamFrames.Count,
                    GroupName = model.GroupName,
                    FrameObjectNames = upperBeamFrames,
                    PointObjectNames = upperJointPoints,
                    AppliedUpperBeamLoads = appliedUpperBeamLoads,
                    Warnings = warnings
                };
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
                if (refreshViewAfterUpdate)
                    TryRefreshCotArchViewAfterDraw(sapModel);
            }
        }
        catch (Exception ex)
        {
            return new CotArchDrawResult
            {
                IsError = true,
                Message = ex.Message,
                AppliedUpperBeamLoads = appliedUpperBeamLoads,
                Warnings = warnings
            };
        }
    }

    public CotArchDrawResult ClearCotArch(CotArchClearRequest request)
    {
        var warnings = new List<string>();
        try
        {
            string prefix = EtabsNameUtility.BuildSafeName("", request.ModelPrefix, 24);
            string groupName = string.IsNullOrWhiteSpace(request.GroupName)
                ? BuildCotArchGroupNames(prefix).Main
                : EtabsNameUtility.BuildSafeName("", request.GroupName);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(GetEtabsObject(request.EtabsInstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);

            int deleted = DeleteCotArchObjects(sapModel, groupName, warnings);
            CotArchGroupNameSet groupNames = BuildCotArchGroupNames(prefix);
            foreach (string generatedGroup in groupNames.All.OrderByDescending(name => name.Length))
            {
                try { sapModel.GroupDef.Delete(generatedGroup); }
                catch { }
            }
            try { CotArchManifestRepository.Delete(prefix); }
            catch (Exception ex) { warnings.Add("Manifest cleanup failed: " + ex.Message); }

            TryRefreshEtabsView(sapModel);
            return new CotArchDrawResult
            {
                Message = deleted == 0 ? $"No generated objects found in '{groupName}'." : $"Cleared {deleted} CoT Arch generated object(s) from '{groupName}'.",
                GroupName = groupName,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new CotArchDrawResult { IsError = true, Message = ex.Message, Warnings = warnings };
        }
    }

    private static void ValidateCotArchFrameSections(ETABSv1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        HashSet<string> availableSections = GetFrameSectionNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (availableSections.Count == 0)
            throw new InvalidOperationException("No ETABS frame sections were available. Define or read ETABS frame sections before generating CoT Arch.");

        List<string> missing = model.Members
            .Select(member => member.SectionName?.Trim() ?? "")
            .Where(section => section.Length == 0 || !availableSections.Contains(section))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException("CoT Arch generation stopped because these selected frame sections do not exist in ETABS: " + string.Join(", ", missing.Select(section => section.Length == 0 ? "<blank>" : section)));
    }

    private static void ValidateCotArchLoadPattern(ETABSv1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        CotArchInput input = model.Input;
        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.None)
            return;

        string loadPattern = (input.UpperBeamLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
            throw new InvalidOperationException("Select an ETABS load pattern before applying a CoT Arch upper-beam load.");

        HashSet<string> availableLoadPatterns = GetLoadPatternNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (availableLoadPatterns.Count > 0 && !availableLoadPatterns.Contains(loadPattern))
            throw new InvalidOperationException($"CoT Arch generation stopped because load pattern '{loadPattern}' does not exist in ETABS.");
    }

    private static Dictionary<CotArchMemberKind, string> EnsureCotArchGroups(ETABSv1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        EnsureEtabsDrawGroup(sapModel, model.PointGroupName, warnings);
        string archGroup = EnsureEtabsDrawGroup(sapModel, model.ArchGroupName, warnings);
        string postGroup = EnsureEtabsDrawGroup(sapModel, model.PostGroupName, warnings);
        string upperBeamGroup = EnsureEtabsDrawGroup(sapModel, model.UpperBeamGroupName, warnings);
        string tieGroup = EnsureEtabsDrawGroup(sapModel, model.TieGroupName, warnings);
        string supportColumnGroup = EnsureEtabsDrawGroup(sapModel, model.SupportColumnGroupName, warnings);

        return new Dictionary<CotArchMemberKind, string>
        {
            [CotArchMemberKind.Arch] = archGroup,
            [CotArchMemberKind.VerticalPost] = postGroup,
            [CotArchMemberKind.UpperBeam] = upperBeamGroup,
            [CotArchMemberKind.TensionTie] = tieGroup,
            [CotArchMemberKind.SupportColumn] = supportColumnGroup
        };
    }

    private static IEnumerable<string> CotArchAllGroupNames(CotArchModel model)
    {
        yield return model.GroupName;
        yield return model.PointGroupName;
        yield return model.ArchGroupName;
        yield return model.PostGroupName;
        yield return model.UpperBeamGroupName;
        yield return model.TieGroupName;
        yield return model.SupportColumnGroupName;
    }

    private static string BuildCotArchDrawMessage(CotArchModel model, int frameCount, string groupName)
    {
        string message = $"Drawn {frameCount} CoT Arch frame object(s) in group '{groupName}'.";
        CotArchInput input = model.Input;
        return input.UpperBeamLoadType switch
        {
            CotArchUpperBeamLoadType.Udl => message + $" Applied {input.UpperBeamUdlKnPerM:0.###} kN/m downward UDL to the upper beam.",
            CotArchUpperBeamLoadType.PointLoadAtJoints => message + $" Applied {input.UpperBeamPointLoadKn:0.###} kN downward point load at each upper-beam joint.",
            _ => message
        };
    }

    private static string BuildCotArchLoadUpdateMessage(CotArchModel model)
    {
        CotArchInput input = model.Input;
        return input.UpperBeamLoadType switch
        {
            CotArchUpperBeamLoadType.Udl => $"Updated CoT Arch upper-beam loading in group '{model.GroupName}' to {input.UpperBeamUdlKnPerM:0.###} kN/m downward UDL.",
            CotArchUpperBeamLoadType.PointLoadAtJoints => $"Updated CoT Arch upper-beam loading in group '{model.GroupName}' to {input.UpperBeamPointLoadKn:0.###} kN downward point load at each upper-beam joint.",
            _ => $"No CoT Arch upper-beam load type was selected; existing loading in group '{model.GroupName}' was not changed."
        };
    }

    private sealed class CotArchGroupNameSet
    {
        public string Main { get; init; } = "";
        public string Points { get; init; } = "";
        public string Arch { get; init; } = "";
        public string Posts { get; init; } = "";
        public string UpperBeam { get; init; } = "";
        public string Tie { get; init; } = "";
        public string SupportColumns { get; init; } = "";
        public List<string> All => [Main, Points, Arch, Posts, UpperBeam, Tie, SupportColumns];
        public List<string> GeneratedSubgroups => [Points, Arch, Posts, UpperBeam, Tie, SupportColumns];
        public List<string> MemberSubgroups => [Arch, Posts, UpperBeam, Tie, SupportColumns];
    }

    private sealed class CotArchFallbackCleanupResult
    {
        public string MainGroupName { get; init; } = "";
        public int IdentifiedCount { get; init; }
        public int DeletedCount { get; init; }
        public List<string> GroupNames { get; init; } = [];
    }

    private static CotArchGroupNameSet BuildCotArchGroupNames(string modelPrefix)
    {
        string safePrefix = EtabsNameUtility.BuildSafeName("", modelPrefix, 24);
        string main = EtabsNameUtility.BuildSafeName("COT_ARCH_", safePrefix);
        return new CotArchGroupNameSet
        {
            Main = main,
            Points = EtabsNameUtility.BuildSafeName("", $"{main}_POINTS"),
            Arch = EtabsNameUtility.BuildSafeName("", $"{main}_ARCH"),
            Posts = EtabsNameUtility.BuildSafeName("", $"{main}_POSTS"),
            UpperBeam = EtabsNameUtility.BuildSafeName("", $"{main}_UPPER_BEAM"),
            Tie = EtabsNameUtility.BuildSafeName("", $"{main}_TIE"),
            SupportColumns = EtabsNameUtility.BuildSafeName("", $"{main}_SUPPORT_COLUMNS")
        };
    }

    private static CotArchFallbackCleanupResult DeleteCotArchObjectsFromGeneratedGroups(
        ETABSv1.cSapModel sapModel,
        CotArchGroupNameSet groupNames,
        List<string> warnings)
    {
        var frameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pointNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string groupName in groupNames.MemberSubgroups)
        {
            foreach ((int Type, string Name) assignment in GetCotArchAssignments(sapModel, groupName, warnings))
            {
                if (assignment.Type == EtabsSelectedFrameObjectType)
                    frameNames.Add(assignment.Name);
            }
        }

        foreach ((int Type, string Name) assignment in GetCotArchAssignments(sapModel, groupNames.Points, warnings))
        {
            if (assignment.Type == EtabsSelectedPointObjectType)
                pointNames.Add(assignment.Name);
        }

        int identified = frameNames.Count + pointNames.Count;
        int deleted = 0;
        foreach (string frameName in frameNames)
        {
            try
            {
                int ret = sapModel.FrameObj.Delete(frameName, EtabsObjects);
                if (ret == 0) deleted++;
                else warnings.Add($"Could not delete CoT Arch frame '{frameName}' from generated subgroup recovery. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not delete CoT Arch frame '{frameName}' from generated subgroup recovery: {ex.Message}");
            }
        }

        foreach (string pointName in pointNames)
        {
            try
            {
                TrySetPointRestraint(sapModel, pointName, BuildFreePointRestraints(), $"CoT Arch generated subgroup point '{pointName}'", warnings);
                int ret = sapModel.PointObj.DeleteSpecialPoint(pointName, EtabsObjects);
                if (ret == 0) deleted++;
                else warnings.Add($"Could not delete CoT Arch point '{pointName}' from generated subgroup recovery. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not delete CoT Arch point '{pointName}' from generated subgroup recovery: {ex.Message}");
            }
        }

        WarnForMainGroupObjectsOutsideGeneratedSubgroups(sapModel, groupNames, frameNames, pointNames, warnings);

        return new CotArchFallbackCleanupResult
        {
            MainGroupName = groupNames.Main,
            IdentifiedCount = identified,
            DeletedCount = deleted,
            GroupNames = groupNames.All
        };
    }

    private static List<(int Type, string Name)> GetCotArchAssignments(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        int count = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        try
        {
            int ret = sapModel.GroupDef.GetAssignments(groupName, ref count, ref objectTypes, ref objectNames);
            if (ret != 0)
                return [];

            int safeCount = Math.Min(count, Math.Min(objectTypes.Length, objectNames.Length));
            return Enumerable.Range(0, safeCount)
                .Where(index => !string.IsNullOrWhiteSpace(objectNames[index]))
                .Select(index => (objectTypes[index], objectNames[index]))
                .ToList();
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read CoT Arch group '{groupName}' during generated subgroup recovery: {ex.Message}");
            return [];
        }
    }

    private static void WarnForMainGroupObjectsOutsideGeneratedSubgroups(
        ETABSv1.cSapModel sapModel,
        CotArchGroupNameSet groupNames,
        HashSet<string> recoveredFrames,
        HashSet<string> recoveredPoints,
        List<string> warnings)
    {
        List<(int Type, string Name)> mainAssignments = GetCotArchAssignments(sapModel, groupNames.Main, warnings);
        int retained = mainAssignments.Count(assignment =>
            (assignment.Type == EtabsSelectedFrameObjectType && !recoveredFrames.Contains(assignment.Name)) ||
            (assignment.Type == EtabsSelectedPointObjectType && !recoveredPoints.Contains(assignment.Name)));
        if (retained > 0)
            warnings.Add($"Retained {retained} object(s) assigned to '{groupNames.Main}' because they were not also assigned to generated CoT Arch subgroups.");
    }

    private static int DeleteCotArchObjects(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        List<(int Type, string Name)> assignments = GetCotArchAssignments(sapModel, groupName, warnings);
        int deleted = 0;
        foreach (string frame in assignments
            .Where(assignment => assignment.Type == EtabsSelectedFrameObjectType)
            .Select(assignment => assignment.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int ret = sapModel.FrameObj.Delete(frame, EtabsObjects);
            if (ret == 0) deleted++;
            else warnings.Add($"Could not delete generated CoT Arch frame '{frame}'. Return code: {ret}.");
        }

        foreach (string point in assignments
            .Where(assignment => assignment.Type == EtabsSelectedPointObjectType)
            .Select(assignment => assignment.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                TrySetPointRestraint(sapModel, point, [false, false, false, false, false, false], $"generated CoT Arch point '{point}'", warnings);
                if (sapModel.PointObj.DeleteSpecialPoint(point, EtabsObjects) == 0)
                    deleted++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not delete generated CoT Arch point '{point}': {ex.Message}");
            }
        }

        return deleted;
    }

    private static int DeleteCotArchManifestObjects(ETABSv1.cSapModel sapModel, CotArchGenerationManifest manifest, List<string> warnings)
    {
        int deleted = 0;
        foreach (string frameName in manifest.CreatedFrameNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                int ret = sapModel.FrameObj.Delete(frameName, EtabsObjects);
                if (ret == 0) deleted++;
                else warnings.Add($"Could not delete CoT Arch frame '{frameName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not delete CoT Arch frame '{frameName}': {ex.Message}");
            }
        }

        foreach (string pointName in manifest.CreatedPointNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                TrySetPointRestraint(sapModel, pointName, BuildFreePointRestraints(), $"CoT Arch generated point '{pointName}'", warnings);
                int ret = sapModel.PointObj.DeleteSpecialPoint(pointName, EtabsObjects);
                if (ret == 0) deleted++;
                else warnings.Add($"Could not delete CoT Arch point '{pointName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not delete CoT Arch point '{pointName}': {ex.Message}");
            }
        }

        return deleted;
    }

    private static int GetCotArchGroupAssignmentCount(ETABSv1.cSapModel sapModel, string groupName)
    {
        int count = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        try
        {
            int ret = sapModel.GroupDef.GetAssignments(groupName, ref count, ref objectTypes, ref objectNames);
            return ret == 0 ? count : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static HashSet<string> ReadEtabsPointNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            int ret = sapModel.PointObj.GetNameList(ref numberNames, ref names);
            if (ret == 0)
                return names.Take(Math.Min(numberNames, names.Length)).Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            warnings.Add($"ETABS point list could not be loaded before CoT Arch generation. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS point list could not be loaded before CoT Arch generation: " + ex.Message);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateCotArchPoint(
        ETABSv1.cSapModel sapModel,
        CotArchNode node,
        HashSet<string> preExistingPoints,
        List<string> createdPoints,
        List<string> reusedPoints,
        List<string> warnings)
    {
        string actualPointName = "";
        string requestedName = EtabsNameUtility.BuildSafeName("", node.Id);
        int ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref actualPointName, requestedName, "Global", true, 0);
        if (ret != 0)
        {
            actualPointName = "";
            ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref actualPointName, "", "Global", true, 0);
            if (ret == 0)
                warnings.Add($"CoT Arch point '{node.Id}' was created with ETABS automatic name because preferred name '{requestedName}' was unavailable.");
        }

        CheckCotArchEtabs(ret, $"Create CoT Arch point {node.Id} at ({node.X:0.######}, {node.Y:0.######}, {node.Z:0.######})");
        if (string.IsNullOrWhiteSpace(actualPointName))
            throw new InvalidOperationException($"ETABS returned an empty point name for CoT Arch point '{node.Id}'.");

        if (preExistingPoints.Contains(actualPointName))
            reusedPoints.Add(actualPointName);
        else
            createdPoints.Add(actualPointName);

        return actualPointName;
    }

    private static string CreateCotArchFrame(ETABSv1.cSapModel sapModel, CotArchMember member, string startPoint, string endPoint, List<string> warnings)
    {
        string actualFrameName = "";
        string requestedName = EtabsNameUtility.BuildSafeName("", member.Id);
        string sectionName = (member.SectionName ?? "").Trim();
        int ret = sapModel.FrameObj.AddByPoint(startPoint, endPoint, ref actualFrameName, sectionName, requestedName);
        if (ret != 0)
        {
            actualFrameName = "";
            ret = sapModel.FrameObj.AddByPoint(startPoint, endPoint, ref actualFrameName, sectionName, "");
            if (ret == 0)
                warnings.Add($"CoT Arch member '{member.Id}' was drawn with ETABS automatic frame name because preferred name '{requestedName}' was unavailable.");
        }

        CheckCotArchEtabs(ret, $"Create CoT Arch frame {member.Id}");
        if (string.IsNullOrWhiteSpace(actualFrameName))
            throw new InvalidOperationException($"ETABS returned an empty frame name for CoT Arch member '{member.Id}'.");

        TryAssignFrameSection(sapModel, actualFrameName, member.Id, sectionName, warnings);
        return actualFrameName;
    }

    private static void AssignCotArchPointToGroup(ETABSv1.cSapModel sapModel, string pointName, string groupName, string nodeId)
    {
        int ret = sapModel.PointObj.SetGroupAssign(pointName, groupName, false, EtabsObjects);
        CheckCotArchEtabs(ret, $"Assign CoT Arch point {nodeId} to group {groupName}");
    }

    private static void AssignCotArchFrameToGroup(ETABSv1.cSapModel sapModel, string frameName, string groupName, string memberId)
    {
        int ret = sapModel.FrameObj.SetGroupAssign(frameName, groupName, false, EtabsObjects);
        CheckCotArchEtabs(ret, $"Assign CoT Arch frame {memberId} to group {groupName}");
    }

    private static void TryApplyCotArchReleasePreset(ETABSv1.cSapModel sapModel, string frameName, CotArchMember member, List<string> warnings)
    {
        if (member.ReleasePreset == CotArchMemberReleasePreset.FullyContinuous)
            return;

        try
        {
            bool[] startReleases = [false, false, false, false, true, true];
            bool[] endReleases = [false, false, false, false, true, true];
            double[] startSpring = [0, 0, 0, 0, 0, 0];
            double[] endSpring = [0, 0, 0, 0, 0, 0];
            int ret = sapModel.FrameObj.SetReleases(frameName, ref startReleases, ref endReleases, ref startSpring, ref endSpring, EtabsObjects);
            if (ret != 0)
                warnings.Add($"CoT Arch member '{member.Id}' was drawn, but ETABS could not assign M22/M33 end releases. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"CoT Arch member '{member.Id}' was drawn, but release assignment failed: {ex.Message}");
        }
    }

    private static void TryAssignCotArchTensionOnlyLimit(ETABSv1.cSapModel sapModel, string frameName, string memberId, List<string> warnings)
    {
        try
        {
            int ret = sapModel.FrameObj.SetTCLimits(frameName, true, 0, false, 0, EtabsObjects);
            if (ret != 0)
                warnings.Add($"CoT Arch tension tie '{memberId}' was drawn, but ETABS could not assign zero compression capacity. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"CoT Arch tension tie '{memberId}' was drawn, but tension-only limit assignment failed: {ex.Message}");
        }
    }

    private static void TryApplyCotArchLoads(
        ETABSv1.cSapModel sapModel,
        CotArchModel model,
        Dictionary<string, string> pointMap,
        Dictionary<string, string> frameMap,
        List<string> warnings)
    {
        List<string> upperBeamFrames = GetCotArchUpperBeamFrameNames(model, frameMap, warnings);
        List<string> upperJointPoints = GetCotArchUpperJointPointNames(model, pointMap, warnings);
        TryApplyCotArchLoadsToEtabs(sapModel, model.Input, upperBeamFrames, upperJointPoints, warnings);
    }

    private static void TryApplyCotArchLoadsToEtabs(
        ETABSv1.cSapModel sapModel,
        CotArchInput input,
        IReadOnlyList<string> upperBeamFrameNames,
        IReadOnlyList<string> upperJointPointNames,
        List<string> warnings)
    {
        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.None)
            return;

        string loadPattern = (input.UpperBeamLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
        {
            warnings.Add("Skipped CoT Arch upper-beam load: no load pattern was selected.");
            return;
        }

        TryClearCotArchUpperBeamLoads(sapModel, upperBeamFrameNames, upperJointPointNames, loadPattern, warnings);

        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.Udl)
        {
            TryApplyCotArchUpperBeamUdl(sapModel, upperBeamFrameNames, loadPattern, -Math.Abs(input.UpperBeamUdlKnPerM), warnings);
            return;
        }

        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.PointLoadAtJoints)
            TryApplyCotArchUpperJointPointLoads(sapModel, upperJointPointNames, loadPattern, -Math.Abs(input.UpperBeamPointLoadKn), warnings);
    }

    private static List<string> GetCotArchUpperBeamFrameNames(CotArchModel model, Dictionary<string, string> frameMap, List<string> warnings)
    {
        var frameNames = new List<string>();
        foreach (CotArchMember member in model.Members.Where(member => member.Kind == CotArchMemberKind.UpperBeam))
        {
            if (!frameMap.TryGetValue(member.Id, out string? frameName) || string.IsNullOrWhiteSpace(frameName))
            {
                warnings.Add($"Skipped CoT Arch upper-beam load target '{member.Id}': ETABS frame name was not found.");
                continue;
            }

            frameNames.Add(frameName);
        }

        return frameNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCotArchUpperJointPointNames(CotArchModel model, Dictionary<string, string> pointMap, List<string> warnings)
    {
        var pointNames = new List<string>();
        foreach (CotArchNode node in model.PostTopNodes.DistinctBy(node => node.Id))
        {
            if (!pointMap.TryGetValue(node.Id, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
            {
                warnings.Add($"Skipped CoT Arch upper joint load target '{node.Id}': ETABS point name was not found.");
                continue;
            }

            pointNames.Add(pointName);
        }

        return pointNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCotArchUpperBeamFrameNamesFromEtabs(ETABSv1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        List<string> frameNames = GetCotArchAssignments(sapModel, model.UpperBeamGroupName, warnings)
            .Where(assignment => assignment.Type == EtabsSelectedFrameObjectType)
            .Select(assignment => assignment.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (frameNames.Count == 0)
            warnings.Add($"No ETABS frame objects were found in CoT Arch upper-beam group '{model.UpperBeamGroupName}'.");

        return frameNames;
    }

    private static List<string> GetCotArchUpperJointPointNamesFromEtabs(ETABSv1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        List<string> pointNames = GetCotArchAssignments(sapModel, model.PointGroupName, warnings)
            .Where(assignment => assignment.Type == EtabsSelectedPointObjectType)
            .Select(assignment => assignment.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pointNames.Count == 0)
        {
            warnings.Add($"No ETABS point objects were found in CoT Arch point group '{model.PointGroupName}'.");
            return [];
        }

        List<CotArchNode> targetNodes = model.PostTopNodes.DistinctBy(node => node.Id).ToList();
        var upperJointPointNames = new List<string>();
        foreach (string pointName in pointNames)
        {
            try
            {
                (double X, double Y, double Z) coordinates = GetPointCoordinates(sapModel, pointName);
                if (targetNodes.Any(node => IsCotArchPointAtNode(coordinates, node)))
                    upperJointPointNames.Add(pointName);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not inspect CoT Arch point '{pointName}' while resolving upper-beam load targets: {ex.Message}");
            }
        }

        if (upperJointPointNames.Count == 0)
            warnings.Add("No CoT Arch upper-beam joint points were resolved from the generated point group.");

        return upperJointPointNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsCotArchPointAtNode((double X, double Y, double Z) coordinates, CotArchNode node)
    {
        return Math.Abs(coordinates.X - node.X) <= CotArchCoordinateTolerance &&
            Math.Abs(coordinates.Y - node.Y) <= CotArchCoordinateTolerance &&
            Math.Abs(coordinates.Z - node.Z) <= CotArchCoordinateTolerance;
    }

    private static void TryClearCotArchUpperBeamLoads(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> upperBeamFrameNames,
        IReadOnlyList<string> upperJointPointNames,
        string loadPattern,
        List<string> warnings)
    {
        foreach (string frameName in upperBeamFrameNames)
        {
            TryClearFrameDistributedLoads(sapModel, frameName, warnings, loadPattern);
            TryClearFramePointLoads(sapModel, frameName, warnings, loadPattern);
        }

        foreach (string pointName in upperJointPointNames)
            TryClearPointForceLoads(sapModel, pointName, warnings, loadPattern);
    }

    private static List<CotArchAppliedUpperBeamLoad> ReadCotArchUpperBeamLoads(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> upperBeamFrameNames,
        IReadOnlyList<string> upperJointPointNames,
        List<string> warnings)
    {
        var seeds = new List<CotArchAppliedUpperBeamLoadSeed>();

        foreach (string frameName in upperBeamFrameNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryAppendCotArchUpperBeamDistributedLoads(sapModel, frameName, seeds, warnings);
        }

        foreach (string pointName in upperJointPointNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryAppendCotArchUpperJointPointLoads(sapModel, pointName, seeds, warnings);
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
                CotArchAppliedUpperBeamLoadSeed first = group.First();
                int count = group.Count();
                return new CotArchAppliedUpperBeamLoad
                {
                    LoadPattern = first.LoadPattern,
                    LoadType = first.LoadType,
                    ValueText = first.ValueText,
                    TargetText = $"{count} {(count == 1 ? first.TargetSingular : first.TargetPlural)}"
                };
            })
            .ToList();
    }

    private static void TryAppendCotArchUpperBeamDistributedLoads(
        ETABSv1.cSapModel sapModel,
        string frameName,
        List<CotArchAppliedUpperBeamLoadSeed> loads,
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
                if (Math.Abs(startValue) <= CotArchCoordinateTolerance && Math.Abs(endValue) <= CotArchCoordinateTolerance)
                    continue;

                int direction = index < directions.Length ? directions[index] : 0;
                loads.Add(new CotArchAppliedUpperBeamLoadSeed(
                    loadPatterns[index],
                    "UDL",
                    FormatCotArchDistributedLoadValue(startValue, endValue, direction),
                    "beam segment",
                    "beam segments"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"CoT Arch upper-beam distributed loads could not be read on frame '{frameName}': {ex.Message}");
        }
    }

    private static void TryAppendCotArchUpperJointPointLoads(
        ETABSv1.cSapModel sapModel,
        string pointName,
        List<CotArchAppliedUpperBeamLoadSeed> loads,
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
                if (Math.Abs(verticalLoad) <= CotArchCoordinateTolerance)
                    continue;

                loads.Add(new CotArchAppliedUpperBeamLoadSeed(
                    loadPatterns[index],
                    "Point",
                    FormatCotArchSignedVerticalLoadValue(verticalLoad, "kN"),
                    "joint",
                    "joints"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"CoT Arch upper-joint point loads could not be read at point '{pointName}': {ex.Message}");
        }
    }

    private static string FormatCotArchDistributedLoadValue(double startValue, double endValue, int direction)
    {
        if (IsCotArchVerticalDistributedDirection(direction))
        {
            if (Math.Abs(startValue - endValue) <= CotArchCoordinateTolerance)
                return FormatCotArchSignedVerticalLoadValue(startValue, "kN/m", IsCotArchGravityPositiveDirection(direction));

            return $"{FormatCotArchSignedVerticalLoadValue(startValue, "kN/m", IsCotArchGravityPositiveDirection(direction))} to {FormatCotArchSignedVerticalLoadValue(endValue, "kN/m", IsCotArchGravityPositiveDirection(direction))}";
        }

        string directionText = direction switch
        {
            4 => "Global X",
            5 => "Global Y",
            7 => "Projected Global X",
            8 => "Projected Global Y",
            _ => $"Direction {direction}"
        };

        if (Math.Abs(startValue - endValue) <= CotArchCoordinateTolerance)
            return $"{startValue:0.###} kN/m ({directionText})";

        return $"{startValue:0.###} to {endValue:0.###} kN/m ({directionText})";
    }

    private static bool IsCotArchVerticalDistributedDirection(int direction) => direction is 6 or 9 or 10 or 11;

    private static bool IsCotArchGravityPositiveDirection(int direction) => direction is 10 or 11;

    private static string FormatCotArchSignedVerticalLoadValue(double value, string unit, bool positiveIsDown = false)
    {
        string sense = positiveIsDown
            ? value > CotArchCoordinateTolerance ? "down" : "up"
            : value < -CotArchCoordinateTolerance ? "down" : "up";
        return $"{Math.Abs(value):0.###} {unit} {sense}";
    }

    private sealed record CotArchAppliedUpperBeamLoadSeed(
        string LoadPattern,
        string LoadType,
        string ValueText,
        string TargetSingular,
        string TargetPlural);

    private static void TryApplyCotArchUpperBeamUdl(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> upperBeamFrameNames,
        string loadPattern,
        double loadKnPerM,
        List<string> warnings)
    {
        if (upperBeamFrameNames.Count == 0)
        {
            warnings.Add("Skipped CoT Arch upper-beam UDL: no upper-beam frame targets were found.");
            return;
        }

        int direction = ToEtabsDistributedLoadDirection("GlobalZ");
        foreach (string frameName in upperBeamFrameNames)
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
                    warnings.Add($"ETABS could not assign CoT Arch upper-beam UDL to frame '{frameName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"CoT Arch upper-beam UDL assignment failed on frame '{frameName}': {ex.Message}");
            }
        }
    }

    private static void TryApplyCotArchUpperJointPointLoads(
        ETABSv1.cSapModel sapModel,
        IReadOnlyList<string> upperJointPointNames,
        string loadPattern,
        double loadKn,
        List<string> warnings)
    {
        if (upperJointPointNames.Count == 0)
        {
            warnings.Add("Skipped CoT Arch upper joint point load: no upper-joint point targets were found.");
            return;
        }

        foreach (string pointName in upperJointPointNames)
        {
            double[] values = [0, 0, loadKn, 0, 0, 0];
            try
            {
                int ret = sapModel.PointObj.SetLoadForce(pointName, loadPattern, ref values, true, "Global", EtabsObjects);
                if (ret != 0)
                    warnings.Add($"ETABS could not assign CoT Arch upper joint point load to point '{pointName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"CoT Arch upper joint point load assignment failed at point '{pointName}': {ex.Message}");
            }
        }
    }

    private static void ApplyCotArchRestraints(ETABSv1.cSapModel sapModel, CotArchModel model, Dictionary<string, string> pointMap)
    {
        foreach (string pointName in pointMap.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            SetCotArchPointRestraint(sapModel, pointName, BuildFreePointRestraints(), $"free CoT Arch joint '{pointName}'");

        if (model.LeftBase == null || model.RightBase == null)
            return;

        bool[] baseRestraints = BuildCotArchPinnedBaseRestraints();
        if (pointMap.TryGetValue(model.LeftBase.Id, out string? leftBasePoint))
            SetCotArchPointRestraint(sapModel, leftBasePoint, baseRestraints, "left CoT Arch base support");
        if (pointMap.TryGetValue(model.RightBase.Id, out string? rightBasePoint))
            SetCotArchPointRestraint(sapModel, rightBasePoint, baseRestraints, "right CoT Arch base support");
    }

    private static bool[] BuildCotArchPinnedBaseRestraints() => [true, true, true, false, false, false];

    private static void SetCotArchPointRestraint(ETABSv1.cSapModel sapModel, string pointName, bool[] restraints, string description)
    {
        int ret = sapModel.PointObj.SetRestraint(pointName, ref restraints, EtabsObjects);
        CheckCotArchEtabs(ret, $"Set restraint for {description}");
    }

    private static void TryRefreshCotArchViewAfterDraw(ETABSv1.cSapModel sapModel)
    {
        TryRefreshEtabsView(sapModel);
        try
        {
            sapModel.View.RefreshView(0, true);
        }
        catch
        {
            // The normal refresh above matches the other tabs; this extra pass only improves live display.
        }
    }

    private static void ValidateWrittenCotArch(
        ETABSv1.cSapModel sapModel,
        CotArchModel model,
        Dictionary<string, string> pointMap,
        Dictionary<string, string> frameMap,
        List<string> warnings)
    {
        foreach (CotArchNode node in model.Nodes)
        {
            if (!pointMap.TryGetValue(node.Id, out string? pointName))
            {
                warnings.Add($"Post-write validation could not find ETABS point for CoT Arch node '{node.Id}'.");
                continue;
            }

            try
            {
                (double X, double Y, double Z) actual = GetPointCoordinates(sapModel, pointName);
                if (Math.Abs(actual.X - node.X) > CotArchCoordinateTolerance ||
                    Math.Abs(actual.Y - node.Y) > CotArchCoordinateTolerance ||
                    Math.Abs(actual.Z - node.Z) > CotArchCoordinateTolerance)
                {
                    warnings.Add($"Post-write validation mismatch at CoT Arch node '{node.Id}' ({pointName}). Expected ({node.X:0.######}, {node.Y:0.######}, {node.Z:0.######}), got ({actual.X:0.######}, {actual.Y:0.######}, {actual.Z:0.######}).");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Post-write validation could not read CoT Arch point '{pointName}': {ex.Message}");
            }
        }

        foreach (CotArchMember member in model.Members)
        {
            if (!frameMap.TryGetValue(member.Id, out string? frameName))
            {
                warnings.Add($"Post-write validation could not find ETABS frame for CoT Arch member '{member.Id}'.");
                continue;
            }

            string pointI = "";
            string pointJ = "";
            try
            {
                int ret = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
                if (ret != 0)
                {
                    warnings.Add($"Post-write validation could not read endpoints for CoT Arch frame '{member.Id}' ({frameName}). Return code: {ret}.");
                }
                else
                {
                    string expectedI = pointMap.GetValueOrDefault(member.StartNodeId, "");
                    string expectedJ = pointMap.GetValueOrDefault(member.EndNodeId, "");
                    if (!string.Equals(pointI, expectedI, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(pointJ, expectedJ, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Post-write validation endpoint mismatch for CoT Arch frame '{member.Id}' ({frameName}). Expected {expectedI}->{expectedJ}, got {pointI}->{pointJ}.");
                    }
                }

                string sectionName = "";
                string autoSelectList = "";
                int sectionRet = sapModel.FrameObj.GetSection(frameName, ref sectionName, ref autoSelectList);
                if (sectionRet == 0 && !string.Equals(sectionName, member.SectionName, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Post-write validation section mismatch for CoT Arch frame '{member.Id}' ({frameName}). Expected '{member.SectionName}', got '{sectionName}'.");
                else if (sectionRet != 0)
                    warnings.Add($"Post-write validation could not read section for CoT Arch frame '{member.Id}' ({frameName}). Return code: {sectionRet}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Post-write validation could not read CoT Arch frame '{member.Id}' ({frameName}): {ex.Message}");
            }
        }

        if (frameMap.Count != model.Members.Count)
            warnings.Add($"Post-write validation expected {model.Members.Count} CoT Arch frame(s), but {frameMap.Count} were recorded.");
    }

    private static void EnsureCotArchModelIsUnlocked(ETABSv1.cSapModel sapModel)
    {
        bool locked;
        try
        {
            locked = sapModel.GetModelIsLocked();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Unable to check whether the selected ETABS model is locked: " + ex.Message, ex);
        }

        if (locked)
            throw new InvalidOperationException("The selected ETABS model is locked. Unlock it in ETABS, then run CoT Arch generation again.");
    }

    private static void CheckCotArchEtabs(int returnCode, string operation)
    {
        if (returnCode != 0)
            throw new InvalidOperationException($"{operation} failed. ETABS return code: {returnCode}.");
    }
}
