using System.Globalization;
using System.Text.RegularExpressions;

namespace CSIModellingTools.Features.IfcImport;

/// <summary>
/// v1 analytical conditioning for the frame model: extends beam ends to column
/// centrelines (work points) and merges near-coincident joints, so the imported
/// physical geometry becomes a connected analytical frame. Beam ends that connect to
/// nothing in the frame model (typically framing into walls/cores that are not part of
/// the frame scope) are left in place and flagged for review rather than force-snapped.
/// </summary>
public sealed class AnalyticalFrameConditioningService
{
    private const double VerticalRatio = 0.85;      // |dz|/L above this => treated as a column
    private const double HorizontalRatio = 0.30;    // |dz|/L below this => treated as a beam
    private const double CaptureMarginMetres = 0.075;
    private const double MaxColumnCaptureMetres = 1.10;
    private const double DefaultCaptureMetres = 0.30;
    private const double ZeroLengthTolerance = 0.000001;
    // A snap move is split relative to the beam's own axis: moving ALONG the axis only changes the
    // member's length (extend/trim to reach a support) and never skews it, so it is allowed
    // generously; moving ACROSS the axis rotates the member (skew) and is held to a tiny tolerance.
    // This connects beams that point at a support without distorting geometry.
    private const double MaxAxialMoveMetres = 0.600;   // extend/trim along the beam's own line
    private const double MaxPerpMoveMetres = 0.050;    // sideways move that would skew the member
    private const double GirderEndDistanceTolerance = 0.10;   // within this of a girder end => meet at end, no split
    private const double MinimumSplitSegmentLength = 0.05;
    private const double SegmentCellSize = 1.0;
    private const double WallCaptureMargin = 0.15;    // added to half-thickness when catching a beam at a wall face
    private const double WallMinCapture = 0.30;       // floor on the wall capture radius
    private const double WallZTolerance = 0.15;       // beam may sit slightly above/below the wall's z-extent
    private const double WallCellSize = 1.0;

    private static readonly Regex DimensionPattern = new(@"(\d{2,5})\s*[xX]\s*(\d{2,5})", RegexOptions.Compiled);

    public List<IfcImportWarning> ConditionFrames(
        List<AnalyticalFrameElement> frames,
        IReadOnlyList<AnalyticalAreaElement> walls,
        double mergeTolerance)
    {
        var warnings = new List<IfcImportWarning>();
        if (frames.Count == 0)
            return warnings;

        // Columns are the stiffer support, so resolve beam-to-column first; the returned
        // set lets the beam-to-girder pass leave those ends alone.
        List<ColumnLine> columns = BuildColumnLines(frames);
        HashSet<AnalyticalPoint> columnSnapped = SnapBeamEndsToColumns(frames, columns);

        // Secondary beams that frame into a girder: snap the end to the girder centreline,
        // and where it lands mid-span, insert a node by splitting the girder.
        GirderConditioningResult girder = SnapBeamEndsToGirders(frames, columnSnapped);
        if (girder.Splits.Count > 0)
            ApplyGirderSplits(frames, girder.Splits);

        // Beams that frame into a wall/core: snap the still-dangling end onto the wall's plan
        // centreline (keeping its floor elevation) so ETABS edge constraints tie it to the wall
        // shell. Precedence is column -> girder -> wall; this pass only moves ends that are still
        // unconnected after the first two, so it never overrides a resolved joint.
        int wallSnapped = SnapBeamEndsToWalls(frames, walls, mergeTolerance);

        int merged = MergeCoincidentEndpoints(frames, mergeTolerance);
        int dangling = FlagDanglingBeamEnds(frames, columns, mergeTolerance, warnings);

        warnings.Add(new IfcImportWarning
        {
            Severity = IfcImportWarningSeverity.Info,
            Category = IfcImportWarningCategory.Cleanup,
            Message = $"Frame conditioning: snapped {columnSnapped.Count} beam end(s) to columns, " +
                $"{girder.SnappedEnds} to girders (split {girder.Splits.Count} girder(s)), " +
                $"{wallSnapped} to walls, " +
                $"merged {merged} joint(s), flagged {dangling} unconnected beam end(s) for review."
        });
        return warnings;
    }

    private static int SnapBeamEndsToWalls(
        List<AnalyticalFrameElement> frames,
        IReadOnlyList<AnalyticalAreaElement> walls,
        double mergeTolerance)
    {
        if (walls.Count == 0)
            return 0;

        var segments = new List<WallSegment>();
        foreach (AnalyticalAreaElement wall in walls)
        {
            if (TryBuildWallSegment(wall, out WallSegment segment))
                segments.Add(segment);
        }

        if (segments.Count == 0)
            return 0;

        var grid = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < segments.Count; i++)
            IndexWallSegment(grid, segments[i], i);

        // A beam end is a candidate only if it is still dangling after the column/girder passes,
        // i.e. it does not already coincide with another frame endpoint. This naturally excludes
        // column- and girder-connected ends without tracking them by identity across girder splits.
        double tol = double.IsFinite(mergeTolerance) && mergeTolerance > 0 ? mergeTolerance : 0.02;
        var shared = new Dictionary<(long, long, long), int>();
        foreach (AnalyticalFrameElement frame in frames)
        {
            CountEndpoint(shared, frame.StartPoint, tol);
            CountEndpoint(shared, frame.EndPoint, tol);
        }

        int snapped = 0;
        foreach (AnalyticalFrameElement frame in frames)
        {
            if (Classify(frame) != MemberKind.Beam)
                continue;

            if (TrySnapEndToWall(frame.StartPoint, frame.EndPoint, segments, grid, shared, tol))
                snapped++;
            if (TrySnapEndToWall(frame.EndPoint, frame.StartPoint, segments, grid, shared, tol))
                snapped++;
        }

        return snapped;
    }

    private static bool TrySnapEndToWall(
        AnalyticalPoint endpoint,
        AnalyticalPoint far,
        List<WallSegment> segments,
        Dictionary<(long, long), List<int>> grid,
        Dictionary<(long, long, long), int> shared,
        double tol)
    {
        (long, long, long) nodeKey = (
            (long)Math.Round(endpoint.X / tol),
            (long)Math.Round(endpoint.Y / tol),
            (long)Math.Round(endpoint.Z / tol));
        if (shared.TryGetValue(nodeKey, out int count) && count > 1)
            return false;   // already connected to another member

        double bestDistance = double.PositiveInfinity;
        double bestX = endpoint.X, bestY = endpoint.Y;

        (long cx, long cy) = WallCell(endpoint.X, endpoint.Y);
        for (long dx = -1; dx <= 1; dx++)
        {
            for (long dy = -1; dy <= 1; dy++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy), out List<int>? bucket))
                    continue;

                foreach (int index in bucket)
                {
                    WallSegment segment = segments[index];
                    if (endpoint.Z < segment.ZMin - WallZTolerance || endpoint.Z > segment.ZMax + WallZTolerance)
                        continue;

                    double distance = PointToSegment2D(endpoint.X, endpoint.Y, segment, out double px, out double py);
                    double capture = Math.Max(segment.Thickness / 2.0 + WallCaptureMargin, WallMinCapture);
                    if (distance <= capture && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = px;
                        bestY = py;
                    }
                }
            }
        }

        // Accept only if reaching the wall is an axial extension of the beam, not a sideways skew.
        if (!double.IsFinite(bestDistance) || bestDistance <= ZeroLengthTolerance || !WithinSkewLimits(endpoint, far, bestX, bestY))
            return false;

        // Snap in plan only; the beam stays on its floor elevation.
        endpoint.X = bestX;
        endpoint.Y = bestY;
        return true;
    }

    private static bool TryBuildWallSegment(AnalyticalAreaElement wall, out WallSegment segment)
    {
        segment = default;
        List<AnalyticalPoint> points = wall.BoundaryPoints;
        if (points.Count < 3)
            return false;

        double zMin = points.Min(point => point.Z);
        double zMax = points.Max(point => point.Z);

        // Plan run = the two most-separated boundary points in plan.
        AnalyticalPoint anchor = points[0];
        AnalyticalPoint end1 = points.OrderByDescending(p => PlanDistanceSquared(p, anchor)).First();
        AnalyticalPoint end2 = points.OrderByDescending(p => PlanDistanceSquared(p, end1)).First();
        if (PlanDistanceSquared(end1, end2) <= ZeroLengthTolerance)
            return false;

        segment = new WallSegment(end1.X, end1.Y, end2.X, end2.Y, zMin, zMax, Math.Max(wall.Thickness, 0));
        return true;
    }

    private static void IndexWallSegment(Dictionary<(long, long), List<int>> grid, WallSegment segment, int index)
    {
        double length = Math.Sqrt(PlanDistanceSquared2(segment.Ax, segment.Ay, segment.Bx, segment.By));
        int steps = Math.Max(1, (int)(length / WallCellSize) + 1);
        var seen = new HashSet<(long, long)>();
        for (int k = 0; k <= steps; k++)
        {
            double t = (double)k / steps;
            (long, long) cell = WallCell(
                segment.Ax + (segment.Bx - segment.Ax) * t,
                segment.Ay + (segment.By - segment.Ay) * t);
            if (!seen.Add(cell))
                continue;

            if (!grid.TryGetValue(cell, out List<int>? bucket))
            {
                bucket = [];
                grid[cell] = bucket;
            }

            bucket.Add(index);
        }
    }

    private static double PointToSegment2D(double x, double y, WallSegment segment, out double px, out double py)
    {
        double dx = segment.Bx - segment.Ax, dy = segment.By - segment.Ay;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= ZeroLengthTolerance)
        {
            px = segment.Ax;
            py = segment.Ay;
            return Math.Sqrt(Square(x - segment.Ax) + Square(y - segment.Ay));
        }

        double t = Math.Clamp(((x - segment.Ax) * dx + (y - segment.Ay) * dy) / lengthSquared, 0, 1);
        px = segment.Ax + dx * t;
        py = segment.Ay + dy * t;
        return Math.Sqrt(Square(x - px) + Square(y - py));
    }

    private static double PlanDistanceSquared(AnalyticalPoint a, AnalyticalPoint b)
        => Square(a.X - b.X) + Square(a.Y - b.Y);

    private static double PlanDistanceSquared2(double ax, double ay, double bx, double by)
        => Square(ax - bx) + Square(ay - by);

    private static (long, long) WallCell(double x, double y)
        => ((long)Math.Floor(x / WallCellSize), (long)Math.Floor(y / WallCellSize));

    private static List<ColumnLine> BuildColumnLines(List<AnalyticalFrameElement> frames)
    {
        var columns = new List<ColumnLine>();
        foreach (AnalyticalFrameElement frame in frames)
        {
            if (Classify(frame) != MemberKind.Column)
                continue;

            double x = (frame.StartPoint.X + frame.EndPoint.X) / 2.0;
            double y = (frame.StartPoint.Y + frame.EndPoint.Y) / 2.0;
            double capture = ResolveCaptureRadius(frame);
            columns.Add(new ColumnLine(x, y, capture));
        }

        return columns;
    }

    private static HashSet<AnalyticalPoint> SnapBeamEndsToColumns(List<AnalyticalFrameElement> frames, List<ColumnLine> columns)
    {
        // AnalyticalPoint has no value-equality override, so the default set tracks
        // endpoint object identity, which is exactly what we want here.
        var snapped = new HashSet<AnalyticalPoint>();
        if (columns.Count == 0)
            return snapped;

        var grid = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < columns.Count; i++)
        {
            (long, long) cell = Cell(columns[i].X, columns[i].Y);
            if (!grid.TryGetValue(cell, out List<int>? bucket))
            {
                bucket = [];
                grid[cell] = bucket;
            }

            bucket.Add(i);
        }

        foreach (AnalyticalFrameElement frame in frames)
        {
            if (Classify(frame) != MemberKind.Beam)
                continue;

            if (TrySnapEndpoint(frame.StartPoint, frame.EndPoint, columns, grid))
                snapped.Add(frame.StartPoint);
            if (TrySnapEndpoint(frame.EndPoint, frame.StartPoint, columns, grid))
                snapped.Add(frame.EndPoint);
        }

        return snapped;
    }

    private static bool TrySnapEndpoint(AnalyticalPoint point, AnalyticalPoint far, List<ColumnLine> columns, Dictionary<(long, long), List<int>> grid)
    {
        (long cx, long cy) = Cell(point.X, point.Y);
        double bestDistance = double.PositiveInfinity;
        ColumnLine? best = null;
        for (long dx = -1; dx <= 1; dx++)
        {
            for (long dy = -1; dy <= 1; dy++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy), out List<int>? bucket))
                    continue;

                foreach (int index in bucket)
                {
                    ColumnLine column = columns[index];
                    double distance = Math.Sqrt(Square(point.X - column.X) + Square(point.Y - column.Y));
                    // Only accept a column the beam actually points at: the move to its centreline
                    // must be almost pure axial extension, not a sideways (skewing) shift.
                    if (distance <= column.Capture && distance < bestDistance && WithinSkewLimits(point, far, column.X, column.Y))
                    {
                        bestDistance = distance;
                        best = column;
                    }
                }
            }
        }

        if (best == null || bestDistance <= ZeroLengthTolerance)
            return false;

        point.X = best.Value.X;
        point.Y = best.Value.Y;
        return true;
    }

    // A move that is (almost) along the beam's own axis only extends/trims it — no skew. A move
    // across the axis rotates it. Allow generous axial reach, tiny perpendicular offset.
    private static bool WithinSkewLimits(AnalyticalPoint end, AnalyticalPoint far, double targetX, double targetY)
    {
        double ux = end.X - far.X, uy = end.Y - far.Y;
        double length = Math.Sqrt(ux * ux + uy * uy);
        if (length <= ZeroLengthTolerance)
            return false;

        ux /= length; uy /= length;
        double mx = targetX - end.X, my = targetY - end.Y;
        double axial = mx * ux + my * uy;          // + = extend beyond the current end
        double perpendicular = Math.Abs(mx * -uy + my * ux);
        return perpendicular <= MaxPerpMoveMetres && Math.Abs(axial) <= MaxAxialMoveMetres;
    }

    private static GirderConditioningResult SnapBeamEndsToGirders(
        List<AnalyticalFrameElement> frames,
        HashSet<AnalyticalPoint> columnSnapped)
    {
        var result = new GirderConditioningResult();
        List<AnalyticalFrameElement> beams = frames.Where(frame => Classify(frame) == MemberKind.Beam).ToList();
        if (beams.Count < 2)
            return result;

        // Spatial grid of beam segments (sampled along their length) for nearest-girder lookup.
        var grid = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < beams.Count; i++)
            IndexBeamSegment(grid, beams[i], i);

        foreach (AnalyticalFrameElement beam in beams)
        {
            TrySnapEndToGirder(beam, beam.StartPoint, beams, grid, columnSnapped, result);
            TrySnapEndToGirder(beam, beam.EndPoint, beams, grid, columnSnapped, result);
        }

        return result;
    }

    private static void TrySnapEndToGirder(
        AnalyticalFrameElement beam,
        AnalyticalPoint endpoint,
        List<AnalyticalFrameElement> beams,
        Dictionary<(long, long, long), List<int>> grid,
        HashSet<AnalyticalPoint> columnSnapped,
        GirderConditioningResult result)
    {
        if (columnSnapped.Contains(endpoint))
            return;

        AnalyticalFrameElement? bestGirder = null;
        double bestDistance = double.PositiveInfinity;
        double bestParameter = 0;
        AnalyticalPoint bestPoint = endpoint;

        (long cx, long cy, long cz) = SegmentCell(endpoint.X, endpoint.Y, endpoint.Z);
        for (long dx = -1; dx <= 1; dx++)
        {
            for (long dy = -1; dy <= 1; dy++)
            {
                for (long dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int>? bucket))
                        continue;

                    foreach (int index in bucket)
                    {
                        AnalyticalFrameElement girder = beams[index];
                        if (ReferenceEquals(girder, beam))
                            continue;

                        double distance = PointToSegment(endpoint, girder.StartPoint, girder.EndPoint, out double t, out AnalyticalPoint projection);
                        if (distance <= ResolveCaptureRadius(girder) && distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestGirder = girder;
                            bestParameter = t;
                            bestPoint = projection;
                        }
                    }
                }
            }
        }

        AnalyticalPoint far = ReferenceEquals(endpoint, beam.StartPoint) ? beam.EndPoint : beam.StartPoint;
        if (bestGirder == null || bestDistance <= ZeroLengthTolerance || !WithinSkewLimits(endpoint, far, bestPoint.X, bestPoint.Y))
            return;

        double girderLength = Distance3D(bestGirder.StartPoint, bestGirder.EndPoint);
        if (bestParameter * girderLength <= GirderEndDistanceTolerance)
        {
            MovePoint(endpoint, bestGirder.StartPoint);
        }
        else if ((1.0 - bestParameter) * girderLength <= GirderEndDistanceTolerance)
        {
            MovePoint(endpoint, bestGirder.EndPoint);
        }
        else
        {
            MovePoint(endpoint, bestPoint);
            if (!result.Splits.TryGetValue(bestGirder, out List<double>? cuts))
            {
                cuts = [];
                result.Splits[bestGirder] = cuts;
            }

            cuts.Add(bestParameter);
        }

        result.SnappedEnds++;
    }

    private static void ApplyGirderSplits(List<AnalyticalFrameElement> frames, Dictionary<AnalyticalFrameElement, List<double>> splits)
    {
        var rebuilt = new List<AnalyticalFrameElement>(frames.Count + splits.Count);
        foreach (AnalyticalFrameElement frame in frames)
        {
            if (!splits.TryGetValue(frame, out List<double>? cuts) || cuts.Count == 0)
            {
                rebuilt.Add(frame);
                continue;
            }

            double length = Distance3D(frame.StartPoint, frame.EndPoint);
            var clean = new List<double>();
            double previous = 0;
            foreach (double t in cuts.Where(value => value > 0 && value < 1).Distinct().OrderBy(value => value))
            {
                if (t * length < MinimumSplitSegmentLength || (1 - t) * length < MinimumSplitSegmentLength)
                    continue;
                if (clean.Count > 0 && (t - previous) * length < MinimumSplitSegmentLength)
                    continue;

                clean.Add(t);
                previous = t;
            }

            if (clean.Count == 0)
            {
                rebuilt.Add(frame);
                continue;
            }

            var bounds = new List<double> { 0 };
            bounds.AddRange(clean);
            bounds.Add(1);
            for (int k = 0; k < bounds.Count - 1; k++)
                rebuilt.Add(CloneSegment(frame, bounds[k], bounds[k + 1]));
        }

        frames.Clear();
        frames.AddRange(rebuilt);
    }

    private static AnalyticalFrameElement CloneSegment(AnalyticalFrameElement source, double t0, double t1)
    {
        var segment = new AnalyticalFrameElement
        {
            SourceGuid = source.SourceGuid,
            SourceName = source.SourceName,
            IfcType = source.IfcType,
            StartPoint = t0 <= 0 ? source.StartPoint : Lerp(source, t0),
            EndPoint = t1 >= 1 ? source.EndPoint : Lerp(source, t1),
            SectionInfo = source.SectionInfo,
            SectionName = source.SectionName,
            MaterialName = source.MaterialName,
            StoreyName = source.StoreyName,
            RecognitionMethod = source.RecognitionMethod,
            Confidence = source.Confidence
        };
        segment.Warnings.Add("Girder split at a beam intersection to create a shared analytical node.");
        return segment;
    }

    private static AnalyticalPoint Lerp(AnalyticalFrameElement frame, double t)
    {
        return new AnalyticalPoint
        {
            X = frame.StartPoint.X + (frame.EndPoint.X - frame.StartPoint.X) * t,
            Y = frame.StartPoint.Y + (frame.EndPoint.Y - frame.StartPoint.Y) * t,
            Z = frame.StartPoint.Z + (frame.EndPoint.Z - frame.StartPoint.Z) * t
        };
    }

    private static void IndexBeamSegment(Dictionary<(long, long, long), List<int>> grid, AnalyticalFrameElement beam, int index)
    {
        double length = Distance3D(beam.StartPoint, beam.EndPoint);
        int steps = Math.Max(1, (int)(length / SegmentCellSize) + 1);
        var seen = new HashSet<(long, long, long)>();
        for (int k = 0; k <= steps; k++)
        {
            double t = (double)k / steps;
            (long, long, long) cell = SegmentCell(
                beam.StartPoint.X + (beam.EndPoint.X - beam.StartPoint.X) * t,
                beam.StartPoint.Y + (beam.EndPoint.Y - beam.StartPoint.Y) * t,
                beam.StartPoint.Z + (beam.EndPoint.Z - beam.StartPoint.Z) * t);
            if (!seen.Add(cell))
                continue;

            if (!grid.TryGetValue(cell, out List<int>? bucket))
            {
                bucket = [];
                grid[cell] = bucket;
            }

            bucket.Add(index);
        }
    }

    private static double PointToSegment(AnalyticalPoint point, AnalyticalPoint a, AnalyticalPoint b, out double t, out AnalyticalPoint closest)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        double lengthSquared = dx * dx + dy * dy + dz * dz;
        if (lengthSquared <= ZeroLengthTolerance)
        {
            t = 0;
            closest = new AnalyticalPoint { X = a.X, Y = a.Y, Z = a.Z };
            return Distance3D(point, a);
        }

        t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy + (point.Z - a.Z) * dz) / lengthSquared, 0, 1);
        closest = new AnalyticalPoint { X = a.X + dx * t, Y = a.Y + dy * t, Z = a.Z + dz * t };
        return Distance3D(point, closest);
    }

    private static double Distance3D(AnalyticalPoint a, AnalyticalPoint b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static void MovePoint(AnalyticalPoint point, AnalyticalPoint target)
    {
        point.X = target.X;
        point.Y = target.Y;
        point.Z = target.Z;
    }

    private static (long, long, long) SegmentCell(double x, double y, double z)
    {
        return ((long)Math.Floor(x / SegmentCellSize), (long)Math.Floor(y / SegmentCellSize), (long)Math.Floor(z / SegmentCellSize));
    }

    private static int MergeCoincidentEndpoints(List<AnalyticalFrameElement> frames, double mergeTolerance)
    {
        if (!double.IsFinite(mergeTolerance) || mergeTolerance <= 0)
            return 0;

        var endpoints = new List<AnalyticalPoint>(frames.Count * 2);
        foreach (AnalyticalFrameElement frame in frames)
        {
            endpoints.Add(frame.StartPoint);
            endpoints.Add(frame.EndPoint);
        }

        var clusterSum = new List<(double X, double Y, double Z, int Count)>();
        var clusterOf = new int[endpoints.Count];
        var grid = new Dictionary<(long, long, long), List<int>>();

        for (int i = 0; i < endpoints.Count; i++)
        {
            AnalyticalPoint point = endpoints[i];
            int found = FindCluster(point, mergeTolerance, clusterSum, grid);
            if (found < 0)
            {
                found = clusterSum.Count;
                clusterSum.Add((point.X, point.Y, point.Z, 1));
                AddToGrid(grid, point, mergeTolerance, found);
            }
            else
            {
                (double X, double Y, double Z, int Count) c = clusterSum[found];
                clusterSum[found] = (c.X + point.X, c.Y + point.Y, c.Z + point.Z, c.Count + 1);
            }

            clusterOf[i] = found;
        }

        int merged = 0;
        for (int i = 0; i < endpoints.Count; i++)
        {
            (double X, double Y, double Z, int Count) c = clusterSum[clusterOf[i]];
            if (c.Count <= 1)
                continue;

            double x = c.X / c.Count;
            double y = c.Y / c.Count;
            double z = c.Z / c.Count;
            AnalyticalPoint point = endpoints[i];
            if (Math.Abs(point.X - x) > ZeroLengthTolerance || Math.Abs(point.Y - y) > ZeroLengthTolerance || Math.Abs(point.Z - z) > ZeroLengthTolerance)
            {
                point.X = x;
                point.Y = y;
                point.Z = z;
                merged++;
            }
        }

        return merged;
    }

    private static int FindCluster(
        AnalyticalPoint point,
        double tolerance,
        List<(double X, double Y, double Z, int Count)> clusterSum,
        Dictionary<(long, long, long), List<int>> grid)
    {
        long cx = (long)Math.Floor(point.X / tolerance);
        long cy = (long)Math.Floor(point.Y / tolerance);
        long cz = (long)Math.Floor(point.Z / tolerance);
        for (long dx = -1; dx <= 1; dx++)
        {
            for (long dy = -1; dy <= 1; dy++)
            {
                for (long dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int>? bucket))
                        continue;

                    foreach (int index in bucket)
                    {
                        (double X, double Y, double Z, int Count) c = clusterSum[index];
                        double rx = c.X / c.Count;
                        double ry = c.Y / c.Count;
                        double rz = c.Z / c.Count;
                        if (Math.Sqrt(Square(point.X - rx) + Square(point.Y - ry) + Square(point.Z - rz)) <= tolerance)
                            return index;
                    }
                }
            }
        }

        return -1;
    }

    private static void AddToGrid(Dictionary<(long, long, long), List<int>> grid, AnalyticalPoint point, double tolerance, int clusterIndex)
    {
        (long, long, long) cell = (
            (long)Math.Floor(point.X / tolerance),
            (long)Math.Floor(point.Y / tolerance),
            (long)Math.Floor(point.Z / tolerance));
        if (!grid.TryGetValue(cell, out List<int>? bucket))
        {
            bucket = [];
            grid[cell] = bucket;
        }

        bucket.Add(clusterIndex);
    }

    private static int FlagDanglingBeamEnds(
        List<AnalyticalFrameElement> frames,
        List<ColumnLine> columns,
        double mergeTolerance,
        List<IfcImportWarning> warnings)
    {
        // An endpoint is "connected" if it coincides with another member's endpoint.
        var shared = new Dictionary<(long, long, long), int>();
        double tol = double.IsFinite(mergeTolerance) && mergeTolerance > 0 ? mergeTolerance : 0.02;
        foreach (AnalyticalFrameElement frame in frames)
        {
            CountEndpoint(shared, frame.StartPoint, tol);
            CountEndpoint(shared, frame.EndPoint, tol);
        }

        int dangling = 0;
        foreach (AnalyticalFrameElement frame in frames)
        {
            if (Classify(frame) != MemberKind.Beam)
                continue;

            foreach (AnalyticalPoint point in new[] { frame.StartPoint, frame.EndPoint })
            {
                (long, long, long) key = (
                    (long)Math.Round(point.X / tol),
                    (long)Math.Round(point.Y / tol),
                    (long)Math.Round(point.Z / tol));
                if (shared.TryGetValue(key, out int count) && count > 1)
                    continue;

                dangling++;
                string message = "Beam end is not connected to any other frame member (likely frames into a wall/core outside the frame scope).";
                frame.Warnings.Add(message);
                warnings.Add(new IfcImportWarning
                {
                    SourceGuid = frame.SourceGuid,
                    SourceName = frame.SourceName,
                    Severity = IfcImportWarningSeverity.Warning,
                    Category = IfcImportWarningCategory.Connectivity,
                    Message = message
                });
            }
        }

        return dangling;
    }

    private static void CountEndpoint(Dictionary<(long, long, long), int> shared, AnalyticalPoint point, double tol)
    {
        (long, long, long) key = (
            (long)Math.Round(point.X / tol),
            (long)Math.Round(point.Y / tol),
            (long)Math.Round(point.Z / tol));
        shared[key] = shared.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    private static MemberKind Classify(AnalyticalFrameElement frame)
    {
        double dx = frame.EndPoint.X - frame.StartPoint.X;
        double dy = frame.EndPoint.Y - frame.StartPoint.Y;
        double dz = frame.EndPoint.Z - frame.StartPoint.Z;
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length <= ZeroLengthTolerance)
            return MemberKind.Other;

        double verticality = Math.Abs(dz) / length;
        if (verticality > VerticalRatio)
            return MemberKind.Column;
        if (verticality < HorizontalRatio)
            return MemberKind.Beam;
        return MemberKind.Other;
    }

    private static double ResolveCaptureRadius(AnalyticalFrameElement column)
    {
        double largestDimension = Math.Max(column.SectionInfo.Width, column.SectionInfo.Depth);
        if (largestDimension <= 0)
            largestDimension = LargestDimensionFromName(column);

        if (largestDimension <= 0)
            return DefaultCaptureMetres;

        return Math.Min(largestDimension / 2.0 + CaptureMarginMetres, MaxColumnCaptureMetres);
    }

    private static double LargestDimensionFromName(AnalyticalFrameElement column)
    {
        foreach (string text in new[] { column.SectionName, column.SectionInfo.SectionName, column.SourceName })
        {
            Match match = DimensionPattern.Match(text ?? "");
            if (!match.Success)
                continue;

            if (double.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double a) &&
                double.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double b))
            {
                return Math.Max(a, b) / 1000.0;
            }
        }

        return 0;
    }

    private static (long, long) Cell(double x, double y)
    {
        return ((long)Math.Floor(x / MaxColumnCaptureMetres), (long)Math.Floor(y / MaxColumnCaptureMetres));
    }

    private static double Square(double value) => value * value;

    private enum MemberKind
    {
        Column,
        Beam,
        Other
    }

    private readonly record struct ColumnLine(double X, double Y, double Capture);

    // A wall reduced to its plan centreline run (A->B), vertical z-extent, and thickness.
    private readonly record struct WallSegment(double Ax, double Ay, double Bx, double By, double ZMin, double ZMax, double Thickness);

    private sealed class GirderConditioningResult
    {
        public int SnappedEnds { get; set; }

        // Keyed by frame identity (AnalyticalFrameElement has no value-equality override),
        // mapping each girder to the parameters (0..1) where it must be split.
        public Dictionary<AnalyticalFrameElement, List<double>> Splits { get; } = new();
    }
}
