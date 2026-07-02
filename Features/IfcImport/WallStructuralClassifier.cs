using Xbim.Ifc4.Interfaces;

namespace CSIModellingTools.Features.IfcImport;

/// <summary>
/// Classifies a wall as structural (shear/core/party) or not, using the IFC's own engineering
/// metadata rather than geometric guesswork. In this project every wall carries a
/// <c>LoadBearing</c> ("Yes"/"No") property and facade flags (e.g. "Prefinished Facade"); a
/// structural wall is load-bearing and not a facade panel. Modelling precast facade cladding as
/// shear walls over-stiffens the building and mis-distributes lateral load, so those are excluded.
/// When the LoadBearing property is absent (other IFC sources) the result is null and the caller
/// should fall back to a thickness heuristic.
/// </summary>
public static class WallStructuralClassifier
{
    private const string LoadBearingProperty = "LoadBearing";
    private static readonly string[] FacadeProperties =
    [
        "Prefinished Facade",
        "BeamFacade",
        "Double Bay Facade"
    ];

    public static bool? IsStructural(IIfcProduct product)
    {
        string? loadBearing = ReadProperty(product, LoadBearingProperty);
        if (loadBearing == null)
            return null;

        bool bearing = IsYes(loadBearing);
        bool facade = FacadeProperties.Any(name => IsYes(ReadProperty(product, name)));
        return bearing && !facade;
    }

    private static bool IsYes(string? value)
    {
        string trimmed = value?.Trim() ?? "";
        return string.Equals(trimmed, "Yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "True", StringComparison.OrdinalIgnoreCase)
            || trimmed == ".T.";
    }

    private static string? ReadProperty(IIfcProduct product, string propertyName)
    {
        if (product is not IIfcObject obj)
            return null;

        foreach (IIfcRelDefinesByProperties relation in obj.IsDefinedBy)
        {
            foreach (IIfcPropertySetDefinition definition in EnumerateSetDefinitions(relation.RelatingPropertyDefinition))
            {
                if (definition is not IIfcPropertySet propertySet)
                    continue;

                foreach (IIfcProperty property in propertySet.HasProperties)
                {
                    if (property is IIfcPropertySingleValue singleValue
                        && string.Equals(singleValue.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                        return singleValue.NominalValue?.Value?.ToString();
                }
            }
        }

        return null;
    }

    private static IEnumerable<IIfcPropertySetDefinition> EnumerateSetDefinitions(IIfcPropertySetDefinitionSelect? select)
    {
        // A plain IfcPropertySet is itself an IfcPropertySetDefinition (the case Revit exports);
        // richer set-of-sets selects are not used by this source.
        return select is IIfcPropertySetDefinition definition ? [definition] : [];
    }
}
