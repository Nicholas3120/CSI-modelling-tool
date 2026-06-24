using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class SteelRailingOptions
{
    public string RailingId { get; set; } = "R01";
    public int SpanCount { get; set; } = 3;
    public double PostSpacing { get; set; } = 1.2;
    public double RailingHeight { get; set; } = 1.1;
    public double BaseElevation { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public bool GenerateMidRails { get; set; } = true;
    public int MidRailCount { get; set; } = 1;
    public double MidRailElevation { get; set; } = 0.55;
    public bool GenerateBottomRail { get; set; }
    public double BottomRailElevation { get; set; } = 0.1;
    public RailingBaseRestraintType BaseRestraintType { get; set; } = RailingBaseRestraintType.Fixed;
    public Dictionary<string, string> SectionAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ApplyTopRailLoad { get; set; } = true;
    public string LoadPattern { get; set; } = "";
    public RailingLoadType LoadType { get; set; } = RailingLoadType.LineLoad;
    public string LoadTargetGroup { get; set; } = SteelRailingMemberGroups.TopRail;
    public RailingLoadDirection LoadDirection { get; set; } = RailingLoadDirection.GlobalY;
    public double HorizontalLoadKnPerM { get; set; } = 0.75;
    public double HorizontalPointLoadKn { get; set; } = 1.0;
    public double PointLoadHeight { get; set; } = 1.1;
}

public sealed class SteelRailingGenerator
{
    public SteelRailingModel Generate(SteelRailingOptions options)
    {
        string railingId = EtabsNameUtility.BuildSafeName("", options.RailingId, 24);
        int spanCount = Math.Max(3, options.SpanCount);
        double spacing = SanitizePositive(options.PostSpacing, 1.2);
        double height = SanitizePositive(options.RailingHeight, 1.1);
        double startX = double.IsFinite(options.StartX) ? options.StartX : 0;
        double startY = double.IsFinite(options.StartY) ? options.StartY : 0;
        double baseElevation = double.IsFinite(options.BaseElevation) ? options.BaseElevation : 0;
        bool pointLoadOnPosts = options.ApplyTopRailLoad && options.LoadType == RailingLoadType.PointLoad;
        double pointLoadHeight = ClampRailElevation(options.PointLoadHeight, height, height);

        var model = new SteelRailingModel
        {
            RailingId = railingId,
            GroupName = EtabsNameUtility.BuildSafeName("WPF_RAILING_", railingId),
            SpanCount = spanCount,
            PostSpacing = spacing,
            RailingHeight = height,
            BaseElevation = baseElevation,
            StartX = startX,
            StartY = startY,
            GenerateMidRails = options.GenerateMidRails,
            MidRailCount = Math.Max(0, options.MidRailCount),
            GenerateBottomRail = options.GenerateBottomRail,
            BaseRestraintType = options.BaseRestraintType,
            SectionAssignments = new Dictionary<string, string>(options.SectionAssignments, StringComparer.OrdinalIgnoreCase)
        };

        if (options.SpanCount < 3)
            model.Warnings.Add("Railing span count was raised to the minimum of 3 spans.");

        var nodesById = new Dictionary<string, SteelRailingNode>(StringComparer.OrdinalIgnoreCase);
        int postCount = spanCount + 1;
        for (int index = 0; index < postCount; index++)
        {
            double station = index * spacing;
            double x = startX + station;

            AddNode(model, nodesById, new SteelRailingNode
            {
                Id = BaseNodeId(index),
                X = x,
                Y = startY,
                Z = baseElevation,
                PreviewX = station,
                PreviewZ = 0,
                IsBaseNode = true
            });

            AddNode(model, nodesById, new SteelRailingNode
            {
                Id = TopNodeId(index),
                X = x,
                Y = startY,
                Z = baseElevation + height,
                PreviewX = station,
                PreviewZ = height,
                IsTopNode = true,
                IsLoadReferenceNode = options.ApplyTopRailLoad &&
                    options.LoadType == RailingLoadType.LineLoad &&
                    string.Equals(options.LoadTargetGroup, SteelRailingMemberGroups.TopRail, StringComparison.OrdinalIgnoreCase)
            });
        }

        if (pointLoadOnPosts)
        {
            for (int index = 0; index < postCount; index++)
            {
                double station = index * spacing;
                AddNode(model, nodesById, new SteelRailingNode
                {
                    Id = PostLoadNodeId(index),
                    X = startX + station,
                    Y = startY,
                    Z = baseElevation + pointLoadHeight,
                    PreviewX = station,
                    PreviewZ = pointLoadHeight,
                    IsLoadReferenceNode = true
                });
            }
        }

        List<(int LevelIndex, double Elevation)> midLevels = BuildMidRailLevels(options, height);
        foreach ((int levelIndex, double elevation) in midLevels)
        {
            for (int index = 0; index < postCount; index++)
            {
                double station = index * spacing;
                AddNode(model, nodesById, new SteelRailingNode
                {
                    Id = MidNodeId(levelIndex, index),
                    X = startX + station,
                    Y = startY,
                    Z = baseElevation + elevation,
                    PreviewX = station,
                    PreviewZ = elevation
                });
            }
        }

        double bottomElevation = ClampRailElevation(options.BottomRailElevation, height, 0.1);
        if (options.GenerateBottomRail)
        {
            for (int index = 0; index < postCount; index++)
            {
                double station = index * spacing;
                AddNode(model, nodesById, new SteelRailingNode
                {
                    Id = BottomRailNodeId(index),
                    X = startX + station,
                    Y = startY,
                    Z = baseElevation + bottomElevation,
                    PreviewX = station,
                    PreviewZ = bottomElevation
                });
            }
        }

        var counters = SteelRailingMemberGroups.All.ToDictionary(group => group, _ => 0, StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < postCount; index++)
        {
            if (pointLoadOnPosts)
            {
                AddMember(model, SteelRailingMemberGroups.Post, BaseNodeId(index), PostLoadNodeId(index), counters);
                AddMember(model, SteelRailingMemberGroups.Post, PostLoadNodeId(index), TopNodeId(index), counters);
            }
            else
            {
                AddMember(model, SteelRailingMemberGroups.Post, BaseNodeId(index), TopNodeId(index), counters);
            }
        }

        for (int index = 0; index < spanCount; index++)
            AddMember(model, SteelRailingMemberGroups.TopRail, TopNodeId(index), TopNodeId(index + 1), counters);

        foreach ((int levelIndex, _) in midLevels)
        {
            for (int index = 0; index < spanCount; index++)
                AddMember(model, SteelRailingMemberGroups.MidRail, MidNodeId(levelIndex, index), MidNodeId(levelIndex, index + 1), counters);
        }

        if (options.GenerateBottomRail)
        {
            for (int index = 0; index < spanCount; index++)
                AddMember(model, SteelRailingMemberGroups.BottomRail, BottomRailNodeId(index), BottomRailNodeId(index + 1), counters);
        }

        foreach (SteelRailingMember member in model.Members)
        {
            if (model.SectionAssignments.TryGetValue(member.Group, out string? sectionName))
                member.SectionName = sectionName ?? "";
        }

        for (int index = 0; index < postCount; index++)
        {
            model.Supports.Add(new SteelRailingSupport
            {
                NodeId = BaseNodeId(index),
                RestraintType = options.BaseRestraintType
            });
        }

        if (options.ApplyTopRailLoad && options.LoadType == RailingLoadType.LineLoad && Math.Abs(options.HorizontalLoadKnPerM) > 0.000001)
        {
            model.Loads.Add(new SteelRailingLoad
            {
                Id = $"{railingId}_LINE_LOAD",
                LoadPattern = options.LoadPattern,
                LoadType = RailingLoadType.LineLoad,
                TargetGroup = NormalizeLineLoadTarget(options.LoadTargetGroup),
                Direction = options.LoadDirection,
                MagnitudeKnPerM = options.HorizontalLoadKnPerM
            });
        }
        else if (pointLoadOnPosts && Math.Abs(options.HorizontalPointLoadKn) > 0.000001)
        {
            model.Loads.Add(new SteelRailingLoad
            {
                Id = $"{railingId}_POST_POINT_LOAD",
                LoadPattern = options.LoadPattern,
                LoadType = RailingLoadType.PointLoad,
                TargetGroup = SteelRailingMemberGroups.Post,
                Direction = options.LoadDirection,
                MagnitudeKn = options.HorizontalPointLoadKn,
                PointHeight = pointLoadHeight,
                TargetNodeIds = Enumerable.Range(0, postCount)
                    .Select(PostLoadNodeId)
                    .ToList()
            });
        }

        if (midLevels.Count == 0)
            model.Warnings.Add("No mid rail is generated for this railing.");
        if (options.BaseRestraintType == RailingBaseRestraintType.Pinned)
            model.Warnings.Add("Pinned post bases are selected; check if base moments should be released for railing checking.");

        return model;
    }

    private static List<(int LevelIndex, double Elevation)> BuildMidRailLevels(SteelRailingOptions options, double height)
    {
        if (!options.GenerateMidRails || options.MidRailCount <= 0)
            return [];

        int count = Math.Max(1, options.MidRailCount);
        var levels = new List<(int LevelIndex, double Elevation)>();
        if (count == 1)
        {
            levels.Add((1, ClampRailElevation(options.MidRailElevation, height, height / 2.0)));
            return levels;
        }

        for (int index = 0; index < count; index++)
        {
            double elevation = height * (index + 1) / (count + 1);
            levels.Add((index + 1, elevation));
        }

        return levels;
    }

    private static void AddNode(SteelRailingModel model, Dictionary<string, SteelRailingNode> nodesById, SteelRailingNode node)
    {
        if (nodesById.ContainsKey(node.Id))
            return;

        nodesById[node.Id] = node;
        model.Nodes.Add(node);
    }

    private static void AddMember(
        SteelRailingModel model,
        string group,
        string startNodeId,
        string endNodeId,
        Dictionary<string, int> counters)
    {
        counters[group] = counters.TryGetValue(group, out int value) ? value + 1 : 1;
        model.Members.Add(new SteelRailingMember
        {
            Id = $"{model.RailingId}_{MemberPrefix(group)}_{EtabsNameUtility.FormatIndex(counters[group])}",
            StartNodeId = startNodeId,
            EndNodeId = endNodeId,
            Group = group
        });
    }

    private static double SanitizePositive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0.000001 ? value : fallback;
    }

    private static double ClampRailElevation(double value, double height, double fallback)
    {
        double elevation = double.IsFinite(value) ? value : fallback;
        return Math.Clamp(elevation, 0.001, Math.Max(0.001, height - 0.001));
    }

    private static string BaseNodeId(int index) => $"B{index:00}";
    private static string TopNodeId(int index) => $"T{index:00}";
    private static string PostLoadNodeId(int index) => $"PL{index:00}";
    private static string MidNodeId(int level, int index) => $"M{level:00}_{index:00}";
    private static string BottomRailNodeId(int index) => $"BR{index:00}";

    private static string NormalizeLineLoadTarget(string? targetGroup)
    {
        return string.Equals(targetGroup, SteelRailingMemberGroups.Post, StringComparison.OrdinalIgnoreCase)
            ? SteelRailingMemberGroups.Post
            : SteelRailingMemberGroups.TopRail;
    }

    private static string MemberPrefix(string group)
    {
        return group switch
        {
            SteelRailingMemberGroups.Post => "POST",
            SteelRailingMemberGroups.TopRail => "TOP",
            SteelRailingMemberGroups.MidRail => "MID",
            SteelRailingMemberGroups.BottomRail => "BOT",
            _ => "MEM"
        };
    }
}
