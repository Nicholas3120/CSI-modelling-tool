using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class SteelRailingValidator
{
    public ParametricValidationResult Validate(
        SteelRailingModel model,
        string? selectedEtabsInstanceId,
        bool etabsDataLoaded,
        IReadOnlyCollection<string> frameSections,
        IReadOnlyCollection<string> loadPatterns,
        bool requireEtabsConnection)
    {
        var result = new ParametricValidationResult();

        if (requireEtabsConnection)
        {
            if (string.IsNullOrWhiteSpace(selectedEtabsInstanceId))
                Add(result, ValidationSeverity.Critical, "Select a target ETABS model.");

            if (!etabsDataLoaded)
                Add(result, ValidationSeverity.Critical, "Read ETABS data before drawing the railing.");
        }

        if (string.IsNullOrWhiteSpace(model.RailingId))
            Add(result, ValidationSeverity.Critical, "Railing ID is required.");
        if (model.SpanCount < 3)
            Add(result, ValidationSeverity.Critical, "Number of spans must be at least 3.");
        if (!double.IsFinite(model.PostSpacing) || model.PostSpacing <= 0)
            Add(result, ValidationSeverity.Critical, "Post spacing must be greater than zero.");
        if (!double.IsFinite(model.RailingHeight) || model.RailingHeight <= 0)
            Add(result, ValidationSeverity.Critical, "Railing height must be greater than zero.");
        if (model.Nodes.Count == 0 || model.Members.Count == 0)
            Add(result, ValidationSeverity.Critical, "No railing geometry was generated.");

        AddDuplicateChecks(result, "railing node", model.Nodes.Select(node => node.Id));
        AddDuplicateChecks(result, "railing member", model.Members.Select(member => member.Id));

        var existingSections = new HashSet<string>(frameSections, StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SteelRailingMember> group in model.Members.GroupBy(member => member.Group, StringComparer.OrdinalIgnoreCase))
        {
            string sectionName = group.FirstOrDefault()?.SectionName?.Trim() ?? "";
            if (sectionName.Length == 0)
            {
                Add(result, ValidationSeverity.Critical, $"Select a frame section for {FormatGroupName(group.Key)}.");
                continue;
            }

            if (requireEtabsConnection && existingSections.Count > 0 && !existingSections.Contains(sectionName))
                Add(result, ValidationSeverity.Critical, $"Selected {FormatGroupName(group.Key)} section '{sectionName}' does not exist in the connected ETABS model.");
        }

        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        foreach (SteelRailingMember member in model.Members)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out SteelRailingNode? start))
                Add(result, ValidationSeverity.Critical, $"Member '{member.Id}' references missing start node '{member.StartNodeId}'.");

            if (!nodes.TryGetValue(member.EndNodeId, out SteelRailingNode? end))
                Add(result, ValidationSeverity.Critical, $"Member '{member.Id}' references missing end node '{member.EndNodeId}'.");

            if (start != null && end != null)
            {
                double length = Math.Sqrt(
                    Math.Pow(end.X - start.X, 2) +
                    Math.Pow(end.Y - start.Y, 2) +
                    Math.Pow(end.Z - start.Z, 2));

                if (!double.IsFinite(length) || length <= 0.000001)
                    Add(result, ValidationSeverity.Critical, $"Member '{member.Id}' has zero length.");
            }
        }

        var coordinateKeys = model.Nodes
            .Select(node => $"{Math.Round(node.X, 6):0.######}|{Math.Round(node.Y, 6):0.######}|{Math.Round(node.Z, 6):0.######}")
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);
        foreach (IGrouping<string, string> duplicate in coordinateKeys)
            Add(result, ValidationSeverity.Warning, $"Multiple railing nodes share coordinate {duplicate.Key}; ETABS may merge them into one joint.");

        var existingLoadPatterns = new HashSet<string>(loadPatterns, StringComparer.OrdinalIgnoreCase);
        foreach (SteelRailingLoad load in model.Loads)
        {
            if (string.IsNullOrWhiteSpace(load.LoadPattern))
                Add(result, ValidationSeverity.Critical, $"Load '{load.Id}' has no load pattern.");
            else if (requireEtabsConnection && existingLoadPatterns.Count > 0 && !existingLoadPatterns.Contains(load.LoadPattern))
                Add(result, ValidationSeverity.Critical, $"Load pattern '{load.LoadPattern}' does not exist in the connected ETABS model.");

            if (load.LoadType == RailingLoadType.LineLoad)
            {
                if (!model.Members.Any(member => string.Equals(member.Group, load.TargetGroup, StringComparison.OrdinalIgnoreCase)))
                    Add(result, ValidationSeverity.Critical, $"Line load '{load.Id}' targets member group '{load.TargetGroup}' but no matching railing members exist.");

                if (!double.IsFinite(load.MagnitudeKnPerM) || Math.Abs(load.MagnitudeKnPerM) <= 0.000001)
                    Add(result, ValidationSeverity.Warning, $"Line load '{load.Id}' has zero magnitude.");
            }
            else
            {
                if (load.TargetNodeIds.Count == 0)
                    Add(result, ValidationSeverity.Critical, $"Point load '{load.Id}' has no generated target nodes.");

                foreach (string nodeId in load.TargetNodeIds)
                {
                    if (!nodes.ContainsKey(nodeId))
                        Add(result, ValidationSeverity.Critical, $"Point load '{load.Id}' targets missing node '{nodeId}'.");
                }

                if (!double.IsFinite(load.PointHeight) || load.PointHeight <= 0 || load.PointHeight >= model.RailingHeight)
                    Add(result, ValidationSeverity.Critical, $"Point load '{load.Id}' height must be between base and railing height.");

                if (!double.IsFinite(load.MagnitudeKn) || Math.Abs(load.MagnitudeKn) <= 0.000001)
                    Add(result, ValidationSeverity.Warning, $"Point load '{load.Id}' has zero magnitude.");
            }
        }

        if (model.SpanCount > 12)
            Add(result, ValidationSeverity.Warning, "Railing has many spans; ETABS drawing may take a little longer.");
        if (model.PostSpacing > 2.0)
            Add(result, ValidationSeverity.Warning, "Post spacing is above 2.0 m. Check if this is intended.");
        if (model.RailingHeight < 0.8 || model.RailingHeight > 1.5)
            Add(result, ValidationSeverity.Warning, "Railing height is outside the common 0.8 m to 1.5 m range.");

        foreach (string warning in model.Warnings)
            Add(result, ValidationSeverity.Warning, warning);

        if (result.Issues.Count == 0)
            Add(result, ValidationSeverity.Info, "Railing validation passed.");

        return result;
    }

    private static void AddDuplicateChecks(ParametricValidationResult result, string label, IEnumerable<string> ids)
    {
        foreach (IGrouping<string, string> duplicate in ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            Add(result, ValidationSeverity.Critical, $"Duplicate {label} ID '{duplicate.Key}'.");
        }
    }

    private static string FormatGroupName(string group)
    {
        return group switch
        {
            SteelRailingMemberGroups.Post => "post",
            SteelRailingMemberGroups.TopRail => "top rail",
            SteelRailingMemberGroups.MidRail => "mid rail",
            SteelRailingMemberGroups.BottomRail => "bottom rail",
            _ => group
        };
    }

    private static void Add(ParametricValidationResult result, ValidationSeverity severity, string message)
    {
        result.Issues.Add(new ValidationIssue
        {
            Severity = severity,
            Message = message
        });
    }
}
