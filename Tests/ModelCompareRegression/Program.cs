using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace ModelCompareRegression;

internal static class Program
{
    private static readonly ModelCompareSnapshotJsonService JsonService = new();
    private static readonly ModelCompareService CompareService = new();
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("No change", NoChange),
            ("One frame added", OneFrameAdded),
            ("One frame deleted", OneFrameDeleted),
            ("Frame section changed", FrameSectionChanged),
            ("Frame material changed", FrameMaterialChanged),
            ("Frame section dimensions changed", FrameSectionDimensionsChanged),
            ("Area property thickness changed", AreaPropertyThicknessChanged),
            ("Legacy snapshot completeness", LegacySnapshotCompleteness),
            ("Failed frame category refuses frame diff", FailedFrameCategoryRefusesFrameDiff),
            ("Reversed I/J endpoints match", ReversedEndpointsMatch),
            ("Moved matching safety limit", MovedMatchingSafetyLimit)
        ];

        int failed = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine("      " + ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} Model Compare regression tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void NoChange()
    {
        ModelCompareComparisonResult result = Compare(Load("baseline.json"), Load("baseline.json"));
        Equal(0, result.Errors.Count, "A complete unchanged snapshot should not produce comparison errors.");
        Equal(0, result.Differences.Count, "Identical snapshots should have no differences.");
    }

    private static void OneFrameAdded()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Frames.Add(new ModelCompareFrameSnapshot
        {
            FrameName = "F2",
            Label = "F2",
            Story = "Story1",
            PointIName = "P3",
            PointJName = "P4",
            IX = 2,
            IY = 0,
            IZ = 0,
            JX = 2,
            JY = 0,
            JZ = 3,
            Length = 3,
            SectionName = "S1",
            MaterialName = "Concrete"
        });

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        SingleFrameChange(result, ModelCompareChangeType.Added, "F2");
    }

    private static void OneFrameDeleted()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Frames.Clear();

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        SingleFrameChange(result, ModelCompareChangeType.Removed, "F1");
    }

    private static void FrameSectionChanged()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Frames[0].SectionName = "S2";

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame && item.ObjectDescription.Contains("/ section", StringComparison.OrdinalIgnoreCase));
        Equal("S1", row.OldValue, "Unexpected old frame section.");
        Equal("S2", row.NewValue, "Unexpected new frame section.");
    }

    private static void FrameMaterialChanged()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Frames[0].MaterialName = "Steel";

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame && item.ObjectDescription.Contains("/ material", StringComparison.OrdinalIgnoreCase));
        Equal("Concrete", row.OldValue, "Unexpected old frame material.");
        Equal("Steel", row.NewValue, "Unexpected new frame material.");
    }

    private static void FrameSectionDimensionsChanged()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.FrameProperties[0].Depth = 0.5;

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.FrameProperty && item.ObjectDescription.Contains("/ depth", StringComparison.OrdinalIgnoreCase));
        Equal("0.4", row.OldValue, "Unexpected old section depth.");
        Equal("0.5", row.NewValue, "Unexpected new section depth.");
    }

    private static void AreaPropertyThicknessChanged()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.AreaProperties[0].Thickness = 0.25;

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.AreaProperty && item.ObjectDescription.Contains("/ thickness", StringComparison.OrdinalIgnoreCase));
        Equal("0.2", row.OldValue, "Unexpected old area thickness.");
        Equal("0.25", row.NewValue, "Unexpected new area thickness.");
    }

    private static void LegacySnapshotCompleteness()
    {
        ModelCompareSnapshotLoadResult loadResult = LoadResult("legacy.json");
        False(loadResult.IsError, "Legacy snapshots should still load.");
        True(loadResult.Warnings.Any(warning => warning.Contains("completeness is unknown", StringComparison.OrdinalIgnoreCase)),
            "Legacy loading should warn about unknown category completeness.");
        Equal(ModelCompareSnapshotReadStatus.Unknown, loadResult.Snapshot!.Metadata.FramesReadStatus,
            "Legacy frame completeness should remain unknown.");

        ModelCompareComparisonResult comparison = Compare(loadResult.Snapshot, Load("baseline.json"));
        True(comparison.Errors.Any(error => error.Contains("Frame comparison is unavailable", StringComparison.OrdinalIgnoreCase)),
            "Legacy frame comparison should be refused clearly.");
        False(comparison.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Frame),
            "Legacy completeness must not generate frame additions or deletions.");
    }

    private static void FailedFrameCategoryRefusesFrameDiff()
    {
        ModelCompareSnapshot failedSnapshot = Load("failed-frames.json");
        ModelCompareComparisonResult result = Compare(failedSnapshot, Load("baseline.json"));

        True(result.Errors.Any(error => error.Contains("Frame comparison is unavailable", StringComparison.OrdinalIgnoreCase)),
            "Failed frame data should produce a clear comparison error.");
        False(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Frame),
            "Failed frame data must not report every frame as added or deleted.");
    }

    private static void ReversedEndpointsMatch()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        ModelCompareFrameSnapshot frame = newSnapshot.Frames[0];
        frame.FrameName = "F1_REVERSED";
        frame.PointIName = "P2";
        frame.PointJName = "P1";
        (frame.IX, frame.IY, frame.IZ, frame.JX, frame.JY, frame.JZ) =
            (frame.JX, frame.JY, frame.JZ, frame.IX, frame.IY, frame.IZ);
        frame.SectionName = "S2";

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        False(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Frame &&
            row.ChangeType is ModelCompareChangeType.Added or ModelCompareChangeType.Removed),
            "Reversed endpoints must not produce frame added/deleted rows.");
        ModelCompareResultRow row = Single(result.Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame && item.ObjectDescription.Contains("/ section", StringComparison.OrdinalIgnoreCase));
        Equal(ModelCompareMatchMethod.ReversedIJ, row.MatchMethod, "The reversed member should use reversed I/J matching.");
    }

    private static void MovedMatchingSafetyLimit()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Frames = Enumerable.Range(0, 501)
            .Select(index => BuildFrame($"OLD_{index}", index * 10.0))
            .ToList();
        newSnapshot.Frames = Enumerable.Range(0, 500)
            .Select(index => BuildFrame($"NEW_{index}", 100_000.0 + index * 10.0))
            .ToList();

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);

        True(result.Warnings.Any(warning =>
                warning.Contains("moved-frame matching was skipped", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("250,000", StringComparison.OrdinalIgnoreCase)),
            "Large unmatched frame sets should produce a clear moved-matching safety warning.");
        Equal(501, result.Differences.Count(row =>
            row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Removed),
            "All unmatched old frames should remain deleted when moved matching is skipped.");
        Equal(500, result.Differences.Count(row =>
            row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Added),
            "All unmatched new frames should remain added when moved matching is skipped.");
        False(result.Differences.Any(row =>
                row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Moved),
            "No near-geometry moved rows should be created after the safety limit is reached.");
    }

    private static ModelCompareFrameSnapshot BuildFrame(string frameName, double x)
    {
        return new ModelCompareFrameSnapshot
        {
            FrameName = frameName,
            Label = frameName,
            Story = "Story1",
            IX = x,
            IY = 0,
            IZ = 0,
            JX = x,
            JY = 0,
            JZ = 3,
            Length = 3,
            SectionName = "S1",
            MaterialName = "Concrete"
        };
    }

    private static ModelCompareSnapshot Load(string fileName)
    {
        ModelCompareSnapshotLoadResult result = LoadResult(fileName);
        False(result.IsError, $"Fixture '{fileName}' failed to load: {result.Message}");
        return result.Snapshot ?? throw new InvalidOperationException($"Fixture '{fileName}' returned no snapshot.");
    }

    private static ModelCompareSnapshotLoadResult LoadResult(string fileName)
    {
        return JsonService.LoadSnapshot(Path.Combine(FixtureDirectory, fileName));
    }

    private static ModelCompareComparisonResult Compare(ModelCompareSnapshot oldSnapshot, ModelCompareSnapshot newSnapshot)
    {
        return CompareService.CompareSnapshots(oldSnapshot, newSnapshot, new ModelCompareToleranceSettings());
    }

    private static void SingleFrameChange(ModelCompareComparisonResult result, ModelCompareChangeType changeType, string objectName)
    {
        ModelCompareResultRow row = Single(result.Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame && item.ChangeType == changeType);
        string actualName = changeType == ModelCompareChangeType.Removed ? row.OldValue : row.NewValue;
        Equal(objectName, actualName, "Unexpected ETABS frame name.");
    }

    private static T Single<T>(IEnumerable<T> values, Func<T, bool> predicate)
    {
        List<T> matches = values.Where(predicate).ToList();
        Equal(1, matches.Count, "Expected exactly one matching comparison row.");
        return matches[0];
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message) => True(!condition, message);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
    }
}
