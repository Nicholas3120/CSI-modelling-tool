using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class WallDrainValidator
{
    public ParametricValidationResult Validate(
        WallDrainModel model,
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
                Add(result, ValidationSeverity.Critical, "Read ETABS data before drawing the wall/drain.");
        }

        if (string.IsNullOrWhiteSpace(model.StructureId))
            Add(result, ValidationSeverity.Critical, "Wall/drain ID is required.");
        if (!double.IsFinite(model.LengthY) || model.LengthY <= 0)
            Add(result, ValidationSeverity.Critical, "Length Y must be greater than zero.");
        if (!double.IsFinite(model.Height) || model.Height <= 0)
            Add(result, ValidationSeverity.Critical, "Height must be greater than zero.");
        if ((model.ShapeMode is WallDrainShapeMode.UDrain or WallDrainShapeMode.BoxDrain) && (!double.IsFinite(model.ClearWidth) || model.ClearWidth <= 0))
            Add(result, ValidationSeverity.Critical, "Clear width must be greater than zero for drain modes.");
        if (model.FrameMembers.Count == 0 && model.ShellPanels.Count == 0)
            Add(result, ValidationSeverity.Critical, "No wall/drain frame members or shell panels were generated.");

        AddDuplicateChecks(result, "wall/drain node", model.Nodes.Select(node => node.Id));
        AddDuplicateChecks(result, "wall/drain frame member", model.FrameMembers.Select(member => member.Id));
        AddDuplicateChecks(result, "wall/drain shell panel", model.ShellPanels.Select(panel => panel.Id));

        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        foreach (WallDrainFrameMember member in model.FrameMembers)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out WallDrainNode? start))
                Add(result, ValidationSeverity.Critical, $"Frame member '{member.Id}' references missing start node '{member.StartNodeId}'.");

            if (!nodes.TryGetValue(member.EndNodeId, out WallDrainNode? end))
                Add(result, ValidationSeverity.Critical, $"Frame member '{member.Id}' references missing end node '{member.EndNodeId}'.");

            if (start != null && end != null)
            {
                double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2) + Math.Pow(end.Z - start.Z, 2));
                if (!double.IsFinite(length) || length <= 0.000001)
                    Add(result, ValidationSeverity.Critical, $"Frame member '{member.Id}' has zero length.");
            }
        }

        foreach (WallDrainShellPanel panel in model.ShellPanels)
        {
            if (panel.NodeIds.Count < 3)
                Add(result, ValidationSeverity.Critical, $"Shell panel '{panel.Id}' has fewer than three nodes.");

            foreach (string nodeId in panel.NodeIds)
            {
                if (!nodes.ContainsKey(nodeId))
                    Add(result, ValidationSeverity.Critical, $"Shell panel '{panel.Id}' references missing node '{nodeId}'.");
            }

            List<WallDrainNode> panelNodes = panel.NodeIds
                .Where(nodes.ContainsKey)
                .Select(nodeId => nodes[nodeId])
                .ToList();

            if (panelNodes.Count >= 3 && CalculateArea(panelNodes) <= 0.000001)
                Add(result, ValidationSeverity.Critical, $"Shell panel '{panel.Id}' has zero area.");
        }

        var existingFrameSections = new HashSet<string>(frameSections, StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, WallDrainFrameMember> group in model.FrameMembers.GroupBy(member => member.Group, StringComparer.OrdinalIgnoreCase))
        {
            string sectionName = group.FirstOrDefault()?.SectionName?.Trim() ?? "";
            if (sectionName.Length == 0)
            {
                Add(result, ValidationSeverity.Critical, $"Select a frame section for {FormatGroupName(group.Key)}.");
                continue;
            }

            if (requireEtabsConnection && existingFrameSections.Count > 0 && !existingFrameSections.Contains(sectionName))
                Add(result, ValidationSeverity.Critical, $"Selected {FormatGroupName(group.Key)} frame section '{sectionName}' does not exist in the connected ETABS model.");
        }

        var existingShellProperties = new HashSet<string>(shellProperties, StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, WallDrainShellPanel> group in model.ShellPanels.GroupBy(panel => panel.Group, StringComparer.OrdinalIgnoreCase))
        {
            string propertyName = group.FirstOrDefault()?.ShellPropertyName?.Trim() ?? "";
            if (propertyName.Length == 0)
            {
                Add(result, ValidationSeverity.Critical, $"Select a shell property for {FormatGroupName(group.Key)}.");
                continue;
            }

            if (requireEtabsConnection && existingShellProperties.Count > 0 && !existingShellProperties.Contains(propertyName))
                Add(result, ValidationSeverity.Critical, $"Selected {FormatGroupName(group.Key)} shell property '{propertyName}' does not exist in the connected ETABS model.");
        }

        var existingLoadPatterns = new HashSet<string>(loadPatterns, StringComparer.OrdinalIgnoreCase);
        foreach (WallDrainSurfaceLoad load in model.SurfaceLoads)
        {
            if (string.IsNullOrWhiteSpace(load.LoadPattern))
                Add(result, ValidationSeverity.Critical, $"Load '{load.Id}' has no load pattern.");
            else if (requireEtabsConnection && existingLoadPatterns.Count > 0 && !existingLoadPatterns.Contains(load.LoadPattern))
                Add(result, ValidationSeverity.Critical, $"Load pattern '{load.LoadPattern}' does not exist in the connected ETABS model.");

            if (load.TargetGroups.Count == 0)
                Add(result, ValidationSeverity.Critical, $"Load '{load.Id}' has no vertical wall target panels.");

            if (load.Kind == WallDrainLoadKind.Udl && (!double.IsFinite(load.UniformPressureKnPerM2) || Math.Abs(load.UniformPressureKnPerM2) <= 0.000001))
                Add(result, ValidationSeverity.Warning, $"UDL load '{load.Id}' has zero pressure.");

            if (load.Kind == WallDrainLoadKind.Triangular &&
                (!double.IsFinite(load.TopPressureKnPerM2) || !double.IsFinite(load.BottomPressureKnPerM2) ||
                (Math.Abs(load.TopPressureKnPerM2) <= 0.000001 && Math.Abs(load.BottomPressureKnPerM2) <= 0.000001)))
            {
                Add(result, ValidationSeverity.Warning, $"Triangular load '{load.Id}' has zero pressure.");
            }
        }

        if (model.FrameMembers.Count > 200)
            Add(result, ValidationSeverity.Warning, "Generated frame count is high. ETABS drawing may take noticeable time.");
        if (model.ShellPanels.Count > 500)
            Add(result, ValidationSeverity.Warning, "Generated shell panel count is high. ETABS drawing may take noticeable time.");

        foreach (string warning in model.Warnings)
            Add(result, ValidationSeverity.Warning, warning);

        if (result.Issues.Count == 0)
            Add(result, ValidationSeverity.Info, "Wall/drain validation passed.");

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
            WallDrainPanelGroups.Stem => "stem",
            WallDrainPanelGroups.LeftWall => "left wall",
            WallDrainPanelGroups.RightWall => "right wall",
            WallDrainPanelGroups.BaseSlab => "base slab",
            WallDrainPanelGroups.TopSlab => "top slab",
            WallDrainPanelGroups.Buttress => "buttress",
            WallDrainPanelGroups.Counterfort => "counterfort",
            _ => group
        };
    }

    private static double CalculateArea(IReadOnlyList<WallDrainNode> nodes)
    {
        double area = 0;
        WallDrainNode anchor = nodes[0];
        for (int index = 1; index < nodes.Count - 1; index++)
        {
            WallDrainNode b = nodes[index];
            WallDrainNode c = nodes[index + 1];
            double ux = b.X - anchor.X;
            double uy = b.Y - anchor.Y;
            double uz = b.Z - anchor.Z;
            double vx = c.X - anchor.X;
            double vy = c.Y - anchor.Y;
            double vz = c.Z - anchor.Z;
            double cx = uy * vz - uz * vy;
            double cy = uz * vx - ux * vz;
            double cz = ux * vy - uy * vx;
            area += Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2.0;
        }

        return area;
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
