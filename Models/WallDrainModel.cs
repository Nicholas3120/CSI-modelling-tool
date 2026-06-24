namespace TrussModelling.Models;

public enum WallDrainShapeMode
{
    OneSidedWall,
    LWall,
    UDrain,
    BoxDrain
}

public enum WallDrainModelingMode
{
    Frame,
    Shell
}

public enum WallDrainLoadKind
{
    Udl,
    Triangular
}

public enum WallDrainLoadDirection
{
    NormalInward,
    GlobalXPositive,
    GlobalXNegative
}

public static class WallDrainPanelGroups
{
    public const string Stem = "Stem";
    public const string LeftWall = "LeftWall";
    public const string RightWall = "RightWall";
    public const string BaseSlab = "BaseSlab";
    public const string TopSlab = "TopSlab";
    public const string Buttress = "Buttress";
    public const string Counterfort = "Counterfort";

    public static IReadOnlyList<string> VerticalWallGroups { get; } =
    [
        Stem,
        LeftWall,
        RightWall
    ];
}

public sealed class WallDrainNode
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public bool IsSupport { get; set; }
}

public sealed class WallDrainShellPanel
{
    public string Id { get; set; } = "";
    public List<string> NodeIds { get; set; } = [];
    public string Group { get; set; } = "";
    public string ShellPropertyName { get; set; } = "";
    public double CentroidX { get; set; }
    public double CentroidY { get; set; }
    public double CentroidZ { get; set; }
    public double LoadSignX { get; set; } = 1.0;
}

public sealed class WallDrainFrameMember
{
    public string Id { get; set; } = "";
    public string StartNodeId { get; set; } = "";
    public string EndNodeId { get; set; } = "";
    public string Group { get; set; } = "";
    public string SectionName { get; set; } = "";
    public double LoadSignX { get; set; } = 1.0;
}

public sealed class WallDrainSurfaceLoad
{
    public string Id { get; set; } = "";
    public WallDrainLoadKind Kind { get; set; } = WallDrainLoadKind.Udl;
    public string LoadPattern { get; set; } = "";
    public WallDrainLoadDirection Direction { get; set; } = WallDrainLoadDirection.NormalInward;
    public List<string> TargetGroups { get; set; } = [];
    public double UniformPressureKnPerM2 { get; set; }
    public double TopPressureKnPerM2 { get; set; }
    public double BottomPressureKnPerM2 { get; set; }
}

public sealed class WallDrainModel
{
    public string StructureId { get; set; } = "WD01";
    public string GroupName { get; set; } = "WPF_WALL_DRAIN_WD01";
    public WallDrainShapeMode ShapeMode { get; set; } = WallDrainShapeMode.LWall;
    public WallDrainModelingMode ModelingMode { get; set; } = WallDrainModelingMode.Frame;
    public double LengthY { get; set; } = 1.0;
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double OriginZ { get; set; }
    public double Height { get; set; } = 3.0;
    public double ClearWidth { get; set; } = 1.5;
    public double ToeLength { get; set; } = 1.0;
    public double HeelLength { get; set; } = 2.0;
    public double LengthMeshSize { get; set; } = 1.0;
    public int HeightDivisions { get; set; } = 4;
    public bool GenerateBaseSlab { get; set; } = true;
    public bool GenerateButtressOrCounterfort { get; set; }
    public bool UseCounterfort { get; set; } = true;
    public double ButtressProjection { get; set; } = 1.0;
    public double ButtressSpacing { get; set; } = 2.0;
    public string WallFrameSectionName { get; set; } = "";
    public string SlabFrameSectionName { get; set; } = "";
    public string ButtressFrameSectionName { get; set; } = "";
    public string WallShellPropertyName { get; set; } = "";
    public string SlabShellPropertyName { get; set; } = "";
    public string ButtressShellPropertyName { get; set; } = "";
    public List<WallDrainNode> Nodes { get; set; } = [];
    public List<WallDrainFrameMember> FrameMembers { get; set; } = [];
    public List<WallDrainShellPanel> ShellPanels { get; set; } = [];
    public List<WallDrainSurfaceLoad> SurfaceLoads { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string ShellGroupName => BuildSubGroup("SHELL");
    public string FrameGroupName => BuildSubGroup("FRAME");
    public string WallGroupName => BuildSubGroup("WALL");
    public string SlabGroupName => BuildSubGroup("SLAB");
    public string ButtressGroupName => BuildSubGroup("BUTTRESS");
    public string LoadGroupName => BuildSubGroup("LOAD_FRAMES");
    public string SupportGroupName => BuildSubGroup("SUPPORT");

    private string BuildSubGroup(string suffix)
    {
        return EtabsNameUtility.BuildSafeName("", $"{GroupName}_{suffix}");
    }
}

public sealed class WallDrainEtabsDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class WallDrainEtabsDataResult
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

public sealed class WallDrainDrawRequest
{
    public string? EtabsInstanceId { get; set; }
    public WallDrainModel Model { get; set; } = new();
    public bool UpdateExistingGroup { get; set; } = true;
    public bool AddAsNew { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double OffsetZ { get; set; }
}

public sealed class WallDrainDrawResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int CreatedShellCount { get; set; }
    public List<string> ShellObjectNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
