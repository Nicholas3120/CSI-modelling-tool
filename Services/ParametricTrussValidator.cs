using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class ParametricTrussValidator
{
    public ParametricValidationResult Validate(
        ParametricTrussModel model,
        string? selectedEtabsInstanceId,
        bool etabsDataLoaded,
        IReadOnlyCollection<string> frameSections,
        IReadOnlyCollection<string> shellProperties,
        IReadOnlyCollection<string> loadPatterns,
        bool requireEtabsConnection)
    {
        var result = new ParametricValidationResult();

        if (requireEtabsConnection)
        {
            if (string.IsNullOrWhiteSpace(selectedEtabsInstanceId))
                Add(result, ValidationSeverity.Critical, "Select a target ETABS model.");

            if (!etabsDataLoaded)
                Add(result, ValidationSeverity.Critical, "Read ETABS data before sending the truss.");
        }

        if (string.IsNullOrWhiteSpace(model.GroupName))
            Add(result, ValidationSeverity.Critical, "Truss group name is empty.");

        if (!double.IsFinite(model.Span) || model.Span <= 0)
            Add(result, ValidationSeverity.Critical, "Span must be greater than zero.");

        if (!double.IsFinite(model.Height) || model.Height <= 0)
            Add(result, ValidationSeverity.Critical, "Height must be greater than zero.");

        if (model.PanelCount < 2)
            Add(result, ValidationSeverity.Critical, "Panel count must be at least 2.");

        if (model.TrussType == TrussType.SpiralStaircase && model.PanelCount < 3)
            Add(result, ValidationSeverity.Critical, "Spiral staircase step count must be at least 3.");

        if (model.TrussType == TrussType.FishBellyTruss && model.Height <= 0)
            Add(result, ValidationSeverity.Critical, "Fish-belly middle depth must be greater than zero.");

        if (model.TrussType == TrussType.VariablePanelWidthTruss && model.Height <= 0)
            Add(result, ValidationSeverity.Critical, "Variable panel truss depth must be greater than zero.");

        if (model.SupportNodeMode != SupportNodeMode.NoSupports && model.Nodes.Count(node => node.IsSupport) == 0)
            Add(result, ValidationSeverity.Warning, "No support nodes are selected for ETABS restraint assignment.");

        if (model.SupportNodeMode == SupportNodeMode.NoSupports)
            Add(result, ValidationSeverity.Warning, "No ETABS point restraints will be assigned for this truss.");

        AddDuplicateChecks(result, "node", model.Nodes.Select(node => node.Id));
        AddDuplicateChecks(result, "member", model.Members.Select(member => member.Id));
        AddDuplicateChecks(result, "shell", model.Shells.Select(shell => shell.Id));

        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var existingSections = new HashSet<string>(frameSections, StringComparer.OrdinalIgnoreCase);
        var usedGroups = model.Members
            .Select(member => member.Group)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string group in usedGroups)
        {
            string selectedSection = model.SectionAssignments.TryGetValue(group, out string? sectionName)
                ? sectionName.Trim()
                : model.Members
                    .FirstOrDefault(member => string.Equals(member.Group, group, StringComparison.OrdinalIgnoreCase))
                    ?.SectionName
                    ?.Trim() ?? "";

            if (selectedSection.Length == 0)
            {
                Add(result, ValidationSeverity.Critical, $"Select a frame section for {group}.");
                continue;
            }

            if (requireEtabsConnection && existingSections.Count > 0 && !existingSections.Contains(selectedSection))
                Add(result, ValidationSeverity.Critical, $"Selected {group} frame section '{selectedSection}' does not exist in the connected ETABS model.");
        }

        foreach (ParametricMember member in model.Members)
        {
            string memberSection = (member.SectionName ?? "").Trim();
            if (memberSection.Length == 0)
            {
                Add(result, ValidationSeverity.Critical, $"Member '{member.Id}' has no frame section selected.");
            }
            else if (requireEtabsConnection && existingSections.Count > 0 && !existingSections.Contains(memberSection))
            {
                Add(result, ValidationSeverity.Critical, $"Selected frame section '{memberSection}' for member '{member.Id}' does not exist in the connected ETABS model.");
            }

            if (!nodes.TryGetValue(member.StartNodeId, out ParametricNode? start))
                Add(result, ValidationSeverity.Critical, $"Member '{member.Id}' references missing start node '{member.StartNodeId}'.");

            if (!nodes.TryGetValue(member.EndNodeId, out ParametricNode? end))
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

        var existingShellProperties = new HashSet<string>(shellProperties, StringComparer.OrdinalIgnoreCase);
        foreach (ParametricShell shell in model.Shells)
        {
            if (shell.NodeIds.Count < 3)
                Add(result, ValidationSeverity.Critical, $"Shell '{shell.Id}' must reference at least three nodes.");

            foreach (string nodeId in shell.NodeIds)
            {
                if (!nodes.ContainsKey(nodeId))
                    Add(result, ValidationSeverity.Critical, $"Shell '{shell.Id}' references missing node '{nodeId}'.");
            }

            string propertyName = (shell.ShellPropertyName ?? "").Trim();
            if (propertyName.Length == 0)
                Add(result, ValidationSeverity.Critical, $"Select a shell property for shell '{shell.Id}'.");
            else if (requireEtabsConnection && existingShellProperties.Count > 0 && !existingShellProperties.Contains(propertyName))
                Add(result, ValidationSeverity.Critical, $"Selected shell property '{propertyName}' does not exist in the connected ETABS model.");
        }

        var existingLoadPatterns = new HashSet<string>(loadPatterns, StringComparer.OrdinalIgnoreCase);
        var groups = model.Members
            .Select(member => member.Group)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ParametricLoad load in model.Loads)
        {
            if (string.IsNullOrWhiteSpace(load.LoadPattern))
                Add(result, ValidationSeverity.Critical, $"Load '{load.Id}' has no load pattern.");
            else if (requireEtabsConnection && existingLoadPatterns.Count > 0 && !existingLoadPatterns.Contains(load.LoadPattern))
                Add(result, ValidationSeverity.Critical, $"Load pattern '{load.LoadPattern}' does not exist in the connected ETABS model.");

            if (load.TargetType.Equals("Node", StringComparison.OrdinalIgnoreCase) && !nodes.ContainsKey(load.TargetId))
                Add(result, ValidationSeverity.Critical, $"Load '{load.Id}' targets missing node '{load.TargetId}'.");

            if (load.TargetType.Equals("MemberGroup", StringComparison.OrdinalIgnoreCase) && !groups.Contains(load.TargetId))
                Add(result, ValidationSeverity.Critical, $"Load '{load.Id}' targets missing member group '{load.TargetId}'.");
        }

        foreach (string warning in model.Warnings)
            Add(result, ValidationSeverity.Warning, warning);

        if (result.Issues.Count == 0)
            Add(result, ValidationSeverity.Info, "Validation passed.");

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

    private static void Add(ParametricValidationResult result, ValidationSeverity severity, string message)
    {
        result.Issues.Add(new ValidationIssue
        {
            Severity = severity,
            Message = message
        });
    }
}
