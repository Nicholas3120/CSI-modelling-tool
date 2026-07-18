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

        try
        {
            CotArchModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No CoT Arch geometry was provided.");

            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(GetEtabsObject(request.EtabsInstanceId));
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCotArchFrameSections(sapModel, model, warnings);

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
                    ApplyCotArchReleasePreset(sapModel, frameName, member);
                }

                ApplyCotArchRestraints(sapModel, model, pointMap);
                TryRefreshEtabsView(sapModel);

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
                    Message = $"Drawn {createdFrames.Count} CoT Arch frame object(s) in group '{mainGroup}'.",
                    FrameCount = createdFrames.Count,
                    GroupName = mainGroup,
                    FrameObjectNames = createdFrames,
                    PointObjectNames = pointMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Warnings = warnings
                };
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
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

    private static void ApplyCotArchReleasePreset(ETABSv1.cSapModel sapModel, string frameName, CotArchMember member)
    {
        if (member.ReleasePreset == CotArchMemberReleasePreset.FullyContinuous)
            return;

        bool[] startReleases = [false, false, false, true, true, true];
        bool[] endReleases = [false, false, false, true, true, true];
        double[] startSpring = [0, 0, 0, 0, 0, 0];
        double[] endSpring = [0, 0, 0, 0, 0, 0];
        int ret = sapModel.FrameObj.SetReleases(frameName, ref startReleases, ref endReleases, ref startSpring, ref endSpring, EtabsObjects);
        CheckCotArchEtabs(ret, $"Assign {member.ReleasePreset} releases to CoT Arch frame {member.Id}");
    }

    private static void ApplyCotArchRestraints(ETABSv1.cSapModel sapModel, CotArchModel model, Dictionary<string, string> pointMap)
    {
        if (model.Input.GenerateAsPlanarModel)
        {
            foreach (string pointName in pointMap.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                SetCotArchPointRestraint(sapModel, pointName, [false, true, false, true, false, true], $"planar CoT Arch joint '{pointName}'");
        }

        if (model.LeftBase == null || model.RightBase == null)
            return;

        bool[] baseRestraints = BuildCotArchBaseRestraints(model.Input.SupportCondition, model.Input.GenerateAsPlanarModel);
        if (pointMap.TryGetValue(model.LeftBase.Id, out string? leftBasePoint))
            SetCotArchPointRestraint(sapModel, leftBasePoint, baseRestraints, "left CoT Arch base support");
        if (pointMap.TryGetValue(model.RightBase.Id, out string? rightBasePoint))
            SetCotArchPointRestraint(sapModel, rightBasePoint, baseRestraints, "right CoT Arch base support");
    }

    private static bool[] BuildCotArchBaseRestraints(CotArchSupportCondition supportCondition, bool planar)
    {
        if (supportCondition == CotArchSupportCondition.Fixed)
            return [true, true, true, true, true, true];

        return planar
            ? [true, true, true, true, false, true]
            : [true, true, true, false, false, false];
    }

    private static void SetCotArchPointRestraint(ETABSv1.cSapModel sapModel, string pointName, bool[] restraints, string description)
    {
        int ret = sapModel.PointObj.SetRestraint(pointName, ref restraints, EtabsObjects);
        CheckCotArchEtabs(ret, $"Set restraint for {description}");
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
