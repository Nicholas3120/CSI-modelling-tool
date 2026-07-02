using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class ParametricTrussOptions
{
    public string TrussId { get; set; } = "TR01";
    public string GroupName { get; set; } = "";
    public TrussType TrussType { get; set; } = TrussType.Warren;
    public ModelPoint3d StartPoint { get; set; } = new();
    public ModelPoint3d EndPoint { get; set; } = new() { X = 12 };
    public double Height { get; set; } = 2.5;
    public int PanelCount { get; set; } = 6;
    public int YBayCount { get; set; } = 1;
    public double YBaySpacing { get; set; } = 3.0;
    public bool GenerateOrthogonalYZTrusses { get; set; }
    public TrussType OrthogonalYZTrussType { get; set; } = TrussType.Warren;
    public OrthogonalTrussPlacementMode OrthogonalYZPlacementMode { get; set; } = OrthogonalTrussPlacementMode.EveryPanelPoint;
    public int OrthogonalYZPanelsPerBay { get; set; } = 1;
    public double RoofSlopePercent { get; set; }
    public double BottomChordSlopePercent { get; set; }
    public ChordSlopeMode TopChordSlopeMode { get; set; } = ChordSlopeMode.Pitch;
    public ChordSlopeMode BottomChordSlopeMode { get; set; } = ChordSlopeMode.Pitch;
    public SupportNodeMode SupportNodeMode { get; set; } = SupportNodeMode.EndBottomNodes;
    public SupportRestraintType SupportRestraintType { get; set; } = SupportRestraintType.FirstPinOthersRoller;
    public Dictionary<string, string> SectionAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ApplyTopChordLoad { get; set; } = true;
    public bool ApplyTopChordLoadToPanelNodes { get; set; } = true;
    public string LoadPattern { get; set; } = "";
    public double TopChordGravityLoadKnPerM { get; set; }
    public bool ApplyBottomChordLoad { get; set; }
    public bool ApplyBottomChordLoadToPanelNodes { get; set; } = true;
    public double BottomChordLoadMagnitude { get; set; }
    public List<ParametricTrussLoadDefinition> LoadDefinitions { get; set; } = [];
    public double SpiralCentreX { get; set; }
    public double SpiralCentreY { get; set; }
    public double SpiralBaseZ { get; set; }
    public double SpiralTotalHeight { get; set; } = 3.6;
    public double SpiralInnerRadius { get; set; } = 1.0;
    public double SpiralOuterRadius { get; set; } = 2.0;
    public int SpiralStepCount { get; set; } = 24;
    public double SpiralTotalRotationDegrees { get; set; } = 360.0;
    public double SpiralStartAngleDegrees { get; set; }
    public SpiralStairRotationDirection SpiralRotationDirection { get; set; } = SpiralStairRotationDirection.Anticlockwise;
    public bool SpiralCreateInnerStringer { get; set; } = true;
    public bool SpiralCreateOuterStringer { get; set; } = true;
    public bool SpiralCreateRadialTreadBeams { get; set; } = true;
    public bool SpiralCreateTreadShellPlates { get; set; }
    public bool SpiralCreateCentralColumn { get; set; }
    public bool SpiralCreateTopLandingBeam { get; set; }
    public bool SpiralCreateBottomLandingBeam { get; set; }
    public string SpiralTreadShellProperty { get; set; } = "";
    public double FishStartX { get; set; }
    public double FishStartY { get; set; }
    public double FishStartZ { get; set; }
    public double FishSpanLength { get; set; } = 24.0;
    public int FishPanelCount { get; set; } = 12;
    public double FishEndDepth { get; set; } = 1.0;
    public double FishMiddleDepth { get; set; } = 3.0;
    public double FishDirectionAngleDegrees { get; set; }
    public double FishTopChordSlopeDegrees { get; set; }
    public FishBellyBottomChordShape FishBottomChordShape { get; set; } = FishBellyBottomChordShape.Parabolic;
    public FishBellyWebPattern FishWebPattern { get; set; } = FishBellyWebPattern.VerticalAlternatingDiagonal;
    public bool FishReleaseMoments { get; set; } = true;
    public double VariablePanelStartX { get; set; }
    public double VariablePanelStartY { get; set; }
    public double VariablePanelStartZ { get; set; } = 10.0;
    public double VariablePanelSpanLength { get; set; } = 24.0;
    public int VariablePanelCount { get; set; } = 12;
    public double VariablePanelTrussDepth { get; set; } = 2.5;
    public double VariablePanelEndWidthRatio { get; set; } = 0.5;
    public double VariablePanelMiddleWidthRatio { get; set; } = 1.5;
    public double VariablePanelDirectionAngleDegrees { get; set; }
    public VariablePanelWidthVariation VariablePanelWidthVariation { get; set; } = VariablePanelWidthVariation.Parabolic;
    public FishBellyWebPattern VariablePanelWebPattern { get; set; } = FishBellyWebPattern.VerticalAlternatingDiagonal;
    public bool VariablePanelReleaseMoments { get; set; } = true;
}

public sealed class ParametricTrussGenerator
{
    public ParametricTrussModel Generate(ParametricTrussOptions options)
    {
        if (options.TrussType == TrussType.SpiralStaircase)
            return GenerateSpiralStaircase(options);

        if (options.TrussType == TrussType.FishBellyTruss)
            return GenerateFishBellyTruss(options);

        if (options.TrussType == TrussType.VariablePanelWidthTruss)
            return GenerateVariablePanelWidthTruss(options);

        string trussId = EtabsNameUtility.BuildSafeName("", options.TrussId, 24);
        int panelCount = Math.Max(2, options.PanelCount);
        int yBayCount = Math.Clamp(options.YBayCount, 1, 40);
        double yBaySpacing = double.IsFinite(options.YBaySpacing) ? Math.Max(0.001, options.YBaySpacing) : 3.0;
        double height = double.IsFinite(options.Height) ? Math.Max(0, options.Height) : 0;
        ModelPoint3d start = options.StartPoint.Clone();
        ModelPoint3d end = options.EndPoint.Clone();

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        double span = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(span) || span <= 0)
        {
            span = 1;
            end = new ModelPoint3d { X = start.X + span, Y = start.Y, Z = start.Z };
            dx = span;
            dy = 0;
            dz = 0;
        }

        var model = new ParametricTrussModel
        {
            TrussId = trussId,
            GroupName = BuildGroupName(options.GroupName, "WPF_TRUSS_", trussId),
            TrussType = options.TrussType,
            Span = span,
            Height = height,
            PanelCount = panelCount,
            YBayCount = yBayCount,
            YBaySpacing = yBaySpacing,
            GenerateOrthogonalYZTrusses = options.GenerateOrthogonalYZTrusses,
            OrthogonalYZTrussType = ToStandardTrussType(options.OrthogonalYZTrussType),
            OrthogonalYZPlacementMode = options.OrthogonalYZPlacementMode,
            OrthogonalYZPanelsPerBay = Math.Clamp(options.OrthogonalYZPanelsPerBay, 1, 40),
            RoofSlopePercent = options.RoofSlopePercent,
            BottomChordSlopePercent = options.BottomChordSlopePercent,
            TopChordSlopeMode = options.TopChordSlopeMode,
            BottomChordSlopeMode = options.BottomChordSlopeMode,
            SupportNodeMode = options.SupportNodeMode,
            SupportRestraintType = options.SupportRestraintType,
            StartPoint = start,
            EndPoint = end,
            SectionAssignments = new Dictionary<string, string>(options.SectionAssignments, StringComparer.OrdinalIgnoreCase)
        };

        var nodesById = new Dictionary<string, ParametricNode>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index <= panelCount; index++)
        {
            double t = (double)index / panelCount;
            double localX = span * t;
            ModelPoint3d basePoint = ModelPoint3d.Interpolate(start, end, t);
            double bottomRise = CalculateSlopeRise(span, t, options.BottomChordSlopePercent, options.BottomChordSlopeMode);
            double topChordRise = CalculateSlopeRise(span, t, options.RoofSlopePercent, options.TopChordSlopeMode);

            AddNode(model, nodesById, new ParametricNode
            {
                Id = BottomNodeId(index),
                X = basePoint.X,
                Y = basePoint.Y,
                Z = basePoint.Z + bottomRise,
                PreviewX = localX,
                PreviewZ = bottomRise,
                IsSupport = IsSupportNode(index, panelCount, options.SupportNodeMode),
                IsBottomChord = true
            });

            AddNode(model, nodesById, new ParametricNode
            {
                Id = TopNodeId(index),
                X = basePoint.X,
                Y = basePoint.Y,
                Z = basePoint.Z + height + topChordRise,
                PreviewX = localX,
                PreviewZ = height + topChordRise,
                IsTopChord = true
            });
        }

        model.Height = Math.Max(
            0.001,
            model.Nodes.Max(node => node.PreviewZ) - model.Nodes.Min(node => node.PreviewZ));

        var counters = ParametricMemberGroups.All.ToDictionary(group => group, _ => 0, StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < panelCount; index++)
        {
            AddMember(model, trussId, ParametricMemberGroups.BottomChord, BottomNodeId(index), BottomNodeId(index + 1), counters);
            AddMember(model, trussId, ParametricMemberGroups.TopChord, TopNodeId(index), TopNodeId(index + 1), counters);
        }

        AddMember(model, trussId, ParametricMemberGroups.EndPost, BottomNodeId(0), TopNodeId(0), counters);
        AddMember(model, trussId, ParametricMemberGroups.EndPost, BottomNodeId(panelCount), TopNodeId(panelCount), counters);

        if (options.TrussType == TrussType.K)
        {
            for (int index = 1; index < panelCount; index++)
                AddMember(model, trussId, ParametricMemberGroups.Vertical, BottomNodeId(index), TopNodeId(index), counters);

            for (int index = 0; index < panelCount; index++)
            {
                ParametricNode leftBottom = nodesById[BottomNodeId(index)];
                ParametricNode rightBottom = nodesById[BottomNodeId(index + 1)];
                ParametricNode leftTop = nodesById[TopNodeId(index)];
                ParametricNode rightTop = nodesById[TopNodeId(index + 1)];

                string midNodeId = $"K{index:00}";
                AddNode(model, nodesById, new ParametricNode
                {
                    Id = midNodeId,
                    X = (leftBottom.X + rightBottom.X + leftTop.X + rightTop.X) / 4.0,
                    Y = (leftBottom.Y + rightBottom.Y + leftTop.Y + rightTop.Y) / 4.0,
                    Z = (leftBottom.Z + rightBottom.Z + leftTop.Z + rightTop.Z) / 4.0,
                    PreviewX = (leftBottom.PreviewX + rightBottom.PreviewX) / 2.0,
                    PreviewZ = (leftBottom.PreviewZ + leftTop.PreviewZ + rightBottom.PreviewZ + rightTop.PreviewZ) / 4.0
                });

                AddMember(model, trussId, ParametricMemberGroups.Diagonal, BottomNodeId(index), midNodeId, counters);
                AddMember(model, trussId, ParametricMemberGroups.Diagonal, TopNodeId(index), midNodeId, counters);
                AddMember(model, trussId, ParametricMemberGroups.Diagonal, midNodeId, BottomNodeId(index + 1), counters);
                AddMember(model, trussId, ParametricMemberGroups.Diagonal, midNodeId, TopNodeId(index + 1), counters);
            }
        }
        else
        {
            for (int index = 1; index < panelCount; index++)
                AddMember(model, trussId, ParametricMemberGroups.Vertical, BottomNodeId(index), TopNodeId(index), counters);

            if (options.TrussType != TrussType.SimpleFrame)
            {
                for (int index = 0; index < panelCount; index++)
                {
                    (string Start, string End) diagonal = options.TrussType switch
                    {
                        TrussType.Warren => index % 2 == 0
                            ? (BottomNodeId(index), TopNodeId(index + 1))
                            : (TopNodeId(index), BottomNodeId(index + 1)),
                        TrussType.Pratt => index < panelCount / 2.0
                            ? (BottomNodeId(index), TopNodeId(index + 1))
                            : (TopNodeId(index), BottomNodeId(index + 1)),
                        TrussType.Howe => index < panelCount / 2.0
                            ? (TopNodeId(index), BottomNodeId(index + 1))
                            : (BottomNodeId(index), TopNodeId(index + 1)),
                        _ => (BottomNodeId(index), TopNodeId(index + 1))
                    };

                    AddMember(model, trussId, ParametricMemberGroups.Diagonal, diagonal.Start, diagonal.End, counters);
                }
            }
        }

        AssignMemberSections(model);

        AddLoads(model, options);
        ApplyYBayArray(model, yBayCount, yBaySpacing);
        AddOrthogonalYZTrusses(
            model,
            trussId,
            options.GenerateOrthogonalYZTrusses,
            ToStandardTrussType(options.OrthogonalYZTrussType),
            options.OrthogonalYZPlacementMode,
            options.OrthogonalYZPanelsPerBay);
        return model;
    }

    private static ParametricNode BuildSpiralNode(
        string id,
        ParametricTrussOptions options,
        double radius,
        double angle,
        double z,
        bool isBottom,
        bool isTop)
    {
        double x = options.SpiralCentreX + radius * Math.Cos(angle);
        double y = options.SpiralCentreY + radius * Math.Sin(angle);
        return new ParametricNode
        {
            Id = id,
            X = x,
            Y = y,
            Z = z,
            PreviewX = x - options.SpiralCentreX,
            PreviewZ = y - options.SpiralCentreY,
            IsSupport = isBottom,
            IsTopChord = isTop,
            IsBottomChord = isBottom
        };
    }

    private static double CalculateFishDepth(double s, double span, double endDepth, double middleDepth, FishBellyBottomChordShape shape)
    {
        return shape switch
        {
            FishBellyBottomChordShape.LinearToMiddle => s <= 0.5
                ? endDepth + (middleDepth - endDepth) * (s / 0.5)
                : middleDepth - (middleDepth - endDepth) * ((s - 0.5) / 0.5),
            FishBellyBottomChordShape.CircularArcApproximation => CalculateCircularFishDepth(s, span, endDepth, middleDepth),
            _ => endDepth + (middleDepth - endDepth) * 4.0 * s * (1.0 - s)
        };
    }

    private static double CalculateCircularFishDepth(double s, double span, double endDepth, double middleDepth)
    {
        double halfSpan = span / 2.0;
        double sag = middleDepth - endDepth;
        if (sag <= 0.000001 || halfSpan <= 0.000001)
            return endDepth;

        double radius = (halfSpan * halfSpan + sag * sag) / (2.0 * sag);
        double x = Math.Abs((s - 0.5) * span);
        double depthIncrement = Math.Sqrt(Math.Max(0, radius * radius - x * x)) - (radius - sag);
        return endDepth + depthIncrement;
    }

    private static (double X, double Y) RotateAndTranslate(double startX, double startY, double xLocal, double cos, double sin)
    {
        return (startX + xLocal * cos, startY + xLocal * sin);
    }

    private static IEnumerable<(string Start, string End)> BuildFishDiagonals(int index, int panelCount, FishBellyWebPattern pattern)
    {
        return pattern switch
        {
            FishBellyWebPattern.VerticalSameDirectionDiagonal => [(TopNodeId(index), BottomNodeId(index + 1))],
            FishBellyWebPattern.CrossBracing => [(TopNodeId(index), BottomNodeId(index + 1)), (BottomNodeId(index), TopNodeId(index + 1))],
            FishBellyWebPattern.Pratt => index < panelCount / 2.0
                ? [(BottomNodeId(index), TopNodeId(index + 1))]
                : [(TopNodeId(index), BottomNodeId(index + 1))],
            FishBellyWebPattern.Howe => index < panelCount / 2.0
                ? [(TopNodeId(index), BottomNodeId(index + 1))]
                : [(BottomNodeId(index), TopNodeId(index + 1))],
            _ => index % 2 == 0
                ? [(TopNodeId(index), BottomNodeId(index + 1))]
                : [(BottomNodeId(index), TopNodeId(index + 1))]
        };
    }

    private static double[] BuildVariablePanelStations(
        double span,
        int panelCount,
        double endRatio,
        double middleRatio,
        VariablePanelWidthVariation variation)
    {
        var ratios = new double[panelCount];
        double sum = 0;
        for (int index = 0; index < panelCount; index++)
        {
            double s = (index + 0.5) / panelCount;
            double ratio = CalculateVariablePanelWidthRatio(s, endRatio, middleRatio, variation);
            ratios[index] = Math.Max(0.000001, ratio);
            sum += ratios[index];
        }

        if (!double.IsFinite(sum) || sum <= 0.000001)
        {
            Array.Fill(ratios, 1.0);
            sum = panelCount;
        }

        var stationX = new double[panelCount + 1];
        for (int index = 0; index < panelCount; index++)
            stationX[index + 1] = stationX[index] + span * ratios[index] / sum;

        stationX[^1] = span;
        return stationX;
    }

    private static double CalculateVariablePanelWidthRatio(
        double s,
        double endRatio,
        double middleRatio,
        VariablePanelWidthVariation variation)
    {
        double shapeFactor = variation switch
        {
            VariablePanelWidthVariation.LinearToMiddle => s <= 0.5 ? s / 0.5 : (1.0 - s) / 0.5,
            VariablePanelWidthVariation.SmoothCosine => 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * s)),
            _ => 4.0 * s * (1.0 - s)
        };

        return endRatio + (middleRatio - endRatio) * Math.Clamp(shapeFactor, 0.0, 1.0);
    }

    private static void AssignMemberSections(ParametricTrussModel model)
    {
        foreach (ParametricMember member in model.Members)
        {
            if (model.SectionAssignments.TryGetValue(member.Group, out string? sectionName))
                member.SectionName = sectionName;
        }
    }

    private static void ApplyYBayArray(ParametricTrussModel model, int yBayCount, double yBaySpacing)
    {
        model.YBayCount = Math.Clamp(yBayCount, 1, 40);
        model.YBaySpacing = double.IsFinite(yBaySpacing) ? Math.Max(0.001, yBaySpacing) : 3.0;

        if (model.YBayCount <= 1)
            return;

        if (Math.Abs(model.EndPoint.Y - model.StartPoint.Y) > 0.000001)
            model.Warnings.Add("Y bay duplication offsets each copy in global Y, but the selected truss span already has a Y component. Verify the plan layout before analysis.");

        foreach (ParametricNode node in model.Nodes)
            node.SupportGroupId = FormatYBayId(0);

        List<ParametricNode> baseNodes = model.Nodes.Select(CloneNode).ToList();
        List<ParametricMember> baseMembers = model.Members.Select(CloneMember).ToList();
        List<ParametricShell> baseShells = model.Shells.Select(CloneShell).ToList();
        List<ParametricLoad> baseNodeLoads = model.Loads
            .Where(load => load.TargetType.Equals("Node", StringComparison.OrdinalIgnoreCase))
            .Select(CloneLoad)
            .ToList();

        for (int bayIndex = 1; bayIndex < model.YBayCount; bayIndex++)
        {
            string supportGroupId = FormatYBayId(bayIndex);
            double offsetY = model.YBaySpacing * bayIndex;

            foreach (ParametricNode node in baseNodes)
            {
                ParametricNode clone = CloneNode(node);
                clone.Id = BuildYBayScopedId(node.Id, bayIndex);
                clone.Y = node.Y + offsetY;
                clone.SupportGroupId = supportGroupId;
                model.Nodes.Add(clone);
            }

            foreach (ParametricMember member in baseMembers)
            {
                ParametricMember clone = CloneMember(member);
                clone.Id = BuildYBayScopedId(member.Id, bayIndex);
                clone.StartNodeId = BuildYBayScopedId(member.StartNodeId, bayIndex);
                clone.EndNodeId = BuildYBayScopedId(member.EndNodeId, bayIndex);
                model.Members.Add(clone);
            }

            foreach (ParametricShell shell in baseShells)
            {
                ParametricShell clone = CloneShell(shell);
                clone.Id = BuildYBayScopedId(shell.Id, bayIndex);
                clone.NodeIds = shell.NodeIds
                    .Select(nodeId => BuildYBayScopedId(nodeId, bayIndex))
                    .ToList();
                model.Shells.Add(clone);
            }

            foreach (ParametricLoad load in baseNodeLoads)
            {
                ParametricLoad clone = CloneLoad(load);
                clone.Id = BuildYBayScopedId(load.Id, bayIndex);
                clone.TargetId = BuildYBayScopedId(load.TargetId, bayIndex);
                model.Loads.Add(clone);
            }
        }
    }

    private static ParametricNode CloneNode(ParametricNode node)
    {
        return new ParametricNode
        {
            Id = node.Id,
            X = node.X,
            Y = node.Y,
            Z = node.Z,
            PreviewX = node.PreviewX,
            PreviewZ = node.PreviewZ,
            IsSupport = node.IsSupport,
            IsTopChord = node.IsTopChord,
            IsBottomChord = node.IsBottomChord,
            SupportGroupId = node.SupportGroupId
        };
    }

    private static ParametricMember CloneMember(ParametricMember member)
    {
        return new ParametricMember
        {
            Id = member.Id,
            StartNodeId = member.StartNodeId,
            EndNodeId = member.EndNodeId,
            Group = member.Group,
            SectionName = member.SectionName,
            ReleaseMoments = member.ReleaseMoments
        };
    }

    private static ParametricShell CloneShell(ParametricShell shell)
    {
        return new ParametricShell
        {
            Id = shell.Id,
            NodeIds = shell.NodeIds.ToList(),
            Group = shell.Group,
            ShellPropertyName = shell.ShellPropertyName
        };
    }

    private static ParametricLoad CloneLoad(ParametricLoad load)
    {
        return new ParametricLoad
        {
            Id = load.Id,
            LoadPattern = load.LoadPattern,
            TargetType = load.TargetType,
            TargetId = load.TargetId,
            Direction = load.Direction,
            Magnitude = load.Magnitude
        };
    }

    private static string BuildYBayScopedId(string id, int bayIndex)
    {
        return EtabsNameUtility.BuildSafeName("", $"{id}_{FormatYBayId(bayIndex)}");
    }

    private static string FormatYBayId(int bayIndex)
    {
        return $"YB{bayIndex + 1:00}";
    }

    private static void AddOrthogonalYZTrusses(
        ParametricTrussModel model,
        string trussId,
        bool generate,
        TrussType trussType,
        OrthogonalTrussPlacementMode placementMode,
        int panelsPerBay)
    {
        model.GenerateOrthogonalYZTrusses = generate;
        model.OrthogonalYZTrussType = ToStandardTrussType(trussType);
        model.OrthogonalYZPlacementMode = placementMode;
        model.OrthogonalYZPanelsPerBay = Math.Clamp(panelsPerBay, 1, 40);
        model.OrthogonalYZTrussLineCount = 0;

        if (!generate)
            return;

        if (model.YBayCount < 2)
        {
            model.Warnings.Add("Orthogonal Y-Z trusses require at least 2 Y bay lines.");
            return;
        }

        CopySectionAssignment(model, ParametricMemberGroups.TopChord, ParametricMemberGroups.YZTopChord);
        CopySectionAssignment(model, ParametricMemberGroups.BottomChord, ParametricMemberGroups.YZBottomChord);
        CopySectionAssignment(model, ParametricMemberGroups.Diagonal, ParametricMemberGroups.YZDiagonal);
        CopySectionAssignment(model, ParametricMemberGroups.Vertical, ParametricMemberGroups.YZVertical);
        CopySectionAssignment(model, ParametricMemberGroups.EndPost, ParametricMemberGroups.YZEndPost);

        var nodesById = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        List<int> xStationIndices = BuildOrthogonalXStationIndices(model.PanelCount, placementMode);
        int yPanelCount = (model.YBayCount - 1) * model.OrthogonalYZPanelsPerBay;

        foreach (int xIndex in xStationIndices)
        {
            if (!HasRequiredOrthogonalBoundaryNodes(nodesById, xIndex, model.YBayCount))
            {
                model.Warnings.Add($"Skipped Y-Z truss at X panel point {xIndex}: matching X-Z chord nodes were not available.");
                continue;
            }

            string lineTrussId = EtabsNameUtility.BuildSafeName("", $"{trussId}_YZ_X{xIndex:00}", 36);
            var counters = ParametricMemberGroups.All.ToDictionary(group => group, _ => 0, StringComparer.OrdinalIgnoreCase);
            List<string> bottomNodeIds = [];
            List<string> topNodeIds = [];

            for (int stationIndex = 0; stationIndex <= yPanelCount; stationIndex++)
            {
                bottomNodeIds.Add(GetOrCreateOrthogonalYZChordNode(model, nodesById, lineTrussId, xIndex, stationIndex, model.OrthogonalYZPanelsPerBay, isTop: false));
                topNodeIds.Add(GetOrCreateOrthogonalYZChordNode(model, nodesById, lineTrussId, xIndex, stationIndex, model.OrthogonalYZPanelsPerBay, isTop: true));
            }

            for (int panelIndex = 0; panelIndex < yPanelCount; panelIndex++)
            {
                AddMember(
                    model,
                    lineTrussId,
                    ParametricMemberGroups.YZBottomChord,
                    bottomNodeIds[panelIndex],
                    bottomNodeIds[panelIndex + 1],
                    counters);
                AddMember(
                    model,
                    lineTrussId,
                    ParametricMemberGroups.YZTopChord,
                    topNodeIds[panelIndex],
                    topNodeIds[panelIndex + 1],
                    counters);
            }

            AddMember(
                model,
                lineTrussId,
                ParametricMemberGroups.YZEndPost,
                bottomNodeIds[0],
                topNodeIds[0],
                counters);
            AddMember(
                model,
                lineTrussId,
                ParametricMemberGroups.YZEndPost,
                bottomNodeIds[yPanelCount],
                topNodeIds[yPanelCount],
                counters);

            if (model.OrthogonalYZTrussType == TrussType.K)
            {
                for (int panelIndex = 1; panelIndex < yPanelCount; panelIndex++)
                {
                    AddMember(
                        model,
                        lineTrussId,
                        ParametricMemberGroups.YZVertical,
                        bottomNodeIds[panelIndex],
                        topNodeIds[panelIndex],
                        counters);
                }

                for (int panelIndex = 0; panelIndex < yPanelCount; panelIndex++)
                {
                    string midNodeId = GetOrCreateOrthogonalKMidNode(model, nodesById, lineTrussId, panelIndex, bottomNodeIds, topNodeIds);

                    AddMember(model, lineTrussId, ParametricMemberGroups.YZDiagonal, bottomNodeIds[panelIndex], midNodeId, counters);
                    AddMember(model, lineTrussId, ParametricMemberGroups.YZDiagonal, topNodeIds[panelIndex], midNodeId, counters);
                    AddMember(model, lineTrussId, ParametricMemberGroups.YZDiagonal, midNodeId, bottomNodeIds[panelIndex + 1], counters);
                    AddMember(model, lineTrussId, ParametricMemberGroups.YZDiagonal, midNodeId, topNodeIds[panelIndex + 1], counters);
                }
            }
            else
            {
                for (int panelIndex = 1; panelIndex < yPanelCount; panelIndex++)
                {
                    AddMember(
                        model,
                        lineTrussId,
                        ParametricMemberGroups.YZVertical,
                        bottomNodeIds[panelIndex],
                        topNodeIds[panelIndex],
                        counters);
                }

                if (model.OrthogonalYZTrussType != TrussType.SimpleFrame)
                {
                    for (int panelIndex = 0; panelIndex < yPanelCount; panelIndex++)
                    {
                        (string Start, string End) diagonal = model.OrthogonalYZTrussType switch
                        {
                            TrussType.Warren => panelIndex % 2 == 0
                                ? (bottomNodeIds[panelIndex], topNodeIds[panelIndex + 1])
                                : (topNodeIds[panelIndex], bottomNodeIds[panelIndex + 1]),
                            TrussType.Pratt => panelIndex < yPanelCount / 2.0
                                ? (bottomNodeIds[panelIndex], topNodeIds[panelIndex + 1])
                                : (topNodeIds[panelIndex], bottomNodeIds[panelIndex + 1]),
                            TrussType.Howe => panelIndex < yPanelCount / 2.0
                                ? (topNodeIds[panelIndex], bottomNodeIds[panelIndex + 1])
                                : (bottomNodeIds[panelIndex], topNodeIds[panelIndex + 1]),
                            _ => (bottomNodeIds[panelIndex], topNodeIds[panelIndex + 1])
                        };

                        AddMember(model, lineTrussId, ParametricMemberGroups.YZDiagonal, diagonal.Start, diagonal.End, counters);
                    }
                }
            }

            AssignMemberSections(model);
            model.OrthogonalYZTrussLineCount++;
        }
    }

    private static bool HasRequiredOrthogonalBoundaryNodes(
        IReadOnlyDictionary<string, ParametricNode> nodesById,
        int xIndex,
        int yBayLineCount)
    {
        for (int bayLineIndex = 0; bayLineIndex < yBayLineCount; bayLineIndex++)
        {
            if (!nodesById.ContainsKey(ExistingYBayNodeId(BottomNodeId(xIndex), bayLineIndex)) ||
                !nodesById.ContainsKey(ExistingYBayNodeId(TopNodeId(xIndex), bayLineIndex)))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetOrCreateOrthogonalYZChordNode(
        ParametricTrussModel model,
        Dictionary<string, ParametricNode> nodesById,
        string lineTrussId,
        int xIndex,
        int stationIndex,
        int panelsPerBay,
        bool isTop)
    {
        int bayLineIndex = stationIndex / panelsPerBay;
        int localPanelIndex = stationIndex % panelsPerBay;
        string baseNodeId = isTop ? TopNodeId(xIndex) : BottomNodeId(xIndex);
        if (localPanelIndex == 0)
            return ExistingYBayNodeId(baseNodeId, bayLineIndex);

        string nodeId = BuildOrthogonalPanelNodeId(lineTrussId, isTop, stationIndex);
        if (nodesById.ContainsKey(nodeId))
            return nodeId;

        ParametricNode start = nodesById[ExistingYBayNodeId(baseNodeId, bayLineIndex)];
        ParametricNode end = nodesById[ExistingYBayNodeId(baseNodeId, bayLineIndex + 1)];
        double t = localPanelIndex / (double)panelsPerBay;

        var node = new ParametricNode
        {
            Id = nodeId,
            X = start.X + (end.X - start.X) * t,
            Y = start.Y + (end.Y - start.Y) * t,
            Z = start.Z + (end.Z - start.Z) * t,
            PreviewX = start.PreviewX + (end.PreviewX - start.PreviewX) * t,
            PreviewZ = start.PreviewZ + (end.PreviewZ - start.PreviewZ) * t,
            IsTopChord = isTop,
            IsBottomChord = !isTop,
            SupportGroupId = $"YZ_X{xIndex:00}"
        };

        model.Nodes.Add(node);
        nodesById[node.Id] = node;
        return node.Id;
    }

    private static string GetOrCreateOrthogonalKMidNode(
        ParametricTrussModel model,
        Dictionary<string, ParametricNode> nodesById,
        string lineTrussId,
        int panelIndex,
        IReadOnlyList<string> bottomNodeIds,
        IReadOnlyList<string> topNodeIds)
    {
        string midNodeId = BuildOrthogonalMidNodeId(lineTrussId, panelIndex);
        if (nodesById.ContainsKey(midNodeId))
            return midNodeId;

        ParametricNode leftBottom = nodesById[bottomNodeIds[panelIndex]];
        ParametricNode rightBottom = nodesById[bottomNodeIds[panelIndex + 1]];
        ParametricNode leftTop = nodesById[topNodeIds[panelIndex]];
        ParametricNode rightTop = nodesById[topNodeIds[panelIndex + 1]];

        var node = new ParametricNode
        {
            Id = midNodeId,
            X = (leftBottom.X + rightBottom.X + leftTop.X + rightTop.X) / 4.0,
            Y = (leftBottom.Y + rightBottom.Y + leftTop.Y + rightTop.Y) / 4.0,
            Z = (leftBottom.Z + rightBottom.Z + leftTop.Z + rightTop.Z) / 4.0,
            PreviewX = (leftBottom.PreviewX + rightBottom.PreviewX + leftTop.PreviewX + rightTop.PreviewX) / 4.0,
            PreviewZ = (leftBottom.PreviewZ + rightBottom.PreviewZ + leftTop.PreviewZ + rightTop.PreviewZ) / 4.0,
            SupportGroupId = lineTrussId
        };

        model.Nodes.Add(node);
        nodesById[node.Id] = node;
        return node.Id;
    }

    private static string ExistingYBayNodeId(string baseNodeId, int bayIndex)
    {
        return bayIndex == 0 ? baseNodeId : BuildYBayScopedId(baseNodeId, bayIndex);
    }

    private static string BuildOrthogonalMidNodeId(string lineTrussId, int panelIndex)
    {
        return EtabsNameUtility.BuildSafeName("", $"{lineTrussId}_K_{panelIndex:000}");
    }

    private static string BuildOrthogonalPanelNodeId(string lineTrussId, bool isTop, int stationIndex)
    {
        string chordCode = isTop ? "T" : "B";
        return EtabsNameUtility.BuildSafeName("", $"{lineTrussId}_{chordCode}Y_{stationIndex:000}");
    }

    private static List<int> BuildOrthogonalXStationIndices(int panelCount, OrthogonalTrussPlacementMode placementMode)
    {
        int clampedPanelCount = Math.Max(2, panelCount);
        return placementMode switch
        {
            OrthogonalTrussPlacementMode.EndLinesOnly => [0, clampedPanelCount],
            OrthogonalTrussPlacementMode.EveryOtherPanelPoint => BuildEveryOtherPanelStationIndices(clampedPanelCount),
            _ => Enumerable.Range(0, clampedPanelCount + 1).ToList()
        };
    }

    private static List<int> BuildEveryOtherPanelStationIndices(int panelCount)
    {
        var indices = new List<int>();
        for (int index = 0; index <= panelCount; index += 2)
            indices.Add(index);

        if (indices[^1] != panelCount)
            indices.Add(panelCount);

        return indices;
    }

    private static void CopySectionAssignment(ParametricTrussModel model, string sourceGroup, string targetGroup)
    {
        model.SectionAssignments[targetGroup] = model.SectionAssignments.TryGetValue(sourceGroup, out string? sectionName)
            ? sectionName
            : "";
    }

    private static TrussType ToStandardTrussType(TrussType trussType)
    {
        return trussType switch
        {
            TrussType.Pratt => TrussType.Pratt,
            TrussType.Howe => TrussType.Howe,
            TrussType.K => TrussType.K,
            TrussType.SimpleFrame => TrussType.SimpleFrame,
            _ => TrussType.Warren
        };
    }

    private ParametricTrussModel GenerateSpiralStaircase(ParametricTrussOptions options)
    {
        string trussId = BuildTypedTrussId(options.TrussId, "SPIRAL_001");
        int stepCount = Math.Clamp(options.SpiralStepCount, 3, 400);
        double innerRadius = Math.Max(0.001, options.SpiralInnerRadius);
        double outerRadius = Math.Max(innerRadius + 0.001, options.SpiralOuterRadius);
        double totalHeight = Math.Max(0.001, options.SpiralTotalHeight);
        double signedRotation = Math.Abs(options.SpiralTotalRotationDegrees);
        if (options.SpiralRotationDirection == SpiralStairRotationDirection.Clockwise)
            signedRotation = -signedRotation;

        var model = new ParametricTrussModel
        {
            TrussId = trussId,
            GroupName = BuildGroupName(options.GroupName, "", "PM_SPIRAL_STAIR_001"),
            TrussType = TrussType.SpiralStaircase,
            Span = outerRadius * 2.0,
            Height = totalHeight,
            PanelCount = stepCount,
            SupportNodeMode = SupportNodeMode.NoSupports,
            SupportRestraintType = options.SupportRestraintType,
            SectionAssignments = new Dictionary<string, string>(options.SectionAssignments, StringComparer.OrdinalIgnoreCase)
        };

        var nodesById = new Dictionary<string, ParametricNode>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index <= stepCount; index++)
        {
            double t = index / (double)stepCount;
            double angle = DegreesToRadians(options.SpiralStartAngleDegrees + signedRotation * t);
            double z = options.SpiralBaseZ + totalHeight * t;
            AddNode(model, nodesById, BuildSpiralNode($"IN_{index:000}", options, innerRadius, angle, z, isBottom: index == 0, isTop: false));
            AddNode(model, nodesById, BuildSpiralNode($"OUT_{index:000}", options, outerRadius, angle, z, isBottom: false, isTop: index == stepCount));
        }

        if (options.SpiralCreateCentralColumn)
        {
            AddNode(model, nodesById, new ParametricNode
            {
                Id = "COL_BOT",
                X = options.SpiralCentreX,
                Y = options.SpiralCentreY,
                Z = options.SpiralBaseZ,
                PreviewX = 0,
                PreviewZ = 0,
                IsSupport = true
            });
            AddNode(model, nodesById, new ParametricNode
            {
                Id = "COL_TOP",
                X = options.SpiralCentreX,
                Y = options.SpiralCentreY,
                Z = options.SpiralBaseZ + totalHeight,
                PreviewX = 0,
                PreviewZ = 0
            });
        }

        var counters = ParametricMemberGroups.All.ToDictionary(group => group, _ => 0, StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < stepCount; index++)
        {
            if (options.SpiralCreateInnerStringer)
                AddMember(model, trussId, ParametricMemberGroups.InnerStringer, $"IN_{index:000}", $"IN_{index + 1:000}", counters);
            if (options.SpiralCreateOuterStringer)
                AddMember(model, trussId, ParametricMemberGroups.OuterStringer, $"OUT_{index:000}", $"OUT_{index + 1:000}", counters);
        }

        if (options.SpiralCreateRadialTreadBeams)
        {
            for (int index = 0; index <= stepCount; index++)
                AddMember(model, trussId, ParametricMemberGroups.RadialTread, $"IN_{index:000}", $"OUT_{index:000}", counters);
        }

        if (options.SpiralCreateCentralColumn)
            AddMember(model, trussId, ParametricMemberGroups.CentralColumn, "COL_BOT", "COL_TOP", counters);

        if (options.SpiralCreateBottomLandingBeam)
            AddMember(model, trussId, ParametricMemberGroups.LandingBeam, "COL_BOT", "OUT_000", counters);
        if (options.SpiralCreateTopLandingBeam)
            AddMember(model, trussId, ParametricMemberGroups.LandingBeam, "COL_TOP", $"OUT_{stepCount:000}", counters);

        if (options.SpiralCreateTreadShellPlates)
        {
            for (int index = 0; index < stepCount; index++)
            {
                model.Shells.Add(new ParametricShell
                {
                    Id = EtabsNameUtility.BuildSafeName("", $"{trussId}_SHELL_{index + 1:000}"),
                    Group = "TreadShell",
                    ShellPropertyName = options.SpiralTreadShellProperty ?? "",
                    NodeIds = [$"IN_{index:000}", $"OUT_{index:000}", $"OUT_{index + 1:000}", $"IN_{index + 1:000}"]
                });
            }
        }

        AssignMemberSections(model);
        if (totalHeight / stepCount > 0.2)
            model.Warnings.Add($"Step rise is {totalHeight / stepCount * 1000.0:0.#} mm, greater than 200 mm.");
        if (outerRadius - innerRadius < 0.75)
            model.Warnings.Add("Outer radius is close to inner radius; stair tread width may be narrow.");

        return model;
    }

    private ParametricTrussModel GenerateFishBellyTruss(ParametricTrussOptions options)
    {
        string trussId = BuildTypedTrussId(options.TrussId, "FBT_001");
        int panelCount = Math.Clamp(options.FishPanelCount, 2, 60);
        double span = Math.Max(0.001, options.FishSpanLength);
        double endDepth = Math.Max(0.001, options.FishEndDepth);
        double middleDepth = Math.Max(endDepth + 0.001, options.FishMiddleDepth);
        double angle = DegreesToRadians(options.FishDirectionAngleDegrees);
        double topSlope = Math.Tan(DegreesToRadians(options.FishTopChordSlopeDegrees));
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        var model = new ParametricTrussModel
        {
            TrussId = trussId,
            GroupName = BuildGroupName(options.GroupName, "", "PM_FISH_BELLY_TRUSS_001"),
            TrussType = TrussType.FishBellyTruss,
            Span = span,
            Height = middleDepth,
            PanelCount = panelCount,
            SupportNodeMode = options.SupportNodeMode,
            SupportRestraintType = options.SupportRestraintType,
            SectionAssignments = new Dictionary<string, string>(options.SectionAssignments, StringComparer.OrdinalIgnoreCase)
        };

        var nodesById = new Dictionary<string, ParametricNode>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index <= panelCount; index++)
        {
            double s = index / (double)panelCount;
            double xLocal = span * s;
            double depth = CalculateFishDepth(s, span, endDepth, middleDepth, options.FishBottomChordShape);
            double topLocalZ = xLocal * topSlope;
            (double X, double Y) top = RotateAndTranslate(options.FishStartX, options.FishStartY, xLocal, cos, sin);

            AddNode(model, nodesById, new ParametricNode
            {
                Id = TopNodeId(index),
                X = top.X,
                Y = top.Y,
                Z = options.FishStartZ + topLocalZ,
                PreviewX = xLocal,
                PreviewZ = topLocalZ,
                IsTopChord = true
            });

            AddNode(model, nodesById, new ParametricNode
            {
                Id = BottomNodeId(index),
                X = top.X,
                Y = top.Y,
                Z = options.FishStartZ + topLocalZ - depth,
                PreviewX = xLocal,
                PreviewZ = topLocalZ - depth,
                IsSupport = IsSupportNode(index, panelCount, options.SupportNodeMode),
                IsBottomChord = true
            });
        }

        model.Height = Math.Max(
            0.001,
            model.Nodes.Max(node => node.PreviewZ) - model.Nodes.Min(node => node.PreviewZ));

        var counters = ParametricMemberGroups.All.ToDictionary(group => group, _ => 0, StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < panelCount; index++)
        {
            AddMember(model, trussId, ParametricMemberGroups.TopChord, TopNodeId(index), TopNodeId(index + 1), counters, options.FishReleaseMoments);
            AddMember(model, trussId, ParametricMemberGroups.BottomChord, BottomNodeId(index), BottomNodeId(index + 1), counters, options.FishReleaseMoments);
        }

        bool addVerticals = options.FishWebPattern != FishBellyWebPattern.Warren;
        if (addVerticals)
        {
            for (int index = 0; index <= panelCount; index++)
                AddMember(model, trussId, ParametricMemberGroups.Vertical, TopNodeId(index), BottomNodeId(index), counters, options.FishReleaseMoments);
        }

        for (int index = 0; index < panelCount; index++)
        {
            foreach ((string Start, string End) diagonal in BuildFishDiagonals(index, panelCount, options.FishWebPattern))
                AddMember(model, trussId, ParametricMemberGroups.Diagonal, diagonal.Start, diagonal.End, counters, options.FishReleaseMoments);
        }

        AssignMemberSections(model);
        if (span / panelCount < 0.5)
            model.Warnings.Add("Fish-belly panel length is less than 0.5 m.");
        if (middleDepth > span / 3.0)
            model.Warnings.Add("Middle depth is large compared with span; verify transport and analysis assumptions.");

        return model;
    }

    private ParametricTrussModel GenerateVariablePanelWidthTruss(ParametricTrussOptions options)
    {
        string trussId = BuildTypedTrussId(options.TrussId, "VPT_001");
        int panelCount = Math.Clamp(options.VariablePanelCount, 2, 80);
        double span = Math.Max(0.001, options.VariablePanelSpanLength);
        double depth = Math.Max(0.001, options.VariablePanelTrussDepth);
        double endRatio = Math.Max(0.001, options.VariablePanelEndWidthRatio);
        double middleRatio = Math.Max(endRatio + 0.001, options.VariablePanelMiddleWidthRatio);
        double[] stationX = BuildVariablePanelStations(span, panelCount, endRatio, middleRatio, options.VariablePanelWidthVariation);
        double angle = DegreesToRadians(options.VariablePanelDirectionAngleDegrees);
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        var model = new ParametricTrussModel
        {
            TrussId = trussId,
            GroupName = BuildGroupName(options.GroupName, "", "PM_VARIABLE_PANEL_TRUSS_001"),
            TrussType = TrussType.VariablePanelWidthTruss,
            Span = span,
            Height = depth,
            PanelCount = panelCount,
            SupportNodeMode = options.SupportNodeMode,
            SupportRestraintType = options.SupportRestraintType,
            SectionAssignments = new Dictionary<string, string>(options.SectionAssignments, StringComparer.OrdinalIgnoreCase)
        };

        var nodesById = new Dictionary<string, ParametricNode>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index <= panelCount; index++)
        {
            double xLocal = stationX[index];
            (double X, double Y) top = RotateAndTranslate(options.VariablePanelStartX, options.VariablePanelStartY, xLocal, cos, sin);

            AddNode(model, nodesById, new ParametricNode
            {
                Id = TopNodeId(index),
                X = top.X,
                Y = top.Y,
                Z = options.VariablePanelStartZ,
                PreviewX = xLocal,
                PreviewZ = 0,
                IsTopChord = true
            });

            AddNode(model, nodesById, new ParametricNode
            {
                Id = BottomNodeId(index),
                X = top.X,
                Y = top.Y,
                Z = options.VariablePanelStartZ - depth,
                PreviewX = xLocal,
                PreviewZ = -depth,
                IsSupport = IsSupportNode(index, panelCount, options.SupportNodeMode),
                IsBottomChord = true
            });
        }

        var counters = ParametricMemberGroups.All.ToDictionary(group => group, _ => 0, StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < panelCount; index++)
        {
            AddMember(model, trussId, ParametricMemberGroups.TopChord, TopNodeId(index), TopNodeId(index + 1), counters, options.VariablePanelReleaseMoments);
            AddMember(model, trussId, ParametricMemberGroups.BottomChord, BottomNodeId(index), BottomNodeId(index + 1), counters, options.VariablePanelReleaseMoments);
        }

        for (int index = 0; index <= panelCount; index++)
            AddMember(model, trussId, ParametricMemberGroups.Vertical, TopNodeId(index), BottomNodeId(index), counters, options.VariablePanelReleaseMoments);

        for (int index = 0; index < panelCount; index++)
        {
            foreach ((string Start, string End) diagonal in BuildFishDiagonals(index, panelCount, options.VariablePanelWebPattern))
                AddMember(model, trussId, ParametricMemberGroups.Diagonal, diagonal.Start, diagonal.End, counters, options.VariablePanelReleaseMoments);
        }

        AssignMemberSections(model);

        double minPanelLength = stationX.Zip(stationX.Skip(1), (start, end) => end - start).Min();
        double maxPanelLength = stationX.Zip(stationX.Skip(1), (start, end) => end - start).Max();
        if (minPanelLength < 0.5)
            model.Warnings.Add($"Smallest variable panel length is {minPanelLength:0.###} m; verify connection spacing.");
        if (maxPanelLength > span / 3.0)
            model.Warnings.Add($"Largest variable panel length is {maxPanelLength:0.###} m; verify chord buckling assumptions.");
        if (maxPanelLength / Math.Max(minPanelLength, 0.000001) > 5.0)
            model.Warnings.Add("Largest variable panel length is more than 5 times the smallest panel length.");

        return model;
    }

    private static string TopNodeId(int index)
    {
        return $"T{index:00}";
    }

    private static string BottomNodeId(int index)
    {
        return $"B{index:00}";
    }

    private static void AddNode(
        ParametricTrussModel model,
        Dictionary<string, ParametricNode> nodesById,
        ParametricNode node)
    {
        nodesById[node.Id] = node;
        model.Nodes.Add(node);
    }

    private static void AddMember(
        ParametricTrussModel model,
        string trussId,
        string group,
        string startNodeId,
        string endNodeId,
        Dictionary<string, int> counters,
        bool releaseMoments = false)
    {
        counters[group]++;
        model.Members.Add(new ParametricMember
        {
            Id = BuildMemberId(trussId, group, counters[group]),
            Group = group,
            StartNodeId = startNodeId,
            EndNodeId = endNodeId,
            ReleaseMoments = releaseMoments
        });
    }

    private static string BuildMemberId(string trussId, string group, int index)
    {
        string groupCode = group switch
        {
            ParametricMemberGroups.TopChord => "TOP",
            ParametricMemberGroups.BottomChord => "BOT",
            ParametricMemberGroups.Diagonal => "DIAG",
            ParametricMemberGroups.Vertical => "VERT",
            ParametricMemberGroups.EndPost => "END",
            ParametricMemberGroups.YZTopChord => "YZTOP",
            ParametricMemberGroups.YZBottomChord => "YZBOT",
            ParametricMemberGroups.YZDiagonal => "YZDIAG",
            ParametricMemberGroups.YZVertical => "YZVERT",
            ParametricMemberGroups.YZEndPost => "YZEND",
            ParametricMemberGroups.InnerStringer => "IN",
            ParametricMemberGroups.OuterStringer => "OUT",
            ParametricMemberGroups.RadialTread => "RAD",
            ParametricMemberGroups.CentralColumn => "COL",
            ParametricMemberGroups.LandingBeam => "LAN",
            _ => "SEC"
        };

        return EtabsNameUtility.BuildSafeName("", $"{trussId}_{groupCode}_{EtabsNameUtility.FormatIndex(index)}");
    }

    private static string BuildTypedTrussId(string requestedTrussId, string fallbackTrussId)
    {
        string raw = (requestedTrussId ?? "").Trim();
        string requested = EtabsNameUtility.BuildSafeName("", requestedTrussId, 24);
        if (raw.Length == 0 ||
            string.Equals(requested, "TR01", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requested, "TRUSS", StringComparison.OrdinalIgnoreCase) ||
            requested.StartsWith("WPF_TRUSS_", StringComparison.OrdinalIgnoreCase))
        {
            return fallbackTrussId;
        }

        return requested;
    }

    private static string BuildGroupName(string requestedGroupName, string prefix, string fallback)
    {
        string raw = string.IsNullOrWhiteSpace(requestedGroupName) ? prefix + fallback : requestedGroupName;
        return EtabsNameUtility.BuildSafeName("", raw);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static double CalculateSlopeRise(double span, double t, double slopePercent, ChordSlopeMode slopeMode)
    {
        return slopeMode == ChordSlopeMode.OneSided
            ? CalculateLinearRise(span, t, slopePercent)
            : CalculateCrownRise(span, t, slopePercent);
    }

    private static double CalculateCrownRise(double span, double t, double roofSlopePercent)
    {
        if (!double.IsFinite(roofSlopePercent) || Math.Abs(roofSlopePercent) < 0.000001)
            return 0;

        double midRise = span * roofSlopePercent / 200.0;
        return midRise * (1.0 - Math.Abs(2.0 * t - 1.0));
    }

    private static double CalculateLinearRise(double span, double t, double slopePercent)
    {
        if (!double.IsFinite(slopePercent) || Math.Abs(slopePercent) < 0.000001)
            return 0;

        return span * slopePercent / 100.0 * t;
    }

    private static bool IsSupportNode(int index, int panelCount, SupportNodeMode supportNodeMode)
    {
        return supportNodeMode switch
        {
            SupportNodeMode.AllBottomChordNodes => true,
            SupportNodeMode.NoSupports => false,
            _ => index == 0 || index == panelCount
        };
    }

    private static void AddLoads(ParametricTrussModel model, ParametricTrussOptions options)
    {
        if (options.LoadDefinitions.Count > 0)
        {
            AddConfiguredLoads(model, options.LoadDefinitions);
            return;
        }

        string loadPattern = (options.LoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
            return;

        AddTopChordGravityLoad(model, options, loadPattern);
        AddBottomChordLoad(model, options, loadPattern);
    }

    private static void AddConfiguredLoads(ParametricTrussModel model, IReadOnlyList<ParametricTrussLoadDefinition> loadDefinitions)
    {
        for (int index = 0; index < loadDefinitions.Count; index++)
        {
            ParametricTrussLoadDefinition definition = loadDefinitions[index];
            string loadPattern = (definition.LoadPattern ?? "").Trim();
            if (loadPattern.Length == 0)
                continue;

            double lineLoadKnPerM = definition.EquivalentLineLoadKnPerM;
            if (!double.IsFinite(lineLoadKnPerM) || Math.Abs(lineLoadKnPerM) < 0.000001 || model.PanelCount <= 0)
                continue;

            string targetCode = definition.Target == ParametricTrussLoadTarget.BottomChord ? "BOT" : "TOP";
            string suffix = EtabsNameUtility.FormatIndex(index + 1);
            string memberGroup = definition.Target == ParametricTrussLoadTarget.BottomChord
                ? ParametricMemberGroups.BottomChord
                : ParametricMemberGroups.TopChord;
            Func<ParametricNode, bool> nodePredicate = definition.Target == ParametricTrussLoadTarget.BottomChord
                ? node => node.IsBottomChord
                : node => node.IsTopChord;

            AddChordGravityLoad(
                model,
                loadPattern,
                lineLoadKnPerM,
                definition.ApplicationMode == ParametricTrussLoadApplicationMode.PanelNodes,
                memberGroup,
                nodePredicate,
                $"L_{targetCode}_LINE_{suffix}",
                $"L_{targetCode}_{suffix}");
        }
    }

    private static void AddTopChordGravityLoad(ParametricTrussModel model, ParametricTrussOptions options, string loadPattern)
    {
        if (!options.ApplyTopChordLoad)
            return;

        double load = options.TopChordGravityLoadKnPerM;
        if (!double.IsFinite(load) || Math.Abs(load) < 0.000001 || model.PanelCount <= 0)
            return;

        AddChordGravityLoad(
            model,
            loadPattern,
            load,
            options.ApplyTopChordLoadToPanelNodes,
            ParametricMemberGroups.TopChord,
            node => node.IsTopChord,
            "L_TOP_LINE",
            "L_TOP");
    }

    private static void AddBottomChordLoad(ParametricTrussModel model, ParametricTrussOptions options, string loadPattern)
    {
        if (!options.ApplyBottomChordLoad)
            return;

        double load = options.BottomChordLoadMagnitude;
        if (!double.IsFinite(load) || Math.Abs(load) < 0.000001 || model.PanelCount <= 0)
            return;

        AddChordGravityLoad(
            model,
            loadPattern,
            load,
            options.ApplyBottomChordLoadToPanelNodes,
            ParametricMemberGroups.BottomChord,
            node => node.IsBottomChord,
            "L_BOT_LINE",
            "L_BOT");
    }

    private static void AddChordGravityLoad(
        ParametricTrussModel model,
        string loadPattern,
        double loadKnPerM,
        bool applyToPanelNodes,
        string memberGroup,
        Func<ParametricNode, bool> nodePredicate,
        string lineLoadId,
        string pointLoadPrefix)
    {
        double loadMagnitude = -Math.Abs(loadKnPerM);

        if (!applyToPanelNodes)
        {
            model.Loads.Add(new ParametricLoad
            {
                Id = lineLoadId,
                LoadPattern = loadPattern,
                TargetType = "MemberGroup",
                TargetId = memberGroup,
                Direction = "GlobalZ",
                Magnitude = loadMagnitude
            });
            return;
        }

        double panelLength = model.Span / model.PanelCount;
        List<ParametricNode> targetNodes = model.Nodes
            .Where(nodePredicate)
            .OrderBy(node => node.PreviewX)
            .ToList();
        int loadIndex = 1;
        for (int index = 0; index < targetNodes.Count; index++)
        {
            ParametricNode node = targetNodes[index];
            bool isEndNode = index == 0 || index == targetNodes.Count - 1;
            double tributaryLength = isEndNode ? panelLength / 2.0 : panelLength;

            model.Loads.Add(new ParametricLoad
            {
                Id = $"{pointLoadPrefix}_{loadIndex:000}",
                LoadPattern = loadPattern,
                TargetType = "Node",
                TargetId = node.Id,
                Direction = "GlobalZ",
                Magnitude = loadMagnitude * tributaryLength
            });
            loadIndex++;
        }
    }
}
