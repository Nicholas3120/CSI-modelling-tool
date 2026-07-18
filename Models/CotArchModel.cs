namespace CSIModellingTools.Models;

public enum CotArchProfileType
{
    Parabolic,
    Circular,
    PowerCurve
}

public enum CotArchSupportCondition
{
    Fixed,
    Pinned
}

public enum CotArchMemberReleasePreset
{
    FullyContinuous,
    PinnedBothEnds
}

public enum CotArchMemberKind
{
    Arch,
    VerticalPost,
    UpperBeam,
    TensionTie,
    SupportColumn
}

public static class CotArchMemberGroups
{
    public const string Arch = "Segmented compression arch";
    public const string VerticalPost = "Vertical posts";
    public const string UpperBeam = "Upper horizontal beam";
    public const string TensionTie = "Tension tie";
    public const string SupportColumn = "Support columns";
}

public sealed class CotArchInput
{
    public string ModelPrefix { get; set; } = "TA01";
    public double OriginX { get; set; }
    public double PlaneY { get; set; }
    public double BaseZ { get; set; } = -8.0;
    public double SpringingZ { get; set; }
    public double UpperBeamZ { get; set; } = 12.0;
    public double Span { get; set; } = 40.0;
    public double Rise { get; set; } = 8.0;
    public int PostCount { get; set; } = 9;
    public List<double>? CustomPostStations { get; set; }
    public string CustomPostStationsError { get; set; } = "";
    public int ArchSegmentsPerPostBay { get; set; } = 1;
    public CotArchProfileType ProfileType { get; set; } = CotArchProfileType.Parabolic;
    public double ShapeExponent { get; set; } = 2.0;
    public string ArchSection { get; set; } = "";
    public string PostSection { get; set; } = "";
    public string UpperBeamSection { get; set; } = "";
    public string TieSection { get; set; } = "";
    public string SupportColumnSection { get; set; } = "";
    public bool GenerateAsPlanarModel { get; set; } = true;
    public CotArchSupportCondition SupportCondition { get; set; } = CotArchSupportCondition.Pinned;
    public CotArchMemberReleasePreset ArchReleasePreset { get; set; } = CotArchMemberReleasePreset.FullyContinuous;
    public CotArchMemberReleasePreset PostReleasePreset { get; set; } = CotArchMemberReleasePreset.FullyContinuous;
    public CotArchMemberReleasePreset TieReleasePreset { get; set; } = CotArchMemberReleasePreset.PinnedBothEnds;
    public CotArchMemberReleasePreset BeamReleasePreset { get; set; } = CotArchMemberReleasePreset.FullyContinuous;
    public CotArchMemberReleasePreset SupportColumnReleasePreset { get; set; } = CotArchMemberReleasePreset.FullyContinuous;
}

public sealed class CotArchNode
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Xi { get; set; }
    public bool IsArchNode { get; set; }
    public bool IsPostBottom { get; set; }
    public bool IsPostTop { get; set; }
    public bool IsSpringing { get; set; }
    public bool IsSupportBase { get; set; }
}

public sealed class CotArchMember
{
    public string Id { get; set; } = "";
    public string StartNodeId { get; set; } = "";
    public string EndNodeId { get; set; } = "";
    public string Group { get; set; } = "";
    public CotArchMemberKind Kind { get; set; }
    public string SectionName { get; set; } = "";
    public CotArchMemberReleasePreset ReleasePreset { get; set; } = CotArchMemberReleasePreset.FullyContinuous;
}

public sealed class CotArchModel
{
    public string ModelPrefix { get; set; } = "TA01";
    public string GroupName { get; set; } = "COT_ARCH_TA01";
    public string PointGroupName { get; set; } = "COT_ARCH_TA01_POINTS";
    public string ArchGroupName { get; set; } = "COT_ARCH_TA01_ARCH";
    public string PostGroupName { get; set; } = "COT_ARCH_TA01_POSTS";
    public string UpperBeamGroupName { get; set; } = "COT_ARCH_TA01_UPPER_BEAM";
    public string TieGroupName { get; set; } = "COT_ARCH_TA01_TIE";
    public string SupportColumnGroupName { get; set; } = "COT_ARCH_TA01_SUPPORT_COLUMNS";
    public CotArchInput Input { get; set; } = new();
    public List<CotArchNode> Nodes { get; set; } = [];
    public List<CotArchNode> ArchNodes { get; set; } = [];
    public List<CotArchNode> PostBottomNodes { get; set; } = [];
    public List<CotArchNode> PostTopNodes { get; set; } = [];
    public List<CotArchMember> Members { get; set; } = [];
    public CotArchNode? LeftSpringing { get; set; }
    public CotArchNode? RightSpringing { get; set; }
    public CotArchNode? LeftBase { get; set; }
    public CotArchNode? RightBase { get; set; }
    public int ArchSegmentCount => Members.Count(member => member.Kind == CotArchMemberKind.Arch);
    public int VerticalPostCount => Members.Count(member => member.Kind == CotArchMemberKind.VerticalPost);
    public int UpperBeamSegmentCount => Members.Count(member => member.Kind == CotArchMemberKind.UpperBeam);
    public int TensionTieCount => Members.Count(member => member.Kind == CotArchMemberKind.TensionTie);
    public int SupportColumnCount => Members.Count(member => member.Kind == CotArchMemberKind.SupportColumn);
    public int FrameMemberCount => Members.Count;
}

public sealed class CotArchDrawRequest
{
    public string? EtabsInstanceId { get; set; }
    public CotArchModel Model { get; set; } = new();
    public bool ReplaceExistingStructure { get; set; }
}

public sealed class CotArchDrawResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int FrameCount { get; set; }
    public string GroupName { get; set; } = "";
    public List<string> FrameObjectNames { get; set; } = [];
    public List<string> PointObjectNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class CotArchClearRequest
{
    public string? EtabsInstanceId { get; set; }
    public string ModelPrefix { get; set; } = "";
    public string GroupName { get; set; } = "";
}

public sealed class CotArchGenerationManifest
{
    public string ModelPrefix { get; set; } = "";
    public Guid GenerationId { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string EtabsGroupName { get; set; } = "";
    public List<string> EtabsGroupNames { get; set; } = [];
    public CotArchInput InputSnapshot { get; set; } = new();
    public List<string> CreatedPointNames { get; set; } = [];
    public List<string> ReusedPointNames { get; set; } = [];
    public List<string> CreatedFrameNames { get; set; } = [];
}
