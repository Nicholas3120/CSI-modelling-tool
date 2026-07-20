namespace CSIModellingTools.Models;

public enum CityMemberKind
{
    Frame,
    Cable,
    Tie
}

public enum CityTopChordLoadType
{
    None,
    Udl,
    PointLoadAtJoints
}

public enum CityMemberReleasePreset
{
    FullyContinuous,
    PinnedBothEnds
}

public static class CityMemberGroups
{
    public const string TopChord = "Top chord";
    public const string MidRail = "Intermediate rail";
    public const string BottomChord = "Bottom chord";
    public const string VerticalPost = "Vertical posts";
    public const string Tower = "Towers";
    public const string SideFrame = "Side frames";
    public const string InternalCable = "Internal cable fans";
    public const string Backstay = "External backstays";
    public const string GlobalTie = "Global lower tie";
}

public sealed class CityOfTomorrowInput
{
    public string StructureId { get; set; } = "VFR_01";
    public double ClearSpanL { get; set; } = 100.0;
    public int PanelsPerHalfN { get; set; } = 5;
    public double VierendeelDepthH { get; set; } = 18.0;
    public double BottomChordLevelZ { get; set; } = 20.0;
    public double MidRailRatio { get; set; } = 0.50;
    public double TieLevelZ { get; set; } = 2.0;
    public double ExternalAnchorWidth { get; set; } = 10.0;
    public double ExternalSideFrameHeight { get; set; } = 8.0;
    public double PileCapLevelZ { get; set; }
    public string TopChordSection { get; set; } = "";
    public string MidRailSection { get; set; } = "";
    public string BottomChordSection { get; set; } = "";
    public string VerticalPostSection { get; set; } = "";
    public string TowerSection { get; set; } = "";
    public string SideFrameSection { get; set; } = "";
    public string SideVerticalSection { get; set; } = "";
    public string SideX1Section { get; set; } = "";
    public string SideX2Section { get; set; } = "";
    public string CableSection { get; set; } = "";
    public string TieCableSection { get; set; } = "";
    public CityMemberReleasePreset TopChordReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
    public CityMemberReleasePreset MidRailReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
    public CityMemberReleasePreset BottomChordReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
    public CityMemberReleasePreset VerticalPostReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
    public CityMemberReleasePreset TowerReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
    public CityMemberReleasePreset SideFrameReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
    public CityMemberReleasePreset SideVerticalReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
    public CityMemberReleasePreset SideX1ReleasePreset { get; set; } = CityMemberReleasePreset.PinnedBothEnds;
    public CityMemberReleasePreset SideX2ReleasePreset { get; set; } = CityMemberReleasePreset.PinnedBothEnds;
    public CityMemberReleasePreset CableReleasePreset { get; set; } = CityMemberReleasePreset.PinnedBothEnds;
    public CityMemberReleasePreset TieReleasePreset { get; set; } = CityMemberReleasePreset.PinnedBothEnds;
    public CityTopChordLoadType TopChordLoadType { get; set; } = CityTopChordLoadType.None;
    public string TopChordLoadPattern { get; set; } = "";
    public double TopChordUdlKnPerM { get; set; }
    public double TopChordPointLoadKn { get; set; }
}

public sealed class CityNode
{
    public string Key { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public bool IsSupport { get; set; }
    public bool IsPrimaryJoint { get; set; }
}

public sealed class CityMember
{
    public string Id { get; set; } = "";
    public string StartNodeKey { get; set; } = "";
    public string EndNodeKey { get; set; } = "";
    public string Group { get; set; } = "";
    public CityMemberKind Kind { get; set; }
    public string SectionName { get; set; } = "";
    public bool IsTensionOnly { get; set; }
    public bool CanUseTensionSection { get; set; }
    public CityMemberReleasePreset ReleasePreset { get; set; } = CityMemberReleasePreset.FullyContinuous;
}

public sealed class CityOfTomorrowModel
{
    public string StructureId { get; set; } = "VFR_01";
    public string GroupName { get; set; } = "GEN_VIERENDEEL_VFR_01";
    public CityOfTomorrowInput Input { get; set; } = new();
    public List<CityNode> Nodes { get; set; } = [];
    public List<CityMember> Members { get; set; } = [];
    public int TotalPanels => Math.Max(0, 2 * Input.PanelsPerHalfN);
    public double PanelWidth => TotalPanels == 0 ? 0 : Input.ClearSpanL / TotalPanels;
    public int FrameMemberCount => Members.Count(member => !member.IsTensionOnly);
    public int TensionOnlyMemberCount => Members.Count(member => member.IsTensionOnly);
    public int InternalCableCount => Members.Count(member =>
        string.Equals(member.Group, CityMemberGroups.InternalCable, StringComparison.OrdinalIgnoreCase));
}

public sealed class CityOfTomorrowDrawRequest
{
    public string? EtabsInstanceId { get; set; }
    public CityOfTomorrowModel Model { get; set; } = new();
    public bool ReplaceExistingStructure { get; set; }
}

public sealed class CityOfTomorrowLoadUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public CityOfTomorrowModel Model { get; set; } = new();
}

public sealed class CityOfTomorrowDrawResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int FrameCount { get; set; }
    public int TensionOnlyCount { get; set; }
    public string GroupName { get; set; } = "";
    public List<string> ObjectNames { get; set; } = [];
    public List<CityAppliedTopChordLoad> AppliedTopChordLoads { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class CityAppliedTopChordLoad
{
    public string LoadPattern { get; set; } = "";
    public string LoadType { get; set; } = "";
    public string ValueText { get; set; } = "";
    public string TargetText { get; set; } = "";
}

public sealed class CityOfTomorrowClearRequest
{
    public string? EtabsInstanceId { get; set; }
    public string GroupName { get; set; } = "";
    public string StructureId { get; set; } = "";
}

public sealed class CityOfTomorrowGenerationManifest
{
    public string StructureId { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; }
    public string EtabsGroupName { get; set; } = "";
    public CityOfTomorrowInput InputSnapshot { get; set; } = new();
    public List<string> EtabsPointNames { get; set; } = [];
    public List<string> EtabsFrameNames { get; set; } = [];
}
