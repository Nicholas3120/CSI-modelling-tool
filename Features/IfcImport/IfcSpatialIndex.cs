namespace CSIModellingTools.Features.IfcImport;

internal readonly record struct IfcSpatialCellKey(long X, long Y, long Z);

internal static class IfcSpatialIndex
{
    public static IfcSpatialCellKey CellFor(AnalyticalPoint point, double cellSize)
    {
        return new IfcSpatialCellKey(
            ToCell(point.X, cellSize),
            ToCell(point.Y, cellSize),
            ToCell(point.Z, cellSize));
    }

    public static IEnumerable<IfcSpatialCellKey> NeighborCells(AnalyticalPoint point, double cellSize)
    {
        IfcSpatialCellKey centre = CellFor(point, cellSize);
        for (long dx = -1; dx <= 1; dx++)
        {
            for (long dy = -1; dy <= 1; dy++)
            {
                for (long dz = -1; dz <= 1; dz++)
                    yield return new IfcSpatialCellKey(centre.X + dx, centre.Y + dy, centre.Z + dz);
            }
        }
    }

    public static double Distance(AnalyticalPoint first, AnalyticalPoint second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static long PairKey(int firstIndex, int secondIndex)
    {
        int first = Math.Min(firstIndex, secondIndex);
        int second = Math.Max(firstIndex, secondIndex);
        return ((long)first << 32) | (uint)second;
    }

    private static long ToCell(double value, double cellSize)
    {
        return (long)Math.Floor(value / cellSize);
    }
}
