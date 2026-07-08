namespace CSIModellingTools.Models;

[Serializable]
public sealed class ModelCompareToleranceSettings
{
    public double CoordinateTolerance { get; set; } = 0.001;
    public double MovementTolerance { get; set; } = 0.5;
    public double MovementSearchDistance { get; set; } = 0.5;
    public double MovedFrameLengthTolerance { get; set; } = 0.001;
    public double MovedFrameOrientationToleranceDegrees { get; set; } = 0.1;
    public double MovedFrameElevationTolerance { get; set; } = 0.05;
    public double LengthTolerance { get; set; } = 0.001;
    public double DimensionTolerance { get; set; } = 0.001;
    public double MaterialPropertyTolerance { get; set; } = 0.001;
    public ModelCompareConfidenceLevel MinimumMovedFrameConfidence { get; set; } = ModelCompareConfidenceLevel.Medium;
}

public sealed class ModelCompareComparisonResult
{
    public List<ModelCompareResultRow> Differences { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public bool FrameComparisonAvailable { get; set; } = true;

    // Per-category outcome counts (added/removed/modified/unchanged/total), populated for each category that
    // was actually compared. Drives the summary and the project-explorer tree; a category that was skipped or
    // failed has no entry here.
    public Dictionary<ModelCompareObjectType, ModelCompareCategorySummary> CategorySummaries { get; } = [];
}

public sealed class ModelCompareCategorySummary
{
    public int Added { get; set; }
    public int Removed { get; set; }
    public int Modified { get; set; }
    public int Unchanged { get; set; }
    public int Total => Added + Removed + Modified + Unchanged;
}

public enum ModelCompareChangeType
{
    Added,
    Removed,
    Moved,
    Modified,
    Unchanged
}

public enum ModelCompareObjectType
{
    ModelMetadata,
    Frame,
    Area,
    Joint,
    FrameProperty,
    AreaProperty,
    Material
}

public enum ModelCompareMemberType
{
    NotApplicable,
    Beam,
    Column,
    Brace,
    Area,
    Other
}

public enum ModelCompareChangeImportance
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

public enum ModelCompareConfidenceLevel
{
    Low,
    Medium,
    High
}

public enum ModelCompareMatchMethod
{
    NotApplicable,
    PersistentId,
    ExactCoordinates,
    ReversedIJ,
    SameFrameName,
    NearGeometry,
    ExactAreaGeometry,
    Unmatched
}

[Serializable]
public class ModelCompareResultRow
{
    public ModelCompareChangeType ChangeType { get; set; }
    public ModelCompareObjectType ObjectType { get; set; }
    public ModelCompareMemberType MemberType { get; set; } = ModelCompareMemberType.NotApplicable;
    public string Story { get; set; } = "";
    public string ObjectDescription { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public ModelCompareChangeImportance Importance { get; set; } = ModelCompareChangeImportance.Medium;
    public double Confidence { get; set; } = 1.0;
    public ModelCompareConfidenceLevel ConfidenceLevel { get; set; } = ModelCompareConfidenceLevel.High;
    public ModelCompareMatchMethod MatchMethod { get; set; }
    public string MatchReason { get; set; } = "";
    public double? CoordinateDifference { get; set; }
    public double? MovementDistance { get; set; }
    public double? LengthDifference { get; set; }
    public double? OrientationDifferenceDegrees { get; set; }
    public string SearchText { get; set; } = "";
    public string OldEtabsObjectName { get; set; } = "";
    public string NewEtabsObjectName { get; set; } = "";
    public string OldObjectLocation { get; set; } = "";
    public string NewObjectLocation { get; set; } = "";
    public string OldLabel { get; set; } = "";
    public string NewLabel { get; set; } = "";
    public string OldUid { get; set; } = "";
    public string NewUid { get; set; } = "";
}
