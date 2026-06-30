using System.IO;
using CSIModellingTools.Models;
using CSIModellingTools.Models.Etabs;

namespace CSIModellingTools.Services;

public sealed partial class EtabsParametricModellingService
{
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
                SetPresentUnitsToKnMForSnapshot(sapModel);

                var extractor = new EtabsSnapshotExtractor(sapModel, TryGetModelFilename(etabsObject));
                EtabsSnapshotExtractionResult extraction = extractor.Extract();
                warnings.AddRange(extraction.Warnings);
                result.Snapshot = extraction.Snapshot;
                result.IsError = false;
                result.Message = $"Extracted model snapshot with {result.Snapshot.Frames.Count} frame object(s), {result.Snapshot.Areas.Count} area object(s), {result.Snapshot.FrameProperties.Count} frame propertie(s), {result.Snapshot.AreaProperties.Count} area propertie(s), and {result.Snapshot.Materials.Count} material(s). Frame read status: {result.Snapshot.Metadata.FramesReadStatus}.";
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

    public ModelCompareEtabsSelectionResult SelectModelCompareObjects(ModelCompareEtabsSelectionRequest request)
    {
        List<ModelCompareEtabsSelectionTarget> targets = (request.Targets ?? [])
            .Where(target => target != null)
            .Select(target => new ModelCompareEtabsSelectionTarget
            {
                ObjectType = target.ObjectType,
                ObjectName = (target.ObjectName ?? "").Trim()
            })
            .Where(target => target.ObjectName.Length > 0)
            .GroupBy(
                target => $"{(int)target.ObjectType}:{target.ObjectName}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (targets.Count == 0)
        {
            return new ModelCompareEtabsSelectionResult
            {
                IsError = true,
                Message = "No reliable ETABS frame or area object names were provided."
            };
        }

        if (targets.Count > ModelCompareEtabsSelectionLimits.MaxObjects)
        {
            return new ModelCompareEtabsSelectionResult
            {
                IsError = true,
                Message = $"Selection was not attempted because {targets.Count} unique objects exceed the safety limit of {ModelCompareEtabsSelectionLimits.MaxObjects}. Filter the table or select fewer rows."
            };
        }

        try
        {
            ETABSv1.cOAPI etabsObject = GetEtabsObject(request.EtabsInstanceId);
            ETABSv1.cSapModel sapModel = GetRequiredSapModelObject(etabsObject);
            string modelFileName = TryGetModelFilename(etabsObject);
            var result = new ModelCompareEtabsSelectionResult
            {
                TargetModelFileName = modelFileName
            };
            var validTargets = new List<ModelCompareEtabsSelectionTarget>();

            foreach (ModelCompareEtabsSelectionTarget target in targets)
            {
                if (TryValidateModelCompareSelectionTarget(sapModel, target, out string failureReason))
                {
                    validTargets.Add(target);
                }
                else
                {
                    result.Failures.Add(new ModelCompareEtabsSelectionFailure
                    {
                        ObjectType = target.ObjectType,
                        ObjectName = target.ObjectName,
                        Reason = failureReason
                    });
                }
            }

            if (validTargets.Count == 0)
            {
                result.IsError = true;
                result.Message = $"None of the {targets.Count} requested objects exist in {FormatModelCompareTargetModel(modelFileName)}. The existing ETABS selection was left unchanged.";
                return result;
            }

            int clearRet = sapModel.SelectObj.ClearSelection();
            if (clearRet != 0)
                throw new InvalidOperationException($"ETABS could not clear the current object selection. Return code: {clearRet}.");

            foreach (ModelCompareEtabsSelectionTarget target in validTargets)
            {
                int selectRet;
                try
                {
                    selectRet = target.ObjectType switch
                    {
                        ModelCompareObjectType.Frame => sapModel.FrameObj.SetSelected(target.ObjectName, true, EtabsObjects),
                        ModelCompareObjectType.Area => sapModel.AreaObj.SetSelected(target.ObjectName, true, EtabsObjects),
                        ModelCompareObjectType.Joint => sapModel.PointObj.SetSelected(target.ObjectName, true, EtabsObjects),
                        _ => -1
                    };
                }
                catch (Exception ex)
                {
                    selectRet = -1;
                    result.Failures.Add(new ModelCompareEtabsSelectionFailure
                    {
                        ObjectType = target.ObjectType,
                        ObjectName = target.ObjectName,
                        Reason = "ETABS selection failed: " + ex.Message
                    });
                }

                if (selectRet == 0)
                {
                    result.SelectedTargets.Add(target);
                }
                else if (!result.Failures.Any(failure =>
                    failure.ObjectType == target.ObjectType &&
                    string.Equals(failure.ObjectName, target.ObjectName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Failures.Add(new ModelCompareEtabsSelectionFailure
                    {
                        ObjectType = target.ObjectType,
                        ObjectName = target.ObjectName,
                        Reason = $"ETABS returned selection code {selectRet}."
                    });
                }
            }

            int refreshRet = result.SelectedTargets.Count > 0
                ? sapModel.View.RefreshView(0, false)
                : 0;
            result.IsError = result.SelectedTargets.Count == 0;
            result.Message = $"Selected {result.SelectedTargets.Count} of {targets.Count} unique requested object(s) in {FormatModelCompareTargetModel(modelFileName)}.";
            if (result.Failures.Count > 0)
                result.Message += $" Skipped {result.Failures.Count}.";
            if (refreshRet != 0)
                result.Message += " The ETABS view did not refresh automatically.";
            return result;
        }
        catch (Exception ex)
        {
            return new ModelCompareEtabsSelectionResult
            {
                IsError = true,
                Message = ex.Message
            };
        }
    }

    private static bool TryValidateModelCompareSelectionTarget(
        ETABSv1.cSapModel sapModel,
        ModelCompareEtabsSelectionTarget target,
        out string failureReason)
    {
        failureReason = "";

        try
        {
            if (target.ObjectType == ModelCompareObjectType.Frame)
            {
                string pointI = "";
                string pointJ = "";
                int ret = sapModel.FrameObj.GetPoints(target.ObjectName, ref pointI, ref pointJ);
                if (ret == 0 && !string.IsNullOrWhiteSpace(pointI) && !string.IsNullOrWhiteSpace(pointJ))
                    return true;

                failureReason = "The frame does not exist in the target ETABS model.";
                return false;
            }

            if (target.ObjectType == ModelCompareObjectType.Area)
            {
                int pointCount = 0;
                string[] pointNames = [];
                int ret = sapModel.AreaObj.GetPoints(target.ObjectName, ref pointCount, ref pointNames);
                if (ret == 0 && pointCount >= 3 && pointNames.Length >= 3)
                    return true;

                failureReason = "The area does not exist in the target ETABS model.";
                return false;
            }

            if (target.ObjectType == ModelCompareObjectType.Joint)
            {
                double x = 0;
                double y = 0;
                double z = 0;
                int ret = sapModel.PointObj.GetCoordCartesian(target.ObjectName, ref x, ref y, ref z, "Global");
                if (ret == 0)
                    return true;

                failureReason = "The joint does not exist in the target ETABS model.";
                return false;
            }

            failureReason = "Only frame, area and joint comparison results can be selected in ETABS.";
            return false;
        }
        catch (Exception ex)
        {
            failureReason = "ETABS could not verify the object: " + ex.Message;
            return false;
        }
    }

    private static string FormatModelCompareTargetModel(string modelFileName)
    {
        return string.IsNullOrWhiteSpace(modelFileName)
            ? "the connected ETABS model"
            : $"ETABS model '{Path.GetFileName(modelFileName)}'";
    }

    private static void SetPresentUnitsToKnMForSnapshot(ETABSv1.cSapModel sapModel)
    {
        try
        {
            int ret = sapModel.SetPresentUnits(EtabsUnitsKnMC);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not switch to {EtabsUnitsKnMC} for model snapshot extraction. Return code: {ret}.");

            ETABSv1.eUnits actualUnits = sapModel.GetPresentUnits();
            if (actualUnits != EtabsUnitsKnMC)
            {
                throw new InvalidOperationException(
                    $"ETABS reported present units '{actualUnits}' after model snapshot extraction requested '{EtabsUnitsKnMC}'. Snapshot extraction was stopped to avoid inconsistent values.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ETABS units could not be verified for model snapshot extraction. Snapshot extraction was stopped: {ex.Message}",
                ex);
        }
    }
}
