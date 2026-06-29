namespace CSIModellingTools.Features.IfcImport;

public sealed class IfcImportNodeSnapper
{
    public List<IfcImportWarning> SnapFrameEndpoints(List<AnalyticalFrameElement> frames, double tolerance)
    {
        var warnings = new List<IfcImportWarning>();
        if (frames.Count == 0 || !double.IsFinite(tolerance) || tolerance <= 0)
            return warnings;

        var clusters = new List<EndpointCluster>();
        var clusterIndex = new Dictionary<IfcSpatialCellKey, List<int>>();
        foreach (AnalyticalFrameElement frame in frames)
        {
            SnapEndpoint(frame, isStart: true, frame.StartPoint, tolerance, clusters, clusterIndex, warnings);
            SnapEndpoint(frame, isStart: false, frame.EndPoint, tolerance, clusters, clusterIndex, warnings);
        }

        return warnings;
    }

    private static void SnapEndpoint(
        AnalyticalFrameElement frame,
        bool isStart,
        AnalyticalPoint point,
        double tolerance,
        List<EndpointCluster> clusters,
        Dictionary<IfcSpatialCellKey, List<int>> clusterIndex,
        List<IfcImportWarning> warnings)
    {
        EndpointCluster? cluster = FindCluster(point, tolerance, clusters, clusterIndex);
        if (cluster == null)
        {
            int clusterIndexValue = clusters.Count;
            clusters.Add(new EndpointCluster(point.Clone()));
            IfcSpatialCellKey cell = IfcSpatialIndex.CellFor(point, tolerance);
            if (!clusterIndex.TryGetValue(cell, out List<int>? clusterIndexes))
            {
                clusterIndexes = [];
                clusterIndex[cell] = clusterIndexes;
            }

            clusterIndexes.Add(clusterIndexValue);
            return;
        }

        double movement = IfcSpatialIndex.Distance(point, cluster.Point);
        if (movement <= 0)
            return;

        point.X = cluster.Point.X;
        point.Y = cluster.Point.Y;
        point.Z = cluster.Point.Z;

        string endpointName = isStart ? "start" : "end";
        string message = $"Snapped {endpointName} endpoint by {movement:0.###} m using tolerance {tolerance:0.###} m.";
        frame.Warnings.Add(message);
        warnings.Add(new IfcImportWarning
        {
            SourceGuid = frame.SourceGuid,
            SourceName = frame.SourceName,
            Severity = IfcImportWarningSeverity.Info,
            Category = IfcImportWarningCategory.Cleanup,
            Message = message
        });
    }

    private static EndpointCluster? FindCluster(
        AnalyticalPoint point,
        double tolerance,
        IReadOnlyList<EndpointCluster> clusters,
        Dictionary<IfcSpatialCellKey, List<int>> clusterIndex)
    {
        foreach (IfcSpatialCellKey cell in IfcSpatialIndex.NeighborCells(point, tolerance))
        {
            if (!clusterIndex.TryGetValue(cell, out List<int>? clusterIndexes))
                continue;

            foreach (int clusterIndexValue in clusterIndexes)
            {
                EndpointCluster cluster = clusters[clusterIndexValue];
                if (IfcSpatialIndex.Distance(cluster.Point, point) <= tolerance)
                    return cluster;
            }
        }

        return null;
    }

    private sealed class EndpointCluster(AnalyticalPoint point)
    {
        public AnalyticalPoint Point { get; } = point;
    }
}
