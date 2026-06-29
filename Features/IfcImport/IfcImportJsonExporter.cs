using System.IO;
using System.Text.Json;

namespace CSIModellingTools.Features.IfcImport;

public sealed class IfcImportJsonExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public void WriteResult(IfcImportResult result, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Choose a JSON output path before exporting the IFC import result.", nameof(outputPath));

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, JsonOptions));
    }
}
