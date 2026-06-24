namespace TrussModelling.Models;

public enum HydroLoadHeightMode
{
    FullWallHeight,
    WaterTableToWallBottom,
    CustomTopBottom
}

public enum HydroLoadTargetMode
{
    SelectedEtabsShells,
    EtabsGroup,
    ShellNameList
}

public enum HydroLoadDirection
{
    GlobalX,
    GlobalY,
    GlobalZ
}

public enum HydroLoadSign
{
    Positive,
    Negative
}

public enum HydroLoadRestrictionOption
{
    UseAllValues,
    ZeroNegativeValues,
    ZeroPositiveValues
}

public enum HydroLoadAssignmentOption
{
    ReplaceExisting,
    AddToExisting,
    DeleteExisting
}

public sealed class HydrostaticShellLoadDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class HydrostaticShellLoadDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<string> LoadPatterns { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class HydrostaticShellLoadInput
{
    public string? EtabsInstanceId { get; set; }
    public string LoadPatternName { get; set; } = "WATER";
    public bool CreateLoadPatternIfMissing { get; set; } = true;
    public HydroLoadTargetMode TargetMode { get; set; } = HydroLoadTargetMode.SelectedEtabsShells;
    public string GroupName { get; set; } = "";
    public List<string> ShellNames { get; set; } = [];
    public HydroLoadHeightMode HeightMode { get; set; } = HydroLoadHeightMode.FullWallHeight;
    public double UserZTop { get; set; }
    public double UserZBottom { get; set; }
    public double GammaKnPerM3 { get; set; } = 9.81;
    public double SurchargeKnPerM2 { get; set; }
    public HydroLoadDirection Direction { get; set; } = HydroLoadDirection.GlobalX;
    public HydroLoadSign Sign { get; set; } = HydroLoadSign.Negative;
    public HydroLoadRestrictionOption RestrictionOption { get; set; } = HydroLoadRestrictionOption.UseAllValues;
    public HydroLoadAssignmentOption AssignmentOption { get; set; } = HydroLoadAssignmentOption.ReplaceExisting;
}

public sealed record HydrostaticLoadCoefficients(double A, double B, double C, double D);

public sealed class HydrostaticShellLoadPreview
{
    public string LoadPatternName { get; set; } = "";
    public HydroLoadDirection Direction { get; set; }
    public HydroLoadSign Sign { get; set; }
    public HydroLoadRestrictionOption RestrictionOption { get; set; }
    public HydroLoadAssignmentOption AssignmentOption { get; set; }
    public double ZTop { get; set; }
    public double ZBottom { get; set; }
    public double Height { get; set; }
    public double GammaKnPerM3 { get; set; }
    public double SurchargeKnPerM2 { get; set; }
    public double PMaxKnPerM2 { get; set; }
    public double A { get; set; }
    public double B { get; set; }
    public double C { get; set; }
    public double D { get; set; }
    public double QTopKnPerM2 { get; set; }
    public double QBottomKnPerM2 { get; set; }
    public int ShellCount { get; set; }
    public List<string> ShellNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class HydrostaticShellLoadPreviewResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public HydrostaticShellLoadPreview? Preview { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class HydrostaticShellLoadAssignResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public HydrostaticShellLoadPreview? Preview { get; set; }
    public int AppliedCount { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class HydrostaticShellArea
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Story { get; set; } = "";
    public List<HydrostaticShellVertex> Vertices { get; set; } = [];
}

public sealed class HydrostaticShellVertex
{
    public string PointName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class HydrostaticPreviewRow
{
    public string Item { get; set; } = "";
    public string Value { get; set; } = "";
}
