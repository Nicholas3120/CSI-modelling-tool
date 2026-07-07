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
            ("Frames without IDs still use geometry", FramesWithoutIdsUseGeometry),
            ("Different IDs block reused-name match", DifferentIdsBlockReusedNameMatch),
            ("Slab split reported as re-partition", SlabSplitDetected),
            ("Slab merge reported as re-partition", SlabMergeDetected),
            ("Slab ID matches reshaped area", SlabIdMatchesReshapedArea),
            ("Slab edge node insertion is ignored", SlabEdgeNodeInsertionIgnored),
            ("Slab redraw at same footprint matches", SlabRedrawSameFootprintMatches),
            ("Slab opening flag change detected", SlabOpeningFlagChangeDetected),
            ("Relocated slab footprint backfill reported", RelocatedSlabBackfillReported),
            ("Reshaped area matched by ID reports geometry", ReshapedAreaByIdReportsGeometry),
            ("Material modulus rounding noise ignored", MaterialModulusNoiseIgnored),
            ("Material modulus real change flagged", MaterialModulusRealChangeFlagged),
            ("Joint across bucket boundary still matches", JointAcrossBucketBoundaryMatches),
            ("Restraint table parses standard columns", RestraintTableParsesStandardColumns),
            ("Restraint table handles aliased shuffled columns", RestraintTableHandlesAliasedColumns),
            ("Restraint table missing DOF column falls back", RestraintTableMissingColumnFallsBack),
            ("Restraint table parses varied boolean tokens", RestraintTableParsesVariedBooleans),
            ("Restraint table drops all-free rows", RestraintTableDropsAllFreeRows)
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

    private static void SlabSplitDetected()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Areas = [MakeArea("OLD1", "AP1", 0.2, (0, 0, 0), (4, 0, 0), (4, 4, 0), (0, 4, 0))];
        newSnapshot.Areas =
        [
            MakeArea("NEW1", "AP2", 0.25, (0, 0, 0), (2, 0, 0), (2, 4, 0), (0, 4, 0)),
            MakeArea("NEW2", "AP2", 0.25, (2, 0, 0), (4, 0, 0), (4, 4, 0), (2, 4, 0))
        ];

        List<ModelCompareResultRow> areaRows = Compare(oldSnapshot, newSnapshot).Differences
            .Where(row => row.ObjectType == ModelCompareObjectType.Area).ToList();
        False(areaRows.Any(row => row.ChangeType is ModelCompareChangeType.Added or ModelCompareChangeType.Removed),
            "A split slab must not be reported as separate added/removed areas.");
        ModelCompareResultRow repartition = Single(areaRows,
            row => row.ObjectDescription.Contains("re-partitioned", StringComparison.OrdinalIgnoreCase));
        True(repartition.OldValue.Contains("1 area", StringComparison.OrdinalIgnoreCase), "Old side should be one area.");
        True(repartition.NewValue.Contains("2 areas", StringComparison.OrdinalIgnoreCase), "New side should be two areas.");
    }

    private static void SlabMergeDetected()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Areas =
        [
            MakeArea("OLD1", "AP1", 0.2, (0, 0, 0), (2, 0, 0), (2, 4, 0), (0, 4, 0)),
            MakeArea("OLD2", "AP1", 0.2, (2, 0, 0), (4, 0, 0), (4, 4, 0), (2, 4, 0))
        ];
        newSnapshot.Areas = [MakeArea("NEW1", "AP2", 0.25, (0, 0, 0), (4, 0, 0), (4, 4, 0), (0, 4, 0))];

        List<ModelCompareResultRow> areaRows = Compare(oldSnapshot, newSnapshot).Differences
            .Where(row => row.ObjectType == ModelCompareObjectType.Area).ToList();
        False(areaRows.Any(row => row.ChangeType is ModelCompareChangeType.Added or ModelCompareChangeType.Removed),
            "A merged slab must not be reported as separate added/removed areas.");
        ModelCompareResultRow repartition = Single(areaRows,
            row => row.ObjectDescription.Contains("re-partitioned", StringComparison.OrdinalIgnoreCase));
        True(repartition.OldValue.Contains("2 areas", StringComparison.OrdinalIgnoreCase), "Old side should be two areas.");
        True(repartition.NewValue.Contains("1 area", StringComparison.OrdinalIgnoreCase), "New side should be one area.");
    }

    private static ModelCompareAreaObjectSnapshot MakeArea(string name, string property, double thickness, params (double X, double Y, double Z)[] corners)
    {
        return new ModelCompareAreaObjectSnapshot
        {
            AreaName = name,
            Label = name,
            Story = "Story1",
            PropertyName = property,
            MaterialName = "Concrete",
            Thickness = thickness,
            Corners = corners.Select(c => new ModelComparePointSnapshot { X = c.X, Y = c.Y, Z = c.Z }).ToList()
        };
    }

    private static void DifferentIdsBlockReusedNameMatch()
    {
        // ETABS reuses names like "10" for unrelated members after a Save-As + edits. Two frames that share
        // that name but carry different tracking IDs and sit far apart must be reported as added/removed,
        // never matched as a large bogus "move".
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Frames[0].FrameName = "10";
        oldSnapshot.Frames[0].Uid = "MCT-old";
        ModelCompareFrameSnapshot newFrame = newSnapshot.Frames[0];
        newFrame.FrameName = "10";
        newFrame.Uid = "MCT-new";
        newFrame.IX = 50;
        newFrame.JX = 50;

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        False(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Moved),
            "Frames with different tracking IDs must not be matched as moved via a shared reused name.");
        True(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Added),
            "The differently-identified new frame should be reported as added.");
        True(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Removed),
            "The differently-identified old frame should be reported as removed.");
    }

    private static void SlabIdMatchesReshapedArea()
    {
        // Same area ID but a different footprint (reshaped/enlarged). The ID must keep it one matched slab,
        // not a deletion plus an addition.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Areas[0].Uid = "MCT-slab1";
        ModelCompareAreaObjectSnapshot reshaped = newSnapshot.Areas[0];
        reshaped.AreaName = "A_RENUMBERED";
        reshaped.Uid = "MCT-slab1";
        reshaped.Corners[2] = new ModelComparePointSnapshot { X = 6, Y = 6, Z = 0 };
        reshaped.PropertyName = "AP2";

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        False(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Area &&
                row.ChangeType is ModelCompareChangeType.Added or ModelCompareChangeType.Removed),
            "A reshaped area sharing an ID must not be reported as added/removed.");
        ModelCompareResultRow row = Single(result.Differences,
            item => item.ObjectType == ModelCompareObjectType.Area && item.ObjectDescription.Contains("/ property", StringComparison.OrdinalIgnoreCase));
        Equal(ModelCompareMatchMethod.PersistentId, row.MatchMethod, "The reshaped area should be matched by its persistent ID.");
    }

    private static void SlabEdgeNodeInsertionIgnored()
    {
        // A collinear edge node inserted (e.g. a beam crossing the slab edge) must not register as a change.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        ModelCompareAreaObjectSnapshot area = newSnapshot.Areas[0];
        area.AreaName = "A_MESHED";
        // Original corners are (0,0)-(4,0)-(4,4)-(0,4); insert a mid-edge node at (2,0) on the first edge.
        area.Corners.Insert(1, new ModelComparePointSnapshot { X = 2, Y = 0, Z = 0 });

        False(Compare(oldSnapshot, newSnapshot).Differences.Any(row => row.ObjectType == ModelCompareObjectType.Area),
            "Inserting a collinear edge node must not produce an area difference.");
    }

    private static void SlabRedrawSameFootprintMatches()
    {
        // No IDs, redrawn with a new name at the same footprint: geometry must still match it, no diff.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Areas[0].AreaName = "A_REDRAWN";

        False(Compare(oldSnapshot, newSnapshot).Differences.Any(row => row.ObjectType == ModelCompareObjectType.Area),
            "A redrawn slab at the same footprint should match by geometry with no difference.");
    }

    private static void SlabOpeningFlagChangeDetected()
    {
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Areas[0].IsOpening = true;

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Area && item.ObjectDescription.Contains("/ opening", StringComparison.OrdinalIgnoreCase));
        Equal("Slab/panel", row.OldValue, "The old area should read as a slab/panel.");
        Equal("Opening", row.NewValue, "The new area should read as an opening.");
    }

    private static void RelocatedSlabBackfillReported()
    {
        // Slab A1 (GUID MCT-300) relocates +10 m, and a different new slab (A2) is drawn on its old footprint.
        // Identity matching alone would report "A1 moved" + "A2 added" with no link; the backfill fallback must
        // connect them so the old location is not mistaken for unchanged.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Areas[0].Uid = "MCT-300";

        ModelCompareAreaObjectSnapshot relocated = newSnapshot.Areas[0];
        relocated.Uid = "MCT-300";
        foreach (ModelComparePointSnapshot corner in relocated.Corners)
            corner.X += 10;
        newSnapshot.Areas.Add(MakeArea("A2", "AP1", 0.2, (0, 0, 0), (4, 0, 0), (4, 4, 0), (0, 4, 0)));

        List<ModelCompareResultRow> areaRows = Compare(oldSnapshot, newSnapshot).Differences
            .Where(row => row.ObjectType == ModelCompareObjectType.Area).ToList();
        False(areaRows.Any(row => row.ChangeType == ModelCompareChangeType.Removed),
            "The GUID-matched slab must not be reported as removed.");
        True(areaRows.Any(row => row.ChangeType == ModelCompareChangeType.Added),
            "The slab drawn on the old footprint should still be reported as added.");
        ModelCompareResultRow backfill = Single(areaRows,
            row => row.ObjectDescription.Contains("backfill", StringComparison.OrdinalIgnoreCase));
        True(backfill.OldValue.Contains("A1", StringComparison.OrdinalIgnoreCase), "The backfill row should name the relocated slab.");
        True(backfill.NewValue.Contains("A2", StringComparison.OrdinalIgnoreCase), "The backfill row should name the added slab.");
    }

    private static void ReshapedAreaByIdReportsGeometry()
    {
        // Same area ID, a corner moved, but the property assignment is unchanged. Without geometry detection
        // this reshape would be silent. It must surface as a geometry change, matched by the persistent ID.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        oldSnapshot.Areas[0].Uid = "MCT-slabG";
        ModelCompareAreaObjectSnapshot reshaped = newSnapshot.Areas[0];
        reshaped.Uid = "MCT-slabG";
        reshaped.Corners[2] = new ModelComparePointSnapshot { X = 6, Y = 6, Z = 0 };

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        False(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Area &&
                row.ChangeType is ModelCompareChangeType.Added or ModelCompareChangeType.Removed),
            "A reshaped area sharing an ID must not be reported as added/removed.");
        ModelCompareResultRow row = Single(result.Differences,
            item => item.ObjectType == ModelCompareObjectType.Area && item.ObjectDescription.Contains("/ geometry", StringComparison.OrdinalIgnoreCase));
        Equal(ModelCompareMatchMethod.PersistentId, row.MatchMethod, "The reshaped area should be matched by its persistent ID.");
    }

    private static void MaterialModulusNoiseIgnored()
    {
        // A sub-MPa modulus difference (e.g. unit-conversion rounding) must not be flagged as a change.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Materials[0].ElasticModulus = 30000.5;

        False(Compare(oldSnapshot, newSnapshot).Differences.Any(row =>
                row.ObjectType == ModelCompareObjectType.Material && row.ObjectDescription.Contains("elastic modulus", StringComparison.OrdinalIgnoreCase)),
            "A sub-MPa modulus rounding difference must not be reported as a change.");
    }

    private static void MaterialModulusRealChangeFlagged()
    {
        // A real grade change (30000 -> 35000 MPa) must still be reported despite the relative tolerance.
        ModelCompareSnapshot oldSnapshot = Load("baseline.json");
        ModelCompareSnapshot newSnapshot = Load("baseline.json");
        newSnapshot.Materials[0].ElasticModulus = 35000;

        ModelCompareResultRow row = Single(Compare(oldSnapshot, newSnapshot).Differences,
            item => item.ObjectType == ModelCompareObjectType.Material && item.ObjectDescription.Contains("elastic modulus", StringComparison.OrdinalIgnoreCase));
        Equal("30000", row.OldValue, "Unexpected old elastic modulus.");
        Equal("35000", row.NewValue, "Unexpected new elastic modulus.");
    }

    private static void JointAcrossBucketBoundaryMatches()
    {
        // Two joints within coordinate tolerance but on opposite sides of a quantization bucket boundary must
        // match, not be reported as one removed plus one added.
        ModelCompareSnapshot oldSnapshot = SnapshotWithJoints(Fixed(0.0009, 0, 0));
        ModelCompareSnapshot newSnapshot = SnapshotWithJoints(Pinned(0.0011, 0, 0));

        ModelCompareComparisonResult result = Compare(oldSnapshot, newSnapshot);
        False(result.Differences.Any(row => row.ObjectType == ModelCompareObjectType.Joint &&
                row.ChangeType is ModelCompareChangeType.Added or ModelCompareChangeType.Removed),
            "Joints within tolerance across a bucket boundary must not be reported as added/removed.");
        ModelCompareResultRow row = Single(result.Differences,
            item => item.ObjectType == ModelCompareObjectType.Joint);
        Equal(ModelCompareChangeType.Modified, row.ChangeType, "The near-coincident joints should match and report a restraint change.");
        Equal("Fixed", row.OldValue, "The old restraint should read as fixed.");
        Equal("Pinned", row.NewValue, "The new restraint should read as pinned.");
    }

    private static void RestraintTableParsesStandardColumns()
    {
        string[] fields = ["UniqueName", "U1", "U2", "U3", "R1", "R2", "R3"];
        string[] data = ["P1", "Yes", "Yes", "Yes", "No", "No", "No"];
        var coords = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase)
        {
            ["P1"] = (1, 2, 3)
        };

        True(ModelCompareRestraintTableParser.TryParse(fields, data, 1, coords, out List<ModelCompareJointSnapshot> joints),
            "A standard restraint table should be recognized.");
        Equal(1, joints.Count, "Exactly one restrained joint should be parsed.");
        ModelCompareJointSnapshot joint = joints[0];
        Equal("P1", joint.PointName, "Unexpected joint name.");
        True(joint.RestraintUX && joint.RestraintUY && joint.RestraintUZ, "Translational DOFs should be restrained.");
        False(joint.RestraintRX || joint.RestraintRY || joint.RestraintRZ, "Rotational DOFs should be free.");
        Equal(1.0, joint.X, "Joint X should come from the coordinate lookup.");
        Equal(2.0, joint.Y, "Joint Y should come from the coordinate lookup.");
        Equal(3.0, joint.Z, "Joint Z should come from the coordinate lookup.");
    }

    private static void RestraintTableHandlesAliasedColumns()
    {
        // Columns out of order and using the UX/RX aliases instead of U1/R1.
        string[] fields = ["RZ", "Point", "UY", "UX", "UZ", "RX", "RY"];
        string[] data = ["Yes", "B1", "Yes", "Yes", "Yes", "Yes", "Yes"];
        var coords = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);

        True(ModelCompareRestraintTableParser.TryParse(fields, data, 1, coords, out List<ModelCompareJointSnapshot> joints),
            "Aliased, reordered columns should still be recognized.");
        Equal(1, joints.Count, "Exactly one restrained joint should be parsed.");
        ModelCompareJointSnapshot joint = joints[0];
        Equal("B1", joint.PointName, "The point-name column should be resolved regardless of position.");
        True(joint.RestraintUX && joint.RestraintUY && joint.RestraintUZ && joint.RestraintRX && joint.RestraintRY && joint.RestraintRZ,
            "A fully fixed support should map every DOF regardless of column order.");
    }

    private static void RestraintTableMissingColumnFallsBack()
    {
        // No R3/RZ column: the parser must refuse (so the caller falls back to the per-point scan).
        string[] fields = ["UniqueName", "U1", "U2", "U3", "R1", "R2"];
        string[] data = ["P1", "Yes", "Yes", "Yes", "No", "No"];
        var coords = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);

        False(ModelCompareRestraintTableParser.TryParse(fields, data, 1, coords, out _),
            "A table missing a DOF column must not be parsed; the caller should fall back to the scan.");
    }

    private static void RestraintTableParsesVariedBooleans()
    {
        string[] fields = ["Joint", "UX", "UY", "UZ", "RX", "RY", "RZ"];
        string[] data = ["S1", "TRUE", "1", "no", "", "No", "yes"];
        var coords = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);

        True(ModelCompareRestraintTableParser.TryParse(fields, data, 1, coords, out List<ModelCompareJointSnapshot> joints),
            "Varied boolean tokens should still parse.");
        ModelCompareJointSnapshot joint = joints[0];
        True(joint.RestraintUX && joint.RestraintUY && joint.RestraintRZ, "TRUE, 1 and yes should read as restrained.");
        False(joint.RestraintUZ || joint.RestraintRX || joint.RestraintRY, "no and blank should read as free.");
    }

    private static void RestraintTableDropsAllFreeRows()
    {
        string[] fields = ["UniqueName", "U1", "U2", "U3", "R1", "R2", "R3"];
        string[] data =
        [
            "P1", "Yes", "Yes", "Yes", "Yes", "Yes", "Yes",
            "P2", "No", "No", "No", "No", "No", "No"
        ];
        var coords = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);

        True(ModelCompareRestraintTableParser.TryParse(fields, data, 2, coords, out List<ModelCompareJointSnapshot> joints),
            "A well-formed table should parse.");
        Equal(1, joints.Count, "An all-free row should be dropped so only true supports remain.");
        Equal("P1", joints[0].PointName, "The remaining joint should be the restrained one.");
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
