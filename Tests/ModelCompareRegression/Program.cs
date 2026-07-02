using CSIModellingTools.Models;
using CSIModellingTools.Services;
using CSIModellingTools.ViewModels;

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
            ("Moved matching safety limit", MovedMatchingSafetyLimit),
            ("Vertical frame classified as column", VerticalFrameClassifiedAsColumn),
            ("Horizontal frame classified as beam", HorizontalFrameClassifiedAsBeam),
            ("Diagonal frame classified as brace", DiagonalFrameClassifiedAsBrace),
            ("Area change classified as area member", AreaChangeClassifiedAsAreaMember),
            ("Object row aggregates field changes", ObjectRowAggregatesFieldChanges),
            ("Added object reports added change", AddedObjectReportsAddedChange),
            ("Moved object reports moved change", MovedObjectReportsMovedChange),
            ("Joint restraint change detected", JointRestraintChangeDetected),
            ("Joint support added", JointSupportAdded),
            ("Joint support removed", JointSupportRemoved),
            ("Unchanged joints produce no diff", UnchangedJointsProduceNoDiff),
            ("Legacy snapshot skips joint comparison", LegacySnapshotSkipsJointComparison),
            ("Frame end release change detected", FrameEndReleaseChangeDetected),
            ("Persistent ID matches relocated frame", PersistentIdMatchesRelocatedFrame),
            ("Persistent ID beats geometry ambiguity", PersistentIdSurvivesRename),
            ("Frames without IDs still use geometry", FramesWithoutIdsUseGeometry)
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

    private static void VerticalFrameClassifiedAsColumn()
    {
        ModelCompareResultRow row = FrameSectionChangeRow(0, 0, 0, 0, 0, 3);
        Equal(ModelCompareMemberType.Column, row.MemberType, "A vertical frame should be classified as a column.");
        Equal("Story1", row.Story, "The frame story should be carried onto the result row.");
    }

    private static void HorizontalFrameClassifiedAsBeam()
    {
        ModelCompareResultRow row = FrameSectionChangeRow(0, 0, 0, 3, 0, 0);
        Equal(ModelCompareMemberType.Beam, row.MemberType, "A horizontal frame should be classified as a beam.");
    }

    private static void DiagonalFrameClassifiedAsBrace()
    {
        ModelCompareResultRow row = FrameSectionChangeRow(0, 0, 0, 3, 0, 3);
        Equal(ModelCompareMemberType.Brace, row.MemberType, "A diagonal frame should be classified as a brace.");
    }

    private static void AreaChangeClassifiedAsAreaMember()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Areas[0].PropertyName = "AP2";

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Area &&
                    item.ObjectDescription.Contains("/ property", StringComparison.OrdinalIgnoreCase));
        Equal(ModelCompareMemberType.Area, row.MemberType, "An area object change should be classified as an area member.");
        Equal("Story1", row.Story, "The area story should be carried onto the result row.");
    }

    private static void ObjectRowAggregatesFieldChanges()
    {
        var rows = new List<ModelCompareResultRowViewModel>
        {
            BuildRowViewModel(ModelCompareChangeType.Modified, ModelCompareObjectType.Frame, "Frame 'F1' / section", "S1", "S2", ModelCompareChangeImportance.High),
            BuildRowViewModel(ModelCompareChangeType.Modified, ModelCompareObjectType.Frame, "Frame 'F1' / material", "Concrete", "Steel", ModelCompareChangeImportance.Medium),
            BuildRowViewModel(ModelCompareChangeType.Modified, ModelCompareObjectType.Frame, "Frame 'F1' / length", "3", "3.2", ModelCompareChangeImportance.Medium)
        };

        var objectRow = new ModelCompareObjectResultViewModel(
            ModelCompareObjectType.Frame, ModelCompareMemberType.Column, "Story1", "F1", "", rows);

        Equal(3, objectRow.Rows.Count, "All field changes for one frame should collapse into a single object with three detail rows.");
        Equal(ModelCompareChangeType.Modified, objectRow.PrimaryChangeType, "A frame with only modifications should report a modified change type.");
        Equal(ModelCompareChangeImportance.High, objectRow.Importance, "Object importance should be the highest of its field changes.");
        True(objectRow.ChangeSummary.Contains("Section", StringComparison.OrdinalIgnoreCase) &&
             objectRow.ChangeSummary.Contains("Material", StringComparison.OrdinalIgnoreCase) &&
             objectRow.ChangeSummary.Contains("Length", StringComparison.OrdinalIgnoreCase),
             "The change summary should mention every changed field.");
    }

    private static void AddedObjectReportsAddedChange()
    {
        var rows = new List<ModelCompareResultRowViewModel>
        {
            BuildRowViewModel(ModelCompareChangeType.Added, ModelCompareObjectType.Frame, "Frame 'F2'", "", "F2", ModelCompareChangeImportance.High)
        };

        var objectRow = new ModelCompareObjectResultViewModel(
            ModelCompareObjectType.Frame, ModelCompareMemberType.Beam, "Story1", "F2", "", rows);
        Equal(ModelCompareChangeType.Added, objectRow.PrimaryChangeType, "An object whose only row is added should report an added change type.");
    }

    private static void MovedObjectReportsMovedChange()
    {
        var rows = new List<ModelCompareResultRowViewModel>
        {
            BuildRowViewModel(ModelCompareChangeType.Moved, ModelCompareObjectType.Frame, "Frame 'F1' / moved", "old", "new", ModelCompareChangeImportance.High, 0.12),
            BuildRowViewModel(ModelCompareChangeType.Modified, ModelCompareObjectType.Frame, "Frame 'F1' / section", "S1", "S2", ModelCompareChangeImportance.High)
        };

        var objectRow = new ModelCompareObjectResultViewModel(
            ModelCompareObjectType.Frame, ModelCompareMemberType.Column, "Story1", "F1", "", rows);
        Equal(ModelCompareChangeType.Moved, objectRow.PrimaryChangeType, "A moved-and-modified frame should report a moved change type.");
        True(objectRow.ChangeSummary.Contains("Moved", StringComparison.OrdinalIgnoreCase), "The change summary should mention the move.");
    }

    private static void JointRestraintChangeDetected()
    {
        ModelCompareSnapshot oldSnapshot = SnapshotWithJoints(Fixed(0, 0, 0));
        ModelCompareSnapshot newSnapshot = SnapshotWithJoints(Pinned(0, 0, 0));

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Joint);
        Equal(ModelCompareChangeType.Modified, row.ChangeType, "A restraint change should be reported as a modification.");
        Equal("Fixed", row.OldValue, "The old restraint should read as fixed.");
        Equal("Pinned", row.NewValue, "The new restraint should read as pinned.");
    }

    private static void JointSupportAdded()
    {
        ModelCompareSnapshot oldSnapshot = SnapshotWithJoints();
        ModelCompareSnapshot newSnapshot = SnapshotWithJoints(Fixed(0, 0, 0));

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Joint);
        Equal(ModelCompareChangeType.Added, row.ChangeType, "A new support should be reported as an added joint.");
    }

    private static void JointSupportRemoved()
    {
        ModelCompareSnapshot oldSnapshot = SnapshotWithJoints(Fixed(0, 0, 0));
        ModelCompareSnapshot newSnapshot = SnapshotWithJoints();

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Joint);
        Equal(ModelCompareChangeType.Removed, row.ChangeType, "A deleted support should be reported as a removed joint.");
    }

    private static void UnchangedJointsProduceNoDiff()
    {
        ModelCompareSnapshot oldSnapshot = SnapshotWithJoints(Fixed(0, 0, 0));
        ModelCompareSnapshot newSnapshot = SnapshotWithJoints(Fixed(0, 0, 0));

        False(Compare(oldSnapshot, newSnapshot).Differences.Any(item => item.ObjectType == ModelCompareObjectType.Joint),
            "Identical joints must not produce a difference row.");
    }

    private static void LegacySnapshotSkipsJointComparison()
    {
        // A legacy snapshot (joints status Unknown) must not have its missing supports reported as removed.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = SnapshotWithJoints(Fixed(0, 0, 0));

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        False(result.Differences.Any(item => item.ObjectType == ModelCompareObjectType.Joint),
            "Joint comparison must be skipped when one snapshot has unknown joint completeness.");
        True(result.Warnings.Any(warning => warning.Contains("Joint", StringComparison.OrdinalIgnoreCase)),
            "A skipped joint comparison should warn that the category is unavailable.");
    }

    private static void FrameEndReleaseChangeDetected()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        ModelCompareFrameSnapshot frame = newSnapshot.Frames[0];
        frame.ReleaseMoment2J = true;
        frame.ReleaseMoment3J = true;

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame &&
                    item.ObjectDescription.Contains("J-end release", StringComparison.OrdinalIgnoreCase));
        Equal("Continuous", row.OldValue, "The old J end should read as continuous.");
        Equal("Pinned (M2, M3)", row.NewValue, "The new J end should read as a pinned connection.");
    }

    private static void PersistentIdMatchesRelocatedFrame()
    {
        // Same member ID, but relocated far beyond any geometry search distance: without the ID this would
        // read as one deletion plus one addition. The ID must keep it a single matched (moved) member.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Frames[0].Uid = "MCT-abc";
        ModelCompareFrameSnapshot moved = newSnapshot.Frames[0];
        moved.FrameName = "RENAMED";
        moved.Uid = "MCT-abc";
        moved.IX = 100; moved.JX = 100;

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        False(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Frame &&
                row.ChangeType is ModelCompareChangeType.Added or ModelCompareChangeType.Removed),
            "A relocated frame sharing a member ID must not be reported as added/removed.");
        ModelCompareResultRow movedRow = Single(result.Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame && item.ChangeType == ModelCompareChangeType.Moved);
        Equal(ModelCompareMatchMethod.PersistentId, movedRow.MatchMethod, "The relocated frame should be matched by its persistent ID.");
    }

    private static void PersistentIdSurvivesRename()
    {
        // Same ID and position, renamed, with a section change: matched by ID and reports only the section.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Frames[0].Uid = "MCT-xyz";
        newSnapshot.Frames[0].Uid = "MCT-xyz";
        newSnapshot.Frames[0].FrameName = "RENAMED";
        newSnapshot.Frames[0].SectionName = "S2";

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame && item.ObjectDescription.Contains("/ section", StringComparison.OrdinalIgnoreCase));
        Equal(ModelCompareMatchMethod.PersistentId, row.MatchMethod, "A renamed frame with a shared ID should be matched by that ID.");
        Equal("S2", row.NewValue, "The section change should still be reported.");
    }

    private static void FramesWithoutIdsUseGeometry()
    {
        // No member IDs: matching must fall back to geometry exactly as before.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Frames[0].FrameName = "REDRAWN";
        newSnapshot.Frames[0].SectionName = "S2";

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame && item.ObjectDescription.Contains("/ section", StringComparison.OrdinalIgnoreCase));
        Equal(ModelCompareMatchMethod.ExactCoordinates, row.MatchMethod, "Without member IDs, delete+redraw at the same position should still match by coordinates.");
    }

    private static ModelCompareSnapshot SnapshotWithJoints(params ModelCompareJointSnapshot[] joints)
    {
        ModelCompareSnapshot snapshot = Load("baseline.json");
        snapshot.Metadata.JointsReadStatus = ModelCompareSnapshotReadStatus.Success;
        snapshot.Joints = joints.ToList();
        return snapshot;
    }

    private static ModelCompareJointSnapshot Fixed(double x, double y, double z)
    {
        return new ModelCompareJointSnapshot
        {
            PointName = $"J_{x}_{y}_{z}",
            X = x,
            Y = y,
            Z = z,
            RestraintUX = true,
            RestraintUY = true,
            RestraintUZ = true,
            RestraintRX = true,
            RestraintRY = true,
            RestraintRZ = true
        };
    }

    private static ModelCompareJointSnapshot Pinned(double x, double y, double z)
    {
        return new ModelCompareJointSnapshot
        {
            PointName = $"J_{x}_{y}_{z}",
            X = x,
            Y = y,
            Z = z,
            RestraintUX = true,
            RestraintUY = true,
            RestraintUZ = true
        };
    }

    private static ModelCompareResultRow FrameSectionChangeRow(double ix, double iy, double iz, double jx, double jy, double jz)
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        foreach (ModelCompareSnapshot snapshot in new[] { oldSnapshot, newSnapshot })
        {
            ModelCompareFrameSnapshot frame = snapshot.Frames[0];
            frame.IX = ix;
            frame.IY = iy;
            frame.IZ = iz;
            frame.JX = jx;
            frame.JY = jy;
            frame.JZ = jz;
        }

        newSnapshot.Frames[0].SectionName = "S2";
        return Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Frame &&
                    item.ObjectDescription.Contains("/ section", StringComparison.OrdinalIgnoreCase));
    }

    private static ModelCompareResultRowViewModel BuildRowViewModel(
        ModelCompareChangeType changeType,
        ModelCompareObjectType objectType,
        string description,
        string oldValue,
        string newValue,
        ModelCompareChangeImportance importance,
        double? movementDistance = null)
    {
        return new ModelCompareResultRowViewModel(new ModelCompareResultRow
        {
            ChangeType = changeType,
            ObjectType = objectType,
            ObjectDescription = description,
            OldValue = oldValue,
            NewValue = newValue,
            Importance = importance,
            MovementDistance = movementDistance
        });
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
