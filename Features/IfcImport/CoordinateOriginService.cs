namespace CSIModellingTools.Features.IfcImport;

public sealed class CoordinateOriginService
{
    public List<IfcImportWarning> ApplyCoordinateOriginReset(
        IfcImportResult result,
        IfcImportOptions options)
    {
        var warnings = new List<IfcImportWarning>();
        if (options.CoordinateOriginReset != IfcImportCoordinateOriginMode.ResetToFirstImportedPoint ||
            (result.Frames.Count == 0 && result.Areas.Count == 0))
        {
            result.CoordinateOffset = new IfcCoordinateOffsetInfo
            {
                Applied = false,
                Message = "Coordinate origin reset was not applied."
            };
            return warnings;
        }

        // Subtract the bounding-box minimum corner (not an arbitrary first element) so
        // the model lands cleanly in the positive octant starting at the origin. The
        // first element can be on an upper storey, which would push the base below z=0.
        AnalyticalPoint origin = ComputeMinimumCorner(result);
        foreach (AnalyticalFrameElement frame in result.Frames)
        {
            Offset(frame.StartPoint, origin);
            Offset(frame.EndPoint, origin);
        }
        foreach (AnalyticalAreaElement area in result.Areas)
        {
            foreach (AnalyticalPoint point in area.BoundaryPoints)
                Offset(point, origin);
        }

        // Keep storey levels in the same shifted frame as the geometry so ETABS stories line up.
        foreach (IfcStoreyLevel level in result.StoreyLevels)
            level.Elevation -= origin.Z;

        string message = $"Coordinate origin reset applied. Offset removed: X={origin.X:0.###} m, Y={origin.Y:0.###} m, Z={origin.Z:0.###} m.";
        result.CoordinateOffset = new IfcCoordinateOffsetInfo
        {
            Applied = true,
            X = origin.X,
            Y = origin.Y,
            Z = origin.Z,
            Message = message
        };
        result.CleanupActions.Add(message);
        warnings.Add(new IfcImportWarning
        {
            Severity = IfcImportWarningSeverity.Info,
            Category = IfcImportWarningCategory.Cleanup,
            Message = message
        });
        return warnings;
    }

    private static AnalyticalPoint ComputeMinimumCorner(IfcImportResult result)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;

        void Consider(AnalyticalPoint point)
        {
            if (point.X < minX) minX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.Z < minZ) minZ = point.Z;
        }

        foreach (AnalyticalFrameElement frame in result.Frames)
        {
            Consider(frame.StartPoint);
            Consider(frame.EndPoint);
        }

        foreach (AnalyticalAreaElement area in result.Areas)
        {
            foreach (AnalyticalPoint point in area.BoundaryPoints)
                Consider(point);
        }

        return new AnalyticalPoint
        {
            X = double.IsFinite(minX) ? minX : 0,
            Y = double.IsFinite(minY) ? minY : 0,
            Z = double.IsFinite(minZ) ? minZ : 0
        };
    }

    private static void Offset(AnalyticalPoint point, AnalyticalPoint origin)
    {
        point.X -= origin.X;
        point.Y -= origin.Y;
        point.Z -= origin.Z;
    }
}
