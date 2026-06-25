using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed partial class EtabsParametricModellingService
{
    private const string EtabsApiObjectProgId = "CSI.ETABS.API.ETABSObject";
    private const ETABSv1.eUnits EtabsUnitsKnMC = ETABSv1.eUnits.kN_m_C;
    private const ETABSv1.eItemType EtabsObjects = ETABSv1.eItemType.Objects;
    private const int EtabsSelectedPointObjectType = 1;
    private const int EtabsSelectedFrameObjectType = 2;
    private const int EtabsSelectedAreaObjectType = 5;

    public EtabsInstanceListResult ListEtabsInstances()
    {
        var warnings = new List<string>();
        var instances = EnumerateEtabsProcessInstances(warnings);

        if (instances.Count == 0)
        {
            instances.AddRange(EnumerateEtabsObjects()
                .Select((item, index) => BuildEtabsInstance(item.Object, item.DisplayName, index)));
        }

        instances = instances
            .GroupBy(instance => instance.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (instances.Count == 0)
        {
            try
            {
                ETABSv1.cOAPI etabsObject = GetActiveEtabsObject();
                instances.Add(BuildEtabsInstance(etabsObject, "active", 0));
            }
            catch
            {
                // The message below tells the UI that no ETABS API object is available.
            }
        }

        return new EtabsInstanceListResult
        {
            IsError = false,
            Message = instances.Count == 0
                ? "No active ETABS API instance was found."
                : $"Found {instances.Count} ETABS instance option(s).",
            Instances = instances,
            Warnings = warnings
        };
    }

    public EtabsParametricModelDataResult ListParametricModelData(EtabsParametricModelDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);

        var result = new EtabsParametricModelDataResult
        {
            IsError = false,
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
            ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);

            result.FrameSections = GetFrameSectionNames(sapModel, warnings);
            result.ShellProperties = GetAreaPropertyNames(sapModel, warnings);
            result.Materials = GetMaterialNames(sapModel, warnings);
            result.LoadPatterns = GetLoadPatternNames(sapModel, warnings);
            result.LoadCombinations = GetComboNames(sapModel, warnings);
            result.Stories = GetStoryNames(sapModel, warnings);
            result.Groups = GetGroupNames(sapModel, warnings);
            result.Message = $"Loaded {result.FrameSections.Count} frame section(s), {result.ShellProperties.Count} shell property item(s), {result.LoadPatterns.Count} load pattern(s), and {result.LoadCombinations.Count} load combination(s).";
        }
        catch (Exception ex)
        {
            result.IsError = true;
            result.Message = ex.Message;
        }

        return result;
    }

    public EtabsSelectedInsertionPointsResult ReadSelectedInsertionPoints(EtabsSelectedInsertionPointsRequest request)
    {
        var warnings = new List<string>();

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);

            int numberItems = 0;
            int[] objectTypes = [];
            string[] objectNames = [];
            int ret = sapModel.SelectObj.GetSelected(ref numberItems, ref objectTypes, ref objectNames);
            if (ret != 0)
                throw new InvalidOperationException("Unable to read selected objects from ETABS.");

            var pointNames = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int ignoredCount = 0;
            int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));

            for (int index = 0; index < count; index++)
            {
                string name = (objectNames[index] ?? "").Trim();
                if (name.Length == 0)
                    continue;

                if (objectTypes[index] != EtabsSelectedPointObjectType)
                {
                    ignoredCount++;
                    continue;
                }

                if (seen.Add(name))
                    pointNames.Add(name);
            }

            if (ignoredCount > 0)
                warnings.Add($"Ignored {ignoredCount} selected ETABS object(s) that are not point/joint objects.");

            if (pointNames.Count != 2)
            {
                return new EtabsSelectedInsertionPointsResult
                {
                    IsError = true,
                    Message = $"Select exactly two ETABS point objects before reading insertion points. Current selected point count: {pointNames.Count}.",
                    Warnings = warnings
                };
            }

            var points = new List<EtabsPointInfo>();
            foreach (string pointName in pointNames)
            {
                (double X, double Y, double Z) coordinates = GetPointCoordinates(sapModel, pointName);
                points.Add(new EtabsPointInfo
                {
                    Name = pointName,
                    X = coordinates.X,
                    Y = coordinates.Y,
                    Z = coordinates.Z
                });
            }

            return new EtabsSelectedInsertionPointsResult
            {
                IsError = false,
                Message = "Loaded two ETABS insertion points.",
                Points = points,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new EtabsSelectedInsertionPointsResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public EtabsFrameSectionImportResult ImportFrameSections(EtabsFrameSectionImportRequest request)
    {
        var warnings = new List<string>();

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);

                string groupName = (request.GroupName ?? "").Trim();
                List<string> frameNames = groupName.Length > 0
                    ? ReadFrameNamesInGroup(sapModel, groupName, warnings)
                    : request.UseSelectedFrames
                        ? ReadSelectedFrameNames(sapModel, warnings)
                        : GetAllFrameNames(sapModel, warnings);

                if (frameNames.Count == 0)
                {
                    return new EtabsFrameSectionImportResult
                    {
                        IsError = true,
                        Message = groupName.Length > 0
                            ? $"No ETABS frame objects were found in group '{groupName}'."
                            : request.UseSelectedFrames
                                ? "No ETABS frame objects are selected. Select frame objects in ETABS or switch to all-frame import."
                                : "No ETABS frame objects were found in the selected ETABS model.",
                        Warnings = warnings
                    };
                }

                var rows = new List<EtabsFrameSectionRow>();
                foreach (string frameName in frameNames)
                    rows.Add(ReadFrameSectionRow(sapModel, frameName, warnings, groupName));

                return new EtabsFrameSectionImportResult
                {
                    IsError = false,
                    Message = groupName.Length > 0
                        ? $"Imported {rows.Count} ETABS frame object(s) from group '{groupName}' for section editing."
                        : $"Imported {rows.Count} ETABS frame object(s) for section editing.",
                    Frames = rows,
                    FrameSections = GetFrameSectionNames(sapModel, warnings),
                    Groups = GetGroupNames(sapModel, warnings),
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
            return new EtabsFrameSectionImportResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public EtabsFrameSectionUpdateResult UpdateFrameSections(EtabsFrameSectionUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            List<EtabsFrameSectionRow> targetFrames = request.Frames
                .Where(frame => frame.Include)
                .Where(frame => !string.IsNullOrWhiteSpace(frame.FrameName))
                .ToList();

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);

            return UpdateFrameSectionsCore(sapModel, targetFrames, warnings, "ETABS frame section assignment");
        }
        catch (Exception ex)
        {
            return new EtabsFrameSectionUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public EtabsFrameLoadUpdateResult UpdateFrameDistributedLoads(EtabsFrameLoadUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            List<EtabsFrameSectionRow> targetFrames = request.Frames
                .Where(frame => frame.Include)
                .Where(frame => !string.IsNullOrWhiteSpace(frame.FrameName))
                .ToList();

            if (targetFrames.Count == 0)
                throw new InvalidOperationException("No frame rows are checked for load update.");

            string loadPattern = (request.LoadPattern ?? "").Trim();
            if (loadPattern.Length == 0)
                throw new InvalidOperationException("Select a load pattern before updating existing frame loads.");

            if (!double.IsFinite(request.LineLoadKnPerM) || Math.Abs(request.LineLoadKnPerM) < 0.000001)
                throw new InvalidOperationException("Enter a non-zero distributed load before updating existing frame loads.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                List<string> loadPatterns = GetLoadPatternNames(sapModel, warnings);
                if (loadPatterns.Count > 0 && !loadPatterns.Contains(loadPattern, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Load pattern '{loadPattern}' does not exist in the connected ETABS model.");

                double loadValue = -Math.Abs(request.LineLoadKnPerM);
                int direction = ToEtabsDistributedLoadDirection("GlobalZ");
                int updatedCount = 0;
                var updatedFrameNames = new List<string>();

                foreach (EtabsFrameSectionRow frame in targetFrames)
                {
                    try
                    {
                        if (request.ReplaceSelectedPatternLoads)
                            TryClearFrameDistributedLoads(sapModel, frame.FrameName, warnings, loadPattern);

                        int ret = sapModel.FrameObj.SetLoadDistributed(
                            frame.FrameName,
                            loadPattern,
                            1,
                            direction,
                            0,
                            1,
                            loadValue,
                            loadValue,
                            "Global",
                            true,
                            false,
                            EtabsObjects);

                        if (ret != 0)
                        {
                            warnings.Add($"ETABS could not update distributed load on frame '{frame.FrameName}'. Return code: {ret}.");
                            continue;
                        }

                        updatedCount++;
                        updatedFrameNames.Add(frame.FrameName);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Frame '{frame.FrameName}' load update failed: {ex.Message}");
                    }
                }

                TryRefreshEtabsView(sapModel);

                return new EtabsFrameLoadUpdateResult
                {
                    IsError = updatedCount == 0,
                    Message = updatedCount == 0
                        ? "No existing ETABS frame distributed loads were updated."
                        : $"Updated distributed loads on {updatedCount} existing ETABS frame object(s).",
                    UpdatedCount = updatedCount,
                    UpdatedFrameNames = updatedFrameNames,
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
            return new EtabsFrameLoadUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public EtabsFrameGroupAssignResult AssignSelectedFramesToGroup(EtabsFrameGroupAssignRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string groupName = (request.GroupName ?? "").Trim();
            if (groupName.Length == 0)
                throw new InvalidOperationException("Select or enter an ETABS group name before assigning selected frames.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            List<string> frameNames = ReadSelectedFrameNames(sapModel, warnings);
            if (frameNames.Count == 0)
                throw new InvalidOperationException("No ETABS frame objects are selected. Select frame members in ETABS before assigning them to a group.");

            TryUnlockModelForDrawing(sapModel, warnings);
            groupName = EnsureEtabsGroup(sapModel, groupName, warnings);

            int assignedCount = 0;
            var assignedFrameNames = new List<string>();
            foreach (string frameName in frameNames)
            {
                try
                {
                    int ret = sapModel.FrameObj.SetGroupAssign(frameName, groupName, false, EtabsObjects);
                    if (ret != 0)
                    {
                        warnings.Add($"ETABS could not assign frame '{frameName}' to group '{groupName}'. Return code: {ret}.");
                        continue;
                    }

                    assignedCount++;
                    assignedFrameNames.Add(frameName);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Frame '{frameName}' group assignment failed: {ex.Message}");
                }
            }

            TryRefreshEtabsView(sapModel);
            List<string> groups = GetGroupNames(sapModel, warnings);

            return new EtabsFrameGroupAssignResult
            {
                IsError = assignedCount == 0,
                Message = assignedCount == 0
                    ? $"No selected ETABS frame object(s) were assigned to group '{groupName}'."
                    : $"Assigned {assignedCount} selected ETABS frame object(s) to group '{groupName}'.",
                AssignedCount = assignedCount,
                GroupName = groupName,
                AssignedFrameNames = assignedFrameNames,
                Groups = groups,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new EtabsFrameGroupAssignResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public EtabsFrameSectionUpdateResult UpdateFrameGroupSection(EtabsFrameGroupSectionUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string groupName = (request.GroupName ?? "").Trim();
            if (groupName.Length == 0)
                throw new InvalidOperationException("Select an ETABS group before applying a section to the group.");

            string sectionName = (request.SectionName ?? "").Trim();
            if (sectionName.Length == 0)
                throw new InvalidOperationException("Select a frame section before applying it to the group.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            List<string> frameNames = ReadFrameNamesInGroup(sapModel, groupName, warnings);
            if (frameNames.Count == 0)
            {
                return new EtabsFrameSectionUpdateResult
                {
                    IsError = true,
                    Message = $"No ETABS frame objects were found in group '{groupName}'.",
                    FrameSections = GetFrameSectionNames(sapModel, warnings),
                    Groups = GetGroupNames(sapModel, warnings),
                    Warnings = warnings
                };
            }

            List<EtabsFrameSectionRow> targetFrames = frameNames
                .Select(frameName => new EtabsFrameSectionRow
                {
                    Include = true,
                    FrameName = frameName,
                    GroupName = groupName,
                    NewSection = sectionName
                })
                .ToList();

            EtabsFrameSectionUpdateResult result = UpdateFrameSectionsCore(
                sapModel,
                targetFrames,
                warnings,
                $"ETABS group '{groupName}' section assignment");
            result.Message = $"Updated {result.UpdatedCount} ETABS frame section assignment(s) in group '{groupName}'.";
            result.FrameSections = GetFrameSectionNames(sapModel, warnings);
            result.Groups = GetGroupNames(sapModel, warnings);
            return result;
        }
        catch (Exception ex)
        {
            return new EtabsFrameSectionUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    private static EtabsFrameSectionUpdateResult UpdateFrameSectionsCore(
        ETABSv1.cSapModel sapModel,
        List<EtabsFrameSectionRow> targetFrames,
        List<string> warnings,
        string description)
    {
        if (targetFrames.Count == 0)
            throw new InvalidOperationException("No frame rows are checked for update.");

        TryUnlockModelForDrawing(sapModel, warnings);
        List<string> frameSections = GetFrameSectionNames(sapModel, warnings);
        HashSet<string> validSections = frameSections.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int updatedCount = 0;
        var updatedFrameNames = new List<string>();

        foreach (EtabsFrameSectionRow frame in targetFrames)
        {
            string newSection = (frame.NewSection ?? "").Trim();
            if (newSection.Length == 0)
            {
                warnings.Add($"Skipped frame '{frame.FrameName}': no new section selected.");
                continue;
            }

            if (validSections.Count > 0 && !validSections.Contains(newSection))
            {
                warnings.Add($"Skipped frame '{frame.FrameName}': section '{newSection}' does not exist in the connected ETABS model.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(frame.CurrentSection) &&
                string.Equals(frame.CurrentSection, newSection, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Skipped frame '{frame.FrameName}': current section is already '{newSection}'.");
                continue;
            }

            try
            {
                int ret = sapModel.FrameObj.SetSection(frame.FrameName, newSection, EtabsObjects, 0, 0);
                if (ret != 0)
                {
                    warnings.Add($"ETABS could not update frame '{frame.FrameName}' to section '{newSection}'. Return code: {ret}.");
                    continue;
                }

                updatedCount++;
                updatedFrameNames.Add(frame.FrameName);
                frame.CurrentSection = newSection;
            }
            catch (Exception ex)
            {
                warnings.Add($"Frame '{frame.FrameName}' section update failed: {ex.Message}");
            }
        }

        TryRefreshEtabsView(sapModel);

        return new EtabsFrameSectionUpdateResult
        {
            IsError = false,
            Message = $"Updated {updatedCount} {description}(s).",
            UpdatedCount = updatedCount,
            UpdatedFrameNames = updatedFrameNames,
            FrameSections = frameSections,
            Groups = GetGroupNames(sapModel, warnings),
            Warnings = warnings
        };
    }

    public LoadCaseCombinationDataResult ListLoadCaseCombinationData(LoadCaseCombinationDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);

        var result = new LoadCaseCombinationDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0 ? instanceResult.Message : "",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            Warnings = warnings
        };

        if (instances.Count == 0)
            return result;

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            result.LoadPatterns = GetLoadPatternRows(sapModel, warnings);
            result.LoadCases = GetLoadCaseRows(sapModel, warnings);
            result.LoadCombinations = GetLoadCombinationRows(sapModel, warnings, result.LoadCases);
            result.Message = $"Loaded {result.LoadPatterns.Count} load pattern(s), {result.LoadCases.Count} load case(s), and {result.LoadCombinations.Count} combination(s).";
        }
        catch (Exception ex)
        {
            result.IsError = true;
            result.Message = ex.Message;
        }

        return result;
    }

    public LoadCaseCombinationUpdateResult UpdateLoadPattern(LoadPatternUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string name = (request.Name ?? "").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Load pattern name is required.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);
            ETABSv1.eLoadPatternType patternType = ParseLoadPatternType(request.PatternType);

            int ret = sapModel.LoadPatterns.Add(name, patternType, request.SelfWeightMultiplier, true);
            if (ret != 0)
            {
                int typeRet = sapModel.LoadPatterns.SetLoadType(name, patternType);
                int swRet = sapModel.LoadPatterns.SetSelfWTMultiplier(name, request.SelfWeightMultiplier);
                if (typeRet != 0 || swRet != 0)
                    throw new InvalidOperationException($"ETABS could not add or update load pattern '{name}'. Add return: {ret}, type return: {typeRet}, self-weight return: {swRet}.");
            }

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"Load pattern '{name}' was added/updated.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new LoadCaseCombinationUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public LoadCaseCombinationUpdateResult UpdateStaticLoadCase(StaticLoadCaseUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string caseName = (request.Name ?? "").Trim();
            if (caseName.Length == 0)
                throw new InvalidOperationException("Static case name is required.");

            List<StaticLoadCaseItemRow> items = request.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Clone())
                .ToList();

            if (items.Count == 0)
            {
                string patternName = (request.LoadPatternName ?? "").Trim();
                if (patternName.Length == 0)
                    throw new InvalidOperationException("Add at least one load pattern item for the static case.");

                items.Add(new StaticLoadCaseItemRow
                {
                    LoadType = "Load",
                    Name = patternName,
                    ScaleFactor = request.ScaleFactor
                });
            }

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.LoadCases.StaticLinear.SetCase(caseName);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not create/update static linear case '{caseName}'. Return code: {ret}.");

            int numberLoads = items.Count;
            string[] loadTypes = items.Select(item => string.IsNullOrWhiteSpace(item.LoadType) ? "Load" : item.LoadType.Trim()).ToArray();
            string[] loadNames = items.Select(item => item.Name.Trim()).ToArray();
            double[] scaleFactors = items.Select(item => item.ScaleFactor).ToArray();
            ret = sapModel.LoadCases.StaticLinear.SetLoads(caseName, numberLoads, ref loadTypes, ref loadNames, ref scaleFactors);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not assign load items to static case '{caseName}'. Return code: {ret}.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"Static linear case '{caseName}' was added/updated.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new LoadCaseCombinationUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public LoadCaseCombinationUpdateResult UpdateLoadCombination(LoadCombinationUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string comboName = (request.Name ?? "").Trim();
            if (comboName.Length == 0)
                throw new InvalidOperationException("Combination name is required.");

            List<EtabsComboItemRow> items = request.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Clone())
                .ToList();
            if (items.Count == 0)
                throw new InvalidOperationException("Add at least one load case or combination item.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);
            int comboType = ParseComboType(request.ComboType);

            int ret = sapModel.RespCombo.Add(comboName, comboType);
            if (ret != 0)
            {
                int existingType = 0;
                int typeRet = sapModel.RespCombo.GetTypeOAPI(comboName, ref existingType);
                if (typeRet != 0)
                    throw new InvalidOperationException($"ETABS could not add or find response combination '{comboName}'. Return code: {ret}.");

                if (existingType != comboType)
                {
                    int deleteRet = sapModel.RespCombo.Delete(comboName);
                    if (deleteRet != 0)
                        throw new InvalidOperationException($"Existing combination '{comboName}' has a different type and could not be replaced. Return code: {deleteRet}.");

                    ret = sapModel.RespCombo.Add(comboName, comboType);
                    if (ret != 0)
                        throw new InvalidOperationException($"ETABS could not recreate response combination '{comboName}'. Return code: {ret}.");
                }
            }

            DeleteExistingComboItems(sapModel, comboName, warnings);
            var addFailures = new List<string>();
            foreach (EtabsComboItemRow item in items)
            {
                string itemName = item.Name.Trim();
                ETABSv1.eCNameType sourceType = ParseComboSourceType(item.SourceType);
                ret = sapModel.RespCombo.SetCaseList(comboName, ref sourceType, itemName, item.Factor);
                if (ret != 0)
                    addFailures.Add($"'{itemName}' returned {ret}");
            }

            if (addFailures.Count > 0)
                throw new InvalidOperationException($"ETABS could not add {addFailures.Count} item(s) to combination '{comboName}': {string.Join(", ", addFailures)}.");

            List<EtabsComboItemRow> savedItems = GetResponseComboItems(sapModel, comboName, warnings);
            List<string> missingItems = items
                .Where(item => !ContainsComboItem(savedItems, item))
                .Select(item => $"{item.Factor.ToString("0.###", CultureInfo.InvariantCulture)} {(item.Name ?? "").Trim()}")
                .ToList();
            if (missingItems.Count > 0)
                throw new InvalidOperationException($"ETABS accepted the update call, but these item(s) were not found after reading '{comboName}' back: {string.Join(", ", missingItems)}.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"Combination '{comboName}' was added/updated with {items.Count} item(s).",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new LoadCaseCombinationUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public LoadCaseCombinationUpdateResult DeleteLoadPattern(LoadCaseCombinationDeleteRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string name = (request.Name ?? "").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Select a load pattern to delete.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.LoadPatterns.Delete(name);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not delete load pattern '{name}'. Return code: {ret}. Check whether this pattern is used by a load case or assigned load.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"Deleted load pattern '{name}'.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new LoadCaseCombinationUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public LoadCaseCombinationUpdateResult DeleteLoadCase(LoadCaseCombinationDeleteRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string name = (request.Name ?? "").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Select a load case to delete.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.LoadCases.Delete(name);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not delete load case '{name}'. Return code: {ret}. Check whether this case is used by a response combination.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"Deleted load case '{name}'.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new LoadCaseCombinationUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public LoadCaseCombinationUpdateResult DeleteLoadCombination(LoadCaseCombinationDeleteRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string name = (request.Name ?? "").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Select a combination to delete.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.RespCombo.Delete(name);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not delete response combination '{name}'. Return code: {ret}. Check whether this combination is used by another combination or design setting.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"Deleted response combination '{name}'.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new LoadCaseCombinationUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyDataResult ListSectionPropertyData(SectionPropertyDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);

        var result = new SectionPropertyDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0 ? instanceResult.Message : "",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            Warnings = warnings
        };

        if (instances.Count == 0)
            return result;

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                result.Materials = GetMaterialPropertyRows(sapModel, warnings);
                result.FrameProperties = GetFramePropertyRows(sapModel, warnings);
                result.AreaProperties = GetAreaPropertyRows(sapModel, warnings);
                result.Message = $"Loaded {result.Materials.Count} material(s), {result.FrameProperties.Count} frame section(s), and {result.AreaProperties.Count} slab/wall item(s).";
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }
        }
        catch (Exception ex)
        {
            result.IsError = true;
            result.Message = ex.Message;
        }

        return result;
    }

    public ModelCompareSnapshotResult ExtractModelCompareSnapshot(ModelCompareSnapshotRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);

        var result = new ModelCompareSnapshotResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0 ? instanceResult.Message : "",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            Warnings = warnings
        };

        if (instances.Count == 0)
            return result;

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);

                List<ModelCompareFramePropertySnapshot> frameProperties = GetModelCompareFrameProperties(sapModel, warnings);
                Dictionary<string, string> materialBySection = frameProperties
                    .Where(section => !string.IsNullOrWhiteSpace(section.SectionName))
                    .GroupBy(section => section.SectionName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().MaterialName, StringComparer.OrdinalIgnoreCase);

                Dictionary<string, List<string>> groupsByFrameName = GetModelCompareFrameGroups(sapModel, warnings);
                List<ModelCompareFrameSnapshot> frames = GetAllFrameNames(sapModel, warnings)
                    .Select(frameName => ReadModelCompareFrameSnapshot(sapModel, frameName, materialBySection, groupsByFrameName, warnings))
                    .OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                result.Snapshot = new ModelCompareSnapshot
                {
                    Metadata = new ModelCompareSnapshotMetadata
                    {
                        ProductName = TryGetEtabsProductName(sapModel),
                        SourceModelFileName = TryGetModelFilename(etabsObject),
                        SnapshotCreatedAt = DateTimeOffset.Now,
                        Units = EtabsUnitsKnMC.ToString()
                    },
                    Frames = frames,
                    FrameProperties = frameProperties,
                    AreaProperties = GetAreaPropertyRows(sapModel, warnings)
                        .Select(MapModelCompareAreaProperty)
                        .ToList(),
                    Materials = GetMaterialPropertyRows(sapModel, warnings)
                        .Select(MapModelCompareMaterial)
                        .ToList()
                };

                result.IsError = false;
                result.Message = $"Extracted model snapshot with {frames.Count} frame object(s), {frameProperties.Count} frame propertie(s), {result.Snapshot.AreaProperties.Count} area propertie(s), and {result.Snapshot.Materials.Count} material(s).";
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }
        }
        catch (Exception ex)
        {
            result.IsError = true;
            result.Message = ex.Message;
        }

        return result;
    }

    public SteelSectionCatalogResult ListSteelSectionCatalog(SteelSectionCatalogRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string databaseFile = ResolveSteelPropertyDatabaseFile(request.DatabaseFile);
            if (databaseFile.Length == 0)
                throw new InvalidOperationException("Steel database file name/path is required.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eFramePropType shapeType = ParseFramePropType(request.ShapeType);

            int numberNames = 0;
            string[] sectionNames = [];
            ETABSv1.eFramePropType[] propTypes = [];
            int ret = sapModel.PropFrame.GetPropFileNameList(databaseFile, ref numberNames, ref sectionNames, ref propTypes, shapeType);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not read steel section database '{databaseFile}'. Return code: {ret}.");

            List<string> names = sectionNames
                .Take(Math.Min(numberNames, sectionNames.Length))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
                warnings.Add($"No sections were returned from '{databaseFile}' for shape '{request.ShapeType}'. Check the ETABS database file name/path and shape filter.");

            return new SteelSectionCatalogResult
            {
                IsError = false,
                Message = $"Loaded {names.Count} steel database section name(s).",
                SectionNames = names,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SteelSectionCatalogResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyUpdateResult ImportSteelFrameProperty(SteelSectionImportRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string propertyName = (request.PropertyName ?? "").Trim();
            string materialName = (request.MaterialName ?? "").Trim();
            string databaseFile = ResolveSteelPropertyDatabaseFile(request.DatabaseFile);
            string databaseSectionName = (request.DatabaseSectionName ?? "").Trim();

            if (propertyName.Length == 0)
                throw new InvalidOperationException("Imported frame property name is required.");
            if (materialName.Length == 0)
                throw new InvalidOperationException("Select a material for the imported steel section.");
            if (databaseFile.Length == 0)
                throw new InvalidOperationException("Steel database file name/path is required.");
            if (databaseSectionName.Length == 0)
                throw new InvalidOperationException("Select or type the steel database section name to import.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.PropFrame.ImportProp(propertyName, materialName, databaseFile, databaseSectionName, -1, "Imported from Sections / Materials tab", "");
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not import steel section '{databaseSectionName}' as '{propertyName}'. Return code: {ret}.");

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Imported steel frame section '{propertyName}'.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyUpdateResult UpdateMaterialProperty(MaterialPropertyUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string materialName = (request.Name ?? "").Trim();
            if (materialName.Length == 0)
                throw new InvalidOperationException("Material name is required.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                ETABSv1.eMatType materialType = ParseMaterialType(request.MaterialType);
                int ret = sapModel.PropMaterial.SetMaterial(materialName, materialType, -1, "Created/updated from Sections / Materials tab", "");
                if (ret != 0)
                    throw new InvalidOperationException($"ETABS could not create/update material '{materialName}'. Return code: {ret}.");

                ret = sapModel.PropMaterial.SetMPIsotropic(
                    materialName,
                    MpaToKnPerM2(EnsurePositive(request.ElasticModulusMpa, "Elastic modulus")),
                    Math.Clamp(request.PoissonRatio, 0.0, 0.49),
                    request.ThermalExpansion,
                    0);
                if (ret != 0)
                    throw new InvalidOperationException($"ETABS could not assign isotropic properties to material '{materialName}'. Return code: {ret}.");

                ret = sapModel.PropMaterial.SetWeightAndMass(materialName, 1, Math.Max(0.0, request.UnitWeightKnPerM3), 0);
                if (ret != 0)
                    warnings.Add($"ETABS could not assign unit weight to material '{materialName}'. Return code: {ret}.");

                if (materialType == ETABSv1.eMatType.Concrete)
                {
                    ret = sapModel.PropMaterial.SetOConcrete(
                        materialName,
                        MpaToKnPerM2(EnsurePositive(request.ConcreteFcMpa, "Concrete f'c")),
                        false,
                        0,
                        0,
                        0,
                        0.0022,
                        0.0052,
                        0,
                        0,
                        0);
                    if (ret != 0)
                        warnings.Add($"ETABS could not assign concrete design properties to material '{materialName}'. Return code: {ret}.");
                }
                else if (materialType == ETABSv1.eMatType.Steel)
                {
                    double fy = MpaToKnPerM2(EnsurePositive(request.SteelFyMpa, "Steel Fy"));
                    double fu = MpaToKnPerM2(Math.Max(request.SteelFuMpa, request.SteelFyMpa));
                    ret = sapModel.PropMaterial.SetOSteel(materialName, fy, fu, fy, fu, 1, 1, 0.015, 0.11, 0.17, 0);
                    if (ret != 0)
                        warnings.Add($"ETABS could not assign steel design properties to material '{materialName}'. Return code: {ret}.");
                }
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Material '{materialName}' was added/updated.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyUpdateResult UpdateFrameProperty(FramePropertyUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string propertyName = (request.Name ?? "").Trim();
            string materialName = (request.MaterialName ?? "").Trim();
            if (propertyName.Length == 0)
                throw new InvalidOperationException("Frame section name is required.");
            if (materialName.Length == 0)
                throw new InvalidOperationException("Select a material for the frame section.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                string shape = NormalizeEtabsLabel(request.ShapeType);
                int ret = shape switch
                {
                    "ConcreteCircular" or "Circular" or "Circle" =>
                        sapModel.PropFrame.SetCircle(propertyName, materialName, EnsurePositive(request.Depth, "Diameter"), -1, "Created/updated from Sections / Materials tab", ""),
                    "SteelI" or "I" or "ISection" =>
                        sapModel.PropFrame.SetISection(
                            propertyName,
                            materialName,
                            EnsurePositive(request.Depth, "Depth"),
                            EnsurePositive(request.Width, "Flange width"),
                            EnsurePositive(request.FlangeThickness, "Flange thickness"),
                            EnsurePositive(request.WebThickness, "Web thickness"),
                            EnsurePositive(request.Width, "Bottom flange width"),
                            EnsurePositive(request.FlangeThickness, "Bottom flange thickness"),
                            -1,
                            "Created/updated from Sections / Materials tab",
                            ""),
                    "SteelChannel" or "Channel" =>
                        sapModel.PropFrame.SetChannel(
                            propertyName,
                            materialName,
                            EnsurePositive(request.Depth, "Depth"),
                            EnsurePositive(request.Width, "Flange width"),
                            EnsurePositive(request.FlangeThickness, "Flange thickness"),
                            EnsurePositive(request.WebThickness, "Web thickness"),
                            -1,
                            "Created/updated from Sections / Materials tab",
                            ""),
                    "SteelTube" or "Tube" or "Box" =>
                        sapModel.PropFrame.SetTube(
                            propertyName,
                            materialName,
                            EnsurePositive(request.Depth, "Depth"),
                            EnsurePositive(request.Width, "Width"),
                            EnsurePositive(request.FlangeThickness, "Flange/wall thickness"),
                            EnsurePositive(request.WebThickness, "Web/wall thickness"),
                            -1,
                            "Created/updated from Sections / Materials tab",
                            ""),
                    "SteelPipe" or "Pipe" =>
                        sapModel.PropFrame.SetPipe(
                            propertyName,
                            materialName,
                            EnsurePositive(request.Depth, "Diameter"),
                            EnsurePositive(request.FlangeThickness, "Wall thickness"),
                            -1,
                            "Created/updated from Sections / Materials tab",
                            ""),
                    _ =>
                        sapModel.PropFrame.SetRectangle(
                            propertyName,
                            materialName,
                            EnsurePositive(request.Depth, "Depth"),
                            EnsurePositive(request.Width, "Width"),
                            -1,
                            "Created/updated from Sections / Materials tab",
                            "")
                };

                if (ret != 0)
                    throw new InvalidOperationException($"ETABS could not create/update frame section '{propertyName}'. Return code: {ret}.");
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Frame section '{propertyName}' was added/updated.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyUpdateResult UpdateAreaProperty(AreaPropertyUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string propertyName = (request.Name ?? "").Trim();
            string materialName = (request.MaterialName ?? "").Trim();
            if (propertyName.Length == 0)
                throw new InvalidOperationException("Slab/wall property name is required.");
            if (materialName.Length == 0)
                throw new InvalidOperationException("Select a material for the slab/wall property.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                ETABSv1.eShellType shellType = ParseShellType(request.ShellType);
                double thickness = EnsurePositive(request.Thickness, "Thickness");
                int ret;

                if (string.Equals(request.AreaType, "Wall", StringComparison.OrdinalIgnoreCase))
                {
                    ret = sapModel.PropArea.SetWall(
                        propertyName,
                        ETABSv1.eWallPropType.Specified,
                        shellType,
                        materialName,
                        thickness,
                        -1,
                        "Created/updated from Sections / Materials tab",
                        "");
                }
                else
                {
                    ret = sapModel.PropArea.SetSlab(
                        propertyName,
                        ParseSlabType(request.SlabType),
                        shellType,
                        materialName,
                        thickness,
                        -1,
                        "Created/updated from Sections / Materials tab",
                        "");
                }

                if (ret != 0)
                    throw new InvalidOperationException($"ETABS could not create/update slab/wall property '{propertyName}'. Return code: {ret}.");
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Slab/wall property '{propertyName}' was added/updated.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public TaperedSteelSelectionResult ReadTaperedSteelBaseSection(TaperedSteelBaseSectionRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string sectionName = (request.SectionName ?? "").Trim();
            if (sectionName.Length == 0)
                throw new InvalidOperationException("Select or import a base steel I or steel tube/box section before creating a tapered section.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);

                TaperedSteelSectionGeometry geometry = GetTaperedSteelBaseSectionGeometry(sapModel, sectionName);
                ValidateBaseTaperedSteelGeometry(geometry);

                TaperedSteelSelection selection = new()
                {
                    Frames = [],
                    BaseGeometry = geometry,
                    BaseSectionName = sectionName,
                    MaterialName = geometry.MaterialName,
                    LengthM = 6.0
                };

                warnings.Add("This tool creates ETABS nonprismatic analysis properties. Final steel design of tapered or cut members still needs separate station-by-station checks.");

                return new TaperedSteelSelectionResult
                {
                    IsError = false,
                    Message = $"Loaded base steel section '{sectionName}' for tapered steel generation.",
                    Selection = selection,
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
            return new TaperedSteelSelectionResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public TaperedSteelApplyResult PreviewTaperedSteelSection(TaperedSteelApplyRequest request)
    {
        var warnings = new List<string>();

        try
        {
            TaperedSteelGenerationPreview preview = BuildTaperedSteelPreview(request, warnings);
            return new TaperedSteelApplyResult
            {
                IsError = false,
                Message = $"Preview ready for nonprismatic section '{preview.NonPrismaticSectionName}'.",
                Preview = preview,
                CreatedOrReusedSections = preview.Stations
                    .Select(station => station.SectionName)
                    .Append(preview.NonPrismaticSectionName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AssignedFrameNames = [],
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new TaperedSteelApplyResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public TaperedSteelApplyResult CreateTaperedSteelSection(TaperedSteelApplyRequest request)
    {
        var warnings = new List<string>();

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                TaperedSteelGenerationPreview preview = BuildTaperedSteelPreview(request, warnings);
                ValidateTaperedSteelMaterialExists(sapModel, request.Selection.MaterialName, warnings);

                var createdOrReused = new List<string>();
                CreateTaperedSteelStationSections(sapModel, request, preview, createdOrReused, warnings);
                CreateTaperedSteelNonPrismaticSection(sapModel, preview, createdOrReused);
                TryRefreshEtabsView(sapModel);

                return new TaperedSteelApplyResult
                {
                    IsError = false,
                    Message = $"Created/updated tapered nonprismatic section '{preview.NonPrismaticSectionName}'.",
                    Preview = preview,
                    CreatedOrReusedSections = createdOrReused
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    AssignedFrameNames = [],
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
            return new TaperedSteelApplyResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public TaperedSteelApplyResult AssignTaperedSteelSectionToSelectedFrames(TaperedSteelApplyRequest request)
    {
        var warnings = new List<string>();

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                TaperedSteelGenerationPreview preview = BuildTaperedSteelPreview(request, warnings);
                List<string> frameSections = GetFrameSectionNames(sapModel, warnings);
                if (frameSections.Count > 0 && !frameSections.Contains(preview.NonPrismaticSectionName, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Tapered section '{preview.NonPrismaticSectionName}' does not exist in ETABS yet. Create the tapered section first, then assign it.");

                List<string> frameNames = ReadSelectedFrameNames(sapModel, warnings);
                if (frameNames.Count == 0)
                    throw new InvalidOperationException("No ETABS frame objects are selected. Select the target frame member(s) in ETABS before assigning the tapered section.");

                var assignedFrames = new List<string>();
                foreach (string frameName in frameNames)
                {
                    int assignRet = sapModel.FrameObj.SetSection(
                        frameName,
                        preview.NonPrismaticSectionName,
                        EtabsObjects,
                        0,
                        0);

                    if (assignRet != 0)
                    {
                        warnings.Add($"ETABS could not assign nonprismatic section '{preview.NonPrismaticSectionName}' to frame '{frameName}'. Return code: {assignRet}.");
                        continue;
                    }

                    assignedFrames.Add(frameName);
                }

                TryRefreshEtabsView(sapModel);

                return new TaperedSteelApplyResult
                {
                    IsError = assignedFrames.Count == 0,
                    Message = assignedFrames.Count == 0
                        ? "No selected frame members were assigned the tapered section."
                        : $"Assigned tapered section '{preview.NonPrismaticSectionName}' to {assignedFrames.Count} selected frame member(s).",
                    Preview = preview,
                    CreatedOrReusedSections = [],
                    AssignedFrameNames = assignedFrames,
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
            return new TaperedSteelApplyResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyUpdateResult DeleteMaterialProperty(SectionPropertyDeleteRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string propertyName = (request.Name ?? "").Trim();
            if (propertyName.Length == 0)
                throw new InvalidOperationException("Select a material to delete.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.PropMaterial.Delete(propertyName);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not delete material '{propertyName}'. Return code: {ret}. Check whether it is used by any section property.");

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Deleted material '{propertyName}'.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyUpdateResult DeleteFrameProperty(SectionPropertyDeleteRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string propertyName = (request.Name ?? "").Trim();
            if (propertyName.Length == 0)
                throw new InvalidOperationException("Select a frame section to delete.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.PropFrame.Delete(propertyName);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not delete frame section '{propertyName}'. Return code: {ret}. Check whether it is assigned to any frame or used by an auto-select list.");

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Deleted frame section '{propertyName}'.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public SectionPropertyUpdateResult DeleteAreaProperty(SectionPropertyDeleteRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string propertyName = (request.Name ?? "").Trim();
            if (propertyName.Length == 0)
                throw new InvalidOperationException("Select a slab/wall property to delete.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.PropArea.Delete(propertyName);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not delete slab/wall property '{propertyName}'. Return code: {ret}. Check whether it is assigned to any area object.");

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Deleted slab/wall property '{propertyName}'.",
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public EtabsTrussDrawResult DrawOrUpdateTruss(EtabsTrussDrawRequest request)
    {
        var warnings = new List<string>();

        try
        {
            ParametricTrussModel model = request.Model;
            if (model.Members.Count == 0 || model.Nodes.Count == 0)
                throw new InvalidOperationException("No parametric truss model was provided.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                string exportSuffix = request.AddAsNew ? BuildExportSuffix() : "";
                string groupName = EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.GroupName, exportSuffix), warnings);
                if (request.AddAsNew)
                {
                    warnings.Add($"Add-as-new mode is active. Existing ETABS objects were not deleted; generated objects were assigned to group '{groupName}'.");
                    if (Math.Abs(request.OffsetX) > 0.000001 || Math.Abs(request.OffsetY) > 0.000001 || Math.Abs(request.OffsetZ) > 0.000001)
                        warnings.Add($"Applied add-as-new placement offset: X {request.OffsetX:0.###} m, Y {request.OffsetY:0.###} m, Z {request.OffsetZ:0.###} m.");
                }

                if (request.ReplaceExistingGroup)
                    TryDeleteFramesInGroup(sapModel, groupName, warnings);

                var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
                var nodePointNames = CreateEtabsPointsForNodes(sapModel, model, groupName, exportSuffix, request, warnings);
                var generatedFrames = new List<GeneratedEtabsFrame>();

                foreach (ParametricMember member in model.Members)
                {
                    if (!nodes.ContainsKey(member.StartNodeId) || !nodes.ContainsKey(member.EndNodeId))
                    {
                        warnings.Add($"Skipped member '{member.Id}': node reference could not be resolved.");
                        continue;
                    }

                    if (!nodePointNames.TryGetValue(member.StartNodeId, out string? startPointName) ||
                        !nodePointNames.TryGetValue(member.EndNodeId, out string? endPointName) ||
                        string.IsNullOrWhiteSpace(startPointName) ||
                        string.IsNullOrWhiteSpace(endPointName))
                    {
                        warnings.Add($"Skipped member '{member.Id}': shared ETABS point objects were not available for both end nodes.");
                        continue;
                    }

                    string sectionName = (member.SectionName ?? "").Trim();
                    if (sectionName.Length == 0)
                    {
                        warnings.Add($"Skipped member '{member.Id}': no frame section selected.");
                        continue;
                    }

                    string preferredFrameName = BuildExportObjectName(member.Id, exportSuffix);
                    string frameName = "";
                    int ret = sapModel.FrameObj.AddByPoint(
                        startPointName,
                        endPointName,
                        ref frameName,
                        sectionName,
                        preferredFrameName);

                    if (ret != 0)
                    {
                        frameName = "";
                        ret = sapModel.FrameObj.AddByPoint(
                            startPointName,
                            endPointName,
                            ref frameName,
                            sectionName,
                            "");

                        if (ret == 0)
                            warnings.Add($"Member '{member.Id}' was drawn with ETABS automatic frame name because the preferred name '{preferredFrameName}' was unavailable.");
                    }

                    if (ret != 0 || string.IsNullOrWhiteSpace(frameName))
                    {
                        warnings.Add($"ETABS could not draw member '{member.Id}'. Return code: {ret}.");
                        continue;
                    }

                    TryAssignFrameSection(sapModel, frameName, member.Id, sectionName, warnings);
                    if (ShouldAssignFrameEndReleases(model, member))
                        TryAssignTrussReleases(sapModel, frameName, member.Id, warnings);
                    TryAssignFrameToEtabsGroup(sapModel, frameName, groupName, member.Id, warnings);

                    generatedFrames.Add(new GeneratedEtabsFrame
                    {
                        MemberId = member.Id,
                        EtabsFrameName = frameName,
                        Group = member.Group,
                        SectionName = sectionName
                    });
                }

                List<string> generatedShellNames = DrawParametricShells(sapModel, model, groupName, exportSuffix, request, nodePointNames, warnings);
                TryAssignSupportRestraints(sapModel, model, nodePointNames, warnings);
                TryApplyLoads(sapModel, model, nodePointNames, generatedFrames, warnings);
                TryRefreshEtabsView(sapModel);

                return new EtabsTrussDrawResult
                {
                    IsError = false,
                    Message = generatedShellNames.Count == 0
                        ? $"Drawn {generatedFrames.Count} truss frame object(s) in ETABS group '{groupName}'."
                        : $"Drawn {generatedFrames.Count} truss frame object(s) and {generatedShellNames.Count} shell area object(s) in ETABS group '{groupName}'.",
                    DrawnCount = generatedFrames.Count,
                    ShellCount = generatedShellNames.Count,
                    Frames = generatedFrames,
                    ShellNames = generatedShellNames,
                    ObjectNames = generatedFrames.Select(frame => frame.EtabsFrameName).Concat(generatedShellNames).ToList(),
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
            return new EtabsTrussDrawResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public DomeEtabsDataResult ListDomeEtabsData(DomeEtabsDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);

        var frameSections = new List<string>();
        var shellProperties = new List<string>();
        var loadPatterns = new List<string>();
        var loadCombinations = new List<string>();
        var stories = new List<string>();
        var groups = new List<string>();

        if (instances.Count > 0)
        {
            try
            {
                ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
                ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
                frameSections = GetFrameSectionNames(sapModel, warnings);
                shellProperties = GetAreaPropertyNames(sapModel, warnings);
                loadPatterns = GetLoadPatternNames(sapModel, warnings);
                loadCombinations = GetComboNames(sapModel, warnings);
                stories = GetStoryNames(sapModel, warnings);
                groups = GetGroupNames(sapModel, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add("ETABS dome data could not be loaded: " + ex.Message);
            }
        }

        return new DomeEtabsDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0
                ? instanceResult.Message
                : $"Loaded {shellProperties.Count} shell propertie(s), {frameSections.Count} frame section(s), and {stories.Count} story option(s).",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            FrameSections = frameSections,
            ShellProperties = shellProperties,
            LoadPatterns = loadPatterns,
            LoadCombinations = loadCombinations,
            Stories = stories,
            Groups = groups,
            Warnings = warnings
        };
    }

    public PlateGirderEtabsDataResult ListPlateGirderEtabsData(PlateGirderEtabsDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);
        var shellProperties = new List<string>();
        var shellPropertyDefinitions = new List<PlateGirderShellPropertyDefinition>();
        var loadPatterns = new List<string>();
        var stories = new List<string>();
        var groups = new List<string>();

        if (instances.Count > 0)
        {
            try
            {
                ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
                ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
                ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
                try
                {
                    TrySetPresentUnitsToKnM(sapModel, warnings);
                    shellPropertyDefinitions = GetPlateGirderShellPropertyDefinitions(sapModel, warnings);
                    shellProperties = shellPropertyDefinitions.Select(definition => definition.Name).ToList();
                    loadPatterns = GetLoadPatternNames(sapModel, warnings);
                    stories = GetStoryNames(sapModel, warnings);
                    groups = GetGroupNames(sapModel, warnings);
                }
                finally
                {
                    if (originalUnits != null)
                        TryRestorePresentUnits(sapModel, originalUnits.Value);
                }
            }
            catch (Exception ex)
            {
                warnings.Add("ETABS plate girder data could not be loaded: " + ex.Message);
            }
        }

        return new PlateGirderEtabsDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0
                ? instanceResult.Message
                : $"Loaded {shellProperties.Count} shell propertie(s), {loadPatterns.Count} load pattern(s), and {stories.Count} story option(s).",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            ShellProperties = shellProperties,
            ShellPropertyDefinitions = shellPropertyDefinitions,
            LoadPatterns = loadPatterns,
            Stories = stories,
            Groups = groups,
            Warnings = warnings
        };
    }

    public SteelRailingEtabsDataResult ListSteelRailingEtabsData(SteelRailingEtabsDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);
        var frameSections = new List<string>();
        var loadPatterns = new List<string>();
        var loadCombinations = new List<string>();
        var stories = new List<string>();
        var groups = new List<string>();

        if (instances.Count > 0)
        {
            try
            {
                ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
                ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
                frameSections = GetFrameSectionNames(sapModel, warnings);
                loadPatterns = GetLoadPatternNames(sapModel, warnings);
                loadCombinations = GetComboNames(sapModel, warnings);
                stories = GetStoryNames(sapModel, warnings);
                groups = GetGroupNames(sapModel, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add("ETABS railing data could not be loaded: " + ex.Message);
            }
        }

        return new SteelRailingEtabsDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0
                ? instanceResult.Message
                : $"Loaded {frameSections.Count} frame section(s), {loadPatterns.Count} load pattern(s), and {stories.Count} story option(s).",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            FrameSections = frameSections,
            LoadPatterns = loadPatterns,
            LoadCombinations = loadCombinations,
            Stories = stories,
            Groups = groups,
            Warnings = warnings
        };
    }

    public SteelRailingDrawResult DrawOrUpdateSteelRailing(SteelRailingDrawRequest request)
    {
        var warnings = new List<string>();

        try
        {
            SteelRailingModel model = request.Model;
            if (model.Nodes.Count == 0 || model.Members.Count == 0)
                throw new InvalidOperationException("No steel railing model was provided.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                string mainGroup = EnsureEtabsDrawGroup(sapModel, model.GroupName, warnings);
                string postGroup = EnsureEtabsDrawGroup(sapModel, model.PostGroupName, warnings);
                string topRailGroup = EnsureEtabsDrawGroup(sapModel, model.TopRailGroupName, warnings);
                string midRailGroup = EnsureEtabsDrawGroup(sapModel, model.MidRailGroupName, warnings);
                string bottomRailGroup = EnsureEtabsDrawGroup(sapModel, model.BottomRailGroupName, warnings);
                string loadPointGroup = EnsureEtabsDrawGroup(sapModel, model.LoadPointGroupName, warnings);

                if (request.UpdateExistingGroup)
                    TryDeleteFramesInGroup(sapModel, mainGroup, warnings);

                var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
                var nodePointNames = CreateEtabsPointsForRailingNodes(sapModel, model, mainGroup, warnings);
                var generatedFrames = new List<GeneratedEtabsFrame>();

                foreach (SteelRailingMember member in model.Members)
                {
                    if (!nodes.ContainsKey(member.StartNodeId) || !nodes.ContainsKey(member.EndNodeId))
                    {
                        warnings.Add($"Skipped railing member '{member.Id}': node reference could not be resolved.");
                        continue;
                    }

                    if (!nodePointNames.TryGetValue(member.StartNodeId, out string? startPointName) ||
                        !nodePointNames.TryGetValue(member.EndNodeId, out string? endPointName) ||
                        string.IsNullOrWhiteSpace(startPointName) ||
                        string.IsNullOrWhiteSpace(endPointName))
                    {
                        warnings.Add($"Skipped railing member '{member.Id}': shared ETABS point objects were not available for both end nodes.");
                        continue;
                    }

                    string sectionName = (member.SectionName ?? "").Trim();
                    if (sectionName.Length == 0)
                    {
                        warnings.Add($"Skipped railing member '{member.Id}': no frame section selected.");
                        continue;
                    }

                    string preferredFrameName = BuildExportObjectName(member.Id, "");
                    string frameName = "";
                    int ret = sapModel.FrameObj.AddByPoint(
                        startPointName,
                        endPointName,
                        ref frameName,
                        sectionName,
                        preferredFrameName);

                    if (ret != 0)
                    {
                        frameName = "";
                        ret = sapModel.FrameObj.AddByPoint(
                            startPointName,
                            endPointName,
                            ref frameName,
                            sectionName,
                            "");

                        if (ret == 0)
                            warnings.Add($"Railing member '{member.Id}' was drawn with ETABS automatic frame name because preferred name '{preferredFrameName}' was unavailable.");
                    }

                    if (ret != 0 || string.IsNullOrWhiteSpace(frameName))
                    {
                        warnings.Add($"ETABS could not draw railing member '{member.Id}'. Return code: {ret}.");
                        continue;
                    }

                    TryAssignFrameSection(sapModel, frameName, member.Id, sectionName, warnings);
                    TryAssignFrameToEtabsGroup(sapModel, frameName, mainGroup, member.Id, warnings);
                    TryAssignFrameToEtabsGroup(sapModel, frameName, RailingMemberGroupToEtabsGroup(member.Group, postGroup, topRailGroup, midRailGroup, bottomRailGroup), member.Id, warnings);

                    generatedFrames.Add(new GeneratedEtabsFrame
                    {
                        MemberId = member.Id,
                        EtabsFrameName = frameName,
                        Group = member.Group,
                        SectionName = sectionName
                    });
                }

                TryAssignRailingBaseSupports(sapModel, model, nodePointNames, warnings);
                TryAssignRailingLoadReferencePoints(sapModel, model, nodePointNames, loadPointGroup, warnings);
                TryApplyRailingLoads(sapModel, model, nodePointNames, generatedFrames, warnings);
                TryRefreshEtabsView(sapModel);

                return new SteelRailingDrawResult
                {
                    IsError = false,
                    Message = $"Created {generatedFrames.Count} railing frame object(s) in ETABS group '{mainGroup}'.",
                    CreatedFrameCount = generatedFrames.Count,
                    Frames = generatedFrames,
                    FrameObjectNames = generatedFrames.Select(frame => frame.EtabsFrameName).ToList(),
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
            return new SteelRailingDrawResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public WallDrainEtabsDataResult ListWallDrainEtabsData(WallDrainEtabsDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);
        var frameSections = new List<string>();
        var shellProperties = new List<string>();
        var loadPatterns = new List<string>();
        var loadCombinations = new List<string>();
        var stories = new List<string>();
        var groups = new List<string>();

        if (instances.Count > 0)
        {
            try
            {
                ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
                ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
                frameSections = GetFrameSectionNames(sapModel, warnings);
                shellProperties = GetAreaPropertyNames(sapModel, warnings);
                loadPatterns = GetLoadPatternNames(sapModel, warnings);
                loadCombinations = GetComboNames(sapModel, warnings);
                stories = GetStoryNames(sapModel, warnings);
                groups = GetGroupNames(sapModel, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add("ETABS wall/drain data could not be loaded: " + ex.Message);
            }
        }

        return new WallDrainEtabsDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0
                ? instanceResult.Message
                : $"Loaded {frameSections.Count} frame section(s), {loadPatterns.Count} load pattern(s), and {stories.Count} story option(s).",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            FrameSections = frameSections,
            ShellProperties = shellProperties,
            LoadPatterns = loadPatterns,
            LoadCombinations = loadCombinations,
            Stories = stories,
            Groups = groups,
            Warnings = warnings
        };
    }

    public HydrostaticShellLoadDataResult ListHydrostaticShellLoadData(HydrostaticShellLoadDataRequest request)
    {
        var warnings = new List<string>();
        EtabsInstanceListResult instanceResult = ListEtabsInstances();
        warnings.AddRange(instanceResult.Warnings);

        List<EtabsInstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.EtabsInstanceId);
        var loadPatterns = new List<string>();
        var groups = new List<string>();

        if (instances.Count > 0)
        {
            try
            {
                ETABSv1.cOAPI etabsObject = GetEtabsObject(selectedInstanceId);
                ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
                loadPatterns = GetLoadPatternNames(sapModel, warnings);
                groups = GetGroupNames(sapModel, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add("ETABS hydrostatic shell load data could not be loaded: " + ex.Message);
            }
        }

        return new HydrostaticShellLoadDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0
                ? instanceResult.Message
                : $"Loaded {loadPatterns.Count} load pattern(s) and {groups.Count} group option(s).",
            Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            LoadPatterns = loadPatterns,
            Groups = groups,
            Warnings = warnings
        };
    }

    public HydrostaticShellLoadPreviewResult PreviewHydrostaticShellLoad(HydrostaticShellLoadInput input)
    {
        var warnings = new List<string>();

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(input.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                HydrostaticShellLoadPreview preview = BuildHydrostaticShellLoadPreview(sapModel, input, warnings, validateLoadPatternExists: true);
                return new HydrostaticShellLoadPreviewResult
                {
                    IsError = false,
                    Message = $"Preview ready for {preview.ShellCount} shell area object(s).",
                    Preview = preview,
                    Warnings = warnings.Concat(preview.Warnings).ToList()
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
            return new HydrostaticShellLoadPreviewResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public HydrostaticShellLoadAssignResult AssignHydrostaticShellLoad(HydrostaticShellLoadInput input)
    {
        var warnings = new List<string>();

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(input.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);
                HydrostaticShellLoadPreview preview = BuildHydrostaticShellLoadPreview(sapModel, input, warnings, validateLoadPatternExists: false);

                if (input.AssignmentOption != HydroLoadAssignmentOption.DeleteExisting)
                    EnsureHydrostaticLoadPattern(sapModel, input, preview, warnings);

                int appliedCount = ApplyHydrostaticShellLoadTable(sapModel, input, preview, warnings);
                TryRefreshEtabsView(sapModel);

                return new HydrostaticShellLoadAssignResult
                {
                    IsError = appliedCount == 0,
                    Message = appliedCount == 0
                        ? "No hydrostatic shell load was assigned."
                        : $"Applied hydrostatic shell load to {appliedCount} shell area object(s).",
                    Preview = preview,
                    AppliedCount = appliedCount,
                    Warnings = warnings.Concat(preview.Warnings).ToList()
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
            return new HydrostaticShellLoadAssignResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public WallDrainDrawResult DrawOrUpdateWallDrain(WallDrainDrawRequest request)
    {
        var warnings = new List<string>();

        try
        {
            WallDrainModel model = request.Model;
            if (model.Nodes.Count == 0 || (model.FrameMembers.Count == 0 && model.ShellPanels.Count == 0))
                throw new InvalidOperationException("No wall/drain frame or shell model was provided.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                string exportSuffix = request.AddAsNew ? BuildExportSuffix() : "";
                string mainGroup = EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.GroupName, exportSuffix), warnings);
                string frameGroup = model.FrameMembers.Count > 0
                    ? EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.FrameGroupName, exportSuffix), warnings)
                    : "";
                string shellGroup = model.ShellPanels.Count > 0
                    ? EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.ShellGroupName, exportSuffix), warnings)
                    : "";
                string wallGroup = EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.WallGroupName, exportSuffix), warnings);
                string slabGroup = EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.SlabGroupName, exportSuffix), warnings);
                string buttressGroup = EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.ButtressGroupName, exportSuffix), warnings);
                string loadGroup = EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.LoadGroupName, exportSuffix), warnings);
                string supportGroup = EnsureEtabsDrawGroup(sapModel, BuildExportGroupName(model.SupportGroupName, exportSuffix), warnings);

                if (request.AddAsNew)
                {
                    warnings.Add($"Add-as-new mode is active. Existing ETABS objects were not deleted; generated objects were assigned to group '{mainGroup}'.");
                    if (Math.Abs(request.OffsetX) > 0.000001 || Math.Abs(request.OffsetY) > 0.000001 || Math.Abs(request.OffsetZ) > 0.000001)
                        warnings.Add($"Applied add-as-new placement offset: X {request.OffsetX:0.###} m, Y {request.OffsetY:0.###} m, Z {request.OffsetZ:0.###} m.");
                }

                if (request.UpdateExistingGroup && !request.AddAsNew)
                    TryDeleteWallDrainObjectsInGroup(sapModel, mainGroup, warnings);

                Dictionary<string, WallDrainNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> nodePointNames = CreateEtabsPointsForWallDrainNodes(sapModel, model, mainGroup, exportSuffix, request, warnings);
                var frameNamesByMemberId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var frameNames = new List<string>();
                var areaNamesByPanelId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var areaNames = new List<string>();

                foreach (WallDrainFrameMember member in model.FrameMembers)
                {
                    if (!nodes.ContainsKey(member.StartNodeId) || !nodes.ContainsKey(member.EndNodeId))
                    {
                        warnings.Add($"Skipped wall/drain frame '{member.Id}': node reference could not be resolved.");
                        continue;
                    }

                    if (!nodePointNames.TryGetValue(member.StartNodeId, out string? startPointName) ||
                        !nodePointNames.TryGetValue(member.EndNodeId, out string? endPointName) ||
                        string.IsNullOrWhiteSpace(startPointName) ||
                        string.IsNullOrWhiteSpace(endPointName))
                    {
                        warnings.Add($"Skipped wall/drain frame '{member.Id}': shared ETABS point objects were not available for both end nodes.");
                        continue;
                    }

                    string sectionName = (member.SectionName ?? "").Trim();
                    if (sectionName.Length == 0)
                    {
                        warnings.Add($"Skipped wall/drain frame '{member.Id}': no frame section selected.");
                        continue;
                    }

                    string frameName = "";
                    string preferredName = BuildExportObjectName(member.Id, exportSuffix);
                    int ret = sapModel.FrameObj.AddByPoint(startPointName, endPointName, ref frameName, sectionName, preferredName);
                    if (ret != 0)
                    {
                        frameName = "";
                        ret = sapModel.FrameObj.AddByPoint(startPointName, endPointName, ref frameName, sectionName, "");
                        if (ret == 0)
                            warnings.Add($"Wall/drain frame '{member.Id}' was drawn with ETABS automatic frame name because preferred name '{preferredName}' was unavailable.");
                    }

                    if (ret != 0 || string.IsNullOrWhiteSpace(frameName))
                    {
                        warnings.Add($"ETABS could not draw wall/drain frame '{member.Id}'. Return code: {ret}.");
                        continue;
                    }

                    TryAssignFrameSection(sapModel, frameName, member.Id, sectionName, warnings);
                    TryAssignFrameToEtabsGroup(sapModel, frameName, mainGroup, member.Id, warnings);
                    TryAssignFrameToEtabsGroup(sapModel, frameName, frameGroup, member.Id, warnings);
                    TryAssignFrameToEtabsGroup(sapModel, frameName, WallDrainPanelGroupToEtabsGroup(member.Group, wallGroup, slabGroup, buttressGroup), member.Id, warnings);
                    frameNamesByMemberId[member.Id] = frameName;
                    frameNames.Add(frameName);
                }

                DrawWallDrainShellPanels(
                    sapModel,
                    model,
                    nodes,
                    nodePointNames,
                    mainGroup,
                    shellGroup,
                    wallGroup,
                    slabGroup,
                    buttressGroup,
                    exportSuffix,
                    areaNamesByPanelId,
                    areaNames,
                    warnings);

                TryAssignWallDrainSupportRestraints(sapModel, model, nodePointNames, supportGroup, warnings);
                if (areaNamesByPanelId.Count > 0)
                    TryApplyWallDrainLoads(sapModel, model, areaNamesByPanelId, loadGroup, warnings);
                if (frameNamesByMemberId.Count > 0)
                    TryApplyWallDrainFrameLoads(sapModel, model, frameNamesByMemberId, loadGroup, warnings);
                TryRefreshEtabsView(sapModel);

                string objectSummary = areaNames.Count > 0 && frameNames.Count > 0
                    ? $"{areaNames.Count} shell area object(s) and {frameNames.Count} frame object(s)"
                    : areaNames.Count > 0
                        ? $"{areaNames.Count} shell area object(s)"
                        : $"{frameNames.Count} wall/drain frame object(s)";

                return new WallDrainDrawResult
                {
                    IsError = false,
                    Message = $"Created {objectSummary} in ETABS group '{mainGroup}'.",
                    CreatedShellCount = areaNames.Count,
                    ShellObjectNames = areaNames.Concat(frameNames).ToList(),
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
            return new WallDrainDrawResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public DomeEtabsDrawResult DrawOrUpdateDome(DomeEtabsDrawRequest request)
    {
        var warnings = new List<string>();

        try
        {
            ParametricDomeModel model = request.Model;
            if (model.Nodes.Count == 0 || (model.FrameMembers.Count == 0 && model.ShellPanels.Count == 0))
                throw new InvalidOperationException("No parametric dome model was provided.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                string mainGroup = EnsureEtabsDrawGroup(sapModel, model.GroupName, warnings);
                string frameGroup = EnsureEtabsDrawGroup(sapModel, BuildDomeSubGroup(model, "FRAME"), warnings);
                string shellGroup = EnsureEtabsDrawGroup(sapModel, BuildDomeSubGroup(model, "SHELL"), warnings);
                string ringGroup = EnsureEtabsDrawGroup(sapModel, BuildDomeSubGroup(model, "RING"), warnings);
                string radialGroup = EnsureEtabsDrawGroup(sapModel, BuildDomeSubGroup(model, "RADIAL"), warnings);
                string diagonalGroup = EnsureEtabsDrawGroup(sapModel, BuildDomeSubGroup(model, "DIAGONAL"), warnings);
                string supportGroup = EnsureEtabsDrawGroup(sapModel, BuildDomeSubGroup(model, "SUPPORT"), warnings);

                if (request.UpdateExistingGroup)
                    TryDeleteDomeObjectsInGroup(sapModel, mainGroup, warnings);

                Dictionary<string, DomeNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
                var shellNames = new List<string>();
                var frameNames = new List<string>();

                if (model.GenerateShellPanels)
                    DrawDomeShellPanels(sapModel, model, nodes, mainGroup, shellGroup, shellNames, warnings);

                DrawDomeFrameMembers(
                    sapModel,
                    model,
                    nodes,
                    mainGroup,
                    frameGroup,
                    ringGroup,
                    radialGroup,
                    diagonalGroup,
                    frameNames,
                    warnings);

                if (model.GenerateSupportsAtBase)
                    DrawDomeBaseSupports(sapModel, model, supportGroup, warnings);

                TryRefreshEtabsView(sapModel);

                return new DomeEtabsDrawResult
                {
                    IsError = false,
                    Message = $"Created {shellNames.Count} dome shell object(s) and {frameNames.Count} frame object(s) in ETABS group '{mainGroup}'.",
                    CreatedFrameCount = frameNames.Count,
                    CreatedShellCount = shellNames.Count,
                    FrameObjectNames = frameNames,
                    ShellObjectNames = shellNames,
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
            return new DomeEtabsDrawResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    public PlateGirderEtabsDrawResult DrawOrUpdatePlateGirder(PlateGirderEtabsDrawRequest request)
    {
        var warnings = new List<string>();

        try
        {
            ParametricPlateGirderModel model = request.Model;
            if (model.Nodes.Count == 0 || model.ShellPanels.Count == 0)
                throw new InvalidOperationException("No parametric plate girder model was provided.");

            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            ETABSv1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                string mainGroup = EnsureEtabsDrawGroup(sapModel, model.GroupName, warnings);
                string webGroup = EnsureEtabsDrawGroup(sapModel, model.WebGroupName, warnings);
                string flangeGroup = EnsureEtabsDrawGroup(sapModel, model.FlangeGroupName, warnings);
                string stiffenerGroup = EnsureEtabsDrawGroup(sapModel, model.StiffenerGroupName, warnings);

                if (request.UpdateExistingGroup)
                    TryDeleteAreasInGroup(sapModel, mainGroup, "plate girder", warnings);

                Dictionary<string, PlateGirderNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
                var shellNames = new List<string>();
                var topFlangeShellNames = new List<string>();
                var allShellPointRefs = new Dictionary<string, PlateGirderEtabsPointRef>(StringComparer.OrdinalIgnoreCase);
                var bottomFlangePointRefs = new Dictionary<string, PlateGirderEtabsPointRef>(StringComparer.OrdinalIgnoreCase);

                foreach (PlateGirderShellPanel panel in model.ShellPanels)
                {
                    List<PlateGirderNode> panelNodes = panel.NodeIds
                        .Where(nodes.ContainsKey)
                        .Select(nodeId => nodes[nodeId])
                        .ToList();

                    if (panelNodes.Count != 4)
                    {
                        warnings.Add($"Skipped plate girder shell panel '{panel.Id}': panel is not a valid quad.");
                        continue;
                    }

                    string shellProperty = (panel.ShellPropertyName ?? "").Trim();
                    if (shellProperty.Length == 0)
                    {
                        warnings.Add($"Skipped plate girder shell panel '{panel.Id}': no shell property selected for {panel.Group}.");
                        continue;
                    }

                    double[] xs = panelNodes.Select(node => node.X).ToArray();
                    double[] ys = panelNodes.Select(node => node.Y).ToArray();
                    double[] zs = panelNodes.Select(node => node.Z).ToArray();
                    string areaName = "";
                    string preferredName = BuildExportObjectName($"{model.PlateGirderId}_{panel.Id}", "");

                    try
                    {
                        int ret = sapModel.AreaObj.AddByCoord(panelNodes.Count, ref xs, ref ys, ref zs, ref areaName, shellProperty, preferredName, "Global");
                        if (ret != 0)
                        {
                            areaName = "";
                            ret = sapModel.AreaObj.AddByCoord(panelNodes.Count, ref xs, ref ys, ref zs, ref areaName, shellProperty, "", "Global");
                            if (ret == 0)
                                warnings.Add($"Plate girder shell panel '{panel.Id}' was drawn with ETABS automatic area name because preferred name '{preferredName}' was unavailable.");
                        }

                        if (ret != 0 || string.IsNullOrWhiteSpace(areaName))
                        {
                            warnings.Add($"ETABS could not draw plate girder shell panel '{panel.Id}'. Return code: {ret}.");
                            continue;
                        }

                        TryAssignAreaToEtabsGroup(sapModel, areaName, mainGroup, panel.Id, warnings);
                        TryAssignAreaToEtabsGroup(sapModel, areaName, PlateGirderGroupToEtabsGroup(panel.Group, webGroup, flangeGroup, stiffenerGroup), panel.Id, warnings);
                        TryCollectPlateGirderAreaPointRefs(sapModel, areaName, panelNodes, allShellPointRefs, panel.Id, warnings);
                        if (panel.Group == PlateGirderShellGroup.BottomFlange)
                            TryCollectPlateGirderAreaPointRefs(sapModel, areaName, panelNodes, bottomFlangePointRefs, panel.Id, warnings);
                        shellNames.Add(areaName);
                        if (panel.Group == PlateGirderShellGroup.TopFlange)
                            topFlangeShellNames.Add(areaName);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Plate girder shell panel '{panel.Id}' drawing failed: {ex.Message}");
                    }
                }

                TryAssignPlateGirderEndSupports(sapModel, model, allShellPointRefs.Values, bottomFlangePointRefs.Values, warnings);
                TryApplyPlateGirderAreaLoad(sapModel, model, topFlangeShellNames, warnings);

                TryRefreshEtabsView(sapModel);

                return new PlateGirderEtabsDrawResult
                {
                    IsError = false,
                    Message = $"Created {shellNames.Count} plate girder shell object(s) in ETABS group '{mainGroup}'.",
                    CreatedShellCount = shellNames.Count,
                    ShellObjectNames = shellNames,
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
            return new PlateGirderEtabsDrawResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    private static void DrawDomeShellPanels(
        ETABSv1.cSapModel sapModel,
        ParametricDomeModel model,
        IReadOnlyDictionary<string, DomeNode> nodes,
        string mainGroup,
        string shellGroup,
        List<string> shellNames,
        List<string> warnings)
    {
        string shellProperty = (model.ShellPropertyName ?? "").Trim();
        if (shellProperty.Length == 0)
        {
            warnings.Add("Skipped dome shell panels: no shell property selected.");
            return;
        }

        foreach (DomeShellPanel panel in model.ShellPanels)
        {
            List<DomeNode> panelNodes = panel.NodeIds
                .Where(nodeId => nodes.ContainsKey(nodeId))
                .Select(nodeId => nodes[nodeId])
                .ToList();

            if (panelNodes.Count < 3)
            {
                warnings.Add($"Skipped dome shell panel '{panel.Id}': fewer than 3 valid nodes.");
                continue;
            }

            double[] xs = panelNodes.Select(node => node.X).ToArray();
            double[] ys = panelNodes.Select(node => node.Y).ToArray();
            double[] zs = panelNodes.Select(node => node.Z).ToArray();
            string areaName = "";
            string preferredName = BuildExportObjectName($"{model.DomeId}_SH_{panel.Id}", "");

            try
            {
                int ret = sapModel.AreaObj.AddByCoord(panelNodes.Count, ref xs, ref ys, ref zs, ref areaName, shellProperty, preferredName, "Global");
                if (ret != 0)
                {
                    areaName = "";
                    ret = sapModel.AreaObj.AddByCoord(panelNodes.Count, ref xs, ref ys, ref zs, ref areaName, shellProperty, "", "Global");
                    if (ret == 0)
                        warnings.Add($"Dome shell panel '{panel.Id}' was drawn with ETABS automatic area name because preferred name '{preferredName}' was unavailable.");
                }

                if (ret != 0 || string.IsNullOrWhiteSpace(areaName))
                {
                    warnings.Add($"ETABS could not draw dome shell panel '{panel.Id}'. Return code: {ret}.");
                    continue;
                }

                TryAssignAreaToEtabsGroup(sapModel, areaName, mainGroup, panel.Id, warnings);
                TryAssignAreaToEtabsGroup(sapModel, areaName, shellGroup, panel.Id, warnings);
                shellNames.Add(areaName);
            }
            catch (Exception ex)
            {
                warnings.Add($"Dome shell panel '{panel.Id}' drawing failed: {ex.Message}");
            }
        }
    }

    private static void DrawDomeFrameMembers(
        ETABSv1.cSapModel sapModel,
        ParametricDomeModel model,
        IReadOnlyDictionary<string, DomeNode> nodes,
        string mainGroup,
        string frameGroup,
        string ringGroup,
        string radialGroup,
        string diagonalGroup,
        List<string> frameNames,
        List<string> warnings)
    {
        foreach (DomeFrameMember member in model.FrameMembers)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out DomeNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out DomeNode? end))
            {
                warnings.Add($"Skipped dome frame '{member.Id}': node reference could not be resolved.");
                continue;
            }

            string sectionName = (member.SectionName ?? "").Trim();
            if (sectionName.Length == 0)
            {
                warnings.Add($"Skipped dome frame '{member.Id}': no frame section selected for {member.Group}.");
                continue;
            }

            string frameName = "";
            string preferredName = BuildExportObjectName($"{model.DomeId}_{member.Id}", "");
            try
            {
                int ret = sapModel.FrameObj.AddByCoord(start.X, start.Y, start.Z, end.X, end.Y, end.Z, ref frameName, sectionName, preferredName, "Global");
                if (ret != 0)
                {
                    frameName = "";
                    ret = sapModel.FrameObj.AddByCoord(start.X, start.Y, start.Z, end.X, end.Y, end.Z, ref frameName, sectionName, "", "Global");
                    if (ret == 0)
                        warnings.Add($"Dome frame '{member.Id}' was drawn with ETABS automatic frame name because preferred name '{preferredName}' was unavailable.");
                }

                if (ret != 0 || string.IsNullOrWhiteSpace(frameName))
                {
                    warnings.Add($"ETABS could not draw dome frame '{member.Id}'. Return code: {ret}.");
                    continue;
                }

                TryAssignFrameSection(sapModel, frameName, member.Id, sectionName, warnings);
                TryAssignFrameToEtabsGroup(sapModel, frameName, mainGroup, member.Id, warnings);
                TryAssignFrameToEtabsGroup(sapModel, frameName, frameGroup, member.Id, warnings);
                TryAssignFrameToEtabsGroup(sapModel, frameName, DomeMemberGroupToEtabsGroup(member.Group, ringGroup, radialGroup, diagonalGroup), member.Id, warnings);
                frameNames.Add(frameName);
            }
            catch (Exception ex)
            {
                warnings.Add($"Dome frame '{member.Id}' drawing failed: {ex.Message}");
            }
        }
    }

    private static void DrawDomeBaseSupports(ETABSv1.cSapModel sapModel, ParametricDomeModel model, string supportGroup, List<string> warnings)
    {
        foreach (DomeNode node in model.Nodes.Where(node => node.RingIndex == 0))
        {
            string pointName = "";
            try
            {
                int ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref pointName, "", "Global", false, 0);
                if (ret != 0 || string.IsNullOrWhiteSpace(pointName))
                {
                    warnings.Add($"ETABS could not create/reuse support point for dome node '{node.Id}'. Return code: {ret}.");
                    continue;
                }

                TrySetPointRestraint(sapModel, pointName, [true, true, true, false, false, false], $"dome base support node '{node.Id}'", warnings);
                TryAssignPointToEtabsGroup(sapModel, pointName, supportGroup, node.Id, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"Dome base support assignment failed for node '{node.Id}': {ex.Message}");
            }
        }
    }

    private static void TryDeleteDomeObjectsInGroup(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];

        try
        {
            int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
            if (ret != 0)
                return;

            int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
            var frames = new List<string>();
            var areas = new List<string>();

            for (int index = 0; index < count; index++)
            {
                if (objectTypes[index] == EtabsSelectedFrameObjectType)
                    frames.Add(objectNames[index]);
                else if (objectTypes[index] == EtabsSelectedAreaObjectType)
                    areas.Add(objectNames[index]);
            }

            foreach (string frame in frames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.Delete(frame, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing dome frame '{frame}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            foreach (string area in areas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.AreaObj.Delete(area, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing dome shell area '{area}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            if (frames.Count > 0 || areas.Count > 0)
                warnings.Add($"Removed {frames.Count} dome frame object(s) and {areas.Count} shell area object(s) from group '{groupName}' before drawing.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Existing dome group '{groupName}' could not be cleaned before update: {ex.Message}");
        }
    }

    private static void TryDeleteAreasInGroup(ETABSv1.cSapModel sapModel, string groupName, string label, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];

        try
        {
            int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
            if (ret != 0)
                return;

            int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
            var areas = new List<string>();

            for (int index = 0; index < count; index++)
            {
                if (objectTypes[index] == EtabsSelectedAreaObjectType)
                    areas.Add(objectNames[index]);
            }

            foreach (string area in areas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.AreaObj.Delete(area, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing {label} shell area '{area}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            if (areas.Count > 0)
                warnings.Add($"Removed {areas.Count} {label} shell area object(s) from group '{groupName}' before drawing.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Existing {label} group '{groupName}' could not be cleaned before update: {ex.Message}");
        }
    }

    private static string BuildDomeSubGroup(ParametricDomeModel model, string suffix)
    {
        return EtabsNameUtility.BuildSafeName("", $"{model.GroupName}_{suffix}");
    }

    private static string DomeMemberGroupToEtabsGroup(DomeMemberGroup group, string ringGroup, string radialGroup, string diagonalGroup)
    {
        return group switch
        {
            DomeMemberGroup.Radial => radialGroup,
            DomeMemberGroup.Diagonal => diagonalGroup,
            _ => ringGroup
        };
    }

    private sealed record PlateGirderEtabsPointRef(string Name, double X, double Y, double Z);

    private static void TryCollectPlateGirderAreaPointRefs(
        ETABSv1.cSapModel sapModel,
        string areaName,
        IReadOnlyList<PlateGirderNode> fallbackNodes,
        Dictionary<string, PlateGirderEtabsPointRef> pointRefs,
        string panelId,
        List<string> warnings)
    {
        int numberPoints = 0;
        string[] pointNames = [];
        try
        {
            int ret = sapModel.AreaObj.GetPoints(areaName, ref numberPoints, ref pointNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS could not read corner points for plate girder shell panel '{panelId}'. Return code: {ret}.");
                return;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS corner point read failed for plate girder shell panel '{panelId}': {ex.Message}");
            return;
        }

        int count = Math.Min(numberPoints, pointNames.Length);
        for (int index = 0; index < count; index++)
        {
            string pointName = (pointNames[index] ?? "").Trim();
            if (pointName.Length == 0)
                continue;

            double x = 0;
            double y = 0;
            double z = 0;
            bool gotCoordinates = false;
            try
            {
                int coordRet = sapModel.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z, "Global");
                gotCoordinates = coordRet == 0;
            }
            catch
            {
                gotCoordinates = false;
            }

            if (!gotCoordinates)
            {
                PlateGirderNode? fallbackNode = index < fallbackNodes.Count ? fallbackNodes[index] : null;
                if (fallbackNode == null)
                {
                    warnings.Add($"Skipped plate girder support point '{pointName}': coordinates could not be read.");
                    continue;
                }

                x = fallbackNode.X;
                y = fallbackNode.Y;
                z = fallbackNode.Z;
            }

            pointRefs[pointName] = new PlateGirderEtabsPointRef(pointName, x, y, z);
        }
    }

    private static void TryAssignPlateGirderEndSupports(
        ETABSv1.cSapModel sapModel,
        ParametricPlateGirderModel model,
        IEnumerable<PlateGirderEtabsPointRef> allPointRefs,
        IEnumerable<PlateGirderEtabsPointRef> bottomFlangePointRefs,
        List<string> warnings)
    {
        List<PlateGirderEtabsPointRef> points = allPointRefs
            .GroupBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        List<PlateGirderEtabsPointRef> bottomFlangePoints = bottomFlangePointRefs
            .GroupBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (points.Count == 0)
        {
            warnings.Add("Plate girder support restraints were not assigned because no generated shell corner points were found.");
            return;
        }

        foreach (PlateGirderEtabsPointRef point in points)
            TrySetPointRestraint(sapModel, point.Name, BuildFreePointRestraints(), $"plate girder generated point '{point.Name}'", warnings);

        if (bottomFlangePoints.Count == 0)
        {
            warnings.Add("Plate girder support restraints were not assigned because no generated bottom flange shell corner points were found.");
            return;
        }

        double leftX = model.OriginX;
        double rightX = model.OriginX + model.Length;
        double tolerance = Math.Max(0.00001, Math.Abs(model.Length) * 0.000001);
        List<PlateGirderEtabsPointRef> leftSupportPoints = bottomFlangePoints
            .Where(point => Math.Abs(point.X - leftX) <= tolerance)
            .OrderBy(point => point.Z)
            .ThenBy(point => point.Y)
            .ToList();
        List<PlateGirderEtabsPointRef> rightSupportPoints = bottomFlangePoints
            .Where(point => Math.Abs(point.X - rightX) <= tolerance)
            .OrderBy(point => point.Z)
            .ThenBy(point => point.Y)
            .ToList();

        if (leftSupportPoints.Count == 0)
            warnings.Add("No generated bottom flange points were found on the left end; left pin support was not assigned.");
        if (rightSupportPoints.Count == 0)
            warnings.Add("No generated bottom flange points were found on the right end; right roller support was not assigned.");

        foreach (PlateGirderEtabsPointRef point in leftSupportPoints)
            TrySetPointRestraint(sapModel, point.Name, [true, true, true, false, false, false], $"plate girder bottom flange left pin support point '{point.Name}'", warnings);

        foreach (PlateGirderEtabsPointRef point in rightSupportPoints)
            TrySetPointRestraint(sapModel, point.Name, [false, false, true, false, false, false], $"plate girder bottom flange right roller support point '{point.Name}'", warnings);

        if (leftSupportPoints.Count > 0 || rightSupportPoints.Count > 0)
            warnings.Add($"Assigned plate girder supports to bottom flange only: {leftSupportPoints.Count} left pin point(s), {rightSupportPoints.Count} right roller point(s); all other generated shell points are free.");
    }

    private static string PlateGirderGroupToEtabsGroup(
        PlateGirderShellGroup group,
        string webGroup,
        string flangeGroup,
        string stiffenerGroup)
    {
        return group switch
        {
            PlateGirderShellGroup.Web => webGroup,
            PlateGirderShellGroup.TopFlange or PlateGirderShellGroup.BottomFlange => flangeGroup,
            _ => stiffenerGroup
        };
    }

    private static void TryApplyPlateGirderAreaLoad(
        ETABSv1.cSapModel sapModel,
        ParametricPlateGirderModel model,
        IReadOnlyCollection<string> topFlangeShellNames,
        List<string> warnings)
    {
        if (!model.ApplyTopFlangeAreaLoad || Math.Abs(model.AnalysisUniformLoadKnPerM) <= 0.000001)
            return;

        string loadPattern = (model.LoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
        {
            warnings.Add("Skipped plate girder shell area load: no load pattern selected.");
            return;
        }

        if (topFlangeShellNames.Count == 0)
        {
            warnings.Add("Skipped plate girder shell area load: no top flange shell panels were generated.");
            return;
        }

        double pressure = -Math.Abs(model.TopFlangeAreaLoadKnPerM2);
        foreach (string areaName in topFlangeShellNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            try
            {
                int ret = sapModel.AreaObj.SetLoadUniform(areaName, loadPattern, pressure, 6, false, "Global", EtabsObjects);
                if (ret != 0)
                    warnings.Add($"ETABS could not assign plate girder area load to shell '{areaName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Plate girder area load assignment failed on shell '{areaName}': {ex.Message}");
            }
        }

        warnings.Add($"Applied plate girder UDL as top flange shell area load: {Math.Abs(pressure):0.###} kN/m2 in Global Z.");
    }

    private static string RailingMemberGroupToEtabsGroup(string group, string postGroup, string topRailGroup, string midRailGroup, string bottomRailGroup)
    {
        return group switch
        {
            SteelRailingMemberGroups.Post => postGroup,
            SteelRailingMemberGroups.TopRail => topRailGroup,
            SteelRailingMemberGroups.MidRail => midRailGroup,
            SteelRailingMemberGroups.BottomRail => bottomRailGroup,
            _ => postGroup
        };
    }

    private static Dictionary<string, string> CreateEtabsPointsForRailingNodes(
        ETABSv1.cSapModel sapModel,
        SteelRailingModel model,
        string mainGroup,
        List<string> warnings)
    {
        var nodePointNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (SteelRailingNode node in model.Nodes)
        {
            string preferredPointName = BuildExportObjectName($"{model.RailingId}_{node.Id}", "");
            string pointName = "";

            try
            {
                int ret = sapModel.PointObj.AddCartesian(
                    node.X,
                    node.Y,
                    node.Z,
                    ref pointName,
                    preferredPointName,
                    "Global",
                    false,
                    0);

                if (ret != 0)
                {
                    pointName = "";
                    ret = sapModel.PointObj.AddCartesian(
                        node.X,
                        node.Y,
                        node.Z,
                        ref pointName,
                        "",
                        "Global",
                        false,
                        0);

                    if (ret == 0)
                        warnings.Add($"Railing node '{node.Id}' was created with an ETABS automatic point name because preferred name '{preferredPointName}' was unavailable.");
                }

                if (ret != 0 || string.IsNullOrWhiteSpace(pointName))
                {
                    warnings.Add($"ETABS could not create point for railing node '{node.Id}'. Return code: {ret}.");
                    continue;
                }

                nodePointNames[node.Id] = pointName;
                TryAssignPointToEtabsGroup(sapModel, pointName, mainGroup, node.Id, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS point creation failed for railing node '{node.Id}': {ex.Message}");
            }
        }

        return nodePointNames;
    }

    private static void TryAssignRailingBaseSupports(
        ETABSv1.cSapModel sapModel,
        SteelRailingModel model,
        Dictionary<string, string> nodePointNames,
        List<string> warnings)
    {
        foreach (SteelRailingSupport support in model.Supports)
        {
            if (!nodePointNames.TryGetValue(support.NodeId, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
            {
                warnings.Add($"Skipped railing support at node '{support.NodeId}': ETABS point name was not found.");
                continue;
            }

            bool[] restraints = support.Restraints;
            TrySetPointRestraint(sapModel, pointName, restraints, $"railing base node '{support.NodeId}'", warnings);
        }
    }

    private static void TryAssignRailingLoadReferencePoints(
        ETABSv1.cSapModel sapModel,
        SteelRailingModel model,
        Dictionary<string, string> nodePointNames,
        string loadPointGroup,
        List<string> warnings)
    {
        foreach (SteelRailingNode node in model.Nodes.Where(node => node.IsLoadReferenceNode))
        {
            if (!nodePointNames.TryGetValue(node.Id, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
                continue;

            TryAssignPointToEtabsGroup(sapModel, pointName, loadPointGroup, node.Id, warnings);
        }
    }

    private static void TryApplyRailingLoads(
        ETABSv1.cSapModel sapModel,
        SteelRailingModel model,
        Dictionary<string, string> nodePointNames,
        List<GeneratedEtabsFrame> generatedFrames,
        List<string> warnings)
    {
        foreach (string frameName in generatedFrames.Select(frame => frame.EtabsFrameName))
        {
            TryClearFrameDistributedLoads(sapModel, frameName, warnings);
            TryClearFramePointLoads(sapModel, frameName, warnings);
        }

        foreach (string pointName in nodePointNames.Values)
            TryClearPointForceLoads(sapModel, pointName, warnings);

        if (model.Loads.Count == 0)
            return;

        var frameNamesByMemberId = generatedFrames.ToDictionary(frame => frame.MemberId, frame => frame.EtabsFrameName, StringComparer.OrdinalIgnoreCase);
        foreach (SteelRailingLoad load in model.Loads)
        {
            if (string.IsNullOrWhiteSpace(load.LoadPattern))
            {
                warnings.Add($"Skipped railing load '{load.Id}': no load pattern was selected.");
                continue;
            }

            if (load.LoadType == RailingLoadType.PointLoad)
            {
                TryApplyRailingPointLoad(sapModel, load, nodePointNames, warnings);
                continue;
            }

            List<SteelRailingMember> targetMembers = model.Members
                .Where(member => string.Equals(member.Group, load.TargetGroup, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targetMembers.Count == 0)
            {
                warnings.Add($"Skipped railing line load '{load.Id}': member group '{load.TargetGroup}' has no generated members.");
                continue;
            }

            int direction = ToEtabsDistributedLoadDirection(load.Direction);
            foreach (SteelRailingMember member in targetMembers)
            {
                if (!frameNamesByMemberId.TryGetValue(member.Id, out string? frameName) || string.IsNullOrWhiteSpace(frameName))
                {
                    warnings.Add($"Skipped railing line load '{load.Id}' on member '{member.Id}': ETABS frame name was not found.");
                    continue;
                }

                try
                {
                    int ret = sapModel.FrameObj.SetLoadDistributed(
                        frameName,
                        load.LoadPattern,
                        1,
                        direction,
                        0,
                        1,
                        load.MagnitudeKnPerM,
                        load.MagnitudeKnPerM,
                        "Global",
                        true,
                        false,
                        EtabsObjects);

                    if (ret != 0)
                        warnings.Add($"ETABS could not assign railing line load '{load.Id}' to frame '{frameName}'. Return code: {ret}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS railing line load '{load.Id}' assignment failed on frame '{frameName}': {ex.Message}");
                }
            }
        }
    }

    private static void TryApplyRailingPointLoad(
        ETABSv1.cSapModel sapModel,
        SteelRailingLoad load,
        Dictionary<string, string> nodePointNames,
        List<string> warnings)
    {
        if (load.TargetNodeIds.Count == 0)
        {
            warnings.Add($"Skipped railing point load '{load.Id}': no target nodes were generated.");
            return;
        }

        foreach (string nodeId in load.TargetNodeIds)
        {
            if (!nodePointNames.TryGetValue(nodeId, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
            {
                warnings.Add($"Skipped railing point load '{load.Id}' at node '{nodeId}': ETABS point name was not found.");
                continue;
            }

            double[] values = [0, 0, 0, 0, 0, 0];
            if (load.Direction == RailingLoadDirection.GlobalX)
                values[0] = load.MagnitudeKn;
            else
                values[1] = load.MagnitudeKn;

            try
            {
                int ret = sapModel.PointObj.SetLoadForce(pointName, load.LoadPattern, ref values, true, "Global", EtabsObjects);
                if (ret != 0)
                    warnings.Add($"ETABS could not assign railing point load '{load.Id}' to point '{pointName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS railing point load '{load.Id}' assignment failed at point '{pointName}': {ex.Message}");
            }
        }
    }

    private static int ToEtabsDistributedLoadDirection(RailingLoadDirection direction)
    {
        return direction == RailingLoadDirection.GlobalX ? 4 : 5;
    }

    private static Dictionary<string, string> CreateEtabsPointsForWallDrainNodes(
        ETABSv1.cSapModel sapModel,
        WallDrainModel model,
        string mainGroup,
        string exportSuffix,
        WallDrainDrawRequest request,
        List<string> warnings)
    {
        var nodePointNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (WallDrainNode node in model.Nodes)
        {
            string preferredPointName = BuildExportObjectName($"{model.StructureId}_{node.Id}", exportSuffix);
            double x = node.X + request.OffsetX;
            double y = node.Y + request.OffsetY;
            double z = node.Z + request.OffsetZ;
            bool mergeOff = request.AddAsNew;
            string pointName = "";

            try
            {
                int ret = sapModel.PointObj.AddCartesian(x, y, z, ref pointName, preferredPointName, "Global", mergeOff, 0);
                if (ret != 0)
                {
                    pointName = "";
                    ret = sapModel.PointObj.AddCartesian(x, y, z, ref pointName, "", "Global", mergeOff, 0);
                    if (ret == 0)
                        warnings.Add($"Wall/drain node '{node.Id}' was created with an ETABS automatic point name because preferred name '{preferredPointName}' was unavailable.");
                }

                if (ret != 0 || string.IsNullOrWhiteSpace(pointName))
                {
                    warnings.Add($"ETABS could not create point for wall/drain node '{node.Id}'. Return code: {ret}.");
                    continue;
                }

                nodePointNames[node.Id] = pointName;
                TryAssignPointToEtabsGroup(sapModel, pointName, mainGroup, node.Id, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS point creation failed for wall/drain node '{node.Id}': {ex.Message}");
            }
        }

        return nodePointNames;
    }

    private static void DrawWallDrainShellPanels(
        ETABSv1.cSapModel sapModel,
        WallDrainModel model,
        IReadOnlyDictionary<string, WallDrainNode> nodes,
        IReadOnlyDictionary<string, string> nodePointNames,
        string mainGroup,
        string shellGroup,
        string wallGroup,
        string slabGroup,
        string buttressGroup,
        string exportSuffix,
        Dictionary<string, string> areaNamesByPanelId,
        List<string> areaNames,
        List<string> warnings)
    {
        foreach (WallDrainShellPanel panel in model.ShellPanels)
        {
            List<string> pointNames = [];
            bool hasMissingNode = false;
            foreach (string nodeId in panel.NodeIds)
            {
                if (!nodes.ContainsKey(nodeId))
                {
                    hasMissingNode = true;
                    break;
                }

                if (!nodePointNames.TryGetValue(nodeId, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
                {
                    warnings.Add($"Skipped wall/drain shell panel '{panel.Id}': ETABS point for node '{nodeId}' was not available.");
                    hasMissingNode = true;
                    break;
                }

                pointNames.Add(pointName);
            }

            if (hasMissingNode)
                continue;

            if (pointNames.Count < 3)
            {
                warnings.Add($"Skipped wall/drain shell panel '{panel.Id}': fewer than three valid nodes.");
                continue;
            }

            string shellProperty = (panel.ShellPropertyName ?? "").Trim();
            if (shellProperty.Length == 0)
            {
                warnings.Add($"Skipped wall/drain shell panel '{panel.Id}': no shell property selected for {panel.Group}.");
                continue;
            }

            string areaName = "";
            string preferredName = BuildExportObjectName(panel.Id, exportSuffix);
            string[] pointNameArray = pointNames.ToArray();

            try
            {
                int ret = sapModel.AreaObj.AddByPoint(pointNameArray.Length, ref pointNameArray, ref areaName, shellProperty, preferredName);
                if (ret != 0)
                {
                    areaName = "";
                    pointNameArray = pointNames.ToArray();
                    ret = sapModel.AreaObj.AddByPoint(pointNameArray.Length, ref pointNameArray, ref areaName, shellProperty, "");
                    if (ret == 0)
                        warnings.Add($"Wall/drain shell panel '{panel.Id}' was drawn with ETABS automatic area name because preferred name '{preferredName}' was unavailable.");
                }

                if (ret != 0 || string.IsNullOrWhiteSpace(areaName))
                {
                    warnings.Add($"ETABS could not draw wall/drain shell panel '{panel.Id}'. Return code: {ret}.");
                    continue;
                }

                TryAssignAreaToEtabsGroup(sapModel, areaName, mainGroup, panel.Id, warnings);
                TryAssignAreaToEtabsGroup(sapModel, areaName, shellGroup, panel.Id, warnings);
                TryAssignAreaToEtabsGroup(sapModel, areaName, WallDrainPanelGroupToEtabsGroup(panel.Group, wallGroup, slabGroup, buttressGroup), panel.Id, warnings);
                areaNamesByPanelId[panel.Id] = areaName;
                areaNames.Add(areaName);
            }
            catch (Exception ex)
            {
                warnings.Add($"Wall/drain shell panel '{panel.Id}' drawing failed: {ex.Message}");
            }
        }
    }

    private static void TryAssignWallDrainSupportRestraints(
        ETABSv1.cSapModel sapModel,
        WallDrainModel model,
        Dictionary<string, string> nodePointNames,
        string supportGroup,
        List<string> warnings)
    {
        foreach (WallDrainNode node in model.Nodes.Where(node => node.IsSupport))
        {
            if (!nodePointNames.TryGetValue(node.Id, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
            {
                warnings.Add($"Skipped wall/drain support at node '{node.Id}': ETABS point name was not found.");
                continue;
            }

            TrySetPointRestraint(sapModel, pointName, [true, true, true, true, true, true], $"wall/drain support node '{node.Id}'", warnings);
            TryAssignPointToEtabsGroup(sapModel, pointName, supportGroup, node.Id, warnings);
        }
    }

    private static void TryApplyWallDrainFrameLoads(
        ETABSv1.cSapModel sapModel,
        WallDrainModel model,
        Dictionary<string, string> frameNamesByMemberId,
        string loadGroup,
        List<string> warnings)
    {
        foreach (string frameName in frameNamesByMemberId.Values)
        {
            TryClearFrameDistributedLoads(sapModel, frameName, warnings);
            TryClearFramePointLoads(sapModel, frameName, warnings);
        }

        if (model.SurfaceLoads.Count == 0)
            return;

        foreach (WallDrainSurfaceLoad load in model.SurfaceLoads)
        {
            if (string.IsNullOrWhiteSpace(load.LoadPattern))
            {
                warnings.Add($"Skipped wall/drain load '{load.Id}': no load pattern was selected.");
                continue;
            }

            var targetGroups = load.TargetGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<WallDrainFrameMember> targetMembers = model.FrameMembers
                .Where(member => targetGroups.Contains(member.Group))
                .ToList();

            if (targetMembers.Count == 0)
            {
                warnings.Add($"Skipped wall/drain load '{load.Id}': no matching vertical frame members were generated.");
                continue;
            }

            foreach (WallDrainFrameMember member in targetMembers)
            {
                if (!frameNamesByMemberId.TryGetValue(member.Id, out string? frameName) || string.IsNullOrWhiteSpace(frameName))
                {
                    warnings.Add($"Skipped wall/drain load '{load.Id}' on member '{member.Id}': ETABS frame name was not found.");
                    continue;
                }

                (double ValueI, double ValueJ) = CalculateWallDrainFrameLoadValues(member, load);
                int direction = ToEtabsDistributedLoadDirection(load.Direction, member.LoadSignX);
                try
                {
                    int ret = sapModel.FrameObj.SetLoadDistributed(
                        frameName,
                        load.LoadPattern,
                        1,
                        direction,
                        0,
                        1,
                        ValueI,
                        ValueJ,
                        "Global",
                        true,
                        false,
                        EtabsObjects);

                    if (ret != 0)
                        warnings.Add($"ETABS could not assign wall/drain load '{load.Id}' to frame '{frameName}'. Return code: {ret}.");
                    else
                        TryAssignFrameToEtabsGroup(sapModel, frameName, loadGroup, member.Id, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS wall/drain load '{load.Id}' assignment failed on frame '{frameName}': {ex.Message}");
                }
            }
        }
    }

    private static (double ValueI, double ValueJ) CalculateWallDrainFrameLoadValues(WallDrainFrameMember member, WallDrainSurfaceLoad load)
    {
        double sign = load.Direction switch
        {
            WallDrainLoadDirection.GlobalXNegative => -1.0,
            WallDrainLoadDirection.GlobalXPositive => 1.0,
            _ => member.LoadSignX == 0 ? 1.0 : Math.Sign(member.LoadSignX)
        };

        if (load.Kind == WallDrainLoadKind.Triangular)
            return (load.BottomPressureKnPerM2 * sign, load.TopPressureKnPerM2 * sign);

        return (load.UniformPressureKnPerM2 * sign, load.UniformPressureKnPerM2 * sign);
    }

    private static int ToEtabsDistributedLoadDirection(WallDrainLoadDirection direction, double loadSignX)
    {
        return direction switch
        {
            WallDrainLoadDirection.GlobalXPositive or WallDrainLoadDirection.GlobalXNegative => 4,
            _ => 4
        };
    }

    private static void TryDeleteWallDrainObjectsInGroup(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];

        try
        {
            int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
            if (ret != 0)
                return;

            int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
            var frames = new List<string>();
            var areas = new List<string>();
            var points = new List<string>();

            for (int index = 0; index < count; index++)
            {
                if (objectTypes[index] == EtabsSelectedFrameObjectType)
                    frames.Add(objectNames[index]);
                else if (objectTypes[index] == EtabsSelectedAreaObjectType)
                    areas.Add(objectNames[index]);
                else if (objectTypes[index] == EtabsSelectedPointObjectType)
                    points.Add(objectNames[index]);
            }

            foreach (string frame in frames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.Delete(frame, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing wall/drain frame '{frame}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            foreach (string area in areas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.AreaObj.Delete(area, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing wall/drain shell area '{area}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            foreach (string point in points.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                TrySetPointRestraint(sapModel, point, BuildFreePointRestraints(), $"existing wall/drain support point '{point}'", warnings);
                TryClearPointForceLoads(sapModel, point, warnings);
            }

            if (frames.Count > 0 || areas.Count > 0 || points.Count > 0)
                warnings.Add($"Removed {frames.Count} wall/drain frame object(s), {areas.Count} shell area object(s), and cleared {points.Distinct(StringComparer.OrdinalIgnoreCase).Count()} generated support point(s) from group '{groupName}' before drawing.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Existing wall/drain group '{groupName}' could not be cleaned before update: {ex.Message}");
        }
    }

    private static string WallDrainPanelGroupToEtabsGroup(string group, string wallGroup, string slabGroup, string buttressGroup)
    {
        return group switch
        {
            WallDrainPanelGroups.BaseSlab or WallDrainPanelGroups.TopSlab => slabGroup,
            WallDrainPanelGroups.Buttress or WallDrainPanelGroups.Counterfort => buttressGroup,
            _ => wallGroup
        };
    }

    private static void TryAssignWallDrainSupports(
        ETABSv1.cSapModel sapModel,
        WallDrainModel model,
        string mainGroup,
        string supportGroup,
        List<string> warnings)
    {
        foreach (WallDrainNode node in model.Nodes.Where(node => node.IsSupport))
        {
            string pointName = "";
            try
            {
                int ret = sapModel.PointObj.AddCartesian(node.X, node.Y, node.Z, ref pointName, "", "Global", false, 0);
                if (ret != 0 || string.IsNullOrWhiteSpace(pointName))
                {
                    warnings.Add($"ETABS could not create/reuse support point for wall/drain node '{node.Id}'. Return code: {ret}.");
                    continue;
                }

                TrySetPointRestraint(sapModel, pointName, [true, true, true, true, true, true], $"wall/drain support node '{node.Id}'", warnings);
                TryAssignPointToEtabsGroup(sapModel, pointName, mainGroup, node.Id, warnings);
                TryAssignPointToEtabsGroup(sapModel, pointName, supportGroup, node.Id, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"Wall/drain support assignment failed for node '{node.Id}': {ex.Message}");
            }
        }
    }

    private static void TryApplyWallDrainLoads(
        ETABSv1.cSapModel sapModel,
        WallDrainModel model,
        Dictionary<string, string> areaNamesByPanelId,
        string loadGroup,
        List<string> warnings)
    {
        foreach (string areaName in areaNamesByPanelId.Values)
            TryClearAreaUniformLoads(sapModel, areaName, warnings);

        if (model.SurfaceLoads.Count == 0)
            return;

        foreach (WallDrainSurfaceLoad load in model.SurfaceLoads)
        {
            if (string.IsNullOrWhiteSpace(load.LoadPattern))
            {
                warnings.Add($"Skipped wall/drain load '{load.Id}': no load pattern was selected.");
                continue;
            }

            var targetGroups = load.TargetGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<WallDrainShellPanel> targetPanels = model.ShellPanels
                .Where(panel => targetGroups.Contains(panel.Group))
                .ToList();

            if (targetPanels.Count == 0)
            {
                warnings.Add($"Skipped wall/drain load '{load.Id}': no matching target shell panels were generated.");
                continue;
            }

            if (load.Kind == WallDrainLoadKind.Triangular)
            {
                int appliedCount = TryApplyWallDrainNonUniformShellLoad(sapModel, model, load, targetPanels, areaNamesByPanelId, loadGroup, warnings);
                if (appliedCount > 0)
                    continue;

                warnings.Add($"ETABS non-uniform shell load table was not available for wall/drain load '{load.Id}'. Falling back to panel-by-panel uniform shell loads.");
            }

            foreach (WallDrainShellPanel panel in targetPanels)
            {
                if (!areaNamesByPanelId.TryGetValue(panel.Id, out string? areaName) || string.IsNullOrWhiteSpace(areaName))
                {
                    warnings.Add($"Skipped wall/drain load '{load.Id}' on panel '{panel.Id}': ETABS area name was not found.");
                    continue;
                }

                double pressure = CalculateWallDrainPanelPressure(model, panel, load);
                if (!double.IsFinite(pressure) || Math.Abs(pressure) <= 0.000001)
                    continue;

                try
                {
                    int direction = ToEtabsAreaLoadDirection(load.Direction);
                    int ret = sapModel.AreaObj.SetLoadUniform(areaName, load.LoadPattern, pressure, direction, false, "Global", EtabsObjects);
                    if (ret != 0)
                        warnings.Add($"ETABS could not assign wall/drain load '{load.Id}' to area '{areaName}'. Return code: {ret}.");
                    else
                        TryAssignAreaToEtabsGroup(sapModel, areaName, loadGroup, panel.Id, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS wall/drain load '{load.Id}' assignment failed on area '{areaName}': {ex.Message}");
                }
            }
        }
    }

    private static double CalculateWallDrainPanelPressure(WallDrainModel model, WallDrainShellPanel panel, WallDrainSurfaceLoad load)
    {
        double pressure = load.Kind == WallDrainLoadKind.Triangular
            ? InterpolateTriangularPressure(model.OriginZ, model.Height, panel.CentroidZ, load.TopPressureKnPerM2, load.BottomPressureKnPerM2)
            : load.UniformPressureKnPerM2;

        double sign = GetWallDrainPanelLoadSign(panel, load.Direction);

        return pressure * sign;
    }

    private static double InterpolateTriangularPressure(double baseZ, double height, double z, double topPressure, double bottomPressure)
    {
        double safeHeight = Math.Max(height, 0.000001);
        double zFromBase = z - baseZ;
        double depthRatio = Math.Clamp((height - zFromBase) / safeHeight, 0.0, 1.0);
        return topPressure + (bottomPressure - topPressure) * depthRatio;
    }

    private static double GetWallDrainPanelLoadSign(WallDrainShellPanel panel, WallDrainLoadDirection direction)
    {
        return direction switch
        {
            WallDrainLoadDirection.GlobalXNegative => -1.0,
            WallDrainLoadDirection.GlobalXPositive => 1.0,
            _ => panel.LoadSignX == 0 ? 1.0 : Math.Sign(panel.LoadSignX)
        };
    }

    private static int ToEtabsAreaLoadDirection(WallDrainLoadDirection direction)
    {
        return direction switch
        {
            WallDrainLoadDirection.GlobalXPositive or WallDrainLoadDirection.GlobalXNegative => 4,
            _ => 4
        };
    }

    private static HydrostaticShellLoadPreview BuildHydrostaticShellLoadPreview(
        ETABSv1.cSapModel sapModel,
        HydrostaticShellLoadInput input,
        List<string> warnings,
        bool validateLoadPatternExists)
    {
        string loadPattern = (input.LoadPatternName ?? "").Trim();
        if (loadPattern.Length == 0)
            throw new InvalidOperationException("Load pattern name is required.");

        if (!double.IsFinite(input.GammaKnPerM3) || input.GammaKnPerM3 <= 0)
            throw new InvalidOperationException("Unit weight gamma must be greater than zero.");

        if (!double.IsFinite(input.SurchargeKnPerM2) || input.SurchargeKnPerM2 < 0)
            throw new InvalidOperationException("Surcharge must be zero or greater.");

        List<string> existingLoadPatterns = GetLoadPatternNames(sapModel, warnings);
        if (validateLoadPatternExists && !existingLoadPatterns.Contains(loadPattern, StringComparer.OrdinalIgnoreCase))
        {
            if (input.CreateLoadPatternIfMissing)
                warnings.Add($"Load pattern '{loadPattern}' does not exist yet; it will be created as type Other with self-weight multiplier 0 during assignment.");
            else
                throw new InvalidOperationException($"Load pattern '{loadPattern}' does not exist in the connected ETABS model.");
        }

        List<HydrostaticShellArea> areas = ReadHydrostaticShellAreas(sapModel, input, warnings);
        if (areas.Count == 0)
            throw new InvalidOperationException("No ETABS shell/area objects were found for the selected target.");

        List<HydrostaticShellVertex> vertices = areas.SelectMany(area => area.Vertices).ToList();
        if (vertices.Count == 0)
            throw new InvalidOperationException("Selected shell/area objects have no readable vertices.");

        double envelopeTop = vertices.Max(vertex => vertex.Z);
        double envelopeBottom = vertices.Min(vertex => vertex.Z);
        double zTop;
        double zBottom;

        switch (input.HeightMode)
        {
            case HydroLoadHeightMode.WaterTableToWallBottom:
                zTop = input.UserZTop;
                zBottom = envelopeBottom;
                break;

            case HydroLoadHeightMode.CustomTopBottom:
                zTop = input.UserZTop;
                zBottom = input.UserZBottom;
                break;

            default:
                zTop = envelopeTop;
                zBottom = envelopeBottom;
                break;
        }

        if (!double.IsFinite(zTop) || !double.IsFinite(zBottom))
            throw new InvalidOperationException("zTop and zBottom must be finite numbers.");
        if (zTop <= zBottom)
            throw new InvalidOperationException("zTop must be greater than zBottom.");

        HydrostaticLoadCoefficients coefficients = CalculateHydrostaticCoefficients(
            zTop,
            zBottom,
            input.GammaKnPerM3,
            input.Sign,
            input.SurchargeKnPerM2);

        double qTop = EvaluateHydrostaticPressure(coefficients, 0, 0, zTop);
        double qBottom = EvaluateHydrostaticPressure(coefficients, 0, 0, zBottom);
        double height = zTop - zBottom;
        double pMax = input.GammaKnPerM3 * height + input.SurchargeKnPerM2;
        double expectedTopMagnitude = input.SurchargeKnPerM2;

        if (Math.Abs(Math.Abs(qTop) - expectedTopMagnitude) > 0.001)
            throw new InvalidOperationException("Coefficient check failed: qTop does not match the expected top pressure.");
        if (Math.Abs(Math.Abs(qBottom) - pMax) > 0.001)
            throw new InvalidOperationException("Coefficient check failed: qBottom does not match the expected bottom pressure.");

        if (input.HeightMode == HydroLoadHeightMode.WaterTableToWallBottom &&
            input.RestrictionOption == HydroLoadRestrictionOption.UseAllValues &&
            envelopeTop > zTop + 0.000001)
        {
            warnings.Add(input.Sign == HydroLoadSign.Negative
                ? "Wall extends above the water table. For negative pressure, consider using Zero Positive Values restriction."
                : "Wall extends above the water table. For positive pressure, consider using Zero Negative Values restriction.");
        }

        return new HydrostaticShellLoadPreview
        {
            LoadPatternName = loadPattern,
            Direction = input.Direction,
            Sign = input.Sign,
            RestrictionOption = input.RestrictionOption,
            AssignmentOption = input.AssignmentOption,
            ZTop = zTop,
            ZBottom = zBottom,
            Height = height,
            GammaKnPerM3 = input.GammaKnPerM3,
            SurchargeKnPerM2 = input.SurchargeKnPerM2,
            PMaxKnPerM2 = pMax,
            A = coefficients.A,
            B = coefficients.B,
            C = coefficients.C,
            D = coefficients.D,
            QTopKnPerM2 = qTop,
            QBottomKnPerM2 = qBottom,
            ShellCount = areas.Count,
            ShellNames = areas.Select(area => area.Name).ToList(),
            Warnings = warnings.ToList()
        };
    }

    private static HydrostaticLoadCoefficients CalculateHydrostaticCoefficients(
        double zTop,
        double zBottom,
        double gamma,
        HydroLoadSign sign,
        double surcharge = 0.0)
    {
        if (zTop <= zBottom)
            throw new ArgumentException("zTop must be greater than zBottom.", nameof(zTop));
        if (!double.IsFinite(gamma) || gamma <= 0)
            throw new ArgumentException("gamma must be greater than zero.", nameof(gamma));
        if (!double.IsFinite(surcharge) || surcharge < 0)
            throw new ArgumentException("surcharge must be zero or greater.", nameof(surcharge));

        return sign == HydroLoadSign.Positive
            ? new HydrostaticLoadCoefficients(0, 0, -gamma, gamma * zTop + surcharge)
            : new HydrostaticLoadCoefficients(0, 0, gamma, -gamma * zTop - surcharge);
    }

    private static double EvaluateHydrostaticPressure(HydrostaticLoadCoefficients coefficients, double x, double y, double z)
    {
        return coefficients.A * x + coefficients.B * y + coefficients.C * z + coefficients.D;
    }

    private static List<HydrostaticShellArea> ReadHydrostaticShellAreas(
        ETABSv1.cSapModel sapModel,
        HydrostaticShellLoadInput input,
        List<string> warnings)
    {
        List<string> areaNames = input.TargetMode switch
        {
            HydroLoadTargetMode.EtabsGroup => ReadAreaNamesInGroup(sapModel, input.GroupName, warnings),
            HydroLoadTargetMode.ShellNameList => ParseHydrostaticAreaNameList(input.ShellNames),
            _ => ReadSelectedAreaNames(sapModel, warnings)
        };

        var areas = new List<HydrostaticShellArea>();
        foreach (string areaName in areaNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            HydrostaticShellArea? area = TryReadHydrostaticShellArea(sapModel, areaName, warnings);
            if (area != null)
                areas.Add(area);
        }

        return areas;
    }

    private static List<string> ReadSelectedAreaNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        int ret = sapModel.SelectObj.GetSelected(ref numberItems, ref objectTypes, ref objectNames);
        if (ret != 0)
            throw new InvalidOperationException("Unable to read selected ETABS objects.");

        var areaNames = new List<string>();
        int ignoredCount = 0;
        int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
        for (int index = 0; index < count; index++)
        {
            string objectName = (objectNames[index] ?? "").Trim();
            if (objectName.Length == 0)
                continue;

            if (objectTypes[index] == EtabsSelectedAreaObjectType)
                areaNames.Add(objectName);
            else
                ignoredCount++;
        }

        if (ignoredCount > 0)
            warnings.Add($"Ignored {ignoredCount} selected ETABS object(s) that are not shell/area objects.");

        return areaNames;
    }

    private static List<string> ReadAreaNamesInGroup(ETABSv1.cSapModel sapModel, string? groupName, List<string> warnings)
    {
        string group = (groupName ?? "").Trim();
        if (group.Length == 0)
            throw new InvalidOperationException("Select an ETABS group before previewing or assigning group shell loads.");

        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        int ret = sapModel.GroupDef.GetAssignments(group, ref numberItems, ref objectTypes, ref objectNames);
        if (ret != 0)
            throw new InvalidOperationException($"Unable to read assignments for ETABS group '{group}'.");

        var areaNames = new List<string>();
        int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
        for (int index = 0; index < count; index++)
        {
            string objectName = (objectNames[index] ?? "").Trim();
            if (objectTypes[index] == EtabsSelectedAreaObjectType && objectName.Length > 0)
                areaNames.Add(objectName);
        }

        if (areaNames.Count == 0)
            warnings.Add($"ETABS group '{group}' contains no shell/area objects.");

        return areaNames;
    }

    private static List<string> ParseHydrostaticAreaNameList(IEnumerable<string> shellNames)
    {
        return shellNames
            .SelectMany(value => (value ?? "").Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries))
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HydrostaticShellArea? TryReadHydrostaticShellArea(
        ETABSv1.cSapModel sapModel,
        string areaName,
        List<string> warnings)
    {
        int numberPoints = 0;
        string[] pointNames = [];
        try
        {
            int ret = sapModel.AreaObj.GetPoints(areaName, ref numberPoints, ref pointNames);
            if (ret != 0 || numberPoints == 0)
            {
                warnings.Add($"ETABS area object '{areaName}' could not be read. Return code: {ret}.");
                return null;
            }

            var vertices = new List<HydrostaticShellVertex>();
            foreach (string pointName in pointNames.Take(Math.Min(numberPoints, pointNames.Length)).Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                (double X, double Y, double Z) coordinates = GetPointCoordinates(sapModel, pointName);
                vertices.Add(new HydrostaticShellVertex
                {
                    PointName = pointName,
                    X = coordinates.X,
                    Y = coordinates.Y,
                    Z = coordinates.Z
                });
            }

            if (vertices.Count < 3)
            {
                warnings.Add($"ETABS area object '{areaName}' has fewer than three readable vertices.");
                return null;
            }

            (string label, string story) = TryGetAreaLabelAndStory(sapModel, areaName);
            return new HydrostaticShellArea
            {
                Name = areaName,
                Label = label,
                Story = story,
                Vertices = vertices
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS area object '{areaName}' could not be read: {ex.Message}");
            return null;
        }
    }

    private static void EnsureHydrostaticLoadPattern(
        ETABSv1.cSapModel sapModel,
        HydrostaticShellLoadInput input,
        HydrostaticShellLoadPreview preview,
        List<string> warnings)
    {
        if (GetLoadPatternNames(sapModel, warnings).Contains(preview.LoadPatternName, StringComparer.OrdinalIgnoreCase))
            return;

        if (!input.CreateLoadPatternIfMissing)
            throw new InvalidOperationException($"Load pattern '{preview.LoadPatternName}' does not exist in the connected ETABS model.");

        int ret = sapModel.LoadPatterns.Add(preview.LoadPatternName, ETABSv1.eLoadPatternType.Other, 0, true);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create load pattern '{preview.LoadPatternName}'. Return code: {ret}.");

        warnings.Add($"Created load pattern '{preview.LoadPatternName}' as type Other with self-weight multiplier 0.");
    }

    private static int ApplyHydrostaticShellLoadTable(
        ETABSv1.cSapModel sapModel,
        HydrostaticShellLoadInput input,
        HydrostaticShellLoadPreview preview,
        List<string> warnings)
    {
        if (!TryGetNonUniformAreaLoadTable(sapModel, out string tableKey, out List<EtabsTableField> fields, warnings))
            return 0;

        var records = preview.ShellNames
            .Select(areaName =>
            {
                (string label, string story) = TryGetAreaLabelAndStory(sapModel, areaName);
                return new HydrostaticNonUniformAreaLoadRecord(
                    areaName,
                    label,
                    story,
                    preview.LoadPatternName,
                    ToEtabsHydrostaticDirection(preview.Direction),
                    ToEtabsHydrostaticRestriction(preview.RestrictionOption),
                    ToEtabsHydrostaticAssignment(preview.AssignmentOption),
                    preview.AssignmentOption == HydroLoadAssignmentOption.DeleteExisting ? 0 : preview.A,
                    preview.AssignmentOption == HydroLoadAssignmentOption.DeleteExisting ? 0 : preview.B,
                    preview.AssignmentOption == HydroLoadAssignmentOption.DeleteExisting ? 0 : preview.C,
                    preview.AssignmentOption == HydroLoadAssignmentOption.DeleteExisting ? 0 : preview.D);
            })
            .ToList();

        if (records.Count == 0)
            return 0;

        string[] fieldKeys = fields.Select(field => field.Key).ToArray();
        var tableData = new List<string>(records.Count * fieldKeys.Length);
        foreach (HydrostaticNonUniformAreaLoadRecord record in records)
        {
            foreach (EtabsTableField field in fields)
                tableData.Add(BuildNonUniformAreaLoadTableValue(field, record));
        }

        int tableVersion = 0;
        string[] tableDataArray = tableData.ToArray();
        try
        {
            int ret = sapModel.DatabaseTables.SetTableForEditingArray(tableKey, ref tableVersion, ref fieldKeys, records.Count, ref tableDataArray);
            if (ret != 0)
            {
                warnings.Add($"ETABS non-uniform shell load table '{tableKey}' could not be staged. Return code: {ret}.");
                return 0;
            }

            int fatalError = 0;
            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;
            string importLog = "";
            ret = sapModel.DatabaseTables.ApplyEditedTables(true, ref fatalError, ref errorCount, ref warningCount, ref infoCount, ref importLog);
            if (ret != 0 || fatalError > 0 || errorCount > 0)
            {
                string summary = string.IsNullOrWhiteSpace(importLog) ? "" : " ETABS import log: " + importLog.Trim();
                warnings.Add($"ETABS non-uniform shell load table '{tableKey}' was rejected. Return code: {ret}; fatal errors: {fatalError}; errors: {errorCount}.{summary}");
                return 0;
            }

            if (warningCount > 0 && !string.IsNullOrWhiteSpace(importLog))
                warnings.Add($"ETABS reported {warningCount} warning(s) while importing hydrostatic shell loads: {importLog.Trim()}");

            warnings.Add($"Used ETABS database table '{tableKey}' for non-uniform shell load assignment.");
            return records.Count;
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS non-uniform shell load assignment failed: {ex.Message}");
            return 0;
        }
    }

    private static string BuildNonUniformAreaLoadTableValue(EtabsTableField field, HydrostaticNonUniformAreaLoadRecord record)
    {
        if (IsStoryField(field))
            return record.Story;
        if (IsLabelField(field))
            return record.Label;
        if (IsAreaObjectField(field))
            return record.AreaName;
        if (IsLoadPatternField(field))
            return record.LoadPattern;
        if (IsCoordinateSystemField(field))
            return "Global";
        if (IsDirectionField(field))
            return record.Direction;
        if (IsCoefficientField(field, "A"))
            return FormatTableDouble(record.A);
        if (IsCoefficientField(field, "B"))
            return FormatTableDouble(record.B);
        if (IsCoefficientField(field, "C"))
            return FormatTableDouble(record.C);
        if (IsCoefficientField(field, "D"))
            return FormatTableDouble(record.D);
        if (IsRestrictionField(field))
            return record.Restriction;
        if (IsOptionField(field))
            return record.Assignment;
        if (IsReplaceField(field))
            return record.Assignment.Contains("Replace", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
        if (IsDeleteField(field))
            return record.Assignment.Contains("Delete", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
        if (IsItemTypeField(field))
            return "Objects";

        return "";
    }

    private static string ToEtabsHydrostaticDirection(HydroLoadDirection direction)
    {
        return direction switch
        {
            HydroLoadDirection.GlobalY => "Global Y",
            HydroLoadDirection.GlobalZ => "Global Z",
            _ => "Global X"
        };
    }

    private static string ToEtabsHydrostaticRestriction(HydroLoadRestrictionOption restriction)
    {
        return restriction switch
        {
            HydroLoadRestrictionOption.ZeroNegativeValues => "Zero Negative Values",
            HydroLoadRestrictionOption.ZeroPositiveValues => "Zero Positive Values",
            _ => "Use All Values"
        };
    }

    private static string ToEtabsHydrostaticAssignment(HydroLoadAssignmentOption assignment)
    {
        return assignment switch
        {
            HydroLoadAssignmentOption.AddToExisting => "Add to Existing Loads",
            HydroLoadAssignmentOption.DeleteExisting => "Delete Existing Loads",
            _ => "Replace Existing Loads"
        };
    }

    private sealed record HydrostaticNonUniformAreaLoadRecord(
        string AreaName,
        string Label,
        string Story,
        string LoadPattern,
        string Direction,
        string Restriction,
        string Assignment,
        double A,
        double B,
        double C,
        double D);

    private static int TryApplyWallDrainNonUniformShellLoad(
        ETABSv1.cSapModel sapModel,
        WallDrainModel model,
        WallDrainSurfaceLoad load,
        IReadOnlyCollection<WallDrainShellPanel> targetPanels,
        IReadOnlyDictionary<string, string> areaNamesByPanelId,
        string loadGroup,
        List<string> warnings)
    {
        if (!TryGetNonUniformAreaLoadTable(sapModel, out string tableKey, out List<EtabsTableField> fields, warnings))
            return 0;

        var records = new List<WallDrainNonUniformAreaLoadRecord>();
        foreach (WallDrainShellPanel panel in targetPanels)
        {
            if (!areaNamesByPanelId.TryGetValue(panel.Id, out string? areaName) || string.IsNullOrWhiteSpace(areaName))
            {
                warnings.Add($"Skipped wall/drain non-uniform load '{load.Id}' on panel '{panel.Id}': ETABS area name was not found.");
                continue;
            }

            (double a, double b, double c, double d) = CalculateWallDrainNonUniformLoadCoefficients(model, panel, load);
            (string label, string story) = TryGetAreaLabelAndStory(sapModel, areaName);
            records.Add(new WallDrainNonUniformAreaLoadRecord(panel.Id, areaName, label, story, load.LoadPattern, ToEtabsNonUniformShellDirection(load.Direction), a, b, c, d));
        }

        if (records.Count == 0)
            return 0;

        string[] fieldKeys = fields.Select(field => field.Key).ToArray();
        var tableData = new List<string>(records.Count * fieldKeys.Length);
        foreach (WallDrainNonUniformAreaLoadRecord record in records)
        {
            foreach (EtabsTableField field in fields)
                tableData.Add(BuildNonUniformAreaLoadTableValue(field, record));
        }

        int tableVersion = 0;
        string[] tableDataArray = tableData.ToArray();
        try
        {
            int ret = sapModel.DatabaseTables.SetTableForEditingArray(tableKey, ref tableVersion, ref fieldKeys, records.Count, ref tableDataArray);
            if (ret != 0)
            {
                warnings.Add($"ETABS non-uniform shell load table '{tableKey}' could not be staged. Return code: {ret}.");
                return 0;
            }

            int fatalError = 0;
            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;
            string importLog = "";
            ret = sapModel.DatabaseTables.ApplyEditedTables(true, ref fatalError, ref errorCount, ref warningCount, ref infoCount, ref importLog);
            if (ret != 0 || fatalError > 0 || errorCount > 0)
            {
                string summary = string.IsNullOrWhiteSpace(importLog) ? "" : " ETABS import log: " + importLog.Trim();
                warnings.Add($"ETABS non-uniform shell load table '{tableKey}' was rejected. Return code: {ret}; fatal errors: {fatalError}; errors: {errorCount}.{summary}");
                return 0;
            }

            if (warningCount > 0 && !string.IsNullOrWhiteSpace(importLog))
                warnings.Add($"ETABS reported {warningCount} warning(s) while importing non-uniform shell loads: {importLog.Trim()}");

            foreach (WallDrainNonUniformAreaLoadRecord record in records)
                TryAssignAreaToEtabsGroup(sapModel, record.AreaName, loadGroup, record.PanelId, warnings);

            warnings.Add($"Applied wall/drain triangular load '{load.Id}' as ETABS non-uniform shell load on {records.Count} area object(s) using table '{tableKey}'.");
            return records.Count;
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS non-uniform shell load assignment failed for wall/drain load '{load.Id}': {ex.Message}");
            return 0;
        }
    }

    private static (double A, double B, double C, double D) CalculateWallDrainNonUniformLoadCoefficients(
        WallDrainModel model,
        WallDrainShellPanel panel,
        WallDrainSurfaceLoad load)
    {
        double sign = GetWallDrainPanelLoadSign(panel, load.Direction);
        double bottomPressure = load.BottomPressureKnPerM2 * sign;
        double topPressure = load.TopPressureKnPerM2 * sign;
        double safeHeight = Math.Max(model.Height, 0.000001);
        double baseZ = model.OriginZ;
        double topZ = model.OriginZ + safeHeight;
        double c = (topPressure - bottomPressure) / (topZ - baseZ);
        double d = bottomPressure - c * baseZ;
        return (0, 0, c, d);
    }

    private static bool TryGetNonUniformAreaLoadTable(
        ETABSv1.cSapModel sapModel,
        out string tableKey,
        out List<EtabsTableField> fields,
        List<string> warnings)
    {
        tableKey = "";
        fields = [];

        if (!TryGetEtabsTableList(sapModel, out List<(string Key, string Name)> tables, warnings))
            return false;

        foreach ((string Key, string Name) table in tables
            .Where(table => IsNonUniformAreaLoadTable(table.Key, table.Name))
            .OrderByDescending(table => ScoreNonUniformAreaLoadTable(table.Key, table.Name)))
        {
            if (!TryGetEditableTableFields(sapModel, table.Key, out List<EtabsTableField> candidateFields))
                continue;

            if (!HasRequiredNonUniformAreaLoadFields(candidateFields))
                continue;

            tableKey = table.Key;
            fields = candidateFields;
            return true;
        }

        warnings.Add("Could not find an editable ETABS database table for non-uniform area/shell load assignments.");
        return false;
    }

    private static bool TryGetEtabsTableList(
        ETABSv1.cSapModel sapModel,
        out List<(string Key, string Name)> tables,
        List<string> warnings)
    {
        tables = [];
        try
        {
            int tableCount = 0;
            string[] tableKeys = [];
            string[] tableNames = [];
            int[] importTypes = [];

            int ret = sapModel.DatabaseTables.GetAvailableTables(ref tableCount, ref tableKeys, ref tableNames, ref importTypes);
            if (ret == 0 && tableKeys.Length > 0)
            {
                int availableCount = Math.Min(tableCount, Math.Min(tableKeys.Length, tableNames.Length));
                for (int index = 0; index < availableCount; index++)
                {
                    string key = (tableKeys[index] ?? "").Trim();
                    if (key.Length == 0)
                        continue;

                    tables.Add((key, (tableNames[index] ?? "").Trim()));
                }

                if (tables.Count > 0)
                    return true;
            }

            bool[] isEmpty = [];
            tableCount = 0;
            tableKeys = [];
            tableNames = [];
            importTypes = [];
            ret = sapModel.DatabaseTables.GetAllTables(ref tableCount, ref tableKeys, ref tableNames, ref importTypes, ref isEmpty);
            if (ret != 0)
            {
                warnings.Add($"ETABS database table list could not be read. Return code: {ret}.");
                return false;
            }

            int count = Math.Min(tableCount, Math.Min(tableKeys.Length, tableNames.Length));
            for (int index = 0; index < count; index++)
            {
                string key = (tableKeys[index] ?? "").Trim();
                if (key.Length == 0)
                    continue;

                tables.Add((key, (tableNames[index] ?? "").Trim()));
            }

            return tables.Count > 0;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS database table list could not be read: " + ex.Message);
            return false;
        }
    }

    private static bool TryGetEditableTableFields(
        ETABSv1.cSapModel sapModel,
        string tableKey,
        out List<EtabsTableField> fields)
    {
        fields = [];
        try
        {
            int fieldCount = 0;
            int tableVersion = 0;
            string[] fieldKeys = [];
            string[] fieldNames = [];
            string[] descriptions = [];
            string[] units = [];
            bool[] isImportable = [];
            int ret = sapModel.DatabaseTables.GetAllFieldsInTable(tableKey, ref fieldCount, ref tableVersion, ref fieldKeys, ref fieldNames, ref descriptions, ref units, ref isImportable);
            if (ret != 0)
                return false;

            int count = Math.Min(fieldCount, fieldKeys.Length);
            var allFields = new List<EtabsTableField>();
            for (int index = 0; index < count; index++)
            {
                string key = (fieldKeys[index] ?? "").Trim();
                if (key.Length == 0)
                    continue;

                string name = index < fieldNames.Length ? (fieldNames[index] ?? "").Trim() : "";
                bool importable = index >= isImportable.Length || isImportable[index];
                allFields.Add(new EtabsTableField(key, name, importable));
            }

            bool hasImportableFlags = allFields.Any(field => field.IsImportable);
            fields = hasImportableFlags
                ? allFields.Where(field => field.IsImportable).ToList()
                : allFields;
            return fields.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNonUniformAreaLoadTable(string key, string name)
    {
        string normalized = NormalizeEtabsTableText($"{key} {name}");
        return normalized.Contains("LOAD", StringComparison.OrdinalIgnoreCase) &&
            normalized.Contains("NON", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Contains("AREA", StringComparison.OrdinalIgnoreCase) || normalized.Contains("SHELL", StringComparison.OrdinalIgnoreCase));
    }

    private static int ScoreNonUniformAreaLoadTable(string key, string name)
    {
        string normalized = NormalizeEtabsTableText($"{key} {name}");
        int score = 0;
        if (normalized.Contains("ASSIGN", StringComparison.OrdinalIgnoreCase))
            score += 4;
        if (normalized.Contains("AREA", StringComparison.OrdinalIgnoreCase))
            score += 3;
        if (normalized.Contains("SHELL", StringComparison.OrdinalIgnoreCase))
            score += 2;
        if (normalized.Contains("NONUNIFORM", StringComparison.OrdinalIgnoreCase))
            score += 2;
        return score;
    }

    private static bool HasRequiredNonUniformAreaLoadFields(IReadOnlyCollection<EtabsTableField> fields)
    {
        bool hasAreaIdentity = fields.Any(IsAreaObjectField) ||
            (fields.Any(IsStoryField) && fields.Any(IsLabelField));

        return hasAreaIdentity &&
            fields.Any(IsLoadPatternField) &&
            fields.Any(field => IsCoefficientField(field, "A")) &&
            fields.Any(field => IsCoefficientField(field, "B")) &&
            fields.Any(field => IsCoefficientField(field, "C")) &&
            fields.Any(field => IsCoefficientField(field, "D"));
    }

    private static string BuildNonUniformAreaLoadTableValue(EtabsTableField field, WallDrainNonUniformAreaLoadRecord record)
    {
        if (IsStoryField(field))
            return record.Story;
        if (IsLabelField(field))
            return record.Label;
        if (IsAreaObjectField(field))
            return record.AreaName;
        if (IsLoadPatternField(field))
            return record.LoadPattern;
        if (IsCoordinateSystemField(field))
            return "Global";
        if (IsDirectionField(field))
            return record.Direction;
        if (IsCoefficientField(field, "A"))
            return FormatTableDouble(record.A);
        if (IsCoefficientField(field, "B"))
            return FormatTableDouble(record.B);
        if (IsCoefficientField(field, "C"))
            return FormatTableDouble(record.C);
        if (IsCoefficientField(field, "D"))
            return FormatTableDouble(record.D);
        if (IsRestrictionField(field))
            return "Use All Values";
        if (IsOptionField(field))
            return "Replace Existing Loads";
        if (IsReplaceField(field))
            return "Yes";
        if (IsDeleteField(field))
            return "No";
        if (IsItemTypeField(field))
            return "Objects";

        return "";
    }

    private static (string Label, string Story) TryGetAreaLabelAndStory(ETABSv1.cSapModel sapModel, string areaName)
    {
        string label = "";
        string story = "";
        try
        {
            sapModel.AreaObj.GetLabelFromName(areaName, ref label, ref story);
        }
        catch
        {
            // Unique area name is enough for ETABS versions that expose it in the table.
        }

        return (label, story);
    }

    private static string ToEtabsNonUniformShellDirection(WallDrainLoadDirection direction)
    {
        return "Global X";
    }

    private static bool IsStoryField(EtabsTableField field)
    {
        return NormalizeFieldText(field).Contains("STORY", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLabelField(EtabsTableField field)
    {
        return NormalizeFieldText(field).Contains("LABEL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAreaObjectField(EtabsTableField field)
    {
        string key = NormalizeEtabsTableText(field.Key);
        string text = NormalizeFieldText(field);
        return key is "UNIQUENAME" or "UNIQUEID" or "AREA" or "AREANAME" or "OBJECT" or "OBJECTNAME" or "OBJ" or "NAME" ||
            text.Contains("UNIQUENAME", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("AREAOBJECT", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("AREANAME", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("OBJECTNAME", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoadPatternField(EtabsTableField field)
    {
        string text = NormalizeFieldText(field);
        return text.Contains("LOADPAT", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("LOADPATTERN", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoordinateSystemField(EtabsTableField field)
    {
        string text = NormalizeFieldText(field);
        return text.Contains("CSYS", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("COORDSYS", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("COORDINATESYSTEM", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectionField(EtabsTableField field)
    {
        string text = NormalizeFieldText(field);
        return text.Contains("DIR", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("COORD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoefficientField(EtabsTableField field, string coefficient)
    {
        string key = NormalizeEtabsTableText(field.Key);
        string name = NormalizeEtabsTableText(field.Name);
        return key == coefficient ||
            name == coefficient ||
            key == $"COEF{coefficient}" ||
            name == $"COEF{coefficient}" ||
            key == $"COEFFICIENT{coefficient}" ||
            name == $"COEFFICIENT{coefficient}";
    }

    private static bool IsRestrictionField(EtabsTableField field)
    {
        return NormalizeFieldText(field).Contains("RESTRICT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOptionField(EtabsTableField field)
    {
        return NormalizeFieldText(field).Contains("OPTION", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReplaceField(EtabsTableField field)
    {
        return NormalizeFieldText(field).Contains("REPLACE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeleteField(EtabsTableField field)
    {
        return NormalizeFieldText(field).Contains("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsItemTypeField(EtabsTableField field)
    {
        string text = NormalizeFieldText(field);
        return text.Contains("ITEMTYPE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ASSIGNTYPE", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFieldText(EtabsTableField field)
    {
        return NormalizeEtabsTableText($"{field.Key} {field.Name}");
    }

    private static string NormalizeEtabsTableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static string FormatTableDouble(double value)
    {
        return value.ToString("0.############", CultureInfo.InvariantCulture);
    }

    private sealed record EtabsTableField(string Key, string Name, bool IsImportable);

    private sealed record WallDrainNonUniformAreaLoadRecord(
        string PanelId,
        string AreaName,
        string Label,
        string Story,
        string LoadPattern,
        string Direction,
        double A,
        double B,
        double C,
        double D);

    private static List<string> ReadSelectedFrameNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        int ret = sapModel.SelectObj.GetSelected(ref numberItems, ref objectTypes, ref objectNames);
        if (ret != 0)
            throw new InvalidOperationException("Unable to read selected objects from ETABS.");

        var frameNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ignoredCount = 0;
        int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));

        for (int index = 0; index < count; index++)
        {
            string name = (objectNames[index] ?? "").Trim();
            if (name.Length == 0)
                continue;

            if (objectTypes[index] != EtabsSelectedFrameObjectType)
            {
                ignoredCount++;
                continue;
            }

            if (seen.Add(name))
                frameNames.Add(name);
        }

        if (ignoredCount > 0)
            warnings.Add($"Ignored {ignoredCount} selected ETABS object(s) that are not frame objects.");

        return frameNames;
    }

    private static List<string> ReadFrameNamesInGroup(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        string normalizedGroupName = (groupName ?? "").Trim();
        if (normalizedGroupName.Length == 0)
            throw new InvalidOperationException("Select an ETABS group first.");

        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        int ret = sapModel.GroupDef.GetAssignments(normalizedGroupName, ref numberItems, ref objectTypes, ref objectNames);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS group '{normalizedGroupName}' could not be read. Return code: {ret}.");

        var frameNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ignoredCount = 0;
        int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));

        for (int index = 0; index < count; index++)
        {
            string name = (objectNames[index] ?? "").Trim();
            if (name.Length == 0)
                continue;

            if (objectTypes[index] != EtabsSelectedFrameObjectType)
            {
                ignoredCount++;
                continue;
            }

            if (seen.Add(name))
                frameNames.Add(name);
        }

        if (ignoredCount > 0)
            warnings.Add($"Ignored {ignoredCount} non-frame ETABS object(s) in group '{normalizedGroupName}'.");

        return frameNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetAllFrameNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            int ret = sapModel.FrameObj.GetNameList(ref numberNames, ref names);
            if (ret != 0)
            {
                warnings.Add($"ETABS frame object list could not be loaded. Return code: {ret}.");
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
            warnings.Add("ETABS frame object list could not be loaded: " + ex.Message);
            return [];
        }
    }

    private static EtabsFrameSectionRow ReadFrameSectionRow(ETABSv1.cSapModel sapModel, string frameName, List<string> warnings, string groupName = "")
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
        double length = 0;
        (double X, double Y, double Z) pointICoord = (0, 0, 0);
        (double X, double Y, double Z) pointJCoord = (0, 0, 0);
        try
        {
            int ret = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
            if (ret == 0 && pointI.Length > 0 && pointJ.Length > 0)
            {
                pointICoord = GetPointCoordinates(sapModel, pointI);
                pointJCoord = GetPointCoordinates(sapModel, pointJ);
                length = CalculateFrameLength(pointICoord, pointJCoord);
            }
        }
        catch
        {
            // End points and length are useful display values, not required for section update.
        }

        return new EtabsFrameSectionRow
        {
            Include = true,
            FrameName = frameName,
            Label = label,
            Story = story,
            GroupName = groupName,
            CurrentSection = currentSection,
            NewSection = currentSection,
            PointI = pointI,
            PointJ = pointJ,
            LengthM = length,
            IX = pointICoord.X,
            IY = pointICoord.Y,
            IZ = pointICoord.Z,
            JX = pointJCoord.X,
            JY = pointJCoord.Y,
            JZ = pointJCoord.Z
        };
    }

    private static ModelCompareFrameSnapshot ReadModelCompareFrameSnapshot(
        ETABSv1.cSapModel sapModel,
        string frameName,
        IReadOnlyDictionary<string, string> materialBySection,
        IReadOnlyDictionary<string, List<string>> groupsByFrameName,
        List<string> warnings)
    {
        EtabsFrameSectionRow row = ReadFrameSectionRow(sapModel, frameName, warnings);
        string materialName = materialBySection.TryGetValue(row.CurrentSection, out string? sectionMaterial)
            ? sectionMaterial
            : "";

        return new ModelCompareFrameSnapshot
        {
            FrameName = row.FrameName,
            Label = row.Label,
            Story = row.Story,
            PointIName = row.PointI,
            PointJName = row.PointJ,
            IX = row.IX,
            IY = row.IY,
            IZ = row.IZ,
            JX = row.JX,
            JY = row.JY,
            JZ = row.JZ,
            Length = row.LengthM,
            SectionName = row.CurrentSection,
            MaterialName = materialName,
            GroupNames = groupsByFrameName.TryGetValue(frameName, out List<string>? groupNames)
                ? groupNames.ToList()
                : []
        };
    }

    private static Dictionary<string, List<string>> GetModelCompareFrameGroups(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        var groupsByFrameName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string groupName in GetGroupNames(sapModel, warnings))
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

    private static TaperedSteelFrameInfo ReadTaperedSteelFrameInfo(ETABSv1.cSapModel sapModel, string frameName)
    {
        string currentSection = "";
        string autoSelectList = "";
        int sectionRet = sapModel.FrameObj.GetSection(frameName, ref currentSection, ref autoSelectList);
        if (sectionRet != 0)
            throw new InvalidOperationException($"ETABS could not read current section for selected frame '{frameName}'. Return code: {sectionRet}.");

        if (string.IsNullOrWhiteSpace(currentSection))
            currentSection = autoSelectList;
        if (string.IsNullOrWhiteSpace(currentSection))
            throw new InvalidOperationException($"Selected frame '{frameName}' does not have a readable assigned frame section.");

        string pointI = "";
        string pointJ = "";
        int pointRet = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
        if (pointRet != 0 || string.IsNullOrWhiteSpace(pointI) || string.IsNullOrWhiteSpace(pointJ))
            throw new InvalidOperationException($"ETABS could not read I/J end points for selected frame '{frameName}'. Return code: {pointRet}.");

        (double X, double Y, double Z) pointICoord = GetPointCoordinates(sapModel, pointI);
        (double X, double Y, double Z) pointJCoord = GetPointCoordinates(sapModel, pointJ);

        double localAxesAngle = 0;
        try
        {
            bool advanced = false;
            sapModel.FrameObj.GetLocalAxes(frameName, ref localAxesAngle, ref advanced);
        }
        catch
        {
            localAxesAngle = 0;
        }

        return new TaperedSteelFrameInfo
        {
            FrameName = frameName,
            SectionName = currentSection.Trim(),
            PointI = pointI,
            PointJ = pointJ,
            LengthM = CalculateFrameLength(pointICoord, pointJCoord),
            IX = pointICoord.X,
            IY = pointICoord.Y,
            IZ = pointICoord.Z,
            JX = pointJCoord.X,
            JY = pointJCoord.Y,
            JZ = pointJCoord.Z,
            LocalAxesAngleDegrees = localAxesAngle
        };
    }

    private static TaperedSteelSectionGeometry GetTaperedSteelBaseSectionGeometry(ETABSv1.cSapModel sapModel, string sectionName)
    {
        ETABSv1.eFramePropType propType = ETABSv1.eFramePropType.I;
        bool hasType = false;
        try
        {
            int typeRet = sapModel.PropFrame.GetTypeOAPI(sectionName, ref propType);
            hasType = typeRet == 0;
        }
        catch
        {
            hasType = false;
        }

        if (hasType && propType == ETABSv1.eFramePropType.Box)
            return GetTaperedSteelTubeSectionGeometry(sapModel, sectionName);

        if (hasType && propType != ETABSv1.eFramePropType.I)
            throw new InvalidOperationException($"Selected section '{sectionName}' is a {FormatFramePropType(propType)} section. Tapered steel currently supports Steel I and Steel Tube/Box base sections.");

        try
        {
            return GetTaperedSteelISectionGeometry(sapModel, sectionName);
        }
        catch when (!hasType)
        {
            return GetTaperedSteelTubeSectionGeometry(sapModel, sectionName);
        }
    }

    private static TaperedSteelSectionGeometry GetTaperedSteelISectionGeometry(ETABSv1.cSapModel sapModel, string sectionName)
    {
        string fileName = "";
        string materialName = "";
        double depth = 0;
        double topWidth = 0;
        double topFlangeThickness = 0;
        double webThickness = 0;
        double bottomWidth = 0;
        double bottomFlangeThickness = 0;
        int color = 0;
        string notes = "";
        string guid = "";

        int ret = sapModel.PropFrame.GetISection(
            sectionName,
            ref fileName,
            ref materialName,
            ref depth,
            ref topWidth,
            ref topFlangeThickness,
            ref webThickness,
            ref bottomWidth,
            ref bottomFlangeThickness,
            ref color,
            ref notes,
            ref guid);

        if (ret != 0)
            throw new InvalidOperationException($"Selected section '{sectionName}' is not a supported ETABS I-section in this version. Import/create it as a Steel I frame section first. Return code: {ret}.");

        if (string.IsNullOrWhiteSpace(materialName))
        {
            try
            {
                sapModel.PropFrame.GetMaterial(sectionName, ref materialName);
            }
            catch
            {
                materialName = "";
            }
        }

        return new TaperedSteelSectionGeometry
        {
            SectionName = sectionName,
            MaterialName = materialName.Trim(),
            SectionKind = TaperedBaseSectionKind.ISection,
            DepthM = depth,
            TopFlangeWidthM = topWidth,
            TopFlangeThicknessM = topFlangeThickness,
            WebThicknessM = webThickness,
            BottomFlangeWidthM = bottomWidth,
            BottomFlangeThicknessM = bottomFlangeThickness
        };
    }

    private static TaperedSteelSectionGeometry GetTaperedSteelTubeSectionGeometry(ETABSv1.cSapModel sapModel, string sectionName)
    {
        string fileName = "";
        string materialName = "";
        double depth = 0;
        double width = 0;
        double flangeThickness = 0;
        double webThickness = 0;
        int color = 0;
        string notes = "";
        string guid = "";

        int ret = sapModel.PropFrame.GetTube(
            sectionName,
            ref fileName,
            ref materialName,
            ref depth,
            ref width,
            ref flangeThickness,
            ref webThickness,
            ref color,
            ref notes,
            ref guid);

        if (ret != 0)
            throw new InvalidOperationException($"Selected section '{sectionName}' is not a supported ETABS Steel I or Steel Tube/Box section. Return code: {ret}.");

        if (string.IsNullOrWhiteSpace(materialName))
        {
            try
            {
                sapModel.PropFrame.GetMaterial(sectionName, ref materialName);
            }
            catch
            {
                materialName = "";
            }
        }

        return new TaperedSteelSectionGeometry
        {
            SectionName = sectionName,
            MaterialName = materialName.Trim(),
            SectionKind = TaperedBaseSectionKind.TubeSection,
            DepthM = depth,
            TopFlangeWidthM = width,
            TopFlangeThicknessM = flangeThickness,
            WebThicknessM = webThickness,
            BottomFlangeWidthM = width,
            BottomFlangeThicknessM = flangeThickness
        };
    }

    private static void ValidateBaseTaperedSteelGeometry(TaperedSteelSectionGeometry geometry)
    {
        if (string.IsNullOrWhiteSpace(geometry.SectionName))
            throw new InvalidOperationException("Base steel section name is missing.");
        if (string.IsNullOrWhiteSpace(geometry.MaterialName))
            throw new InvalidOperationException($"Base steel section '{geometry.SectionName}' does not expose a material name through ETABS.");

        string label = geometry.SectionKind == TaperedBaseSectionKind.TubeSection ? "Base tube/box section" : "Base I-section";
        EnsurePositive(geometry.DepthM, $"{label} depth");
        EnsurePositive(geometry.TopFlangeWidthM, $"{label} width");
        EnsurePositive(geometry.TopFlangeThicknessM, $"{label} top flange/wall thickness");
        EnsurePositive(geometry.WebThicknessM, $"{label} web/side wall thickness");
        EnsurePositive(geometry.BottomFlangeWidthM, $"{label} bottom flange/wall width");
        EnsurePositive(geometry.BottomFlangeThicknessM, $"{label} bottom flange/wall thickness");
    }

    private static TaperedSteelGenerationPreview BuildTaperedSteelPreview(TaperedSteelApplyRequest request, List<string> warnings)
    {
        if (request.Selection == null || string.IsNullOrWhiteSpace(request.Selection.BaseSectionName))
            throw new InvalidOperationException("Load a base steel I or steel tube/box section before previewing a tapered section.");

        TaperedSteelSelection selection = request.Selection;
        TaperedSteelSectionGeometry geometry = selection.BaseGeometry;
        ValidateBaseTaperedSteelGeometry(geometry);
        ValidateTaperedSteelModeCompatibility(geometry, request.TaperType);

        if (selection.Frames.Count > 0 && selection.Frames.Any(frame => frame.LengthM <= 0))
            throw new InvalidOperationException("Selected frame member length must be greater than zero.");

        if (selection.Frames.Count > 0)
        {
            List<IGrouping<string, TaperedSteelFrameInfo>> sectionGroups = selection.Frames
                .GroupBy(frame => frame.SectionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sectionGroups.Count != 1)
                throw new InvalidOperationException("Selected frame members must all use the same base section for this first tapered steel implementation.");
        }

        double tipDepth = EnsurePositive(request.TipDepthM, "Tip remaining depth");
        if (tipDepth >= geometry.DepthM)
            throw new InvalidOperationException($"Tip remaining depth must be smaller than the original section depth ({geometry.DepthM * 1000.0:0.#} mm).");

        double minimumWebDepth = Math.Max(0.05, 3.0 * geometry.WebThicknessM);
        if (IsClosedTaperMode(request.TaperType))
        {
            double minimumDepth = geometry.TopFlangeThicknessM + geometry.BottomFlangeThicknessM + minimumWebDepth;
            if (tipDepth <= minimumDepth)
                throw new InvalidOperationException($"Tip depth is too shallow for a closed tapered section. Minimum is greater than {minimumDepth * 1000.0:0.#} mm for the current wall/flange thicknesses.");
        }
        else
        {
            double minimumDepth = geometry.TopFlangeThicknessM + minimumWebDepth;
            if (tipDepth <= minimumDepth)
                throw new InvalidOperationException($"Tip depth is too shallow for a bottom-removed tapered section. Minimum is greater than {minimumDepth * 1000.0:0.#} mm for the current top flange/wall thickness.");
        }

        if (request.ReferenceLine != TaperedReferenceLine.KeepCentroidLineStraight)
            warnings.Add("Insertion point/reference line is not assigned yet. ETABS will use its default centroid reference; stiffness tapers correctly, but displayed/fabrication geometry may not keep the flange line straight.");

        warnings.Add("This feature creates an ETABS analysis model using nonprismatic frame properties. Perform final tapered/cut member steel design checks separately.");

        string baseName = selection.BaseSectionName.Length > 0 ? selection.BaseSectionName : geometry.SectionName;
        int tipDepthMm = RoundDepthMm(tipDepth);
        string endTag = request.TipEnd == TaperedTipEnd.IEnd ? "TIPI" : "TIPJ";
        string modeTag = TaperedModeTag(request.TaperType);
        string npName = BuildTaperedSteelSectionName($"NP_{baseName}_TO_{modeTag}{tipDepthMm}_{endTag}");

        var preview = new TaperedSteelGenerationPreview
        {
            SelectedFrameCount = selection.Frames.Count,
            BaseSectionName = baseName,
            MaterialName = geometry.MaterialName,
            OriginalDepthM = geometry.DepthM,
            TipDepthM = tipDepth,
            TipEnd = request.TipEnd,
            TaperType = request.TaperType,
            ReferenceLine = request.ReferenceLine,
            PreviewLengthM = selection.LengthM > 0 ? selection.LengthM : 6.0,
            NonPrismaticSectionName = npName,
            BaseGeometry = geometry,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        if (IsClosedTaperMode(request.TaperType))
        {
            string tipSectionName = BuildTaperedSteelSectionName($"TAPER_{modeTag}_{baseName}_TIP{tipDepthMm}");
            if (request.TipEnd == TaperedTipEnd.IEnd)
            {
                preview.Stations.Add(new TaperedSteelStationPreview { Index = 1, PositionRatio = 0, DepthM = tipDepth, SectionName = tipSectionName });
                preview.Stations.Add(new TaperedSteelStationPreview { Index = 2, PositionRatio = 1, DepthM = geometry.DepthM, SectionName = baseName });
            }
            else
            {
                preview.Stations.Add(new TaperedSteelStationPreview { Index = 1, PositionRatio = 0, DepthM = geometry.DepthM, SectionName = baseName });
                preview.Stations.Add(new TaperedSteelStationPreview { Index = 2, PositionRatio = 1, DepthM = tipDepth, SectionName = tipSectionName });
            }
        }
        else
        {
            int stationCount = Math.Max(2, request.StationCount);
            for (int index = 0; index < stationCount; index++)
            {
                double ratio = index / (double)(stationCount - 1);
                double depth = request.TipEnd == TaperedTipEnd.IEnd
                    ? tipDepth + (geometry.DepthM - tipDepth) * ratio
                    : geometry.DepthM + (tipDepth - geometry.DepthM) * ratio;

                string stationName = BuildTaperedSteelSectionName($"TAPER_{modeTag}_{baseName}_D{RoundDepthMm(depth)}");
                preview.Stations.Add(new TaperedSteelStationPreview
                {
                    Index = index + 1,
                    PositionRatio = ratio,
                    DepthM = depth,
                    SectionName = stationName
                });
            }
        }

        return preview;
    }

    private static void ValidateTaperedSteelMaterialExists(ETABSv1.cSapModel sapModel, string materialName, List<string> warnings)
    {
        string normalizedMaterialName = (materialName ?? "").Trim();
        if (normalizedMaterialName.Length == 0)
            throw new InvalidOperationException("Base section material name is missing.");

        List<string> materialNames = GetMaterialNames(sapModel, warnings);
        if (materialNames.Count > 0 && !materialNames.Contains(normalizedMaterialName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Material '{normalizedMaterialName}' does not exist in the connected ETABS model.");
    }

    private static void ValidateTaperedSteelModeCompatibility(TaperedSteelSectionGeometry geometry, TaperedSectionType taperType)
    {
        if (geometry.SectionKind == TaperedBaseSectionKind.ISection &&
            taperType is not (TaperedSectionType.ISection or TaperedSectionType.TSection))
        {
            throw new InvalidOperationException("The selected base section is Steel I. Use a tapered I-section or tapered T-section bottom-removed option.");
        }

        if (geometry.SectionKind == TaperedBaseSectionKind.TubeSection &&
            taperType is not (TaperedSectionType.TubeSection or TaperedSectionType.USection))
        {
            throw new InvalidOperationException("The selected base section is Steel Tube/Box. Use a tapered tube/box or tapered U-section bottom-removed option.");
        }
    }

    private static bool IsClosedTaperMode(TaperedSectionType taperType)
    {
        return taperType is TaperedSectionType.ISection or TaperedSectionType.TubeSection;
    }

    private static string TaperedModeTag(TaperedSectionType taperType)
    {
        return taperType switch
        {
            TaperedSectionType.TSection => "T",
            TaperedSectionType.TubeSection => "BOX",
            TaperedSectionType.USection => "U",
            _ => "I"
        };
    }

    private static void CreateTaperedSteelStationSections(
        ETABSv1.cSapModel sapModel,
        TaperedSteelApplyRequest request,
        TaperedSteelGenerationPreview preview,
        List<string> createdOrReused,
        List<string> warnings)
    {
        TaperedSteelSectionGeometry geometry = request.Selection.BaseGeometry;

        if (request.TaperType == TaperedSectionType.ISection)
        {
            TaperedSteelStationPreview tipStation = preview.Stations
                .First(station => Math.Abs(station.DepthM - request.TipDepthM) < 0.0000001);
            CreateTaperedSteelISection(sapModel, tipStation.SectionName, geometry, request.TipDepthM);
            createdOrReused.Add(tipStation.SectionName);
            return;
        }

        if (request.TaperType == TaperedSectionType.TubeSection)
        {
            TaperedSteelStationPreview tipStation = preview.Stations
                .First(station => Math.Abs(station.DepthM - request.TipDepthM) < 0.0000001);
            CreateTaperedSteelTubeSection(sapModel, tipStation.SectionName, geometry, request.TipDepthM);
            createdOrReused.Add(tipStation.SectionName);
            return;
        }

        foreach (TaperedSteelStationPreview station in preview.Stations)
        {
            if (request.TaperType == TaperedSectionType.USection)
                CreateTaperedSteelUSection(sapModel, station.SectionName, geometry, station.DepthM);
            else
                CreateTaperedSteelTeeSection(sapModel, station.SectionName, geometry, station.DepthM, warnings);

            createdOrReused.Add(station.SectionName);
        }
    }

    private static void CreateTaperedSteelISection(
        ETABSv1.cSapModel sapModel,
        string sectionName,
        TaperedSteelSectionGeometry geometry,
        double depth)
    {
        int ret = sapModel.PropFrame.SetISection(
            sectionName,
            geometry.MaterialName,
            depth,
            geometry.TopFlangeWidthM,
            geometry.TopFlangeThicknessM,
            geometry.WebThicknessM,
            geometry.BottomFlangeWidthM,
            geometry.BottomFlangeThicknessM,
            -1,
            "Generated tapered steel I-section tip. Analysis property; verify steel design separately.",
            "");

        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create tapered I-section '{sectionName}'. Return code: {ret}.");
    }

    private static void CreateTaperedSteelTubeSection(
        ETABSv1.cSapModel sapModel,
        string sectionName,
        TaperedSteelSectionGeometry geometry,
        double depth)
    {
        int ret = sapModel.PropFrame.SetTube(
            sectionName,
            geometry.MaterialName,
            depth,
            geometry.TopFlangeWidthM,
            geometry.TopFlangeThicknessM,
            geometry.WebThicknessM,
            -1,
            "Generated tapered steel tube/box tip. Analysis property; verify steel design separately.",
            "");

        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create tapered tube/box section '{sectionName}'. Return code: {ret}.");
    }

    private static void CreateTaperedSteelTeeSection(
        ETABSv1.cSapModel sapModel,
        string sectionName,
        TaperedSteelSectionGeometry geometry,
        double depth,
        List<string> warnings)
    {
        int ret = sapModel.PropFrame.SetSteelTee(
            sectionName,
            geometry.MaterialName,
            depth,
            geometry.TopFlangeWidthM,
            geometry.TopFlangeThicknessM,
            geometry.WebThicknessM,
            0,
            false,
            -1,
            "Generated tapered steel T-section station. Analysis property; verify steel design separately.",
            "");

        if (ret == 0)
            return;

        ret = sapModel.PropFrame.SetTee(
            sectionName,
            geometry.MaterialName,
            depth,
            geometry.TopFlangeWidthM,
            geometry.TopFlangeThicknessM,
            geometry.WebThicknessM,
            -1,
            "Generated tapered tee station. Analysis property; verify steel design separately.",
            "");

        if (ret == 0)
            return;

        warnings.Add($"ETABS SetSteelTee/SetTee failed for '{sectionName}'. Falling back to calculated general section properties.");
        TaperedTSectionProperties props = CalculateTaperedTSectionProperties(
            depth,
            geometry.TopFlangeWidthM,
            geometry.TopFlangeThicknessM,
            geometry.WebThicknessM);

        ret = sapModel.PropFrame.SetGeneral(
            sectionName,
            geometry.MaterialName,
            depth,
            geometry.TopFlangeWidthM,
            props.Area,
            props.As2,
            props.As3,
            props.Torsion,
            props.I22,
            props.I33,
            props.S22,
            props.S33,
            props.Z22,
            props.Z33,
            props.R22,
            props.R33,
            -1,
            "Generated tapered T-section as general section. Torsion/plastic values are approximate for analysis only.",
            "");

        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create tapered T/general section '{sectionName}'. Return code: {ret}.");
    }

    private static void CreateTaperedSteelUSection(
        ETABSv1.cSapModel sapModel,
        string sectionName,
        TaperedSteelSectionGeometry geometry,
        double depth)
    {
        TaperedTSectionProperties props = CalculateTaperedUSectionProperties(
            depth,
            geometry.TopFlangeWidthM,
            geometry.TopFlangeThicknessM,
            geometry.WebThicknessM);

        int ret = sapModel.PropFrame.SetGeneral(
            sectionName,
            geometry.MaterialName,
            depth,
            geometry.TopFlangeWidthM,
            props.Area,
            props.As2,
            props.As3,
            props.Torsion,
            props.I22,
            props.I33,
            props.S22,
            props.S33,
            props.Z22,
            props.Z33,
            props.R22,
            props.R33,
            -1,
            "Generated tapered U-section from tube/box with bottom wall removed. Torsion/plastic values are approximate for analysis only.",
            "");

        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create tapered U/general section '{sectionName}'. Return code: {ret}.");
    }

    private static void CreateTaperedSteelNonPrismaticSection(
        ETABSv1.cSapModel sapModel,
        TaperedSteelGenerationPreview preview,
        List<string> createdOrReused)
    {
        int numberItems = preview.Stations.Count - 1;
        if (numberItems <= 0)
            throw new InvalidOperationException("At least two section stations are required to create a nonprismatic frame property.");

        string[] startSections = new string[numberItems];
        string[] endSections = new string[numberItems];
        double[] lengths = new double[numberItems];
        int[] lengthTypes = new int[numberItems];
        int[] ei33 = new int[numberItems];
        int[] ei22 = new int[numberItems];

        for (int index = 0; index < numberItems; index++)
        {
            TaperedSteelStationPreview start = preview.Stations[index];
            TaperedSteelStationPreview end = preview.Stations[index + 1];
            startSections[index] = start.SectionName;
            endSections[index] = end.SectionName;
            lengths[index] = Math.Max(0.000001, end.PositionRatio - start.PositionRatio);
            lengthTypes[index] = 1;
            ei33[index] = 1;
            ei22[index] = 1;
        }

        int ret = sapModel.PropFrame.SetNonPrismatic(
            preview.NonPrismaticSectionName,
            numberItems,
            ref startSections,
            ref endSections,
            ref lengths,
            ref lengthTypes,
            ref ei33,
            ref ei22,
            -1,
            "Generated tapered steel nonprismatic frame property. Analysis property; verify steel design separately.",
            "");

        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create nonprismatic section '{preview.NonPrismaticSectionName}'. Return code: {ret}.");

        createdOrReused.Add(preview.NonPrismaticSectionName);
    }

    private static TaperedTSectionProperties CalculateTaperedTSectionProperties(double depth, double flangeWidth, double flangeThickness, double webThickness)
    {
        depth = EnsurePositive(depth, "T-section depth");
        flangeWidth = EnsurePositive(flangeWidth, "T-section flange width");
        flangeThickness = EnsurePositive(flangeThickness, "T-section flange thickness");
        webThickness = EnsurePositive(webThickness, "T-section web thickness");
        if (depth <= flangeThickness)
            throw new InvalidOperationException("T-section depth must be greater than top flange thickness.");

        double webDepth = depth - flangeThickness;
        double flangeArea = flangeWidth * flangeThickness;
        double webArea = webThickness * webDepth;
        double area = flangeArea + webArea;
        double yFlange = flangeThickness / 2.0;
        double yWeb = flangeThickness + webDepth / 2.0;
        double yBar = (flangeArea * yFlange + webArea * yWeb) / area;
        double i22 = flangeWidth * Math.Pow(flangeThickness, 3) / 12.0 +
            flangeArea * Math.Pow(yBar - yFlange, 2) +
            webThickness * Math.Pow(webDepth, 3) / 12.0 +
            webArea * Math.Pow(yWeb - yBar, 2);
        double i33 = flangeThickness * Math.Pow(flangeWidth, 3) / 12.0 +
            webDepth * Math.Pow(webThickness, 3) / 12.0;
        double s22Top = i22 / yBar;
        double s22Bottom = i22 / (depth - yBar);
        double s22 = Math.Min(s22Top, s22Bottom);
        double s33 = i33 / (flangeWidth / 2.0);
        double torsion = (flangeWidth * Math.Pow(flangeThickness, 3) + webDepth * Math.Pow(webThickness, 3)) / 3.0;

        return new TaperedTSectionProperties(
            area,
            webThickness * webDepth,
            area,
            torsion,
            i22,
            i33,
            s22,
            s33,
            s22,
            s33,
            Math.Sqrt(i22 / area),
            Math.Sqrt(i33 / area));
    }

    private static TaperedTSectionProperties CalculateTaperedUSectionProperties(double depth, double width, double topWallThickness, double sideWallThickness)
    {
        depth = EnsurePositive(depth, "U-section depth");
        width = EnsurePositive(width, "U-section width");
        topWallThickness = EnsurePositive(topWallThickness, "U-section top wall thickness");
        sideWallThickness = EnsurePositive(sideWallThickness, "U-section side wall thickness");
        if (depth <= topWallThickness)
            throw new InvalidOperationException("U-section depth must be greater than top wall thickness.");
        if (width <= 2.0 * sideWallThickness)
            throw new InvalidOperationException("U-section width must be greater than twice the side wall thickness.");

        double webDepth = depth - topWallThickness;
        double topArea = width * topWallThickness;
        double sideArea = sideWallThickness * webDepth;
        double area = topArea + 2.0 * sideArea;
        double yTop = topWallThickness / 2.0;
        double ySide = topWallThickness + webDepth / 2.0;
        double yBar = (topArea * yTop + 2.0 * sideArea * ySide) / area;
        double sideOffset = width / 2.0 - sideWallThickness / 2.0;

        double i22 = width * Math.Pow(topWallThickness, 3) / 12.0 +
            topArea * Math.Pow(yBar - yTop, 2) +
            2.0 * (sideWallThickness * Math.Pow(webDepth, 3) / 12.0 + sideArea * Math.Pow(ySide - yBar, 2));
        double i33 = topWallThickness * Math.Pow(width, 3) / 12.0 +
            2.0 * (webDepth * Math.Pow(sideWallThickness, 3) / 12.0 + sideArea * Math.Pow(sideOffset, 2));
        double s22Top = i22 / yBar;
        double s22Bottom = i22 / (depth - yBar);
        double s22 = Math.Min(s22Top, s22Bottom);
        double s33 = i33 / (width / 2.0);
        double torsion = (width * Math.Pow(topWallThickness, 3) + 2.0 * webDepth * Math.Pow(sideWallThickness, 3)) / 3.0;

        return new TaperedTSectionProperties(
            area,
            2.0 * sideWallThickness * webDepth,
            area,
            torsion,
            i22,
            i33,
            s22,
            s33,
            s22,
            s33,
            Math.Sqrt(i22 / area),
            Math.Sqrt(i33 / area));
    }

    private static string BuildTaperedSteelSectionName(string rawName)
    {
        return EtabsNameUtility.BuildSafeName("", rawName, 60);
    }

    private static int RoundDepthMm(double depthM)
    {
        return Math.Max(1, (int)Math.Round(depthM * 1000.0, MidpointRounding.AwayFromZero));
    }

    private sealed record TaperedTSectionProperties(
        double Area,
        double As2,
        double As3,
        double Torsion,
        double I22,
        double I33,
        double S22,
        double S33,
        double Z22,
        double Z33,
        double R22,
        double R33);

    private static double CalculateFrameLength((double X, double Y, double Z) pointI, (double X, double Y, double Z) pointJ)
    {
        double dx = pointJ.X - pointI.X;
        double dy = pointJ.Y - pointI.Y;
        double dz = pointJ.Z - pointI.Z;
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return double.IsFinite(length) ? length : 0;
    }

    private static string ResolveSelectedInstanceId(List<EtabsInstanceInfo> instances, string? requestedInstanceId)
    {
        string selectedInstanceId = (requestedInstanceId ?? "").Trim();
        bool hasSelectedInstance = instances.Any(instance =>
            string.Equals(instance.Id, selectedInstanceId, StringComparison.OrdinalIgnoreCase));

        if (instances.Count > 0 && (selectedInstanceId.Length == 0 ||
            string.Equals(selectedInstanceId, "active", StringComparison.OrdinalIgnoreCase) ||
            !hasSelectedInstance))
        {
            return instances[0].Id;
        }

        return selectedInstanceId.Length == 0 ? "active" : selectedInstanceId;
    }

    private static string BuildExportSuffix()
    {
        return DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
    }

    private static string BuildExportGroupName(string groupName, string exportSuffix)
    {
        string safeGroupName = EtabsNameUtility.BuildSafeName("", groupName, 60);
        if (string.IsNullOrWhiteSpace(exportSuffix))
            return safeGroupName;

        return EtabsNameUtility.BuildSafeName("", $"{safeGroupName}_{exportSuffix}", 60);
    }

    private static string BuildExportObjectName(string objectName, string exportSuffix)
    {
        string safeObjectName = EtabsNameUtility.BuildSafeName("", objectName, 60);
        if (string.IsNullOrWhiteSpace(exportSuffix))
            return safeObjectName;

        return EtabsNameUtility.BuildSafeName("", $"{safeObjectName}_{exportSuffix}", 60);
    }

    private static List<string> GetFrameSectionNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ETABSv1.eFramePropType propType in Enum.GetValues(typeof(ETABSv1.eFramePropType)).Cast<ETABSv1.eFramePropType>())
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
                // Some ETABS versions return errors for property families not present in a model.
            }
        }

        if (names.Count == 0)
            warnings.Add("ETABS frame section list could not be loaded or the connected model has no frame sections.");

        return names.ToList();
    }

    private static List<string> GetAreaPropertyNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.PropArea.GetNameList(ref numberNames, ref names, 0) == 0)
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
            warnings.Add("ETABS shell/area property list could not be loaded: " + ex.Message);
        }

        warnings.Add("ETABS shell/area property list could not be loaded or the connected model has no shell properties.");
        return [];
    }

    private static List<string> GetMaterialNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ETABSv1.eMatType materialType in Enum.GetValues(typeof(ETABSv1.eMatType)).Cast<ETABSv1.eMatType>())
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
                // Missing material families are expected in many ETABS models.
            }
        }

        if (names.Count == 0)
            warnings.Add("ETABS material list could not be loaded or the connected model has no materials.");

        return names.ToList();
    }

    private static List<EtabsMaterialPropertyRow> GetMaterialPropertyRows(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetMaterialNames(sapModel, warnings)
            .Select(name =>
            {
                ETABSv1.eMatType materialType = ETABSv1.eMatType.NoDesign;
                int color = 0;
                string notes = "";
                string guid = "";

                try
                {
                    int ret = sapModel.PropMaterial.GetMaterial(name, ref materialType, ref color, ref notes, ref guid);
                    if (ret != 0)
                    {
                        int subType = 0;
                        ret = sapModel.PropMaterial.GetTypeOAPI(name, ref materialType, ref subType);
                    }

                    if (ret != 0)
                        warnings.Add($"ETABS could not read material type for '{name}'. Return code: {ret}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS material type read failed for '{name}': {ex.Message}");
                }

                double elasticModulus = 0;
                double poisson = 0;
                double thermal = 0;
                double shearModulus = 0;
                try
                {
                    int ret = sapModel.PropMaterial.GetMPIsotropic(name, ref elasticModulus, ref poisson, ref thermal, ref shearModulus, 0);
                    if (ret != 0)
                        warnings.Add($"ETABS could not read isotropic material properties for '{name}'. Return code: {ret}.");
                }
                catch
                {
                    // Some material records may not be isotropic.
                }

                double unitWeight = 0;
                double unitMass = 0;
                try
                {
                    int ret = sapModel.PropMaterial.GetWeightAndMass(name, ref unitWeight, ref unitMass, 0);
                    if (ret != 0)
                        warnings.Add($"ETABS could not read unit weight for '{name}'. Return code: {ret}.");
                }
                catch
                {
                    // Weight is optional display data.
                }

                return new EtabsMaterialPropertyRow
                {
                    Name = name,
                    MaterialType = FormatMaterialType(materialType),
                    ElasticModulusMpa = KnPerM2ToMpa(elasticModulus),
                    PoissonRatio = poisson,
                    UnitWeightKnPerM3 = unitWeight,
                    DesignSummary = GetMaterialDesignSummary(sapModel, name, materialType)
                };
            })
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<EtabsFramePropertyRow> GetFramePropertyRows(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetFrameSectionNames(sapModel, warnings)
            .Select(name =>
            {
                ETABSv1.eFramePropType propType = ETABSv1.eFramePropType.General;
                string materialName = "";

                try
                {
                    int typeRet = sapModel.PropFrame.GetTypeOAPI(name, ref propType);
                    if (typeRet != 0)
                        warnings.Add($"ETABS could not read frame section shape for '{name}'. Return code: {typeRet}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS frame section shape read failed for '{name}': {ex.Message}");
                }

                try
                {
                    int matRet = sapModel.PropFrame.GetMaterial(name, ref materialName);
                    if (matRet != 0)
                        warnings.Add($"ETABS could not read frame section material for '{name}'. Return code: {matRet}.");
                }
                catch
                {
                    // Imported or auto-select properties may not expose one simple material name.
                }

                (double Depth, double Width, double FlangeThickness, double WebThickness) dimensions = GetFrameSectionDimensions(sapModel, name, propType);

                return new EtabsFramePropertyRow
                {
                    Name = name,
                    ShapeType = FormatFramePropType(propType),
                    MaterialName = materialName,
                    DepthMm = dimensions.Depth * 1000.0,
                    WidthMm = dimensions.Width * 1000.0,
                    FlangeThicknessMm = dimensions.FlangeThickness * 1000.0,
                    WebThicknessMm = dimensions.WebThickness * 1000.0,
                    SectionSummary = GetFrameSectionSummary(sapModel, name)
                };
            })
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<EtabsAreaPropertyRow> GetAreaPropertyRows(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetAreaPropertyNames(sapModel, warnings)
            .Select(name =>
            {
                ETABSv1.eSlabType slabType = ETABSv1.eSlabType.Slab;
                ETABSv1.eShellType shellType = ETABSv1.eShellType.ShellThin;
                string materialName = "";
                double thickness = 0;
                int color = 0;
                string notes = "";
                string guid = "";
                string areaType = "Other";

                try
                {
                    int slabRet = sapModel.PropArea.GetSlab(name, ref slabType, ref shellType, ref materialName, ref thickness, ref color, ref notes, ref guid);
                    if (slabRet == 0)
                    {
                        areaType = slabType.ToString();
                    }
                    else
                    {
                        ETABSv1.eWallPropType wallType = ETABSv1.eWallPropType.Specified;
                        int wallRet = sapModel.PropArea.GetWall(name, ref wallType, ref shellType, ref materialName, ref thickness, ref color, ref notes, ref guid);
                        if (wallRet == 0)
                            areaType = "Wall";
                        else
                            warnings.Add($"ETABS could not read slab/wall property data for '{name}'. Return codes: slab {slabRet}, wall {wallRet}.");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS slab/wall property read failed for '{name}': {ex.Message}");
                }

                return new EtabsAreaPropertyRow
                {
                    Name = name,
                    AreaType = areaType,
                    ShellType = shellType.ToString(),
                    MaterialName = materialName,
                    Thickness = thickness
                };
            })
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ModelCompareFramePropertySnapshot> GetModelCompareFrameProperties(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetFrameSectionNames(sapModel, warnings)
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

    private static List<PlateGirderShellPropertyDefinition> GetPlateGirderShellPropertyDefinitions(
        ETABSv1.cSapModel sapModel,
        List<string> warnings)
    {
        return GetAreaPropertyRows(sapModel, warnings)
            .Select(row =>
            {
                (double elasticModulusGpa, double yieldStrengthMpa) = GetPlateGirderMaterialValues(
                    sapModel,
                    row.MaterialName,
                    row.Name,
                    warnings);

                return new PlateGirderShellPropertyDefinition
                {
                    Name = row.Name,
                    AreaType = row.AreaType,
                    ShellType = row.ShellType,
                    MaterialName = row.MaterialName,
                    Thickness = row.Thickness,
                    YieldStrengthMpa = yieldStrengthMpa,
                    ElasticModulusGpa = elasticModulusGpa
                };
            })
            .ToList();
    }

    private static (double ElasticModulusGpa, double YieldStrengthMpa) GetPlateGirderMaterialValues(
        ETABSv1.cSapModel sapModel,
        string materialName,
        string shellPropertyName,
        List<string> warnings)
    {
        double elasticModulusGpa = 200.0;
        double yieldStrengthMpa = 355.0;
        string normalizedMaterial = (materialName ?? "").Trim();
        if (normalizedMaterial.Length == 0)
        {
            warnings.Add($"Plate girder shell property '{shellPropertyName}' has no material name; using E 200 GPa and Fy 355 MPa for preview checks.");
            return (elasticModulusGpa, yieldStrengthMpa);
        }

        ETABSv1.eMatType materialType = ETABSv1.eMatType.NoDesign;
        bool materialTypeRead = false;
        try
        {
            int color = 0;
            string notes = "";
            string guid = "";
            int ret = sapModel.PropMaterial.GetMaterial(normalizedMaterial, ref materialType, ref color, ref notes, ref guid);
            if (ret != 0)
            {
                int subType = 0;
                ret = sapModel.PropMaterial.GetTypeOAPI(normalizedMaterial, ref materialType, ref subType);
            }

            materialTypeRead = ret == 0;
        }
        catch
        {
            materialTypeRead = false;
        }

        try
        {
            double elasticModulus = 0;
            double poisson = 0;
            double thermal = 0;
            double shearModulus = 0;
            int ret = sapModel.PropMaterial.GetMPIsotropic(normalizedMaterial, ref elasticModulus, ref poisson, ref thermal, ref shearModulus, 0);
            if (ret == 0 && elasticModulus > 0)
                elasticModulusGpa = KnPerM2ToMpa(elasticModulus) / 1000.0;
            else
                warnings.Add($"ETABS could not read elastic modulus for material '{normalizedMaterial}' used by shell property '{shellPropertyName}'. Using 200 GPa for preview checks.");
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS elastic modulus read failed for material '{normalizedMaterial}' used by shell property '{shellPropertyName}': {ex.Message}");
        }

        try
        {
            double fy = 0;
            double fu = 0;
            double eFy = 0;
            double eFu = 0;
            int ssType = 0;
            int ssHysType = 0;
            double strainAtHardening = 0;
            double strainAtMaxStress = 0;
            double strainAtRupture = 0;
            int ret = sapModel.PropMaterial.GetOSteel(
                normalizedMaterial,
                ref fy,
                ref fu,
                ref eFy,
                ref eFu,
                ref ssType,
                ref ssHysType,
                ref strainAtHardening,
                ref strainAtMaxStress,
                ref strainAtRupture,
                0);
            if (ret == 0 && fy > 0)
                yieldStrengthMpa = KnPerM2ToMpa(fy);
            else if (!materialTypeRead || materialType == ETABSv1.eMatType.Steel)
                warnings.Add($"ETABS could not read steel Fy for material '{normalizedMaterial}' used by shell property '{shellPropertyName}'. Using 355 MPa for preview checks.");
        }
        catch (Exception ex)
        {
            if (!materialTypeRead || materialType == ETABSv1.eMatType.Steel)
                warnings.Add($"ETABS steel Fy read failed for material '{normalizedMaterial}' used by shell property '{shellPropertyName}': {ex.Message}");
        }

        return (elasticModulusGpa, yieldStrengthMpa);
    }

    private static (double Depth, double Width, double FlangeThickness, double WebThickness) GetFrameSectionDimensions(
        ETABSv1.cSapModel sapModel,
        string name,
        ETABSv1.eFramePropType propType)
    {
        try
        {
            string fileName = "";
            string materialName = "";
            int color = 0;
            string notes = "";
            string guid = "";

            switch (propType)
            {
                case ETABSv1.eFramePropType.Rectangular:
                    {
                        double depth = 0;
                        double width = 0;
                        int ret = sapModel.PropFrame.GetRectangle(name, ref fileName, ref materialName, ref depth, ref width, ref color, ref notes, ref guid);
                        return ret == 0 ? (depth, width, 0, 0) : default;
                    }
                case ETABSv1.eFramePropType.Circle:
                    {
                        double diameter = 0;
                        int ret = sapModel.PropFrame.GetCircle(name, ref fileName, ref materialName, ref diameter, ref color, ref notes, ref guid);
                        return ret == 0 ? (diameter, 0, 0, 0) : default;
                    }
                case ETABSv1.eFramePropType.I:
                    {
                        double depth = 0;
                        double topWidth = 0;
                        double topFlangeThickness = 0;
                        double webThickness = 0;
                        double bottomWidth = 0;
                        double bottomFlangeThickness = 0;
                        int ret = sapModel.PropFrame.GetISection(
                            name,
                            ref fileName,
                            ref materialName,
                            ref depth,
                            ref topWidth,
                            ref topFlangeThickness,
                            ref webThickness,
                            ref bottomWidth,
                            ref bottomFlangeThickness,
                            ref color,
                            ref notes,
                            ref guid);
                        return ret == 0 ? (depth, topWidth, topFlangeThickness, webThickness) : default;
                    }
                case ETABSv1.eFramePropType.Channel:
                    {
                        double depth = 0;
                        double width = 0;
                        double flangeThickness = 0;
                        double webThickness = 0;
                        int ret = sapModel.PropFrame.GetChannel(name, ref fileName, ref materialName, ref depth, ref width, ref flangeThickness, ref webThickness, ref color, ref notes, ref guid);
                        return ret == 0 ? (depth, width, flangeThickness, webThickness) : default;
                    }
                case ETABSv1.eFramePropType.Box:
                    {
                        double depth = 0;
                        double width = 0;
                        double flangeThickness = 0;
                        double webThickness = 0;
                        int ret = sapModel.PropFrame.GetTube(name, ref fileName, ref materialName, ref depth, ref width, ref flangeThickness, ref webThickness, ref color, ref notes, ref guid);
                        return ret == 0 ? (depth, width, flangeThickness, webThickness) : default;
                    }
                case ETABSv1.eFramePropType.Pipe:
                    {
                        double diameter = 0;
                        double wallThickness = 0;
                        int ret = sapModel.PropFrame.GetPipe(name, ref fileName, ref materialName, ref diameter, ref wallThickness, ref color, ref notes, ref guid);
                        return ret == 0 ? (diameter, 0, wallThickness, 0) : default;
                    }
                default:
                    return default;
            }
        }
        catch
        {
            return default;
        }
    }

    private static string GetMaterialDesignSummary(ETABSv1.cSapModel sapModel, string materialName, ETABSv1.eMatType materialType)
    {
        try
        {
            if (materialType == ETABSv1.eMatType.Concrete)
            {
                double fc = 0;
                bool isLightweight = false;
                double fcsFactor = 0;
                int ssType = 0;
                int ssHysType = 0;
                double strainAtFc = 0;
                double strainUltimate = 0;
                double frictionAngle = 0;
                double dilatationalAngle = 0;
                int ret = sapModel.PropMaterial.GetOConcrete(materialName, ref fc, ref isLightweight, ref fcsFactor, ref ssType, ref ssHysType, ref strainAtFc, ref strainUltimate, ref frictionAngle, ref dilatationalAngle, 0);
                return ret == 0 ? $"f'c {KnPerM2ToMpa(fc):0.###} MPa" : "";
            }

            if (materialType == ETABSv1.eMatType.Steel)
            {
                double fy = 0;
                double fu = 0;
                double eFy = 0;
                double eFu = 0;
                int ssType = 0;
                int ssHysType = 0;
                double strainAtHardening = 0;
                double strainAtMaxStress = 0;
                double strainAtRupture = 0;
                int ret = sapModel.PropMaterial.GetOSteel(materialName, ref fy, ref fu, ref eFy, ref eFu, ref ssType, ref ssHysType, ref strainAtHardening, ref strainAtMaxStress, ref strainAtRupture, 0);
                return ret == 0 ? $"Fy {KnPerM2ToMpa(fy):0.###} MPa / Fu {KnPerM2ToMpa(fu):0.###} MPa" : "";
            }
        }
        catch
        {
            // Design properties are optional display data.
        }

        return "";
    }

    private static string GetFrameSectionSummary(ETABSv1.cSapModel sapModel, string name)
    {
        try
        {
            double area = 0;
            double as2 = 0;
            double as3 = 0;
            double torsion = 0;
            double i22 = 0;
            double i33 = 0;
            double s22 = 0;
            double s33 = 0;
            double z22 = 0;
            double z33 = 0;
            double r22 = 0;
            double r33 = 0;
            int ret = sapModel.PropFrame.GetSectProps(name, ref area, ref as2, ref as3, ref torsion, ref i22, ref i33, ref s22, ref s33, ref z22, ref z33, ref r22, ref r33);
            return ret == 0 ? $"A {area:0.####} m2, I22 {i22:0.####}, I33 {i33:0.####}" : "";
        }
        catch
        {
            return "";
        }
    }

    private static ETABSv1.eMatType ParseMaterialType(string? value)
    {
        string normalized = NormalizeEtabsLabel(value);
        foreach (ETABSv1.eMatType materialType in Enum.GetValues(typeof(ETABSv1.eMatType)).Cast<ETABSv1.eMatType>())
        {
            if (string.Equals(NormalizeEtabsLabel(materialType.ToString()), normalized, StringComparison.OrdinalIgnoreCase))
                return materialType;
        }

        return string.Equals(normalized, "Concrete", StringComparison.OrdinalIgnoreCase)
            ? ETABSv1.eMatType.Concrete
            : ETABSv1.eMatType.Steel;
    }

    private static string FormatMaterialType(ETABSv1.eMatType materialType)
    {
        return materialType.ToString();
    }

    private static ETABSv1.eFramePropType ParseFramePropType(string? value)
    {
        string normalized = NormalizeEtabsLabel(value);
        return normalized switch
        {
            "SteelI" or "I" or "ISection" => ETABSv1.eFramePropType.I,
            "SteelChannel" or "Channel" => ETABSv1.eFramePropType.Channel,
            "SteelT" or "Tee" or "T" => ETABSv1.eFramePropType.T,
            "SteelAngle" or "Angle" => ETABSv1.eFramePropType.Angle,
            "SteelTube" or "Tube" or "Box" => ETABSv1.eFramePropType.Box,
            "SteelPipe" or "Pipe" => ETABSv1.eFramePropType.Pipe,
            "ConcreteRectangular" or "Rectangular" or "Rectangle" => ETABSv1.eFramePropType.Rectangular,
            "ConcreteCircular" or "Circle" or "Circular" => ETABSv1.eFramePropType.Circle,
            _ => ETABSv1.eFramePropType.I
        };
    }

    private static string ResolveSteelPropertyDatabaseFile(string? databaseFile)
    {
        string raw = (databaseFile ?? "").Trim();
        if (raw.Length == 0)
            return "";

        string normalized = NormalizeEtabsLabel(raw);
        string baseName = normalized switch
        {
            "BSSHAPE2006" or "BSSHAPES2006" => "BSShapes2006",
            "BSSHAPE" or "BSSHAPES" => "BSShapes",
            _ => Path.GetFileNameWithoutExtension(raw)
        };

        var candidates = new List<string>();
        string? extension = Path.GetExtension(raw);
        string appBaseDirectory = AppContext.BaseDirectory;
        string currentDirectory = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(appBaseDirectory, "PropertyLibraries", baseName + ".xml"));
        candidates.Add(Path.Combine(currentDirectory, "PropertyLibraries", baseName + ".xml"));
        candidates.Add(Path.Combine(appBaseDirectory, "PropertyLibraries", raw));
        candidates.Add(Path.Combine(currentDirectory, "PropertyLibraries", raw));

        if (extension?.Length > 0)
            candidates.Add(raw);
        else
        {
            candidates.Add(raw);
            candidates.Add(baseName + ".xml");
            candidates.Add(baseName + ".pro");
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            string csiRoot = Path.Combine(programFiles, "Computers and Structures");
            if (Directory.Exists(csiRoot))
            {
                string[] preferredProductFolders =
                [
                    "ETABS 22",
                    "ETABS 21",
                    "ETABS 20",
                    "ETABS 19",
                    "SAP2000 25",
                    "CSiBridge 25",
                    "SAFE 22"
                ];

                foreach (string productFolder in preferredProductFolders)
                {
                    string productPath = Path.Combine(csiRoot, productFolder);
                    if (!Directory.Exists(productPath))
                        continue;

                    candidates.Add(Path.Combine(productPath, "Property Libraries", baseName + ".xml"));
                    candidates.Add(Path.Combine(productPath, "Property Libraries", "Sections", baseName + ".xml"));
                    candidates.Add(Path.Combine(productPath, baseName + ".pro"));
                    candidates.Add(Path.Combine(productPath, "Property Libraries Old", baseName + ".xml"));
                }

                try
                {
                    foreach (string foundPath in Directory.EnumerateFiles(csiRoot, baseName + ".*", SearchOption.AllDirectories))
                        candidates.Add(foundPath);
                }
                catch
                {
                    // Program Files search is only a convenience fallback.
                }
            }
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists) ?? raw;
    }

    private static string FormatFramePropType(ETABSv1.eFramePropType propType)
    {
        return propType switch
        {
            ETABSv1.eFramePropType.Rectangular => "Concrete Rectangular",
            ETABSv1.eFramePropType.Circle => "Concrete Circular",
            ETABSv1.eFramePropType.I => "Steel I",
            ETABSv1.eFramePropType.Channel => "Steel Channel",
            ETABSv1.eFramePropType.Box => "Steel Tube",
            ETABSv1.eFramePropType.Pipe => "Steel Pipe",
            _ => propType.ToString()
        };
    }

    private static ETABSv1.eShellType ParseShellType(string? value)
    {
        string normalized = NormalizeEtabsLabel(value);
        foreach (ETABSv1.eShellType shellType in Enum.GetValues(typeof(ETABSv1.eShellType)).Cast<ETABSv1.eShellType>())
        {
            if (string.Equals(NormalizeEtabsLabel(shellType.ToString()), normalized, StringComparison.OrdinalIgnoreCase))
                return shellType;
        }

        return ETABSv1.eShellType.ShellThin;
    }

    private static ETABSv1.eSlabType ParseSlabType(string? value)
    {
        string normalized = NormalizeEtabsLabel(value);
        foreach (ETABSv1.eSlabType slabType in Enum.GetValues(typeof(ETABSv1.eSlabType)).Cast<ETABSv1.eSlabType>())
        {
            if (string.Equals(NormalizeEtabsLabel(slabType.ToString()), normalized, StringComparison.OrdinalIgnoreCase))
                return slabType;
        }

        return ETABSv1.eSlabType.Slab;
    }

    private static double MpaToKnPerM2(double value)
    {
        return value * 1000.0;
    }

    private static double KnPerM2ToMpa(double value)
    {
        return value / 1000.0;
    }

    private static double EnsurePositive(double value, string fieldName)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new InvalidOperationException($"{fieldName} must be greater than zero.");

        return value;
    }

    private static List<string> GetLoadPatternNames(ETABSv1.cSapModel sapModel, List<string> warnings)
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
            warnings.Add("ETABS load pattern list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<string> GetComboNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.RespCombo.GetNameList(ref numberNames, ref names) == 0)
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
            warnings.Add("ETABS load combination list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<EtabsLoadPatternRow> GetLoadPatternRows(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        var rows = new List<EtabsLoadPatternRow>();

        foreach (string name in GetLoadPatternNames(sapModel, warnings))
        {
            ETABSv1.eLoadPatternType loadType = ETABSv1.eLoadPatternType.Other;
            double selfWeightMultiplier = 0;

            try
            {
                int typeRet = sapModel.LoadPatterns.GetLoadType(name, ref loadType);
                if (typeRet != 0)
                    warnings.Add($"ETABS could not read load pattern type for '{name}'. Return code: {typeRet}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS load pattern type read failed for '{name}': {ex.Message}");
            }

            try
            {
                int swRet = sapModel.LoadPatterns.GetSelfWTMultiplier(name, ref selfWeightMultiplier);
                if (swRet != 0)
                    warnings.Add($"ETABS could not read self-weight multiplier for load pattern '{name}'. Return code: {swRet}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS self-weight multiplier read failed for '{name}': {ex.Message}");
            }

            var row = new EtabsLoadPatternRow
            {
                Name = name,
                PatternType = FormatLoadPatternType(loadType),
                SelfWeightMultiplier = selfWeightMultiplier
            };
            row.AcceptChanges();
            rows.Add(row);
        }

        return rows;
    }

    private static List<EtabsLoadCaseRow> GetLoadCaseRows(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        var rowsByName = new Dictionary<string, EtabsLoadCaseRow>(StringComparer.OrdinalIgnoreCase);

        foreach (ETABSv1.eLoadCaseType caseFamily in Enum.GetValues(typeof(ETABSv1.eLoadCaseType)).Cast<ETABSv1.eLoadCaseType>())
        {
            int numberNames = 0;
            string[] names = [];
            try
            {
                int ret = sapModel.LoadCases.GetNameList(ref numberNames, ref names, caseFamily);
                if (ret != 0)
                    continue;

                foreach (string name in names.Take(Math.Min(numberNames, names.Length)))
                {
                    string caseName = (name ?? "").Trim();
                    if (caseName.Length == 0 || rowsByName.ContainsKey(caseName))
                        continue;

                    ETABSv1.eLoadCaseType caseType = caseFamily;
                    int subType = 0;
                    try
                    {
                        int typeRet = sapModel.LoadCases.GetTypeOAPI(caseName, ref caseType, ref subType);
                        if (typeRet != 0)
                            warnings.Add($"ETABS could not read load case type for '{caseName}'. Return code: {typeRet}.");
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"ETABS load case type read failed for '{caseName}': {ex.Message}");
                    }

                    List<StaticLoadCaseItemRow> items = caseType == ETABSv1.eLoadCaseType.LinearStatic
                        ? GetStaticLoadCaseItems(sapModel, caseName, warnings)
                        : [];

                    var row = new EtabsLoadCaseRow
                    {
                        Name = caseName,
                        CaseType = FormatLoadCaseType(caseType),
                        SubType = subType == 0
                            ? ""
                            : subType.ToString(CultureInfo.InvariantCulture),
                        IsEditable = caseType == ETABSv1.eLoadCaseType.LinearStatic,
                        Items = new ObservableCollection<StaticLoadCaseItemRow>(items),
                        ItemsSummary = BuildStaticCaseItemsSummary(items)
                    };
                    row.AcceptChanges();
                    rowsByName[caseName] = row;
                }
            }
            catch
            {
                // Many ETABS models do not have every analysis case family.
            }
        }

        if (rowsByName.Count == 0)
            warnings.Add("ETABS load case list could not be loaded or the connected model has no load cases.");

        return rowsByName.Values
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<EtabsLoadCombinationRow> GetLoadCombinationRows(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        IReadOnlyList<EtabsLoadCaseRow> loadCases)
    {
        var rows = new List<EtabsLoadCombinationRow>();
        List<string> comboNames = GetComboNames(sapModel, warnings);
        HashSet<string> validLoadCaseNames = loadCases
            .Select(loadCase => loadCase.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> validComboNames = comboNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool canValidateLoadCaseNames = validLoadCaseNames.Count > 0;

        foreach (string name in comboNames)
        {
            int comboType = 0;
            try
            {
                int typeRet = sapModel.RespCombo.GetTypeOAPI(name, ref comboType);
                if (typeRet != 0)
                    warnings.Add($"ETABS could not read response combination type for '{name}'. Return code: {typeRet}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS response combination type read failed for '{name}': {ex.Message}");
            }

            var items = new List<EtabsComboItemRow>();
            try
            {
                int numberItems = 0;
                ETABSv1.eCNameType[] sourceTypes = [];
                string[] sourceNames = [];
                double[] factors = [];
                int ret = sapModel.RespCombo.GetCaseList(name, ref numberItems, ref sourceTypes, ref sourceNames, ref factors);
                if (ret == 0)
                {
                    int count = Math.Min(numberItems, Math.Min(sourceTypes.Length, Math.Min(sourceNames.Length, factors.Length)));
                    for (int index = 0; index < count; index++)
                    {
                        string sourceName = (sourceNames[index] ?? "").Trim();
                        if (sourceName.Length == 0)
                            continue;

                        if (!IsCurrentComboSource(
                            name,
                            sourceTypes[index],
                            sourceName,
                            validLoadCaseNames,
                            validComboNames,
                            canValidateLoadCaseNames,
                            warnings))
                        {
                            continue;
                        }

                        items.Add(new EtabsComboItemRow
                        {
                            SourceType = FormatComboSourceType(sourceTypes[index]),
                            Name = sourceName,
                            Factor = factors[index]
                        });
                    }
                }
                else
                {
                    warnings.Add($"ETABS could not read response combination items for '{name}'. Return code: {ret}.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS response combination item read failed for '{name}': {ex.Message}");
            }

            var row = new EtabsLoadCombinationRow
            {
                Name = name,
                ComboType = FormatComboType(comboType),
                Items = new ObservableCollection<EtabsComboItemRow>(items),
                ItemsSummary = BuildComboItemsSummary(items)
            };
            row.AcceptChanges();
            rows.Add(row);
        }

        return rows;
    }

    private static bool IsCurrentComboSource(
        string comboName,
        ETABSv1.eCNameType sourceType,
        string sourceName,
        HashSet<string> validLoadCaseNames,
        HashSet<string> validComboNames,
        bool canValidateLoadCaseNames,
        List<string> warnings)
    {
        if (sourceType == ETABSv1.eCNameType.LoadCombo)
        {
            if (validComboNames.Contains(sourceName))
                return true;

            warnings.Add($"Ignored stale response combination item '{sourceName}' in '{comboName}' because that combination no longer exists in ETABS.");
            return false;
        }

        if (!canValidateLoadCaseNames || validLoadCaseNames.Contains(sourceName))
            return true;

        warnings.Add($"Ignored stale response combination item '{sourceName}' in '{comboName}' because that load case no longer exists in ETABS.");
        return false;
    }

    private static List<StaticLoadCaseItemRow> GetStaticLoadCaseItems(ETABSv1.cSapModel sapModel, string caseName, List<string> warnings)
    {
        var items = new List<StaticLoadCaseItemRow>();

        try
        {
            int numberLoads = 0;
            string[] loadTypes = [];
            string[] loadNames = [];
            double[] scaleFactors = [];
            int ret = sapModel.LoadCases.StaticLinear.GetLoads(caseName, ref numberLoads, ref loadTypes, ref loadNames, ref scaleFactors);
            if (ret != 0)
            {
                warnings.Add($"ETABS could not read static load case items for '{caseName}'. Return code: {ret}.");
                return items;
            }

            int count = Math.Min(numberLoads, Math.Min(loadTypes.Length, Math.Min(loadNames.Length, scaleFactors.Length)));
            for (int index = 0; index < count; index++)
            {
                string name = (loadNames[index] ?? "").Trim();
                if (name.Length == 0)
                    continue;

                items.Add(new StaticLoadCaseItemRow
                {
                    LoadType = string.IsNullOrWhiteSpace(loadTypes[index]) ? "Load" : loadTypes[index].Trim(),
                    Name = name,
                    ScaleFactor = scaleFactors[index]
                });
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS static load case item read failed for '{caseName}': {ex.Message}");
        }

        return items;
    }

    private static void DeleteExistingComboItems(ETABSv1.cSapModel sapModel, string comboName, List<string> warnings)
    {
        try
        {
            int numberItems = 0;
            ETABSv1.eCNameType[] sourceTypes = [];
            string[] sourceNames = [];
            double[] factors = [];
            int ret = sapModel.RespCombo.GetCaseList(comboName, ref numberItems, ref sourceTypes, ref sourceNames, ref factors);
            if (ret != 0)
                return;

            int count = Math.Min(numberItems, Math.Min(sourceTypes.Length, sourceNames.Length));
            for (int index = 0; index < count; index++)
            {
                string sourceName = (sourceNames[index] ?? "").Trim();
                if (sourceName.Length == 0)
                    continue;

                int deleteRet = sapModel.RespCombo.DeleteCase(comboName, sourceTypes[index], sourceName);
                if (deleteRet != 0)
                    warnings.Add($"ETABS could not remove existing combination item '{sourceName}' from '{comboName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Existing response combination items could not be cleared for '{comboName}': {ex.Message}");
        }
    }

    private static List<EtabsComboItemRow> GetResponseComboItems(ETABSv1.cSapModel sapModel, string comboName, List<string> warnings)
    {
        var items = new List<EtabsComboItemRow>();

        try
        {
            int numberItems = 0;
            ETABSv1.eCNameType[] sourceTypes = [];
            string[] sourceNames = [];
            double[] factors = [];
            int ret = sapModel.RespCombo.GetCaseList(comboName, ref numberItems, ref sourceTypes, ref sourceNames, ref factors);
            if (ret != 0)
            {
                warnings.Add($"ETABS could not verify response combination items for '{comboName}'. Return code: {ret}.");
                return items;
            }

            int count = Math.Min(numberItems, Math.Min(sourceTypes.Length, Math.Min(sourceNames.Length, factors.Length)));
            for (int index = 0; index < count; index++)
            {
                string sourceName = (sourceNames[index] ?? "").Trim();
                if (sourceName.Length == 0)
                    continue;

                items.Add(new EtabsComboItemRow
                {
                    SourceType = FormatComboSourceType(sourceTypes[index]),
                    Name = sourceName,
                    Factor = factors[index]
                });
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS response combination verification failed for '{comboName}': {ex.Message}");
        }

        return items;
    }

    private static bool ContainsComboItem(IEnumerable<EtabsComboItemRow> savedItems, EtabsComboItemRow requestedItem)
    {
        ETABSv1.eCNameType requestedSourceType = ParseComboSourceType(requestedItem.SourceType);
        string requestedName = (requestedItem.Name ?? "").Trim();

        return savedItems.Any(savedItem =>
            ParseComboSourceType(savedItem.SourceType) == requestedSourceType &&
            string.Equals((savedItem.Name ?? "").Trim(), requestedName, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(savedItem.Factor - requestedItem.Factor) <= 0.000001);
    }

    private static ETABSv1.eLoadPatternType ParseLoadPatternType(string? value)
    {
        string normalized = NormalizeEtabsLabel(value);
        foreach (ETABSv1.eLoadPatternType loadType in Enum.GetValues(typeof(ETABSv1.eLoadPatternType)).Cast<ETABSv1.eLoadPatternType>())
        {
            if (string.Equals(NormalizeEtabsLabel(loadType.ToString()), normalized, StringComparison.OrdinalIgnoreCase))
                return loadType;
        }

        return ETABSv1.eLoadPatternType.Other;
    }

    private static string FormatLoadPatternType(ETABSv1.eLoadPatternType loadPatternType)
    {
        return loadPatternType.ToString();
    }

    private static string FormatLoadCaseType(ETABSv1.eLoadCaseType loadCaseType)
    {
        return loadCaseType.ToString();
    }

    private static int ParseComboType(string? value)
    {
        return NormalizeEtabsLabel(value) switch
        {
            "Envelope" => 1,
            "AbsoluteAdditive" => 2,
            "SRSS" => 3,
            "RangeAdditive" => 4,
            _ => 0
        };
    }

    private static string FormatComboType(int comboType)
    {
        return comboType switch
        {
            0 => "Linear Additive",
            1 => "Envelope",
            2 => "Absolute Additive",
            3 => "SRSS",
            4 => "Range Additive",
            _ => $"Type {comboType.ToString(CultureInfo.InvariantCulture)}"
        };
    }

    private static ETABSv1.eCNameType ParseComboSourceType(string? value)
    {
        string normalized = NormalizeEtabsLabel(value);
        return normalized == "LoadCombo" ||
            normalized == "Combination" ||
            normalized == "Combo"
            ? ETABSv1.eCNameType.LoadCombo
            : ETABSv1.eCNameType.LoadCase;
    }

    private static string FormatComboSourceType(ETABSv1.eCNameType sourceType)
    {
        return sourceType == ETABSv1.eCNameType.LoadCombo ? "Combination" : "Load Case";
    }

    private static string BuildStaticCaseItemsSummary(IReadOnlyList<StaticLoadCaseItemRow> items)
    {
        if (items.Count == 0)
            return "";

        var parts = new List<string>();
        for (int index = 0; index < items.Count; index++)
        {
            StaticLoadCaseItemRow item = items[index];
            string name = string.IsNullOrWhiteSpace(item.Name) ? "(blank)" : item.Name.Trim();
            string factor = Math.Abs(item.ScaleFactor).ToString("0.###", CultureInfo.InvariantCulture);

            if (index == 0)
            {
                string sign = item.ScaleFactor < 0 ? "-" : "";
                parts.Add($"{sign}{factor} {name}");
            }
            else
            {
                string sign = item.ScaleFactor < 0 ? "-" : "+";
                parts.Add($"{sign} {factor} {name}");
            }
        }

        return string.Join(" ", parts);
    }

    private static string BuildComboItemsSummary(IReadOnlyList<EtabsComboItemRow> items)
    {
        if (items.Count == 0)
            return "";

        var parts = new List<string>();
        for (int index = 0; index < items.Count; index++)
        {
            EtabsComboItemRow item = items[index];
            string name = string.IsNullOrWhiteSpace(item.Name) ? "(blank)" : item.Name.Trim();
            string factor = Math.Abs(item.Factor).ToString("0.###", CultureInfo.InvariantCulture);
            string sourceTypeLabel = NormalizeEtabsLabel(item.SourceType);
            string sourceSuffix = sourceTypeLabel == "LoadCombo" ||
                sourceTypeLabel == "Combination" ||
                sourceTypeLabel == "Combo"
                ? " combo"
                : "";

            if (index == 0)
            {
                string sign = item.Factor < 0 ? "-" : "";
                parts.Add($"{sign}{factor} {name}{sourceSuffix}");
            }
            else
            {
                string sign = item.Factor < 0 ? "-" : "+";
                parts.Add($"{sign} {factor} {name}{sourceSuffix}");
            }
        }

        return string.Join(" ", parts);
    }

    private static string NormalizeEtabsLabel(string? value)
    {
        return new string((value ?? "")
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static List<string> GetStoryNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.Story.GetNameList(ref numberNames, ref names) == 0)
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
            warnings.Add("ETABS story list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<string> GetGroupNames(ETABSv1.cSapModel sapModel, List<string> warnings)
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
            warnings.Add("ETABS group list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static void TryDeleteFramesInGroup(ETABSv1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];

        try
        {
            int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
            if (ret != 0)
                return;

            int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
            var frames = new List<string>();
            var points = new List<string>();
            var areas = new List<string>();

            for (int index = 0; index < count; index++)
            {
                if (objectTypes[index] == EtabsSelectedFrameObjectType)
                    frames.Add(objectNames[index]);
                else if (objectTypes[index] == EtabsSelectedPointObjectType)
                    points.Add(objectNames[index]);
                else if (objectTypes[index] == EtabsSelectedAreaObjectType)
                    areas.Add(objectNames[index]);
            }

            foreach (string frame in frames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.Delete(frame, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing generated frame '{frame}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            if (frames.Count > 0)
                warnings.Add($"Removed {frames.Count} existing frame object(s) from group '{groupName}' before drawing.");

            foreach (string area in areas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.AreaObj.Delete(area, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"Existing generated shell area '{area}' in group '{groupName}' could not be deleted. Return code: {deleteRet}.");
            }

            if (areas.Count > 0)
                warnings.Add($"Removed {areas.Count} existing shell area object(s) from group '{groupName}' before drawing.");

            foreach (string point in points.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                TrySetPointRestraint(sapModel, point, BuildFreePointRestraints(), $"existing generated point '{point}'", warnings);
                TryClearPointForceLoads(sapModel, point, warnings);
            }

            if (points.Count > 0)
                warnings.Add($"Cleared restraints and point loads on {points.Distinct(StringComparer.OrdinalIgnoreCase).Count()} retained point object(s) from group '{groupName}' before drawing.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Existing generated group '{groupName}' could not be cleaned before update: {ex.Message}");
        }
    }

    private static string EnsureEtabsDrawGroup(ETABSv1.cSapModel sapModel, string? rawGroupName, List<string> warnings)
    {
        string groupName = EtabsNameUtility.BuildSafeName("", rawGroupName);
        return EnsureEtabsGroup(sapModel, groupName, warnings);
    }

    private static string EnsureEtabsGroup(ETABSv1.cSapModel sapModel, string? rawGroupName, List<string> warnings)
    {
        string groupName = (rawGroupName ?? "").Trim();
        if (groupName.Length == 0)
            throw new InvalidOperationException("ETABS group name is required.");

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
                warnings.Add($"ETABS group '{groupName}' could not be created/updated. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS group '{groupName}' could not be created/updated: {ex.Message}");
        }

        return groupName;
    }

    private static Dictionary<string, string> CreateEtabsPointsForNodes(
        ETABSv1.cSapModel sapModel,
        ParametricTrussModel model,
        string groupName,
        string exportSuffix,
        EtabsTrussDrawRequest request,
        List<string> warnings)
    {
        var nodePointNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ParametricNode node in model.Nodes)
        {
            string preferredPointName = BuildExportObjectName($"{model.TrussId}_{node.Id}", exportSuffix);
            string pointName = "";
            double x = node.X + request.OffsetX;
            double y = node.Y + request.OffsetY;
            double z = node.Z + request.OffsetZ;
            bool mergeOff = request.AddAsNew;

            try
            {
                int ret = sapModel.PointObj.AddCartesian(
                    x,
                    y,
                    z,
                    ref pointName,
                    preferredPointName,
                    "Global",
                    mergeOff,
                    0);

                if (ret != 0)
                {
                    pointName = "";
                    ret = sapModel.PointObj.AddCartesian(
                        x,
                        y,
                        z,
                        ref pointName,
                        "",
                        "Global",
                        mergeOff,
                        0);

                    if (ret == 0)
                        warnings.Add($"Node '{node.Id}' was created with an ETABS automatic point name because the preferred name '{preferredPointName}' was unavailable.");
                }

                if (ret != 0 || string.IsNullOrWhiteSpace(pointName))
                {
                    warnings.Add($"ETABS could not create point for node '{node.Id}'. Return code: {ret}.");
                    continue;
                }

                nodePointNames[node.Id] = pointName;
                TryAssignPointToEtabsGroup(sapModel, pointName, groupName, node.Id, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS point creation failed for node '{node.Id}': {ex.Message}");
            }
        }

        return nodePointNames;
    }

    private static void TryAssignFrameSection(ETABSv1.cSapModel sapModel, string frameName, string memberId, string sectionName, List<string> warnings)
    {
        try
        {
            int ret = sapModel.FrameObj.SetSection(frameName, sectionName, EtabsObjects, 0, 0);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but ETABS could not assign section '{sectionName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but section assignment failed: {ex.Message}");
        }
    }

    private static List<string> DrawParametricShells(
        ETABSv1.cSapModel sapModel,
        ParametricTrussModel model,
        string groupName,
        string exportSuffix,
        EtabsTrussDrawRequest request,
        Dictionary<string, string> nodePointNames,
        List<string> warnings)
    {
        var generatedShellNames = new List<string>();
        foreach (ParametricShell shell in model.Shells)
        {
            string propertyName = (shell.ShellPropertyName ?? "").Trim();
            if (propertyName.Length == 0)
            {
                warnings.Add($"Skipped shell '{shell.Id}': no shell property selected.");
                continue;
            }

            string[] pointNames = shell.NodeIds
                .Select(nodeId => nodePointNames.TryGetValue(nodeId, out string? pointName) ? pointName : "")
                .Where(pointName => !string.IsNullOrWhiteSpace(pointName))
                .ToArray();

            if (pointNames.Length < 3)
            {
                warnings.Add($"Skipped shell '{shell.Id}': fewer than three ETABS point objects were available.");
                continue;
            }

            string preferredAreaName = BuildExportObjectName(shell.Id, exportSuffix);
            string areaName = "";
            int numberPoints = pointNames.Length;
            try
            {
                int ret = sapModel.AreaObj.AddByPoint(
                    numberPoints,
                    ref pointNames,
                    ref areaName,
                    propertyName,
                    preferredAreaName);

                if (ret != 0)
                {
                    areaName = "";
                    ret = sapModel.AreaObj.AddByPoint(
                        numberPoints,
                        ref pointNames,
                        ref areaName,
                        propertyName,
                        "");

                    if (ret == 0)
                        warnings.Add($"Shell '{shell.Id}' was drawn with an ETABS automatic area name because the preferred name '{preferredAreaName}' was unavailable.");
                }

                if (ret != 0 || string.IsNullOrWhiteSpace(areaName))
                {
                    warnings.Add($"ETABS could not draw shell '{shell.Id}'. Return code: {ret}.");
                    continue;
                }

                TryAssignAreaToEtabsGroup(sapModel, areaName, groupName, shell.Id, warnings);
                generatedShellNames.Add(areaName);
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS shell '{shell.Id}' drawing failed: {ex.Message}");
            }
        }

        return generatedShellNames;
    }

    private static void TryAssignTrussReleases(ETABSv1.cSapModel sapModel, string frameName, string memberId, List<string> warnings)
    {
        try
        {
            bool[] startReleases = [false, false, false, false, true, true];
            bool[] endReleases = [false, false, false, false, true, true];
            double[] startSpring = [0, 0, 0, 0, 0, 0];
            double[] endSpring = [0, 0, 0, 0, 0, 0];

            int ret = sapModel.FrameObj.SetReleases(frameName, ref startReleases, ref endReleases, ref startSpring, ref endSpring, EtabsObjects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but ETABS could not assign M22/M33 end releases. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but release assignment failed: {ex.Message}");
        }
    }

    private static bool ShouldAssignFrameEndReleases(ParametricTrussModel model, ParametricMember member)
    {
        if (model.TrussType == TrussType.FishBellyTruss ||
            model.TrussType == TrussType.VariablePanelWidthTruss)
        {
            return member.ReleaseMoments;
        }

        if (member.ReleaseMoments)
            return true;

        if (model.TrussType == TrussType.SimpleFrame)
            return false;

        return !string.Equals(member.Group, ParametricMemberGroups.TopChord, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(member.Group, ParametricMemberGroups.BottomChord, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryAssignSupportRestraints(
        ETABSv1.cSapModel sapModel,
        ParametricTrussModel model,
        Dictionary<string, string> nodePointNames,
        List<string> warnings)
    {
        ClearAllGeneratedNodeRestraints(sapModel, model, nodePointNames, warnings);

        if (model.SupportNodeMode == SupportNodeMode.NoSupports)
            return;

        List<ParametricNode> supportNodes = model.Nodes
            .Where(node => node.IsSupport)
            .OrderBy(node => node.PreviewX)
            .ThenBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (supportNodes.Count == 0)
        {
            warnings.Add("No support nodes were selected; ETABS point restraints were not assigned.");
            return;
        }

        for (int index = 0; index < supportNodes.Count; index++)
        {
            ParametricNode node = supportNodes[index];
            if (!nodePointNames.TryGetValue(node.Id, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
            {
                warnings.Add($"Skipped support restraint for node '{node.Id}': ETABS point name was not found.");
                continue;
            }

            bool[] restraints = BuildPointRestraints(model.SupportRestraintType, index == 0);
            TrySetPointRestraint(sapModel, pointName, restraints, $"support node '{node.Id}'", warnings);
        }
    }

    private static void ClearAllGeneratedNodeRestraints(
        ETABSv1.cSapModel sapModel,
        ParametricTrussModel model,
        Dictionary<string, string> nodePointNames,
        List<string> warnings)
    {
        foreach (ParametricNode node in model.Nodes)
        {
            if (!nodePointNames.TryGetValue(node.Id, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
                continue;

            TrySetPointRestraint(sapModel, pointName, BuildFreePointRestraints(), $"generated node '{node.Id}'", warnings);
        }
    }

    private static bool[] BuildPointRestraints(SupportRestraintType restraintType, bool isFirstSupportNode)
    {
        return restraintType switch
        {
            SupportRestraintType.AllPinned => [true, true, true, false, false, false],
            SupportRestraintType.AllZRollers => [false, false, true, false, false, false],
            SupportRestraintType.FirstPinOthersRoller when isFirstSupportNode => [true, true, true, false, false, false],
            SupportRestraintType.FirstPinOthersRoller => [false, false, true, false, false, false],
            _ => [true, true, true, false, false, false]
        };
    }

    private static bool[] BuildFreePointRestraints()
    {
        return [false, false, false, false, false, false];
    }

    private static void TrySetPointRestraint(
        ETABSv1.cSapModel sapModel,
        string pointName,
        bool[] restraints,
        string description,
        List<string> warnings)
    {
        try
        {
            int ret = sapModel.PointObj.SetRestraint(pointName, ref restraints, EtabsObjects);
            if (ret != 0)
            {
                warnings.Add($"ETABS could not set point restraint for {description} at point '{pointName}'. Return code: {ret}.");
                return;
            }

            bool[] actualRestraints = new bool[6];
            int getRet = sapModel.PointObj.GetRestraint(pointName, ref actualRestraints);
            if (getRet == 0 && !RestraintsMatch(restraints, actualRestraints))
                warnings.Add($"ETABS point '{pointName}' did not report the expected restraints for {description}. Expected {FormatRestraints(restraints)}, got {FormatRestraints(actualRestraints)}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Point restraint assignment failed for {description} at point '{pointName}': {ex.Message}");
        }
    }

    private static bool RestraintsMatch(bool[] expected, bool[] actual)
    {
        for (int index = 0; index < 6; index++)
        {
            bool expectedValue = index < expected.Length && expected[index];
            bool actualValue = index < actual.Length && actual[index];
            if (expectedValue != actualValue)
                return false;
        }

        return true;
    }

    private static string FormatRestraints(bool[] restraints)
    {
        string[] labels = ["UX", "UY", "UZ", "RX", "RY", "RZ"];
        string text = string.Join("; ", labels.Where((_, index) => index < restraints.Length && restraints[index]));
        return text.Length == 0 ? "None" : text;
    }

    private static void TryAssignFrameToEtabsGroup(ETABSv1.cSapModel sapModel, string frameName, string groupName, string memberId, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(frameName) || string.IsNullOrWhiteSpace(groupName))
            return;

        try
        {
            int ret = sapModel.FrameObj.SetGroupAssign(frameName, groupName, false, EtabsObjects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but ETABS could not assign frame '{frameName}' to group '{groupName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but frame group assignment failed: {ex.Message}");
        }
    }

    private static void TryAssignAreaToEtabsGroup(ETABSv1.cSapModel sapModel, string areaName, string groupName, string panelId, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(areaName) || string.IsNullOrWhiteSpace(groupName))
            return;

        try
        {
            int ret = sapModel.AreaObj.SetGroupAssign(areaName, groupName, false, EtabsObjects);
            if (ret != 0)
                warnings.Add($"Shell panel '{panelId}' was drawn, but ETABS could not assign area '{areaName}' to group '{groupName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Shell panel '{panelId}' was drawn, but area group assignment failed: {ex.Message}");
        }
    }

    private static void TryCaptureAndAssignFramePoints(
        ETABSv1.cSapModel sapModel,
        string frameName,
        string groupName,
        ParametricMember member,
        Dictionary<string, string> nodePointNames,
        List<string> warnings)
    {
        try
        {
            string pointI = "";
            string pointJ = "";
            int ret = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
            if (ret != 0)
                return;

            if (!string.IsNullOrWhiteSpace(pointI))
            {
                nodePointNames[member.StartNodeId] = pointI;
                TryAssignPointToEtabsGroup(sapModel, pointI, groupName, member.Id, warnings);
            }

            if (!string.IsNullOrWhiteSpace(pointJ))
            {
                nodePointNames[member.EndNodeId] = pointJ;
                TryAssignPointToEtabsGroup(sapModel, pointJ, groupName, member.Id, warnings);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{member.Id}' was drawn, but its ETABS end points could not be read: {ex.Message}");
        }
    }

    private static void TryAssignPointToEtabsGroup(ETABSv1.cSapModel sapModel, string pointName, string groupName, string memberId, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(pointName) || string.IsNullOrWhiteSpace(groupName))
            return;

        try
        {
            int ret = sapModel.PointObj.SetGroupAssign(pointName, groupName, false, EtabsObjects);
            if (ret != 0)
                warnings.Add($"Member '{memberId}' was drawn, but ETABS could not assign point '{pointName}' to group '{groupName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Member '{memberId}' was drawn, but point group assignment failed: {ex.Message}");
        }
    }

    private static void TryApplyLoads(
        ETABSv1.cSapModel sapModel,
        ParametricTrussModel model,
        Dictionary<string, string> nodePointNames,
        List<GeneratedEtabsFrame> generatedFrames,
        List<string> warnings)
    {
        TryClearGeneratedLoads(sapModel, nodePointNames.Values, generatedFrames.Select(frame => frame.EtabsFrameName), warnings);

        if (model.Loads.Count == 0)
            return;

        warnings.Add("Generated loads were refreshed on the newly generated ETABS objects.");
        var frameNamesByMemberId = generatedFrames.ToDictionary(frame => frame.MemberId, frame => frame.EtabsFrameName, StringComparer.OrdinalIgnoreCase);

        foreach (ParametricLoad load in model.Loads)
        {
            if (load.TargetType.Equals("Node", StringComparison.OrdinalIgnoreCase))
            {
                TryApplyNodalLoad(sapModel, load, nodePointNames, warnings);
                continue;
            }

            if (load.TargetType.Equals("MemberGroup", StringComparison.OrdinalIgnoreCase))
            {
                TryApplyDistributedMemberGroupLoad(sapModel, model, load, frameNamesByMemberId, warnings);
            }
        }
    }

    private static void TryApplyNodalLoad(
        ETABSv1.cSapModel sapModel,
        ParametricLoad load,
        Dictionary<string, string> nodePointNames,
        List<string> warnings)
    {
        if (!nodePointNames.TryGetValue(load.TargetId, out string? pointName) || string.IsNullOrWhiteSpace(pointName))
        {
            warnings.Add($"Skipped load '{load.Id}': ETABS point for node '{load.TargetId}' was not found.");
            return;
        }

        double[] values = [0, 0, 0, 0, 0, 0];
        switch ((load.Direction ?? "").Trim().ToUpperInvariant())
        {
            case "GLOBALX":
                values[0] = load.Magnitude;
                break;
            case "GLOBALY":
                values[1] = load.Magnitude;
                break;
            default:
                values[2] = load.Magnitude;
                break;
        }

        try
        {
            int ret = sapModel.PointObj.SetLoadForce(pointName, load.LoadPattern, ref values, false, "Global", EtabsObjects);
            if (ret != 0)
                warnings.Add($"ETABS could not assign load '{load.Id}' to point '{pointName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS load '{load.Id}' assignment failed: {ex.Message}");
        }
    }

    private static void TryApplyDistributedMemberGroupLoad(
        ETABSv1.cSapModel sapModel,
        ParametricTrussModel model,
        ParametricLoad load,
        Dictionary<string, string> frameNamesByMemberId,
        List<string> warnings)
    {
        List<ParametricMember> targetMembers = model.Members
            .Where(member => string.Equals(member.Group, load.TargetId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targetMembers.Count == 0)
        {
            warnings.Add($"Skipped line load '{load.Id}': member group '{load.TargetId}' has no generated members.");
            return;
        }

        int direction = ToEtabsDistributedLoadDirection(load.Direction);
        foreach (ParametricMember member in targetMembers)
        {
            if (!frameNamesByMemberId.TryGetValue(member.Id, out string? frameName) || string.IsNullOrWhiteSpace(frameName))
            {
                warnings.Add($"Skipped line load '{load.Id}' on member '{member.Id}': ETABS frame name was not found.");
                continue;
            }

            try
            {
                int ret = sapModel.FrameObj.SetLoadDistributed(
                    frameName,
                    load.LoadPattern,
                    1,
                    direction,
                    0,
                    1,
                    load.Magnitude,
                    load.Magnitude,
                    "Global",
                    true,
                    false,
                    EtabsObjects);

                if (ret != 0)
                    warnings.Add($"ETABS could not assign line load '{load.Id}' to frame '{frameName}'. Return code: {ret}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS line load '{load.Id}' assignment failed on frame '{frameName}': {ex.Message}");
            }
        }
    }

    private static void TryClearGeneratedLoads(
        ETABSv1.cSapModel sapModel,
        IEnumerable<string> pointNames,
        IEnumerable<string> frameNames,
        List<string> warnings)
    {
        foreach (string pointName in pointNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryClearPointForceLoads(sapModel, pointName, warnings);
        }

        foreach (string frameName in frameNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryClearFrameDistributedLoads(sapModel, frameName, warnings);
        }
    }

    private static void TryClearPointForceLoads(ETABSv1.cSapModel sapModel, string pointName, List<string> warnings)
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

            foreach (string loadPattern in loadPatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.PointObj.DeleteLoadForce(pointName, loadPattern, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"ETABS could not clear old point load pattern '{loadPattern}' on point '{pointName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Old point loads could not be cleared on point '{pointName}': {ex.Message}");
        }
    }

    private static void TryClearAreaUniformLoads(ETABSv1.cSapModel sapModel, string areaName, List<string> warnings)
    {
        try
        {
            int numberItems = 0;
            string[] areaNames = [];
            string[] loadPatterns = [];
            string[] coordinateSystems = [];
            int[] directions = [];
            double[] values = [];

            int ret = sapModel.AreaObj.GetLoadUniform(
                areaName,
                ref numberItems,
                ref areaNames,
                ref loadPatterns,
                ref coordinateSystems,
                ref directions,
                ref values,
                EtabsObjects);

            if (ret != 0 || loadPatterns.Length == 0)
                return;

            foreach (string loadPattern in loadPatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.AreaObj.DeleteLoadUniform(areaName, loadPattern, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"ETABS could not clear old uniform area load pattern '{loadPattern}' on shell area '{areaName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Old uniform area loads could not be cleared on shell area '{areaName}': {ex.Message}");
        }
    }

    private static void TryClearFrameDistributedLoads(ETABSv1.cSapModel sapModel, string frameName, List<string> warnings)
    {
        TryClearFrameDistributedLoads(sapModel, frameName, warnings, "");
    }

    private static void TryClearFrameDistributedLoads(ETABSv1.cSapModel sapModel, string frameName, List<string> warnings, string loadPatternFilter)
    {
        try
        {
            int numberItems = 0;
            string[] frameNames = [];
            string[] loadPatterns = [];
            int[] loadTypes = [];
            string[] coordinateSystems = [];
            int[] directions = [];
            double[] distance1 = [];
            double[] distance2 = [];
            double[] value1 = [];
            double[] value2 = [];
            double[] absoluteDistance1 = [];
            double[] absoluteDistance2 = [];

            int ret = sapModel.FrameObj.GetLoadDistributed(
                frameName,
                ref numberItems,
                ref frameNames,
                ref loadPatterns,
                ref loadTypes,
                ref coordinateSystems,
                ref directions,
                ref distance1,
                ref distance2,
                ref value1,
                ref value2,
                ref absoluteDistance1,
                ref absoluteDistance2,
                EtabsObjects);

            if (ret != 0 || loadPatterns.Length == 0)
                return;

            foreach (string loadPattern in loadPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Where(pattern => string.IsNullOrWhiteSpace(loadPatternFilter) || string.Equals(pattern, loadPatternFilter, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.DeleteLoadDistributed(frameName, loadPattern, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"ETABS could not clear old distributed load pattern '{loadPattern}' on frame '{frameName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Old distributed frame loads could not be cleared on frame '{frameName}': {ex.Message}");
        }
    }

    private static void TryClearFramePointLoads(ETABSv1.cSapModel sapModel, string frameName, List<string> warnings)
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

            int ret = sapModel.FrameObj.GetLoadPoint(
                frameName,
                ref numberItems,
                ref frameNames,
                ref loadPatterns,
                ref loadTypes,
                ref coordinateSystems,
                ref directions,
                ref relativeDistances,
                ref values,
                ref absoluteDistances,
                EtabsObjects);

            if (ret != 0 || loadPatterns.Length == 0)
                return;

            foreach (string loadPattern in loadPatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int deleteRet = sapModel.FrameObj.DeleteLoadPoint(frameName, loadPattern, EtabsObjects);
                if (deleteRet != 0)
                    warnings.Add($"ETABS could not clear old point load pattern '{loadPattern}' on frame '{frameName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Old frame point loads could not be cleared on frame '{frameName}': {ex.Message}");
        }
    }

    private static int ToEtabsDistributedLoadDirection(string? direction)
    {
        return (direction ?? "").Trim().ToUpperInvariant() switch
        {
            "GLOBALX" => 4,
            "GLOBALY" => 5,
            _ => 6
        };
    }

    private static void TryUnlockModelForDrawing(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        try
        {
            if (sapModel.GetModelIsLocked())
            {
                int ret = sapModel.SetModelIsLocked(false);
                if (ret == 0)
                    warnings.Add("ETABS model was locked; it was unlocked before drawing.");
                else
                    warnings.Add("ETABS model appears locked and could not be unlocked. Drawing may fail.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add("Unable to check/unlock ETABS model: " + ex.Message);
        }
    }

    private static void TryRefreshEtabsView(ETABSv1.cSapModel sapModel)
    {
        try
        {
            sapModel.View.RefreshView(0, false);
        }
        catch
        {
            // Refresh is cosmetic; object creation results matter more.
        }
    }

    private static (double X, double Y, double Z) GetPointCoordinates(ETABSv1.cSapModel sapModel, string pointName)
    {
        double x = 0;
        double y = 0;
        double z = 0;
        int ret = sapModel.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z, "Global");
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not find point object '{pointName}' for coordinate retrieval.");

        return (x, y, z);
    }

    private static ETABSv1.eUnits? TryGetPresentUnits(ETABSv1.cSapModel sapModel)
    {
        try
        {
            return sapModel.GetPresentUnits();
        }
        catch
        {
            return null;
        }
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

    private static void TrySetPresentUnitsToKnM(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        try
        {
            int ret = sapModel.SetPresentUnits(EtabsUnitsKnMC);
            if (ret != 0)
                warnings.Add("ETABS did not switch present units to kN-m. Returned values may follow current ETABS units.");
        }
        catch (Exception ex)
        {
            warnings.Add("Unable to switch ETABS present units to kN-m: " + ex.Message);
        }
    }

    private static void TryRestorePresentUnits(ETABSv1.cSapModel sapModel, ETABSv1.eUnits originalUnits)
    {
        try
        {
            sapModel.SetPresentUnits(originalUnits);
        }
        catch
        {
            // The operation result should not be masked by a unit restore failure.
        }
    }

    private static ETABSv1.cOAPI GetEtabsObject(string? instanceId)
    {
        string id = (instanceId ?? "").Trim();
        if (id.Length == 0 || string.Equals(id, "active", StringComparison.OrdinalIgnoreCase))
            return GetActiveEtabsObject();

        if (id.StartsWith("process:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(id["process:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
        {
            return GetEtabsObjectForProcess(processId);
        }

        foreach ((string DisplayName, ETABSv1.cOAPI Object) instance in EnumerateEtabsObjects())
        {
            if (string.Equals(instance.DisplayName, id, StringComparison.OrdinalIgnoreCase))
                return instance.Object;
        }

        throw new InvalidOperationException("The selected ETABS instance is no longer available. Refresh the ETABS instance list and try again.");
    }

    private static ETABSv1.cOAPI GetActiveEtabsObject()
    {
        ETABSv1.cOAPI? activeObject = TryGetActiveObjectFromRot(EtabsApiObjectProgId);
        if (activeObject != null)
            return activeObject;

        Process[] etabsProcesses = Process.GetProcessesByName("ETABS");
        if (etabsProcesses.Length > 0)
        {
            ETABSv1.cOAPI? helperObject = TryGetHelperEtabsObject(null);
            if (helperObject != null)
                return helperObject;

            if (etabsProcesses.Length == 1)
            {
                ETABSv1.cOAPI? processObject = TryGetHelperEtabsObject(etabsProcesses[0].Id);
                if (processObject != null)
                    return processObject;
            }
        }

        throw new InvalidOperationException("No active ETABS API instance was found. Open ETABS before using ETABS import/draw tools.");
    }

    private static ETABSv1.cOAPI GetEtabsObjectForProcess(int processId)
    {
        ETABSv1.cOAPI? etabsObject = TryGetHelperEtabsObject(processId);
        if (etabsObject != null)
            return etabsObject;

        throw new InvalidOperationException($"Unable to connect to ETABS process {processId}. Confirm the model is open and ETABS is running under the same Windows user/elevation as this app.");
    }

    private static ETABSv1.cOAPI? TryGetHelperEtabsObject(int? processId)
    {
        try
        {
            ETABSv1.cHelper helper = new ETABSv1.Helper();
            return processId.HasValue
                ? helper.GetObjectProcess(EtabsApiObjectProgId, processId.Value)
                : helper.GetObject(EtabsApiObjectProgId);
        }
        catch
        {
            return null;
        }
    }

    private static List<EtabsInstanceInfo> EnumerateEtabsProcessInstances(List<string> warnings)
    {
        var instances = new List<EtabsInstanceInfo>();

        foreach (Process process in Process.GetProcessesByName("ETABS").OrderBy(process => process.Id))
        {
            string modelFile = "";
            try
            {
                ETABSv1.cOAPI etabsObject = GetEtabsObjectForProcess(process.Id);
                modelFile = TryGetModelFilename(etabsObject);
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS process {process.Id} is visible but could not be queried: {ex.Message}");
            }

            string title = "";
            try
            {
                title = process.MainWindowTitle ?? "";
            }
            catch
            {
                // Process title is optional display information.
            }

            string shortName = string.IsNullOrWhiteSpace(modelFile)
                ? (string.IsNullOrWhiteSpace(title) ? "ETABS" : title)
                : Path.GetFileName(modelFile);

            instances.Add(new EtabsInstanceInfo
            {
                Id = $"process:{process.Id.ToString(CultureInfo.InvariantCulture)}",
                DisplayName = $"{shortName} - PID {process.Id.ToString(CultureInfo.InvariantCulture)}",
                ModelFile = modelFile,
                RotDisplayName = ""
            });
        }

        return instances;
    }

    private static List<(string DisplayName, ETABSv1.cOAPI Object)> EnumerateEtabsObjects()
    {
        var objects = new List<(string DisplayName, ETABSv1.cOAPI Object)>();

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
                ETABSv1.cOAPI apiObject = (ETABSv1.cOAPI)comObject;

                if (IsEtabsObject(apiObject, displayName))
                    objects.Add((displayName, apiObject));
            }
            catch
            {
                // Ignore unrelated or inaccessible ROT entries.
            }
        }

        return objects;
    }

    private static bool IsEtabsObject(ETABSv1.cOAPI comObject, string displayName)
    {
        if (displayName.Contains("ETABS", StringComparison.OrdinalIgnoreCase) ||
            displayName.Contains("CSI.ETABS", StringComparison.OrdinalIgnoreCase))
        {
            return HasSapModel(comObject);
        }

        return HasSapModel(comObject) && TryGetModelFilename(comObject).Length > 0;
    }

    private static bool HasSapModel(ETABSv1.cOAPI comObject)
    {
        try
        {
            ETABSv1.cSapModel sapModel = comObject.SapModel;
            return sapModel != null;
        }
        catch
        {
            return false;
        }
    }

    private static ETABSv1.cSapModel GetRequiredSapModelObject(ETABSv1.cOAPI etabsObject)
    {
        try
        {
            ETABSv1.cSapModel sapModel = etabsObject.SapModel;
            if (sapModel == null)
                throw new InvalidOperationException("Connected to the ETABS API object, but ETABS did not return SapModel. Open a model in ETABS and make sure ETABS and this app are running under the same Windows user/elevation.");

            return sapModel;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("ETABS returned a COM object, but the SapModel property could not be read. Details: " + ex.Message, ex);
        }
    }

    private static EtabsInstanceInfo BuildEtabsInstance(ETABSv1.cOAPI etabsObject, string displayName, int index)
    {
        string modelFile = TryGetModelFilename(etabsObject);
        string shortName = string.IsNullOrWhiteSpace(modelFile)
            ? $"ETABS Instance {index + 1}"
            : Path.GetFileName(modelFile);

        return new EtabsInstanceInfo
        {
            Id = displayName,
            DisplayName = shortName,
            ModelFile = modelFile,
            RotDisplayName = displayName
        };
    }

    private static string TryGetModelFilename(ETABSv1.cOAPI etabsObject)
    {
        try
        {
            ETABSv1.cSapModel sapModel = etabsObject.SapModel;
            string? fileName = sapModel.GetModelFilename(true);
            return fileName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static ETABSv1.cOAPI? TryGetActiveObjectFromRot(string progId)
    {
        if (CLSIDFromProgID(progId, out Guid clsid) != 0)
            return null;

        int result = GetActiveObject(ref clsid, IntPtr.Zero, out object? activeObject);
        if (result != 0 || activeObject == null)
            return null;

        return activeObject as ETABSv1.cOAPI;
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
