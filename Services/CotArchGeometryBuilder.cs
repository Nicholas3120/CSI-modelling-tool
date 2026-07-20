using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class CotArchGeometryBuilder
{
    private const double Tolerance = 0.000001;

    public CotArchModel Build(CotArchInput input)
    {
        string prefix = EtabsNameUtility.BuildSafeName("", input.ModelPrefix, 24);
        var normalizedInput = CopyInput(input);
        normalizedInput.ModelPrefix = prefix;

        var model = new CotArchModel
        {
            ModelPrefix = prefix,
            GroupName = EtabsNameUtility.BuildSafeName("COT_ARCH_", prefix),
            Input = normalizedInput
        };
        model.PointGroupName = BuildSubGroup(model.GroupName, "POINTS");
        model.ArchGroupName = BuildSubGroup(model.GroupName, "ARCH");
        model.PostGroupName = BuildSubGroup(model.GroupName, "POSTS");
        model.UpperBeamGroupName = BuildSubGroup(model.GroupName, "UPPER_BEAM");
        model.TieGroupName = BuildSubGroup(model.GroupName, "TIE");
        model.SupportColumnGroupName = BuildSubGroup(model.GroupName, "SUPPORT_COLUMNS");

        List<double> postStations = BuildPostStations(normalizedInput);
        List<double> archStations = BuildArchStations(postStations, Math.Max(1, normalizedInput.ArchSegmentsPerPostBay));

        for (int index = 0; index < archStations.Count; index++)
        {
            double xi = archStations[index];
            CotArchNode node = AddNode(
                model,
                BuildArchNodeId(prefix, index, archStations.Count),
                normalizedInput.OriginX + xi * normalizedInput.Span,
                normalizedInput.PlaneY,
                EvaluateArchZ(normalizedInput, xi),
                xi);
            node.IsArchNode = true;
            node.IsSpringing = index == 0 || index == archStations.Count - 1;
            model.ArchNodes.Add(node);
        }

        foreach (double xi in postStations)
        {
            CotArchNode bottom = model.ArchNodes.First(node => Math.Abs(node.Xi - xi) <= Tolerance);
            bottom.IsPostBottom = true;
            model.PostBottomNodes.Add(bottom);
        }

        for (int index = 0; index < postStations.Count; index++)
        {
            double xi = postStations[index];
            CotArchNode bottom = model.PostBottomNodes[index];
            CotArchNode top = Math.Abs(normalizedInput.UpperBeamZ - bottom.Z) <= Tolerance
                ? bottom
                : AddNode(
                    model,
                    $"{prefix}_P_TOP_{index:000}",
                    normalizedInput.OriginX + xi * normalizedInput.Span,
                    normalizedInput.PlaneY,
                    normalizedInput.UpperBeamZ,
                    xi);
            top.IsPostTop = true;
            model.PostTopNodes.Add(top);
        }

        model.LeftSpringing = model.PostBottomNodes.FirstOrDefault();
        model.RightSpringing = model.PostBottomNodes.LastOrDefault();
        if (model.LeftSpringing != null) model.LeftSpringing.IsSpringing = true;
        if (model.RightSpringing != null) model.RightSpringing.IsSpringing = true;

        model.LeftBase = AddNode(model, $"{prefix}_P_BASE_L", normalizedInput.OriginX, normalizedInput.PlaneY, normalizedInput.BaseZ, 0);
        model.LeftBase.IsSupportBase = true;
        model.RightBase = AddNode(model, $"{prefix}_P_BASE_R", normalizedInput.OriginX + normalizedInput.Span, normalizedInput.PlaneY, normalizedInput.BaseZ, 1);
        model.RightBase.IsSupportBase = true;

        for (int index = 0; index < model.ArchNodes.Count - 1; index++)
        {
            AddMember(
                model,
                $"{prefix}_F_ARCH_{index:000}",
                model.ArchNodes[index].Id,
                model.ArchNodes[index + 1].Id,
                CotArchMemberGroups.Arch,
                CotArchMemberKind.Arch,
                normalizedInput.ArchSection,
                normalizedInput.ArchReleasePreset);
        }

        for (int index = 0; index < model.PostBottomNodes.Count; index++)
        {
            if (Distance(model.PostBottomNodes[index], model.PostTopNodes[index]) <= Tolerance)
                continue;

            AddMember(
                model,
                $"{prefix}_F_POST_{index:000}",
                model.PostBottomNodes[index].Id,
                model.PostTopNodes[index].Id,
                CotArchMemberGroups.VerticalPost,
                CotArchMemberKind.VerticalPost,
                normalizedInput.PostSection,
                normalizedInput.PostReleasePreset);
        }

        for (int index = 0; index < model.PostTopNodes.Count - 1; index++)
        {
            AddMember(
                model,
                $"{prefix}_F_BEAM_{index:000}",
                model.PostTopNodes[index].Id,
                model.PostTopNodes[index + 1].Id,
                CotArchMemberGroups.UpperBeam,
                CotArchMemberKind.UpperBeam,
                normalizedInput.UpperBeamSection,
                normalizedInput.BeamReleasePreset);
        }

        if (model.LeftSpringing != null && model.RightSpringing != null)
        {
            AddMember(
                model,
                $"{prefix}_F_TIE_000",
                model.LeftSpringing.Id,
                model.RightSpringing.Id,
                CotArchMemberGroups.TensionTie,
                CotArchMemberKind.TensionTie,
                normalizedInput.TieSection,
                normalizedInput.TieReleasePreset);
        }

        if (model.LeftBase != null && model.LeftSpringing != null)
        {
            AddMember(
                model,
                $"{prefix}_F_SUPPORT_L",
                model.LeftBase.Id,
                model.LeftSpringing.Id,
                CotArchMemberGroups.SupportColumn,
                CotArchMemberKind.SupportColumn,
                normalizedInput.SupportColumnSection,
                normalizedInput.SupportColumnReleasePreset);
        }

        if (model.RightBase != null && model.RightSpringing != null)
        {
            AddMember(
                model,
                $"{prefix}_F_SUPPORT_R",
                model.RightBase.Id,
                model.RightSpringing.Id,
                CotArchMemberGroups.SupportColumn,
                CotArchMemberKind.SupportColumn,
                normalizedInput.SupportColumnSection,
                normalizedInput.SupportColumnReleasePreset);
        }

        return model;
    }

    public static double EvaluateArchZ(CotArchInput input, double xi)
    {
        double clampedXi = Math.Clamp(xi, 0.0, 1.0);
        return input.ProfileType switch
        {
            CotArchProfileType.Circular => CircularArchZ(clampedXi, Math.Max(input.Span, Tolerance), input.SpringingZ, Math.Max(input.Rise, Tolerance)),
            CotArchProfileType.PowerCurve => PowerArchZ(clampedXi, input.SpringingZ, input.Rise, Math.Max(input.ShapeExponent, 1.0)),
            _ => ParabolicArchZ(clampedXi, input.SpringingZ, input.Rise)
        };
    }

    public static List<double> BuildPostStations(CotArchInput input)
    {
        if (input.CustomPostStations is { Count: > 0 })
            return input.CustomPostStations.Select(xi => Math.Clamp(xi, 0.0, 1.0)).ToList();

        int count = Math.Max(3, input.PostCount);
        return Enumerable.Range(0, count)
            .Select(index => (double)index / (count - 1))
            .ToList();
    }

    public static List<double> BuildArchStations(IReadOnlyList<double> postStations, int segmentsPerPostBay)
    {
        var stations = new List<double>();
        int segmentCount = Math.Max(1, segmentsPerPostBay);
        for (int bay = 0; bay < postStations.Count - 1; bay++)
        {
            double start = postStations[bay];
            double end = postStations[bay + 1];
            if (bay == 0)
                stations.Add(start);

            for (int division = 1; division <= segmentCount; division++)
            {
                double t = (double)division / segmentCount;
                stations.Add(start + (end - start) * t);
            }
        }

        return stations
            .Select(xi => Math.Round(xi, 12, MidpointRounding.AwayFromZero))
            .Distinct()
            .Order()
            .ToList();
    }

    private static CotArchInput CopyInput(CotArchInput input)
    {
        return new CotArchInput
        {
            ModelPrefix = input.ModelPrefix,
            OriginX = Finite(input.OriginX, 0),
            PlaneY = Finite(input.PlaneY, 0),
            BaseZ = Finite(input.BaseZ, -8),
            SpringingZ = Finite(input.SpringingZ, 0),
            UpperBeamZ = Finite(input.UpperBeamZ, 12),
            Span = Math.Max(Finite(input.Span, 40), Tolerance),
            Rise = Math.Max(Finite(input.Rise, 8), Tolerance),
            PostCount = Math.Max(3, input.PostCount),
            CustomPostStations = input.CustomPostStations?.ToList(),
            CustomPostStationsError = input.CustomPostStationsError ?? "",
            ArchSegmentsPerPostBay = Math.Max(1, input.ArchSegmentsPerPostBay),
            ProfileType = input.ProfileType,
            ShapeExponent = Math.Max(Finite(input.ShapeExponent, 2), 1),
            ArchSection = input.ArchSection ?? "",
            PostSection = input.PostSection ?? "",
            UpperBeamSection = input.UpperBeamSection ?? "",
            TieSection = input.TieSection ?? "",
            SupportColumnSection = input.SupportColumnSection ?? "",
            GenerateAsPlanarModel = input.GenerateAsPlanarModel,
            SupportCondition = input.SupportCondition,
            ArchReleasePreset = input.ArchReleasePreset,
            PostReleasePreset = input.PostReleasePreset,
            TieReleasePreset = input.TieReleasePreset,
            BeamReleasePreset = input.BeamReleasePreset,
            SupportColumnReleasePreset = input.SupportColumnReleasePreset,
            UpperBeamLoadType = input.UpperBeamLoadType,
            UpperBeamLoadPattern = input.UpperBeamLoadPattern ?? "",
            UpperBeamUdlKnPerM = Math.Abs(Finite(input.UpperBeamUdlKnPerM, 0)),
            UpperBeamPointLoadKn = Math.Abs(Finite(input.UpperBeamPointLoadKn, 0))
        };
    }

    private static CotArchNode AddNode(CotArchModel model, string id, double x, double y, double z, double xi)
    {
        var node = new CotArchNode { Id = id, X = x, Y = y, Z = z, Xi = xi };
        model.Nodes.Add(node);
        return node;
    }

    private static void AddMember(
        CotArchModel model,
        string id,
        string startNodeId,
        string endNodeId,
        string group,
        CotArchMemberKind kind,
        string sectionName,
        CotArchMemberReleasePreset releasePreset)
    {
        model.Members.Add(new CotArchMember
        {
            Id = id,
            StartNodeId = startNodeId,
            EndNodeId = endNodeId,
            Group = group,
            Kind = kind,
            SectionName = sectionName ?? "",
            ReleasePreset = releasePreset
        });
    }

    private static double Distance(CotArchNode start, CotArchNode end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static string BuildArchNodeId(string prefix, int index, int count)
    {
        if (index == 0)
            return $"{prefix}_P_SPRING_L";
        if (index == count - 1)
            return $"{prefix}_P_SPRING_R";

        return $"{prefix}_P_ARCH_{index:000}";
    }

    private static string BuildSubGroup(string groupName, string suffix)
    {
        return EtabsNameUtility.BuildSafeName("", $"{groupName}_{suffix}");
    }

    private static double ParabolicArchZ(double xi, double springingZ, double rise)
    {
        return springingZ + 4.0 * rise * xi * (1.0 - xi);
    }

    private static double PowerArchZ(double xi, double springingZ, double rise, double exponent)
    {
        double normalized = Math.Abs(2.0 * xi - 1.0);
        return springingZ + rise * (1.0 - Math.Pow(normalized, exponent));
    }

    private static double CircularArchZ(double xi, double span, double springingZ, double rise)
    {
        double radius = span * span / (8.0 * rise) + rise / 2.0;
        double centerZ = springingZ + rise - radius;
        double localX = xi * span;
        double dx = localX - span / 2.0;
        double radicand = radius * radius - dx * dx;
        if (radicand < -Tolerance)
            throw new InvalidOperationException("Invalid circular CoT Arch geometry.");

        return centerZ + Math.Sqrt(Math.Max(0.0, radicand));
    }

    private static double Finite(double value, double fallback)
    {
        return double.IsFinite(value) ? value : fallback;
    }
}
