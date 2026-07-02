using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.IO;
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
    private readonly AreaMeshRecoveryService _areaMeshRecoveryService = new();
    private readonly AdvancedFrameGeometryRecognitionService _advancedFrameGeometryRecognitionService = new();
    private readonly AnalyticalFrameConditioningService _frameConditioningService = new();

    // Memoizes resolved placement matrices by placement entity label within one import,
    // so shared storey/building/site placements are not re-walked for every element.
    // Entity labels are per-model, so this is cleared at the start of each import.
    private readonly Dictionary<int, XbimMatrix3D> _placementCache = new();

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
        _placementCache.Clear();
        using IfcStore model = OpenModel(ifcPath, progress);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new IfcImportProgress(0, "Reading model data...", false));
        double lengthFactor = ResolveLengthFactor(model, options, result);
        IReadOnlyList<IfcStoreyInfo> storeys = _storeyDetector.ReadStoreys(model, lengthFactor);
        result.StoreyLevels = storeys
            .Select(storey => new IfcStoreyLevel { Name = storey.Name, Elevation = storey.Elevation })
            .ToList();
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
            try
            {
                ImportFrameCandidate(candidate, lengthFactor, storeys, options, result);
            }
            catch (Exception ex)
            {
                RecordCandidateError(candidate, result, ex);
            }

            processed++;
            ReportRecognition($"Recognising elements {processed}/{total}");
        }

        foreach (ImportCandidate candidate in areaCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ImportAreaCandidate(candidate, lengthFactor, storeys, options, result);
            }
            catch (Exception ex)
            {
                RecordCandidateError(candidate, result, ex);
            }

            processed++;
            ReportRecognition($"Recognising elements {processed}/{total}");
        }

        progress?.Report(new IfcImportProgress(96, "Cleaning up and validating...", true));
        if (options.ApplyFrameConditioning)
            result.Warnings.AddRange(_frameConditioningService.ConditionFrames(result.Frames, options.FrameConditioningMergeTolerance));
        AddScaleSanityWarning(result, lengthFactor);
        result.Warnings.AddRange(_coordinateOriginService.ApplyCoordinateOriginReset(result, options));
        List<IfcImportWarning> snapWarnings = _nodeSnapper.SnapFrameEndpoints(result.Frames, options.NodeSnapTolerance);
        result.Warnings.AddRange(snapWarnings);
        result.CleanupActions.AddRange(snapWarnings.Select(warning => warning.Message));
        result.Warnings.AddRange(_duplicateFrameDetector.DetectDuplicates(result.Frames, options.DuplicateFrameTolerance, options.DuplicateSectionTolerance));
        result.Warnings.AddRange(_shortMemberDetector.DetectShortMembers(result.Frames, options.ShortMemberMinimumLength));
        result.Warnings.AddRange(_connectivityChecker.CheckBeamEndpointConnectivity(result.Frames, options.ConnectivityTolerance));

        // Derive ETABS story levels from the final geometry (slab/beam elevations). The IFC
        // building storeys are unreliable here (incomplete for towers), so geometry levels are
        // used for the story system and IFC storey names are borrowed where they line up.
        result.StoreyLevels = DeriveStoryLevels(result, result.StoreyLevels);

        // Snap floor elements to their story elevation so the slabs and beams at each level are
        // coplanar — the condition ETABS needs to mesh the slab onto the beams (the gravity load
        // path). Columns still span level-to-level and tie the floors vertically.
        if (options.ApplyFrameConditioning)
            SnapToWorkPlanes(result);
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

    // A single malformed element must never abort the whole import; record it as a skip
    // with the error so the rest of the model still comes through.
    private static void RecordCandidateError(ImportCandidate candidate, IfcImportResult result, Exception ex)
    {
        string reason = $"Element processing failed: {ex.Message}";
        result.SkippedElements.Add(new SkippedIfcElement
        {
            SourceGuid = GetGuid(candidate.Product),
            SourceName = GetName(candidate.Product),
            IfcType = candidate.IfcType,
            Reason = reason
        });
        result.Warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Error, IfcImportWarningCategory.Geometry, reason));
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

        // Members exported as a triangle mesh instead of an extrusion (e.g. notched wall
        // columns) have no Axis or SweptSolid. Recover them from the mesh bounding box so
        // they are not silently dropped; they are flagged low-confidence for review.
        if (options.RecoverMeshGeometry)
        {
            AdvancedFrameGeometryRecognitionResult meshResult = _advancedFrameGeometryRecognitionService.TryRecoverPrismaticMember(candidate.Product, candidate.IfcType, lengthFactor);
            localWarnings.AddRange(meshResult.Warnings);
            if (meshResult.Frame != null)
            {
                FinalizeFrame(meshResult.Frame, candidate, lengthFactor, storeys, options, result, localWarnings);
                return;
            }
        }

        string reason = options.EnableAdvancedGeometryRecognition || options.RecoverMeshGeometry
            ? "No usable Axis, SweptSolid, or recoverable mesh representation found."
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

        // Walls and slabs exported as a triangle mesh instead of an extrusion (~39% of walls in
        // this project) have no SweptSolid, so recognition returns nothing. Recover their
        // analytical surface from the mesh so they are not silently dropped.
        if (areaResult.Area == null && options.RecoverMeshGeometry)
        {
            AreaRecognitionResult meshResult = _areaMeshRecoveryService.TryRecoverArea(candidate.Product, candidate.IfcType, lengthFactor);
            result.Warnings.AddRange(meshResult.Warnings);
            if (meshResult.Area != null)
                areaResult = meshResult;
        }

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

        // Cull non-structural walls so facade cladding is not modelled as shear walls (which
        // over-stiffens the building). Prefer the IFC's own LoadBearing/facade metadata; fall
        // back to thickness only when those properties are absent.
        if (options.StructuralWallsOnly
            && string.Equals(area.IfcType, "IfcWall", StringComparison.OrdinalIgnoreCase))
        {
            bool? structural = WallStructuralClassifier.IsStructural(candidate.Product);
            string? excludeReason = structural switch
            {
                false => "Non-structural wall excluded by IFC metadata (not LoadBearing, or flagged as facade cladding).",
                null when area.Thickness > 0 && area.Thickness < options.MinimumStructuralWallThickness
                    => $"Non-structural wall excluded: thickness {area.Thickness * 1000.0:0} mm is below the {options.MinimumStructuralWallThickness * 1000.0:0} mm structural threshold.",
                _ => null
            };

            if (excludeReason != null)
            {
                result.SkippedElements.Add(new SkippedIfcElement
                {
                    SourceGuid = GetGuid(candidate.Product),
                    SourceName = GetName(candidate.Product),
                    IfcType = candidate.IfcType,
                    Reason = excludeReason
                });
                result.Warnings.AddRange(areaResult.Warnings);
                return;
            }
        }
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

    private bool TryCreateFromAxis(
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

    private bool TryCreateFromSweptSolid(
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

    private XbimMatrix3D ResolvePlacement(
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

        if (_placementCache.TryGetValue(localPlacement.EntityLabel, out XbimMatrix3D cached))
            return cached;

        XbimMatrix3D relative = localPlacement.RelativePlacement == null
            ? new XbimMatrix3D()
            : Xbim.Ifc.IIfcAxis2PlacementExtensions.ToMatrix3D(localPlacement.RelativePlacement);
        XbimMatrix3D result = localPlacement.PlacementRelTo == null
            ? relative
            : XbimMatrix3D.Multiply(relative, ResolvePlacement(localPlacement.PlacementRelTo, warnings, candidate));

        _placementCache[localPlacement.EntityLabel] = result;
        return result;
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

        // Precast/arbitrary profiles (very common from Revit) carry their real outline as a
        // polyline. Read its bounding box for true width x depth instead of leaving the
        // section Unknown and guessing from the name later. Genuinely shaped sections
        // (L, T, ledge beams) are approximated by their bounding box and flagged.
        if (profile is IIfcArbitraryClosedProfileDef arbitrary &&
            TryProfileBoundingBox(arbitrary.OuterCurve, out double profileWidth, out double profileDepth, out int cornerCount) &&
            profileWidth > 0 && profileDepth > 0)
        {
            if (cornerCount > 5)
            {
                warnings.Add(BuildWarning(candidate, IfcImportWarningSeverity.Info, IfcImportWarningCategory.Section,
                    $"Non-rectangular profile '{profileType}' ({cornerCount} corners) approximated by its bounding box " +
                    $"{ToMillimetreName(profileWidth * lengthFactor)}x{ToMillimetreName(profileDepth * lengthFactor)} mm; verify section."));
            }

            return new SectionInfo
            {
                OriginalIfcProfileType = profileType,
                ShapeType = IfcSectionShapeType.Rectangle,
                Width = profileWidth,
                Depth = profileDepth,
                SectionName = BuildRectangleSectionName(ifcType, profileWidth * lengthFactor, profileDepth * lengthFactor, profileName)
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

    private static bool TryProfileBoundingBox(IIfcCurve? curve, out double width, out double depth, out int cornerCount)
    {
        width = 0;
        depth = 0;
        cornerCount = 0;

        List<(double X, double Y)> points = ExtractProfilePoints(curve);
        if (points.Count < 3)
            return false;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach ((double x, double y) in points)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        width = maxX - minX;
        depth = maxY - minY;
        cornerCount = points.Count;
        return double.IsFinite(width) && double.IsFinite(depth);
    }

    private static List<(double X, double Y)> ExtractProfilePoints(IIfcCurve? curve)
    {
        var points = new List<(double X, double Y)>();
        switch (curve)
        {
            case IIfcPolyline polyline:
                foreach (IIfcCartesianPoint point in polyline.Points)
                    points.Add((ToDouble(point.X), ToDouble(point.Y)));
                break;

            case IIfcIndexedPolyCurve indexed when indexed.Points is IIfcCartesianPointList2D list2D:
                foreach (System.Collections.IEnumerable coordinate in list2D.CoordList)
                {
                    double[] values = coordinate.Cast<object>().Select(ToDouble).ToArray();
                    if (values.Length >= 2)
                        points.Add((values[0], values[1]));
                }

                break;

            case IIfcIndexedPolyCurve indexed when indexed.Points is IIfcCartesianPointList3D list3D:
                foreach (System.Collections.IEnumerable coordinate in list3D.CoordList)
                {
                    double[] values = coordinate.Cast<object>().Select(ToDouble).ToArray();
                    if (values.Length >= 2)
                        points.Add((values[0], values[1]));
                }

                break;
        }

        return points;
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
    // Clusters the elevations of horizontal members (slabs and beams) into floor levels for
    // the ETABS story system, since these mark where the structure actually is. IFC storey
    // names are reused when a level lands near one; otherwise a level is named generically.
    private static List<IfcStoreyLevel> DeriveStoryLevels(IfcImportResult result, IReadOnlyList<IfcStoreyLevel> ifcStoreys)
    {
        var candidates = new List<double>();
        foreach (AnalyticalAreaElement area in result.Areas)
        {
            if (area.BoundaryPoints.Count > 0)
                candidates.Add(area.BoundaryPoints.Average(point => point.Z));
        }

        foreach (AnalyticalFrameElement frame in result.Frames)
        {
            double length = Distance(frame.StartPoint, frame.EndPoint);
            double verticalDrop = Math.Abs(frame.EndPoint.Z - frame.StartPoint.Z);
            if (length > ZeroLengthTolerance && verticalDrop / length < 0.30)
                candidates.Add((frame.StartPoint.Z + frame.EndPoint.Z) / 2.0);
        }

        if (candidates.Count == 0)
            return [];

        candidates.Sort();

        // Greedy clustering: sub-floor noise (<~1.5 m spread) merges; real ~3 m floor gaps split.
        const double mergeWindowMetres = 1.5;
        var clusters = new List<(double Sum, int Count)>();
        foreach (double z in candidates)
        {
            if (clusters.Count > 0)
            {
                (double sum, int count) = clusters[^1];
                if (z - sum / count <= mergeWindowMetres)
                {
                    clusters[^1] = (sum + z, count + 1);
                    continue;
                }
            }

            clusters.Add((z, 1));
        }

        int supportThreshold = Math.Max(10, candidates.Count / 400);
        List<double> levels = clusters
            .Where(cluster => cluster.Count >= supportThreshold)
            .Select(cluster => cluster.Sum / cluster.Count)
            .OrderBy(z => z)
            .ToList();
        if (levels.Count < 2)
            return [];

        // Anchor the base at the true bottom of the structure (e.g. column bases), which sits
        // below the lowest floor level, so nothing ends up beneath the lowest ETABS story.
        double minZ = MinimumGeometryElevation(result);
        if (double.IsFinite(minZ) && minZ < levels[0] - 0.5)
            levels.Insert(0, minZ);

        var storeyLevels = new List<IfcStoreyLevel>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < levels.Count; i++)
        {
            string name = NearestStoreyName(ifcStoreys, levels[i], 1.0) ?? $"Level {i + 1}";
            string unique = name;
            int suffix = 2;
            while (!usedNames.Add(unique))
                unique = $"{name}_{suffix++}";

            storeyLevels.Add(new IfcStoreyLevel { Name = unique, Elevation = levels[i] });
        }

        return storeyLevels;
    }

    private static void SnapToWorkPlanes(IfcImportResult result)
    {
        if (result.StoreyLevels.Count == 0)
            return;

        double[] elevations = result.StoreyLevels
            .Select(level => level.Elevation)
            .OrderBy(z => z)
            .ToArray();
        const double snapTolerance = 1.2;   // half a typical storey height — catches floor spread, not mid-height

        foreach (AnalyticalFrameElement frame in result.Frames)
        {
            SnapPointToLevel(frame.StartPoint, elevations, snapTolerance);
            SnapPointToLevel(frame.EndPoint, elevations, snapTolerance);
        }

        foreach (AnalyticalAreaElement area in result.Areas)
        {
            foreach (AnalyticalPoint point in area.BoundaryPoints)
                SnapPointToLevel(point, elevations, snapTolerance);
        }
    }

    private static void SnapPointToLevel(AnalyticalPoint point, double[] elevations, double tolerance)
    {
        double best = double.NaN;
        double bestDistance = tolerance;
        foreach (double elevation in elevations)
        {
            double distance = Math.Abs(elevation - point.Z);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = elevation;
            }
        }

        if (!double.IsNaN(best))
            point.Z = best;
    }

    private static double MinimumGeometryElevation(IfcImportResult result)
    {
        double minZ = double.PositiveInfinity;
        foreach (AnalyticalFrameElement frame in result.Frames)
        {
            minZ = Math.Min(minZ, frame.StartPoint.Z);
            minZ = Math.Min(minZ, frame.EndPoint.Z);
        }

        foreach (AnalyticalAreaElement area in result.Areas)
        {
            foreach (AnalyticalPoint point in area.BoundaryPoints)
                minZ = Math.Min(minZ, point.Z);
        }

        return minZ;
    }

    private static string? NearestStoreyName(IReadOnlyList<IfcStoreyLevel> storeys, double elevation, double tolerance)
    {
        string? best = null;
        double bestDistance = tolerance;
        foreach (IfcStoreyLevel storey in storeys)
        {
            double distance = Math.Abs(storey.Elevation - elevation);
            if (distance <= bestDistance && !string.IsNullOrWhiteSpace(storey.Name))
            {
                bestDistance = distance;
                best = storey.Name;
            }
        }

        return best;
    }

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

    // xbim keeps the model in RAM when the IFC size (MB) is at or below the threshold we
    // pass, otherwise it builds a slower disk-backed database. We size the threshold to the
    // machine's free memory, so large models open in memory where RAM allows and fall back
    // to the disk model on low-memory machines instead of failing.
    private const double InMemoryExpansionFactor = 8.0;   // in-memory model ~ 8x IFC text size
    private const double InMemorySafetyFraction = 0.5;    // never budget more than half of free RAM
    private const double MinimumInMemoryThresholdMb = 50.0;

    private static IfcStore OpenModel(string ifcPath, IProgress<IfcImportProgress>? progress)
    {
        double fileMb = new FileInfo(ifcPath).Length / (1024.0 * 1024.0);
        double maxInMemoryMb = ComputeInMemoryThresholdMb();
        bool inMemory = fileMb <= maxInMemoryMb;
        string mode = inMemory ? "in memory" : "disk-backed, low memory";

        progress?.Report(new IfcImportProgress(0, $"Opening IFC file ({mode})...", false));
        ReportProgressDelegate reportProgress = (percent, _) =>
            progress?.Report(new IfcImportProgress(0, $"Opening IFC file ({mode})... {percent}%", false));

        double xbimThresholdMb = inMemory ? fileMb + 1.0 : 0.0;
        return IfcStore.Open(ifcPath, null, xbimThresholdMb, reportProgress, XbimDBAccess.Read);
    }

    private static double ComputeInMemoryThresholdMb()
    {
        ulong availableBytes = GetAvailablePhysicalMemoryBytes();
        if (availableBytes == 0)
            return MinimumInMemoryThresholdMb;

        double budgetMb = availableBytes * InMemorySafetyFraction / InMemoryExpansionFactor / (1024.0 * 1024.0);
        return Math.Max(budgetMb, MinimumInMemoryThresholdMb);
    }

    private static ulong GetAvailablePhysicalMemoryBytes()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? status.ullAvailPhys : 0;
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private sealed record ImportCandidate(IIfcProduct Product, string IfcType);

    private sealed record SweptSolidMatch(IIfcExtrudedAreaSolid Solid, XbimMatrix3D Placement);

    private readonly record struct DirectionVector(double X, double Y, double Z, bool IsValid = true);
}
