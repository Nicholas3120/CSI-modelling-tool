using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrussModelling.Models;

public sealed class SectionPropertyDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class SectionPropertyDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<EtabsMaterialPropertyRow> Materials { get; set; } = [];
    public List<EtabsFramePropertyRow> FrameProperties { get; set; } = [];
    public List<EtabsAreaPropertyRow> AreaProperties { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class SteelSectionCatalogRequest
{
    public string? EtabsInstanceId { get; set; }
    public string DatabaseFile { get; set; } = "";
    public string ShapeType { get; set; } = "I";
}

public sealed class SteelSectionCatalogResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<string> SectionNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class SteelSectionImportRequest
{
    public string? EtabsInstanceId { get; set; }
    public string PropertyName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string DatabaseFile { get; set; } = "";
    public string DatabaseSectionName { get; set; } = "";
}

public sealed class SteelCatalogSectionRow : INotifyPropertyChanged
{
    private bool _include;
    private string _sectionName = "";
    private string _propertyName = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Include
    {
        get => _include;
        set => SetProperty(ref _include, value);
    }

    public string SectionName
    {
        get => _sectionName;
        set => SetProperty(ref _sectionName, value ?? "");
    }

    public string PropertyName
    {
        get => _propertyName;
        set => SetProperty(ref _propertyName, value ?? "");
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class SectionPropertyUpdateResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
}

public sealed class MaterialPropertyUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
    public string MaterialType { get; set; } = "Concrete";
    public double ElasticModulusMpa { get; set; } = 30000.0;
    public double PoissonRatio { get; set; } = 0.2;
    public double ThermalExpansion { get; set; } = 0.0000099;
    public double UnitWeightKnPerM3 { get; set; } = 24.0;
    public double ConcreteFcMpa { get; set; } = 30.0;
    public double SteelFyMpa { get; set; } = 345.0;
    public double SteelFuMpa { get; set; } = 450.0;
}

public sealed class FramePropertyUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
    public string ShapeType { get; set; } = "Concrete Rectangular";
    public string MaterialName { get; set; } = "";
    public string SectionRole { get; set; } = "Beam / General";
    public double Depth { get; set; } = 0.5;
    public double Width { get; set; } = 0.3;
    public double FlangeThickness { get; set; } = 0.016;
    public double WebThickness { get; set; } = 0.01;
    public double WallThickness { get; set; } = 0.01;
}

public sealed class AreaPropertyUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
    public string AreaType { get; set; } = "Slab";
    public string SlabType { get; set; } = "Slab";
    public string ShellType { get; set; } = "ShellThin";
    public string MaterialName { get; set; } = "";
    public double Thickness { get; set; } = 0.15;
}

public enum TaperedSectionType
{
    ISection,
    TSection,
    TubeSection,
    USection
}

public enum TaperedBaseSectionKind
{
    ISection,
    TubeSection
}

public enum TaperedTipEnd
{
    IEnd,
    JEnd
}

public enum TaperedReferenceLine
{
    KeepTopFlangeStraight,
    KeepCentroidLineStraight,
    KeepBottomFlangeStraight
}

public sealed class TaperedSteelBaseSectionRequest
{
    public string? EtabsInstanceId { get; set; }
    public string SectionName { get; set; } = "";
}

public sealed class TaperedSteelSelectionResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public TaperedSteelSelection? Selection { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class TaperedSteelApplyRequest
{
    public string? EtabsInstanceId { get; set; }
    public TaperedSteelSelection Selection { get; set; } = new();
    public double TipDepthM { get; set; } = 0.2;
    public TaperedTipEnd TipEnd { get; set; } = TaperedTipEnd.JEnd;
    public TaperedSectionType TaperType { get; set; } = TaperedSectionType.ISection;
    public TaperedReferenceLine ReferenceLine { get; set; } = TaperedReferenceLine.KeepTopFlangeStraight;
    public bool FullMemberLength { get; set; } = true;
    public int StationCount { get; set; } = 5;
}

public sealed class TaperedSteelApplyResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public TaperedSteelGenerationPreview? Preview { get; set; }
    public List<string> CreatedOrReusedSections { get; set; } = [];
    public List<string> AssignedFrameNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class TaperedSteelSelection
{
    public List<TaperedSteelFrameInfo> Frames { get; set; } = [];
    public TaperedSteelSectionGeometry BaseGeometry { get; set; } = new();
    public string BaseSectionName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double LengthM { get; set; }
}

public sealed class TaperedSteelFrameInfo
{
    public string FrameName { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string PointI { get; set; } = "";
    public string PointJ { get; set; } = "";
    public double LengthM { get; set; }
    public double IX { get; set; }
    public double IY { get; set; }
    public double IZ { get; set; }
    public double JX { get; set; }
    public double JY { get; set; }
    public double JZ { get; set; }
    public double LocalAxesAngleDegrees { get; set; }
}

public sealed class TaperedSteelSectionGeometry
{
    public string SectionName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public TaperedBaseSectionKind SectionKind { get; set; } = TaperedBaseSectionKind.ISection;
    public double DepthM { get; set; }
    public double TopFlangeWidthM { get; set; }
    public double TopFlangeThicknessM { get; set; }
    public double WebThicknessM { get; set; }
    public double BottomFlangeWidthM { get; set; }
    public double BottomFlangeThicknessM { get; set; }
}

public sealed class TaperedSteelGenerationPreview
{
    public int SelectedFrameCount { get; set; }
    public string BaseSectionName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double OriginalDepthM { get; set; }
    public double TipDepthM { get; set; }
    public TaperedTipEnd TipEnd { get; set; }
    public TaperedSectionType TaperType { get; set; }
    public TaperedReferenceLine ReferenceLine { get; set; }
    public double PreviewLengthM { get; set; } = 6.0;
    public string NonPrismaticSectionName { get; set; } = "";
    public TaperedSteelSectionGeometry BaseGeometry { get; set; } = new();
    public List<TaperedSteelStationPreview> Stations { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class TaperedSteelStationPreview
{
    public int Index { get; set; }
    public double PositionRatio { get; set; }
    public double PositionPercent => PositionRatio * 100.0;
    public double DepthM { get; set; }
    public double DepthMm => DepthM * 1000.0;
    public string SectionName { get; set; } = "";
}

public sealed class SectionPropertyDeleteRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class EtabsMaterialPropertyRow
{
    public string Name { get; set; } = "";
    public string MaterialType { get; set; } = "";
    public double ElasticModulusMpa { get; set; }
    public double PoissonRatio { get; set; }
    public double UnitWeightKnPerM3 { get; set; }
    public string DesignSummary { get; set; } = "";
    public bool IsPendingNew { get; set; }
}

public sealed class EtabsFramePropertyRow
{
    public string Name { get; set; } = "";
    public string ShapeType { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double DepthMm { get; set; }
    public double WidthMm { get; set; }
    public double FlangeThicknessMm { get; set; }
    public double WebThicknessMm { get; set; }
    public string SectionSummary { get; set; } = "";
    public bool IsPendingNew { get; set; }
}

public sealed class EtabsAreaPropertyRow
{
    public string Name { get; set; } = "";
    public string AreaType { get; set; } = "";
    public string ShellType { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double Thickness { get; set; }
    public double ThicknessMm
    {
        get => Thickness * 1000.0;
        set => Thickness = value / 1000.0;
    }
    public bool IsPendingNew { get; set; }
}
