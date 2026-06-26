using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class Sap2000ModellingService
{
    private const string Sap2000ApiObjectProgId = "CSI.SAP2000.API.SapObject";
    private const SAP2000v1.eUnits Sap2000UnitsKnMC = SAP2000v1.eUnits.kN_m_C;
    private const SAP2000v1.eItemType Sap2000Objects = SAP2000v1.eItemType.Objects;
    private const int Sap2000SelectedPointObjectType = 1;
    private const int Sap2000SelectedFrameObjectType = 2;

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
            result.FrameSections = GetFrameSectionNames(sapModel, warnings);
            result.CableSections = GetCableSectionNames(sapModel, warnings);
            result.TendonSections = GetTendonSectionNames(sapModel, warnings);
            result.TensionMemberSections = result.CableSections
                .Concat(result.TendonSections)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Groups = GetGroupNames(sapModel, warnings);
            result.Message = $"Loaded {result.FrameSections.Count} SAP2000 frame section(s), {result.CableSections.Count} cable section(s), and {result.TendonSections.Count} tendon section(s).";
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

                    if (member.IsTensionOnly)
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
                    objects.Add(frameName);
                    frameCount++;
                }

                foreach (CityNode support in model.Nodes.Where(n => n.IsSupport))
                {
                    if (points.TryGetValue(support.Key, out string? pointName))
                        TrySetPointRestraint(sapModel, pointName, [true, true, true, false, false, false], $"City support '{support.Key}'", warnings);
                }

                TryRefreshSap2000View(sapModel);
                return new CityOfTomorrowDrawResult
                {
                    IsError = objects.Count == 0,
                    Message = $"Drawn {frameCount} frame object(s), {cableCount} cable object(s), and {tendonCount} tendon object(s) in SAP2000 group '{groupName}'.",
                    FrameCount = frameCount,
                    TensionOnlyCount = tensionCount,
                    GroupName = groupName,
                    ObjectNames = objects,
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
            return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, Warnings = warnings };
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
