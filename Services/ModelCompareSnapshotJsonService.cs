using System.IO;
using System.Text.Json;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class ModelCompareSnapshotJsonService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public ModelCompareSnapshotSaveResult SaveSnapshot(ModelCompareSnapshot snapshot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new ModelCompareSnapshotSaveResult
            {
                IsError = true,
                Message = "Choose a JSON file path before saving the model compare snapshot."
            };
        }

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(filePath, json);

            return new ModelCompareSnapshotSaveResult
            {
                IsError = false,
                Message = "Model compare snapshot saved.",
                FilePath = filePath
            };
        }
        catch (Exception ex)
        {
            return new ModelCompareSnapshotSaveResult
            {
                IsError = true,
                Message = $"Model compare snapshot could not be saved: {ex.Message}",
                FilePath = filePath
            };
        }
    }

    public ModelCompareSnapshotLoadResult LoadSnapshot(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new ModelCompareSnapshotLoadResult
            {
                IsError = true,
                Message = "Choose a JSON file path before loading a model compare snapshot."
            };
        }

        if (!File.Exists(filePath))
        {
            return new ModelCompareSnapshotLoadResult
            {
                IsError = true,
                Message = $"Model compare snapshot file was not found: {filePath}",
                FilePath = filePath
            };
        }

        try
        {
            string json = File.ReadAllText(filePath);
            ModelCompareSnapshot? snapshot = JsonSerializer.Deserialize<ModelCompareSnapshot>(json, JsonOptions);
            if (snapshot == null)
            {
                return new ModelCompareSnapshotLoadResult
                {
                    IsError = true,
                    Message = "Model compare snapshot file did not contain a valid snapshot.",
                    FilePath = filePath
                };
            }

            return new ModelCompareSnapshotLoadResult
            {
                IsError = false,
                Message = "Model compare snapshot loaded.",
                FilePath = filePath,
                Snapshot = snapshot
            };
        }
        catch (JsonException ex)
        {
            return new ModelCompareSnapshotLoadResult
            {
                IsError = true,
                Message = $"Model compare snapshot JSON is invalid: {ex.Message}",
                FilePath = filePath
            };
        }
        catch (Exception ex)
        {
            return new ModelCompareSnapshotLoadResult
            {
                IsError = true,
                Message = $"Model compare snapshot could not be loaded: {ex.Message}",
                FilePath = filePath
            };
        }
    }
}

public sealed class ModelCompareSnapshotSaveResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public string FilePath { get; set; } = "";
}

public sealed class ModelCompareSnapshotLoadResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public string FilePath { get; set; } = "";
    public ModelCompareSnapshot? Snapshot { get; set; }
}
