namespace CSIModellingTools.Models;

public enum DomeType
{
    SphericalCap
}

public enum DomeShellMeshType
{
    Triangular,
    Quad
}

public enum DomeRingSpacingMode
{
    EqualHeight,
    EqualRadius,
    HybridTopEqualRadius
}

public enum DomeGeometryInputMode
{
    RiseAndCutHeights,
    PartialHeightTopRadius
}

public enum DomeMemberGroup
{
    Ring,
    Radial,
    Diagonal,
    BaseRing,
    CrownRing
}

public sealed class DomeNode
{
    public string Id { get; set; } = "";
    public int RingIndex { get; set; }
    public int SegmentIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class DomeFrameMember
{
    public string Id { get; set; } = "";
    public string StartNodeId { get; set; } = "";
    public string EndNodeId { get; set; } = "";
    public DomeMemberGroup Group { get; set; }
    public string SectionName { get; set; } = "";
}

public sealed class DomeShellPanel
{
    public string Id { get; set; } = "";
    public List<string> NodeIds { get; set; } = [];
    public string ShellPropertyName { get; set; } = "";
}

public sealed class ParametricDomeModel
{
    public string DomeId { get; set; } = "D01";
    public string GroupName { get; set; } = "WPF_DOME_D01";
    public DomeType DomeType { get; set; } = DomeType.SphericalCap;
    public DomeShellMeshType ShellMeshType { get; set; } = DomeShellMeshType.Triangular;
    public DomeGeometryInputMode GeometryInputMode { get; set; } = DomeGeometryInputMode.RiseAndCutHeights;
    public DomeRingSpacingMode RingSpacingMode { get; set; } = DomeRingSpacingMode.EqualHeight;
    public double BaseCenterX { get; set; }
    public double BaseCenterY { get; set; }
    public double BaseElevationZ { get; set; }
    public double BaseRadius { get; set; }
    public double DomeRise { get; set; }
    public double LowerCutHeight { get; set; }
    public double UpperCutHeight { get; set; }
    public double PartialDomeHeight { get; set; }
    public double CrownRingRadius { get; set; }
    public int RingCount { get; set; }
    public int SegmentCount { get; set; }
    public double StartAngleDeg { get; set; }
    public double EndAngleDeg { get; set; }
    public bool Full360 { get; set; } = true;
    public bool GenerateShellPanels { get; set; } = true;
    public bool GenerateRingFrames { get; set; } = true;
    public bool GenerateRadialFrames { get; set; }
    public bool GenerateDiagonalFrames { get; set; }
    public bool GenerateBaseRing { get; set; } = true;
    public bool GenerateCrownRing { get; set; } = true;
    public bool GenerateSupportsAtBase { get; set; }
    public string ShellPropertyName { get; set; } = "";
    public string RingSectionName { get; set; } = "";
    public string RadialSectionName { get; set; } = "";
    public string DiagonalSectionName { get; set; } = "";
    public string BaseRingSectionName { get; set; } = "";
    public string CrownRingSectionName { get; set; } = "";
    public List<DomeNode> Nodes { get; set; } = [];
    public List<DomeFrameMember> FrameMembers { get; set; } = [];
    public List<DomeShellPanel> ShellPanels { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class DomeEtabsDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class DomeEtabsDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<string> FrameSections { get; set; } = [];
    public List<string> ShellProperties { get; set; } = [];
    public List<string> LoadPatterns { get; set; } = [];
    public List<string> LoadCombinations { get; set; } = [];
    public List<string> Stories { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class DomeEtabsDrawRequest
{
    public string? EtabsInstanceId { get; set; }
    public ParametricDomeModel Model { get; set; } = new();
    public bool UpdateExistingGroup { get; set; } = true;
}

public sealed class DomeEtabsDrawResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int CreatedFrameCount { get; set; }
    public int CreatedShellCount { get; set; }
    public List<string> FrameObjectNames { get; set; } = [];
    public List<string> ShellObjectNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
