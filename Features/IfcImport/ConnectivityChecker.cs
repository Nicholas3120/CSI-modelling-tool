namespace CSIModellingTools.Features.IfcImport;

public sealed class ConnectivityChecker
{
    public List<IfcImportWarning> CheckBeamEndpointConnectivity(
        IReadOnlyList<AnalyticalFrameElement> frames,
        double tolerance)
    {
        var warnings = new List<IfcImportWarning>();
        if (frames.Count == 0 || !double.IsFinite(tolerance) || tolerance <= 0)
            return warnings;

        Dictionary<IfcSpatialCellKey, List<EndpointEntry>> endpointIndex = BuildEndpointIndex(frames, tolerance);
        for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            AnalyticalFrameElement beam = frames[frameIndex];
            if (beam.IfcType != "IfcBeam")
                continue;

            CheckEndpoint(beam, frameIndex, "start", beam.StartPoint, endpointIndex, tolerance, warnings);
            CheckEndpoint(beam, frameIndex, "end", beam.EndPoint, endpointIndex, tolerance, warnings);
        }

        return warnings;
    }

    private static void CheckEndpoint(
        AnalyticalFrameElement beam,
        int beamIndex,
        string endpointName,
        AnalyticalPoint endpoint,
        Dictionary<IfcSpatialCellKey, List<EndpointEntry>> endpointIndex,
        double tolerance,
        List<IfcImportWarning> warnings)
    {
        bool connected = IfcSpatialIndex.NeighborCells(endpoint, tolerance)
            .Where(endpointIndex.ContainsKey)
            .SelectMany(cell => endpointIndex[cell])
            .Any(entry => entry.FrameIndex != beamIndex && IfcSpatialIndex.Distance(endpoint, entry.Point) <= tolerance);

        if (connected)
            return;

        string message = $"Beam {endpointName} endpoint is not connected to another frame endpoint within {tolerance:0.###} m.";
        beam.Warnings.Add(message);
        warnings.Add(new IfcImportWarning
        {
            SourceGuid = beam.SourceGuid,
            SourceName = beam.SourceName,
            Severity = IfcImportWarningSeverity.Warning,
            Category = IfcImportWarningCategory.Connectivity,
            Message = message
        });
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
