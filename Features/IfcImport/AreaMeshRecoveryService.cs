using Xbim.Common.Geometry;
using Xbim.Ifc4.Interfaces;

namespace CSIModellingTools.Features.IfcImport;

/// <summary>
/// Recovers analytical area surfaces (walls and slabs) from tessellated / mesh-only IFC
/// geometry, i.e. products that an authoring tool exported as a triangle mesh instead of an
/// <c>IfcExtrudedAreaSolid</c>. These are silently lost by <see cref="AreaRecognitionService"/>
/// because it only reads swept solids. In this project ~39% of the walls are mesh-only, so
/// without this path they never reach ETABS.
///
/// A wall is recovered as its vertical mid-surface: the plan footprint's run axis is found by a
/// 2-D principal-axis fit, the perpendicular extent is the thickness, and the surface spans the
/// full mesh height. A slab is recovered as the convex hull of its plan outline at mid-thickness.
/// Results are flagged for review; walls are Medium confidence (a clean box is reliable), slabs
/// are Low confidence (the hull over-approximates non-convex outlines).
/// </summary>
public sealed class AreaMeshRecoveryService
{
    private const int MaximumVertexCount = 4000;
    private const int MaximumBoundaryPointCount = 64;
    private const double DuplicateVertexTolerance = 0.001;
    private const double MinimumWallHeight = 0.300;
    private const double MinimumWallRun = 0.200;
    private const double MinimumSlabSpan = 0.200;
    private const string UnknownMaterial = "UNKNOWN_MATERIAL";

    public AreaRecognitionResult TryRecoverArea(IIfcProduct product, string ifcType, double lengthFactor)
    {
        var result = new AreaRecognitionResult();
        XbimMatrix3D placement = ResolvePlacement(product.ObjectPlacement, result.Warnings, product, ifcType);
        List<AnalyticalPoint> vertices = Deduplicate(CollectBodyVertices(product, placement, lengthFactor));

        if (vertices.Count < 4)
        {
            result.SkipReason = "Mesh area recovery skipped: not enough mesh vertices.";
            return result;
        }

        if (vertices.Count > MaximumVertexCount)
        {
            result.SkipReason = "Mesh area recovery skipped: mesh is too dense or complex.";
            return result;
        }

        return string.Equals(ifcType, "IfcWall", StringComparison.OrdinalIgnoreCase)
            ? RecoverWall(product, ifcType, vertices, result)
            : RecoverSlab(product, ifcType, vertices, result);
    }

    private AreaRecognitionResult RecoverWall(IIfcProduct product, string ifcType, List<AnalyticalPoint> vertices, AreaRecognitionResult result)
    {
        double minZ = vertices.Min(v => v.Z);
        double maxZ = vertices.Max(v => v.Z);
        double height = maxZ - minZ;
        if (height < MinimumWallHeight)
        {
            result.SkipReason = "Mesh wall recovery skipped: recovered height is too short (likely not a vertical wall).";
            return result;
        }

        // Run axis from a 2-D principal-axis fit of the plan footprint.
        double cx = vertices.Average(v => v.X);
        double cy = vertices.Average(v => v.Y);
        double sxx = 0, sxy = 0, syy = 0;
        foreach (AnalyticalPoint v in vertices)
        {
            double dx = v.X - cx, dy = v.Y - cy;
            sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
        }

        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        double ux = Math.Cos(angle), uy = Math.Sin(angle);   // run direction
        double vx = -uy, vy = ux;                            // thickness direction

        double tMin = double.PositiveInfinity, tMax = double.NegativeInfinity;
        double sMin = double.PositiveInfinity, sMax = double.NegativeInfinity;
        foreach (AnalyticalPoint v in vertices)
        {
            double t = (v.X - cx) * ux + (v.Y - cy) * uy;
            double s = (v.X - cx) * vx + (v.Y - cy) * vy;
            tMin = Math.Min(tMin, t); tMax = Math.Max(tMax, t);
            sMin = Math.Min(sMin, s); sMax = Math.Max(sMax, s);
        }

        double run = tMax - tMin;
        double thickness = sMax - sMin;
        if (run < MinimumWallRun)
        {
            result.SkipReason = "Mesh wall recovery skipped: recovered run length is too short.";
            return result;
        }

        double midS = (sMin + sMax) / 2.0;
        AnalyticalPoint At(double t, double z) => new()
        {
            X = cx + ux * t + vx * midS,
            Y = cy + uy * t + vy * midS,
            Z = z
        };

        var boundary = new List<AnalyticalPoint>
        {
            At(tMin, minZ),
            At(tMax, minZ),
            At(tMax, maxZ),
            At(tMin, maxZ)
        };

        var area = new AnalyticalAreaElement
        {
            SourceGuid = GetGuid(product),
            SourceName = GetName(product),
            IfcType = ifcType,
            BoundaryPoints = boundary,
            Thickness = thickness,
            MaterialName = ResolveMaterialName(product),
            RecognitionMethod = IfcRecognitionMethod.Inferred,
            Confidence = IfcRecognitionConfidence.Medium
        };
        AddRecoveryWarning(area, result, product, ifcType, "Wall recovered from mesh geometry (no extrusion). Verify thickness and extent.");
        result.Area = area;
        return result;
    }

    private AreaRecognitionResult RecoverSlab(IIfcProduct product, string ifcType, List<AnalyticalPoint> vertices, AreaRecognitionResult result)
    {
        double minZ = vertices.Min(v => v.Z);
        double maxZ = vertices.Max(v => v.Z);
        double thickness = maxZ - minZ;
        double midZ = (minZ + maxZ) / 2.0;

        double extentX = vertices.Max(v => v.X) - vertices.Min(v => v.X);
        double extentY = vertices.Max(v => v.Y) - vertices.Min(v => v.Y);
        if (extentX < MinimumSlabSpan || extentY < MinimumSlabSpan)
        {
            result.SkipReason = "Mesh slab recovery skipped: recovered plan extent is too small.";
            return result;
        }

        // A slab's thin dimension should be vertical. If it is not, this mesh is really a
        // wall/ramp/upstand and a horizontal mid-plane would be meaningless.
        if (thickness > Math.Min(extentX, extentY) * 0.5)
        {
            result.SkipReason = "Mesh slab recovery skipped: geometry is not plate-like (thickness comparable to plan span).";
            return result;
        }

        List<AnalyticalPoint> hull = ConvexHull(vertices, midZ);
        if (hull.Count < 3)
        {
            result.SkipReason = "Mesh slab recovery skipped: could not form a plan outline.";
            return result;
        }

        if (hull.Count > MaximumBoundaryPointCount)
        {
            result.SkipReason = "Mesh slab recovery skipped: recovered outline is too complex.";
            return result;
        }

        var area = new AnalyticalAreaElement
        {
            SourceGuid = GetGuid(product),
            SourceName = GetName(product),
            IfcType = ifcType,
            BoundaryPoints = hull,
            Thickness = thickness,
            MaterialName = ResolveMaterialName(product),
            RecognitionMethod = IfcRecognitionMethod.Inferred,
            Confidence = IfcRecognitionConfidence.Low
        };
        AddRecoveryWarning(area, result, product, ifcType, "Slab recovered from mesh geometry as a convex plan outline; non-rectangular slabs are over-approximated. Verify boundary.");
        result.Area = area;
        return result;
    }

    private void AddRecoveryWarning(AnalyticalAreaElement area, AreaRecognitionResult result, IIfcProduct product, string ifcType, string message)
    {
        area.Warnings.Add(message);
        result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, message));
        if (area.MaterialName.Length == 0)
        {
            area.MaterialName = UnknownMaterial;
            area.Warnings.Add("No IFC material association was found; assigned UNKNOWN_MATERIAL.");
        }
    }

    // 2-D convex hull (Andrew's monotone chain) of the plan projection, returned at the given
    // elevation with plan orientation preserved.
    private static List<AnalyticalPoint> ConvexHull(IReadOnlyList<AnalyticalPoint> vertices, double z)
    {
        var pts = vertices
            .Select(v => (v.X, v.Y))
            .Distinct()
            .OrderBy(p => p.X).ThenBy(p => p.Y)
            .ToList();
        if (pts.Count < 3)
            return [];

        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var lower = new List<(double X, double Y)>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<(double X, double Y)>();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return lower.Concat(upper)
            .Select(p => new AnalyticalPoint { X = p.X, Y = p.Y, Z = z })
            .ToList();
    }

    // ---- Mesh vertex reading (self-contained; mirrors AdvancedFrameGeometryRecognitionService) ----

    private static List<AnalyticalPoint> CollectBodyVertices(IIfcProduct product, XbimMatrix3D placement, double lengthFactor)
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
            CollectVertices(item, placement, lengthFactor, vertices);

        return vertices;
    }

    private static void CollectVertices(IIfcRepresentationItem item, XbimMatrix3D placement, double lengthFactor, List<AnalyticalPoint> vertices)
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
                    if (shell is IIfcConnectedFaceSet faceSet)
                        CollectConnectedFaceSetVertices(faceSet, placement, lengthFactor, vertices);
                break;
            case IIfcTessellatedFaceSet tessellatedFaceSet:
                CollectPointListVertices(tessellatedFaceSet.Coordinates, placement, lengthFactor, vertices);
                break;
            case IIfcMappedItem mappedItem:
                XbimMatrix3D mappedPlacement = ResolveMappedItemPlacement(mappedItem, placement);
                foreach (IIfcRepresentationItem mapped in mappedItem.MappingSource.MappedRepresentation.Items)
                    CollectVertices(mapped, mappedPlacement, lengthFactor, vertices);
                break;
        }
    }

    private static void CollectConnectedFaceSetVertices(IIfcConnectedFaceSet faceSet, XbimMatrix3D placement, double lengthFactor, List<AnalyticalPoint> vertices)
    {
        foreach (IIfcFace face in faceSet.CfsFaces)
            foreach (IIfcFaceBound bound in face.Bounds)
            {
                if (bound.Bound is not IIfcPolyLoop loop)
                    continue;
                foreach (IIfcCartesianPoint point in loop.Polygon)
                    vertices.Add(TransformPoint(placement, point.X, point.Y, point.Z, lengthFactor));
            }
    }

    private static void CollectPointListVertices(IIfcCartesianPointList3D pointList, XbimMatrix3D placement, double lengthFactor, List<AnalyticalPoint> vertices)
    {
        // CoordList items are value-type collections (ItemSet<IfcLengthMeasure>), which do not
        // covary to IEnumerable<object>; iterate non-generically and box via Cast.
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
            if (!unique.Any(existing => Distance(existing, vertex) <= DuplicateVertexTolerance))
                unique.Add(vertex);
        return unique;
    }

    private static XbimMatrix3D ResolvePlacement(IIfcObjectPlacement? placement, List<IfcImportWarning> warnings, IIfcProduct product, string ifcType)
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
        (double X, double Y, double Z) axis1 = DirectionOr(transformation.Axis1, (1, 0, 0));
        (double X, double Y, double Z) axis2 = DirectionOr(transformation.Axis2, (0, 1, 0));
        (double X, double Y, double Z) axis3 = transformation is IIfcCartesianTransformationOperator3D transformation3D
            ? DirectionOr(transformation3D.Axis3, Cross(axis1, axis2))
            : (0, 0, 1);

        return new XbimMatrix3D(
            axis1.X * scale, axis1.Y * scale, axis1.Z * scale, 0,
            axis2.X * scale, axis2.Y * scale, axis2.Z * scale, 0,
            axis3.X * scale, axis3.Y * scale, axis3.Z * scale, 0,
            ToDouble(transformation.LocalOrigin.X), ToDouble(transformation.LocalOrigin.Y), ToDouble(transformation.LocalOrigin.Z), 1);
    }

    private static (double X, double Y, double Z) DirectionOr(IIfcDirection? direction, (double X, double Y, double Z) fallback)
    {
        if (direction == null)
            return fallback;
        var value = Normalize((ToDouble(direction.X), ToDouble(direction.Y), ToDouble(direction.Z)));
        return value == (0, 0, 0) ? fallback : value;
    }

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => Normalize((a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X));

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length <= double.Epsilon ? (0, 0, 0) : (v.X / length, v.Y / length, v.Z / length);
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

    private static double Distance(AnalyticalPoint a, AnalyticalPoint b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static string ResolveMaterialName(IIfcProduct product)
    {
        IIfcMaterialSelect? material = product.Material;
        return material switch
        {
            IIfcMaterial direct => direct.Name.ToString(),
            IIfcMaterialProfileSetUsage profileUsage => FirstMaterialName(profileUsage.ForProfileSet.MaterialProfiles.Select(profile => profile.Material)),
            IIfcMaterialProfileSet profileSet => FirstMaterialName(profileSet.MaterialProfiles.Select(profile => profile.Material)),
            IIfcMaterialLayerSetUsage layerUsage => FirstMaterialName(layerUsage.ForLayerSet.MaterialLayers.Select(layer => layer.Material)),
            IIfcMaterialLayerSet layerSet => FirstMaterialName(layerSet.MaterialLayers.Select(layer => layer.Material)),
            IIfcMaterialList materialList => FirstMaterialName(materialList.Materials),
            _ => ""
        };
    }

    private static string FirstMaterialName(IEnumerable<IIfcMaterial?> materials)
    {
        return materials
            .Where(material => material != null)
            .Select(material => material!.Name.ToString())
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
    }

    private static bool IsRepresentation(IIfcRepresentation representation, string identifier)
        => string.Equals(representation.RepresentationIdentifier?.ToString(), identifier, StringComparison.OrdinalIgnoreCase);

    private static bool IsRepresentationType(IIfcRepresentation representation, string representationType)
        => string.Equals(representation.RepresentationType?.ToString(), representationType, StringComparison.OrdinalIgnoreCase);

    private static string GetGuid(IIfcRoot root) => root.GlobalId.ToString();

    private static string GetName(IIfcRoot root) => root.Name?.ToString() ?? "";

    private static double ToDouble(object value) => IfcMeasureValueConverter.ToDouble(value);

    private static IfcImportWarning BuildWarning(IIfcProduct product, string ifcType, IfcImportWarningSeverity severity, IfcImportWarningCategory category, string message)
        => new()
        {
            SourceGuid = GetGuid(product),
            SourceName = GetName(product),
            Severity = severity,
            Category = category,
            Message = message
        };
}
