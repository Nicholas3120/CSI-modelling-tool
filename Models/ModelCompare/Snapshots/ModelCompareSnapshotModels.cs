namespace CSIModellingTools.Models;

public static class ModelCompareSchema
{
    // v4: added joint objects (restraints) and frame end releases.
    // v5: added tool-owned persistent member IDs (frames).
    // v6: added persistent IDs and opening flag for areas.
    public const int CurrentVersion = 6;
}

public static class ModelCompareMemberId
{
    // Prefix that marks a GUID as one this tool assigned (as opposed to a GUID from a Revit/IFC import).
    public const string Prefix = "MCT-";

    public static bool IsToolOwned(string? guid) =>
        !string.IsNullOrWhiteSpace(guid) && guid.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}

public enum ModelCompareSnapshotReadStatus
{
    Unknown,
    NotAttempted,
    Success,
    SuccessWithWarnings,
    Failed
}

[Serializable]
public sealed class ModelCompareSnapshot
{
    public ModelCompareSnapshotMetadata Metadata { get; set; } = new();
    public List<ModelCompareFrameSnapshot> Frames { get; set; } = [];
    public List<ModelCompareAreaObjectSnapshot> Areas { get; set; } = [];
    public List<ModelCompareJointSnapshot> Joints { get; set; } = [];
    public List<ModelCompareFramePropertySnapshot> FrameProperties { get; set; } = [];
    public List<ModelCompareAreaPropertySnapshot> AreaProperties { get; set; } = [];
    public List<ModelCompareMaterialSnapshot> Materials { get; set; } = [];
}

[Serializable]
public sealed class ModelCompareSnapshotMetadata
{
    public int SchemaVersion { get; set; }
    public string ProductName { get; set; } = "";
    public string SourceModelFileName { get; set; } = "";
    public DateTimeOffset SnapshotCreatedAt { get; set; } = DateTimeOffset.Now;
    public string Units { get; set; } = "";
    public string LengthUnit { get; set; } = "";
    public string ForceUnit { get; set; } = "";
    public string StressUnit { get; set; } = "";
    public string UnitWeightUnit { get; set; } = "";
    public string UnitWeightConvention { get; set; } = "";
    public ModelCompareSnapshotReadStatus FramesReadStatus { get; set; }
    public ModelCompareSnapshotReadStatus AreasReadStatus { get; set; }
    public ModelCompareSnapshotReadStatus FramePropertiesReadStatus { get; set; }
    public ModelCompareSnapshotReadStatus AreaPropertiesReadStatus { get; set; }
    public ModelCompareSnapshotReadStatus MaterialsReadStatus { get; set; }
    public ModelCompareSnapshotReadStatus GroupsReadStatus { get; set; }
    public ModelCompareSnapshotReadStatus JointsReadStatus { get; set; }
    public List<string> ExtractionWarnings { get; set; } = [];
}

[Serializable]
public sealed class ModelCompareFrameSnapshot
{
    public string FrameName { get; set; } = "";
    public string Uid { get; set; } = "";
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

    // End releases at the I and J ends, in ETABS DOF order [P, V2, V3, T, M2, M3].
    public bool ReleaseAxialI { get; set; }
    public bool ReleaseShear2I { get; set; }
    public bool ReleaseShear3I { get; set; }
    public bool ReleaseTorsionI { get; set; }
    public bool ReleaseMoment2I { get; set; }
    public bool ReleaseMoment3I { get; set; }
    public bool ReleaseAxialJ { get; set; }
    public bool ReleaseShear2J { get; set; }
    public bool ReleaseShear3J { get; set; }
    public bool ReleaseTorsionJ { get; set; }
    public bool ReleaseMoment2J { get; set; }
    public bool ReleaseMoment3J { get; set; }
}

[Serializable]
public sealed class ModelCompareJointSnapshot
{
    public string PointName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    // Restraint DOFs in ETABS order [UX, UY, UZ, RX, RY, RZ].
    public bool RestraintUX { get; set; }
    public bool RestraintUY { get; set; }
    public bool RestraintUZ { get; set; }
    public bool RestraintRX { get; set; }
    public bool RestraintRY { get; set; }
    public bool RestraintRZ { get; set; }
}

[Serializable]
public sealed class ModelCompareAreaObjectSnapshot
{
    public string AreaName { get; set; } = "";
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    public string Story { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double Thickness { get; set; }
    public bool IsOpening { get; set; }
    public List<ModelComparePointSnapshot> Corners { get; set; } = [];
    public List<string> GroupNames { get; set; } = [];
}

[Serializable]
public sealed class ModelComparePointSnapshot
{
    public string PointName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
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
