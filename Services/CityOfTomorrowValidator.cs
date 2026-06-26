using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class CityOfTomorrowValidator
{
    private const double Tolerance = 0.000001;

    public ParametricValidationResult Validate(CityOfTomorrowModel model)
    {
        var result = new ParametricValidationResult();
        CityOfTomorrowInput input = model.Input;
        int n = input.PanelsPerHalfN;
        int p = 2 * n;
        Critical(result, string.IsNullOrWhiteSpace(input.StructureId), "Structure ID is required.");
        Critical(result, input.ClearSpanL <= 0 || !double.IsFinite(input.ClearSpanL), "Clear span must be greater than zero.");
        Critical(result, n < 1, "Panels per half must be at least 1.");
        Critical(result, input.VierendeelDepthH <= 0 || !double.IsFinite(input.VierendeelDepthH), "Vierendeel depth must be greater than zero.");
        Critical(result, input.MidRailRatio <= 0 || input.MidRailRatio >= 1, "Intermediate rail ratio must be between 0 and 1.");
        Critical(result, input.TieLevelZ >= input.BottomChordLevelZ, "Global tie level must be below the bottom chord.");
        Critical(result, input.ExternalAnchorWidth <= 0, "External anchor width must be greater than zero.");
        Critical(result, input.ExternalSideFrameHeight <= 0, "Side-frame height must be greater than zero.");

        foreach ((string label, string value) in new Dictionary<string, string>
        {
            ["top chord"] = input.TopChordSection, ["intermediate rail"] = input.MidRailSection,
            ["bottom chord"] = input.BottomChordSection, ["vertical posts"] = input.VerticalPostSection,
            ["tower"] = input.TowerSection, ["side frame"] = input.SideFrameSection
        })
            Critical(result, string.IsNullOrWhiteSpace(value), $"Select a CSI frame section for {label}.");

        foreach ((string label, string value) in new Dictionary<string, string>
        {
            ["cables/backstays"] = input.CableSection, ["global tie/tendon"] = input.TieCableSection
        })
            Critical(result, string.IsNullOrWhiteSpace(value), $"Select a CSI cable or tendon section for {label}.");

        var nodes = model.Nodes.ToDictionary(node => node.Key, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= p && n >= 1; i++)
        {
            CheckPair(result, nodes, CityOfTomorrowGeometryBuilder.BottomKey(i), CityOfTomorrowGeometryBuilder.BottomKey(p - i));
            CheckPair(result, nodes, CityOfTomorrowGeometryBuilder.MidKey(i), CityOfTomorrowGeometryBuilder.MidKey(p - i));
            CheckPair(result, nodes, CityOfTomorrowGeometryBuilder.TopKey(i), CityOfTomorrowGeometryBuilder.TopKey(p - i));
        }

        if (n >= 1)
        {
            string centre = CityOfTomorrowGeometryBuilder.BottomKey(n);
            int centreCables = model.Members.Count(member => member.IsTensionOnly &&
                member.Group == CityMemberGroups.InternalCable &&
                string.Equals(member.EndNodeKey, centre, StringComparison.OrdinalIgnoreCase));
            Critical(result, centreCables != 2, $"Both innermost cables must terminate at shared centre joint {centre}.");
        }

        Critical(result, model.Nodes.Count(node => node.IsPrimaryJoint) != 3 * (p + 1), $"Expected {3 * (p + 1)} primary joints.");
        Critical(result, Count(model, CityMemberGroups.TopChord) != p, $"Expected {p} top-chord segments.");
        Critical(result, Count(model, CityMemberGroups.MidRail) != p, $"Expected {p} intermediate-rail segments.");
        Critical(result, Count(model, CityMemberGroups.BottomChord) != p, $"Expected {p} bottom-chord segments.");
        Critical(result, Count(model, CityMemberGroups.VerticalPost) != 2 * (p - 1), $"Expected {2 * (p - 1)} vertical-post segments.");
        Critical(result, Count(model, CityMemberGroups.InternalCable) != 2 * n, $"Expected {2 * n} internal cables.");

        if (!result.HasCriticalIssues)
        {
            result.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Info, Message = $"Geometry valid: {p} symmetrical panels, {model.Nodes.Count} points, {model.FrameMemberCount} frames and {model.TensionOnlyMemberCount} tension-only members." });
            result.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, Message = "Use a nonlinear CSI load case for tension-only cable behavior and define project-specific pretension, large-displacement and out-of-plane assumptions." });
        }
        return result;
    }

    private static int Count(CityOfTomorrowModel model, string group) => model.Members.Count(member => member.Group == group);
    private static void CheckPair(ParametricValidationResult result, IReadOnlyDictionary<string, CityNode> nodes, string aKey, string bKey)
    {
        if (!nodes.TryGetValue(aKey, out CityNode? a) || !nodes.TryGetValue(bKey, out CityNode? b))
        {
            Critical(result, true, $"Mirrored node pair {aKey}/{bKey} is incomplete.");
            return;
        }
        Critical(result, Math.Abs(a.X + b.X) > Tolerance || Math.Abs(a.Z - b.Z) > Tolerance, $"Node pair {aKey}/{bKey} is not symmetrical.");
        Critical(result, Math.Abs(a.Y) > Tolerance || Math.Abs(b.Y) > Tolerance, "All geometry must remain at Y = 0.");
    }
    private static void Critical(ParametricValidationResult result, bool condition, string message)
    {
        if (condition) result.Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Critical, Message = message });
    }
}
