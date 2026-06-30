namespace CSIModellingTools.Features.IfcImport;

public enum EtabsExportTargetMode
{
    AddToExistingModel,
    CreateNewModel
}

public enum IfcEtabsUnits
{
    kN_m_C,
    N_m_C,
    kN_mm_C
}

public enum EtabsExportWarningSeverity
{
    Info,
    Warning,
    Error
}

public enum EtabsExportWarningCategory
{
    Connection,
    Units,
    Material,
    Section,
    Geometry,
    Confidence,
    Export,
    SourceGuid,
    Area
}

public readonly record struct EtabsExportProgress(double Percent, string Stage);

public interface IEtabsFrameExporter
{
    EtabsExportResult ExportFramesToEtabs(IfcImportResult result, EtabsExportOptions options, IProgress<EtabsExportProgress>? progress = null);
}

public interface IEtabsAreaExporter
{
    EtabsExportResult ExportAreasToEtabs(IfcImportResult result, EtabsExportOptions options, IProgress<EtabsExportProgress>? progress = null);
}

public sealed class EtabsExportOptions
{
    public string? EtabsInstanceId { get; set; }
    public EtabsExportTargetMode TargetMode { get; set; } = EtabsExportTargetMode.AddToExistingModel;
    public IfcEtabsUnits EtabsUnits { get; set; } = IfcEtabsUnits.kN_m_C;
    public string DefaultConcreteMaterial { get; set; } = "IFC_DEFAULT_CONCRETE";
    public string DefaultSteelMaterial { get; set; } = "IFC_DEFAULT_STEEL";
    public string DefaultUnknownSectionName { get; set; } = "IFC_DEFAULT_FRAME";
    public string DefaultAreaPropertyName { get; set; } = "IFC_DEFAULT_AREA";
    public bool ExportOnlyHighConfidence { get; set; } = true;
    public bool ExportMediumConfidenceWithWarnings { get; set; }
    public bool SkipUnknownSections { get; set; } = true;
    public bool SkipUnknownMaterials { get; set; } = true;
    public bool PreserveSourceGuid { get; set; } = true;
    public bool ExportSlabAreas { get; set; } = true;
    public bool ExportWallAreas { get; set; } = true;
    public string ExportGroupName { get; set; } = "IFC_STRUCTURAL_FRAME_IMPORT";
}

public sealed class EtabsExportResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int ExportedFrameCount { get; set; }
    public int SkippedFrameCount { get; set; }
    public int ExportedAreaCount { get; set; }
    public int SkippedAreaCount { get; set; }
    public int CreatedMaterialCount { get; set; }
    public int CreatedSectionCount { get; set; }
    public int CreatedAreaPropertyCount { get; set; }
    public List<string> ExportedFrameNames { get; set; } = [];
    public List<string> ExportedAreaNames { get; set; } = [];
    public List<EtabsExportWarning> Warnings { get; set; } = [];
}

public sealed class EtabsExportWarning
{
    public string SourceGuid { get; set; } = "";
    public string SourceName { get; set; } = "";
    public EtabsExportWarningSeverity Severity { get; set; } = EtabsExportWarningSeverity.Warning;
    public string Message { get; set; } = "";
    public EtabsExportWarningCategory Category { get; set; } = EtabsExportWarningCategory.Export;
}
