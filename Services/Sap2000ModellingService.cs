using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed partial class Sap2000ModellingService
{
    private const string Sap2000ApiObjectProgId = "CSI.SAP2000.API.SapObject";
    private const SAP2000v1.eUnits Sap2000UnitsKnMC = SAP2000v1.eUnits.kN_m_C;
    private const SAP2000v1.eItemType Sap2000Objects = SAP2000v1.eItemType.Objects;
    private const int Sap2000SelectedPointObjectType = 1;
    private const int Sap2000SelectedFrameObjectType = 2;
    private const double CityCoordinateTolerance = 0.000001;

    private enum Sap2000TensionObjectKind
    {
        Cable,
        Tendon
    }

    public Sap2000InstanceListResult ListSap2000Instances()
    {
        var warnings = new List<string>();
        var instances = EnumerateSap2000ProcessInstances(warnings);

        if (instances.Count == 0)
        {
            instances.AddRange(EnumerateSap2000Objects()
                .Select((item, index) => BuildSap2000Instance(item.Object, item.DisplayName, index)));
        }

        instances = instances
            .GroupBy(instance => instance.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (instances.Count == 0)
        {
            try
            {
                SAP2000v1.cOAPI sapObject = GetActiveSap2000Object();
                instances.Add(BuildSap2000Instance(sapObject, "active", 0));
            }
            catch
            {
                // The result message tells the UI that no SAP2000 API object is available.
            }
        }

        return new Sap2000InstanceListResult
        {
            Message = instances.Count == 0
                ? "No active SAP2000 API instance was found."
                : $"Found {instances.Count} SAP2000 instance option(s).",
            Instances = instances,
            Warnings = warnings
        };
    }

    public Sap2000ModelDataResult ListModelData(Sap2000ModelDataRequest request)
    {
        var warnings = new List<string>();
        Sap2000InstanceListResult instanceResult = ListSap2000Instances();
        warnings.AddRange(instanceResult.Warnings);

        List<Sap2000InstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.Sap2000InstanceId);
        var result = new Sap2000ModelDataResult
        {
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            Warnings = warnings
        };

        if (instances.Count == 0)
        {
            result.Message = instanceResult.Message;
            return result;
        }

        try
        {
            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(selectedInstanceId));
            result.Materials = GetMaterialNames(sapModel, warnings);
            result.FrameSections = GetFrameSectionNames(sapModel, warnings);
            result.CableSections = GetCableSectionNames(sapModel, warnings);
            result.TendonSections = GetTendonSectionNames(sapModel, warnings);
            result.TensionMemberSections = result.CableSections
                .Concat(result.TendonSections)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.LoadPatterns = GetLoadPatternNames(sapModel, warnings);
            result.Groups = GetGroupNames(sapModel, warnings);
            result.Message = $"Loaded {result.Materials.Count} SAP2000 material(s), {result.FrameSections.Count} frame section(s), {result.CableSections.Count} cable section(s), {result.TendonSections.Count} tendon section(s), and {result.LoadPatterns.Count} load pattern(s).";
        }
        catch (Exception ex)
        {
            result.IsError = true;
            result.Message = ex.Message;
        }

        return result;
    }

    public CityOfTomorrowDrawResult DrawCityOfTomorrow(Sap2000CityOfTomorrowDrawRequest request)
    {
        var warnings = new List<string>();
        var appliedTopChordLoads = new List<CityAppliedTopChordLoad>();
        try
        {
            CityOfTomorrowModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No City of Tomorrow geometry was provided.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCityTopChordLoadPattern(sapModel, model, warnings);
                string groupName = EnsureSap2000DrawGroup(sapModel, model.GroupName, warnings);
                int existing = GetCityAssignments(sapModel, groupName).Count;
                if (!request.ReplaceExistingStructure && existing > 0)
                    throw new InvalidOperationException($"Group '{groupName}' already contains {existing} object(s). Use Regenerate SAP2000 Structure or change Structure ID.");

                if (request.ReplaceExistingStructure)
                    DeleteCityObjects(sapModel, groupName, warnings);

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
                        warnings.Add($"SAP2000 could not create point '{node.Key}'. Return code: {ret}.");
                        continue;
                    }

                    points[node.Key] = pointName;
                    TryAssignPointToGroup(sapModel, pointName, groupName, node.Key, warnings);
                }

                var objects = new List<string>();
                var frameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int frameCount = 0;
                int cableCount = 0;
                int tendonCount = 0;
                int tensionCount = 0;
                HashSet<string> cableProperties = GetCableSectionNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> tendonProperties = GetTendonSectionNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (CityMember member in model.Members)
                {
                    if (!points.TryGetValue(member.StartNodeKey, out string? pi) ||
                        !points.TryGetValue(member.EndNodeKey, out string? pj))
                    {
                        warnings.Add($"Skipped '{member.Id}': shared endpoints were unavailable.");
                        continue;
                    }

                    if (ShouldDrawCityMemberAsTensionObject(member, cableProperties, tendonProperties))
                    {
                        if (TryAddTensionObjectByPoint(sapModel, member, pi, pj, cableProperties, tendonProperties, out string tensionName, out Sap2000TensionObjectKind tensionKind, warnings))
                        {
                            TryAssignTensionObjectToGroup(sapModel, tensionName, tensionKind, groupName, member.Id, warnings);
                            objects.Add(tensionName);
                            tensionCount++;
                            if (tensionKind == Sap2000TensionObjectKind.Tendon) tendonCount++;
                            else cableCount++;
                        }

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
                        warnings.Add($"SAP2000 could not draw '{member.Id}'. Return code: {ret}.");
                        continue;
                    }

                    TryAssignFrameSection(sapModel, frameName, member.Id, member.SectionName, warnings);
                    TryAssignFrameToGroup(sapModel, frameName, groupName, member.Id, warnings);
                    TryApplyCityReleasePreset(sapModel, frameName, member, warnings);
                    if (member.IsTensionOnly)
                    {
                        TryAssignCityTensionOnlyLimit(sapModel, frameName, member.Id, warnings);
                        tensionCount++;
                    }
                    frameMap[member.Id] = frameName;
                    objects.Add(frameName);
                    frameCount++;
                }

                foreach (CityNode support in model.Nodes.Where(n => n.IsSupport))
                {
                    if (points.TryGetValue(support.Key, out string? pointName))
                        TrySetPointRestraint(sapModel, pointName, [true, true, true, false, false, false], $"City support '{support.Key}'", warnings);
                }

                List<string> topChordFrames = GetCityTopChordFrameNames(model, frameMap, warnings);
                List<string> topChordJoints = GetCityTopChordJointPointNames(model, points, warnings);
                TryApplyCityTopChordLoadsToSap2000(sapModel, model.Input, topChordFrames, topChordJoints, warnings);
                appliedTopChordLoads = ReadCityTopChordLoads(sapModel, topChordFrames, topChordJoints, warnings);

                TryRefreshSap2000View(sapModel);
                return new CityOfTomorrowDrawResult
                {
                    IsError = objects.Count == 0,
                    Message = $"Drawn {frameCount} frame object(s), {cableCount} cable object(s), and {tendonCount} tendon object(s) in SAP2000 group '{groupName}'.",
                    FrameCount = frameCount,
                    TensionOnlyCount = tensionCount,
                    GroupName = groupName,
                    ObjectNames = objects,
                    AppliedTopChordLoads = appliedTopChordLoads,
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
            return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, AppliedTopChordLoads = appliedTopChordLoads, Warnings = warnings };
        }
    }

    public CityOfTomorrowDrawResult UpdateCityOfTomorrowLoads(Sap2000CityOfTomorrowLoadUpdateRequest request)
    {
        var warnings = new List<string>();
        var appliedTopChordLoads = new List<CityAppliedTopChordLoad>();

        try
        {
            CityOfTomorrowModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No City of Tomorrow geometry was provided.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            bool refreshViewAfterUpdate = false;

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCityTopChordLoadPattern(sapModel, model, warnings);

                int existing = GetCityAssignments(sapModel, model.GroupName).Count;
                if (existing == 0)
                    throw new InvalidOperationException($"No generated City of Tomorrow SAP2000 objects were found in group '{model.GroupName}'. Generate the structure before updating loads.");

                List<string> topChordFrames = GetCityTopChordFrameNames(sapModel, model, warnings);
                if (topChordFrames.Count == 0)
                    throw new InvalidOperationException($"No City of Tomorrow top-chord frame targets were found in SAP2000 group '{model.GroupName}'. Regenerate the structure, then update loads again.");

                List<string> topChordJoints = GetCityTopChordJointPointNames(sapModel, topChordFrames, warnings);
                TryApplyCityTopChordLoadsToSap2000(sapModel, model.Input, topChordFrames, topChordJoints, warnings);
                appliedTopChordLoads = ReadCityTopChordLoads(sapModel, topChordFrames, topChordJoints, warnings);
                refreshViewAfterUpdate = true;

                return new CityOfTomorrowDrawResult
                {
                    Message = BuildCityTopChordLoadUpdateMessage(model, "SAP2000"),
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
                    TryRefreshSap2000View(sapModel);
            }
        }
        catch (Exception ex)
        {
            return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, AppliedTopChordLoads = appliedTopChordLoads, Warnings = warnings };
        }
    }

    public CityOfTomorrowDrawResult ClearCityOfTomorrow(Sap2000CityOfTomorrowClearRequest request)
    {
        var warnings = new List<string>();
        try
        {
            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);
            int deleted = DeleteCityObjects(sapModel, request.GroupName, warnings);
            try { sapModel.GroupDef.Delete(request.GroupName); } catch { }
            TryRefreshSap2000View(sapModel);

            return new CityOfTomorrowDrawResult
            {
                Message = deleted == 0
                    ? $"No generated SAP2000 objects found in '{request.GroupName}'."
                    : $"Cleared {deleted} generated SAP2000 object(s) from '{request.GroupName}'.",
                GroupName = request.GroupName,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, Warnings = warnings };
        }
    }

    private static void ValidateCityTopChordLoadPattern(SAP2000v1.cSapModel sapModel, CityOfTomorrowModel model, List<string> warnings)
    {
        CityOfTomorrowInput input = model.Input;
        if (input.TopChordLoadType == CityTopChordLoadType.None)
            return;

        string loadPattern = (input.TopChordLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
            throw new InvalidOperationException("Select a SAP2000 load pattern before applying a City of Tomorrow top-chord load.");

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
            throw new InvalidOperationException($"City of Tomorrow generation stopped because load pattern '{loadPattern}' does not exist in SAP2000.");
    }

    private static string BuildCityTopChordLoadUpdateMessage(CityOfTomorrowModel model, string productName)
    {
        CityOfTomorrowInput input = model.Input;
        return input.TopChordLoadType switch
        {
            CityTopChordLoadType.Udl => $"Updated City of Tomorrow top-chord loading in {productName} group '{model.GroupName}' to {input.TopChordUdlKnPerM:0.###} kN/m downward UDL.",
            CityTopChordLoadType.PointLoadAtJoints => $"Updated City of Tomorrow top-chord loading in {productName} group '{model.GroupName}' to {input.TopChordPointLoadKn:0.###} kN downward point load at each top-chord joint.",
            _ => $"No City of Tomorrow top-chord load type was selected; existing loading in {productName} group '{model.GroupName}' was not changed."
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
                warnings.Add($"Skipped City of Tomorrow top-chord load target '{member.Id}': SAP2000 frame name was not found.");
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
                warnings.Add($"Skipped City of Tomorrow top-chord joint load target '{nodeKey}': SAP2000 point name was not found.");
                continue;
            }

            pointNames.Add(pointName);
        }

        return pointNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCityTopChordFrameNames(SAP2000v1.cSapModel sapModel, CityOfTomorrowModel model, List<string> warnings)
    {
        List<string> generatedFrames = GetCityAssignments(sapModel, model.GroupName)
            .Where(assignment => assignment.Type == Sap2000SelectedFrameObjectType)
            .Select(assignment => assignment.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (generatedFrames.Count == 0)
        {
            warnings.Add($"No SAP2000 frame objects were found in City of Tomorrow group '{model.GroupName}'.");
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
                warnings.Add($"Could not inspect SAP2000 City of Tomorrow frame '{frameName}' while resolving top-chord load targets: {ex.Message}");
            }
        }

        if (topChordFrames.Count == 0)
            warnings.Add("No City of Tomorrow top-chord frame objects were resolved from the generated SAP2000 group.");

        return topChordFrames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCityTopChordJointPointNames(SAP2000v1.cSapModel sapModel, IReadOnlyList<string> topChordFrameNames, List<string> warnings)
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
                    warnings.Add($"Could not read end points for SAP2000 City of Tomorrow top-chord frame '{frameName}'. Return code: {ret}.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(pointI))
                    pointNames.Add(pointI);
                if (!string.IsNullOrWhiteSpace(pointJ))
                    pointNames.Add(pointJ);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not inspect SAP2000 City of Tomorrow top-chord frame '{frameName}' end points: {ex.Message}");
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

    private static bool IsCityFrameMatchingAnySegment(SAP2000v1.cSapModel sapModel, string frameName, IReadOnlyList<CityTopChordSegment> segments)
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

    private static void TryApplyCityTopChordLoadsToSap2000(
        SAP2000v1.cSapModel sapModel,
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
            warnings.Add("Skipped SAP2000 City of Tomorrow top-chord load: no load pattern was selected.");
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
        SAP2000v1.cSapModel sapModel,
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
        SAP2000v1.cSapModel sapModel,
        IReadOnlyList<string> topChordFrameNames,
        string loadPattern,
        double loadKnPerM,
        List<string> warnings)
    {
        if (topChordFrameNames.Count == 0)
        {
            warnings.Add("Skipped SAP2000 City of Tomorrow top-chord UDL: no top-chord frame targets were found.");
            return;
        }

        foreach (string frameName in topChordFrameNames)
        {
            try
            {
                int ret = sapModel.FrameObj.SetLoadDistributed(
                    frameName,
                    loadPattern,
                    1,
                    6,
                    0,
                    1,
                    loadKnPerM,
                    loadKnPerM,
                    "Global",
                    true,
                    true,
                    Sap2000Objects);

                if (ret != 0)
                    warnings.Add($"SAP2000 could not assign City of Tomorrow top-chord UDL to frame '{frameName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 City of Tomorrow top-chord UDL assignment failed on frame '{frameName}': {ex.Message}");
            }
        }
    }

    private static void TryApplyCityTopChordJointPointLoads(
        SAP2000v1.cSapModel sapModel,
        IReadOnlyList<string> topChordJointPointNames,
        string loadPattern,
        double loadKn,
        List<string> warnings)
    {
        if (topChordJointPointNames.Count == 0)
        {
            warnings.Add("Skipped SAP2000 City of Tomorrow top-chord point load: no top-chord joint targets were found.");
            return;
        }

        foreach (string pointName in topChordJointPointNames)
        {
            double[] values = [0, 0, loadKn, 0, 0, 0];
            try
            {
                int ret = sapModel.PointObj.SetLoadForce(pointName, loadPattern, ref values, true, "Global", Sap2000Objects);
                if (ret != 0)
                    warnings.Add($"SAP2000 could not assign City of Tomorrow top-chord point load to point '{pointName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 City of Tomorrow top-chord point load assignment failed at point '{pointName}': {ex.Message}");
            }
        }
    }

    private static List<CityAppliedTopChordLoad> ReadCityTopChordLoads(
        SAP2000v1.cSapModel sapModel,
        IReadOnlyList<string> topChordFrameNames,
        IReadOnlyList<string> topChordJointPointNames,
        List<string> warnings)
    {
        var seeds = new List<CityAppliedTopChordLoadSeed>();

        foreach (string frameName in topChordFrameNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            TryAppendCityTopChordDistributedLoads(sapModel, frameName, seeds, warnings);

        foreach (string pointName in topChordJointPointNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            TryAppendCityTopChordPointLoads(sapModel, pointName, seeds, warnings);

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
        SAP2000v1.cSapModel sapModel,
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
                Sap2000Objects);

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
            warnings.Add($"SAP2000 City of Tomorrow top-chord distributed loads could not be read on frame '{frameName}': {ex.Message}");
        }
    }

    private static void TryAppendCityTopChordPointLoads(
        SAP2000v1.cSapModel sapModel,
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
                Sap2000Objects);

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
            warnings.Add($"SAP2000 City of Tomorrow top-chord point loads could not be read at point '{pointName}': {ex.Message}");
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

    public CotArchDrawResult DrawCotArch(Sap2000CotArchDrawRequest request)
    {
        var warnings = new List<string>();
        var createdFrames = new List<string>();
        var createdObjects = new List<string>();
        var createdPoints = new List<string>();
        var appliedLoads = new List<CotArchAppliedUpperBeamLoad>();

        try
        {
            CotArchModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No CoT Arch geometry was provided.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                HashSet<string> cableProperties = GetCableSectionNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> tendonProperties = GetTendonSectionNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
                ValidateCotArchFrameSections(sapModel, model, cableProperties, tendonProperties, warnings);
                ValidateCotArchLoadPattern(sapModel, model, warnings);

                string mainGroup = EnsureSap2000DrawGroup(sapModel, model.GroupName, warnings);
                Dictionary<CotArchMemberKind, string> memberGroups = EnsureCotArchGroups(sapModel, model, warnings);
                int existing = GetSap2000Assignments(sapModel, mainGroup).Count;
                if (!request.ReplaceExistingStructure && existing > 0)
                    throw new InvalidOperationException($"Group '{mainGroup}' already contains {existing} object(s). Use Regenerate SAP2000 Structure or change the model prefix.");
                if (request.ReplaceExistingStructure)
                    DeleteCotArchObjects(sapModel, model, warnings);

                var pointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (CotArchNode node in model.Nodes)
                {
                    string pointName = "";
                    string preferred = EtabsNameUtility.BuildSafeName("", node.Id);
                    int ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref pointName, preferred, "Global", true, 0);
                    if (ret != 0)
                    {
                        pointName = "";
                        ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref pointName, "", "Global", true, 0);
                    }

                    if (ret != 0 || string.IsNullOrWhiteSpace(pointName))
                    {
                        warnings.Add($"SAP2000 could not create CoT Arch point '{node.Id}'. Return code: {ret}.");
                        continue;
                    }

                    pointMap[node.Id] = pointName;
                    createdPoints.Add(pointName);
                    TryAssignPointToGroup(sapModel, pointName, mainGroup, node.Id, warnings);
                    TryAssignPointToGroup(sapModel, pointName, model.PointGroupName, node.Id, warnings);
                }

                var frameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (CotArchMember member in model.Members)
                {
                    if (!pointMap.TryGetValue(member.StartNodeId, out string? pi) ||
                        !pointMap.TryGetValue(member.EndNodeId, out string? pj))
                    {
                        warnings.Add($"Skipped CoT Arch member '{member.Id}': shared endpoints were unavailable.");
                        continue;
                    }

                    if (IsCotArchTensionProperty(member.SectionName, cableProperties, tendonProperties))
                    {
                        if (TryAddCotArchTensionObjectByPoint(sapModel, member, pi, pj, cableProperties, tendonProperties, out string tensionName, out Sap2000TensionObjectKind tensionKind, warnings))
                        {
                            createdObjects.Add(tensionName);
                            TryAssignTensionObjectToGroup(sapModel, tensionName, tensionKind, mainGroup, member.Id, warnings);
                            TryAssignTensionObjectToGroup(sapModel, tensionName, tensionKind, memberGroups[member.Kind], member.Id, warnings);
                        }

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
                        warnings.Add($"SAP2000 could not draw CoT Arch member '{member.Id}'. Return code: {ret}.");
                        continue;
                    }

                    frameMap[member.Id] = frameName;
                    createdFrames.Add(frameName);
                    createdObjects.Add(frameName);
                    TryAssignFrameSection(sapModel, frameName, member.Id, member.SectionName, warnings);
                    TryAssignFrameToGroup(sapModel, frameName, mainGroup, member.Id, warnings);
                    TryAssignFrameToGroup(sapModel, frameName, memberGroups[member.Kind], member.Id, warnings);
                    TryApplyCotArchReleasePreset(sapModel, frameName, member, warnings);
                    if (member.Kind == CotArchMemberKind.TensionTie)
                        TryAssignCotArchTensionOnlyLimit(sapModel, frameName, member.Id, warnings);
                }

                ApplyCotArchRestraints(sapModel, model, pointMap, warnings);
                List<string> upperBeamFrames = GetCotArchUpperBeamFrameNames(model, frameMap, warnings);
                List<string> upperJointPoints = GetCotArchUpperJointPointNames(model, pointMap, warnings);
                TryApplyCotArchLoads(sapModel, model.Input, upperBeamFrames, upperJointPoints, warnings);
                appliedLoads = ReadCotArchUpperBeamLoads(sapModel, upperBeamFrames, upperJointPoints, warnings);

                TryRefreshSap2000View(sapModel);
                return new CotArchDrawResult
                {
                    Message = BuildCotArchDrawMessage(model, createdObjects.Count, mainGroup, "SAP2000"),
                    FrameCount = createdObjects.Count,
                    GroupName = mainGroup,
                    FrameObjectNames = createdObjects,
                    PointObjectNames = createdPoints,
                    AppliedUpperBeamLoads = appliedLoads,
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
                FrameCount = createdObjects.Count,
                FrameObjectNames = createdObjects,
                PointObjectNames = createdPoints,
                AppliedUpperBeamLoads = appliedLoads,
                Warnings = warnings
            };
        }
    }

    public CotArchDrawResult UpdateCotArchLoads(Sap2000CotArchLoadUpdateRequest request)
    {
        var warnings = new List<string>();
        var appliedLoads = new List<CotArchAppliedUpperBeamLoad>();

        try
        {
            CotArchModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No CoT Arch geometry was provided.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                ValidateCotArchLoadPattern(sapModel, model, warnings);

                int existing = GetSap2000Assignments(sapModel, model.GroupName).Count;
                if (existing == 0)
                    throw new InvalidOperationException($"No generated CoT Arch objects were found in SAP2000 group '{model.GroupName}'. Generate the structure before updating loads.");

                List<string> upperBeamFrames = GetCotArchUpperBeamFrameNames(sapModel, model, warnings);
                List<string> upperJointPoints = GetCotArchUpperJointPointNames(sapModel, model, warnings);
                if (upperBeamFrames.Count == 0 && upperJointPoints.Count == 0)
                    throw new InvalidOperationException($"No CoT Arch upper-beam load targets were found in SAP2000 group '{model.GroupName}'. Regenerate the structure, then update loads again.");

                TryApplyCotArchLoads(sapModel, model.Input, upperBeamFrames, upperJointPoints, warnings);
                appliedLoads = ReadCotArchUpperBeamLoads(sapModel, upperBeamFrames, upperJointPoints, warnings);
                TryRefreshSap2000View(sapModel);

                return new CotArchDrawResult
                {
                    Message = BuildCotArchLoadUpdateMessage(model, "SAP2000"),
                    FrameCount = upperBeamFrames.Count,
                    GroupName = model.GroupName,
                    FrameObjectNames = upperBeamFrames,
                    PointObjectNames = upperJointPoints,
                    AppliedUpperBeamLoads = appliedLoads,
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
            return new CotArchDrawResult { IsError = true, Message = ex.Message, AppliedUpperBeamLoads = appliedLoads, Warnings = warnings };
        }
    }

    public CotArchDrawResult ClearCotArch(Sap2000CotArchClearRequest request)
    {
        var warnings = new List<string>();
        try
        {
            string prefix = EtabsNameUtility.BuildSafeName("", request.ModelPrefix, 24);
            CotArchModel model = new CotArchGeometryBuilder().Build(new CotArchInput { ModelPrefix = prefix });
            string groupName = string.IsNullOrWhiteSpace(request.GroupName)
                ? model.GroupName
                : EtabsNameUtility.BuildSafeName("", request.GroupName);

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);
            int deleted = DeleteSap2000Objects(sapModel, groupName, warnings);
            foreach (string generatedGroup in CotArchAllGroupNames(model).OrderByDescending(name => name.Length))
            {
                try { sapModel.GroupDef.Delete(generatedGroup); }
                catch { }
            }

            TryRefreshSap2000View(sapModel);
            return new CotArchDrawResult
            {
                Message = deleted == 0 ? $"No generated SAP2000 objects found in '{groupName}'." : $"Cleared {deleted} CoT Arch SAP2000 generated object(s) from '{groupName}'.",
                GroupName = groupName,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new CotArchDrawResult { IsError = true, Message = ex.Message, Warnings = warnings };
        }
    }

    private static void ValidateCotArchFrameSections(
        SAP2000v1.cSapModel sapModel,
        CotArchModel model,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties,
        List<string> warnings)
    {
        HashSet<string> availableSections = GetFrameSectionNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (availableSections.Count == 0)
            throw new InvalidOperationException("No SAP2000 frame sections were available. Define or read SAP2000 frame sections before generating CoT Arch.");

        List<string> missing = model.Members
            .Where(member => !IsCotArchSectionAvailable(member, availableSections, cableProperties, tendonProperties))
            .Select(member => member.SectionName?.Trim() ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException("CoT Arch SAP2000 generation stopped because these selected SAP2000 sections do not exist for their member type: " + string.Join(", ", missing.Select(section => section.Length == 0 ? "<blank>" : section)));
    }

    private static bool IsCotArchSectionAvailable(
        CotArchMember member,
        IReadOnlySet<string> frameProperties,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties)
    {
        string section = member.SectionName?.Trim() ?? "";
        if (section.Length == 0)
            return false;

        if (frameProperties.Contains(section))
            return true;

        return member.Kind == CotArchMemberKind.TensionTie &&
            IsCotArchTensionProperty(section, cableProperties, tendonProperties);
    }

    private static bool IsCotArchTensionProperty(
        string? sectionName,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties)
    {
        string section = sectionName?.Trim() ?? "";
        return section.Length > 0 && (cableProperties.Contains(section) || tendonProperties.Contains(section));
    }

    private static void ValidateCotArchLoadPattern(SAP2000v1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        CotArchInput input = model.Input;
        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.None)
            return;

        string loadPattern = (input.UpperBeamLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
            throw new InvalidOperationException("Select a SAP2000 load pattern before applying a CoT Arch upper-beam load.");

        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.Udl &&
            (!double.IsFinite(input.UpperBeamUdlKnPerM) || input.UpperBeamUdlKnPerM <= 0.000001))
        {
            throw new InvalidOperationException("CoT Arch upper-beam UDL must be greater than zero.");
        }

        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.PointLoadAtJoints &&
            (!double.IsFinite(input.UpperBeamPointLoadKn) || input.UpperBeamPointLoadKn <= 0.000001))
        {
            throw new InvalidOperationException("CoT Arch upper-beam point load per joint must be greater than zero.");
        }

        HashSet<string> availableLoadPatterns = GetLoadPatternNames(sapModel, warnings).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (availableLoadPatterns.Count > 0 && !availableLoadPatterns.Contains(loadPattern))
            throw new InvalidOperationException($"CoT Arch SAP2000 generation stopped because load pattern '{loadPattern}' does not exist in SAP2000.");
    }

    private static Dictionary<CotArchMemberKind, string> EnsureCotArchGroups(SAP2000v1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        EnsureSap2000DrawGroup(sapModel, model.PointGroupName, warnings);
        string archGroup = EnsureSap2000DrawGroup(sapModel, model.ArchGroupName, warnings);
        string postGroup = EnsureSap2000DrawGroup(sapModel, model.PostGroupName, warnings);
        string upperBeamGroup = EnsureSap2000DrawGroup(sapModel, model.UpperBeamGroupName, warnings);
        string tieGroup = EnsureSap2000DrawGroup(sapModel, model.TieGroupName, warnings);
        string supportColumnGroup = EnsureSap2000DrawGroup(sapModel, model.SupportColumnGroupName, warnings);

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

    private static string BuildCotArchDrawMessage(CotArchModel model, int objectCount, string groupName, string productName)
    {
        string message = $"Drawn {objectCount} CoT Arch object(s) in {productName} group '{groupName}'.";
        CotArchInput input = model.Input;
        return input.UpperBeamLoadType switch
        {
            CotArchUpperBeamLoadType.Udl => message + $" Applied {input.UpperBeamUdlKnPerM:0.###} kN/m downward UDL to the upper beam.",
            CotArchUpperBeamLoadType.PointLoadAtJoints => message + $" Applied {input.UpperBeamPointLoadKn:0.###} kN downward point load at each upper-beam joint.",
            _ => message
        };
    }

    private static string BuildCotArchLoadUpdateMessage(CotArchModel model, string productName)
    {
        CotArchInput input = model.Input;
        return input.UpperBeamLoadType switch
        {
            CotArchUpperBeamLoadType.Udl => $"Updated CoT Arch upper-beam loading in {productName} group '{model.GroupName}' to {input.UpperBeamUdlKnPerM:0.###} kN/m downward UDL.",
            CotArchUpperBeamLoadType.PointLoadAtJoints => $"Updated CoT Arch upper-beam loading in {productName} group '{model.GroupName}' to {input.UpperBeamPointLoadKn:0.###} kN downward point load at each upper-beam joint.",
            _ => $"No CoT Arch upper-beam load type was selected; existing loading in {productName} group '{model.GroupName}' was not changed."
        };
    }

    private static List<string> GetCotArchUpperBeamFrameNames(CotArchModel model, Dictionary<string, string> frameMap, List<string> warnings)
    {
        var frameNames = new List<string>();
        foreach (CotArchMember member in model.Members.Where(member => member.Kind == CotArchMemberKind.UpperBeam))
        {
            if (!frameMap.TryGetValue(member.Id, out string? frameName) || string.IsNullOrWhiteSpace(frameName))
            {
                warnings.Add($"Skipped CoT Arch upper-beam load target '{member.Id}': SAP2000 frame name was not found.");
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
                warnings.Add($"Skipped CoT Arch upper joint load target '{node.Id}': SAP2000 point name was not found.");
                continue;
            }

            pointNames.Add(pointName);
        }

        return pointNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetCotArchUpperBeamFrameNames(SAP2000v1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        List<string> frameNames = GetSap2000Assignments(sapModel, model.UpperBeamGroupName)
            .Where(assignment => assignment.Type == Sap2000SelectedFrameObjectType)
            .Select(assignment => assignment.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (frameNames.Count == 0)
            warnings.Add($"No SAP2000 frame objects were found in CoT Arch upper-beam group '{model.UpperBeamGroupName}'.");

        return frameNames;
    }

    private static List<string> GetCotArchUpperJointPointNames(SAP2000v1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        List<string> pointNames = GetSap2000Assignments(sapModel, model.PointGroupName)
            .Where(assignment => assignment.Type == Sap2000SelectedPointObjectType)
            .Select(assignment => assignment.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pointNames.Count == 0)
        {
            warnings.Add($"No SAP2000 point objects were found in CoT Arch point group '{model.PointGroupName}'.");
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
                warnings.Add($"Could not inspect SAP2000 CoT Arch point '{pointName}' while resolving upper-beam load targets: {ex.Message}");
            }
        }

        if (upperJointPointNames.Count == 0)
            warnings.Add("No SAP2000 CoT Arch upper-beam joint points were resolved from the generated point group.");

        return upperJointPointNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsCotArchPointAtNode((double X, double Y, double Z) coordinates, CotArchNode node)
    {
        return Math.Abs(coordinates.X - node.X) <= 0.000001 &&
            Math.Abs(coordinates.Y - node.Y) <= 0.000001 &&
            Math.Abs(coordinates.Z - node.Z) <= 0.000001;
    }

    private static void ApplyCotArchRestraints(SAP2000v1.cSapModel sapModel, CotArchModel model, Dictionary<string, string> pointMap, List<string> warnings)
    {
        foreach (string pointName in pointMap.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            TrySetPointRestraint(sapModel, pointName, [false, false, false, false, false, false], $"free CoT Arch joint '{pointName}'", warnings);

        if (model.LeftBase == null || model.RightBase == null)
            return;

        bool[] baseRestraints = [true, true, true, false, false, false];
        if (pointMap.TryGetValue(model.LeftBase.Id, out string? leftBasePoint))
            TrySetPointRestraint(sapModel, leftBasePoint, baseRestraints, "left CoT Arch base support", warnings);
        if (pointMap.TryGetValue(model.RightBase.Id, out string? rightBasePoint))
            TrySetPointRestraint(sapModel, rightBasePoint, baseRestraints, "right CoT Arch base support", warnings);
    }

    private static void TryApplyCotArchReleasePreset(SAP2000v1.cSapModel sapModel, string frameName, CotArchMember member, List<string> warnings)
    {
        if (member.ReleasePreset == CotArchMemberReleasePreset.FullyContinuous)
            return;

        TryAssignTrussReleases(sapModel, frameName, member.Id, warnings);
    }

    private static void TryApplyCityReleasePreset(SAP2000v1.cSapModel sapModel, string frameName, CityMember member, List<string> warnings)
    {
        if (member.ReleasePreset == CityMemberReleasePreset.FullyContinuous)
            return;

        TryAssignTrussReleases(sapModel, frameName, member.Id, warnings);
    }

    private static void TryAssignCityTensionOnlyLimit(SAP2000v1.cSapModel sapModel, string frameName, string memberId, List<string> warnings)
    {
        try
        {
            int ret = sapModel.FrameObj.SetTCLimits(frameName, true, 0, false, 0, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"City of Tomorrow member '{memberId}' was drawn, but SAP2000 could not assign zero compression capacity. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"City of Tomorrow member '{memberId}' was drawn, but SAP2000 tension-only limit assignment failed: {ex.Message}");
        }
    }

    private static void TryAssignCotArchTensionOnlyLimit(SAP2000v1.cSapModel sapModel, string frameName, string memberId, List<string> warnings)
    {
        try
        {
            int ret = sapModel.FrameObj.SetTCLimits(frameName, true, 0, false, 0, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"CoT Arch tension tie '{memberId}' was drawn, but SAP2000 could not assign zero compression capacity. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"CoT Arch tension tie '{memberId}' was drawn, but SAP2000 tension-only limit assignment failed: {ex.Message}");
        }
    }

    private static void TryApplyCotArchLoads(
        SAP2000v1.cSapModel sapModel,
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
            warnings.Add("Skipped SAP2000 CoT Arch upper-beam load: no load pattern was selected.");
            return;
        }

        TryClearCotArchLoads(sapModel, upperBeamFrameNames, upperJointPointNames, loadPattern, warnings);

        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.Udl)
        {
            TryApplyCotArchUpperBeamUdl(sapModel, upperBeamFrameNames, loadPattern, -Math.Abs(input.UpperBeamUdlKnPerM), warnings);
            return;
        }

        if (input.UpperBeamLoadType == CotArchUpperBeamLoadType.PointLoadAtJoints)
            TryApplyCotArchUpperJointPointLoads(sapModel, upperJointPointNames, loadPattern, -Math.Abs(input.UpperBeamPointLoadKn), warnings);
    }

    private static void TryClearCotArchLoads(
        SAP2000v1.cSapModel sapModel,
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

    private static void TryApplyCotArchUpperBeamUdl(
        SAP2000v1.cSapModel sapModel,
        IReadOnlyList<string> upperBeamFrameNames,
        string loadPattern,
        double loadKnPerM,
        List<string> warnings)
    {
        if (upperBeamFrameNames.Count == 0)
        {
            warnings.Add("Skipped SAP2000 CoT Arch upper-beam UDL: no upper-beam frame targets were found.");
            return;
        }

        foreach (string frameName in upperBeamFrameNames)
        {
            try
            {
                int ret = sapModel.FrameObj.SetLoadDistributed(
                    frameName,
                    loadPattern,
                    1,
                    6,
                    0,
                    1,
                    loadKnPerM,
                    loadKnPerM,
                    "Global",
                    true,
                    true,
                    Sap2000Objects);

                if (ret != 0)
                    warnings.Add($"SAP2000 could not assign CoT Arch upper-beam UDL to frame '{frameName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 CoT Arch upper-beam UDL assignment failed on frame '{frameName}': {ex.Message}");
            }
        }
    }

    private static void TryApplyCotArchUpperJointPointLoads(
        SAP2000v1.cSapModel sapModel,
        IReadOnlyList<string> upperJointPointNames,
        string loadPattern,
        double loadKn,
        List<string> warnings)
    {
        if (upperJointPointNames.Count == 0)
        {
            warnings.Add("Skipped SAP2000 CoT Arch upper joint point load: no upper-joint point targets were found.");
            return;
        }

        foreach (string pointName in upperJointPointNames)
        {
            double[] values = [0, 0, loadKn, 0, 0, 0];
            try
            {
                int ret = sapModel.PointObj.SetLoadForce(pointName, loadPattern, ref values, true, "Global", Sap2000Objects);
                if (ret != 0)
                    warnings.Add($"SAP2000 could not assign CoT Arch upper joint point load to point '{pointName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 CoT Arch upper joint point load assignment failed at point '{pointName}': {ex.Message}");
            }
        }
    }

    private static List<CotArchAppliedUpperBeamLoad> ReadCotArchUpperBeamLoads(
        SAP2000v1.cSapModel sapModel,
        IReadOnlyList<string> upperBeamFrameNames,
        IReadOnlyList<string> upperJointPointNames,
        List<string> warnings)
    {
        var seeds = new List<CotArchAppliedUpperBeamLoadSeed>();

        foreach (string frameName in upperBeamFrameNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            TryAppendCotArchUpperBeamDistributedLoads(sapModel, frameName, seeds, warnings);

        foreach (string pointName in upperJointPointNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            TryAppendCotArchUpperJointPointLoads(sapModel, pointName, seeds, warnings);

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
        SAP2000v1.cSapModel sapModel,
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
                Sap2000Objects);

            if (ret != 0 || loadPatterns.Length == 0)
                return;

            int count = Math.Min(numberItems, loadPatterns.Length);
            for (int index = 0; index < count; index++)
            {
                if (index < loadTypes.Length && loadTypes[index] != 1)
                    continue;

                double startValue = index < value1.Length ? value1[index] : 0;
                double endValue = index < value2.Length ? value2[index] : startValue;
                if (Math.Abs(startValue) <= 0.000001 && Math.Abs(endValue) <= 0.000001)
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
            warnings.Add($"SAP2000 CoT Arch upper-beam distributed loads could not be read on frame '{frameName}': {ex.Message}");
        }
    }

    private static void TryAppendCotArchUpperJointPointLoads(
        SAP2000v1.cSapModel sapModel,
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
                Sap2000Objects);

            if (ret != 0 || loadPatterns.Length == 0)
                return;

            int count = Math.Min(numberItems, loadPatterns.Length);
            for (int index = 0; index < count; index++)
            {
                double verticalLoad = index < f3.Length ? f3[index] : 0;
                if (Math.Abs(verticalLoad) <= 0.000001)
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
            warnings.Add($"SAP2000 CoT Arch upper-joint point loads could not be read at point '{pointName}': {ex.Message}");
        }
    }

    private static string FormatCotArchDistributedLoadValue(double startValue, double endValue, int direction)
    {
        if (direction is 6 or 9 or 10 or 11)
        {
            bool positiveIsDown = direction is 10 or 11;
            if (Math.Abs(startValue - endValue) <= 0.000001)
                return FormatCotArchSignedVerticalLoadValue(startValue, "kN/m", positiveIsDown);

            return $"{FormatCotArchSignedVerticalLoadValue(startValue, "kN/m", positiveIsDown)} to {FormatCotArchSignedVerticalLoadValue(endValue, "kN/m", positiveIsDown)}";
        }

        string directionText = direction switch
        {
            4 => "Global X",
            5 => "Global Y",
            7 => "Projected Global X",
            8 => "Projected Global Y",
            _ => $"Direction {direction}"
        };

        if (Math.Abs(startValue - endValue) <= 0.000001)
            return $"{startValue:0.###} kN/m ({directionText})";

        return $"{startValue:0.###} to {endValue:0.###} kN/m ({directionText})";
    }

    private static string FormatCotArchSignedVerticalLoadValue(double value, string unit, bool positiveIsDown = false)
    {
        string sense = positiveIsDown
            ? value > 0.000001 ? "down" : "up"
            : value < -0.000001 ? "down" : "up";
        return $"{Math.Abs(value):0.###} {unit} {sense}";
    }

    private static void TryClearPointForceLoads(SAP2000v1.cSapModel sapModel, string pointName, List<string> warnings, string loadPatternFilter)
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

            int ret = sapModel.PointObj.GetLoadForce(pointName, ref numberItems, ref pointNames, ref loadPatterns, ref stepTypes, ref coordinateSystems, ref f1, ref f2, ref f3, ref m1, ref m2, ref m3, Sap2000Objects);
            if (ret != 0 || loadPatterns.Length == 0)
                return;

            foreach (string loadPattern in loadPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Where(pattern => string.IsNullOrWhiteSpace(loadPatternFilter) || string.Equals(pattern, loadPatternFilter, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.PointObj.DeleteLoadForce(pointName, loadPattern, Sap2000Objects);
                if (deleteRet != 0)
                    warnings.Add($"SAP2000 could not clear old point load pattern '{loadPattern}' on point '{pointName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Old SAP2000 point loads could not be cleared on point '{pointName}': {ex.Message}");
        }
    }

    private static void TryClearFrameDistributedLoads(SAP2000v1.cSapModel sapModel, string frameName, List<string> warnings, string loadPatternFilter)
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

            int ret = sapModel.FrameObj.GetLoadDistributed(frameName, ref numberItems, ref frameNames, ref loadPatterns, ref loadTypes, ref coordinateSystems, ref directions, ref relativeDistance1, ref relativeDistance2, ref distance1, ref distance2, ref value1, ref value2, Sap2000Objects);
            if (ret != 0 || loadPatterns.Length == 0)
                return;

            foreach (string loadPattern in loadPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Where(pattern => string.IsNullOrWhiteSpace(loadPatternFilter) || string.Equals(pattern, loadPatternFilter, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.DeleteLoadDistributed(frameName, loadPattern, Sap2000Objects);
                if (deleteRet != 0)
                    warnings.Add($"SAP2000 could not clear old distributed load pattern '{loadPattern}' on frame '{frameName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Old SAP2000 distributed frame loads could not be cleared on frame '{frameName}': {ex.Message}");
        }
    }

    private static void TryClearFramePointLoads(SAP2000v1.cSapModel sapModel, string frameName, List<string> warnings, string loadPatternFilter)
    {
        try
        {
            int numberItems = 0;
            string[] frameNames = [];
            string[] loadPatterns = [];
            int[] loadTypes = [];
            string[] coordinateSystems = [];
            int[] directions = [];
            double[] relativeDistances = [];
            double[] values = [];
            double[] absoluteDistances = [];

            int ret = sapModel.FrameObj.GetLoadPoint(frameName, ref numberItems, ref frameNames, ref loadPatterns, ref loadTypes, ref coordinateSystems, ref directions, ref relativeDistances, ref values, ref absoluteDistances, Sap2000Objects);
            if (ret != 0 || loadPatterns.Length == 0)
                return;

            foreach (string loadPattern in loadPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Where(pattern => string.IsNullOrWhiteSpace(loadPatternFilter) || string.Equals(pattern, loadPatternFilter, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.DeleteLoadPoint(frameName, loadPattern, Sap2000Objects);
                if (deleteRet != 0)
                    warnings.Add($"SAP2000 could not clear old frame point load pattern '{loadPattern}' on frame '{frameName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Old SAP2000 frame point loads could not be cleared on frame '{frameName}': {ex.Message}");
        }
    }

    private sealed record CotArchAppliedUpperBeamLoadSeed(
        string LoadPattern,
        string LoadType,
        string ValueText,
        string TargetSingular,
        string TargetPlural);

    private static List<string> GetFrameSectionNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SAP2000v1.eFramePropType propType in Enum.GetValues(typeof(SAP2000v1.eFramePropType)).Cast<SAP2000v1.eFramePropType>())
        {
            int numberNames = 0;
            string[] frameNames = [];
            try
            {
                if (sapModel.PropFrame.GetNameList(ref numberNames, ref frameNames, propType) == 0)
                {
                    foreach (string name in frameNames.Take(Math.Min(numberNames, frameNames.Length)))
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name.Trim());
                    }
                }
            }
            catch
            {
                // Some property families may not be present in the connected model.
            }
        }

        if (names.Count == 0)
            warnings.Add("SAP2000 frame section list could not be loaded or the connected model has no frame sections.");

        return names.ToList();
    }

    private static List<string> GetMaterialNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SAP2000v1.eMatType materialType in Enum.GetValues(typeof(SAP2000v1.eMatType)).Cast<SAP2000v1.eMatType>())
        {
            int numberNames = 0;
            string[] materialNames = [];
            try
            {
                if (sapModel.PropMaterial.GetNameList(ref numberNames, ref materialNames, materialType) == 0)
                {
                    foreach (string name in materialNames.Take(Math.Min(numberNames, materialNames.Length)))
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name.Trim());
                    }
                }
            }
            catch
            {
                // Missing material families are expected in many SAP2000 models.
            }
        }

        if (names.Count == 0)
            warnings.Add("SAP2000 material list could not be loaded or the connected model has no materials.");

        return names.ToList();
    }

    private static List<string> GetCableSectionNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.PropCable.GetNameList(ref numberNames, ref names) == 0)
            {
                return names
                    .Take(Math.Min(numberNames, names.Length))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            warnings.Add("SAP2000 cable section list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<string> GetTendonSectionNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.PropTendon.GetNameList(ref numberNames, ref names) == 0)
            {
                return names
                    .Take(Math.Min(numberNames, names.Length))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            warnings.Add("SAP2000 tendon section list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<string> GetLoadPatternNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.LoadPatterns.GetNameList(ref numberNames, ref names) == 0)
            {
                return names
                    .Take(Math.Min(numberNames, names.Length))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            warnings.Add("SAP2000 load pattern list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<string> GetGroupNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.GroupDef.GetNameList(ref numberNames, ref names) == 0)
            {
                return names
                    .Take(Math.Min(numberNames, names.Length))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            warnings.Add("SAP2000 group list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<(int Type, string Name)> GetSap2000Assignments(SAP2000v1.cSapModel sapModel, string groupName)
    {
        int count = 0;
        int[] types = [];
        string[] names = [];
        if (sapModel.GroupDef.GetAssignments(groupName, ref count, ref types, ref names) != 0)
            return [];

        return Enumerable.Range(0, Math.Min(count, Math.Min(types.Length, names.Length)))
            .Select(index => (types[index], names[index]))
            .Where(item => !string.IsNullOrWhiteSpace(item.Item2))
            .ToList();
    }

    private static int DeleteSap2000Objects(SAP2000v1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        var assignments = GetSap2000Assignments(sapModel, groupName);
        int deleted = 0;
        foreach (string objectName in assignments.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryDeleteCable(sapModel, objectName)) deleted++;
            if (TryDeleteTendon(sapModel, objectName)) deleted++;
            if (TryDeleteFrame(sapModel, objectName)) deleted++;
        }

        foreach (string point in assignments.Where(item => item.Type == Sap2000SelectedPointObjectType).Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                TrySetPointRestraint(sapModel, point, [false, false, false, false, false, false], $"generated point '{point}'", warnings);
                if (sapModel.PointObj.DeleteSpecialPoint(point, Sap2000Objects) == 0)
                    deleted++;
            }
            catch
            {
                // Points tied to surviving objects may not be deletable.
            }
        }

        return deleted;
    }

    private static int DeleteCotArchObjects(SAP2000v1.cSapModel sapModel, CotArchModel model, List<string> warnings)
    {
        return DeleteSap2000Objects(sapModel, model.GroupName, warnings);
    }

    private static (double X, double Y, double Z) GetPointCoordinates(SAP2000v1.cSapModel sapModel, string pointName)
    {
        double x = 0;
        double y = 0;
        double z = 0;
        int ret = sapModel.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z, "Global");
        if (ret != 0)
            throw new InvalidOperationException($"SAP2000 could not find point object '{pointName}' for coordinate retrieval.");

        return (x, y, z);
    }

    private static string EnsureSap2000DrawGroup(SAP2000v1.cSapModel sapModel, string? rawGroupName, List<string> warnings)
    {
        string groupName = EtabsNameUtility.BuildSafeName("", rawGroupName);
        if (groupName.Length == 0)
            throw new InvalidOperationException("SAP2000 group name is required.");

        try
        {
            int ret = sapModel.GroupDef.SetGroup(
                groupName,
                -1,
                true,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false);
            if (ret != 0)
                warnings.Add($"SAP2000 group '{groupName}' could not be created/updated. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 group '{groupName}' could not be created/updated: {ex.Message}");
        }

        return groupName;
    }

    private static List<(int Type, string Name)> GetCityAssignments(SAP2000v1.cSapModel sapModel, string groupName)
    {
        int count = 0;
        int[] types = [];
        string[] names = [];
        if (sapModel.GroupDef.GetAssignments(groupName, ref count, ref types, ref names) != 0)
            return [];

        return Enumerable.Range(0, Math.Min(count, Math.Min(types.Length, names.Length)))
            .Select(i => (types[i], names[i]))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item2))
            .ToList();
    }

    private static int DeleteCityObjects(SAP2000v1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        var assignments = GetCityAssignments(sapModel, groupName);
        int deleted = 0;
        foreach (string objectName in assignments.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryDeleteCable(sapModel, objectName)) deleted++;
            if (TryDeleteTendon(sapModel, objectName)) deleted++;
            if (TryDeleteFrame(sapModel, objectName)) deleted++;
        }

        foreach (string point in assignments.Where(x => x.Type == Sap2000SelectedPointObjectType).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                TrySetPointRestraint(sapModel, point, [false, false, false, false, false, false], $"generated point '{point}'", warnings);
                if (sapModel.PointObj.DeleteSpecialPoint(point, Sap2000Objects) == 0)
                    deleted++;
            }
            catch
            {
                // Points tied to surviving objects may not be deletable.
            }
        }

        return deleted;
    }

    private static bool TryDeleteCable(SAP2000v1.cSapModel sapModel, string name)
    {
        try { return sapModel.CableObj.Delete(name, Sap2000Objects) == 0; }
        catch { return false; }
    }

    private static bool TryDeleteTendon(SAP2000v1.cSapModel sapModel, string name)
    {
        try { return sapModel.TendonObj.Delete(name, Sap2000Objects) == 0; }
        catch { return false; }
    }

    private static bool TryDeleteFrame(SAP2000v1.cSapModel sapModel, string name)
    {
        try { return sapModel.FrameObj.Delete(name, Sap2000Objects) == 0; }
        catch { return false; }
    }

    private static void TryAssignFrameSection(SAP2000v1.cSapModel sapModel, string frameName, string memberId, string sectionName, List<string> warnings)
    {
        try
        {
            int ret = sapModel.FrameObj.SetSection(frameName, sectionName, Sap2000Objects, 0, 0);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but SAP2000 could not assign section '{sectionName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but SAP2000 section assignment failed: {ex.Message}");
        }
    }

    private static void TryAssignTrussReleases(SAP2000v1.cSapModel sapModel, string frameName, string memberId, List<string> warnings)
    {
        try
        {
            bool[] startReleases = [false, false, false, false, true, true];
            bool[] endReleases = [false, false, false, false, true, true];
            double[] startSpring = [0, 0, 0, 0, 0, 0];
            double[] endSpring = [0, 0, 0, 0, 0, 0];

            int ret = sapModel.FrameObj.SetReleases(frameName, ref startReleases, ref endReleases, ref startSpring, ref endSpring, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but SAP2000 could not assign M22/M33 end releases. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but SAP2000 release assignment failed: {ex.Message}");
        }
    }

    private static bool TryAddCotArchTensionObjectByPoint(
        SAP2000v1.cSapModel sapModel,
        CotArchMember member,
        string pointI,
        string pointJ,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties,
        out string objectName,
        out Sap2000TensionObjectKind objectKind,
        List<string> warnings)
    {
        objectName = "";
        objectKind = ResolveCotArchTensionObjectKind(member.SectionName, cableProperties, tendonProperties);
        Sap2000TensionObjectKind fallbackKind = objectKind == Sap2000TensionObjectKind.Cable
            ? Sap2000TensionObjectKind.Tendon
            : Sap2000TensionObjectKind.Cable;

        if (TryAddCotArchTensionObjectByPoint(sapModel, member, pointI, pointJ, objectKind, out objectName, warnings))
            return true;

        if (TryAddCotArchTensionObjectByPoint(sapModel, member, pointI, pointJ, fallbackKind, out objectName, warnings))
        {
            warnings.Add($"CoT Arch member '{member.Id}' was drawn as a SAP2000 {FormatTensionKind(fallbackKind)} because the selected property '{member.SectionName}' was not accepted by {FormatTensionKind(objectKind)}.");
            objectKind = fallbackKind;
            return true;
        }

        warnings.Add($"SAP2000 could not draw CoT Arch cable/tendon member '{member.Id}' with property '{member.SectionName}'.");
        return false;
    }

    private static Sap2000TensionObjectKind ResolveCotArchTensionObjectKind(
        string? sectionName,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties)
    {
        string section = sectionName?.Trim() ?? "";
        bool isCableProperty = cableProperties.Contains(section);
        bool isTendonProperty = tendonProperties.Contains(section);

        if (isTendonProperty && !isCableProperty)
            return Sap2000TensionObjectKind.Tendon;

        if (isCableProperty && !isTendonProperty)
            return Sap2000TensionObjectKind.Cable;

        return Sap2000TensionObjectKind.Tendon;
    }

    private static bool TryAddCotArchTensionObjectByPoint(
        SAP2000v1.cSapModel sapModel,
        CotArchMember member,
        string pointI,
        string pointJ,
        Sap2000TensionObjectKind objectKind,
        out string objectName,
        List<string> warnings)
    {
        objectName = "";
        string safeName = EtabsNameUtility.BuildSafeName("", member.Id);
        string sectionName = member.SectionName?.Trim() ?? "";

        try
        {
            int ret = objectKind == Sap2000TensionObjectKind.Tendon
                ? sapModel.TendonObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, safeName)
                : sapModel.CableObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, safeName);

            if (ret != 0)
            {
                objectName = "";
                ret = objectKind == Sap2000TensionObjectKind.Tendon
                    ? sapModel.TendonObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, "")
                    : sapModel.CableObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, "");
            }

            if (ret != 0 || string.IsNullOrWhiteSpace(objectName))
                return false;

            TryAssignTensionProperty(sapModel, objectName, objectKind, member.Id, sectionName, warnings);
            if (objectKind == Sap2000TensionObjectKind.Tendon)
                TryAssignTendonTensionOnlyLimit(sapModel, objectName, member.Id, warnings);

            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 {FormatTensionKind(objectKind)} creation failed for CoT Arch member '{member.Id}': {ex.Message}");
            return false;
        }
    }

    private static bool ShouldDrawCityMemberAsTensionObject(
        CityMember member,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties)
    {
        return (member.IsTensionOnly || member.CanUseTensionSection) &&
            IsCityTensionProperty(member.SectionName, cableProperties, tendonProperties);
    }

    private static bool IsCityTensionProperty(
        string? sectionName,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties)
    {
        string section = sectionName?.Trim() ?? "";
        return section.Length > 0 && (cableProperties.Contains(section) || tendonProperties.Contains(section));
    }

    private static bool TryAddTensionObjectByPoint(
        SAP2000v1.cSapModel sapModel,
        CityMember member,
        string pointI,
        string pointJ,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties,
        out string objectName,
        out Sap2000TensionObjectKind objectKind,
        List<string> warnings)
    {
        objectName = "";
        objectKind = ResolveTensionObjectKind(member, cableProperties, tendonProperties);
        Sap2000TensionObjectKind fallbackKind = objectKind == Sap2000TensionObjectKind.Cable
            ? Sap2000TensionObjectKind.Tendon
            : Sap2000TensionObjectKind.Cable;

        if (TryAddTensionObjectByPoint(sapModel, member, pointI, pointJ, objectKind, out objectName, warnings))
            return true;

        if (TryAddTensionObjectByPoint(sapModel, member, pointI, pointJ, fallbackKind, out objectName, warnings))
        {
            warnings.Add($"Member '{member.Id}' was drawn as a SAP2000 {FormatTensionKind(fallbackKind)} because the selected property '{member.SectionName}' was not accepted by {FormatTensionKind(objectKind)}.");
            objectKind = fallbackKind;
            return true;
        }

        warnings.Add($"SAP2000 could not draw cable/tendon member '{member.Id}' with property '{member.SectionName}'.");
        return false;
    }

    private static Sap2000TensionObjectKind ResolveTensionObjectKind(
        CityMember member,
        IReadOnlySet<string> cableProperties,
        IReadOnlySet<string> tendonProperties)
    {
        string sectionName = member.SectionName ?? "";
        bool isCableProperty = cableProperties.Contains(sectionName);
        bool isTendonProperty = tendonProperties.Contains(sectionName);

        if (isCableProperty && !isTendonProperty)
            return Sap2000TensionObjectKind.Cable;

        if (isTendonProperty && !isCableProperty)
            return Sap2000TensionObjectKind.Tendon;

        return member.Kind == CityMemberKind.Tie
            ? Sap2000TensionObjectKind.Tendon
            : Sap2000TensionObjectKind.Cable;
    }

    private static bool TryAddTensionObjectByPoint(
        SAP2000v1.cSapModel sapModel,
        CityMember member,
        string pointI,
        string pointJ,
        Sap2000TensionObjectKind objectKind,
        out string objectName,
        List<string> warnings)
    {
        objectName = "";
        string safeName = EtabsNameUtility.BuildSafeName("", member.Id);
        string sectionName = member.SectionName ?? "";

        try
        {
            int ret = objectKind == Sap2000TensionObjectKind.Tendon
                ? sapModel.TendonObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, safeName)
                : sapModel.CableObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, safeName);

            if (ret != 0)
            {
                objectName = "";
                ret = objectKind == Sap2000TensionObjectKind.Tendon
                    ? sapModel.TendonObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, "")
                    : sapModel.CableObj.AddByPoint(pointI, pointJ, ref objectName, sectionName, "");
            }

            if (ret != 0 || string.IsNullOrWhiteSpace(objectName))
                return false;

            TryAssignTensionProperty(sapModel, objectName, objectKind, member.Id, sectionName, warnings);
            if (objectKind == Sap2000TensionObjectKind.Tendon)
                TryAssignTendonTensionOnlyLimit(sapModel, objectName, member.Id, warnings);

            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 {FormatTensionKind(objectKind)} creation failed for '{member.Id}': {ex.Message}");
            return false;
        }
    }

    private static void TryAssignTensionProperty(
        SAP2000v1.cSapModel sapModel,
        string objectName,
        Sap2000TensionObjectKind objectKind,
        string memberId,
        string sectionName,
        List<string> warnings)
    {
        try
        {
            int ret = objectKind == Sap2000TensionObjectKind.Tendon
                ? sapModel.TendonObj.SetProperty(objectName, sectionName, Sap2000Objects)
                : sapModel.CableObj.SetProperty(objectName, sectionName, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but SAP2000 could not assign {FormatTensionKind(objectKind)} property '{sectionName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but SAP2000 {FormatTensionKind(objectKind)} property assignment failed: {ex.Message}");
        }
    }

    private static void TryAssignTendonTensionOnlyLimit(SAP2000v1.cSapModel sapModel, string tendonName, string memberId, List<string> warnings)
    {
        try
        {
            int ret = sapModel.TendonObj.SetTCLimits(tendonName, true, 0, false, 0, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn as a tendon, but SAP2000 could not assign zero compression capacity. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn as a tendon, but SAP2000 tendon compression limit assignment failed: {ex.Message}");
        }
    }

    private static void TryAssignTensionObjectToGroup(
        SAP2000v1.cSapModel sapModel,
        string objectName,
        Sap2000TensionObjectKind objectKind,
        string groupName,
        string memberId,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(groupName))
            return;

        try
        {
            int ret = objectKind == Sap2000TensionObjectKind.Tendon
                ? sapModel.TendonObj.SetGroupAssign(objectName, groupName, false, Sap2000Objects)
                : sapModel.CableObj.SetGroupAssign(objectName, groupName, false, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but SAP2000 could not assign {FormatTensionKind(objectKind)} '{objectName}' to group '{groupName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but SAP2000 {FormatTensionKind(objectKind)} group assignment failed: {ex.Message}");
        }
    }

    private static string FormatTensionKind(Sap2000TensionObjectKind objectKind) =>
        objectKind == Sap2000TensionObjectKind.Tendon ? "tendon object" : "cable object";

    private static void TryAssignFrameToGroup(SAP2000v1.cSapModel sapModel, string frameName, string groupName, string memberId, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(frameName) || string.IsNullOrWhiteSpace(groupName))
            return;

        try
        {
            int ret = sapModel.FrameObj.SetGroupAssign(frameName, groupName, false, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but SAP2000 could not assign frame '{frameName}' to group '{groupName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but SAP2000 frame group assignment failed: {ex.Message}");
        }
    }

    private static void TryAssignPointToGroup(SAP2000v1.cSapModel sapModel, string pointName, string groupName, string memberId, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(pointName) || string.IsNullOrWhiteSpace(groupName))
            return;

        try
        {
            int ret = sapModel.PointObj.SetGroupAssign(pointName, groupName, false, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but SAP2000 could not assign point '{pointName}' to group '{groupName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but SAP2000 point group assignment failed: {ex.Message}");
        }
    }

    private static void TrySetPointRestraint(SAP2000v1.cSapModel sapModel, string pointName, bool[] restraints, string description, List<string> warnings)
    {
        try
        {
            int ret = sapModel.PointObj.SetRestraint(pointName, ref restraints, Sap2000Objects);
            if (ret != 0)
                warnings.Add($"SAP2000 could not set point restraint for {description} at point '{pointName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 point restraint assignment failed for {description} at point '{pointName}': {ex.Message}");
        }
    }

    private static void TryUnlockModelForDrawing(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        try
        {
            if (sapModel.GetModelIsLocked())
            {
                int ret = sapModel.SetModelIsLocked(false);
                if (ret == 0)
                    warnings.Add("SAP2000 model was locked; it was unlocked before drawing.");
                else
                    warnings.Add("SAP2000 model appears locked and could not be unlocked. Drawing may fail.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add("Unable to check/unlock SAP2000 model: " + ex.Message);
        }
    }

    private static SAP2000v1.eUnits? TryGetPresentUnits(SAP2000v1.cSapModel sapModel)
    {
        try { return sapModel.GetPresentUnits(); }
        catch { return null; }
    }

    private static void TrySetPresentUnitsToKnM(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        try
        {
            int ret = sapModel.SetPresentUnits(Sap2000UnitsKnMC);
            if (ret != 0)
                warnings.Add("SAP2000 did not switch present units to kN-m. Returned values may follow current SAP2000 units.");
        }
        catch (Exception ex)
        {
            warnings.Add("Unable to switch SAP2000 present units to kN-m: " + ex.Message);
        }
    }

    private static void TryRestorePresentUnits(SAP2000v1.cSapModel sapModel, SAP2000v1.eUnits originalUnits)
    {
        try { sapModel.SetPresentUnits(originalUnits); }
        catch { }
    }

    private static void TryRefreshSap2000View(SAP2000v1.cSapModel sapModel)
    {
        try { sapModel.View.RefreshView(0, false); }
        catch { }
    }

    private static SAP2000v1.cOAPI GetSap2000Object(string? instanceId)
    {
        string id = (instanceId ?? "").Trim();
        if (id.Length == 0 || string.Equals(id, "active", StringComparison.OrdinalIgnoreCase))
            return GetActiveSap2000Object();

        if (id.StartsWith("process:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(id["process:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
        {
            return GetSap2000ObjectForProcess(processId);
        }

        foreach ((string DisplayName, SAP2000v1.cOAPI Object) instance in EnumerateSap2000Objects())
        {
            if (string.Equals(instance.DisplayName, id, StringComparison.OrdinalIgnoreCase))
                return instance.Object;
        }

        throw new InvalidOperationException("The selected SAP2000 instance is no longer available. Refresh the SAP2000 instance list and try again.");
    }

    private static SAP2000v1.cOAPI GetActiveSap2000Object()
    {
        SAP2000v1.cOAPI? activeObject = TryGetActiveObjectFromRot(Sap2000ApiObjectProgId);
        if (activeObject != null)
            return activeObject;

        Process[] sapProcesses = Process.GetProcessesByName("SAP2000");
        if (sapProcesses.Length > 0)
        {
            SAP2000v1.cOAPI? helperObject = TryGetHelperSap2000Object(null);
            if (helperObject != null)
                return helperObject;

            if (sapProcesses.Length == 1)
            {
                SAP2000v1.cOAPI? processObject = TryGetHelperSap2000Object(sapProcesses[0].Id);
                if (processObject != null)
                    return processObject;
            }
        }

        throw new InvalidOperationException("No active SAP2000 API instance was found. Open SAP2000 before using SAP2000 draw tools.");
    }

    private static SAP2000v1.cOAPI GetSap2000ObjectForProcess(int processId)
    {
        SAP2000v1.cOAPI? sapObject = TryGetHelperSap2000Object(processId);
        if (sapObject != null)
            return sapObject;

        throw new InvalidOperationException($"Unable to connect to SAP2000 process {processId}. Confirm the model is open and SAP2000 is running under the same Windows user/elevation as this app.");
    }

    private static SAP2000v1.cOAPI? TryGetHelperSap2000Object(int? processId)
    {
        try
        {
            SAP2000v1.cHelper helper = new SAP2000v1.Helper();
            return processId.HasValue
                ? helper.GetObjectProcess(Sap2000ApiObjectProgId, processId.Value)
                : helper.GetObject(Sap2000ApiObjectProgId);
        }
        catch
        {
            return null;
        }
    }

    private static List<Sap2000InstanceInfo> EnumerateSap2000ProcessInstances(List<string> warnings)
    {
        var instances = new List<Sap2000InstanceInfo>();
        foreach (Process process in Process.GetProcessesByName("SAP2000").OrderBy(process => process.Id))
        {
            string modelFile = "";
            try
            {
                SAP2000v1.cOAPI sapObject = GetSap2000ObjectForProcess(process.Id);
                modelFile = TryGetModelFilename(sapObject);
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 process {process.Id} is visible but could not be queried: {ex.Message}");
            }

            string title = "";
            try { title = process.MainWindowTitle ?? ""; }
            catch { }

            string shortName = string.IsNullOrWhiteSpace(modelFile)
                ? (string.IsNullOrWhiteSpace(title) ? "SAP2000" : title)
                : Path.GetFileName(modelFile);

            instances.Add(new Sap2000InstanceInfo
            {
                Id = $"process:{process.Id.ToString(CultureInfo.InvariantCulture)}",
                DisplayName = $"{shortName} - PID {process.Id.ToString(CultureInfo.InvariantCulture)}",
                ModelFile = modelFile
            });
        }

        return instances;
    }

    private static List<(string DisplayName, SAP2000v1.cOAPI Object)> EnumerateSap2000Objects()
    {
        var objects = new List<(string DisplayName, SAP2000v1.cOAPI Object)>();

        if (GetRunningObjectTable(0, out IRunningObjectTable? rot) != 0 || rot == null)
            return objects;

        if (CreateBindCtx(0, out IBindCtx? bindCtx) != 0 || bindCtx == null)
            return objects;

        rot.EnumRunning(out IEnumMoniker enumMoniker);
        var monikers = new IMoniker[1];
        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            string displayName = "";
            try
            {
                monikers[0].GetDisplayName(bindCtx, null, out displayName);
                rot.GetObject(monikers[0], out object comObject);
                SAP2000v1.cOAPI apiObject = (SAP2000v1.cOAPI)comObject;
                if (IsSap2000Object(apiObject, displayName))
                    objects.Add((displayName, apiObject));
            }
            catch
            {
                // Ignore unrelated or inaccessible ROT entries.
            }
        }

        return objects;
    }

    private static bool IsSap2000Object(SAP2000v1.cOAPI comObject, string displayName)
    {
        if (displayName.Contains("SAP2000", StringComparison.OrdinalIgnoreCase) ||
            displayName.Contains("CSI.SAP2000", StringComparison.OrdinalIgnoreCase))
        {
            return HasSapModel(comObject);
        }

        return HasSapModel(comObject) && TryGetModelFilename(comObject).Length > 0;
    }

    private static bool HasSapModel(SAP2000v1.cOAPI comObject)
    {
        try
        {
            SAP2000v1.cSapModel sapModel = comObject.SapModel;
            return sapModel != null;
        }
        catch
        {
            return false;
        }
    }

    private static SAP2000v1.cSapModel GetRequiredSapModelObject(SAP2000v1.cOAPI sapObject)
    {
        try
        {
            SAP2000v1.cSapModel sapModel = sapObject.SapModel;
            if (sapModel == null)
                throw new InvalidOperationException("Connected to the SAP2000 API object, but SAP2000 did not return SapModel. Open a model in SAP2000 and make sure SAP2000 and this app are running under the same Windows user/elevation.");

            return sapModel;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("SAP2000 returned a COM object, but the SapModel property could not be read. Details: " + ex.Message, ex);
        }
    }

    private static Sap2000InstanceInfo BuildSap2000Instance(SAP2000v1.cOAPI sapObject, string displayName, int index)
    {
        string modelFile = TryGetModelFilename(sapObject);
        string shortName = string.IsNullOrWhiteSpace(modelFile)
            ? $"SAP2000 Instance {index + 1}"
            : Path.GetFileName(modelFile);

        return new Sap2000InstanceInfo
        {
            Id = displayName,
            DisplayName = shortName,
            ModelFile = modelFile,
            RotDisplayName = displayName
        };
    }

    private static string TryGetModelFilename(SAP2000v1.cOAPI sapObject)
    {
        try
        {
            SAP2000v1.cSapModel sapModel = sapObject.SapModel;
            string? fileName = sapModel.GetModelFilename(true);
            return fileName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveSelectedInstanceId(IReadOnlyList<Sap2000InstanceInfo> instances, string? requestedId)
    {
        string requested = (requestedId ?? "").Trim();
        if (requested.Length > 0 && instances.Any(instance => string.Equals(instance.Id, requested, StringComparison.OrdinalIgnoreCase)))
            return requested;

        return instances.FirstOrDefault()?.Id ?? "";
    }

    private static SAP2000v1.cOAPI? TryGetActiveObjectFromRot(string progId)
    {
        if (CLSIDFromProgID(progId, out Guid clsid) != 0)
            return null;

        int result = GetActiveObject(ref clsid, IntPtr.Zero, out object? activeObject);
        if (result != 0 || activeObject == null)
            return null;

        return activeObject as SAP2000v1.cOAPI;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable? prot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx? ppbc);
}
