namespace CSIModellingTools.Features.IfcImport;

public sealed class DuplicateFrameDetector
{
    public List<IfcImportWarning> DetectDuplicates(
        IReadOnlyList<AnalyticalFrameElement> frames,
        double endpointTolerance,
        double sectionTolerance)
    {
        var warnings = new List<IfcImportWarning>();
        if (frames.Count < 2 || !double.IsFinite(endpointTolerance) || endpointTolerance <= 0)
            return warnings;

        Dictionary<IfcSpatialCellKey, List<EndpointEntry>> endpointIndex = BuildEndpointIndex(frames, endpointTolerance);
        var checkedPairs = new HashSet<long>();

        for (int firstIndex = 0; firstIndex < frames.Count; firstIndex++)
        {
            AnalyticalFrameElement first = frames[firstIndex];
            foreach (int secondIndex in CandidateFrameIndexes(first, firstIndex, endpointIndex, endpointTolerance))
            {
                long pairKey = IfcSpatialIndex.PairKey(firstIndex, secondIndex);
                if (!checkedPairs.Add(pairKey))
                    continue;

                AnalyticalFrameElement second = frames[secondIndex];
                if (!HasSameEndpoints(first, second, endpointTolerance) ||
                    !HasSimilarSection(first.SectionInfo, second.SectionInfo, sectionTolerance))
                {
                    continue;
                }

                string message = $"Possible duplicate frame: matches '{second.SourceName}' ({second.SourceGuid}) within {endpointTolerance:0.###} m endpoint tolerance.";
                first.Warnings.Add(message);
                warnings.Add(new IfcImportWarning
                {
                    SourceGuid = first.SourceGuid,
                    SourceName = first.SourceName,
                    Severity = IfcImportWarningSeverity.Warning,
                    Category = IfcImportWarningCategory.Duplicate,
                    Message = message
                });
            }
        }

        return warnings;
    }

    private static bool HasSameEndpoints(AnalyticalFrameElement first, AnalyticalFrameElement second, double tolerance)
    {
        bool sameDirection = IfcSpatialIndex.Distance(first.StartPoint, second.StartPoint) <= tolerance &&
            IfcSpatialIndex.Distance(first.EndPoint, second.EndPoint) <= tolerance;
        bool reversedDirection = IfcSpatialIndex.Distance(first.StartPoint, second.EndPoint) <= tolerance &&
            IfcSpatialIndex.Distance(first.EndPoint, second.StartPoint) <= tolerance;
        return sameDirection || reversedDirection;
    }

    private static bool HasSimilarSection(SectionInfo first, SectionInfo second, double tolerance)
    {
        if (first.ShapeType != second.ShapeType)
            return false;
        if (!string.IsNullOrWhiteSpace(first.SectionName) &&
            !string.IsNullOrWhiteSpace(second.SectionName) &&
            string.Equals(first.SectionName, second.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        double effectiveTolerance = double.IsFinite(tolerance) && tolerance >= 0 ? tolerance : 0.001;
        return Math.Abs(first.Width - second.Width) <= effectiveTolerance &&
            Math.Abs(first.Depth - second.Depth) <= effectiveTolerance &&
            Math.Abs(first.Diameter - second.Diameter) <= effectiveTolerance &&
            Math.Abs(first.FlangeWidth - second.FlangeWidth) <= effectiveTolerance &&
            Math.Abs(first.FlangeThickness - second.FlangeThickness) <= effectiveTolerance &&
            Math.Abs(first.WebThickness - second.WebThickness) <= effectiveTolerance;
    }

    private static IEnumerable<int> CandidateFrameIndexes(
        AnalyticalFrameElement frame,
        int frameIndex,
        Dictionary<IfcSpatialCellKey, List<EndpointEntry>> endpointIndex,
        double tolerance)
    {
        var candidates = new HashSet<int>();
        foreach (EndpointEntry entry in NearbyEndpointEntries(frame.StartPoint, endpointIndex, tolerance))
        {
            if (entry.FrameIndex > frameIndex)
                candidates.Add(entry.FrameIndex);
        }

        foreach (EndpointEntry entry in NearbyEndpointEntries(frame.EndPoint, endpointIndex, tolerance))
        {
            if (entry.FrameIndex > frameIndex)
                candidates.Add(entry.FrameIndex);
        }

        return candidates;
    }

    private static IEnumerable<EndpointEntry> NearbyEndpointEntries(
        AnalyticalPoint point,
        Dictionary<IfcSpatialCellKey, List<EndpointEntry>> endpointIndex,
        double tolerance)
    {
        foreach (IfcSpatialCellKey cell in IfcSpatialIndex.NeighborCells(point, tolerance))
        {
            if (!endpointIndex.TryGetValue(cell, out List<EndpointEntry>? entries))
                continue;

            foreach (EndpointEntry entry in entries)
            {
                if (IfcSpatialIndex.Distance(point, entry.Point) <= tolerance)
                    yield return entry;
            }
        }
    }

    private static Dictionary<IfcSpatialCellKey, List<EndpointEntry>> BuildEndpointIndex(
        IReadOnlyList<AnalyticalFrameElement> frames,
        double tolerance)
    {
        var index = new Dictionary<IfcSpatialCellKey, List<EndpointEntry>>();
        for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            AddEndpoint(index, frameIndex, frames[frameIndex].StartPoint, tolerance);
            AddEndpoint(index, frameIndex, frames[frameIndex].EndPoint, tolerance);
        }

        return index;
    }

    private static void AddEndpoint(
        Dictionary<IfcSpatialCellKey, List<EndpointEntry>> index,
        int frameIndex,
        AnalyticalPoint point,
        double tolerance)
    {
        IfcSpatialCellKey cell = IfcSpatialIndex.CellFor(point, tolerance);
        if (!index.TryGetValue(cell, out List<EndpointEntry>? entries))
        {
            entries = [];
            index[cell] = entries;
        }

        entries.Add(new EndpointEntry(frameIndex, point));
    }

    private sealed record EndpointEntry(int FrameIndex, AnalyticalPoint Point);
}
