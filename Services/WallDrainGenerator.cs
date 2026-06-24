using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class WallDrainOptions
{
    public string StructureId { get; set; } = "WD01";
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
    public bool ApplyUdl { get; set; }
    public string UdlLoadPattern { get; set; } = "";
    public WallDrainLoadDirection UdlDirection { get; set; } = WallDrainLoadDirection.NormalInward;
    public double UdlPressureKnPerM2 { get; set; } = 10.0;
    public bool ApplyTriangularLoad { get; set; } = true;
    public string TriangularLoadPattern { get; set; } = "";
    public WallDrainLoadDirection TriangularDirection { get; set; } = WallDrainLoadDirection.NormalInward;
    public double TriangularTopPressureKnPerM2 { get; set; }
    public double TriangularBottomPressureKnPerM2 { get; set; } = 30.0;
}

public sealed class WallDrainGenerator
{
    public WallDrainModel Generate(WallDrainOptions options)
    {
        string structureId = EtabsNameUtility.BuildSafeName("", options.StructureId, 24);
        double length = SanitizePositive(options.LengthY, 1.0);
        double height = SanitizePositive(options.Height, 3.0);
        double clearWidth = SanitizePositive(options.ClearWidth, 1.5);
        double toe = Math.Max(0, SanitizeFinite(options.ToeLength));
        double heel = Math.Max(0, SanitizeFinite(options.HeelLength));
        double meshSize = SanitizePositive(options.LengthMeshSize, 1.0);
        int lengthDivisions = Math.Clamp((int)Math.Ceiling(length / meshSize), 1, 200);
        int heightDivisions = Math.Clamp(options.HeightDivisions, 1, 100);

        var model = new WallDrainModel
        {
            StructureId = structureId,
            GroupName = EtabsNameUtility.BuildSafeName("WPF_WALL_DRAIN_", structureId),
            ShapeMode = options.ShapeMode,
            ModelingMode = options.ModelingMode,
            LengthY = length,
            OriginX = SanitizeFinite(options.OriginX),
            OriginY = SanitizeFinite(options.OriginY),
            OriginZ = SanitizeFinite(options.OriginZ),
            Height = height,
            ClearWidth = clearWidth,
            ToeLength = toe,
            HeelLength = heel,
            LengthMeshSize = meshSize,
            HeightDivisions = heightDivisions,
            GenerateBaseSlab = options.GenerateBaseSlab,
            GenerateButtressOrCounterfort = options.GenerateButtressOrCounterfort,
            UseCounterfort = options.UseCounterfort,
            ButtressProjection = SanitizePositive(options.ButtressProjection, 1.0),
            ButtressSpacing = SanitizePositive(options.ButtressSpacing, 2.0),
            WallFrameSectionName = options.WallFrameSectionName ?? "",
            SlabFrameSectionName = options.SlabFrameSectionName ?? "",
            ButtressFrameSectionName = options.ButtressFrameSectionName ?? "",
            WallShellPropertyName = options.WallShellPropertyName ?? "",
            SlabShellPropertyName = options.SlabShellPropertyName ?? "",
            ButtressShellPropertyName = options.ButtressShellPropertyName ?? ""
        };

        var nodesByKey = new Dictionary<string, WallDrainNode>(StringComparer.OrdinalIgnoreCase);
        int frameCounter = 0;
        int panelCounter = 0;

        foreach (SectionSegment segment in BuildSectionSegments(options.ShapeMode, clearWidth, toe, heel, height, options.GenerateBaseSlab))
        {
            if (options.ModelingMode == WallDrainModelingMode.Shell)
                AddExtrudedPanels(model, nodesByKey, segment, lengthDivisions, heightDivisions, ref panelCounter);
            else
                AddFrame(model, nodesByKey, segment, ref frameCounter);
        }

        if (options.GenerateButtressOrCounterfort && IsRetainingWallShape(options.ShapeMode))
        {
            if (options.ModelingMode == WallDrainModelingMode.Shell)
                AddButtressOrCounterfortPanel(model, nodesByKey, ref panelCounter);
            else
                AddButtressOrCounterfortFrame(model, nodesByKey, ref frameCounter);
        }

        AddLoads(model, options);
        AddModelWarnings(model, options);
        return model;
    }

    private static void AddFrame(
        WallDrainModel model,
        Dictionary<string, WallDrainNode> nodesByKey,
        SectionSegment segment,
        ref int frameCounter)
    {
        WallDrainNode start = GetOrAddNode(model, nodesByKey, new Point3(segment.X1, 0, segment.Z1, Math.Abs(segment.Z1) <= 0.000001));
        WallDrainNode end = GetOrAddNode(model, nodesByKey, new Point3(segment.X2, 0, segment.Z2, Math.Abs(segment.Z2) <= 0.000001));
        AddFrameMember(model, segment.Group, start.Id, end.Id, segment.LoadSignX, ref frameCounter);
    }

    private static List<SectionSegment> BuildSectionSegments(WallDrainShapeMode shape, double clearWidth, double toe, double heel, double height, bool generateBaseSlab)
    {
        var segments = new List<SectionSegment>();

        switch (shape)
        {
            case WallDrainShapeMode.OneSidedWall:
                segments.Add(new SectionSegment(WallDrainPanelGroups.Stem, 0, 0, 0, height, 1));
                if (generateBaseSlab && toe > 0)
                    segments.Add(new SectionSegment(WallDrainPanelGroups.BaseSlab, -toe, 0, 0, 0, 0));
                break;

            case WallDrainShapeMode.LWall:
                segments.Add(new SectionSegment(WallDrainPanelGroups.Stem, 0, 0, 0, height, 1));
                double left = toe > 0 ? -toe : 0;
                double right = heel > 0 ? heel : Math.Max(clearWidth, 0.001);
                segments.Add(new SectionSegment(WallDrainPanelGroups.BaseSlab, left, 0, right, 0, 0));
                break;

            case WallDrainShapeMode.UDrain:
                segments.Add(new SectionSegment(WallDrainPanelGroups.BaseSlab, 0, 0, clearWidth, 0, 0));
                segments.Add(new SectionSegment(WallDrainPanelGroups.LeftWall, 0, 0, 0, height, 1));
                segments.Add(new SectionSegment(WallDrainPanelGroups.RightWall, clearWidth, 0, clearWidth, height, -1));
                break;

            case WallDrainShapeMode.BoxDrain:
                segments.Add(new SectionSegment(WallDrainPanelGroups.BaseSlab, 0, 0, clearWidth, 0, 0));
                segments.Add(new SectionSegment(WallDrainPanelGroups.LeftWall, 0, 0, 0, height, 1));
                segments.Add(new SectionSegment(WallDrainPanelGroups.RightWall, clearWidth, 0, clearWidth, height, -1));
                segments.Add(new SectionSegment(WallDrainPanelGroups.TopSlab, 0, height, clearWidth, height, 0));
                break;
        }

        return segments;
    }

    private static void AddExtrudedPanels(
        WallDrainModel model,
        Dictionary<string, WallDrainNode> nodesByKey,
        SectionSegment segment,
        int lengthDivisions,
        int heightDivisions,
        ref int panelCounter)
    {
        int crossDivisions = IsVerticalGroup(segment.Group)
            ? heightDivisions
            : Math.Max(1, (int)Math.Ceiling(Math.Abs(segment.X2 - segment.X1) / Math.Max(model.LengthMeshSize, 0.001)));

        for (int yIndex = 0; yIndex < lengthDivisions; yIndex++)
        {
            double y0 = model.LengthY * yIndex / lengthDivisions;
            double y1 = model.LengthY * (yIndex + 1) / lengthDivisions;

            for (int crossIndex = 0; crossIndex < crossDivisions; crossIndex++)
            {
                double t0 = (double)crossIndex / crossDivisions;
                double t1 = (double)(crossIndex + 1) / crossDivisions;
                WallDrainNode n00 = GetOrAddNode(model, nodesByKey, Interpolate(segment, t0, y0));
                WallDrainNode n10 = GetOrAddNode(model, nodesByKey, Interpolate(segment, t1, y0));
                WallDrainNode n11 = GetOrAddNode(model, nodesByKey, Interpolate(segment, t1, y1));
                WallDrainNode n01 = GetOrAddNode(model, nodesByKey, Interpolate(segment, t0, y1));
                AddPanel(model, segment.Group, [n00.Id, n10.Id, n11.Id, n01.Id], segment.LoadSignX, ref panelCounter);
            }
        }
    }

    private static void AddButtressOrCounterfortFrame(
        WallDrainModel model,
        Dictionary<string, WallDrainNode> nodesByKey,
        ref int frameCounter)
    {
        double projection = Math.Min(model.ButtressProjection, Math.Max(model.ToeLength, model.HeelLength));
        if (projection <= 0.000001)
            projection = model.ButtressProjection;

        double sign = model.UseCounterfort ? 1.0 : -1.0;
        string group = model.UseCounterfort ? WallDrainPanelGroups.Counterfort : WallDrainPanelGroups.Buttress;
        WallDrainNode bottomProjection = GetOrAddNode(model, nodesByKey, new Point3(sign * projection, 0, 0, true));
        WallDrainNode topStem = GetOrAddNode(model, nodesByKey, new Point3(0, 0, model.Height, false));
        AddFrameMember(model, group, bottomProjection.Id, topStem.Id, 0, ref frameCounter);
    }

    private static void AddButtressOrCounterfortPanel(
        WallDrainModel model,
        Dictionary<string, WallDrainNode> nodesByKey,
        ref int panelCounter)
    {
        double projection = Math.Min(model.ButtressProjection, Math.Max(model.ToeLength, model.HeelLength));
        if (projection <= 0.000001)
            projection = model.ButtressProjection;

        double sign = model.UseCounterfort ? 1.0 : -1.0;
        string group = model.UseCounterfort ? WallDrainPanelGroups.Counterfort : WallDrainPanelGroups.Buttress;
        WallDrainNode bottomStem = GetOrAddNode(model, nodesByKey, new Point3(0, 0, 0, true));
        WallDrainNode bottomProjection = GetOrAddNode(model, nodesByKey, new Point3(sign * projection, 0, 0, true));
        WallDrainNode topStem = GetOrAddNode(model, nodesByKey, new Point3(0, 0, model.Height, false));
        AddPanel(model, group, [bottomStem.Id, bottomProjection.Id, topStem.Id], 0, ref panelCounter);
    }

    private static void AddFrameMember(WallDrainModel model, string group, string startNodeId, string endNodeId, double loadSignX, ref int frameCounter)
    {
        frameCounter++;
        model.FrameMembers.Add(new WallDrainFrameMember
        {
            Id = $"{model.StructureId}_FR_{EtabsNameUtility.FormatIndex(frameCounter)}",
            Group = group,
            StartNodeId = startNodeId,
            EndNodeId = endNodeId,
            SectionName = FrameSectionForGroup(model, group),
            LoadSignX = loadSignX == 0 ? 1.0 : Math.Sign(loadSignX)
        });
    }

    private static void AddPanel(WallDrainModel model, string group, List<string> nodeIds, double loadSignX, ref int panelCounter)
    {
        panelCounter++;
        List<WallDrainNode> nodes = nodeIds
            .Select(nodeId => model.Nodes.First(node => string.Equals(node.Id, nodeId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        model.ShellPanels.Add(new WallDrainShellPanel
        {
            Id = $"{model.StructureId}_SH_{EtabsNameUtility.FormatIndex(panelCounter)}",
            Group = group,
            NodeIds = nodeIds,
            ShellPropertyName = ShellPropertyForGroup(model, group),
            CentroidX = nodes.Average(node => node.X),
            CentroidY = nodes.Average(node => node.Y),
            CentroidZ = nodes.Average(node => node.Z),
            LoadSignX = loadSignX == 0 ? 1.0 : Math.Sign(loadSignX)
        });
    }

    private static WallDrainNode GetOrAddNode(WallDrainModel model, Dictionary<string, WallDrainNode> nodesByKey, Point3 point)
    {
        double x = model.OriginX + point.X;
        double y = model.OriginY + point.Y;
        double z = model.OriginZ + point.Z;
        string key = $"{Math.Round(x, 6):0.######}|{Math.Round(y, 6):0.######}|{Math.Round(z, 6):0.######}";
        if (nodesByKey.TryGetValue(key, out WallDrainNode? existing))
        {
            if (point.IsSupport)
                existing.IsSupport = true;
            return existing;
        }

        var node = new WallDrainNode
        {
            Id = $"N{model.Nodes.Count + 1:0000}",
            X = x,
            Y = y,
            Z = z,
            IsSupport = point.IsSupport
        };
        nodesByKey[key] = node;
        model.Nodes.Add(node);
        return node;
    }

    private static Point3 Interpolate(SectionSegment segment, double t, double y)
    {
        return new Point3(
            segment.X1 + (segment.X2 - segment.X1) * t,
            y,
            segment.Z1 + (segment.Z2 - segment.Z1) * t,
            Math.Abs(segment.Z1 + (segment.Z2 - segment.Z1) * t) <= 0.000001);
    }

    private static void AddLoads(WallDrainModel model, WallDrainOptions options)
    {
        List<string> verticalGroups = WallDrainPanelGroups.VerticalWallGroups
            .Where(group => model.FrameMembers.Any(member => string.Equals(member.Group, group, StringComparison.OrdinalIgnoreCase)) ||
                model.ShellPanels.Any(panel => string.Equals(panel.Group, group, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (options.ApplyUdl && Math.Abs(options.UdlPressureKnPerM2) > 0.000001)
        {
            model.SurfaceLoads.Add(new WallDrainSurfaceLoad
            {
                Id = $"{model.StructureId}_UDL",
                Kind = WallDrainLoadKind.Udl,
                LoadPattern = options.UdlLoadPattern,
                Direction = options.UdlDirection,
                TargetGroups = verticalGroups,
                UniformPressureKnPerM2 = options.UdlPressureKnPerM2
            });
        }

        if (options.ApplyTriangularLoad && (Math.Abs(options.TriangularTopPressureKnPerM2) > 0.000001 || Math.Abs(options.TriangularBottomPressureKnPerM2) > 0.000001))
        {
            model.SurfaceLoads.Add(new WallDrainSurfaceLoad
            {
                Id = $"{model.StructureId}_TRI",
                Kind = WallDrainLoadKind.Triangular,
                LoadPattern = options.TriangularLoadPattern,
                Direction = options.TriangularDirection,
                TargetGroups = verticalGroups,
                TopPressureKnPerM2 = options.TriangularTopPressureKnPerM2,
                BottomPressureKnPerM2 = options.TriangularBottomPressureKnPerM2
            });
        }
    }

    private static void AddModelWarnings(WallDrainModel model, WallDrainOptions options)
    {
        if (model.ShapeMode == WallDrainShapeMode.OneSidedWall && !model.GenerateBaseSlab)
            model.Warnings.Add("One-sided wall has no toe/base slab generated.");
        if (model.ShapeMode == WallDrainShapeMode.OneSidedWall && model.ToeLength <= 0.000001)
            model.Warnings.Add("One-sided wall toe length is zero.");
        if (options.GenerateButtressOrCounterfort && !IsRetainingWallShape(model.ShapeMode))
            model.Warnings.Add("Buttress/counterfort panels are only generated for one-sided and L wall modes.");
    }

    private static string ShellPropertyForGroup(WallDrainModel model, string group)
    {
        return group switch
        {
            WallDrainPanelGroups.BaseSlab or WallDrainPanelGroups.TopSlab => model.SlabShellPropertyName,
            WallDrainPanelGroups.Buttress or WallDrainPanelGroups.Counterfort => model.ButtressShellPropertyName,
            _ => model.WallShellPropertyName
        };
    }

    private static string FrameSectionForGroup(WallDrainModel model, string group)
    {
        return group switch
        {
            WallDrainPanelGroups.BaseSlab or WallDrainPanelGroups.TopSlab => model.SlabFrameSectionName,
            WallDrainPanelGroups.Buttress or WallDrainPanelGroups.Counterfort => model.ButtressFrameSectionName,
            _ => model.WallFrameSectionName
        };
    }

    private static bool IsVerticalGroup(string group)
    {
        return WallDrainPanelGroups.VerticalWallGroups.Contains(group);
    }

    private static bool IsRetainingWallShape(WallDrainShapeMode shape)
    {
        return shape is WallDrainShapeMode.OneSidedWall or WallDrainShapeMode.LWall;
    }

    private static double SanitizePositive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0.000001 ? value : fallback;
    }

    private static double SanitizeFinite(double value)
    {
        return double.IsFinite(value) ? value : 0.0;
    }

    private readonly record struct SectionSegment(string Group, double X1, double Z1, double X2, double Z2, double LoadSignX);
    private readonly record struct Point3(double X, double Y, double Z, bool IsSupport);
}
