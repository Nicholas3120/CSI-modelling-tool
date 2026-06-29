using System.IO;

namespace CSIModellingTools.Features.IfcImport;

public static class IfcImportDemo
{
    public static int Run(string ifcPath)
    {
        if (string.IsNullOrWhiteSpace(ifcPath) || !File.Exists(ifcPath))
        {
            Console.WriteLine("Provide an existing IFC file path.");
            return 1;
        }

        var service = new IfcImportService();
        IfcImportResult result = service.ImportStructuralFrames(ifcPath, new IfcImportOptions());

        string outputPath = Path.Combine(
            Path.GetDirectoryName(ifcPath) ?? "",
            Path.GetFileNameWithoutExtension(ifcPath) + ".ifc-frames.json");
        string reportPath = Path.Combine(
            Path.GetDirectoryName(ifcPath) ?? "",
            Path.GetFileNameWithoutExtension(ifcPath) + ".ifc-frames-review.md");

        new IfcImportJsonExporter().WriteResult(result, outputPath);
        new ImportReviewReportGenerator().WriteMarkdownReport(result, reportPath);

        Console.WriteLine($"IFC scanned: {result.TotalIfcElementsScanned}");
        Console.WriteLine($"Beams: {result.BeamCount}");
        Console.WriteLine($"Columns: {result.ColumnCount}");
        Console.WriteLine($"Imported: {result.ImportedCount}");
        Console.WriteLine($"Skipped: {result.SkippedCount}");
        Console.WriteLine($"Warnings: {result.WarningCount}");
        Console.WriteLine($"JSON: {outputPath}");
        Console.WriteLine($"Review: {reportPath}");
        return result.SkippedCount > 0 || result.WarningCount > 0 ? 2 : 0;
    }
}
