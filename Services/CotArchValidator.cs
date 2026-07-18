using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class CotArchValidator
{
    private const double Tolerance = 0.000001;

    public ParametricValidationResult Validate(CotArchModel model)
    {
        var result = new ParametricValidationResult();
        CotArchInput input = model.Input;

        Critical(result, string.IsNullOrWhiteSpace(input.ModelPrefix), "Model prefix is required.");
        Critical(result, !double.IsFinite(input.Span) || input.Span <= 0, "Span must be greater than zero.");
        Critical(result, !double.IsFinite(input.Rise) || input.Rise <= 0, "Arch rise must be greater than zero.");
        Critical(result, !double.IsFinite(input.UpperBeamZ) || input.UpperBeamZ <= input.SpringingZ + input.Rise, "Upper beam Z must be above springing Z plus rise.");
        Critical(result, !double.IsFinite(input.SpringingZ) || input.SpringingZ <= input.BaseZ, "Springing Z must be above base Z.");
        Critical(result, input.PostCount < 3, "Post count must be at least 3.");
        Critical(result, input.ArchSegmentsPerPostBay < 1, "Arch segments per post bay must be at least 1.");
        Critical(result, input.ProfileType == CotArchProfileType.PowerCurve && input.ShapeExponent < 1.0, "Power-curve exponent must be at least 1.");

        if (input.ProfileType == CotArchProfileType.PowerCurve && (input.ShapeExponent < 1.5 || input.ShapeExponent > 6.0))
            Warning(result, "Power-curve exponent is outside the practical 1.5 to 6 range; verify the intended cap shape.");

        if (!string.IsNullOrWhiteSpace(input.CustomPostStationsError))
            Critical(result, true, input.CustomPostStationsError);

        ValidateCustomStations(result, input);
        ValidateSections(result, input);
        ValidateTopology(result, model);

        if (!result.HasCriticalIssues)
        {
            Info(result, $"Geometry valid: {model.ArchSegmentCount} arch segment(s), {model.VerticalPostCount} post(s), {model.UpperBeamSegmentCount} upper-beam segment(s), {model.FrameMemberCount} total frame object(s).");
            Info(result, $"Upper beam starts at X {input.OriginX:0.###} m and ends at X {input.OriginX + input.Span:0.###} m with no overhang.");
            Warning(result, "CoT Arch writes only to an already-open ETABS model. If ETABS is locked, generation will use the same unlock-and-warn workflow as City of Tomorrow.");
        }

        return result;
    }

    private static void ValidateCustomStations(ParametricValidationResult result, CotArchInput input)
    {
        if (input.CustomPostStations is not { Count: > 0 } stations)
            return;

        Critical(result, stations.Count < 3, "Custom post stations must include at least 3 values.");
        Critical(result, Math.Abs(stations.First() - 0.0) > Tolerance, "The first custom post station must be 0.");
        Critical(result, Math.Abs(stations.Last() - 1.0) > Tolerance, "The last custom post station must be 1.");

        for (int index = 0; index < stations.Count; index++)
        {
            double station = stations[index];
            Critical(result, !double.IsFinite(station) || station < -Tolerance || station > 1.0 + Tolerance, $"Custom post station {index + 1} must lie between 0 and 1.");
            if (index > 0)
                Critical(result, stations[index] <= stations[index - 1] + Tolerance, "Custom post stations must be strictly increasing with no duplicates.");
        }
    }

    private static void ValidateSections(ParametricValidationResult result, CotArchInput input)
    {
        foreach ((string label, string section) in new[]
        {
            ("arch", input.ArchSection),
            ("vertical posts", input.PostSection),
            ("upper beam", input.UpperBeamSection),
            ("tension tie", input.TieSection),
            ("support columns", input.SupportColumnSection)
        })
        {
            Critical(result, string.IsNullOrWhiteSpace(section), $"Select an ETABS frame section for {label}.");
        }
    }

    private static void ValidateTopology(ParametricValidationResult result, CotArchModel model)
    {
        CotArchInput input = model.Input;
        Dictionary<string, CotArchNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        Critical(result, model.LeftSpringing == null || model.RightSpringing == null, "Left and right springing nodes are required.");
        Critical(result, model.LeftBase == null || model.RightBase == null, "Left and right support base nodes are required.");
        if (model.LeftSpringing == null || model.RightSpringing == null || model.LeftBase == null || model.RightBase == null)
            return;

        Nearly(result, model.LeftSpringing.X, input.OriginX, "Left springing X must equal origin X.");
        Nearly(result, model.LeftSpringing.Y, input.PlaneY, "Left springing Y must equal plane Y.");
        Nearly(result, model.LeftSpringing.Z, input.SpringingZ, "Left springing Z must equal springing Z.");
        Nearly(result, model.RightSpringing.X, input.OriginX + input.Span, "Right springing X must equal origin X plus span.");
        Nearly(result, model.RightSpringing.Y, input.PlaneY, "Right springing Y must equal plane Y.");
        Nearly(result, model.RightSpringing.Z, input.SpringingZ, "Right springing Z must equal springing Z.");
        Nearly(result, CotArchGeometryBuilder.EvaluateArchZ(input, 0.5), input.SpringingZ + input.Rise, "Arch crown must reach springing Z plus rise.");

        int expectedArch = Math.Max(0, (model.PostBottomNodes.Count - 1) * input.ArchSegmentsPerPostBay);
        Critical(result, model.ArchSegmentCount != expectedArch, $"Expected {expectedArch} arch segment(s).");
        Critical(result, model.VerticalPostCount != model.PostBottomNodes.Count, $"Expected {model.PostBottomNodes.Count} vertical post(s).");
        Critical(result, model.UpperBeamSegmentCount != Math.Max(0, model.PostTopNodes.Count - 1), $"Expected {Math.Max(0, model.PostTopNodes.Count - 1)} upper-beam segment(s).");
        Critical(result, model.TensionTieCount != 1, "Expected exactly one tension tie.");
        Critical(result, model.SupportColumnCount != 2, "Expected exactly two support columns.");

        foreach (CotArchMember member in model.Members)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out CotArchNode? start) || !nodes.TryGetValue(member.EndNodeId, out CotArchNode? end))
            {
                Critical(result, true, $"Member '{member.Id}' references a missing node.");
                continue;
            }

            Critical(result, Distance(start, end) <= Tolerance, $"Member '{member.Id}' has zero length.");
            if (member.Kind == CotArchMemberKind.UpperBeam)
            {
                Critical(result, start.X < input.OriginX - Tolerance || end.X > input.OriginX + input.Span + Tolerance, $"Upper-beam member '{member.Id}' extends beyond the end posts.");
                Nearly(result, start.Z, input.UpperBeamZ, $"Upper-beam member '{member.Id}' start Z must equal upper beam Z.");
                Nearly(result, end.Z, input.UpperBeamZ, $"Upper-beam member '{member.Id}' end Z must equal upper beam Z.");
            }
        }

        for (int index = 0; index < model.PostBottomNodes.Count; index++)
        {
            CotArchNode bottom = model.PostBottomNodes[index];
            CotArchNode top = model.PostTopNodes[index];
            Nearly(result, bottom.X, top.X, $"Post {index} bottom/top X must match.");
            Nearly(result, bottom.Y, top.Y, $"Post {index} bottom/top Y must match.");
            Critical(result, top.Z <= bottom.Z + Tolerance, $"Post {index} length must be positive.");
            Nearly(result, bottom.Z, CotArchGeometryBuilder.EvaluateArchZ(input, bottom.Xi), $"Post {index} bottom must lie on the arch.");
        }

        ValidateSpringingConnectivity(result, model);
        ValidateArchSymmetry(result, model);
    }

    private static void ValidateSpringingConnectivity(ParametricValidationResult result, CotArchModel model)
    {
        string left = model.LeftSpringing!.Id;
        string right = model.RightSpringing!.Id;

        Critical(result, model.Members.FirstOrDefault(member => member.Kind == CotArchMemberKind.Arch)?.StartNodeId != left, "First arch segment must start at the left springing joint.");
        Critical(result, model.Members.LastOrDefault(member => member.Kind == CotArchMemberKind.Arch)?.EndNodeId != right, "Last arch segment must end at the right springing joint.");
        Critical(result, model.Members.FirstOrDefault(member => member.Kind == CotArchMemberKind.VerticalPost)?.StartNodeId != left, "Left end post bottom must reuse the left springing joint.");
        Critical(result, model.Members.LastOrDefault(member => member.Kind == CotArchMemberKind.VerticalPost)?.StartNodeId != right, "Right end post bottom must reuse the right springing joint.");

        CotArchMember? tie = model.Members.FirstOrDefault(member => member.Kind == CotArchMemberKind.TensionTie);
        Critical(result, tie == null || tie.StartNodeId != left || tie.EndNodeId != right, "Tension tie endpoints must be the left and right springing joints.");

        CotArchMember? leftSupport = model.Members.FirstOrDefault(member => member.Id.EndsWith("_F_SUPPORT_L", StringComparison.OrdinalIgnoreCase));
        CotArchMember? rightSupport = model.Members.FirstOrDefault(member => member.Id.EndsWith("_F_SUPPORT_R", StringComparison.OrdinalIgnoreCase));
        Critical(result, leftSupport == null || leftSupport.EndNodeId != left, "Left support column top must reuse the left springing joint.");
        Critical(result, rightSupport == null || rightSupport.EndNodeId != right, "Right support column top must reuse the right springing joint.");
    }

    private static void ValidateArchSymmetry(ParametricValidationResult result, CotArchModel model)
    {
        foreach (CotArchNode node in model.ArchNodes)
        {
            CotArchNode? mirror = model.ArchNodes.FirstOrDefault(candidate => Math.Abs(candidate.Xi - (1.0 - node.Xi)) <= Tolerance);
            if (mirror == null)
                continue;

            Critical(result, Math.Abs(node.Z - mirror.Z) > Tolerance, $"Arch station {node.Xi:0.###} is not symmetric with station {mirror.Xi:0.###}.");
        }
    }

    private static double Distance(CotArchNode start, CotArchNode end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static void Nearly(ParametricValidationResult result, double actual, double expected, string message)
    {
        Critical(result, Math.Abs(actual - expected) > Tolerance, $"{message} Expected {expected:0.######}, actual {actual:0.######}.");
    }

    private static void Info(ParametricValidationResult result, string message)
    {
        result.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Info, Message = message });
    }

    private static void Warning(ParametricValidationResult result, string message)
    {
        result.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, Message = message });
    }

    private static void Critical(ParametricValidationResult result, bool condition, string message)
    {
        if (condition)
            result.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Critical, Message = message });
    }
}
