using System.Globalization;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace CSIModellingTools.Features.IfcImport;

public sealed class AreaRecognitionService
{
    private const double PlanarityTolerance = 0.010;
    private const double DuplicatePointTolerance = 0.001;
    private const int MaximumBoundaryPointCount = 64;
    private const string UnknownMaterial = "UNKNOWN_MATERIAL";

    public AreaRecognitionResult TryCreateArea(
        IIfcProduct product,
        string ifcType,
        double lengthFactor)
    {
        var result = new AreaRecognitionResult();
        IIfcExtrudedAreaSolid? solid = FindSweptSolid(product);
        if (solid == null)
        {
            result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Unsupported, "Unsupported representation: no SweptSolid area body was found."));
            result.SkipReason = "No usable SweptSolid area representation found.";
            return result;
        }

        XbimMatrix3D placement = ResolvePlacement(product.ObjectPlacement, result.Warnings, product, ifcType);
        XbimMatrix3D solidPlacement = solid.Position == null
            ? new XbimMatrix3D()
            : Xbim.Ifc.IIfcAxis2PlacementExtensions.ToMatrix3D(solid.Position);
        XbimMatrix3D transform = XbimMatrix3D.Multiply(solidPlacement, placement);

        double extrusionDepth = ToDouble(solid.Depth);
        DirectionVector direction = Normalize(new DirectionVector(solid.ExtrudedDirection.X, solid.ExtrudedDirection.Y, solid.ExtrudedDirection.Z));
        if (extrusionDepth <= 0 || !direction.IsValid)
        {
            result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, "SweptSolid extrusion direction or depth is not usable."));
            result.SkipReason = "SweptSolid extrusion direction or depth is not usable.";
            return result;
        }

        List<ProfilePoint> profileBoundary = ExtractProfileBoundary(solid.SweptArea, product, ifcType, result.Warnings);
        if (profileBoundary.Count < 3)
        {
            result.SkipReason = "Area boundary is unsupported or has fewer than three points.";
            return result;
        }

        profileBoundary = SimplifyBoundary(profileBoundary);
        if (profileBoundary.Count < 3)
        {
            result.SkipReason = "Area boundary became invalid after conservative simplification.";
            return result;
        }

        if (profileBoundary.Count > MaximumBoundaryPointCount)
        {
            result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Boundary, $"Complex boundary has {profileBoundary.Count} points and was skipped."));
            result.SkipReason = "Complex boundary has too many points.";
            return result;
        }

        double halfDepth = extrusionDepth / 2.0;
        List<AnalyticalPoint> boundary = profileBoundary
            .Select(point => TransformPoint(
                transform,
                point.X + direction.X * halfDepth,
                point.Y + direction.Y * halfDepth,
                point.Z + direction.Z * halfDepth,
                lengthFactor))
            .ToList();

        if (!IsPlanar(boundary))
        {
            result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, "Non-planar surface was skipped."));
            result.SkipReason = "Non-planar surface.";
            return result;
        }

        if (HasOpenings(product))
        {
            string message = "Openings are present and were ignored in the analytical area boundary.";
            result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Opening, message));
        }

        double thickness = ResolveThickness(product, solid, lengthFactor);
        bool hasGeometryWarning = result.Warnings.Any(warning =>
            warning.Category is IfcImportWarningCategory.Opening or IfcImportWarningCategory.Boundary or IfcImportWarningCategory.Geometry or IfcImportWarningCategory.Unsupported);
        var area = new AnalyticalAreaElement
        {
            SourceGuid = GetGuid(product),
            SourceName = GetName(product),
            IfcType = ifcType,
            BoundaryPoints = boundary,
            Thickness = thickness,
            MaterialName = ResolveMaterialName(product),
            RecognitionMethod = IfcRecognitionMethod.SweptSolid,
            Confidence = thickness > 0 && !hasGeometryWarning ? IfcRecognitionConfidence.High : IfcRecognitionConfidence.Medium
        };

        if (area.MaterialName.Length == 0)
        {
            area.MaterialName = UnknownMaterial;
            string message = "No IFC material association was found; assigned UNKNOWN_MATERIAL.";
            area.Warnings.Add(message);
            result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Material, message));
        }

        if (thickness <= 0)
        {
            string message = "Area thickness could not be resolved from IFC material layers, structural member thickness, or extrusion depth.";
            area.Warnings.Add(message);
            result.Warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, message));
            area.Confidence = IfcRecognitionConfidence.Low;
        }

        foreach (IfcImportWarning warning in result.Warnings.Where(warning => warning.Category is IfcImportWarningCategory.Opening or IfcImportWarningCategory.Boundary))
            area.Warnings.Add(warning.Message);

        result.Area = area;
        return result;
    }

    private static IIfcExtrudedAreaSolid? FindSweptSolid(IIfcProduct product)
    {
        return product.Representation?.Representations
            .Where(representation =>
                IsRepresentation(representation, "Body") ||
                IsRepresentationType(representation, "SweptSolid") ||
                IsRepresentationType(representation, "AdvancedSweptSolid"))
            .SelectMany(representation => representation.Items)
            .OfType<IIfcExtrudedAreaSolid>()
            .FirstOrDefault();
    }

    private static List<ProfilePoint> ExtractProfileBoundary(
        IIfcProfileDef? profile,
        IIfcProduct product,
        string ifcType,
        List<IfcImportWarning> warnings)
    {
        if (profile == null)
        {
            warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Boundary, "SweptSolid has no swept profile."));
            return [];
        }

        if (profile is IIfcRectangleProfileDef rectangle)
        {
            double halfX = ToDouble(rectangle.XDim) / 2.0;
            double halfY = ToDouble(rectangle.YDim) / 2.0;
            return
            [
                new ProfilePoint(-halfX, -halfY, 0),
                new ProfilePoint(halfX, -halfY, 0),
                new ProfilePoint(halfX, halfY, 0),
                new ProfilePoint(-halfX, halfY, 0)
            ];
        }

        if (profile is IIfcArbitraryProfileDefWithVoids voidProfile && voidProfile.InnerCurves.Count > 0)
        {
            warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Opening, "Profile openings are present and were ignored in this phase."));
        }

        if (profile is IIfcArbitraryClosedProfileDef closedProfile)
            return ExtractCurveBoundary(closedProfile.OuterCurve, product, ifcType, warnings);

        warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Unsupported, $"Unsupported profile '{profile.GetType().Name}' for analytical area boundary."));
        return [];
    }

    private static List<ProfilePoint> ExtractCurveBoundary(
        IIfcCurve curve,
        IIfcProduct product,
        string ifcType,
        List<IfcImportWarning> warnings)
    {
        if (curve is IIfcPolyline polyline)
        {
            return polyline.Points
                .Select(point => new ProfilePoint(ToDouble(point.X), ToDouble(point.Y), ToDouble(point.Z)))
                .ToList();
        }

        warnings.Add(BuildWarning(product, ifcType, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Boundary, $"Complex boundary curve '{curve.GetType().Name}' was skipped."));
        return [];
    }

    private static double ResolveThickness(IIfcProduct product, IIfcExtrudedAreaSolid solid, double lengthFactor)
    {
        if (product is IIfcStructuralSurfaceMember surfaceMember && surfaceMember.Thickness.HasValue)
            return ToDouble(surfaceMember.Thickness.Value) * lengthFactor;

        double layerThickness = ResolveMaterialLayerThickness(product);
        if (layerThickness > 0)
            return layerThickness * lengthFactor;

        double extrusionDepth = ToDouble(solid.Depth) * lengthFactor;
        return extrusionDepth > 0 ? extrusionDepth : 0;
    }

    private static double ResolveMaterialLayerThickness(IIfcProduct product)
    {
        IIfcMaterialSelect? material = product.Material;
        if (material is IIfcMaterialLayerSetUsage layerUsage)
            return SumLayerThickness(layerUsage.ForLayerSet.MaterialLayers);
        if (material is IIfcMaterialLayerSet layerSet)
            return SumLayerThickness(layerSet.MaterialLayers);

        return 0;
    }

    private static double SumLayerThickness(IEnumerable<IIfcMaterialLayer> layers)
    {
        return layers
            .Select(layer => ToDouble(layer.LayerThickness))
            .Where(value => double.IsFinite(value) && value > 0)
            .Sum();
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

    private static bool HasOpenings(IIfcProduct product)
    {
        return product is IIfcElement element && element.HasOpenings.Any();
    }

    private static List<ProfilePoint> SimplifyBoundary(List<ProfilePoint> points)
    {
        var simplified = new List<ProfilePoint>();
        foreach (ProfilePoint point in points)
        {
            if (simplified.Count == 0 || Distance(simplified[^1], point) > DuplicatePointTolerance)
                simplified.Add(point);
        }

        if (simplified.Count > 2 && Distance(simplified[0], simplified[^1]) <= DuplicatePointTolerance)
            simplified.RemoveAt(simplified.Count - 1);

        return simplified;
    }

    private static bool IsPlanar(IReadOnlyList<AnalyticalPoint> points)
    {
        if (points.Count < 3)
            return false;

        AnalyticalPoint origin = points[0];
        Vector3 normal = new(0, 0, 0);
        for (int i = 1; i < points.Count - 1; i++)
        {
            normal = Cross(Subtract(points[i], origin), Subtract(points[i + 1], origin));
            if (normal.Length > PlanarityTolerance)
                break;
        }

        if (normal.Length <= PlanarityTolerance)
            return false;

        normal = normal.Normalize();
        return points.All(point => Math.Abs(Dot(Subtract(point, origin), normal)) <= PlanarityTolerance);
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

    private static DirectionVector Normalize(DirectionVector direction)
    {
        double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
        if (!double.IsFinite(length) || length <= double.Epsilon)
            return direction with { IsValid = false };

        return new DirectionVector(direction.X / length, direction.Y / length, direction.Z / length, true);
    }

    private static Vector3 Subtract(AnalyticalPoint point, AnalyticalPoint origin)
    {
        return new Vector3(point.X - origin.X, point.Y - origin.Y, point.Z - origin.Z);
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

    private static double Distance(ProfilePoint first, ProfilePoint second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static bool IsRepresentation(IIfcRepresentation representation, string identifier)
    {
        return string.Equals(representation.RepresentationIdentifier?.ToString(), identifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRepresentationType(IIfcRepresentation representation, string representationType)
    {
        return string.Equals(representation.RepresentationType?.ToString(), representationType, StringComparison.OrdinalIgnoreCase);
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

    private readonly record struct ProfilePoint(double X, double Y, double Z);
    private readonly record struct DirectionVector(double X, double Y, double Z, bool IsValid = true);

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

public sealed class AreaRecognitionResult
{
    public AnalyticalAreaElement? Area { get; set; }
    public List<IfcImportWarning> Warnings { get; } = [];
    public string SkipReason { get; set; } = "";
}
