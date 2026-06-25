using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed partial class EtabsParametricModellingService
{
    public CityOfTomorrowDrawResult DrawCityOfTomorrow(CityOfTomorrowDrawRequest request)
    {
        var warnings = new List<string>();
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
                    if (member.IsTensionOnly)
                    {
                        TryAssignTrussReleases(sapModel, frameName, member.Id, warnings);
                        int limitRet = sapModel.FrameObj.SetTCLimits(frameName, true, 0, false, 0, EtabsObjects);
                        if (limitRet != 0) warnings.Add($"Could not assign zero compression capacity to '{member.Id}'. Return code: {limitRet}.");
                        tensionCount++;
                    }
                    objects.Add(frameName);
                }

                foreach (CityNode support in model.Nodes.Where(n => n.IsSupport))
                    if (points.TryGetValue(support.Key, out string? pointName))
                        TrySetPointRestraint(sapModel, pointName, [true, true, true, false, false, false], $"City support '{support.Key}'", warnings);

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
                    FrameCount = objects.Count, TensionOnlyCount = tensionCount, GroupName = groupName, ObjectNames = objects, Warnings = warnings
                };
            }
            finally { if (originalUnits != null) TryRestorePresentUnits(sapModel, originalUnits.Value); }
        }
        catch (Exception ex) { return new CityOfTomorrowDrawResult { IsError = true, Message = ex.Message, Warnings = warnings }; }
    }

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
