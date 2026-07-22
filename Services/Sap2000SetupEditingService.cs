using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed partial class Sap2000ModellingService
{
    public EtabsFrameSectionImportResult ImportFrameSections(EtabsFrameSectionImportRequest request)
    {
        var warnings = new List<string>();

        try
        {
            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

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
                            ? $"No SAP2000 frame objects were found in group '{groupName}'."
                            : request.UseSelectedFrames
                                ? "No SAP2000 frame objects are selected. Select frame objects in SAP2000 or switch to all-frame import."
                                : "No SAP2000 frame objects were found in the selected SAP2000 model.",
                        Warnings = warnings
                    };
                }

                List<EtabsFrameSectionRow> rows = frameNames
                    .Select(frameName => ReadFrameSectionRow(sapModel, frameName, warnings, groupName))
                    .ToList();

                return new EtabsFrameSectionImportResult
                {
                    IsError = false,
                    Message = groupName.Length > 0
                        ? $"Imported {rows.Count} SAP2000 frame object(s) from group '{groupName}' for section editing."
                        : $"Imported {rows.Count} SAP2000 frame object(s) for section editing.",
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

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            return UpdateFrameSectionsCore(sapModel, targetFrames, warnings, "SAP2000 frame section assignment");
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

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                List<string> loadPatterns = GetLoadPatternNames(sapModel, warnings);
                if (loadPatterns.Count > 0 && !loadPatterns.Contains(loadPattern, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Load pattern '{loadPattern}' does not exist in the connected SAP2000 model.");

                double loadValue = -Math.Abs(request.LineLoadKnPerM);
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
                            10,
                            0,
                            1,
                            loadValue,
                            loadValue,
                            "Global",
                            true,
                            false,
                            Sap2000Objects);

                        if (ret != 0)
                        {
                            warnings.Add($"SAP2000 could not update distributed load on frame '{frame.FrameName}'. Return code: {ret}.");
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

                TryRefreshSap2000View(sapModel);

                return new EtabsFrameLoadUpdateResult
                {
                    IsError = updatedCount == 0,
                    Message = updatedCount == 0
                        ? "No existing SAP2000 frame distributed loads were updated."
                        : $"Updated distributed loads on {updatedCount} existing SAP2000 frame object(s).",
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
                throw new InvalidOperationException("Select or enter a SAP2000 group name before assigning selected frames.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            List<string> frameNames = ReadSelectedFrameNames(sapModel, warnings);
            if (frameNames.Count == 0)
                throw new InvalidOperationException("No SAP2000 frame objects are selected. Select frame members in SAP2000 before assigning them to a group.");

            TryUnlockModelForDrawing(sapModel, warnings);
            groupName = EnsureSap2000Group(sapModel, groupName, warnings);

            int assignedCount = 0;
            var assignedFrameNames = new List<string>();
            foreach (string frameName in frameNames)
            {
                try
                {
                    int ret = sapModel.FrameObj.SetGroupAssign(frameName, groupName, false, Sap2000Objects);
                    if (ret != 0)
                    {
                        warnings.Add($"SAP2000 could not assign frame '{frameName}' to group '{groupName}'. Return code: {ret}.");
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

            TryRefreshSap2000View(sapModel);

            return new EtabsFrameGroupAssignResult
            {
                IsError = assignedCount == 0,
                Message = assignedCount == 0
                    ? $"No selected SAP2000 frame object(s) were assigned to group '{groupName}'."
                    : $"Assigned {assignedCount} selected SAP2000 frame object(s) to group '{groupName}'.",
                AssignedCount = assignedCount,
                GroupName = groupName,
                AssignedFrameNames = assignedFrameNames,
                Groups = GetGroupNames(sapModel, warnings),
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
                throw new InvalidOperationException("Select a SAP2000 group before applying a section to the group.");

            string sectionName = (request.SectionName ?? "").Trim();
            if (sectionName.Length == 0)
                throw new InvalidOperationException("Select a frame section before applying it to the group.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            List<string> frameNames = ReadFrameNamesInGroup(sapModel, groupName, warnings);
            if (frameNames.Count == 0)
            {
                return new EtabsFrameSectionUpdateResult
                {
                    IsError = true,
                    Message = $"No SAP2000 frame objects were found in group '{groupName}'.",
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
                $"SAP2000 group '{groupName}' section assignment");
            result.Message = $"Updated {result.UpdatedCount} SAP2000 frame section assignment(s) in group '{groupName}'.";
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

    public LoadCaseCombinationDataResult ListLoadCaseCombinationData(LoadCaseCombinationDataRequest request)
    {
        var warnings = new List<string>();
        Sap2000InstanceListResult instanceResult = ListSap2000Instances();
        warnings.AddRange(instanceResult.Warnings);

        List<Sap2000InstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.Sap2000InstanceId);

        var result = new LoadCaseCombinationDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0 ? instanceResult.Message : "",
            Sap2000Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            Warnings = warnings
        };

        if (instances.Count == 0)
            return result;

        try
        {
            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(selectedInstanceId));
            result.LoadPatterns = GetLoadPatternRows(sapModel, warnings);
            result.LoadCases = GetLoadCaseRows(sapModel, warnings);
            result.LoadCombinations = GetLoadCombinationRows(sapModel, warnings, result.LoadCases);
            result.Message = $"Loaded {result.LoadPatterns.Count} SAP2000 load pattern(s), {result.LoadCases.Count} load case(s), and {result.LoadCombinations.Count} combination(s).";
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

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);
            SAP2000v1.eLoadPatternType patternType = ParseLoadPatternType(request.PatternType);

            int ret = sapModel.LoadPatterns.Add(name, patternType, request.SelfWeightMultiplier, true);
            if (ret != 0)
            {
                int typeRet = sapModel.LoadPatterns.SetLoadType(name, patternType);
                int swRet = sapModel.LoadPatterns.SetSelfWTMultiplier(name, request.SelfWeightMultiplier);
                if (typeRet != 0 || swRet != 0)
                    throw new InvalidOperationException($"SAP2000 could not add or update load pattern '{name}'. Add return: {ret}, type return: {typeRet}, self-weight return: {swRet}.");
            }

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"SAP2000 load pattern '{name}' was added/updated.",
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

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.LoadCases.StaticLinear.SetCase(caseName);
            if (ret != 0)
                throw new InvalidOperationException($"SAP2000 could not create/update static linear case '{caseName}'. Return code: {ret}.");

            int numberLoads = items.Count;
            string[] loadTypes = items.Select(item => string.IsNullOrWhiteSpace(item.LoadType) ? "Load" : item.LoadType.Trim()).ToArray();
            string[] loadNames = items.Select(item => item.Name.Trim()).ToArray();
            double[] scaleFactors = items.Select(item => item.ScaleFactor).ToArray();
            ret = sapModel.LoadCases.StaticLinear.SetLoads(caseName, numberLoads, ref loadTypes, ref loadNames, ref scaleFactors);
            if (ret != 0)
                throw new InvalidOperationException($"SAP2000 could not assign load items to static case '{caseName}'. Return code: {ret}.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"SAP2000 static linear case '{caseName}' was added/updated.",
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

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);
            int comboType = ParseComboType(request.ComboType);

            int ret = sapModel.RespCombo.Add(comboName, comboType);
            if (ret != 0)
            {
                int existingType = 0;
                int typeRet = sapModel.RespCombo.GetTypeOAPI(comboName, ref existingType);
                if (typeRet != 0)
                    throw new InvalidOperationException($"SAP2000 could not add or find response combination '{comboName}'. Return code: {ret}.");

                if (existingType != comboType)
                {
                    int deleteRet = sapModel.RespCombo.Delete(comboName);
                    if (deleteRet != 0)
                        throw new InvalidOperationException($"Existing SAP2000 combination '{comboName}' has a different type and could not be replaced. Return code: {deleteRet}.");

                    ret = sapModel.RespCombo.Add(comboName, comboType);
                    if (ret != 0)
                        throw new InvalidOperationException($"SAP2000 could not recreate response combination '{comboName}'. Return code: {ret}.");
                }
            }

            DeleteExistingComboItems(sapModel, comboName, warnings);
            var addFailures = new List<string>();
            foreach (EtabsComboItemRow item in items)
            {
                string itemName = item.Name.Trim();
                SAP2000v1.eCNameType sourceType = ParseComboSourceType(item.SourceType);
                ret = sapModel.RespCombo.SetCaseList(comboName, ref sourceType, itemName, item.Factor);
                if (ret != 0)
                    addFailures.Add($"'{itemName}' returned {ret}");
            }

            if (addFailures.Count > 0)
                throw new InvalidOperationException($"SAP2000 could not add {addFailures.Count} item(s) to combination '{comboName}': {string.Join(", ", addFailures)}.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"SAP2000 combination '{comboName}' was added/updated with {items.Count} item(s).",
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
        return DeleteSap2000LoadItem(request, "load pattern", (sapModel, name) => sapModel.LoadPatterns.Delete(name));
    }

    public LoadCaseCombinationUpdateResult DeleteLoadCase(LoadCaseCombinationDeleteRequest request)
    {
        return DeleteSap2000LoadItem(request, "load case", (sapModel, name) => sapModel.LoadCases.Delete(name));
    }

    public LoadCaseCombinationUpdateResult DeleteLoadCombination(LoadCaseCombinationDeleteRequest request)
    {
        return DeleteSap2000LoadItem(request, "response combination", (sapModel, name) => sapModel.RespCombo.Delete(name));
    }

    public SectionPropertyDataResult ListSectionPropertyData(SectionPropertyDataRequest request)
    {
        var warnings = new List<string>();
        Sap2000InstanceListResult instanceResult = ListSap2000Instances();
        warnings.AddRange(instanceResult.Warnings);

        List<Sap2000InstanceInfo> instances = instanceResult.Instances;
        string selectedInstanceId = ResolveSelectedInstanceId(instances, request.Sap2000InstanceId);

        var result = new SectionPropertyDataResult
        {
            IsError = instances.Count == 0,
            Message = instances.Count == 0 ? instanceResult.Message : "",
            Sap2000Instances = instances,
            SelectedInstanceId = selectedInstanceId,
            Warnings = warnings
        };

        if (instances.Count == 0)
            return result;

        try
        {
            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(selectedInstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                result.Materials = GetMaterialPropertyRows(sapModel, warnings);
                result.FrameProperties = GetFramePropertyRows(sapModel, warnings);
                result.AreaProperties = GetAreaPropertyRows(sapModel, warnings);
                result.Message = $"Loaded {result.Materials.Count} SAP2000 material(s), {result.FrameProperties.Count} frame section(s), and {result.AreaProperties.Count} area propertie(s).";
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

    public SectionPropertyUpdateResult UpdateMaterialProperty(MaterialPropertyUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string materialName = (request.Name ?? "").Trim();
            if (materialName.Length == 0)
                throw new InvalidOperationException("Material name is required.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                SAP2000v1.eMatType materialType = ParseMaterialType(request.MaterialType);
                int ret = sapModel.PropMaterial.SetMaterial(materialName, materialType, -1, "Created/updated from CSI Modelling Tools", "");
                if (ret != 0)
                    throw new InvalidOperationException($"SAP2000 could not create/update material '{materialName}'. Return code: {ret}.");

                ret = sapModel.PropMaterial.SetMPIsotropic(
                    materialName,
                    MpaToKnPerM2(EnsurePositive(request.ElasticModulusMpa, "Elastic modulus")),
                    Math.Clamp(request.PoissonRatio, 0.0, 0.49),
                    request.ThermalExpansion,
                    0);
                if (ret != 0)
                    throw new InvalidOperationException($"SAP2000 could not assign isotropic properties to material '{materialName}'. Return code: {ret}.");

                ret = sapModel.PropMaterial.SetWeightAndMass(materialName, 1, Math.Max(0.0, request.UnitWeightKnPerM3), 0);
                if (ret != 0)
                    warnings.Add($"SAP2000 could not assign unit weight to material '{materialName}'. Return code: {ret}.");
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"SAP2000 material '{materialName}' was added/updated.",
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

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                string shape = NormalizeCsiLabel(request.ShapeType);
                int ret = shape switch
                {
                    "ConcreteCircular" or "Circle" or "Circular" =>
                        sapModel.PropFrame.SetCircle(propertyName, materialName, EnsurePositive(request.Depth, "Diameter"), -1, "Created/updated from CSI Modelling Tools", ""),
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
                            "Created/updated from CSI Modelling Tools",
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
                            "Created/updated from CSI Modelling Tools",
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
                            "Created/updated from CSI Modelling Tools",
                            ""),
                    "SteelPipe" or "Pipe" =>
                        sapModel.PropFrame.SetPipe(
                            propertyName,
                            materialName,
                            EnsurePositive(request.Depth, "Diameter"),
                            EnsurePositive(request.FlangeThickness, "Wall thickness"),
                            -1,
                            "Created/updated from CSI Modelling Tools",
                            ""),
                    _ =>
                        sapModel.PropFrame.SetRectangle(
                            propertyName,
                            materialName,
                            EnsurePositive(request.Depth, "Depth"),
                            EnsurePositive(request.Width, "Width"),
                            -1,
                            "Created/updated from CSI Modelling Tools",
                            "")
                };

                if (ret != 0)
                    throw new InvalidOperationException($"SAP2000 could not create/update frame section '{propertyName}'. Return code: {ret}.");
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"SAP2000 frame section '{propertyName}' was added/updated.",
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
                throw new InvalidOperationException("Area property name is required.");
            if (materialName.Length == 0)
                throw new InvalidOperationException("Select a material for the area property.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                int shellType = ParseShellType(request.ShellType);
                double thickness = EnsurePositive(request.Thickness, "Thickness");
                int ret = sapModel.PropArea.SetShell(propertyName, shellType, materialName, 0, thickness, 0, -1, "Created/updated from CSI Modelling Tools", "");

                if (ret != 0)
                    throw new InvalidOperationException($"SAP2000 could not create/update area property '{propertyName}'. Return code: {ret}.");
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"SAP2000 area property '{propertyName}' was added/updated.",
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

    public SectionPropertyUpdateResult DeleteMaterialProperty(SectionPropertyDeleteRequest request)
    {
        return DeleteSap2000Property(request, "material", (sapModel, name) => sapModel.PropMaterial.Delete(name));
    }

    public SectionPropertyUpdateResult DeleteFrameProperty(SectionPropertyDeleteRequest request)
    {
        return DeleteSap2000Property(request, "frame section", (sapModel, name) => sapModel.PropFrame.Delete(name));
    }

    public SectionPropertyUpdateResult DeleteAreaProperty(SectionPropertyDeleteRequest request)
    {
        return DeleteSap2000Property(request, "area property", (sapModel, name) => sapModel.PropArea.Delete(name));
    }

    public SteelSectionCatalogResult ListSteelSectionCatalog(SteelSectionCatalogRequest request)
    {
        var warnings = new List<string>();

        try
        {
            string databaseFile = ResolveSteelPropertyDatabaseFile(request.DatabaseFile);
            if (databaseFile.Length == 0)
                throw new InvalidOperationException("Steel database file name/path is required.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eFramePropType shapeType = ParseFramePropType(request.ShapeType);
            int numberNames = 0;
            string[] sectionNames = [];
            SAP2000v1.eFramePropType[] propTypes = [];
            int ret = sapModel.PropFrame.GetPropFileNameList(databaseFile, ref numberNames, ref sectionNames, ref propTypes, shapeType);
            if (ret != 0)
                throw new InvalidOperationException($"SAP2000 could not read steel section database '{databaseFile}'. Return code: {ret}.");

            List<string> names = sectionNames
                .Take(Math.Min(numberNames, sectionNames.Length))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
                warnings.Add($"No SAP2000 sections were returned from '{databaseFile}' for shape '{request.ShapeType}'.");

            return new SteelSectionCatalogResult
            {
                IsError = false,
                Message = $"Loaded {names.Count} SAP2000 steel database section name(s).",
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

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);

            int ret = sapModel.PropFrame.ImportProp(propertyName, materialName, databaseFile, databaseSectionName, -1, "Imported from CSI Modelling Tools", "");
            if (ret != 0)
                throw new InvalidOperationException($"SAP2000 could not import steel section '{databaseSectionName}' as '{propertyName}'. Return code: {ret}.");

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Imported SAP2000 steel frame section '{propertyName}'.",
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

    private static EtabsFrameSectionUpdateResult UpdateFrameSectionsCore(
        SAP2000v1.cSapModel sapModel,
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
                warnings.Add($"Skipped SAP2000 frame '{frame.FrameName}': no new section selected.");
                continue;
            }

            if (validSections.Count > 0 && !validSections.Contains(newSection))
            {
                warnings.Add($"Skipped SAP2000 frame '{frame.FrameName}': section '{newSection}' does not exist in the connected model.");
                continue;
            }

            try
            {
                int ret = sapModel.FrameObj.SetSection(frame.FrameName, newSection, Sap2000Objects, 0, 0);
                if (ret != 0)
                {
                    warnings.Add($"SAP2000 could not update frame '{frame.FrameName}' to section '{newSection}'. Return code: {ret}.");
                    continue;
                }

                updatedCount++;
                updatedFrameNames.Add(frame.FrameName);
                frame.CurrentSection = newSection;
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 frame '{frame.FrameName}' section update failed: {ex.Message}");
            }
        }

        TryRefreshSap2000View(sapModel);

        return new EtabsFrameSectionUpdateResult
        {
            IsError = updatedCount == 0,
            Message = $"Updated {updatedCount} {description}(s).",
            UpdatedCount = updatedCount,
            UpdatedFrameNames = updatedFrameNames,
            FrameSections = frameSections,
            Groups = GetGroupNames(sapModel, warnings),
            Warnings = warnings
        };
    }

    private static List<string> ReadSelectedFrameNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        int ret = sapModel.SelectObj.GetSelected(ref numberItems, ref objectTypes, ref objectNames);
        if (ret != 0)
            throw new InvalidOperationException("Unable to read selected objects from SAP2000.");

        var frameNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ignoredCount = 0;
        int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));

        for (int index = 0; index < count; index++)
        {
            string name = (objectNames[index] ?? "").Trim();
            if (name.Length == 0)
                continue;

            if (objectTypes[index] != Sap2000SelectedFrameObjectType)
            {
                ignoredCount++;
                continue;
            }

            if (seen.Add(name))
                frameNames.Add(name);
        }

        if (ignoredCount > 0)
            warnings.Add($"Ignored {ignoredCount} selected SAP2000 object(s) that are not frame objects.");

        return frameNames;
    }

    private static List<string> ReadFrameNamesInGroup(SAP2000v1.cSapModel sapModel, string groupName, List<string> warnings)
    {
        string normalizedGroupName = (groupName ?? "").Trim();
        if (normalizedGroupName.Length == 0)
            throw new InvalidOperationException("Select a SAP2000 group first.");

        int numberItems = 0;
        int[] objectTypes = [];
        string[] objectNames = [];
        int ret = sapModel.GroupDef.GetAssignments(normalizedGroupName, ref numberItems, ref objectTypes, ref objectNames);
        if (ret != 0)
            throw new InvalidOperationException($"SAP2000 group '{normalizedGroupName}' could not be read. Return code: {ret}.");

        var frameNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ignoredCount = 0;
        int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));

        for (int index = 0; index < count; index++)
        {
            string name = (objectNames[index] ?? "").Trim();
            if (name.Length == 0)
                continue;

            if (objectTypes[index] != Sap2000SelectedFrameObjectType)
            {
                ignoredCount++;
                continue;
            }

            if (seen.Add(name))
                frameNames.Add(name);
        }

        if (ignoredCount > 0)
            warnings.Add($"Ignored {ignoredCount} non-frame SAP2000 object(s) in group '{normalizedGroupName}'.");

        return frameNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetAllFrameNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            int ret = sapModel.FrameObj.GetNameList(ref numberNames, ref names);
            if (ret != 0)
            {
                warnings.Add($"SAP2000 frame object list could not be loaded. Return code: {ret}.");
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
            warnings.Add("SAP2000 frame object list could not be loaded: " + ex.Message);
            return [];
        }
    }

    private static EtabsFrameSectionRow ReadFrameSectionRow(SAP2000v1.cSapModel sapModel, string frameName, List<string> warnings, string groupName = "")
    {
        string currentSection = "";
        try
        {
            string autoSelectList = "";
            int ret = sapModel.FrameObj.GetSection(frameName, ref currentSection, ref autoSelectList);
            if (ret != 0)
                warnings.Add($"SAP2000 could not read current section for frame '{frameName}'. Return code: {ret}.");

            if (string.IsNullOrWhiteSpace(currentSection))
                currentSection = autoSelectList;
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 could not read current section for frame '{frameName}': {ex.Message}");
        }

        string pointI = "";
        string pointJ = "";
        double length = 0;
        try
        {
            int ret = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
            if (ret == 0 && pointI.Length > 0 && pointJ.Length > 0)
            {
                (double X, double Y, double Z) pointICoord = GetPointCoordinates(sapModel, pointI);
                (double X, double Y, double Z) pointJCoord = GetPointCoordinates(sapModel, pointJ);
                length = CalculateDistance(pointICoord, pointJCoord);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 could not read endpoints for frame '{frameName}': {ex.Message}");
        }

        return new EtabsFrameSectionRow
        {
            Include = true,
            FrameName = frameName,
            Label = frameName,
            Story = "",
            GroupName = groupName,
            CurrentSection = currentSection,
            NewSection = currentSection,
            PointI = pointI,
            PointJ = pointJ,
            LengthM = length
        };
    }

    private static List<EtabsLoadPatternRow> GetLoadPatternRows(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        var rows = new List<EtabsLoadPatternRow>();

        foreach (string name in GetLoadPatternNames(sapModel, warnings))
        {
            SAP2000v1.eLoadPatternType loadType = SAP2000v1.eLoadPatternType.Other;
            double selfWeightMultiplier = 0;

            try
            {
                int typeRet = sapModel.LoadPatterns.GetLoadType(name, ref loadType);
                if (typeRet != 0)
                    warnings.Add($"SAP2000 could not read load pattern type for '{name}'. Return code: {typeRet}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 load pattern type read failed for '{name}': {ex.Message}");
            }

            try
            {
                int swRet = sapModel.LoadPatterns.GetSelfWTMultiplier(name, ref selfWeightMultiplier);
                if (swRet != 0)
                    warnings.Add($"SAP2000 could not read self-weight multiplier for load pattern '{name}'. Return code: {swRet}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 self-weight multiplier read failed for '{name}': {ex.Message}");
            }

            var row = new EtabsLoadPatternRow
            {
                Name = name,
                PatternType = loadType.ToString(),
                SelfWeightMultiplier = selfWeightMultiplier
            };
            row.AcceptChanges();
            rows.Add(row);
        }

        return rows;
    }

    private static List<EtabsLoadCaseRow> GetLoadCaseRows(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        var rowsByName = new Dictionary<string, EtabsLoadCaseRow>(StringComparer.OrdinalIgnoreCase);

        foreach (SAP2000v1.eLoadCaseType caseFamily in Enum.GetValues(typeof(SAP2000v1.eLoadCaseType)).Cast<SAP2000v1.eLoadCaseType>())
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

                    SAP2000v1.eLoadCaseType caseType = caseFamily;
                    int subType = 0;
                    try
                    {
                        int typeRet = sapModel.LoadCases.GetTypeOAPI(caseName, ref caseType, ref subType);
                        if (typeRet != 0)
                            warnings.Add($"SAP2000 could not read load case type for '{caseName}'. Return code: {typeRet}.");
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"SAP2000 load case type read failed for '{caseName}': {ex.Message}");
                    }

                    List<StaticLoadCaseItemRow> items = caseType == SAP2000v1.eLoadCaseType.LinearStatic
                        ? GetStaticLoadCaseItems(sapModel, caseName, warnings)
                        : [];

                    var row = new EtabsLoadCaseRow
                    {
                        Name = caseName,
                        CaseType = caseType.ToString(),
                        SubType = subType == 0 ? "" : subType.ToString(CultureInfo.InvariantCulture),
                        IsEditable = caseType == SAP2000v1.eLoadCaseType.LinearStatic,
                        Items = new ObservableCollection<StaticLoadCaseItemRow>(items),
                        ItemsSummary = BuildStaticCaseItemsSummary(items)
                    };
                    row.AcceptChanges();
                    rowsByName[caseName] = row;
                }
            }
            catch
            {
                // Some SAP2000 models do not have every analysis case family.
            }
        }

        return rowsByName.Values
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<EtabsLoadCombinationRow> GetLoadCombinationRows(
        SAP2000v1.cSapModel sapModel,
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
                    warnings.Add($"SAP2000 could not read response combination type for '{name}'. Return code: {typeRet}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 response combination type read failed for '{name}': {ex.Message}");
            }

            var items = new List<EtabsComboItemRow>();
            try
            {
                int numberItems = 0;
                SAP2000v1.eCNameType[] sourceTypes = [];
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
                            sourceTypes[index],
                            sourceName,
                            validLoadCaseNames,
                            validComboNames,
                            canValidateLoadCaseNames))
                        {
                            continue;
                        }

                        items.Add(new EtabsComboItemRow
                        {
                            SourceType = sourceTypes[index] == SAP2000v1.eCNameType.LoadCombo ? "Combination" : "Load Case",
                            Name = sourceName,
                            Factor = factors[index]
                        });
                    }
                }
                else
                {
                    warnings.Add($"SAP2000 could not read response combination items for '{name}'. Return code: {ret}.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"SAP2000 response combination item read failed for '{name}': {ex.Message}");
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

    private static List<StaticLoadCaseItemRow> GetStaticLoadCaseItems(SAP2000v1.cSapModel sapModel, string caseName, List<string> warnings)
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
                warnings.Add($"SAP2000 could not read static load case items for '{caseName}'. Return code: {ret}.");
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
            warnings.Add($"SAP2000 static load case item read failed for '{caseName}': {ex.Message}");
        }

        return items;
    }

    private static List<EtabsMaterialPropertyRow> GetMaterialPropertyRows(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        return GetMaterialNames(sapModel, warnings)
            .Select(name =>
            {
                SAP2000v1.eMatType materialType = SAP2000v1.eMatType.NoDesign;
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
                        warnings.Add($"SAP2000 could not read material type for '{name}'. Return code: {ret}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"SAP2000 material type read failed for '{name}': {ex.Message}");
                }

                double elasticModulus = 0;
                double poisson = 0;
                double thermal = 0;
                double shearModulus = 0;
                try
                {
                    int ret = sapModel.PropMaterial.GetMPIsotropic(name, ref elasticModulus, ref poisson, ref thermal, ref shearModulus, 0);
                    if (ret != 0)
                        warnings.Add($"SAP2000 could not read isotropic material properties for '{name}'. Return code: {ret}.");
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
                        warnings.Add($"SAP2000 could not read unit weight for '{name}'. Return code: {ret}.");
                }
                catch
                {
                    // Weight is optional display data.
                }

                return new EtabsMaterialPropertyRow
                {
                    Name = name,
                    MaterialType = materialType.ToString(),
                    ElasticModulusMpa = KnPerM2ToMpa(elasticModulus),
                    PoissonRatio = poisson,
                    UnitWeightKnPerM3 = unitWeight,
                    DesignSummary = "SAP2000 material"
                };
            })
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<EtabsFramePropertyRow> GetFramePropertyRows(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        return GetFrameSectionNames(sapModel, warnings)
            .Select(name =>
            {
                SAP2000v1.eFramePropType propType = SAP2000v1.eFramePropType.General;
                string materialName = "";

                try
                {
                    int typeRet = sapModel.PropFrame.GetTypeOAPI(name, ref propType);
                    if (typeRet != 0)
                        warnings.Add($"SAP2000 could not read frame section shape for '{name}'. Return code: {typeRet}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"SAP2000 frame section shape read failed for '{name}': {ex.Message}");
                }

                try
                {
                    int matRet = sapModel.PropFrame.GetMaterial(name, ref materialName);
                    if (matRet != 0)
                        warnings.Add($"SAP2000 could not read frame section material for '{name}'. Return code: {matRet}.");
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

    private static List<EtabsAreaPropertyRow> GetAreaPropertyRows(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        return GetAreaPropertyNames(sapModel, warnings)
            .Select(name =>
            {
                int shellType = 1;
                string materialName = "";
                double thickness = 0;
                double materialAngle = 0;
                double bending = 0;
                int color = 0;
                string notes = "";
                string guid = "";
                string areaType = "Shell";

                try
                {
                    int ret = sapModel.PropArea.GetShell(name, ref shellType, ref materialName, ref materialAngle, ref thickness, ref bending, ref color, ref notes, ref guid);
                    if (ret != 0)
                        warnings.Add($"SAP2000 could not read area property data for '{name}'. Return code: {ret}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"SAP2000 area property read failed for '{name}': {ex.Message}");
                }

                return new EtabsAreaPropertyRow
                {
                    Name = name,
                    AreaType = areaType,
                    ShellType = FormatShellType(shellType),
                    MaterialName = materialName,
                    Thickness = thickness
                };
            })
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string EnsureSap2000Group(SAP2000v1.cSapModel sapModel, string? rawGroupName, List<string> warnings)
    {
        string groupName = EtabsNameUtility.BuildSafeName("", rawGroupName);
        if (groupName.Length == 0)
            throw new InvalidOperationException("Group name is required.");

        try
        {
            int ret = sapModel.GroupDef.SetGroup(groupName);
            if (ret != 0)
                warnings.Add($"SAP2000 group '{groupName}' could not be created or updated. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 group '{groupName}' could not be created or updated: {ex.Message}");
        }

        return groupName;
    }

    private static List<string> GetComboNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
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
            warnings.Add("SAP2000 load combination list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static List<string> GetAreaPropertyNames(SAP2000v1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            if (sapModel.PropArea.GetNameList(ref numberNames, ref names) == 0)
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
            warnings.Add("SAP2000 area property list could not be loaded: " + ex.Message);
        }

        return [];
    }

    private static void DeleteExistingComboItems(SAP2000v1.cSapModel sapModel, string comboName, List<string> warnings)
    {
        try
        {
            int numberItems = 0;
            SAP2000v1.eCNameType[] sourceTypes = [];
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
                    warnings.Add($"SAP2000 could not remove existing combination item '{sourceName}' from '{comboName}'. Return code: {deleteRet}.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Existing SAP2000 response combination items could not be cleared for '{comboName}': {ex.Message}");
        }
    }

    private static LoadCaseCombinationUpdateResult DeleteSap2000LoadItem(
        LoadCaseCombinationDeleteRequest request,
        string itemLabel,
        Func<SAP2000v1.cSapModel, string, int> delete)
    {
        var warnings = new List<string>();

        try
        {
            string name = (request.Name ?? "").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException($"Select a SAP2000 {itemLabel} to delete.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);
            int ret = delete(sapModel, name);
            if (ret != 0)
                throw new InvalidOperationException($"SAP2000 could not delete {itemLabel} '{name}'. Return code: {ret}.");

            return new LoadCaseCombinationUpdateResult
            {
                IsError = false,
                Message = $"Deleted SAP2000 {itemLabel} '{name}'.",
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

    private static SectionPropertyUpdateResult DeleteSap2000Property(
        SectionPropertyDeleteRequest request,
        string itemLabel,
        Func<SAP2000v1.cSapModel, string, int> delete)
    {
        var warnings = new List<string>();

        try
        {
            string name = (request.Name ?? "").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException($"Select a SAP2000 {itemLabel} to delete.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            TryUnlockModelForDrawing(sapModel, warnings);
            int ret = delete(sapModel, name);
            if (ret != 0)
                throw new InvalidOperationException($"SAP2000 could not delete {itemLabel} '{name}'. Return code: {ret}.");

            return new SectionPropertyUpdateResult
            {
                IsError = false,
                Message = $"Deleted SAP2000 {itemLabel} '{name}'.",
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

    private static bool IsCurrentComboSource(
        SAP2000v1.eCNameType sourceType,
        string sourceName,
        HashSet<string> validLoadCaseNames,
        HashSet<string> validComboNames,
        bool canValidateLoadCaseNames)
    {
        if (sourceType == SAP2000v1.eCNameType.LoadCombo)
            return validComboNames.Contains(sourceName);

        return !canValidateLoadCaseNames || validLoadCaseNames.Contains(sourceName);
    }

    private static SAP2000v1.eLoadPatternType ParseLoadPatternType(string? value)
    {
        string normalized = NormalizeCsiLabel(value);
        foreach (SAP2000v1.eLoadPatternType loadType in Enum.GetValues(typeof(SAP2000v1.eLoadPatternType)).Cast<SAP2000v1.eLoadPatternType>())
        {
            if (string.Equals(NormalizeCsiLabel(loadType.ToString()), normalized, StringComparison.OrdinalIgnoreCase))
                return loadType;
        }

        return SAP2000v1.eLoadPatternType.Other;
    }

    private static SAP2000v1.eMatType ParseMaterialType(string? value)
    {
        string normalized = NormalizeCsiLabel(value);
        foreach (SAP2000v1.eMatType materialType in Enum.GetValues(typeof(SAP2000v1.eMatType)).Cast<SAP2000v1.eMatType>())
        {
            if (string.Equals(NormalizeCsiLabel(materialType.ToString()), normalized, StringComparison.OrdinalIgnoreCase))
                return materialType;
        }

        return string.Equals(normalized, "Concrete", StringComparison.OrdinalIgnoreCase)
            ? SAP2000v1.eMatType.Concrete
            : SAP2000v1.eMatType.Steel;
    }

    private static SAP2000v1.eFramePropType ParseFramePropType(string? value)
    {
        string normalized = NormalizeCsiLabel(value);
        return normalized switch
        {
            "SteelI" or "I" or "ISection" => SAP2000v1.eFramePropType.I,
            "SteelChannel" or "Channel" => SAP2000v1.eFramePropType.Channel,
            "SteelT" or "Tee" or "T" => SAP2000v1.eFramePropType.T,
            "SteelAngle" or "Angle" => SAP2000v1.eFramePropType.Angle,
            "SteelTube" or "Tube" or "Box" => SAP2000v1.eFramePropType.Box,
            "SteelPipe" or "Pipe" => SAP2000v1.eFramePropType.Pipe,
            "ConcreteRectangular" or "Rectangular" or "Rectangle" => SAP2000v1.eFramePropType.Rectangular,
            "ConcreteCircular" or "Circle" or "Circular" => SAP2000v1.eFramePropType.Circle,
            _ => SAP2000v1.eFramePropType.I
        };
    }

    private static int ParseShellType(string? value)
    {
        string normalized = NormalizeCsiLabel(value);
        return normalized switch
        {
            "ShellThin" or "ThinShell" or "Shell" => 1,
            "ShellThick" or "ThickShell" => 2,
            "Membrane" => 3,
            "Layered" or "LayeredShell" => 4,
            _ => 1
        };
    }

    private static string FormatShellType(int shellType)
    {
        return shellType switch
        {
            1 => "ShellThin",
            2 => "ShellThick",
            3 => "Membrane",
            4 => "Layered",
            _ => $"Shell Type {shellType.ToString(CultureInfo.InvariantCulture)}"
        };
    }

    private static int ParseComboType(string? value)
    {
        return NormalizeCsiLabel(value) switch
        {
            "Envelope" => 1,
            "AbsoluteAdditive" => 2,
            "SRSS" => 3,
            "RangeAdditive" => 4,
            _ => 0
        };
    }

    private static SAP2000v1.eCNameType ParseComboSourceType(string? value)
    {
        string normalized = NormalizeCsiLabel(value);
        return normalized == "LoadCombo" ||
            normalized == "Combination" ||
            normalized == "Combo"
            ? SAP2000v1.eCNameType.LoadCombo
            : SAP2000v1.eCNameType.LoadCase;
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

    private static string FormatFramePropType(SAP2000v1.eFramePropType propType)
    {
        return propType switch
        {
            SAP2000v1.eFramePropType.Rectangular => "Concrete Rectangular",
            SAP2000v1.eFramePropType.Circle => "Concrete Circular",
            SAP2000v1.eFramePropType.I => "Steel I",
            SAP2000v1.eFramePropType.Channel => "Steel Channel",
            SAP2000v1.eFramePropType.Box => "Steel Tube",
            SAP2000v1.eFramePropType.Pipe => "Steel Pipe",
            _ => propType.ToString()
        };
    }

    private static string GetFrameSectionSummary(SAP2000v1.cSapModel sapModel, string name)
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

    private static (double Depth, double Width, double FlangeThickness, double WebThickness) GetFrameSectionDimensions(
        SAP2000v1.cSapModel sapModel,
        string name,
        SAP2000v1.eFramePropType propType)
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
                case SAP2000v1.eFramePropType.Rectangular:
                    {
                        double depth = 0;
                        double width = 0;
                        int ret = sapModel.PropFrame.GetRectangle(name, ref fileName, ref materialName, ref depth, ref width, ref color, ref notes, ref guid);
                        return ret == 0 ? (depth, width, 0, 0) : default;
                    }
                case SAP2000v1.eFramePropType.Circle:
                    {
                        double diameter = 0;
                        int ret = sapModel.PropFrame.GetCircle(name, ref fileName, ref materialName, ref diameter, ref color, ref notes, ref guid);
                        return ret == 0 ? (diameter, 0, 0, 0) : default;
                    }
                case SAP2000v1.eFramePropType.I:
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
                case SAP2000v1.eFramePropType.Channel:
                    {
                        double depth = 0;
                        double width = 0;
                        double flangeThickness = 0;
                        double webThickness = 0;
                        int ret = sapModel.PropFrame.GetChannel(name, ref fileName, ref materialName, ref depth, ref width, ref flangeThickness, ref webThickness, ref color, ref notes, ref guid);
                        return ret == 0 ? (depth, width, flangeThickness, webThickness) : default;
                    }
                case SAP2000v1.eFramePropType.Box:
                    {
                        double depth = 0;
                        double width = 0;
                        double flangeThickness = 0;
                        double webThickness = 0;
                        int ret = sapModel.PropFrame.GetTube(name, ref fileName, ref materialName, ref depth, ref width, ref flangeThickness, ref webThickness, ref color, ref notes, ref guid);
                        return ret == 0 ? (depth, width, flangeThickness, webThickness) : default;
                    }
                case SAP2000v1.eFramePropType.Pipe:
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

    private static string ResolveSteelPropertyDatabaseFile(string? databaseFile)
    {
        string raw = (databaseFile ?? "").Trim();
        if (raw.Length == 0)
            return "";

        string baseName = Path.GetFileNameWithoutExtension(raw);
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "PropertyLibraries", baseName + ".xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "PropertyLibraries", baseName + ".xml"),
            raw
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists) ?? raw;
    }

    private static string BuildStaticCaseItemsSummary(IReadOnlyList<StaticLoadCaseItemRow> items)
    {
        if (items.Count == 0)
            return "";

        return string.Join(" ", items.Select((item, index) =>
        {
            string name = string.IsNullOrWhiteSpace(item.Name) ? "(blank)" : item.Name.Trim();
            string factor = Math.Abs(item.ScaleFactor).ToString("0.###", CultureInfo.InvariantCulture);
            string sign = index == 0
                ? item.ScaleFactor < 0 ? "-" : ""
                : item.ScaleFactor < 0 ? "-" : "+";
            return index == 0 ? $"{sign}{factor} {name}" : $"{sign} {factor} {name}";
        }));
    }

    private static string BuildComboItemsSummary(IReadOnlyList<EtabsComboItemRow> items)
    {
        if (items.Count == 0)
            return "";

        return string.Join(" + ", items.Select(item => $"{item.Factor:0.###} {item.Name}".Trim()));
    }

    private static double CalculateDistance((double X, double Y, double Z) first, (double X, double Y, double Z) second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        double dz = second.Z - first.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
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

    private static string NormalizeCsiLabel(string? value)
    {
        return new string((value ?? "")
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }
}
