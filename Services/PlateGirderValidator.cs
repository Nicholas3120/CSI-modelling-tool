using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class PlateGirderValidator
{
    public ParametricValidationResult Validate(
        ParametricPlateGirderModel model,
        string? selectedEtabsInstanceId,
        bool etabsDataLoaded,
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
                Add(result, ValidationSeverity.Critical, "Read ETABS data before drawing the plate girder.");
        }

        if (string.IsNullOrWhiteSpace(model.PlateGirderId))
            Add(result, ValidationSeverity.Critical, "Plate girder ID is required.");
        if (!double.IsFinite(model.Length) || model.Length <= 0)
            Add(result, ValidationSeverity.Critical, "Length must be greater than zero.");
        if (!double.IsFinite(model.Depth) || model.Depth <= 0)
            Add(result, ValidationSeverity.Critical, "Depth must be greater than zero.");
        if (!double.IsFinite(model.FlangeWidth) || model.FlangeWidth <= 0)
            Add(result, ValidationSeverity.Critical, "Flange width must be greater than zero.");
        if (model.LengthDivisions < 1 || model.DepthDivisions < 1)
            Add(result, ValidationSeverity.Critical, "Mesh divisions must be at least 1.");

        if (model.HasWebOpening)
        {
            IReadOnlyList<PlateGirderOpening> openings = model.Openings.Count > 0
                ? model.Openings
                :
                [
                    new PlateGirderOpening
                    {
                        Id = "OP01",
                        CenterX = model.OpeningCenterX,
                        CenterZ = model.OpeningCenterZ,
                        Width = model.OpeningWidth,
                        Height = model.OpeningHeight,
                        StiffenerOutstand = model.OpeningStiffenerWidth,
                        StiffenerExtension = model.OpeningStiffenerExtension
                    }
                ];

            foreach (PlateGirderOpening opening in openings)
            {
                string label = string.IsNullOrWhiteSpace(opening.Id) ? "Opening" : $"Opening '{opening.Id}'";
                if (opening.Width <= 0 || opening.Height <= 0)
                    Add(result, ValidationSeverity.Critical, $"{label} width and height must be greater than zero.");
                if (opening.Width >= model.Length)
                    Add(result, ValidationSeverity.Critical, $"{label} width must be smaller than girder length.");
                if (opening.Height >= model.Depth)
                    Add(result, ValidationSeverity.Critical, $"{label} height must be smaller than web depth.");
                if (opening.CenterX - opening.Width / 2.0 < 0 || opening.CenterX + opening.Width / 2.0 > model.Length)
                    Add(result, ValidationSeverity.Warning, $"{label} is outside the web length and will be adjusted inside the web.");
                if (opening.CenterZ - opening.Height / 2.0 < 0 || opening.CenterZ + opening.Height / 2.0 > model.Depth)
                    Add(result, ValidationSeverity.Warning, $"{label} is outside the web depth and will be adjusted inside the web.");
                if (opening.StiffenerOutstand <= 0)
                    Add(result, ValidationSeverity.Critical, $"{label} stiffener outstand must be greater than zero.");
                if (opening.StiffenerExtension < 0)
                    Add(result, ValidationSeverity.Critical, $"{label} stiffener extension cannot be negative.");
            }
        }

        if (model.WebThickness <= 0 || model.FlangeThickness <= 0 || model.StiffenerThickness <= 0)
            Add(result, ValidationSeverity.Critical, "Plate thicknesses must be greater than zero.");
        if (model.WebSteelYieldStrengthMpa <= 0 || model.FlangeSteelYieldStrengthMpa <= 0 || model.StiffenerSteelYieldStrengthMpa <= 0 || model.ElasticModulusGpa <= 0)
            Add(result, ValidationSeverity.Critical, "Steel grades and elastic modulus must be greater than zero.");
        if (model.OpeningStiffenerExtension < 0)
            Add(result, ValidationSeverity.Critical, "Opening stiffener extension cannot be negative.");

        var existingShellProperties = new HashSet<string>(shellProperties, StringComparer.OrdinalIgnoreCase);
        CheckShellProperty(result, model.WebShellPropertyName, existingShellProperties, requireEtabsConnection, "web");
        if (model.GenerateTopFlange || model.GenerateBottomFlange)
            CheckShellProperty(result, model.FlangeShellPropertyName, existingShellProperties, requireEtabsConnection, "flange");
        if (model.ShellPanels.Any(panel => IsStiffener(panel.Group)))
            CheckShellProperty(result, model.StiffenerShellPropertyName, existingShellProperties, requireEtabsConnection, "stiffener");

        if (model.ApplyTopFlangeAreaLoad && Math.Abs(model.AnalysisUniformLoadKnPerM) > 0.000001)
        {
            var existingLoadPatterns = new HashSet<string>(loadPatterns, StringComparer.OrdinalIgnoreCase);
            if ((requireEtabsConnection || etabsDataLoaded) && string.IsNullOrWhiteSpace(model.LoadPattern))
                Add(result, ValidationSeverity.Critical, "Select a load pattern before applying plate girder shell area load.");
            else if (requireEtabsConnection && existingLoadPatterns.Count > 0 && !existingLoadPatterns.Contains(model.LoadPattern))
                Add(result, ValidationSeverity.Critical, $"Load pattern '{model.LoadPattern}' does not exist in the connected ETABS model.");

            if (!model.GenerateTopFlange)
                Add(result, ValidationSeverity.Critical, "Top flange shell panels must be generated before applying UDL as shell area load.");
        }

        AddDuplicateChecks(result, "plate girder node", model.Nodes.Select(node => node.Id));
        AddDuplicateChecks(result, "plate girder shell panel", model.ShellPanels.Select(panel => panel.Id));

        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        foreach (PlateGirderShellPanel panel in model.ShellPanels)
        {
            if (panel.NodeIds.Count != 4)
                Add(result, ValidationSeverity.Critical, $"Plate girder shell panel '{panel.Id}' must be a quad panel.");

            foreach (string nodeId in panel.NodeIds)
            {
                if (!nodes.ContainsKey(nodeId))
                    Add(result, ValidationSeverity.Critical, $"Plate girder shell panel '{panel.Id}' references missing node '{nodeId}'.");
            }
        }

        if (model.ShellPanels.Count > 8000)
            Add(result, ValidationSeverity.Warning, "Generated plate girder shell count is high. ETABS drawing may take noticeable time.");

        foreach (string warning in model.Warnings)
            Add(result, ValidationSeverity.Warning, warning);

        if (result.Issues.Count == 0)
            Add(result, ValidationSeverity.Info, "Plate girder validation passed.");

        return result;
    }

    private static void CheckShellProperty(
        ParametricValidationResult result,
        string shellProperty,
        HashSet<string> existingShellProperties,
        bool requireEtabsConnection,
        string label)
    {
        if (string.IsNullOrWhiteSpace(shellProperty))
        {
            Add(result, ValidationSeverity.Critical, $"Select a shell property for {label} plates.");
            return;
        }

        if (requireEtabsConnection && existingShellProperties.Count > 0 && !existingShellProperties.Contains(shellProperty))
            Add(result, ValidationSeverity.Critical, $"Selected {label} shell property '{shellProperty}' does not exist in the connected ETABS model.");
    }

    private static bool IsStiffener(PlateGirderShellGroup group)
    {
        return group is PlateGirderShellGroup.OpeningTopStiffener or
            PlateGirderShellGroup.OpeningBottomStiffener or
            PlateGirderShellGroup.OpeningLeftStiffener or
            PlateGirderShellGroup.OpeningRightStiffener;
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
