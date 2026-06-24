namespace TrussModelling.Models;

public enum RailingBaseRestraintType
{
    Fixed,
    Pinned
}

public enum RailingLoadDirection
{
    GlobalX,
    GlobalY
}

public enum RailingLoadType
{
    LineLoad,
    PointLoad
}

public static class SteelRailingMemberGroups
{
    public const string Post = "Post";
    public const string TopRail = "TopRail";
    public const string MidRail = "MidRail";
    public const string BottomRail = "BottomRail";

    public static IReadOnlyList<string> All { get; } =
    [
        Post,
        TopRail,
        MidRail,
        BottomRail
    ];
}

public sealed class SteelRailingNode
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double PreviewX { get; set; }
    public double PreviewZ { get; set; }
    public bool IsBaseNode { get; set; }
    public bool IsTopNode { get; set; }
    public bool IsLoadReferenceNode { get; set; }
}

public sealed class SteelRailingMember
{
    public string Id { get; set; } = "";
    public string StartNodeId { get; set; } = "";
    public string EndNodeId { get; set; } = "";
    public string Group { get; set; } = "";
    public string SectionName { get; set; } = "";
}

public sealed class SteelRailingLoad
{
    public string Id { get; set; } = "";
    public string LoadPattern { get; set; } = "";
    public RailingLoadType LoadType { get; set; } = RailingLoadType.LineLoad;
    public string TargetGroup { get; set; } = SteelRailingMemberGroups.TopRail;
    public RailingLoadDirection Direction { get; set; } = RailingLoadDirection.GlobalY;
    public double MagnitudeKnPerM { get; set; }
    public double MagnitudeKn { get; set; }
    public double PointHeight { get; set; }
    public List<string> TargetNodeIds { get; set; } = [];
}

public sealed class SteelRailingSupport
{
    public string NodeId { get; set; } = "";
    public RailingBaseRestraintType RestraintType { get; set; } = RailingBaseRestraintType.Fixed;

    public bool[] Restraints => RestraintType == RailingBaseRestraintType.Fixed
        ? [true, true, true, true, true, true]
        : [true, true, true, false, false, false];
}

public sealed class SteelRailingModel
{
    public string RailingId { get; set; } = "R01";
    public string GroupName { get; set; } = "WPF_RAILING_R01";
    public int SpanCount { get; set; } = 3;
    public int PostCount => SpanCount + 1;
    public double PostSpacing { get; set; } = 1.2;
    public double RailingHeight { get; set; } = 1.1;
    public double BaseElevation { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double Length => SpanCount * PostSpacing;
    public bool GenerateMidRails { get; set; } = true;
    public int MidRailCount { get; set; } = 1;
    public bool GenerateBottomRail { get; set; }
    public RailingBaseRestraintType BaseRestraintType { get; set; } = RailingBaseRestraintType.Fixed;
    public Dictionary<string, string> SectionAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SteelRailingNode> Nodes { get; set; } = [];
    public List<SteelRailingMember> Members { get; set; } = [];
    public List<SteelRailingLoad> Loads { get; set; } = [];
    public List<SteelRailingSupport> Supports { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string PostGroupName => BuildRailingGroupName("POSTS");
    public string TopRailGroupName => BuildRailingGroupName("TOP_RAIL");
    public string MidRailGroupName => BuildRailingGroupName("MID_RAIL");
    public string BottomRailGroupName => BuildRailingGroupName("BOTTOM_RAIL");
    public string LoadPointGroupName => BuildRailingGroupName("LOAD_POINTS");

    private string BuildRailingGroupName(string suffix)
    {
        return EtabsNameUtility.BuildSafeName("", $"{GroupName}_{suffix}");
    }
}

public sealed class SteelRailingEtabsDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class SteelRailingEtabsDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<string> FrameSections { get; set; } = [];
    public List<string> LoadPatterns { get; set; } = [];
    public List<string> LoadCombinations { get; set; } = [];
    public List<string> Stories { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class SteelRailingDrawRequest
{
    public string? EtabsInstanceId { get; set; }
    public SteelRailingModel Model { get; set; } = new();
    public bool UpdateExistingGroup { get; set; } = true;
}

public sealed class SteelRailingDrawResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int CreatedFrameCount { get; set; }
    public List<GeneratedEtabsFrame> Frames { get; set; } = [];
    public List<string> FrameObjectNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
