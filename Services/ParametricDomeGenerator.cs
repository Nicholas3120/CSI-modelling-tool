using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class ParametricDomeOptions
{
    public string DomeId { get; set; } = "D01";
    public DomeType DomeType { get; set; } = DomeType.SphericalCap;
    public DomeShellMeshType ShellMeshType { get; set; } = DomeShellMeshType.Triangular;
    public DomeGeometryInputMode GeometryInputMode { get; set; } = DomeGeometryInputMode.RiseAndCutHeights;
    public DomeRingSpacingMode RingSpacingMode { get; set; } = DomeRingSpacingMode.EqualHeight;
    public double BaseCenterX { get; set; }
    public double BaseCenterY { get; set; }
    public double BaseElevationZ { get; set; }
    public double BaseRadius { get; set; } = 20.0;
    public double DomeRise { get; set; } = 8.0;
    public double LowerCutHeight { get; set; }
    public double UpperCutHeight { get; set; } = 8.0;
    public double PartialDomeHeight { get; set; } = 3.0;
    public double CrownRingRadius { get; set; }
    public int RingCount { get; set; } = 8;
    public int SegmentCount { get; set; } = 24;
    public double StartAngleDeg { get; set; }
    public double EndAngleDeg { get; set; } = 360.0;
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
}

public sealed class ParametricDomeGenerator
{
    private const double MinRingRadius = 0.0001;
    private const double ApexCutTolerance = 0.000001;

    public ParametricDomeModel Generate(ParametricDomeOptions options)
    {
        string domeId = EtabsNameUtility.BuildSafeName("", options.DomeId, 24);
        double baseRadius = PositiveOrDefault(options.BaseRadius, 20.0);
        double rise = PositiveOrDefault(options.DomeRise, 8.0);
        double lowerCut = Math.Clamp(FiniteOrDefault(options.LowerCutHeight, 0.0), 0.0, rise);
        double upperCut = Math.Clamp(FiniteOrDefault(options.UpperCutHeight, rise), 0.0, rise);
        double partialDomeHeight = PositiveOrDefault(options.PartialDomeHeight, Math.Max(upperCut - lowerCut, 0.1));
        double crownRingRadius = Math.Max(0.0, FiniteOrDefault(options.CrownRingRadius, 0.0));
        var setupWarnings = new List<string>();

        if (options.GeometryInputMode == DomeGeometryInputMode.PartialHeightTopRadius)
        {
            lowerCut = 0.0;
            upperCut = partialDomeHeight;
            double maximumTopRadius = Math.Max(0.0, baseRadius - MinRingRadius);
            double requestedTopRadius = crownRingRadius;
            crownRingRadius = Math.Clamp(crownRingRadius, 0.0, maximumTopRadius);
            rise = upperCut;

            if (Math.Abs(requestedTopRadius - crownRingRadius) > 0.001)
                setupWarnings.Add($"Top radius was adjusted to {crownRingRadius:0.###} m so it remains smaller than the base radius.");

            setupWarnings.Add($"Partial dome input uses a monotonic tapered radius profile from {baseRadius:0.###} m at base to {crownRingRadius:0.###} m at height {upperCut:0.###} m.");
        }

        if (upperCut <= lowerCut)
            upperCut = Math.Min(rise, lowerCut + Math.Max(rise * 0.1, 0.1));

        int ringCount = Math.Clamp(options.RingCount, 2, 200);
        int segmentCount = Math.Clamp(options.SegmentCount, options.Full360 ? 6 : 1, 360);

        var model = new ParametricDomeModel
        {
            DomeId = domeId,
            GroupName = EtabsNameUtility.BuildSafeName("WPF_DOME_", domeId),
            DomeType = options.DomeType,
            ShellMeshType = options.ShellMeshType,
            GeometryInputMode = options.GeometryInputMode,
            RingSpacingMode = options.RingSpacingMode,
            BaseCenterX = FiniteOrDefault(options.BaseCenterX, 0.0),
            BaseCenterY = FiniteOrDefault(options.BaseCenterY, 0.0),
            BaseElevationZ = FiniteOrDefault(options.BaseElevationZ, 0.0),
            BaseRadius = baseRadius,
            DomeRise = rise,
            LowerCutHeight = lowerCut,
            UpperCutHeight = upperCut,
            PartialDomeHeight = partialDomeHeight,
            CrownRingRadius = crownRingRadius,
            RingCount = ringCount,
            SegmentCount = segmentCount,
            StartAngleDeg = FiniteOrDefault(options.StartAngleDeg, 0.0),
            EndAngleDeg = options.Full360 ? FiniteOrDefault(options.StartAngleDeg, 0.0) + 360.0 : FiniteOrDefault(options.EndAngleDeg, 90.0),
            Full360 = options.Full360,
            GenerateShellPanels = options.GenerateShellPanels,
            GenerateRingFrames = options.GenerateRingFrames,
            GenerateRadialFrames = options.GenerateRadialFrames,
            GenerateDiagonalFrames = options.GenerateDiagonalFrames,
            GenerateBaseRing = options.GenerateBaseRing,
            GenerateCrownRing = options.GenerateCrownRing,
            GenerateSupportsAtBase = options.GenerateSupportsAtBase,
            ShellPropertyName = options.ShellPropertyName ?? "",
            RingSectionName = options.RingSectionName ?? "",
            RadialSectionName = options.RadialSectionName ?? "",
            DiagonalSectionName = options.DiagonalSectionName ?? "",
            BaseRingSectionName = options.BaseRingSectionName ?? "",
            CrownRingSectionName = options.CrownRingSectionName ?? ""
        };
        model.Warnings.AddRange(setupWarnings);

        List<List<DomeNode>> rings = BuildRings(model);
        AddShellPanels(model, rings);
        AddFrameMembers(model, rings);

        if (HasSeparateApexNode(model, rings))
            model.Warnings.Add("Upper cut reaches the dome apex. The crown ring beam is kept as a finite ring below the top, then shell/radial members close to a single apex node.");

        if (model.CrownRingRadius > MinRingRadius && model.GeometryInputMode != DomeGeometryInputMode.PartialHeightTopRadius)
        {
            if (HasSeparateApexNode(model, rings))
            {
                double actualCrownRadius = GetRingRadius(model, rings[^2]);
                string message = $"Crown ring radius override is active. Last ring beam radius: {actualCrownRadius:0.###} m.";
                if (Math.Abs(actualCrownRadius - model.CrownRingRadius) > 0.001)
                    message += $" Requested {model.CrownRingRadius:0.###} m was adjusted to fit the spherical cap.";
                model.Warnings.Add(message);
            }
            else
                model.Warnings.Add("Crown ring radius override is only applied when upper cut height reaches the dome rise.");
        }

        if (model.ShellMeshType == DomeShellMeshType.Quad)
            model.Warnings.Add("Quad shell panels on curved dome surfaces may be warped. Use triangular mesh if ETABS reports shell geometry issues.");

        if (!model.Full360)
            model.Warnings.Add("Partial angular dome sector has open radial boundary edges.");

        return model;
    }

    private static List<List<DomeNode>> BuildRings(ParametricDomeModel model)
    {
        if (model.GeometryInputMode == DomeGeometryInputMode.PartialHeightTopRadius)
            return BuildPartialHeightTopRadiusRings(model);

        double sphereRadius = (model.BaseRadius * model.BaseRadius + model.DomeRise * model.DomeRise) / (2.0 * model.DomeRise);
        double sphereCenterLocalZ = model.DomeRise - sphereRadius;
        double startAngle = DegreesToRadians(model.StartAngleDeg);
        double endAngle = DegreesToRadians(model.EndAngleDeg);
        int nodesPerNormalRing = model.Full360 ? model.SegmentCount : model.SegmentCount + 1;
        bool addSeparateApexNode = ReachesApex(model);

        return model.RingSpacingMode switch
        {
            DomeRingSpacingMode.EqualRadius => BuildEqualRadiusRings(model, sphereRadius, sphereCenterLocalZ, startAngle, endAngle, nodesPerNormalRing, addSeparateApexNode),
            DomeRingSpacingMode.HybridTopEqualRadius => BuildHybridTopEqualRadiusRings(model, sphereRadius, sphereCenterLocalZ, startAngle, endAngle, nodesPerNormalRing, addSeparateApexNode),
            _ => BuildEqualHeightRings(model, sphereRadius, sphereCenterLocalZ, startAngle, endAngle, nodesPerNormalRing, addSeparateApexNode)
        };
    }

    private static List<List<DomeNode>> BuildPartialHeightTopRadiusRings(ParametricDomeModel model)
    {
        double startAngle = DegreesToRadians(model.StartAngleDeg);
        double endAngle = DegreesToRadians(model.EndAngleDeg);
        int nodesPerNormalRing = model.Full360 ? model.SegmentCount : model.SegmentCount + 1;
        double topRadius = Math.Clamp(model.CrownRingRadius, 0.0, model.BaseRadius);

        var rings = new List<List<DomeNode>>();
        for (int ringIndex = 0; ringIndex < model.RingCount; ringIndex++)
        {
            double t = model.RingCount <= 1 ? 0.0 : (double)ringIndex / (model.RingCount - 1);
            double zLocal = model.UpperCutHeight * t;
            double ringRadius = model.BaseRadius + (topRadius - model.BaseRadius) * t;
            rings.Add(BuildRing(model, ringIndex, ringRadius, zLocal, startAngle, endAngle, nodesPerNormalRing));
        }

        return rings;
    }

    private static List<List<DomeNode>> BuildEqualHeightRings(
        ParametricDomeModel model,
        double sphereRadius,
        double sphereCenterLocalZ,
        double startAngle,
        double endAngle,
        int nodesPerNormalRing,
        bool addSeparateApexNode)
    {
        bool useCrownRingRadius = addSeparateApexNode && model.CrownRingRadius > MinRingRadius;
        double upperRingCutHeight = useCrownRingRadius
            ? ResolveCrownRingCutHeight(model, sphereRadius, sphereCenterLocalZ)
            : model.UpperCutHeight;
        int ringFractionDenominator = addSeparateApexNode && !useCrownRingRadius
            ? model.RingCount
            : model.RingCount - 1;

        var rings = new List<List<DomeNode>>();
        for (int ringIndex = 0; ringIndex < model.RingCount; ringIndex++)
        {
            double t = ringFractionDenominator <= 0 ? 0.0 : (double)ringIndex / ringFractionDenominator;
            double zLocal = model.LowerCutHeight + (upperRingCutHeight - model.LowerCutHeight) * t;
            double ringRadius = GetRadiusAtLocalZ(sphereRadius, sphereCenterLocalZ, zLocal);
            rings.Add(BuildRing(model, ringIndex, ringRadius, zLocal, startAngle, endAngle, nodesPerNormalRing));
        }

        if (addSeparateApexNode)
            AddApexRing(model, rings);

        return rings;
    }

    private static List<List<DomeNode>> BuildEqualRadiusRings(
        ParametricDomeModel model,
        double sphereRadius,
        double sphereCenterLocalZ,
        double startAngle,
        double endAngle,
        int nodesPerNormalRing,
        bool addSeparateApexNode)
    {
        bool useCrownRingRadius = addSeparateApexNode && model.CrownRingRadius > MinRingRadius;
        double lowerRingRadius = GetRadiusAtLocalZ(sphereRadius, sphereCenterLocalZ, model.LowerCutHeight);
        double upperRingRadius = ResolveUpperRingRadius(model, sphereRadius, sphereCenterLocalZ, lowerRingRadius, addSeparateApexNode, useCrownRingRadius);
        int radiusIntervalCount = addSeparateApexNode && !useCrownRingRadius
            ? model.RingCount
            : model.RingCount - 1;
        double radiusStep = radiusIntervalCount <= 0
            ? 0.0
            : (lowerRingRadius - upperRingRadius) / radiusIntervalCount;

        var rings = new List<List<DomeNode>>();
        for (int ringIndex = 0; ringIndex < model.RingCount; ringIndex++)
        {
            double ringRadius = Math.Max(0.0, lowerRingRadius - radiusStep * ringIndex);
            double zLocal = GetLocalZAtRadius(sphereRadius, sphereCenterLocalZ, ringRadius);
            rings.Add(BuildRing(model, ringIndex, ringRadius, zLocal, startAngle, endAngle, nodesPerNormalRing));
        }

        if (addSeparateApexNode)
            AddApexRing(model, rings);

        return rings;
    }

    private static List<List<DomeNode>> BuildHybridTopEqualRadiusRings(
        ParametricDomeModel model,
        double sphereRadius,
        double sphereCenterLocalZ,
        double startAngle,
        double endAngle,
        int nodesPerNormalRing,
        bool addSeparateApexNode)
    {
        if (!addSeparateApexNode || model.CrownRingRadius > MinRingRadius)
            return BuildEqualHeightRings(model, sphereRadius, sphereCenterLocalZ, startAngle, endAngle, nodesPerNormalRing, addSeparateApexNode);

        var rings = new List<List<DomeNode>>();
        for (int ringIndex = 0; ringIndex < model.RingCount; ringIndex++)
        {
            double t = (double)ringIndex / model.RingCount;
            double zLocal = model.LowerCutHeight + (model.UpperCutHeight - model.LowerCutHeight) * t;
            double ringRadius = GetRadiusAtLocalZ(sphereRadius, sphereCenterLocalZ, zLocal);
            rings.Add(BuildRing(model, ringIndex, ringRadius, zLocal, startAngle, endAngle, nodesPerNormalRing));
        }

        double lastEqualHeightRadius = GetRingRadius(model, rings[^1]);
        int crownInfillRingCount = ResolveCrownInfillRingCount(model.RingCount);
        double radiusStep = lastEqualHeightRadius / (crownInfillRingCount + 1);

        for (int infillIndex = 1; infillIndex <= crownInfillRingCount; infillIndex++)
        {
            int ringIndex = model.RingCount + infillIndex - 1;
            double ringRadius = Math.Max(0.0, lastEqualHeightRadius - radiusStep * infillIndex);
            double zLocal = GetLocalZAtRadius(sphereRadius, sphereCenterLocalZ, ringRadius);
            rings.Add(BuildRing(model, ringIndex, ringRadius, zLocal, startAngle, endAngle, nodesPerNormalRing));
        }

        AddApexRing(model, rings);
        model.Warnings.Add($"Added {crownInfillRingCount} crown infill ring(s) above the equal-height dome rings.");

        return rings;
    }

    private static List<DomeNode> BuildRing(
        ParametricDomeModel model,
        int ringIndex,
        double ringRadius,
        double zLocal,
        double startAngle,
        double endAngle,
        int nodesPerNormalRing)
    {
        var ring = new List<DomeNode>();

        if (ringRadius <= MinRingRadius)
        {
            ring.Add(AddNode(model, ringIndex, 0, model.BaseCenterX, model.BaseCenterY, model.BaseElevationZ + zLocal));
            return ring;
        }

        for (int segmentIndex = 0; segmentIndex < nodesPerNormalRing; segmentIndex++)
        {
            double angleT = (double)segmentIndex / model.SegmentCount;
            double theta = startAngle + (endAngle - startAngle) * angleT;
            double x = model.BaseCenterX + ringRadius * Math.Cos(theta);
            double y = model.BaseCenterY + ringRadius * Math.Sin(theta);
            double z = model.BaseElevationZ + zLocal;
            ring.Add(AddNode(model, ringIndex, segmentIndex, x, y, z));
        }

        return ring;
    }

    private static void AddApexRing(ParametricDomeModel model, List<List<DomeNode>> rings)
    {
        rings.Add(
        [
            AddNode(model, rings.Count, 0, model.BaseCenterX, model.BaseCenterY, model.BaseElevationZ + model.UpperCutHeight)
        ]);
    }

    private static DomeNode AddNode(ParametricDomeModel model, int ringIndex, int segmentIndex, double x, double y, double z)
    {
        var node = new DomeNode
        {
            Id = $"R{ringIndex:000}_S{segmentIndex:000}",
            RingIndex = ringIndex,
            SegmentIndex = segmentIndex,
            X = x,
            Y = y,
            Z = z
        };
        model.Nodes.Add(node);
        return node;
    }

    private static void AddShellPanels(ParametricDomeModel model, IReadOnlyList<List<DomeNode>> rings)
    {
        if (!model.GenerateShellPanels)
            return;

        int panelIndex = 1;
        for (int ringIndex = 0; ringIndex < rings.Count - 1; ringIndex++)
        {
            List<DomeNode> lower = rings[ringIndex];
            List<DomeNode> upper = rings[ringIndex + 1];
            int segmentPanels = model.Full360 ? model.SegmentCount : model.SegmentCount;

            for (int segmentIndex = 0; segmentIndex < segmentPanels; segmentIndex++)
            {
                if (lower.Count == 1 && upper.Count == 1)
                    continue;

                if (upper.Count == 1)
                {
                    AddPanel(model, ref panelIndex, [lower[segmentIndex].Id, lower[NextIndex(segmentIndex, lower.Count, model.Full360)].Id, upper[0].Id]);
                    continue;
                }

                if (lower.Count == 1)
                {
                    AddPanel(model, ref panelIndex, [lower[0].Id, upper[NextIndex(segmentIndex, upper.Count, model.Full360)].Id, upper[segmentIndex].Id]);
                    continue;
                }

                DomeNode n00 = lower[segmentIndex];
                DomeNode n01 = lower[NextIndex(segmentIndex, lower.Count, model.Full360)];
                DomeNode n11 = upper[NextIndex(segmentIndex, upper.Count, model.Full360)];
                DomeNode n10 = upper[segmentIndex];

                if (model.ShellMeshType == DomeShellMeshType.Triangular)
                {
                    AddPanel(model, ref panelIndex, [n00.Id, n01.Id, n11.Id]);
                    AddPanel(model, ref panelIndex, [n00.Id, n11.Id, n10.Id]);
                }
                else
                {
                    AddPanel(model, ref panelIndex, [n00.Id, n01.Id, n11.Id, n10.Id]);
                }
            }
        }
    }

    private static void AddPanel(ParametricDomeModel model, ref int panelIndex, List<string> nodeIds)
    {
        model.ShellPanels.Add(new DomeShellPanel
        {
            Id = $"SH_{panelIndex:00000}",
            NodeIds = nodeIds,
            ShellPropertyName = model.ShellPropertyName
        });
        panelIndex++;
    }

    private static void AddFrameMembers(ParametricDomeModel model, IReadOnlyList<List<DomeNode>> rings)
    {
        int frameIndex = 1;
        int crownRingIndex = FindCrownRingIndex(rings);
        for (int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            List<DomeNode> ring = rings[ringIndex];
            if (ring.Count <= 1)
                continue;

            bool isBaseRing = ringIndex == 0;
            bool isCrownRing = ringIndex == crownRingIndex;
            bool addRing = isBaseRing
                ? model.GenerateBaseRing
                : isCrownRing ? model.GenerateCrownRing : model.GenerateRingFrames;

            if (addRing)
            {
                DomeMemberGroup group = isBaseRing
                    ? DomeMemberGroup.BaseRing
                    : isCrownRing ? DomeMemberGroup.CrownRing : DomeMemberGroup.Ring;
                string section = group switch
                {
                    DomeMemberGroup.BaseRing => model.BaseRingSectionName,
                    DomeMemberGroup.CrownRing => model.CrownRingSectionName,
                    _ => model.RingSectionName
                };

                int stop = model.Full360 ? ring.Count : ring.Count - 1;
                for (int segmentIndex = 0; segmentIndex < stop; segmentIndex++)
                    AddFrame(model, ref frameIndex, ring[segmentIndex].Id, ring[NextIndex(segmentIndex, ring.Count, model.Full360)].Id, group, section);
            }
        }

        for (int ringIndex = 0; ringIndex < rings.Count - 1; ringIndex++)
        {
            List<DomeNode> lower = rings[ringIndex];
            List<DomeNode> upper = rings[ringIndex + 1];
            int count = Math.Max(lower.Count, upper.Count);

            if (model.GenerateRadialFrames)
            {
                for (int segmentIndex = 0; segmentIndex < count; segmentIndex++)
                {
                    DomeNode start = lower.Count == 1 ? lower[0] : lower[Math.Min(segmentIndex, lower.Count - 1)];
                    DomeNode end = upper.Count == 1 ? upper[0] : upper[Math.Min(segmentIndex, upper.Count - 1)];
                    if (!string.Equals(start.Id, end.Id, StringComparison.OrdinalIgnoreCase))
                        AddFrame(model, ref frameIndex, start.Id, end.Id, DomeMemberGroup.Radial, model.RadialSectionName);
                }
            }

            if (model.GenerateDiagonalFrames && lower.Count > 1 && upper.Count > 1)
            {
                int stop = model.Full360 ? model.SegmentCount : model.SegmentCount;
                for (int segmentIndex = 0; segmentIndex < stop; segmentIndex++)
                    AddFrame(model, ref frameIndex, lower[segmentIndex].Id, upper[NextIndex(segmentIndex, upper.Count, model.Full360)].Id, DomeMemberGroup.Diagonal, model.DiagonalSectionName);
            }
        }
    }

    private static void AddFrame(
        ParametricDomeModel model,
        ref int frameIndex,
        string startNodeId,
        string endNodeId,
        DomeMemberGroup group,
        string sectionName)
    {
        model.FrameMembers.Add(new DomeFrameMember
        {
            Id = $"FR_{frameIndex:00000}",
            StartNodeId = startNodeId,
            EndNodeId = endNodeId,
            Group = group,
            SectionName = sectionName
        });
        frameIndex++;
    }

    private static int NextIndex(int index, int count, bool wrap)
    {
        if (count <= 1)
            return 0;

        int next = index + 1;
        return next < count ? next : wrap ? 0 : count - 1;
    }

    private static int FindCrownRingIndex(IReadOnlyList<List<DomeNode>> rings)
    {
        for (int ringIndex = rings.Count - 1; ringIndex >= 0; ringIndex--)
        {
            if (rings[ringIndex].Count > 1)
                return ringIndex;
        }

        return -1;
    }

    private static bool HasSeparateApexNode(ParametricDomeModel model, IReadOnlyList<List<DomeNode>> rings)
    {
        return rings.Count > model.RingCount && rings[^1].Count == 1;
    }

    private static bool ReachesApex(ParametricDomeModel model)
    {
        double tolerance = ApexCutTolerance * Math.Max(1.0, model.DomeRise);
        return Math.Abs(model.UpperCutHeight - model.DomeRise) <= tolerance;
    }

    private static double ResolveCrownRingCutHeight(ParametricDomeModel model, double sphereRadius, double sphereCenterLocalZ)
    {
        double maxRadius = Math.Max(MinRingRadius, GetRadiusAtLocalZ(sphereRadius, sphereCenterLocalZ, model.LowerCutHeight) - MinRingRadius);
        double requestedRadius = Math.Clamp(model.CrownRingRadius, MinRingRadius, maxRadius);
        double height = GetLocalZAtRadius(sphereRadius, sphereCenterLocalZ, requestedRadius);
        double span = model.UpperCutHeight - model.LowerCutHeight;
        double margin = Math.Min(Math.Max(span * 0.001, 0.000001), span * 0.25);
        double minimumHeight = model.LowerCutHeight + margin;
        double maximumHeight = model.UpperCutHeight - margin;
        if (maximumHeight < minimumHeight)
            return model.LowerCutHeight + span * 0.5;

        return Math.Clamp(height, minimumHeight, maximumHeight);
    }

    private static double ResolveUpperRingRadius(
        ParametricDomeModel model,
        double sphereRadius,
        double sphereCenterLocalZ,
        double lowerRingRadius,
        bool addSeparateApexNode,
        bool useCrownRingRadius)
    {
        if (useCrownRingRadius)
            return Math.Clamp(model.CrownRingRadius, MinRingRadius, Math.Max(MinRingRadius, lowerRingRadius - MinRingRadius));

        if (addSeparateApexNode)
            return 0.0;

        double upperCutRadius = GetRadiusAtLocalZ(sphereRadius, sphereCenterLocalZ, model.UpperCutHeight);
        return Math.Clamp(upperCutRadius, 0.0, lowerRingRadius);
    }

    private static double GetRadiusAtLocalZ(double sphereRadius, double sphereCenterLocalZ, double zLocal)
    {
        double radiusSquared = sphereRadius * sphereRadius - Math.Pow(zLocal - sphereCenterLocalZ, 2);
        return Math.Sqrt(Math.Max(0.0, radiusSquared));
    }

    private static double GetLocalZAtRadius(double sphereRadius, double sphereCenterLocalZ, double radius)
    {
        return sphereCenterLocalZ + Math.Sqrt(Math.Max(0.0, sphereRadius * sphereRadius - radius * radius));
    }

    private static double GetRingRadius(ParametricDomeModel model, IReadOnlyList<DomeNode> ring)
    {
        return ring
            .Select(node => Math.Sqrt(Math.Pow(node.X - model.BaseCenterX, 2) + Math.Pow(node.Y - model.BaseCenterY, 2)))
            .DefaultIfEmpty(0.0)
            .Max();
    }

    private static int ResolveCrownInfillRingCount(int ringCount)
    {
        if (ringCount <= 3)
            return 1;

        return Math.Clamp((int)Math.Ceiling(ringCount * 0.25), 2, 6);
    }

    private static double DegreesToRadians(double angle)
    {
        return angle * Math.PI / 180.0;
    }

    private static double PositiveOrDefault(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    private static double FiniteOrDefault(double value, double fallback)
    {
        return double.IsFinite(value) ? value : fallback;
    }
}
