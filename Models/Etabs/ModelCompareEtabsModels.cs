using CSIModellingTools.Models;

namespace CSIModellingTools.Models.Etabs;

[Serializable]
public sealed class ModelCompareSnapshotRequest
{
    public string? EtabsInstanceId { get; set; }
}

[Serializable]
public sealed class ModelCompareSnapshotResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public ModelCompareSnapshot? Snapshot { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public static class ModelCompareEtabsSelectionLimits
{
    public const int MaxObjects = 500;
}

public sealed class ModelCompareEtabsSelectionRequest
{
    public string? EtabsInstanceId { get; set; }
    public List<ModelCompareEtabsSelectionTarget> Targets { get; set; } = [];
}

public sealed class ModelCompareEtabsSelectionTarget
{
    public ModelCompareObjectType ObjectType { get; set; }
    public string ObjectName { get; set; } = "";
}

public sealed class ModelCompareEtabsSelectionFailure
{
    public ModelCompareObjectType ObjectType { get; set; }
    public string ObjectName { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ModelCompareEtabsSelectionResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public string TargetModelFileName { get; set; } = "";
    public List<ModelCompareEtabsSelectionTarget> SelectedTargets { get; set; } = [];
    public List<ModelCompareEtabsSelectionFailure> Failures { get; set; } = [];
}
