using System.Globalization;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class ModelCompareService
{
    private const long MaxMovedFrameCandidateChecks = 250_000;

    public ModelCompareComparisonResult CompareSnapshots(
        ModelCompareSnapshot oldSnapshot,
        ModelCompareSnapshot newSnapshot,
        ModelCompareToleranceSettings? tolerances = null)
    {
        ModelCompareToleranceSettings settings = NormalizeTolerances(tolerances);
        var results = new List<ModelCompareResultRow>();
        var comparison = new ModelCompareComparisonResult();
        bool framePropertyDataAvailable = CanCompareCategory(
            oldSnapshot.Metadata.FramePropertiesReadStatus,
            newSnapshot.Metadata.FramePropertiesReadStatus);
        bool areaPropertyDataAvailable = CanCompareCategory(
            oldSnapshot.Metadata.AreaPropertiesReadStatus,
            newSnapshot.Metadata.AreaPropertiesReadStatus);

        if (CanCompareCategory(oldSnapshot.Metadata.FramesReadStatus, newSnapshot.Metadata.FramesReadStatus))
        {
            CompareFrames(oldSnapshot.Frames, newSnapshot.Frames, settings, framePropertyDataAvailable, results, comparison.Warnings);
            AddReadWarningIfNeeded("Frame", oldSnapshot.Metadata.FramesReadStatus, newSnapshot.Metadata.FramesReadStatus, comparison.Warnings);
            if (!framePropertyDataAvailable)
                comparison.Warnings.Add("Frame material assignment comparison was skipped because frame property completeness is unavailable.");
        }
        else
        {
            comparison.FrameComparisonAvailable = false;
            comparison.Errors.Add(BuildUnavailableCategoryMessage(
                "Frame",
                oldSnapshot.Metadata.FramesReadStatus,
                newSnapshot.Metadata.FramesReadStatus));
        }

        CompareOptionalCategory(
            "Area",
            oldSnapshot.Metadata.AreasReadStatus,
            newSnapshot.Metadata.AreasReadStatus,
            comparison,
            () =>
            {
                CompareAreas(oldSnapshot.Areas, newSnapshot.Areas, settings, areaPropertyDataAvailable, results);
                if (!areaPropertyDataAvailable)
                    comparison.Warnings.Add("Area material and thickness comparison was skipped because area property completeness is unavailable.");
            });
        CompareOptionalCategory(
            "Frame property",
            oldSnapshot.Metadata.FramePropertiesReadStatus,
            newSnapshot.Metadata.FramePropertiesReadStatus,
            comparison,
            () => CompareFrameProperties(oldSnapshot.FrameProperties, newSnapshot.FrameProperties, settings, results));
        CompareOptionalCategory(
            "Area property",
            oldSnapshot.Metadata.AreaPropertiesReadStatus,
            newSnapshot.Metadata.AreaPropertiesReadStatus,
            comparison,
            () => CompareAreaProperties(oldSnapshot.AreaProperties, newSnapshot.AreaProperties, settings, results));
        CompareOptionalCategory(
            "Material",
            oldSnapshot.Metadata.MaterialsReadStatus,
            newSnapshot.Metadata.MaterialsReadStatus,
            comparison,
            () => CompareMaterials(oldSnapshot.Materials, newSnapshot.Materials, settings, results));

        comparison.Differences = results
            .OrderBy(row => row.ObjectType)
            .ThenBy(row => row.ObjectDescription, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.OldValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.NewValue, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return comparison;
    }

    private static void CompareOptionalCategory(
        string categoryName,
        ModelCompareSnapshotReadStatus oldStatus,
        ModelCompareSnapshotReadStatus newStatus,
        ModelCompareComparisonResult comparison,
        Action compare)
    {
        if (!CanCompareCategory(oldStatus, newStatus))
        {
            comparison.Warnings.Add(BuildUnavailableCategoryMessage(categoryName, oldStatus, newStatus));
            return;
        }

        compare();
        AddReadWarningIfNeeded(categoryName, oldStatus, newStatus, comparison.Warnings);
    }

    private static bool CanCompareCategory(
        ModelCompareSnapshotReadStatus oldStatus,
        ModelCompareSnapshotReadStatus newStatus)
    {
        return IsComparableStatus(oldStatus) && IsComparableStatus(newStatus);
    }

    private static bool IsComparableStatus(ModelCompareSnapshotReadStatus status)
    {
        return status is ModelCompareSnapshotReadStatus.Success or ModelCompareSnapshotReadStatus.SuccessWithWarnings;
    }

    private static void AddReadWarningIfNeeded(
        string categoryName,
        ModelCompareSnapshotReadStatus oldStatus,
        ModelCompareSnapshotReadStatus newStatus,
        List<string> warnings)
    {
        if (oldStatus == ModelCompareSnapshotReadStatus.SuccessWithWarnings ||
            newStatus == ModelCompareSnapshotReadStatus.SuccessWithWarnings)
        {
            warnings.Add($"{categoryName} comparison continued with extraction warnings. Review the snapshot warnings before accepting the results.");
        }
    }

    private static string BuildUnavailableCategoryMessage(
        string categoryName,
        ModelCompareSnapshotReadStatus oldStatus,
        ModelCompareSnapshotReadStatus newStatus)
    {
        return $"{categoryName} comparison is unavailable because snapshot data is incomplete (old status: {oldStatus}; new status: {newStatus}). No {categoryName.ToLowerInvariant()} added/deleted results were generated.";
    }

    private static void CompareFrames(
        IReadOnlyList<ModelCompareFrameSnapshot> oldFrames,
        IReadOnlyList<ModelCompareFrameSnapshot> newFrames,
        ModelCompareToleranceSettings settings,
        bool compareMaterialAssignments,
        List<ModelCompareResultRow> results,
        List<string> warnings)
    {
        List<ModelCompareFrameSnapshot> orderedOldFrames = oldFrames
            .OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<ModelCompareFrameSnapshot> orderedNewFrames = newFrames
            .OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Dictionary<FramePointBucket, List<int>> newFramesByEndpoint = BuildFrameEndpointLookup(
            orderedNewFrames,
            settings.CoordinateTolerance);
        var matchedOldIndexes = new HashSet<int>();
        var matchedNewIndexes = new HashSet<int>();

        for (int oldIndex = 0; oldIndex < orderedOldFrames.Count; oldIndex++)
        {
            ModelCompareFrameSnapshot oldFrame = orderedOldFrames[oldIndex];
            ModelCompareFrameSnapshot? newFrame = null;
            ModelCompareMatchMethod matchMethod = ModelCompareMatchMethod.Unmatched;
            int matchedNewIndex = -1;
            foreach (int newIndex in GetExactFrameCandidateIndexes(oldFrame, newFramesByEndpoint, settings.CoordinateTolerance))
            {
                if (matchedNewIndexes.Contains(newIndex))
                    continue;

                ModelCompareFrameSnapshot candidate = orderedNewFrames[newIndex];
                ModelCompareMatchMethod candidateMethod = GetExactFrameMatchMethod(oldFrame, candidate, settings.CoordinateTolerance);
                if (candidateMethod == ModelCompareMatchMethod.Unmatched)
                    continue;

                newFrame = candidate;
                matchMethod = candidateMethod;
                matchedNewIndex = newIndex;
                break;
            }

            if (newFrame == null)
                continue;

            matchedOldIndexes.Add(oldIndex);
            matchedNewIndexes.Add(matchedNewIndex);
            int resultStart = results.Count;
            CompareMatchedFrame(oldFrame, newFrame, settings, compareMaterialAssignments, results);
            ApplyDiagnostics(results, resultStart, BuildFrameDiagnostics(
                oldFrame,
                newFrame,
                matchMethod,
                ModelCompareConfidenceLevel.High,
                matchMethod == ModelCompareMatchMethod.ReversedIJ
                    ? $"Frame endpoints match within {FormatNumber(settings.CoordinateTolerance)} after reversing I and J."
                    : $"Frame I and J endpoints match within {FormatNumber(settings.CoordinateTolerance)}."));
            ApplySearchContext(results, resultStart, BuildFrameSearchText(oldFrame, newFrame));
            ApplyFrameNavigationContext(results, resultStart, oldFrame, newFrame);
        }

        List<ModelCompareFrameSnapshot> unmatchedOldFrames = orderedOldFrames
            .Where((_, index) => !matchedOldIndexes.Contains(index))
            .ToList();
        List<ModelCompareFrameSnapshot> unmatchedNewFrames = orderedNewFrames
            .Where((_, index) => !matchedNewIndexes.Contains(index))
            .ToList();

        Dictionary<string, Queue<int>> unmatchedNewFramesByName = BuildFrameNameLookup(unmatchedNewFrames);
        var sameNameMatchedOldIndexes = new HashSet<int>();
        var sameNameMatchedNewIndexes = new HashSet<int>();
        for (int oldIndex = 0; oldIndex < unmatchedOldFrames.Count; oldIndex++)
        {
            ModelCompareFrameSnapshot oldFrame = unmatchedOldFrames[oldIndex];
            string oldFrameName = (oldFrame.FrameName ?? "").Trim();
            if (oldFrameName.Length == 0 ||
                !unmatchedNewFramesByName.TryGetValue(oldFrameName, out Queue<int>? matchingIndexes) ||
                matchingIndexes.Count == 0)
            {
                continue;
            }

            int newIndex = matchingIndexes.Dequeue();
            ModelCompareFrameSnapshot newFrame = unmatchedNewFrames[newIndex];
            sameNameMatchedOldIndexes.Add(oldIndex);
            sameNameMatchedNewIndexes.Add(newIndex);
            int resultStart = results.Count;
            AddFrameMovedDifference(oldFrame, newFrame, settings, ModelCompareConfidenceLevel.High, results);
            CompareMatchedFrame(oldFrame, newFrame, settings, compareMaterialAssignments, results);
            ApplyDiagnostics(results, resultStart, BuildFrameDiagnostics(
                oldFrame,
                newFrame,
                ModelCompareMatchMethod.SameFrameName,
                ModelCompareConfidenceLevel.High,
                "Exact geometry did not match, but the ETABS frame name is unchanged.",
                CalculateMidpointMovement(oldFrame, newFrame) <= settings.MovementTolerance ? 0.95 : 0.9));
            ApplySearchContext(results, resultStart, BuildFrameSearchText(oldFrame, newFrame));
            ApplyFrameNavigationContext(results, resultStart, oldFrame, newFrame);
        }

        unmatchedOldFrames = unmatchedOldFrames
            .Where((_, index) => !sameNameMatchedOldIndexes.Contains(index))
            .ToList();
        unmatchedNewFrames = unmatchedNewFrames
            .Where((_, index) => !sameNameMatchedNewIndexes.Contains(index))
            .ToList();

        long movedCandidateChecks = (long)unmatchedOldFrames.Count * unmatchedNewFrames.Count;
        bool movedMatchingSkipped = movedCandidateChecks > MaxMovedFrameCandidateChecks;
        if (movedMatchingSkipped)
        {
            warnings.Add(
                $"Near-geometry moved-frame matching was skipped because {unmatchedOldFrames.Count} unmatched old frames x " +
                $"{unmatchedNewFrames.Count} unmatched new frames would require {movedCandidateChecks.ToString("N0", CultureInfo.InvariantCulture)} candidate checks, " +
                $"exceeding the safety limit of {MaxMovedFrameCandidateChecks.ToString("N0", CultureInfo.InvariantCulture)}. Remaining unmatched frames are reported as added/deleted.");
        }
        else
        {
            MatchLikelyMovedFrames(unmatchedOldFrames, unmatchedNewFrames, settings, compareMaterialAssignments, results);
        }

        string unmatchedOldReason = movedMatchingSkipped
            ? "No exact-coordinate or same-name candidate was found; near-geometry matching was skipped by the candidate safety limit."
            : "No exact-coordinate, same-name, or unique near-geometry candidate was found in the new snapshot.";
        string unmatchedNewReason = movedMatchingSkipped
            ? "No exact-coordinate or same-name candidate was found; near-geometry matching was skipped by the candidate safety limit."
            : "No exact-coordinate, same-name, or unique near-geometry candidate was found in the old snapshot.";

        foreach (ModelCompareFrameSnapshot oldFrame in unmatchedOldFrames)
        {
            int resultStart = results.Count;
            results.Add(BuildResult(
                ModelCompareChangeType.Removed,
                ModelCompareObjectType.Frame,
                DescribeFrame(oldFrame),
                oldFrame.FrameName,
                "",
                ModelCompareChangeImportance.High,
                diagnostics: BuildUnmatchedDiagnostics(unmatchedOldReason),
                searchText: BuildFrameSearchText(oldFrame, null)));
            ApplyFrameNavigationContext(results, resultStart, oldFrame, null);
        }

        foreach (ModelCompareFrameSnapshot newFrame in unmatchedNewFrames)
        {
            int resultStart = results.Count;
            results.Add(BuildResult(
                ModelCompareChangeType.Added,
                ModelCompareObjectType.Frame,
                DescribeFrame(newFrame),
                "",
                newFrame.FrameName,
                ModelCompareChangeImportance.High,
                diagnostics: BuildUnmatchedDiagnostics(unmatchedNewReason),
                searchText: BuildFrameSearchText(null, newFrame)));
            ApplyFrameNavigationContext(results, resultStart, null, newFrame);
        }
    }

    private static void CompareMatchedFrame(
        ModelCompareFrameSnapshot oldFrame,
        ModelCompareFrameSnapshot newFrame,
        ModelCompareToleranceSettings settings,
        bool compareMaterialAssignments,
        List<ModelCompareResultRow> results)
    {
        string description = $"{DescribeFrame(newFrame)} matched to {oldFrame.FrameName}";

        AddStringDifference(
            results,
            ModelCompareObjectType.Frame,
            $"{description} / section",
            oldFrame.SectionName,
            newFrame.SectionName,
            ModelCompareChangeImportance.High);

        if (compareMaterialAssignments)
        {
            AddStringDifference(
                results,
                ModelCompareObjectType.Frame,
                $"{description} / material",
                oldFrame.MaterialName,
                newFrame.MaterialName,
                ModelCompareChangeImportance.Medium);
        }

        AddNumericDifference(
            results,
            ModelCompareObjectType.Frame,
            $"{description} / length",
            oldFrame.Length,
            newFrame.Length,
            settings.LengthTolerance,
            ModelCompareChangeImportance.Medium);
    }

    private static void CompareAreas(
        IReadOnlyList<ModelCompareAreaObjectSnapshot> oldAreas,
        IReadOnlyList<ModelCompareAreaObjectSnapshot> newAreas,
        ModelCompareToleranceSettings settings,
        bool comparePropertyDetails,
        List<ModelCompareResultRow> results)
    {
        Dictionary<string, Queue<ModelCompareAreaObjectSnapshot>> oldAreasByKey = BuildAreaLookup(oldAreas, settings.CoordinateTolerance);
        Dictionary<string, Queue<ModelCompareAreaObjectSnapshot>> newAreasByKey = BuildAreaLookup(newAreas, settings.CoordinateTolerance);
        var allKeys = new SortedSet<string>(oldAreasByKey.Keys, StringComparer.Ordinal);
        allKeys.UnionWith(newAreasByKey.Keys);

        foreach (string key in allKeys)
        {
            oldAreasByKey.TryGetValue(key, out Queue<ModelCompareAreaObjectSnapshot>? oldQueue);
            newAreasByKey.TryGetValue(key, out Queue<ModelCompareAreaObjectSnapshot>? newQueue);

            while ((oldQueue?.Count ?? 0) > 0 && (newQueue?.Count ?? 0) > 0)
            {
                ModelCompareAreaObjectSnapshot oldArea = oldQueue!.Dequeue();
                ModelCompareAreaObjectSnapshot newArea = newQueue!.Dequeue();
                int resultStart = results.Count;
                CompareMatchedArea(oldArea, newArea, settings, comparePropertyDetails, results);
                ApplyDiagnostics(results, resultStart, new ComparisonDiagnostics(
                    ModelCompareMatchMethod.ExactAreaGeometry,
                    ModelCompareConfidenceLevel.High,
                    1.0,
                    $"Sorted area corner coordinates match within {FormatNumber(settings.CoordinateTolerance)}.",
                    null,
                    null,
                    null,
                    null));
                ApplySearchContext(results, resultStart, BuildAreaSearchText(oldArea, newArea));
                ApplyAreaNavigationContext(results, resultStart, oldArea, newArea);
            }

            while ((oldQueue?.Count ?? 0) > 0)
            {
                ModelCompareAreaObjectSnapshot oldArea = oldQueue!.Dequeue();
                int resultStart = results.Count;
                results.Add(BuildResult(
                    ModelCompareChangeType.Removed,
                    ModelCompareObjectType.Area,
                    DescribeArea(oldArea),
                    oldArea.AreaName,
                    "",
                    ModelCompareChangeImportance.High,
                    diagnostics: BuildUnmatchedDiagnostics("No area with the same stable corner geometry was found in the new snapshot."),
                    searchText: BuildAreaSearchText(oldArea, null)));
                ApplyAreaNavigationContext(results, resultStart, oldArea, null);
            }

            while ((newQueue?.Count ?? 0) > 0)
            {
                ModelCompareAreaObjectSnapshot newArea = newQueue!.Dequeue();
                int resultStart = results.Count;
                results.Add(BuildResult(
                    ModelCompareChangeType.Added,
                    ModelCompareObjectType.Area,
                    DescribeArea(newArea),
                    "",
                    newArea.AreaName,
                    ModelCompareChangeImportance.High,
                    diagnostics: BuildUnmatchedDiagnostics("No area with the same stable corner geometry was found in the old snapshot."),
                    searchText: BuildAreaSearchText(null, newArea)));
                ApplyAreaNavigationContext(results, resultStart, null, newArea);
            }
        }
    }

    private static void CompareMatchedArea(
        ModelCompareAreaObjectSnapshot oldArea,
        ModelCompareAreaObjectSnapshot newArea,
        ModelCompareToleranceSettings settings,
        bool comparePropertyDetails,
        List<ModelCompareResultRow> results)
    {
        string description = $"{DescribeArea(newArea)} matched to {oldArea.AreaName}";
        bool propertyChanged = !string.Equals(
            (oldArea.PropertyName ?? "").Trim(),
            (newArea.PropertyName ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase);

        if (propertyChanged)
        {
            results.Add(BuildResult(
                ModelCompareChangeType.Modified,
                ModelCompareObjectType.Area,
                $"{description} / property",
                FormatAreaPropertyAssignment(oldArea),
                FormatAreaPropertyAssignment(newArea),
                ModelCompareChangeImportance.High));
            return;
        }

        if (!comparePropertyDetails)
            return;

        AddStringDifference(
            results,
            ModelCompareObjectType.Area,
            $"{description} / material",
            oldArea.MaterialName,
            newArea.MaterialName,
            ModelCompareChangeImportance.Medium);

        AddNumericDifference(
            results,
            ModelCompareObjectType.Area,
            $"{description} / thickness",
            oldArea.Thickness,
            newArea.Thickness,
            settings.DimensionTolerance,
            ModelCompareChangeImportance.High);
    }

    private static string FormatAreaPropertyAssignment(ModelCompareAreaObjectSnapshot area)
    {
        string propertyName = string.IsNullOrWhiteSpace(area.PropertyName) ? "<none>" : area.PropertyName;
        string materialName = string.IsNullOrWhiteSpace(area.MaterialName) ? "<no material>" : area.MaterialName;
        return $"{propertyName}, t={FormatNumber(area.Thickness)}, mat={materialName}";
    }

    private static void MatchLikelyMovedFrames(
        List<ModelCompareFrameSnapshot> unmatchedOldFrames,
        List<ModelCompareFrameSnapshot> unmatchedNewFrames,
        ModelCompareToleranceSettings settings,
        bool compareMaterialAssignments,
        List<ModelCompareResultRow> results)
    {
        foreach (ModelCompareFrameSnapshot oldFrame in unmatchedOldFrames.ToList())
        {
            List<MovedFrameCandidate> candidates = unmatchedNewFrames
                .Select(newFrame => BuildMovedFrameCandidate(oldFrame, newFrame, settings))
                .Where(candidate => candidate != null)
                .Select(candidate => candidate!)
                .Where(candidate => candidate.ConfidenceLevel >= settings.MinimumMovedFrameConfidence)
                .OrderByDescending(candidate => candidate.Score)
                .ToList();

            if (candidates.Count == 0)
                continue;

            MovedFrameCandidate best = candidates[0];
            bool ambiguous = candidates.Count > 1 &&
                best.Score - candidates[1].Score < 15.0;
            bool newFrameAlreadyBestForAnotherOldFrame = unmatchedOldFrames
                .Where(otherOldFrame => !ReferenceEquals(otherOldFrame, oldFrame))
                .Select(otherOldFrame => BuildMovedFrameCandidate(otherOldFrame, best.NewFrame, settings))
                .Any(candidate => candidate != null && candidate.Score >= best.Score - 10.0);

            if (ambiguous || newFrameAlreadyBestForAnotherOldFrame)
                continue;

            unmatchedOldFrames.Remove(oldFrame);
            unmatchedNewFrames.Remove(best.NewFrame);
            int resultStart = results.Count;
            AddFrameMovedDifference(oldFrame, best.NewFrame, settings, best.ConfidenceLevel, results);
            CompareMatchedFrame(oldFrame, best.NewFrame, settings, compareMaterialAssignments, results);
            string sectionReason = best.SameSection ? "section names match" : "section names differ";
            ApplyDiagnostics(results, resultStart, new ComparisonDiagnostics(
                ModelCompareMatchMethod.NearGeometry,
                best.ConfidenceLevel,
                Math.Clamp(best.Score / 100.0, 0.0, 1.0),
                $"Unique near-geometry candidate passed search distance, length, orientation, and elevation limits; {sectionReason}; score {best.Score:0.#}.",
                best.CoordinateDifference,
                best.MidpointMovement,
                best.LengthDifference,
                best.OrientationDifferenceDegrees));
            ApplySearchContext(results, resultStart, BuildFrameSearchText(oldFrame, best.NewFrame));
            ApplyFrameNavigationContext(results, resultStart, oldFrame, best.NewFrame);
        }
    }

    private static MovedFrameCandidate? BuildMovedFrameCandidate(
        ModelCompareFrameSnapshot oldFrame,
        ModelCompareFrameSnapshot newFrame,
        ModelCompareToleranceSettings settings)
    {
        double midpointMovement = CalculateMidpointMovement(oldFrame, newFrame);
        if (midpointMovement > settings.MovementSearchDistance)
            return null;

        double lengthDifference = Math.Abs(oldFrame.Length - newFrame.Length);
        if (lengthDifference > settings.MovedFrameLengthTolerance)
            return null;

        double orientationDifference = CalculateOrientationDifferenceDegrees(oldFrame, newFrame);
        if (orientationDifference > settings.MovedFrameOrientationToleranceDegrees)
            return null;

        double elevationDifference = Math.Abs(CalculateMidpointZ(oldFrame) - CalculateMidpointZ(newFrame));
        if (elevationDifference > settings.MovedFrameElevationTolerance)
            return null;

        bool sameSection = string.Equals(
            (oldFrame.SectionName ?? "").Trim(),
            (newFrame.SectionName ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase);

        double score = 100.0;
        score -= midpointMovement / settings.MovementSearchDistance * 30.0;
        score -= lengthDifference / settings.MovedFrameLengthTolerance * 20.0;
        score -= orientationDifference / settings.MovedFrameOrientationToleranceDegrees * 20.0;
        score -= elevationDifference / settings.MovedFrameElevationTolerance * 15.0;
        if (!sameSection)
            score -= 20.0;

        ModelCompareConfidenceLevel confidenceLevel = score >= 80.0
            ? ModelCompareConfidenceLevel.High
            : score >= 60.0
                ? ModelCompareConfidenceLevel.Medium
                : ModelCompareConfidenceLevel.Low;

        return new MovedFrameCandidate(
            newFrame,
            score,
            confidenceLevel,
            CalculateMaximumEndpointMovement(oldFrame, newFrame),
            midpointMovement,
            lengthDifference,
            orientationDifference,
            elevationDifference,
            sameSection);
    }

    private static void AddFrameMovedDifference(
        ModelCompareFrameSnapshot oldFrame,
        ModelCompareFrameSnapshot newFrame,
        ModelCompareToleranceSettings settings,
        ModelCompareConfidenceLevel confidenceLevel,
        List<ModelCompareResultRow> results)
    {
        (double X, double Y, double Z) vector = CalculateMidpointMovementVector(oldFrame, newFrame);
        double movement = CalculateDistance(0, 0, 0, vector.X, vector.Y, vector.Z);
        double confidence = confidenceLevel switch
        {
            ModelCompareConfidenceLevel.High => movement <= settings.MovementTolerance ? 0.95 : 0.9,
            ModelCompareConfidenceLevel.Medium => 0.7,
            _ => 0.45
        };

        results.Add(BuildResult(
            ModelCompareChangeType.Moved,
            ModelCompareObjectType.Frame,
            $"{DescribeFrame(newFrame)} / moved {FormatNumber(movement)} / vector ({FormatNumber(vector.X)}, {FormatNumber(vector.Y)}, {FormatNumber(vector.Z)})",
            FormatFrameLocation(oldFrame),
            FormatFrameLocation(newFrame),
            ModelCompareChangeImportance.High,
            confidence,
            confidenceLevel));
    }

    private static ModelCompareMatchMethod GetExactFrameMatchMethod(
        ModelCompareFrameSnapshot oldFrame,
        ModelCompareFrameSnapshot newFrame,
        double coordinateTolerance)
    {
        bool directMatch = PointsMatch(oldFrame.IX, oldFrame.IY, oldFrame.IZ, newFrame.IX, newFrame.IY, newFrame.IZ, coordinateTolerance) &&
            PointsMatch(oldFrame.JX, oldFrame.JY, oldFrame.JZ, newFrame.JX, newFrame.JY, newFrame.JZ, coordinateTolerance);
        if (directMatch)
            return ModelCompareMatchMethod.ExactCoordinates;

        bool reversedMatch = PointsMatch(oldFrame.IX, oldFrame.IY, oldFrame.IZ, newFrame.JX, newFrame.JY, newFrame.JZ, coordinateTolerance) &&
            PointsMatch(oldFrame.JX, oldFrame.JY, oldFrame.JZ, newFrame.IX, newFrame.IY, newFrame.IZ, coordinateTolerance);
        return reversedMatch
            ? ModelCompareMatchMethod.ReversedIJ
            : ModelCompareMatchMethod.Unmatched;
    }

    private static bool IsSamePhysicalFrame(ModelCompareFrameSnapshot oldFrame, ModelCompareFrameSnapshot newFrame, double coordinateTolerance)
    {
        return GetExactFrameMatchMethod(oldFrame, newFrame, coordinateTolerance) != ModelCompareMatchMethod.Unmatched;
    }

    private static bool PointsMatch(
        double ax,
        double ay,
        double az,
        double bx,
        double by,
        double bz,
        double coordinateTolerance)
    {
        return Math.Abs(ax - bx) <= coordinateTolerance &&
            Math.Abs(ay - by) <= coordinateTolerance &&
            Math.Abs(az - bz) <= coordinateTolerance;
    }

    private static double CalculateMaximumEndpointMovement(ModelCompareFrameSnapshot oldFrame, ModelCompareFrameSnapshot newFrame)
    {
        double sameDirection = Math.Max(
            CalculateDistance(oldFrame.IX, oldFrame.IY, oldFrame.IZ, newFrame.IX, newFrame.IY, newFrame.IZ),
            CalculateDistance(oldFrame.JX, oldFrame.JY, oldFrame.JZ, newFrame.JX, newFrame.JY, newFrame.JZ));

        double reversedDirection = Math.Max(
            CalculateDistance(oldFrame.IX, oldFrame.IY, oldFrame.IZ, newFrame.JX, newFrame.JY, newFrame.JZ),
            CalculateDistance(oldFrame.JX, oldFrame.JY, oldFrame.JZ, newFrame.IX, newFrame.IY, newFrame.IZ));

        return Math.Min(sameDirection, reversedDirection);
    }

    private static double CalculateMidpointMovement(ModelCompareFrameSnapshot oldFrame, ModelCompareFrameSnapshot newFrame)
    {
        (double X, double Y, double Z) vector = CalculateMidpointMovementVector(oldFrame, newFrame);
        return CalculateDistance(0, 0, 0, vector.X, vector.Y, vector.Z);
    }

    private static (double X, double Y, double Z) CalculateMidpointMovementVector(ModelCompareFrameSnapshot oldFrame, ModelCompareFrameSnapshot newFrame)
    {
        return (
            CalculateMidpointX(newFrame) - CalculateMidpointX(oldFrame),
            CalculateMidpointY(newFrame) - CalculateMidpointY(oldFrame),
            CalculateMidpointZ(newFrame) - CalculateMidpointZ(oldFrame));
    }

    private static double CalculateMidpointX(ModelCompareFrameSnapshot frame)
    {
        return (frame.IX + frame.JX) / 2.0;
    }

    private static double CalculateMidpointY(ModelCompareFrameSnapshot frame)
    {
        return (frame.IY + frame.JY) / 2.0;
    }

    private static double CalculateMidpointZ(ModelCompareFrameSnapshot frame)
    {
        return (frame.IZ + frame.JZ) / 2.0;
    }

    private static double CalculateOrientationDifferenceDegrees(ModelCompareFrameSnapshot oldFrame, ModelCompareFrameSnapshot newFrame)
    {
        (double X, double Y, double Z) oldVector = (oldFrame.JX - oldFrame.IX, oldFrame.JY - oldFrame.IY, oldFrame.JZ - oldFrame.IZ);
        (double X, double Y, double Z) newVector = (newFrame.JX - newFrame.IX, newFrame.JY - newFrame.IY, newFrame.JZ - newFrame.IZ);
        double oldLength = CalculateDistance(0, 0, 0, oldVector.X, oldVector.Y, oldVector.Z);
        double newLength = CalculateDistance(0, 0, 0, newVector.X, newVector.Y, newVector.Z);
        if (oldLength <= 0 || newLength <= 0)
            return 180.0;

        double dot = oldVector.X * newVector.X + oldVector.Y * newVector.Y + oldVector.Z * newVector.Z;
        double cosine = Math.Clamp(dot / (oldLength * newLength), -1.0, 1.0);
        double angle = Math.Acos(cosine) * 180.0 / Math.PI;

        return Math.Min(angle, 180.0 - angle);
    }

    private static double CalculateDistance(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = ax - bx;
        double dy = ay - by;
        double dz = az - bz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static void CompareFrameProperties(
        IReadOnlyList<ModelCompareFramePropertySnapshot> oldProperties,
        IReadOnlyList<ModelCompareFramePropertySnapshot> newProperties,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        Dictionary<string, ModelCompareFramePropertySnapshot> oldByName = BuildUniqueLookup(oldProperties, property => property.SectionName);
        Dictionary<string, ModelCompareFramePropertySnapshot> newByName = BuildUniqueLookup(newProperties, property => property.SectionName);

        foreach (string name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            ModelCompareFramePropertySnapshot oldProperty = oldByName[name];
            ModelCompareFramePropertySnapshot newProperty = newByName[name];
            string description = $"Frame section '{name}'";

            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / depth", oldProperty.Depth, newProperty.Depth, settings.DimensionTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / width", oldProperty.Width, newProperty.Width, settings.DimensionTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / flange thickness", oldProperty.FlangeThickness, newProperty.FlangeThickness, settings.DimensionTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / web thickness", oldProperty.WebThickness, newProperty.WebThickness, settings.DimensionTolerance, ModelCompareChangeImportance.High);
        }
    }

    private static void CompareAreaProperties(
        IReadOnlyList<ModelCompareAreaPropertySnapshot> oldProperties,
        IReadOnlyList<ModelCompareAreaPropertySnapshot> newProperties,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        Dictionary<string, ModelCompareAreaPropertySnapshot> oldByName = BuildUniqueLookup(oldProperties, property => property.PropertyName);
        Dictionary<string, ModelCompareAreaPropertySnapshot> newByName = BuildUniqueLookup(newProperties, property => property.PropertyName);

        foreach (string name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            AddNumericDifference(
                results,
                ModelCompareObjectType.AreaProperty,
                $"Area property '{name}' / thickness",
                oldByName[name].Thickness,
                newByName[name].Thickness,
                settings.DimensionTolerance,
                ModelCompareChangeImportance.High);
        }
    }

    private static void CompareMaterials(
        IReadOnlyList<ModelCompareMaterialSnapshot> oldMaterials,
        IReadOnlyList<ModelCompareMaterialSnapshot> newMaterials,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        Dictionary<string, ModelCompareMaterialSnapshot> oldByName = BuildUniqueLookup(oldMaterials, material => material.MaterialName);
        Dictionary<string, ModelCompareMaterialSnapshot> newByName = BuildUniqueLookup(newMaterials, material => material.MaterialName);

        foreach (string name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            ModelCompareMaterialSnapshot oldMaterial = oldByName[name];
            ModelCompareMaterialSnapshot newMaterial = newByName[name];
            string description = $"Material '{name}'";

            AddStringDifference(results, ModelCompareObjectType.Material, $"{description} / type", oldMaterial.MaterialType, newMaterial.MaterialType, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.Material, $"{description} / elastic modulus", oldMaterial.ElasticModulus, newMaterial.ElasticModulus, settings.MaterialPropertyTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.Material, $"{description} / Poisson ratio", oldMaterial.PoissonRatio, newMaterial.PoissonRatio, settings.MaterialPropertyTolerance, ModelCompareChangeImportance.Medium);
            AddNumericDifference(results, ModelCompareObjectType.Material, $"{description} / unit weight", oldMaterial.UnitWeight, newMaterial.UnitWeight, settings.MaterialPropertyTolerance, ModelCompareChangeImportance.Medium);
        }
    }

    private static Dictionary<FramePointBucket, List<int>> BuildFrameEndpointLookup(
        IReadOnlyList<ModelCompareFrameSnapshot> frames,
        double coordinateTolerance)
    {
        var lookup = new Dictionary<FramePointBucket, List<int>>();

        for (int index = 0; index < frames.Count; index++)
        {
            ModelCompareFrameSnapshot frame = frames[index];
            FramePointBucket iBucket = BuildFramePointBucket(frame.IX, frame.IY, frame.IZ, coordinateTolerance);
            FramePointBucket jBucket = BuildFramePointBucket(frame.JX, frame.JY, frame.JZ, coordinateTolerance);
            AddFrameEndpointIndex(lookup, iBucket, index);
            if (jBucket != iBucket)
                AddFrameEndpointIndex(lookup, jBucket, index);
        }

        return lookup;
    }

    private static Dictionary<string, Queue<int>> BuildFrameNameLookup(IReadOnlyList<ModelCompareFrameSnapshot> frames)
    {
        var lookup = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < frames.Count; index++)
        {
            string frameName = (frames[index].FrameName ?? "").Trim();
            if (frameName.Length == 0)
                continue;

            if (!lookup.TryGetValue(frameName, out Queue<int>? indexes))
            {
                indexes = new Queue<int>();
                lookup[frameName] = indexes;
            }

            indexes.Enqueue(index);
        }

        return lookup;
    }

    private static void AddFrameEndpointIndex(
        Dictionary<FramePointBucket, List<int>> lookup,
        FramePointBucket bucket,
        int frameIndex)
    {
        if (!lookup.TryGetValue(bucket, out List<int>? indexes))
        {
            indexes = [];
            lookup[bucket] = indexes;
        }

        indexes.Add(frameIndex);
    }

    private static IEnumerable<int> GetExactFrameCandidateIndexes(
        ModelCompareFrameSnapshot oldFrame,
        IReadOnlyDictionary<FramePointBucket, List<int>> newFramesByEndpoint,
        double coordinateTolerance)
    {
        FramePointBucket oldIBucket = BuildFramePointBucket(
            oldFrame.IX,
            oldFrame.IY,
            oldFrame.IZ,
            coordinateTolerance);
        var candidateIndexes = new HashSet<int>();

        for (long xOffset = -1; xOffset <= 1; xOffset++)
        {
            for (long yOffset = -1; yOffset <= 1; yOffset++)
            {
                for (long zOffset = -1; zOffset <= 1; zOffset++)
                {
                    var bucket = new FramePointBucket(
                        oldIBucket.X + xOffset,
                        oldIBucket.Y + yOffset,
                        oldIBucket.Z + zOffset);
                    if (!newFramesByEndpoint.TryGetValue(bucket, out List<int>? indexes))
                        continue;

                    foreach (int index in indexes)
                        candidateIndexes.Add(index);
                }
            }
        }

        return candidateIndexes.Order();
    }

    private static FramePointBucket BuildFramePointBucket(double x, double y, double z, double tolerance)
    {
        return new FramePointBucket(
            QuantizeFrameCoordinate(x, tolerance),
            QuantizeFrameCoordinate(y, tolerance),
            QuantizeFrameCoordinate(z, tolerance));
    }

    private static long QuantizeFrameCoordinate(double value, double tolerance)
    {
        if (!double.IsFinite(value))
            return 0;

        return (long)Math.Floor(value / tolerance);
    }

    private static Dictionary<string, Queue<ModelCompareAreaObjectSnapshot>> BuildAreaLookup(
        IEnumerable<ModelCompareAreaObjectSnapshot> areas,
        double coordinateTolerance)
    {
        var lookup = new Dictionary<string, Queue<ModelCompareAreaObjectSnapshot>>(StringComparer.Ordinal);

        foreach (ModelCompareAreaObjectSnapshot area in areas.OrderBy(area => area.AreaName, StringComparer.OrdinalIgnoreCase))
        {
            string key = BuildAreaGeometryKey(area, coordinateTolerance);
            if (key.Length == 0)
                continue;

            if (!lookup.TryGetValue(key, out Queue<ModelCompareAreaObjectSnapshot>? queue))
            {
                queue = new Queue<ModelCompareAreaObjectSnapshot>();
                lookup[key] = queue;
            }

            queue.Enqueue(area);
        }

        return lookup;
    }

    private static string BuildAreaGeometryKey(ModelCompareAreaObjectSnapshot area, double tolerance)
    {
        if (area.Corners.Count < 3)
            return "";

        return string.Join("|", area.Corners
            .Select(point => BuildPointCoordinateKey(point.X, point.Y, point.Z, tolerance))
            .OrderBy(key => key, StringComparer.Ordinal));
    }

    private static string BuildPointCoordinateKey(double x, double y, double z, double tolerance)
    {
        return $"{Quantize(x, tolerance)},{Quantize(y, tolerance)},{Quantize(z, tolerance)}";
    }

    private static long Quantize(double value, double tolerance)
    {
        if (!double.IsFinite(value))
            return 0;

        return (long)Math.Round(value / tolerance, MidpointRounding.AwayFromZero);
    }

    private static Dictionary<string, T> BuildUniqueLookup<T>(IEnumerable<T> values, Func<T, string> keySelector)
    {
        return values
            .Select(value => new { Value = value, Key = (keySelector(value) ?? "").Trim() })
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddStringDifference(
        List<ModelCompareResultRow> results,
        ModelCompareObjectType objectType,
        string description,
        string oldValue,
        string newValue,
        ModelCompareChangeImportance importance)
    {
        if (string.Equals((oldValue ?? "").Trim(), (newValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        results.Add(BuildResult(
            ModelCompareChangeType.Modified,
            objectType,
            description,
            oldValue ?? "",
            newValue ?? "",
            importance));
    }

    private static void AddNumericDifference(
        List<ModelCompareResultRow> results,
        ModelCompareObjectType objectType,
        string description,
        double oldValue,
        double newValue,
        double tolerance,
        ModelCompareChangeImportance importance)
    {
        if (!double.IsFinite(oldValue) && !double.IsFinite(newValue))
            return;

        if (Math.Abs(oldValue - newValue) <= tolerance)
            return;

        results.Add(BuildResult(
            ModelCompareChangeType.Modified,
            objectType,
            description,
            FormatNumber(oldValue),
            FormatNumber(newValue),
            importance));
    }

    private static ComparisonDiagnostics BuildFrameDiagnostics(
        ModelCompareFrameSnapshot oldFrame,
        ModelCompareFrameSnapshot newFrame,
        ModelCompareMatchMethod matchMethod,
        ModelCompareConfidenceLevel confidenceLevel,
        string reason,
        double confidence = 1.0)
    {
        bool movedMatch = matchMethod is ModelCompareMatchMethod.SameFrameName or ModelCompareMatchMethod.NearGeometry;
        return new ComparisonDiagnostics(
            matchMethod,
            confidenceLevel,
            confidence,
            reason,
            CalculateMaximumEndpointMovement(oldFrame, newFrame),
            movedMatch ? CalculateMidpointMovement(oldFrame, newFrame) : null,
            Math.Abs(oldFrame.Length - newFrame.Length),
            CalculateOrientationDifferenceDegrees(oldFrame, newFrame));
    }

    private static ComparisonDiagnostics BuildUnmatchedDiagnostics(string reason)
    {
        return new ComparisonDiagnostics(
            ModelCompareMatchMethod.Unmatched,
            ModelCompareConfidenceLevel.Medium,
            0.7,
            reason,
            null,
            null,
            null,
            null);
    }

    private static void ApplyDiagnostics(
        List<ModelCompareResultRow> results,
        int startIndex,
        ComparisonDiagnostics diagnostics)
    {
        for (int index = startIndex; index < results.Count; index++)
            ApplyDiagnostics(results[index], diagnostics);
    }

    private static void ApplyDiagnostics(ModelCompareResultRow result, ComparisonDiagnostics diagnostics)
    {
        result.MatchMethod = diagnostics.MatchMethod;
        result.MatchReason = diagnostics.Reason;
        result.Confidence = diagnostics.Confidence;
        result.ConfidenceLevel = diagnostics.ConfidenceLevel;
        result.CoordinateDifference = diagnostics.CoordinateDifference;
        result.MovementDistance = diagnostics.MovementDistance;
        result.LengthDifference = diagnostics.LengthDifference;
        result.OrientationDifferenceDegrees = diagnostics.OrientationDifferenceDegrees;
    }

    private static void ApplySearchContext(List<ModelCompareResultRow> results, int startIndex, string searchText)
    {
        for (int index = startIndex; index < results.Count; index++)
            results[index].SearchText = searchText;
    }

    private static void ApplyFrameNavigationContext(
        List<ModelCompareResultRow> results,
        int startIndex,
        ModelCompareFrameSnapshot? oldFrame,
        ModelCompareFrameSnapshot? newFrame)
    {
        string oldName = (oldFrame?.FrameName ?? "").Trim();
        string newName = (newFrame?.FrameName ?? "").Trim();
        string oldLocation = oldFrame == null ? "" : FormatFrameLocation(oldFrame);
        string newLocation = newFrame == null ? "" : FormatFrameLocation(newFrame);

        for (int index = startIndex; index < results.Count; index++)
        {
            results[index].OldEtabsObjectName = oldName;
            results[index].NewEtabsObjectName = newName;
            results[index].OldObjectLocation = oldLocation;
            results[index].NewObjectLocation = newLocation;
        }
    }

    private static void ApplyAreaNavigationContext(
        List<ModelCompareResultRow> results,
        int startIndex,
        ModelCompareAreaObjectSnapshot? oldArea,
        ModelCompareAreaObjectSnapshot? newArea)
    {
        string oldName = (oldArea?.AreaName ?? "").Trim();
        string newName = (newArea?.AreaName ?? "").Trim();
        string oldLocation = oldArea == null ? "" : FormatAreaLocation(oldArea);
        string newLocation = newArea == null ? "" : FormatAreaLocation(newArea);

        for (int index = startIndex; index < results.Count; index++)
        {
            results[index].OldEtabsObjectName = oldName;
            results[index].NewEtabsObjectName = newName;
            results[index].OldObjectLocation = oldLocation;
            results[index].NewObjectLocation = newLocation;
        }
    }

    private static string BuildFrameSearchText(ModelCompareFrameSnapshot? oldFrame, ModelCompareFrameSnapshot? newFrame)
    {
        return string.Join(" ", new[] { oldFrame, newFrame }
            .Where(frame => frame != null)
            .SelectMany(frame => new[]
            {
                frame!.FrameName,
                frame.Label,
                frame.Story,
                frame.PointIName,
                frame.PointJName,
                frame.SectionName,
                frame.MaterialName,
                string.Join(" ", frame.GroupNames)
            })
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildAreaSearchText(ModelCompareAreaObjectSnapshot? oldArea, ModelCompareAreaObjectSnapshot? newArea)
    {
        return string.Join(" ", new[] { oldArea, newArea }
            .Where(area => area != null)
            .SelectMany(area => new[]
            {
                area!.AreaName,
                area.Label,
                area.Story,
                area.PropertyName,
                area.MaterialName,
                string.Join(" ", area.GroupNames)
            })
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static ModelCompareResultRow BuildResult(
        ModelCompareChangeType changeType,
        ModelCompareObjectType objectType,
        string description,
        string oldValue,
        string newValue,
        ModelCompareChangeImportance importance,
        double confidence = 1.0,
        ModelCompareConfidenceLevel confidenceLevel = ModelCompareConfidenceLevel.High,
        ComparisonDiagnostics? diagnostics = null,
        string searchText = "")
    {
        var result = new ModelCompareResultRow
        {
            ChangeType = changeType,
            ObjectType = objectType,
            ObjectDescription = description,
            OldValue = oldValue ?? "",
            NewValue = newValue ?? "",
            Importance = importance,
            Confidence = confidence,
            ConfidenceLevel = confidenceLevel,
            SearchText = searchText
        };

        if (diagnostics != null)
            ApplyDiagnostics(result, diagnostics);

        return result;
    }

    private static string DescribeFrame(ModelCompareFrameSnapshot frame)
    {
        return string.IsNullOrWhiteSpace(frame.FrameName)
            ? $"Frame at ({FormatNumber(frame.IX)}, {FormatNumber(frame.IY)}, {FormatNumber(frame.IZ)}) to ({FormatNumber(frame.JX)}, {FormatNumber(frame.JY)}, {FormatNumber(frame.JZ)})"
            : $"Frame '{frame.FrameName}'";
    }

    private static string DescribeArea(ModelCompareAreaObjectSnapshot area)
    {
        return string.IsNullOrWhiteSpace(area.AreaName)
            ? $"Area with {area.Corners.Count} corner(s)"
            : $"Area '{area.AreaName}'";
    }

    private static string FormatFrameLocation(ModelCompareFrameSnapshot frame)
    {
        return $"({FormatNumber(frame.IX)}, {FormatNumber(frame.IY)}, {FormatNumber(frame.IZ)}) to ({FormatNumber(frame.JX)}, {FormatNumber(frame.JY)}, {FormatNumber(frame.JZ)})";
    }

    private static string FormatAreaLocation(ModelCompareAreaObjectSnapshot area)
    {
        const int maxDisplayedCorners = 8;
        string coordinates = string.Join("; ", area.Corners
            .Take(maxDisplayedCorners)
            .Select(corner => $"({FormatNumber(corner.X)}, {FormatNumber(corner.Y)}, {FormatNumber(corner.Z)})"));

        return area.Corners.Count > maxDisplayedCorners
            ? $"{coordinates}; ... ({area.Corners.Count - maxDisplayedCorners} more corners)"
            : coordinates;
    }

    private static string FormatNumber(double value)
    {
        return double.IsFinite(value)
            ? value.ToString("0.########", CultureInfo.InvariantCulture)
            : "";
    }

    private static ModelCompareToleranceSettings NormalizeTolerances(ModelCompareToleranceSettings? tolerances)
    {
        ModelCompareToleranceSettings settings = tolerances ?? new ModelCompareToleranceSettings();

        return new ModelCompareToleranceSettings
        {
            CoordinateTolerance = EnsurePositiveTolerance(settings.CoordinateTolerance, 0.001),
            MovementTolerance = EnsurePositiveTolerance(settings.MovementTolerance, 0.5),
            MovementSearchDistance = EnsurePositiveTolerance(settings.MovementSearchDistance, 0.5),
            MovedFrameLengthTolerance = EnsurePositiveTolerance(settings.MovedFrameLengthTolerance, 0.001),
            MovedFrameOrientationToleranceDegrees = EnsurePositiveTolerance(settings.MovedFrameOrientationToleranceDegrees, 0.1),
            MovedFrameElevationTolerance = EnsurePositiveTolerance(settings.MovedFrameElevationTolerance, 0.05),
            LengthTolerance = EnsurePositiveTolerance(settings.LengthTolerance, 0.001),
            DimensionTolerance = EnsurePositiveTolerance(settings.DimensionTolerance, 0.001),
            MaterialPropertyTolerance = EnsurePositiveTolerance(settings.MaterialPropertyTolerance, 0.001),
            MinimumMovedFrameConfidence = Enum.IsDefined(typeof(ModelCompareConfidenceLevel), settings.MinimumMovedFrameConfidence)
                ? settings.MinimumMovedFrameConfidence
                : ModelCompareConfidenceLevel.Medium
        };
    }

    private static double EnsurePositiveTolerance(double value, double defaultValue)
    {
        return double.IsFinite(value) && value > 0
            ? value
            : defaultValue;
    }

    private sealed record MovedFrameCandidate(
        ModelCompareFrameSnapshot NewFrame,
        double Score,
        ModelCompareConfidenceLevel ConfidenceLevel,
        double CoordinateDifference,
        double MidpointMovement,
        double LengthDifference,
        double OrientationDifferenceDegrees,
        double ElevationDifference,
        bool SameSection);

    private readonly record struct FramePointBucket(long X, long Y, long Z);

    private sealed record ComparisonDiagnostics(
        ModelCompareMatchMethod MatchMethod,
        ModelCompareConfidenceLevel ConfidenceLevel,
        double Confidence,
        string Reason,
        double? CoordinateDifference,
        double? MovementDistance,
        double? LengthDifference,
        double? OrientationDifferenceDegrees);
}
