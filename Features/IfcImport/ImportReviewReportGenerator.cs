using System.IO;
using System.Text;

namespace CSIModellingTools.Features.IfcImport;

public sealed class ImportReviewReportGenerator
{
    public string GenerateMarkdown(IfcImportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# IFC Structural Frame Import Review");
        builder.AppendLine();
        AppendSummary(builder, result);
        AppendImportedElements(builder, result);
        AppendSkippedElements(builder, result);
        AppendWarningsByCategory(builder, result);
        AppendConfidenceReview(builder, result);
        AppendCleanupActions(builder, result);
        AppendCoordinateOffset(builder, result);
        return builder.ToString();
    }

    public void WriteMarkdownReport(IfcImportResult result, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Choose a report output path before exporting the IFC import review.", nameof(outputPath));

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, GenerateMarkdown(result));
    }

    private static void AppendSummary(StringBuilder builder, IfcImportResult result)
    {
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Total IFC elements scanned: {result.TotalIfcElementsScanned}");
        builder.AppendLine($"- Beams: {result.BeamCount}");
        builder.AppendLine($"- Columns: {result.ColumnCount}");
        builder.AppendLine($"- Slabs: {result.SlabCount}");
        builder.AppendLine($"- Walls: {result.WallCount}");
        builder.AppendLine($"- Structural surface members: {result.StructuralSurfaceMemberCount}");
        builder.AppendLine($"- Imported: {result.ImportedCount}");
        builder.AppendLine($"- Imported areas: {result.ImportedAreaCount}");
        builder.AppendLine($"- Skipped: {result.SkippedCount}");
        builder.AppendLine($"- Warnings: {result.WarningCount}");
        builder.AppendLine();
    }

    private static void AppendImportedElements(StringBuilder builder, IfcImportResult result)
    {
        builder.AppendLine("## Imported Elements");
        builder.AppendLine();
        if (result.Frames.Count == 0)
        {
            builder.AppendLine("No frame elements were imported.");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| IFC Type | Name | GUID | Storey | Section | Material | Recognition | Confidence |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (AnalyticalFrameElement frame in result.Frames)
            {
                builder.AppendLine($"| {Escape(frame.IfcType)} | {Escape(frame.SourceName)} | {Escape(frame.SourceGuid)} | {Escape(frame.StoreyName)} | {Escape(frame.SectionName)} | {Escape(frame.MaterialName)} | {frame.RecognitionMethod} | {frame.Confidence} |");
            }
            builder.AppendLine();
        }

        if (result.Areas.Count == 0)
        {
            builder.AppendLine("No analytical area elements were imported.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| IFC Type | Name | GUID | Storey | Boundary Points | Thickness | Material | Recognition | Confidence |");
        builder.AppendLine("| --- | --- | --- | --- | ---: | ---: | --- | --- | --- |");
        foreach (AnalyticalAreaElement area in result.Areas)
        {
            builder.AppendLine($"| {Escape(area.IfcType)} | {Escape(area.SourceName)} | {Escape(area.SourceGuid)} | {Escape(area.StoreyName)} | {area.BoundaryPoints.Count} | {area.Thickness:0.###} | {Escape(area.MaterialName)} | {area.RecognitionMethod} | {area.Confidence} |");
        }
        builder.AppendLine();
    }

    private static void AppendSkippedElements(StringBuilder builder, IfcImportResult result)
    {
        builder.AppendLine("## Skipped Elements");
        builder.AppendLine();
        if (result.SkippedElements.Count == 0)
        {
            builder.AppendLine("No elements were skipped.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| IFC Type | Name | GUID | Reason |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (SkippedIfcElement skipped in result.SkippedElements)
            builder.AppendLine($"| {Escape(skipped.IfcType)} | {Escape(skipped.SourceName)} | {Escape(skipped.SourceGuid)} | {Escape(skipped.Reason)} |");
        builder.AppendLine();
    }

    private static void AppendWarningsByCategory(StringBuilder builder, IfcImportResult result)
    {
        builder.AppendLine("## Warnings By Category");
        builder.AppendLine();
        if (result.Warnings.Count == 0)
        {
            builder.AppendLine("No warnings were reported.");
            builder.AppendLine();
            return;
        }

        foreach (IGrouping<IfcImportWarningCategory, IfcImportWarning> group in result.Warnings.GroupBy(warning => warning.Category).OrderBy(group => group.Key.ToString()))
        {
            builder.AppendLine($"### {group.Key}");
            builder.AppendLine();
            foreach (IfcImportWarning warning in group)
                builder.AppendLine($"- [{warning.Severity}] {FormatSource(warning)} {warning.Message}");
            builder.AppendLine();
        }
    }

    private static void AppendConfidenceReview(StringBuilder builder, IfcImportResult result)
    {
        builder.AppendLine("## Medium/Low Confidence Elements");
        builder.AppendLine();
        var frames = result.Frames
            .Where(frame => frame.Confidence is IfcRecognitionConfidence.Medium or IfcRecognitionConfidence.Low)
            .ToList();
        var areas = result.Areas
            .Where(area => area.Confidence is IfcRecognitionConfidence.Medium or IfcRecognitionConfidence.Low)
            .ToList();
        if (frames.Count == 0 && areas.Count == 0)
        {
            builder.AppendLine("No medium or low confidence analytical elements were imported.");
            builder.AppendLine();
            return;
        }

        foreach (AnalyticalFrameElement frame in frames)
            builder.AppendLine($"- [{frame.Confidence}] {frame.IfcType} {frame.SourceName} ({frame.SourceGuid}) via {frame.RecognitionMethod}.");
        foreach (AnalyticalAreaElement area in areas)
            builder.AppendLine($"- [{area.Confidence}] {area.IfcType} {area.SourceName} ({area.SourceGuid}) via {area.RecognitionMethod}.");
        builder.AppendLine();
    }

    private static void AppendCleanupActions(StringBuilder builder, IfcImportResult result)
    {
        builder.AppendLine("## Cleanup Actions");
        builder.AppendLine();
        if (result.CleanupActions.Count == 0)
        {
            builder.AppendLine("No cleanup actions were applied.");
            builder.AppendLine();
            return;
        }

        foreach (string action in result.CleanupActions)
            builder.AppendLine($"- {action}");
        builder.AppendLine();
    }

    private static void AppendCoordinateOffset(StringBuilder builder, IfcImportResult result)
    {
        builder.AppendLine("## Coordinate Offset");
        builder.AppendLine();
        if (!result.CoordinateOffset.Applied)
        {
            builder.AppendLine(result.CoordinateOffset.Message.Length == 0
                ? "Coordinate origin reset was not applied."
                : result.CoordinateOffset.Message);
            builder.AppendLine();
            return;
        }

        builder.AppendLine(result.CoordinateOffset.Message);
        builder.AppendLine();
    }

    private static string FormatSource(IfcImportWarning warning)
    {
        if (string.IsNullOrWhiteSpace(warning.SourceGuid) && string.IsNullOrWhiteSpace(warning.SourceName))
            return "";

        return $"{warning.SourceName} ({warning.SourceGuid}):";
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("|", "\\|", StringComparison.Ordinal);
    }
}
