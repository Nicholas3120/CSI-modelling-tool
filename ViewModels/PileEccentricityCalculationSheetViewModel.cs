using TrussModelling.Models;

namespace TrussModelling.ViewModels;

public sealed class PileEccentricityCalculationSheetViewModel
{
    private static readonly IReadOnlyList<string> SectionOrder =
    [
        "Geometry",
        "Resistance geometry",
        "Eccentricity",
        "Moment",
        "Pile distribution",
        "Isolated pile load",
        "Tie beam transfer",
        "Tie beam section design",
        "Revised pile load"
    ];

    public PileEccentricityCalculationSheetViewModel(PileEccentricityViewModel source)
    {
        Sections = source.CalculationSteps
            .GroupBy(step => step.Section)
            .OrderBy(group => SectionSortIndex(group.Key))
            .ThenBy(group => group.Key)
            .Select(group => new PileEccentricityCalculationSheetSection(
                group.Key,
                BuildDescription(group.Key),
                group.ToList()))
            .ToList();

        Comparisons = source.Comparisons.ToList();
        Messages = source.Messages.ToList();
        Title = "Pile Ecc Point Load Calculation Sheet";
        Subtitle = $"Prepared from current input - {Sections.Sum(section => section.Steps.Count)} calculation step(s)";
    }

    public string Title { get; }
    public string Subtitle { get; }
    public IReadOnlyList<PileEccentricityCalculationSheetSection> Sections { get; }
    public IReadOnlyList<PileEccentricityComparisonRow> Comparisons { get; }
    public IReadOnlyList<ValidationIssue> Messages { get; }
    public bool HasComparisons => Comparisons.Count > 0;
    public bool HasMessages => Messages.Count > 0;

    private static int SectionSortIndex(string section)
    {
        int index = SectionOrder
            .Select((name, i) => new { name, i })
            .FirstOrDefault(item => string.Equals(item.name, section, StringComparison.OrdinalIgnoreCase))?.i ?? -1;

        return index >= 0 ? index : SectionOrder.Count;
    }

    private static string BuildDescription(string section)
    {
        return section switch
        {
            "Geometry" => "Actual shifted pile positions are used to calculate the active pile centroid.",
            "Resistance geometry" => "Original standard pile layout is used for pile-group resistance arms.",
            "Eccentricity" => "Column position is compared with the actual pile centroid.",
            "Moment" => "Eccentricity is converted to pile-cap moment components.",
            "Pile distribution" => "Pile lever-arm square sums are prepared for axial load distribution.",
            "Isolated pile load" => "Pile loads before any tie-beam transfer are calculated.",
            "Tie beam transfer" => "Selected eccentricity is treated as a point load on a simply supported tie beam. Between supports, both pile groups get added compression; overhang cases produce uplift at the opposite support.",
            "Tie beam section design" => "The tie-beam design moment is used for Eurocode rectangular beam rebar design, including the K limit and compression steel check.",
            "Revised pile load" => "Pile loads after ideal tie-beam transfer are calculated.",
            _ => ""
        };
    }
}

public sealed record PileEccentricityCalculationSheetSection(
    string Title,
    string Description,
    IReadOnlyList<PileEccentricityCalculationStep> Steps);
