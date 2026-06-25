namespace CSIModellingTools.Models;

[Serializable]
public sealed class ModelCompareSnapshot
{
    public ModelCompareSnapshotMetadata Metadata { get; set; } = new();
    public List<ModelCompareFrameSnapshot> Frames { get; set; } = [];
    public List<ModelCompareFramePropertySnapshot> FrameProperties { get; set; } = [];
    public List<ModelCompareAreaPropertySnapshot> AreaProperties { get; set; } = [];
    public List<ModelCompareMaterialSnapshot> Materials { get; set; } = [];
}

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

[Serializable]
public sealed class ModelCompareToleranceSettings
{
    public double CoordinateTolerance { get; set; } = 0.001;
    public double LengthTolerance { get; set; } = 0.001;
    public double DimensionTolerance { get; set; } = 0.001;
    public double MaterialPropertyTolerance { get; set; } = 0.001;
}

[Serializable]
public sealed class ModelCompareSnapshotMetadata
{
    public string ProductName { get; set; } = "";
    public string SourceModelFileName { get; set; } = "";
    public DateTimeOffset SnapshotCreatedAt { get; set; } = DateTimeOffset.Now;
    public string Units { get; set; } = "";
}

[Serializable]
public sealed class ModelCompareFrameSnapshot
{
    public string FrameName { get; set; } = "";
    public string Label { get; set; } = "";
    public string Story { get; set; } = "";
    public string PointIName { get; set; } = "";
    public string PointJName { get; set; } = "";
    public double IX { get; set; }
    public double IY { get; set; }
    public double IZ { get; set; }
    public double JX { get; set; }
    public double JY { get; set; }
    public double JZ { get; set; }
    public double Length { get; set; }
    public string SectionName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public List<string> GroupNames { get; set; } = [];
}

[Serializable]
public sealed class ModelCompareFramePropertySnapshot
{
    public string SectionName { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double Depth { get; set; }
    public double Width { get; set; }
    public double FlangeThickness { get; set; }
    public double WebThickness { get; set; }
    public string SummaryText { get; set; } = "";
}

[Serializable]
public sealed class ModelCompareAreaPropertySnapshot
{
    public string PropertyName { get; set; } = "";
    public string AreaType { get; set; } = "";
    public string ShellType { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double Thickness { get; set; }
}

[Serializable]
public sealed class ModelCompareMaterialSnapshot
{
    public string MaterialName { get; set; } = "";
    public string MaterialType { get; set; } = "";
    public double ElasticModulus { get; set; }
    public double PoissonRatio { get; set; }
    public double UnitWeight { get; set; }
    public string DesignSummary { get; set; } = "";
}

public enum ModelCompareChangeType
{
    Added,
    Removed,
    Modified,
    Unchanged
}

public enum ModelCompareObjectType
{
    ModelMetadata,
    Frame,
    FrameProperty,
    AreaProperty,
    Material
}

public enum ModelCompareChangeImportance
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

[Serializable]
public sealed class ModelCompareResultRow
{
    public ModelCompareChangeType ChangeType { get; set; }
    public ModelCompareObjectType ObjectType { get; set; }
    public string ObjectDescription { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public ModelCompareChangeImportance Importance { get; set; } = ModelCompareChangeImportance.Medium;
    public double Confidence { get; set; } = 1.0;
}
