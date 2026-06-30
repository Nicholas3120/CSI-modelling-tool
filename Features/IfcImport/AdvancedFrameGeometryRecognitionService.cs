using System.Globalization;
using Xbim.Common.Geometry;
using Xbim.Ifc4.Interfaces;

namespace CSIModellingTools.Features.IfcImport;

public sealed class AdvancedFrameGeometryRecognitionService
{
    private const int MaximumVertexCount = 2000;
    private const int MinimumVertexCount = 8;
    private const double DuplicateVertexTolerance = 0.001;
    private const double MinimumInferredLength = 0.300;
    private const double MinimumLengthToSectionRatio = 2.5;
    private const double MinimumPrincipalEigenRatio = 4.0;
    private const double MaximumSectionAspectRatio = 20.0;
    private const double EndRegionFraction = 0.12;
    private const string InferredGeometryWarning = "Element imported using inferred geometry. Please verify centreline and section.";

    public AdvancedFrameGeometryRecognitionResult TryInferFrame(
        IIfcProduct product,
        string ifcType,
        double lengthFactor)
    {
        var result = new AdvancedFrameGeometryRecognitionResult();
        var localWarnings = result.Warnings;
        XbimMatrix3D placement = ResolvePlacement(product.ObjectPlacement, localWarnings, product, ifcType);
        List<AnalyticalPoint> vertices = CollectBodyVertices(product, placement, lengthFactor, localWarnings, ifcType);
        vertices = Deduplicate(vertices);

        if (vertices.Count < MinimumVertexCount)
        {
            result.SkipReason = "Advanced geometry recognition skipped: not enough Brep/mesh vertices.";
            return result;
        }

        if (vertices.Count > MaximumVertexCount)
        {
            result.SkipReason = "Advanced geometry recognition skipped: mesh is too dense or complex.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, result.SkipReason));
            return result;
        }

        PrincipalAxisResult principalAxis = EstimatePrincipalAxis(vertices);
        if (!principalAxis.IsValid || principalAxis.SecondEigenValue <= 0 || principalAxis.FirstEigenValue / principalAxis.SecondEigenValue < MinimumPrincipalEigenRatio)
        {
            result.SkipReason = "Advanced geometry recognition skipped: ambiguous principal direction.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, result.SkipReason));
            return result;
        }

        ProjectionExtents extents = Project(vertices, principalAxis.Axis);
        double inferredLength = extents.Max - extents.Min;
        if (inferredLength < MinimumInferredLength)
        {
            result.SkipReason = "Advanced geometry recognition skipped: inferred member length is very short.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, result.SkipReason));
            return result;
        }

        List<AnalyticalPoint> startRegion = EndRegion(vertices, principalAxis.Axis, extents.Min, inferredLength, true);
        List<AnalyticalPoint> endRegion = EndRegion(vertices, principalAxis.Axis, extents.Max, inferredLength, false);
        if (startRegion.Count == 0 || endRegion.Count == 0)
        {
            result.SkipReason = "Advanced geometry recognition skipped: could not isolate end regions.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, result.SkipReason));
            return result;
        }

        AnalyticalPoint start = Centroid(startRegion);
        AnalyticalPoint end = Centroid(endRegion);
        double centrelineLength = Distance(start, end);
        if (centrelineLength < MinimumInferredLength)
        {
            result.SkipReason = "Advanced geometry recognition skipped: inferred centreline is very short.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, result.SkipReason));
            return result;
        }

        SectionEstimate sectionEstimate = EstimateSection(vertices, principalAxis.Axis);
        if (!sectionEstimate.IsValid)
        {
            result.SkipReason = "Advanced geometry recognition skipped: section dimensions could not be estimated safely.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Section, result.SkipReason));
            return result;
        }

        double maxSection = Math.Max(sectionEstimate.Width, sectionEstimate.Depth);
        double minSection = Math.Min(sectionEstimate.Width, sectionEstimate.Depth);
        if (centrelineLength / maxSection < MinimumLengthToSectionRatio)
        {
            result.SkipReason = "Advanced geometry recognition skipped: inferred member aspect ratio is too low for a beam/column.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, result.SkipReason));
            return result;
        }

        if (maxSection / minSection > MaximumSectionAspectRatio)
        {
            result.SkipReason = "Advanced geometry recognition skipped: inferred section aspect ratio is extreme.";
            localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Section, result.SkipReason));
            return result;
        }

        IfcRecognitionConfidence confidence = principalAxis.FirstEigenValue / principalAxis.SecondEigenValue >= 10.0 &&
            centrelineLength / maxSection >= 4.0
            ? IfcRecognitionConfidence.Medium
            : IfcRecognitionConfidence.Low;

        var frame = new AnalyticalFrameElement
        {
            SourceGuid = GetGuid(product),
            SourceName = GetName(product),
            IfcType = ifcType,
            StartPoint = start,
            EndPoint = end,
            RecognitionMethod = IfcRecognitionMethod.Inferred,
            Confidence = confidence,
            SectionInfo = new SectionInfo
            {
                ShapeType = IfcSectionShapeType.Rectangle,
                Width = sectionEstimate.Width,
                Depth = sectionEstimate.Depth,
                SectionName = BuildInferredSectionName(ifcType, sectionEstimate.Width, sectionEstimate.Depth),
                OriginalIfcProfileType = "InferredFromBrepOrMesh"
            }
        };
        frame.Warnings.Add(InferredGeometryWarning);
        localWarnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, InferredGeometryWarning));

        result.Frame = frame;
        return result;
    }

    /// <summary>
    /// Recovers a prismatic member from tessellated/mesh-only geometry (e.g. columns and
    /// beams that an authoring tool exported as a triangle mesh instead of an extrusion).
    /// Unlike <see cref="TryInferFrame"/>, this does not require a slender shape: it reads
    /// the axis-aligned bounding box, takes the member axis from the IFC type (column =
    /// vertical, beam = longest horizontal), and reports the section as the perpendicular
    /// extents. Results are always low-confidence and flagged for review.
    /// </summary>
    public AdvancedFrameGeometryRecognitionResult TryRecoverPrismaticMember(
        IIfcProduct product,
        string ifcType,
        double lengthFactor)
    {
        var result = new AdvancedFrameGeometryRecognitionResult();
        XbimMatrix3D placement = ResolvePlacement(product.ObjectPlacement, result.Warnings, product, ifcType);
        List<AnalyticalPoint> vertices = Deduplicate(CollectBodyVertices(product, placement, lengthFactor, result.Warnings, ifcType));
        if (vertices.Count < 4)
        {
            result.SkipReason = "Mesh recovery skipped: not enough mesh vertices.";
            return result;
        }

        if (vertices.Count > MaximumVertexCount)
        {
            result.SkipReason = "Mesh recovery skipped: mesh is too dense or complex.";
            return result;
        }

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        foreach (AnalyticalPoint vertex in vertices)
        {
            minX = Math.Min(minX, vertex.X); maxX = Math.Max(maxX, vertex.X);
            minY = Math.Min(minY, vertex.Y); maxY = Math.Max(maxY, vertex.Y);
            minZ = Math.Min(minZ, vertex.Z); maxZ = Math.Max(maxZ, vertex.Z);
        }

        double extentX = maxX - minX, extentY = maxY - minY, extentZ = maxZ - minZ;
        int axis = ifcType == "IfcColumn"
            ? 2
            : ifcType == "IfcBeam"
                ? (extentX >= extentY ? 0 : 1)
                : LongestAxis(extentX, extentY, extentZ);

        double length = axis == 0 ? extentX : axis == 1 ? extentY : extentZ;
        if (length < MinimumInferredLength)
        {
            result.SkipReason = "Mesh recovery skipped: recovered member length is very short.";
            return result;
        }

        double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0, cz = (minZ + maxZ) / 2.0;
        AnalyticalPoint start, end;
        double sectionWidth, sectionDepth;
        switch (axis)
        {
            case 0:
                start = new AnalyticalPoint { X = minX, Y = cy, Z = cz };
                end = new AnalyticalPoint { X = maxX, Y = cy, Z = cz };
                sectionWidth = extentY; sectionDepth = extentZ;
                break;
            case 1:
                start = new AnalyticalPoint { X = cx, Y = minY, Z = cz };
                end = new AnalyticalPoint { X = cx, Y = maxY, Z = cz };
                sectionWidth = extentX; sectionDepth = extentZ;
                break;
            default:
                start = new AnalyticalPoint { X = cx, Y = cy, Z = minZ };
                end = new AnalyticalPoint { X = cx, Y = cy, Z = maxZ };
                sectionWidth = extentX; sectionDepth = extentY;
                break;
        }

        if (sectionWidth <= 0 || sectionDepth <= 0)
        {
            result.SkipReason = "Mesh recovery skipped: section dimensions could not be measured.";
            return result;
        }

        var frame = new AnalyticalFrameElement
        {
            SourceGuid = GetGuid(product),
            SourceName = GetName(product),
            IfcType = ifcType,
            StartPoint = start,
            EndPoint = end,
            // Medium (not Low): a prismatic member read from a clean mesh bounding box is
            // reliable enough to export under the default "medium confidence" setting,
            // while still carrying a verify-me warning.
            RecognitionMethod = IfcRecognitionMethod.Inferred,
            Confidence = IfcRecognitionConfidence.Medium,
            SectionInfo = new SectionInfo
            {
                ShapeType = IfcSectionShapeType.Rectangle,
                Width = sectionWidth,
                Depth = sectionDepth,
                SectionName = BuildInferredSectionName(ifcType, sectionWidth, sectionDepth),
                OriginalIfcProfileType = "RecoveredFromMesh"
            }
        };
        string message = "Element recovered from mesh geometry (no extrusion/axis representation). Verify centreline and section.";
        frame.Warnings.Add(message);
        result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, message));
        result.Frame = frame;
        return result;
    }

    private static int LongestAxis(double extentX, double extentY, double extentZ)
    {
        if (extentX >= extentY && extentX >= extentZ)
            return 0;
        return extentY >= extentZ ? 1 : 2;
    }

    private static List<AnalyticalPoint> CollectBodyVertices(
        IIfcProduct product,
        XbimMatrix3D placement,
        double lengthFactor,
        List<IfcImportWarning> warnings,
        string ifcType)
    {
        var vertices = new List<AnalyticalPoint>();
        IEnumerable<IIfcRepresentationItem> items = product.Representation?.Representations
            .Where(representation =>
                IsRepresentation(representation, "Body") ||
                IsRepresentationType(representation, "Brep") ||
                IsRepresentationType(representation, "SurfaceModel") ||
                IsRepresentationType(representation, "Tessellation"))
            .SelectMany(representation => representation.Items) ?? [];

        foreach (IIfcRepresentationItem item in items)
            CollectVertices(item, placement, lengthFactor, vertices, warnings, product, ifcType);

        return vertices;
    }

    private static void CollectVertices(
        IIfcRepresentationItem item,
        XbimMatrix3D placement,
        double lengthFactor,
        List<AnalyticalPoint> vertices,
        List<IfcImportWarning> warnings,
        IIfcProduct product,
        string ifcType)
    {
        switch (item)
        {
            case IIfcManifoldSolidBrep brep:
                CollectConnectedFaceSetVertices(brep.Outer, placement, lengthFactor, vertices);
                break;
            case IIfcFaceBasedSurfaceModel faceModel:
                foreach (IIfcConnectedFaceSet faceSet in faceModel.FbsmFaces)
                    CollectConnectedFaceSetVertices(faceSet, placement, lengthFactor, vertices);
                break;
            case IIfcShellBasedSurfaceModel shellModel:
                foreach (IIfcShell shell in shellModel.SbsmBoundary)
                {
                    if (shell is IIfcConnectedFaceSet faceSet)
                        CollectConnectedFaceSetVertices(faceSet, placement, lengthFactor, vertices);
                }
                break;
            case IIfcTessellatedFaceSet tessellatedFaceSet:
                CollectPointListVertices(tessellatedFaceSet.Coordinates, placement, lengthFactor, vertices);
                break;
            case IIfcMappedItem mappedItem:
                XbimMatrix3D mappedPlacement = ResolveMappedItemPlacement(mappedItem, placement);
                foreach (IIfcRepresentationItem mappedRepresentationItem in mappedItem.MappingSource.MappedRepresentation.Items)
                    CollectVertices(mappedRepresentationItem, mappedPlacement, lengthFactor, vertices, warnings, product, ifcType);
                break;
            default:
                warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Info, IfcImportWarningCategory.Unsupported, $"Advanced recognition ignored unsupported representation item '{item.GetType().Name}'."));
                break;
        }
    }

    private static void CollectConnectedFaceSetVertices(
        IIfcConnectedFaceSet faceSet,
        XbimMatrix3D placement,
        double lengthFactor,
        List<AnalyticalPoint> vertices)
    {
        foreach (IIfcFace face in faceSet.CfsFaces)
        {
            foreach (IIfcFaceBound bound in face.Bounds)
            {
                if (bound.Bound is not IIfcPolyLoop loop)
                    continue;

                foreach (IIfcCartesianPoint point in loop.Polygon)
                    vertices.Add(TransformPoint(placement, point.X, point.Y, point.Z, lengthFactor));
            }
        }
    }

    private static void CollectPointListVertices(
        IIfcCartesianPointList3D pointList,
        XbimMatrix3D placement,
        double lengthFactor,
        List<AnalyticalPoint> vertices)
    {
        // CoordList items are value-type collections (ItemSet<IfcLengthMeasure>), which do
        // not covary to IEnumerable<object>; iterate non-generically and box via Cast.
        foreach (System.Collections.IEnumerable coordinate in pointList.CoordList)
        {
            double[] values = coordinate.Cast<object>().Select(ToDouble).ToArray();
            if (values.Length >= 3)
                vertices.Add(TransformPoint(placement, values[0], values[1], values[2], lengthFactor));
        }
    }

    private static List<AnalyticalPoint> Deduplicate(IEnumerable<AnalyticalPoint> vertices)
    {
        var unique = new List<AnalyticalPoint>();
        foreach (AnalyticalPoint vertex in vertices)
        {
            if (!unique.Any(existing => Distance(existing, vertex) <= DuplicateVertexTolerance))
                unique.Add(vertex);
        }

        return unique;
    }

    private static PrincipalAxisResult EstimatePrincipalAxis(IReadOnlyList<AnalyticalPoint> points)
    {
        AnalyticalPoint centroid = Centroid(points);
        double[,] covariance = new double[3, 3];
        foreach (AnalyticalPoint point in points)
        {
            Vector3 delta = new(point.X - centroid.X, point.Y - centroid.Y, point.Z - centroid.Z);
            covariance[0, 0] += delta.X * delta.X;
            covariance[0, 1] += delta.X * delta.Y;
            covariance[0, 2] += delta.X * delta.Z;
            covariance[1, 0] += delta.Y * delta.X;
            covariance[1, 1] += delta.Y * delta.Y;
            covariance[1, 2] += delta.Y * delta.Z;
            covariance[2, 0] += delta.Z * delta.X;
            covariance[2, 1] += delta.Z * delta.Y;
            covariance[2, 2] += delta.Z * delta.Z;
        }

        double scale = Math.Max(points.Count - 1, 1);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
                covariance[row, column] /= scale;
        }

        Vector3 firstAxis = PowerIteration(covariance);
        double firstEigenValue = Rayleigh(covariance, firstAxis);
        if (firstAxis.Length <= double.Epsilon || firstEigenValue <= double.Epsilon)
            return new PrincipalAxisResult(false, firstAxis, 0, 0);

        double[,] deflated = Deflate(covariance, firstAxis, firstEigenValue);
        Vector3 secondAxis = PowerIteration(deflated);
        double secondEigenValue = Math.Max(Rayleigh(deflated, secondAxis), 0);
        return new PrincipalAxisResult(true, firstAxis.Normalize(), firstEigenValue, secondEigenValue);
    }

    private static Vector3 PowerIteration(double[,] matrix)
    {
        Vector3 vector = new(1, 0.7, 0.3);
        for (int i = 0; i < 32; i++)
        {
            vector = Multiply(matrix, vector);
            if (vector.Length <= double.Epsilon)
                return new Vector3(0, 0, 0);
            vector = vector.Normalize();
        }

        return vector;
    }

    private static double Rayleigh(double[,] matrix, Vector3 vector)
    {
        Vector3 multiplied = Multiply(matrix, vector);
        return Dot(vector, multiplied);
    }

    private static double[,] Deflate(double[,] matrix, Vector3 axis, double eigenValue)
    {
        double[,] result = new double[3, 3];
        double[] values = [axis.X, axis.Y, axis.Z];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
                result[row, column] = matrix[row, column] - eigenValue * values[row] * values[column];
        }

        return result;
    }

    private static Vector3 Multiply(double[,] matrix, Vector3 vector)
    {
        return new Vector3(
            matrix[0, 0] * vector.X + matrix[0, 1] * vector.Y + matrix[0, 2] * vector.Z,
            matrix[1, 0] * vector.X + matrix[1, 1] * vector.Y + matrix[1, 2] * vector.Z,
            matrix[2, 0] * vector.X + matrix[2, 1] * vector.Y + matrix[2, 2] * vector.Z);
    }

    private static ProjectionExtents Project(IReadOnlyList<AnalyticalPoint> points, Vector3 axis)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (AnalyticalPoint point in points)
        {
            double projection = Dot(ToVector(point), axis);
            min = Math.Min(min, projection);
            max = Math.Max(max, projection);
        }

        return new ProjectionExtents(min, max);
    }

    private static List<AnalyticalPoint> EndRegion(
        IReadOnlyList<AnalyticalPoint> points,
        Vector3 axis,
        double projectionLimit,
        double length,
        bool start)
    {
        double threshold = Math.Max(length * EndRegionFraction, MinimumInferredLength / 2.0);
        return points
            .Where(point =>
            {
                double projection = Dot(ToVector(point), axis);
                return start
                    ? projection <= projectionLimit + threshold
                    : projection >= projectionLimit - threshold;
            })
            .ToList();
    }

    private static SectionEstimate EstimateSection(IReadOnlyList<AnalyticalPoint> points, Vector3 axis)
    {
        Vector3 reference = Math.Abs(axis.Z) < 0.9 ? new Vector3(0, 0, 1) : new Vector3(0, 1, 0);
        Vector3 u = Cross(axis, reference).Normalize();
        Vector3 v = Cross(axis, u).Normalize();
        if (u.Length <= double.Epsilon || v.Length <= double.Epsilon)
            return new SectionEstimate(false, 0, 0);

        double minU = double.PositiveInfinity;
        double maxU = double.NegativeInfinity;
        double minV = double.PositiveInfinity;
        double maxV = double.NegativeInfinity;
        foreach (AnalyticalPoint point in points)
        {
            Vector3 p = ToVector(point);
            double pu = Dot(p, u);
            double pv = Dot(p, v);
            minU = Math.Min(minU, pu);
            maxU = Math.Max(maxU, pu);
            minV = Math.Min(minV, pv);
            maxV = Math.Max(maxV, pv);
        }

        double width = maxU - minU;
        double depth = maxV - minV;
        return new SectionEstimate(
            double.IsFinite(width) && double.IsFinite(depth) && width > 0 && depth > 0,
            width,
            depth);
    }

    private static XbimMatrix3D ResolvePlacement(
        IIfcObjectPlacement? placement,
        List<IfcImportWarning> warnings,
        IIfcProduct product,
        string ifcType)
    {
        if (placement == null)
        {
            warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Placement, "IFC object has no placement; local coordinates were treated as global."));
            return new XbimMatrix3D();
        }

        if (placement is not IIfcLocalPlacement localPlacement)
        {
            warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Placement, $"Unsupported placement type '{placement.GetType().Name}'; local coordinates were treated as global."));
            return new XbimMatrix3D();
        }

        XbimMatrix3D relative = localPlacement.RelativePlacement == null
            ? new XbimMatrix3D()
            : Xbim.Ifc.IIfcAxis2PlacementExtensions.ToMatrix3D(localPlacement.RelativePlacement);
        if (localPlacement.PlacementRelTo == null)
            return relative;

        XbimMatrix3D parent = ResolvePlacement(localPlacement.PlacementRelTo, warnings, product, ifcType);
        return XbimMatrix3D.Multiply(relative, parent);
    }

    private static AnalyticalPoint TransformPoint(XbimMatrix3D matrix, double x, double y, double z, double lengthFactor)
    {
        XbimPoint3D transformed = matrix.Transform(new XbimPoint3D(x, y, z));
        return new AnalyticalPoint
        {
            X = transformed.X * lengthFactor,
            Y = transformed.Y * lengthFactor,
            Z = transformed.Z * lengthFactor
        };
    }

    private static XbimMatrix3D ResolveMappedItemPlacement(IIfcMappedItem mappedItem, XbimMatrix3D parentPlacement)
    {
        XbimMatrix3D mappingOrigin = mappedItem.MappingSource.MappingOrigin == null
            ? new XbimMatrix3D()
            : Xbim.Ifc.IIfcAxis2PlacementExtensions.ToMatrix3D(mappedItem.MappingSource.MappingOrigin);
        XbimMatrix3D mappingTarget = ToTransformMatrix(mappedItem.MappingTarget);
        return XbimMatrix3D.Multiply(XbimMatrix3D.Multiply(mappingOrigin, mappingTarget), parentPlacement);
    }

    private static XbimMatrix3D ToTransformMatrix(IIfcCartesianTransformationOperator transformation)
    {
        double scale = transformation.Scale.HasValue ? ToDouble(transformation.Scale.Value) : 1.0;
        Vector3 axis1 = DirectionOr(transformation.Axis1, new Vector3(1, 0, 0));
        Vector3 axis2 = DirectionOr(transformation.Axis2, new Vector3(0, 1, 0));
        Vector3 axis3 = transformation is IIfcCartesianTransformationOperator3D transformation3D
            ? DirectionOr(transformation3D.Axis3, Cross(axis1, axis2).Normalize())
            : new Vector3(0, 0, 1);

        return new XbimMatrix3D(
            axis1.X * scale, axis1.Y * scale, axis1.Z * scale, 0,
            axis2.X * scale, axis2.Y * scale, axis2.Z * scale, 0,
            axis3.X * scale, axis3.Y * scale, axis3.Z * scale, 0,
            ToDouble(transformation.LocalOrigin.X), ToDouble(transformation.LocalOrigin.Y), ToDouble(transformation.LocalOrigin.Z), 1);
    }

    private static Vector3 DirectionOr(IIfcDirection? direction, Vector3 fallback)
    {
        if (direction == null)
            return fallback;

        Vector3 value = new Vector3(ToDouble(direction.X), ToDouble(direction.Y), ToDouble(direction.Z)).Normalize();
        return value.Length <= double.Epsilon ? fallback : value;
    }

    private static AnalyticalPoint Centroid(IReadOnlyList<AnalyticalPoint> points)
    {
        return new AnalyticalPoint
        {
            X = points.Average(point => point.X),
            Y = points.Average(point => point.Y),
            Z = points.Average(point => point.Z)
        };
    }

    private static Vector3 ToVector(AnalyticalPoint point)
    {
        return new Vector3(point.X, point.Y, point.Z);
    }

    private static Vector3 Cross(Vector3 first, Vector3 second)
    {
        return new Vector3(
            first.Y * second.Z - first.Z * second.Y,
            first.Z * second.X - first.X * second.Z,
            first.X * second.Y - first.Y * second.X);
    }

    private static double Dot(Vector3 first, Vector3 second)
    {
        return first.X * second.X + first.Y * second.Y + first.Z * second.Z;
    }

    private static double Distance(AnalyticalPoint first, AnalyticalPoint second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static string BuildInferredSectionName(string ifcType, double widthMetres, double depthMetres)
    {
        string prefix = ifcType == "IfcColumn" ? "C_INF" : "B_INF";
        return $"{prefix}_{ToMillimetreName(widthMetres)}x{ToMillimetreName(depthMetres)}";
    }

    private static string ToMillimetreName(double metres)
    {
        return Math.Round(metres * 1000.0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
    }

    private static bool IsRepresentation(IIfcRepresentation representation, string identifier)
    {
        return string.Equals(representation.RepresentationIdentifier?.ToString(), identifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRepresentationType(IIfcRepresentation representation, string representationType)
    {
        return string.Equals(representation.RepresentationType?.ToString(), representationType, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGuid(IIfcRoot root)
    {
        return root.GlobalId.ToString();
    }

    private static string GetName(IIfcRoot root)
    {
        return root.Name?.ToString() ?? "";
    }

    private static double ToDouble(object value)
    {
        return IfcMeasureValueConverter.ToDouble(value);
    }

    private static IfcImportWarning BuildWarning(
        IIfcProduct product,
        string ifcType,
        IfcImportWarningSeverity severity,
        IfcImportWarningCategory category,
        string message)
    {
        return new IfcImportWarning
        {
            SourceGuid = GetGuid(product),
            SourceName = GetName(product),
            Severity = severity,
            Category = category,
            Message = message
        };
    }

    private readonly record struct PrincipalAxisResult(bool IsValid, Vector3 Axis, double FirstEigenValue, double SecondEigenValue);
    private readonly record struct ProjectionExtents(double Min, double Max);
    private readonly record struct SectionEstimate(bool IsValid, double Width, double Depth);

    private readonly record struct Vector3(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vector3 Normalize()
        {
            double length = Length;
            return length <= double.Epsilon ? this : new Vector3(X / length, Y / length, Z / length);
        }
    }
}

public sealed class AdvancedFrameGeometryRecognitionResult
{
    public AnalyticalFrameElement? Frame { get; set; }
    public List<IfcImportWarning> Warnings { get; } = [];
    public string SkipReason { get; set; } = "";
}
