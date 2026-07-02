namespace CSIModellingTools.Features.IfcImport;

public enum IfcImportCoordinateOriginMode
{
    PreserveIfcCoordinates,
    ResetToFirstImportedPoint
}

public enum IfcImportUnitMode
{
    ConvertToMetres,
    PreserveIfcProjectUnits
}

public enum IfcRecognitionMethod
{
    Axis,
    SweptSolid,
    Inferred,
    Unknown
}

public enum IfcRecognitionConfidence
{
    High,
    Medium,
    Low
}

public enum IfcSectionShapeType
{
    Rectangle,
    Circle,
    ISection,
    Unknown
}

public enum IfcImportWarningSeverity
{
    Info,
    Warning,
    Error
}

public enum IfcImportWarningCategory
{
    Geometry,
    Section,
    Material,
    Placement,
    Unsupported,
    Cleanup,
    Duplicate,
    Connectivity,
    Storey,
    Opening,
    Boundary
}

// Progress payload reported during import. IsDeterminate is false for phases whose
// duration is unknown up front (opening/parsing the file); true once element counts
// are known and Percent is meaningful.
public readonly record struct IfcImportProgress(double Percent, string Stage, bool IsDeterminate);

public sealed class IfcImportOptions
{
    public bool IncludeBeams { get; set; } = true;
    public bool IncludeColumns { get; set; } = true;
    public bool IncludeSlabs { get; set; }
    public bool IncludeWalls { get; set; }
    public bool IncludeStructuralSurfaceMembers { get; set; }
    public bool EnableAdvancedGeometryRecognition { get; set; }
    public bool RecoverMeshGeometry { get; set; } = true;

    // Structural walls only: the IFC has ~17k walls with no LoadBearing flag, most of which are
    // partitions. Use thickness as the structural proxy (structural walls here are 200-700mm,
    // partitions are thinner) so partitions do not flood the analysis model.
    public bool StructuralWallsOnly { get; set; } = true;
    public double MinimumStructuralWallThickness { get; set; } = 0.140;

    public bool ApplyFrameConditioning { get; set; } = true;
    public double FrameConditioningMergeTolerance { get; set; } = 0.075;
    public double NodeSnapTolerance { get; set; } = 0.020;
    public double StoreyElevationTolerance { get; set; } = 0.050;
    public double DuplicateFrameTolerance { get; set; } = 0.020;
    public double DuplicateSectionTolerance { get; set; } = 0.001;
    public double ShortMemberMinimumLength { get; set; } = 0.300;
    public double ConnectivityTolerance { get; set; } = 0.020;
    public IfcImportCoordinateOriginMode CoordinateOriginReset { get; set; } = IfcImportCoordinateOriginMode.PreserveIfcCoordinates;
    public IfcImportUnitMode UnitHandling { get; set; } = IfcImportUnitMode.ConvertToMetres;
}

public sealed class IfcImportResult
{
    public List<AnalyticalFrameElement> Frames { get; set; } = [];
    public List<AnalyticalAreaElement> Areas { get; set; } = [];
    public List<IfcStoreyLevel> StoreyLevels { get; set; } = [];
    public List<IfcImportWarning> Warnings { get; set; } = [];
    public List<SkippedIfcElement> SkippedElements { get; set; } = [];
    public List<string> CleanupActions { get; set; } = [];
    public IfcCoordinateOffsetInfo CoordinateOffset { get; set; } = new();
    public int TotalIfcElementsScanned { get; set; }
    public int BeamCount { get; set; }
    public int ColumnCount { get; set; }
    public int SlabCount { get; set; }
    public int WallCount { get; set; }
    public int StructuralSurfaceMemberCount { get; set; }
    public int ImportedCount { get; set; }
    public int ImportedAreaCount { get; set; }
    public int SkippedCount { get; set; }
    public int WarningCount { get; set; }
}

public sealed class IfcStoreyLevel
{
    public string Name { get; set; } = "";
    public double Elevation { get; set; }
}

public sealed class IfcCoordinateOffsetInfo
{
    public bool Applied { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string Message { get; set; } = "";
}

public sealed class AnalyticalFrameElement
{
    public string SourceGuid { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string IfcType { get; set; } = "";
    public AnalyticalPoint StartPoint { get; set; } = new();
    public AnalyticalPoint EndPoint { get; set; } = new();
    public SectionInfo SectionInfo { get; set; } = new();
    public string SectionName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string StoreyName { get; set; } = "";
    public IfcRecognitionMethod RecognitionMethod { get; set; } = IfcRecognitionMethod.Unknown;
    public IfcRecognitionConfidence Confidence { get; set; } = IfcRecognitionConfidence.Low;
    public List<string> Warnings { get; set; } = [];
}

public sealed class AnalyticalAreaElement
{
    public string SourceGuid { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string IfcType { get; set; } = "";
    public List<AnalyticalPoint> BoundaryPoints { get; set; } = [];
    public double Thickness { get; set; }
    public string MaterialName { get; set; } = "";
    public string StoreyName { get; set; } = "";
    public IfcRecognitionMethod RecognitionMethod { get; set; } = IfcRecognitionMethod.Unknown;
    public IfcRecognitionConfidence Confidence { get; set; } = IfcRecognitionConfidence.Low;
    public List<string> Warnings { get; set; } = [];
}

public sealed class AnalyticalPoint
{
    // Coordinates are stored in metres when IfcImportOptions.UnitHandling is ConvertToMetres.
    // When PreserveIfcProjectUnits is selected, coordinates remain in the source IFC length unit.
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public AnalyticalPoint Clone()
    {
        return new AnalyticalPoint { X = X, Y = Y, Z = Z };
    }
}

public sealed class SectionInfo
{
    public string SectionName { get; set; } = "";
    public IfcSectionShapeType ShapeType { get; set; } = IfcSectionShapeType.Unknown;
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Diameter { get; set; }
    public double FlangeWidth { get; set; }
    public double FlangeThickness { get; set; }
    public double WebThickness { get; set; }
    public string OriginalIfcProfileType { get; set; } = "";
}

public sealed class IfcImportWarning
{
    public string SourceGuid { get; set; } = "";
    public string SourceName { get; set; } = "";
    public IfcImportWarningSeverity Severity { get; set; } = IfcImportWarningSeverity.Warning;
    public IfcImportWarningCategory Category { get; set; } = IfcImportWarningCategory.Geometry;
    public string Message { get; set; } = "";
}

public sealed class SkippedIfcElement
{
    public string SourceGuid { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string IfcType { get; set; } = "";
    public string Reason { get; set; } = "";
}
