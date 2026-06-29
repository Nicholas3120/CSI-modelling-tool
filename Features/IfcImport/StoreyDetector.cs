using System.Globalization;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace CSIModellingTools.Features.IfcImport;

public sealed class StoreyDetector
{
    public IReadOnlyList<IfcStoreyInfo> ReadStoreys(IfcStore model, double lengthFactor)
    {
        return model.Instances
            .OfType<IIfcBuildingStorey>()
            .Select(storey => new IfcStoreyInfo(
                GetName(storey),
                storey.Elevation.HasValue ? ToDouble(storey.Elevation.Value) * lengthFactor : double.NaN))
            .Where(storey => !string.IsNullOrWhiteSpace(storey.Name) && double.IsFinite(storey.Elevation))
            .OrderBy(storey => storey.Elevation)
            .ToList();
    }

    public string ResolveStoreyName(
        IIfcProduct product,
        AnalyticalFrameElement frame,
        IReadOnlyList<IfcStoreyInfo> storeys,
        double storeyElevationTolerance)
    {
        IIfcSpatialElement? spatialElement = product.IsContainedIn;
        if (spatialElement is IIfcBuildingStorey storey)
            return GetName(storey);

        if (product is IIfcElement element)
        {
            string containedStorey = element.ContainedInStructure
                .Select(relation => relation.RelatingStructure)
                .OfType<IIfcBuildingStorey>()
                .Select(GetName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";

            if (containedStorey.Length > 0)
                return containedStorey;
        }

        return ResolveByElevation(frame, storeys, storeyElevationTolerance);
    }

    public IfcImportWarning? BuildUnknownStoreyWarning(AnalyticalFrameElement frame)
    {
        if (!string.IsNullOrWhiteSpace(frame.StoreyName))
            return null;

        string message = "IFC storey could not be resolved from containment or inferred from elevation.";
        frame.Warnings.Add(message);
        return new IfcImportWarning
        {
            SourceGuid = frame.SourceGuid,
            SourceName = frame.SourceName,
            Severity = IfcImportWarningSeverity.Warning,
            Category = IfcImportWarningCategory.Storey,
            Message = message
        };
    }

    public string ResolveStoreyName(
        IIfcProduct product,
        AnalyticalAreaElement area,
        IReadOnlyList<IfcStoreyInfo> storeys,
        double storeyElevationTolerance)
    {
        IIfcSpatialElement? spatialElement = product.IsContainedIn;
        if (spatialElement is IIfcBuildingStorey storey)
            return GetName(storey);

        if (product is IIfcElement element)
        {
            string containedStorey = element.ContainedInStructure
                .Select(relation => relation.RelatingStructure)
                .OfType<IIfcBuildingStorey>()
                .Select(GetName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";

            if (containedStorey.Length > 0)
                return containedStorey;
        }

        return ResolveByElevation(area, storeys, storeyElevationTolerance);
    }

    public IfcImportWarning? BuildUnknownStoreyWarning(AnalyticalAreaElement area)
    {
        if (!string.IsNullOrWhiteSpace(area.StoreyName))
            return null;

        string message = "IFC storey could not be resolved from containment or inferred from elevation.";
        area.Warnings.Add(message);
        return new IfcImportWarning
        {
            SourceGuid = area.SourceGuid,
            SourceName = area.SourceName,
            Severity = IfcImportWarningSeverity.Warning,
            Category = IfcImportWarningCategory.Storey,
            Message = message
        };
    }

    private static string ResolveByElevation(
        AnalyticalFrameElement frame,
        IReadOnlyList<IfcStoreyInfo> storeys,
        double storeyElevationTolerance)
    {
        if (storeys.Count == 0 || !double.IsFinite(storeyElevationTolerance) || storeyElevationTolerance <= 0)
            return "";

        double middleZ = (frame.StartPoint.Z + frame.EndPoint.Z) / 2.0;
        return storeys
            .Select(storey => new { storey.Name, Delta = Math.Abs(storey.Elevation - middleZ) })
            .Where(candidate => candidate.Delta <= storeyElevationTolerance)
            .OrderBy(candidate => candidate.Delta)
            .Select(candidate => candidate.Name)
            .FirstOrDefault() ?? "";
    }

    private static string ResolveByElevation(
        AnalyticalAreaElement area,
        IReadOnlyList<IfcStoreyInfo> storeys,
        double storeyElevationTolerance)
    {
        if (storeys.Count == 0 || !double.IsFinite(storeyElevationTolerance) || storeyElevationTolerance <= 0 || area.BoundaryPoints.Count == 0)
            return "";

        double averageZ = area.BoundaryPoints.Average(point => point.Z);
        return storeys
            .Select(storey => new { storey.Name, Delta = Math.Abs(storey.Elevation - averageZ) })
            .Where(candidate => candidate.Delta <= storeyElevationTolerance)
            .OrderBy(candidate => candidate.Delta)
            .Select(candidate => candidate.Name)
            .FirstOrDefault() ?? "";
    }

    private static string GetName(IIfcRoot root)
    {
        return root.Name?.ToString() ?? "";
    }

    private static double ToDouble(object value)
    {
        return IfcMeasureValueConverter.ToDouble(value);
    }
}

public sealed record IfcStoreyInfo(string Name, double Elevation);
