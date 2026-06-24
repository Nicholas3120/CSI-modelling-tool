using TrussModelling.Models;

namespace TrussModelling.Services;

public sealed class ParametricDomeValidator
{
    public ParametricValidationResult Validate(
        ParametricDomeModel model,
        string? selectedEtabsInstanceId,
        bool etabsDataLoaded,
        IReadOnlyCollection<string> frameSections,
        IReadOnlyCollection<string> shellProperties,
        bool requireEtabsConnection)
    {
        var result = new ParametricValidationResult();

        if (requireEtabsConnection)
        {
            if (string.IsNullOrWhiteSpace(selectedEtabsInstanceId))
                Add(result, ValidationSeverity.Critical, "Select a target ETABS model.");

            if (!etabsDataLoaded)
                Add(result, ValidationSeverity.Critical, "Read ETABS data before drawing the dome.");
        }

        if (string.IsNullOrWhiteSpace(model.DomeId))
            Add(result, ValidationSeverity.Critical, "Dome ID is required.");
        if (!double.IsFinite(model.BaseRadius) || model.BaseRadius <= 0)
            Add(result, ValidationSeverity.Critical, "Base radius must be greater than zero.");
        if (!double.IsFinite(model.DomeRise) || model.DomeRise <= 0)
            Add(result, ValidationSeverity.Critical, "Dome rise must be greater than zero.");
        if (model.RingCount < 2)
            Add(result, ValidationSeverity.Critical, "Number of rings must be at least 2.");
        if (model.Full360 && model.SegmentCount < 6)
            Add(result, ValidationSeverity.Critical, "Full dome requires at least 6 circumferential segments.");
        if (!model.Full360 && model.EndAngleDeg <= model.StartAngleDeg)
            Add(result, ValidationSeverity.Critical, "End angle must be greater than start angle for a partial dome.");
        if (model.LowerCutHeight < 0 || model.UpperCutHeight > model.DomeRise || model.UpperCutHeight <= model.LowerCutHeight)
            Add(result, ValidationSeverity.Critical, "Cut heights must satisfy 0 <= lower < upper <= dome rise.");

        var existingShellProperties = new HashSet<string>(shellProperties, StringComparer.OrdinalIgnoreCase);
        if (model.GenerateShellPanels)
        {
            if (string.IsNullOrWhiteSpace(model.ShellPropertyName))
                Add(result, ValidationSeverity.Critical, "Select a shell property before drawing dome shell panels.");
            else if (requireEtabsConnection && existingShellProperties.Count > 0 && !existingShellProperties.Contains(model.ShellPropertyName))
                Add(result, ValidationSeverity.Critical, $"Selected shell property '{model.ShellPropertyName}' does not exist in the connected ETABS model.");
        }

        var existingSections = new HashSet<string>(frameSections, StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<DomeMemberGroup, DomeFrameMember> group in model.FrameMembers.GroupBy(member => member.Group))
        {
            string sectionName = group.FirstOrDefault()?.SectionName ?? "";
            if (string.IsNullOrWhiteSpace(sectionName))
            {
                Add(result, ValidationSeverity.Critical, $"Select a frame section for {group.Key} dome members.");
                continue;
            }

            if (requireEtabsConnection && existingSections.Count > 0 && !existingSections.Contains(sectionName))
                Add(result, ValidationSeverity.Critical, $"Selected {group.Key} frame section '{sectionName}' does not exist in the connected ETABS model.");
        }

        AddDuplicateChecks(result, "dome node", model.Nodes.Select(node => node.Id));
        AddDuplicateChecks(result, "dome frame", model.FrameMembers.Select(member => member.Id));
        AddDuplicateChecks(result, "dome shell panel", model.ShellPanels.Select(panel => panel.Id));

        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        foreach (DomeFrameMember member in model.FrameMembers)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out DomeNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out DomeNode? end))
            {
                Add(result, ValidationSeverity.Critical, $"Dome frame '{member.Id}' references a missing node.");
                continue;
            }

            double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2) + Math.Pow(end.Z - start.Z, 2));
            if (!double.IsFinite(length) || length <= 0.000001)
                Add(result, ValidationSeverity.Critical, $"Dome frame '{member.Id}' has zero length.");
        }

        foreach (DomeShellPanel panel in model.ShellPanels)
        {
            if (panel.NodeIds.Count is < 3 or > 4)
                Add(result, ValidationSeverity.Critical, $"Dome shell panel '{panel.Id}' must have 3 or 4 nodes.");

            foreach (string nodeId in panel.NodeIds)
            {
                if (!nodes.ContainsKey(nodeId))
                    Add(result, ValidationSeverity.Critical, $"Dome shell panel '{panel.Id}' references missing node '{nodeId}'.");
            }
        }

        if (model.ShellPanels.Count > 5000 || model.FrameMembers.Count > 5000)
            Add(result, ValidationSeverity.Warning, "Generated dome object count is high. ETABS drawing may take noticeable time.");

        foreach (string warning in model.Warnings)
            Add(result, ValidationSeverity.Warning, warning);

        if (result.Issues.Count == 0)
            Add(result, ValidationSeverity.Info, "Dome validation passed.");

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
