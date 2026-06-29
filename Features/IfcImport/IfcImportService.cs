using System.IO;
using System.Globalization;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace CSIModellingTools.Features.IfcImport;

public sealed class IfcImportService
{
    private const string UnknownMaterial = "UNKNOWN_MATERIAL";
    private const double ZeroLengthTolerance = 0.000001;

    private readonly IfcImportNodeSnapper _nodeSnapper = new();
    private readonly DuplicateFrameDetector _duplicateFrameDetector = new();
    private readonly ShortMemberDetector _shortMemberDetector = new();
    private readonly ConnectivityChecker _connectivityChecker = new();
    private readonly StoreyDetector _storeyDetector = new();
    private readonly CoordinateOriginService _coordinateOriginService = new();
    private readonly AreaRecognitionService _areaRecognitionService = new();
    private readonly AdvancedFrameGeometryRecognitionService _advancedFrameGeometryRecognitionService = new();

    public IfcImportResult ImportStructuralFrames(
        string ifcPath,
        IfcImportOptions options,
        CancellationToken cancellationToken = default,
        IProgress<IfcImportProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(ifcPath))
            throw new ArgumentException("Choose an IFC file path before importing structural frames.", nameof(ifcPath));
        if (!File.Exists(ifcPath))
            throw new FileNotFoundException("IFC file was not found.", ifcPath);

        options ??= new IfcImportOptions();

        var result = new IfcImportResult();
        progress?.Report(new IfcImportProgress(0, "Opening IFC file...", false));
        using IfcStore model = IfcStore.Open(ifcPath);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new IfcImportProgress(0, "Reading model data...", false));
        double lengthFactor = ResolveLengthFactor(model, options, result);
        IReadOnlyList<IfcStoreyInfo> storeys = _storeyDetector.ReadStoreys(model, lengthFactor);
        var candidates = EnumerateCandidates(model, options).ToList();
        var areaCandidates = EnumerateAreaCandidates(model, options).ToList();

        result.TotalIfcElementsScanned = candidates.Count + areaCandidates.Count;
        result.BeamCount = candidates.Count(candidate => candidate.IfcType == "IfcBeam");
        result.ColumnCount = candidates.Count(candidate => candidate.IfcType == "IfcColumn");
        result.SlabCount = areaCandidates.Count(candidate => candidate.IfcType == "IfcSlab");
        result.WallCount = areaCandidates.Count(candidate => candidate.IfcType == "IfcWall");
        result.StructuralSurfaceMemberCount = areaCandidates.Count(candidate => candidate.IfcType == "IfcStructuralSurfaceMember");

        // Recognition is the long, countable phase; report determinate progress over it.
        // Reserve the final few percent for the cleanup passes below.
        int total = candidates.Count + areaCandidates.Count;
        int processed = 0;
        int lastReportedPercent = -1;
        void ReportRecognition(string stage)
        {
            if (progress == null || total == 0)
                return;

            int percent = (int)(processed * 95.0 / total);
            if (percent == lastReportedPercent)
                return;

            lastReportedPercent = percent;
            progress.Report(new IfcImportProgress(percent, stage, true));
        }

        foreach (ImportCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportFrameCandidate(candidate, lengthFactor, storeys, options, result);
            processed++;
            ReportRecognition($"Recognising elements {processed}/{total}");
        }

        foreach (ImportCandidate candidate in areaCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportAreaCandidate(candidate, lengthFactor, storeys, options, result);
            processed++;
            ReportRecognition($"Recognising elements {processed}/{total}");
        }

        progress?.Report(new IfcImportProgress(96, "Cleaning up and validating...", true));
        AddScaleSanityWarning(result, lengthFactor);
        result.Warnings.AddRange(_coordinateOriginService.ApplyCoordinateOriginReset(result, options));
        List<IfcImportWarning> snapWarnings = _nodeSnapper.SnapFrameEndpoints(result.Frames, options.NodeSnapTolerance);
        result.Warnings.AddRange(snapWarnings);
        result.CleanupActions.AddRange(snapWarnings.Select(warning => warning.Message));
        result.Warnings.AddRange(_duplicateFrameDetector.DetectDuplicates(result.Frames, options.DuplicateFrameTolerance, options.DuplicateSectionTolerance));
        result.Warnings.AddRange(_shortMemberDetector.DetectShortMembers(result.Frames, options.ShortMemberMinimumLength));
        result.Warnings.AddRange(_connectivityChecker.CheckBeamEndpointConnectivity(result.Frames, options.ConnectivityTolerance));
        progress?.Report(new IfcImportProgress(100, "Done", true));

        result.ImportedAreaCount = result.Areas.Count;
        result.ImportedCount = result.Frames.Count + result.Areas.Count;
        result.SkippedCount = result.SkippedElements.Count;
        result.WarningCount = result.Warnings.Count;
        return result;
    }

    private static IEnumerable<ImportCandidate> EnumerateCandidates(IfcStore model, IfcImportOptions options)
    {
        if (options.IncludeBeams)
        {
            foreach (IIfcBeam beam in model.Instances.OfType<IIfcBeam>())
                yield return new ImportCandidate(beam, "IfcBeam");
        }

        if (options.IncludeColumns)
        {
            foreach (IIfcColumn column in model.Instances.OfType<IIfcColumn>())
                yield return new ImportCandidate(column, "IfcColumn");
        }
    }

    private static IEnumerable<ImportCandidate> EnumerateAreaCandidates(IfcStore model, IfcImportOptions options)
    {
        if (options.IncludeSlabs)
        {
            foreach (IIfcSlab slab in model.Instances.OfType<IIfcSlab>())
                yield return new ImportCandidate(slab, "IfcSlab");
        }

        if (options.IncludeWalls)
        {
            foreach (IIfcWall wall in model.Instances.OfType<IIfcWall>())
                yield return new ImportCandidate(wall, "IfcWall");
        }

        if (options.IncludeStructuralSurfaceMembers)
        {
            foreach (IIfcStructuralSurfaceMember surfaceMember in model.Instances.OfType<IIfcStructuralSurfaceMember>())
                yield return new ImportCandidate(surfaceMember, "IfcStructuralSurfaceMember");
        }
    }

    private void ImportFrameCandidate(
        ImportCandidate candidate,
        double lengthFactor,
        IReadOnlyList<IfcStoreyInfo> storeys,
        IfcImportOptions options,
        IfcImportResult result)
    {
        var localWarnings = new List<IfcImportWarning>();
        if (TryCreateFromAxis(candidate, lengthFactor, localWarnings, out AnalyticalFrameElement? axisFrame) && axisFrame != null ||
            TryCreateFromSweptSolid(candidate, lengthFactor, localWarnings, out axisFrame) && axisFrame != null)
        {
            FinalizeFrame(axisFrame, candidate, lengthFactor, storeys, options, result, localWarnings);
            return;
        }

        if (options.EnableAdvancedGeometryRecognition)
        {
            AdvancedFrameGeometryRecognitionResult advancedResult = _advancedFrameGeometryRecognitionService.TryInferFrame(candidate.Product, candidate.IfcType, lengthFactor);
            localWarnings.AddRange(advancedResult.Warnings);
            if (advancedResult.Frame != null)
            {
                FinalizeFrame(advancedResult.Frame, candidate, lengthFactor, storeys, options, result, localWarnings);
                return;
            }
        }

        string reason = options.EnableAdvancedGeometryRecognition
            ? "No usable Axis, SweptSolid, or safely inferred Brep/mesh representation found."
            : "No usable Axis or SweptSolid representation found.";
        result.SkippedElements.Add(new SkippedIfcElement
        {
            SourceGuid = GetGuid(candidate.Product),
            SourceName = GetName(candidate.Product),
            IfcType = candidate.IfcType,
            Reason = reason
        });
        result.Warnings.AddRange(localWarnings);
        result.Warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Unsupported, reason));
    }

    private void ImportAreaCandidate(
        ImportCandidate candidate,
        double lengthFactor,
        IReadOnlyList<IfcStoreyInfo> storeys,
        IfcImportOptions options,
        IfcImportResult result)
    {
        AreaRecognitionResult areaResult = _areaRecognitionService.TryCreateArea(candidate.Product, candidate.IfcType, lengthFactor);
        if (areaResult.Area == null)
        {
            string reason = string.IsNullOrWhiteSpace(areaResult.SkipReason)
                ? "No usable analytical area representation found."
                : areaResult.SkipReason;
            result.SkippedElements.Add(new SkippedIfcElement
            {
                SourceGuid = GetGuid(candidate.Product),
                SourceName = GetName(candidate.Product),
                IfcType = candidate.IfcType,
                Reason = reason
            });
            result.Warnings.AddRange(areaResult.Warnings);
            return;
        }

        AnalyticalAreaElement area = areaResult.Area;
        area.StoreyName = _storeyDetector.ResolveStoreyName(candidate.Product, area, storeys, options.StoreyElevationTolerance);
        IfcImportWarning? unknownStoreyWarning = _storeyDetector.BuildUnknownStoreyWarning(area);
        if (unknownStoreyWarning != null)
            areaResult.Warnings.Add(unknownStoreyWarning);

        result.Areas.Add(area);
        result.Warnings.AddRange(areaResult.Warnings);
    }

    private void FinalizeFrame(
        AnalyticalFrameElement frame,
        ImportCandidate candidate,
        double lengthFactor,
        IReadOnlyList<IfcStoreyInfo> storeys,
        IfcImportOptions options,
        IfcImportResult result,
        List<IfcImportWarning> localWarnings)
    {
        if (Distance(frame.StartPoint, frame.EndPoint) <= ZeroLengthTolerance)
        {
            string reason = "Analytical frame line has zero or near-zero length.";
            result.SkippedElements.Add(new SkippedIfcElement
            {
                SourceGuid = frame.SourceGuid,
                SourceName = frame.SourceName,
                IfcType = frame.IfcType,
                Reason = reason
            });
            result.Warnings.AddRange(localWarnings);
            result.Warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Error, IfcImportWarningCategory.Geometry, reason));
            return;
        }

        frame.StoreyName = _storeyDetector.ResolveStoreyName(candidate.Product, frame, storeys, options.StoreyElevationTolerance);
        IfcImportWarning? unknownStoreyWarning = _storeyDetector.BuildUnknownStoreyWarning(frame);
        if (unknownStoreyWarning != null)
            localWarnings.Add(unknownStoreyWarning);
        frame.MaterialName = ResolveMaterialName(candidate.Product);
        if (frame.MaterialName.Length == 0)
        {
            frame.MaterialName = UnknownMaterial;
            string message = "No IFC material association was found; assigned UNKNOWN_MATERIAL.";
            frame.Warnings.Add(message);
            localWarnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Material, message));
        }

        frame.SectionInfo = frame.SectionInfo.ShapeType == IfcSectionShapeType.Unknown
            ? frame.SectionInfo
            : frame.RecognitionMethod == IfcRecognitionMethod.Inferred
                ? frame.SectionInfo
                : ConvertSectionUnits(frame.SectionInfo, lengthFactor, candidate.IfcType);
        frame.SectionName = frame.SectionInfo.SectionName;

        result.Frames.Add(frame);
        result.Warnings.AddRange(localWarnings);
    }

    private static bool TryCreateFromAxis(
        ImportCandidate candidate,
        double lengthFactor,
        List<IfcImportWarning> warnings,
        out AnalyticalFrameElement? frame)
    {
        frame = null;
        IIfcRepresentation? axisRepresentation = candidate.Product.Representation?.Representations
            .FirstOrDefault(representation => IsRepresentation(representation, "Axis"));
        if (axisRepresentation == null)
            return false;

        XbimMatrix3D placement = ResolvePlacement(candidate.Product.ObjectPlacement, warnings, candidate);
        foreach (IIfcRepresentationItem item in axisRepresentation.Items)
        {
            List<AnalyticalPoint> points = ExtractAxisPoints(item, placement, lengthFactor, warnings, candidate);
            if (points.Count >= 2)
            {
                frame = BuildFrame(
                    candidate,
                    points.First(),
                    points.Last(),
                    IfcRecognitionMethod.Axis,
                    IfcRecognitionConfidence.High,
                    ExtractBodySectionInfo(candidate, lengthFactor, warnings));
                return true;
            }
        }

        warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, "Axis representation exists but is not a supported straight polyline."));
        return false;
    }

    private static SectionInfo ExtractBodySectionInfo(
        ImportCandidate candidate,
        double lengthFactor,
        List<IfcImportWarning> warnings)
    {
        SweptSolidMatch? solidMatch = candidate.Product.Representation?.Representations
            .Where(representation =>
                IsRepresentation(representation, "Body") ||
                IsRepresentationType(representation, "SweptSolid") ||
                IsRepresentationType(representation, "AdvancedSweptSolid") ||
                IsRepresentationType(representation, "MappedRepresentation"))
            .SelectMany(representation => representation.Items)
            .Select(item => FindSweptSolid(item, new XbimMatrix3D()))
            .Where(match => match != null)
            .FirstOrDefault();

        if (solidMatch == null)
            return new SectionInfo();

        return MapSectionInfo(solidMatch.Solid.SweptArea, lengthFactor, candidate.IfcType, warnings, candidate);
    }

    private static List<AnalyticalPoint> ExtractAxisPoints(
        IIfcRepresentationItem item,
        XbimMatrix3D placement,
        double lengthFactor,
        List<IfcImportWarning> warnings,
        ImportCandidate candidate)
    {
        if (item is IIfcCurve curve)
            return ExtractCurvePoints(curve, placement, lengthFactor, warnings, candidate);

        if (item is IIfcMappedItem mappedItem)
        {
            XbimMatrix3D mappedPlacement = ResolveMappedItemPlacement(mappedItem, placement);
            foreach (IIfcRepresentationItem mappedRepresentationItem in mappedItem.MappingSource.MappedRepresentation.Items)
            {
                List<AnalyticalPoint> points = ExtractAxisPoints(mappedRepresentationItem, mappedPlacement, lengthFactor, warnings, candidate);
                if (points.Count >= 2)
                    return points;
            }
        }

        warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Info, IfcImportWarningCategory.Geometry, $"Axis item '{item.GetType().Name}' is not a supported curve."));
        return [];
    }

    private static List<AnalyticalPoint> ExtractCurvePoints(
        IIfcCurve curve,
        XbimMatrix3D placement,
        double lengthFactor,
        List<IfcImportWarning> warnings,
        ImportCandidate candidate)
    {
        switch (curve)
        {
            case IIfcPolyline polyline:
                return polyline.Points
                    .Select(point => TransformPoint(placement, point, lengthFactor))
                    .ToList();

            case IIfcIndexedPolyCurve indexedPolyCurve:
                if (indexedPolyCurve.Segments.Any(segment => segment.GetType().Name.Contains("ArcIndex", StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, "Axis IndexedPolyCurve contains arc segments; curved members are not supported."));
                    return [];
                }

                return ExtractPointList(indexedPolyCurve.Points, placement, lengthFactor);

            case IIfcCompositeCurve compositeCurve:
                return ExtractCompositeCurvePoints(compositeCurve, placement, lengthFactor, warnings, candidate);

            case IIfcTrimmedCurve trimmedCurve:
                return ExtractTrimmedCurvePoints(trimmedCurve, placement, lengthFactor, warnings, candidate);

            case IIfcLine line:
                return ExtractLinePoints(line, placement, lengthFactor);

            default:
                warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Info, IfcImportWarningCategory.Geometry, $"Axis curve '{curve.GetType().Name}' is not supported."));
                return [];
        }
    }

    private static List<AnalyticalPoint> ExtractPointList(
        IIfcCartesianPointList pointList,
        XbimMatrix3D placement,
        double lengthFactor)
    {
        if (pointList is IIfcCartesianPointList3D pointList3D)
        {
            return pointList3D.CoordList
                .Select(coordinates => ToCoordinateArray(coordinates))
                .Where(coordinates => coordinates.Length >= 3)
                .Select(coordinates => TransformPoint(placement, coordinates[0], coordinates[1], coordinates[2], lengthFactor))
                .ToList();
        }

        if (pointList is IIfcCartesianPointList2D pointList2D)
        {
            return pointList2D.CoordList
                .Select(coordinates => ToCoordinateArray(coordinates))
                .Where(coordinates => coordinates.Length >= 2)
                .Select(coordinates => TransformPoint(placement, coordinates[0], coordinates[1], 0, lengthFactor))
                .ToList();
        }

        return [];
    }

    private static List<AnalyticalPoint> ExtractCompositeCurvePoints(
        IIfcCompositeCurve compositeCurve,
        XbimMatrix3D placement,
        double lengthFactor,
        List<IfcImportWarning> warnings,
        ImportCandidate candidate)
    {
        var points = new List<AnalyticalPoint>();
        foreach (IIfcCompositeCurveSegment segment in compositeCurve.Segments)
        {
            List<AnalyticalPoint> segmentPoints = ExtractCurvePoints(segment.ParentCurve, placement, lengthFactor, warnings, candidate);
            if (segmentPoints.Count == 0)
                continue;

            if (points.Count > 0 && Distance(points[^1], segmentPoints[0]) <= ZeroLengthTolerance)
                segmentPoints.RemoveAt(0);

            points.AddRange(segmentPoints);
        }

        return points;
    }

    private static List<AnalyticalPoint> ExtractTrimmedCurvePoints(
        IIfcTrimmedCurve trimmedCurve,
        XbimMatrix3D placement,
        double lengthFactor,
        List<IfcImportWarning> warnings,
        ImportCandidate candidate)
    {
        AnalyticalPoint? trimStart = ExtractTrimPoint(trimmedCurve.Trim1, placement, lengthFactor);
        AnalyticalPoint? trimEnd = ExtractTrimPoint(trimmedCurve.Trim2, placement, lengthFactor);
        if (trimStart != null && trimEnd != null)
            return [trimStart, trimEnd];

        if (trimmedCurve.BasisCurve is IIfcLine line)
        {
            double? startParameter = ExtractTrimParameter(trimmedCurve.Trim1);
            double? endParameter = ExtractTrimParameter(trimmedCurve.Trim2);
            if (startParameter.HasValue && endParameter.HasValue)
            {
                return
                [
                    PointOnLine(line, startParameter.Value, placement, lengthFactor),
                    PointOnLine(line, endParameter.Value, placement, lengthFactor)
                ];
            }

            return ExtractLinePoints(line, placement, lengthFactor);
        }

        return ExtractCurvePoints(trimmedCurve.BasisCurve, placement, lengthFactor, warnings, candidate);
    }

    private static AnalyticalPoint? ExtractTrimPoint(
        IEnumerable<IIfcTrimmingSelect> trimValues,
        XbimMatrix3D placement,
        double lengthFactor)
    {
        return trimValues
            .OfType<IIfcCartesianPoint>()
            .Select(point => TransformPoint(placement, point, lengthFactor))
            .FirstOrDefault();
    }

    private static double? ExtractTrimParameter(IEnumerable<IIfcTrimmingSelect> trimValues)
    {
        foreach (IIfcTrimmingSelect trimValue in trimValues)
        {
            if (trimValue is IIfcCartesianPoint)
                continue;

            try
            {
                return ToDouble(trimValue);
            }
            catch
            {
                // Non-parameter trim values are ignored; Cartesian trims are handled separately.
            }
        }

        return null;
    }

    private static List<AnalyticalPoint> ExtractLinePoints(
        IIfcLine line,
        XbimMatrix3D placement,
        double lengthFactor)
    {
        double magnitude = ToDouble(line.Dir.Magnitude);
        if (magnitude <= 0)
            return [];

        return
        [
            TransformPoint(placement, line.Pnt, lengthFactor),
            TransformPoint(
                placement,
                line.Pnt.X + line.Dir.Orientation.X * magnitude,
                line.Pnt.Y + line.Dir.Orientation.Y * magnitude,
                line.Pnt.Z + line.Dir.Orientation.Z * magnitude,
                lengthFactor)
        ];
    }

    private static AnalyticalPoint PointOnLine(
        IIfcLine line,
        double parameter,
        XbimMatrix3D placement,
        double lengthFactor)
    {
        double magnitude = ToDouble(line.Dir.Magnitude);
        return TransformPoint(
            placement,
            line.Pnt.X + line.Dir.Orientation.X * magnitude * parameter,
            line.Pnt.Y + line.Dir.Orientation.Y * magnitude * parameter,
            line.Pnt.Z + line.Dir.Orientation.Z * magnitude * parameter,
            lengthFactor);
    }

    private static bool TryCreateFromSweptSolid(
        ImportCandidate candidate,
        double lengthFactor,
        List<IfcImportWarning> warnings,
        out AnalyticalFrameElement? frame)
    {
        frame = null;
        XbimMatrix3D productPlacement = ResolvePlacement(candidate.Product.ObjectPlacement, warnings, candidate);
        SweptSolidMatch? solidMatch = candidate.Product.Representation?.Representations
            .Where(representation =>
                IsRepresentation(representation, "Body") ||
                IsRepresentationType(representation, "SweptSolid") ||
                IsRepresentationType(representation, "AdvancedSweptSolid"))
            .SelectMany(representation => representation.Items)
            .Select(item => FindSweptSolid(item, productPlacement))
            .Where(match => match != null)
            .FirstOrDefault();

        if (solidMatch == null)
            return false;

        IIfcExtrudedAreaSolid solid = solidMatch.Solid;
        XbimMatrix3D solidPlacement = solid.Position == null
            ? new XbimMatrix3D()
            : Xbim.Ifc.IIfcAxis2PlacementExtensions.ToMatrix3D(solid.Position);
        XbimMatrix3D placement = XbimMatrix3D.Multiply(solidPlacement, solidMatch.Placement);

        double depth = ToDouble(solid.Depth);
        DirectionVector direction = Normalize(new DirectionVector(solid.ExtrudedDirection.X, solid.ExtrudedDirection.Y, solid.ExtrudedDirection.Z));
        if (depth <= 0 || !direction.IsValid)
        {
            warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Geometry, "SweptSolid extrusion direction or depth is not usable."));
            return false;
        }

        SectionInfo section = MapSectionInfo(solid.SweptArea, lengthFactor, candidate.IfcType, warnings, candidate);
        IfcRecognitionConfidence confidence = section.ShapeType == IfcSectionShapeType.Unknown
            ? IfcRecognitionConfidence.Medium
            : IfcRecognitionConfidence.High;

        frame = BuildFrame(
            candidate,
            TransformPoint(placement, 0, 0, 0, lengthFactor),
            TransformPoint(placement, direction.X * depth, direction.Y * depth, direction.Z * depth, lengthFactor),
            IfcRecognitionMethod.SweptSolid,
            confidence,
            section);
        return true;
    }

    private static SweptSolidMatch? FindSweptSolid(IIfcRepresentationItem item, XbimMatrix3D placement)
    {
        if (item is IIfcExtrudedAreaSolid solid)
            return new SweptSolidMatch(solid, placement);

        if (item is IIfcMappedItem mappedItem)
        {
            XbimMatrix3D mappedPlacement = ResolveMappedItemPlacement(mappedItem, placement);
            foreach (IIfcRepresentationItem mappedRepresentationItem in mappedItem.MappingSource.MappedRepresentation.Items)
            {
                SweptSolidMatch? match = FindSweptSolid(mappedRepresentationItem, mappedPlacement);
                if (match != null)
                    return match;
            }
        }

        return null;
    }

    private static AnalyticalFrameElement BuildFrame(
        ImportCandidate candidate,
        AnalyticalPoint start,
        AnalyticalPoint end,
        IfcRecognitionMethod recognitionMethod,
        IfcRecognitionConfidence confidence,
        SectionInfo sectionInfo)
    {
        return new AnalyticalFrameElement
        {
            SourceGuid = GetGuid(candidate.Product),
            SourceName = GetName(candidate.Product),
            IfcType = candidate.IfcType,
            StartPoint = start,
            EndPoint = end,
            RecognitionMethod = recognitionMethod,
            Confidence = confidence,
            SectionInfo = sectionInfo
        };
    }

    private static XbimMatrix3D ResolvePlacement(
        IIfcObjectPlacement? placement,
        List<IfcImportWarning> warnings,
        ImportCandidate candidate)
    {
        if (placement == null)
        {
            warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Placement, "IFC object has no placement; local coordinates were treated as global."));
            return new XbimMatrix3D();
        }

        if (placement is not IIfcLocalPlacement localPlacement)
        {
            warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Placement, $"Unsupported placement type '{placement.GetType().Name}'; local coordinates were treated as global."));
            return new XbimMatrix3D();
        }

        XbimMatrix3D relative = localPlacement.RelativePlacement == null
            ? new XbimMatrix3D()
            : Xbim.Ifc.IIfcAxis2PlacementExtensions.ToMatrix3D(localPlacement.RelativePlacement);
        if (localPlacement.PlacementRelTo == null)
            return relative;

        XbimMatrix3D parent = ResolvePlacement(localPlacement.PlacementRelTo, warnings, candidate);
        return XbimMatrix3D.Multiply(relative, parent);
    }

    private static AnalyticalPoint TransformPoint(XbimMatrix3D matrix, IIfcCartesianPoint point, double lengthFactor)
    {
        return TransformPoint(matrix, point.X, point.Y, point.Z, lengthFactor);
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
        XbimMatrix3D mappingTarget = ToMatrix3D(mappedItem.MappingTarget);
        return XbimMatrix3D.Multiply(XbimMatrix3D.Multiply(mappingOrigin, mappingTarget), parentPlacement);
    }

    private static XbimMatrix3D ToMatrix3D(IIfcCartesianTransformationOperator transformation)
    {
        double scale = transformation.Scale.HasValue ? ToDouble(transformation.Scale.Value) : 1.0;
        DirectionVector axis1 = ToDirection(transformation.Axis1, new DirectionVector(1, 0, 0));
        DirectionVector axis2 = ToDirection(transformation.Axis2, new DirectionVector(0, 1, 0));
        DirectionVector axis3 = transformation is IIfcCartesianTransformationOperator3D transformation3D
            ? ToDirection(transformation3D.Axis3, Cross(axis1, axis2))
            : new DirectionVector(0, 0, 1);

        return new XbimMatrix3D(
            axis1.X * scale,
            axis1.Y * scale,
            axis1.Z * scale,
            0,
            axis2.X * scale,
            axis2.Y * scale,
            axis2.Z * scale,
            0,
            axis3.X * scale,
            axis3.Y * scale,
            axis3.Z * scale,
            0,
            ToDouble(transformation.LocalOrigin.X),
            ToDouble(transformation.LocalOrigin.Y),
            ToDouble(transformation.LocalOrigin.Z),
            1);
    }

    private static DirectionVector ToDirection(IIfcDirection? direction, DirectionVector fallback)
    {
        if (direction == null)
            return fallback;

        DirectionVector result = Normalize(new DirectionVector(direction.X, direction.Y, direction.Z));
        return result.IsValid ? result : fallback;
    }

    private static DirectionVector Cross(DirectionVector first, DirectionVector second)
    {
        return Normalize(new DirectionVector(
            first.Y * second.Z - first.Z * second.Y,
            first.Z * second.X - first.X * second.Z,
            first.X * second.Y - first.Y * second.X));
    }

    private static SectionInfo MapSectionInfo(
        IIfcProfileDef? profile,
        double lengthFactor,
        string ifcType,
        List<IfcImportWarning> warnings,
        ImportCandidate candidate)
    {
        if (profile == null)
        {
            warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Section, "SweptSolid has no swept profile."));
            return new SectionInfo { ShapeType = IfcSectionShapeType.Unknown };
        }

        string profileType = profile.GetType().Name;
        string profileName = profile.ProfileName?.ToString() ?? "";
        if (profile is IIfcRectangleProfileDef rectangle)
        {
            return new SectionInfo
            {
                OriginalIfcProfileType = profileType,
                ShapeType = IfcSectionShapeType.Rectangle,
                Width = ToDouble(rectangle.XDim),
                Depth = ToDouble(rectangle.YDim),
                SectionName = BuildRectangleSectionName(ifcType, ToDouble(rectangle.XDim) * lengthFactor, ToDouble(rectangle.YDim) * lengthFactor, profileName)
            };
        }

        if (profile is IIfcCircleProfileDef circle)
        {
            double diameter = ToDouble(circle.Radius) * 2.0;
            return new SectionInfo
            {
                OriginalIfcProfileType = profileType,
                ShapeType = IfcSectionShapeType.Circle,
                Diameter = diameter,
                SectionName = string.IsNullOrWhiteSpace(profileName)
                    ? $"D{ToMillimetreName(diameter * lengthFactor)}"
                    : profileName
            };
        }

        if (profile is IIfcIShapeProfileDef iShape)
        {
            double depth = ToDouble(iShape.OverallDepth);
            double width = ToDouble(iShape.OverallWidth);
            double webThickness = ToDouble(iShape.WebThickness);
            double flangeThickness = ToDouble(iShape.FlangeThickness);
            return new SectionInfo
            {
                OriginalIfcProfileType = profileType,
                ShapeType = IfcSectionShapeType.ISection,
                Depth = depth,
                FlangeWidth = width,
                WebThickness = webThickness,
                FlangeThickness = flangeThickness,
                SectionName = string.IsNullOrWhiteSpace(profileName)
                    ? $"I{ToMillimetreName(depth * lengthFactor)}x{ToMillimetreName(width * lengthFactor)}x{ToMillimetreName(webThickness * lengthFactor)}x{ToMillimetreName(flangeThickness * lengthFactor)}"
                    : profileName
            };
        }

        warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Warning, IfcImportWarningCategory.Section, $"Unsupported IFC profile '{profileType}' was kept as ShapeType.Unknown."));
        return new SectionInfo
        {
            OriginalIfcProfileType = profileType,
            ShapeType = IfcSectionShapeType.Unknown,
            SectionName = string.IsNullOrWhiteSpace(profileName) ? "UNKNOWN_SECTION" : profileName
        };
    }

    private static SectionInfo ConvertSectionUnits(SectionInfo source, double lengthFactor, string ifcType)
    {
        var section = new SectionInfo
        {
            OriginalIfcProfileType = source.OriginalIfcProfileType,
            ShapeType = source.ShapeType,
            Width = source.Width * lengthFactor,
            Depth = source.Depth * lengthFactor,
            Diameter = source.Diameter * lengthFactor,
            FlangeWidth = source.FlangeWidth * lengthFactor,
            FlangeThickness = source.FlangeThickness * lengthFactor,
            WebThickness = source.WebThickness * lengthFactor,
            SectionName = source.SectionName
        };

        if (string.IsNullOrWhiteSpace(section.SectionName))
        {
            section.SectionName = section.ShapeType == IfcSectionShapeType.Rectangle
                ? BuildRectangleSectionName(ifcType, section.Width, section.Depth, "")
                : "UNKNOWN_SECTION";
        }

        return section;
    }

    private static string BuildRectangleSectionName(string ifcType, double widthMetres, double depthMetres, string profileName)
    {
        if (!string.IsNullOrWhiteSpace(profileName))
            return profileName;

        string prefix = ifcType == "IfcColumn" ? "C" : "B";
        return $"{prefix}{ToMillimetreName(widthMetres)}x{ToMillimetreName(depthMetres)}";
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

    private static double ResolveLengthFactor(IfcStore model, IfcImportOptions options, IfcImportResult result)
    {
        if (options.UnitHandling == IfcImportUnitMode.PreserveIfcProjectUnits)
            return 1.0;

        // xbim resolves the authoritative length-to-metre scale from the file's unit
        // definitions when the model is opened. This handles every SI prefix and
        // conversion-based unit (using its real conversion factor), so unusual unit
        // declarations cannot silently fall through to an unscaled import.
        double factor = model.ModelFactors.LengthToMetresConversionFactor;
        if (!double.IsFinite(factor) || factor <= 0)
        {
            result.Warnings.Add(new IfcImportWarning
            {
                Severity = IfcImportWarningSeverity.Warning,
                Category = IfcImportWarningCategory.Unsupported,
                Message = "IFC project length unit could not be resolved; coordinates were preserved as source units."
            });
            return 1.0;
        }

        return factor;
    }

    // Guards against unit-scale errors: after import, a model whose members are far
    // outside any plausible structural range almost always means the length unit was
    // misread. Flag it loudly so a wrong-scale import is never accepted silently.
    private static void AddScaleSanityWarning(IfcImportResult result, double lengthFactor)
    {
        if (result.Frames.Count == 0)
            return;

        double averageLength = result.Frames
            .Select(frame => Distance(frame.StartPoint, frame.EndPoint))
            .Average();

        const double minimumPlausibleLength = 0.05;   // 50 mm
        const double maximumPlausibleLength = 500.0;   // 500 m
        if (averageLength is >= minimumPlausibleLength and <= maximumPlausibleLength)
            return;

        result.Warnings.Add(new IfcImportWarning
        {
            Severity = IfcImportWarningSeverity.Error,
            Category = IfcImportWarningCategory.Geometry,
            Message = $"Imported geometry scale looks wrong: average member length is {averageLength:0.###} m " +
                $"(length unit factor {lengthFactor:0.######}). Check the IFC length unit or the unit-handling option before exporting."
        });
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

    private static double[] ToCoordinateArray(System.Collections.IEnumerable coordinates)
    {
        return coordinates
            .Cast<object>()
            .Select(ToDouble)
            .ToArray();
    }

    private static string ToMillimetreName(double metres)
    {
        return Math.Round(metres * 1000.0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
    }

    private static double Distance(AnalyticalPoint first, AnalyticalPoint second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static DirectionVector Normalize(DirectionVector direction)
    {
        double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
        if (!double.IsFinite(length) || length <= ZeroLengthTolerance)
            return direction with { IsValid = false };

        return new DirectionVector(direction.X / length, direction.Y / length, direction.Z / length, true);
    }

    private static IfcImportWarning BuildWarning(
        ImportCandidate candidate,
        IfcImportWarningSeverity severity,
        IfcImportWarningCategory category,
        string message)
    {
        return new IfcImportWarning
        {
            SourceGuid = GetGuid(candidate.Product),
            SourceName = GetName(candidate.Product),
            Severity = severity,
            Category = category,
            Message = message
        };
    }

    private sealed record ImportCandidate(IIfcProduct Product, string IfcType);

    private sealed record SweptSolidMatch(IIfcExtrudedAreaSolid Solid, XbimMatrix3D Placement);

    private readonly record struct DirectionVector(double X, double Y, double Z, bool IsValid = true);
}
