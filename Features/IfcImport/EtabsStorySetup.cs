namespace CSIModellingTools.Features.IfcImport;

/// <summary>
/// Builds the ETABS story system from the IFC building storeys before objects are added,
/// so the model is correctly elevated and organised into a story per floor. Storeys at the
/// same elevation (multi-block IFCs repeat them per block) are merged into one global story.
/// </summary>
internal static class EtabsStorySetup
{
    private const double MergeToleranceMetres = 0.5;

    public static bool Configure(ETABSv1.cSapModel sapModel, IReadOnlyList<IfcStoreyLevel> levels)
    {
        if (levels == null || levels.Count == 0)
            return false;

        List<(double Elevation, string Name)> merged = MergeByElevation(levels);
        if (merged.Count < 2)
            return false; // need at least a base plus one story to define a story system

        double baseElevation = merged[0].Elevation;
        int numberStories = merged.Count - 1;
        var names = new string[numberStories];
        var heights = new double[numberStories];
        var isMaster = new bool[numberStories];
        var similarTo = new string[numberStories];
        var spliceAbove = new bool[numberStories];
        var spliceHeight = new double[numberStories];
        var color = new int[numberStories];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Stories are ordered bottom-to-top; each height is the rise from the level below.
        for (int i = 0; i < numberStories; i++)
        {
            names[i] = UniqueName(merged[i + 1].Name, i + 1, used);
            heights[i] = merged[i + 1].Elevation - merged[i].Elevation;
            isMaster[i] = true;
            similarTo[i] = "";
            spliceAbove[i] = false;
            spliceHeight[i] = 0;
            color[i] = -1;
        }

        int ret = sapModel.Story.SetStories_2(
            baseElevation, numberStories, ref names, ref heights,
            ref isMaster, ref similarTo, ref spliceAbove, ref spliceHeight, ref color);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not define the story system from IFC storeys. Return code: {ret}.");

        return true;
    }

    private static List<(double Elevation, string Name)> MergeByElevation(IReadOnlyList<IfcStoreyLevel> levels)
    {
        var result = new List<(double Elevation, string Name)>();
        foreach (IfcStoreyLevel level in levels
            .Where(level => double.IsFinite(level.Elevation))
            .OrderBy(level => level.Elevation))
        {
            if (result.Count > 0 && Math.Abs(level.Elevation - result[^1].Elevation) <= MergeToleranceMetres)
                continue;

            result.Add((level.Elevation, (level.Name ?? "").Trim()));
        }

        return result;
    }

    private static string UniqueName(string raw, int index, HashSet<string> used)
    {
        string baseName = EtabsFrameExporter.BuildSafeEtabsName(
            string.IsNullOrWhiteSpace(raw) ? $"Story{index}" : raw, "Story", 50);
        string candidate = baseName;
        int suffix = 2;
        while (!used.Add(candidate))
            candidate = $"{baseName}_{suffix++}";

        return candidate;
    }
}
