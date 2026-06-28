using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class ModelCompareSnapshotJsonService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ModelCompareSnapshotSaveResult SaveSnapshot(ModelCompareSnapshot snapshot, string filePath)
    {
        if (snapshot == null)
        {
            return new ModelCompareSnapshotSaveResult
            {
                IsError = true,
                Message = "No model compare snapshot was provided for saving."
            };
        }

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

            ModelCompareSnapshotValidationResult validation = ValidateAndNormalizeSnapshot(snapshot);
            if (validation.IsError)
            {
                return new ModelCompareSnapshotLoadResult
                {
                    IsError = true,
                    Message = validation.Message,
                    FilePath = filePath,
                    Warnings = validation.Warnings
                };
            }

            return new ModelCompareSnapshotLoadResult
            {
                IsError = false,
                Message = validation.Message,
                FilePath = filePath,
                Snapshot = snapshot,
                Warnings = validation.Warnings
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

    private static ModelCompareSnapshotValidationResult ValidateAndNormalizeSnapshot(ModelCompareSnapshot snapshot)
    {
        var warnings = new List<string>();

        if (snapshot.Metadata == null)
        {
            return new ModelCompareSnapshotValidationResult
            {
                IsError = true,
                Message = "Model compare snapshot metadata is missing. The file cannot be compared safely."
            };
        }

        if (snapshot.Frames == null)
        {
            return new ModelCompareSnapshotValidationResult
            {
                IsError = true,
                Message = "Model compare snapshot frame data is missing. The file cannot be compared safely."
            };
        }

        if (snapshot.Metadata.SchemaVersion <= 0)
        {
            warnings.Add("Snapshot has no schema version and is treated as an older snapshot. Area objects or other newer categories may be unavailable.");
        }
        else if (snapshot.Metadata.SchemaVersion < ModelCompareSchema.CurrentVersion)
        {
            warnings.Add($"Snapshot schema version {snapshot.Metadata.SchemaVersion} is older than current version {ModelCompareSchema.CurrentVersion}. Some comparison categories may be unavailable.");
        }
        else if (snapshot.Metadata.SchemaVersion > ModelCompareSchema.CurrentVersion)
        {
            warnings.Add($"Snapshot schema version {snapshot.Metadata.SchemaVersion} is newer than this application supports. Unknown categories will be ignored.");
        }

        snapshot.Metadata.ProductName ??= "";
        snapshot.Metadata.SourceModelFileName ??= "";
        snapshot.Metadata.Units ??= "";
        snapshot.Metadata.LengthUnit ??= "";
        snapshot.Metadata.ForceUnit ??= "";
        snapshot.Metadata.StressUnit ??= "";
        snapshot.Metadata.UnitWeightUnit ??= "";
        snapshot.Metadata.UnitWeightConvention ??= "";
        snapshot.Metadata.ExtractionWarnings ??= [];

        NormalizeReadStatuses(snapshot.Metadata);
        if (HasUnknownCompleteness(snapshot.Metadata))
        {
            warnings.Add("Snapshot category completeness is unknown because this is a legacy snapshot or status metadata is missing. Unsafe categories will not be compared.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Metadata.LengthUnit) ||
            string.IsNullOrWhiteSpace(snapshot.Metadata.ForceUnit) ||
            string.IsNullOrWhiteSpace(snapshot.Metadata.StressUnit) ||
            string.IsNullOrWhiteSpace(snapshot.Metadata.UnitWeightUnit))
        {
            warnings.Add("Snapshot canonical unit metadata is unavailable. Values are loaded for compatibility, but their stored unit convention cannot be verified.");
        }

        warnings.AddRange(snapshot.Metadata.ExtractionWarnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => "Snapshot extraction warning: " + warning.Trim()));
        snapshot.Frames = snapshot.Frames.Where(frame => frame != null).ToList();

        if (snapshot.Areas == null)
        {
            snapshot.Areas = [];
            snapshot.Metadata.AreasReadStatus = ModelCompareSnapshotReadStatus.Failed;
            warnings.Add("Snapshot area object data is unavailable and was normalized to an empty list.");
        }
        else
        {
            snapshot.Areas = snapshot.Areas.Where(area => area != null).ToList();
            foreach (ModelCompareAreaObjectSnapshot area in snapshot.Areas)
            {
                area.Corners ??= [];
                area.GroupNames ??= [];
            }
        }

        if (snapshot.FrameProperties == null)
        {
            snapshot.FrameProperties = [];
            snapshot.Metadata.FramePropertiesReadStatus = ModelCompareSnapshotReadStatus.Failed;
            warnings.Add("Snapshot frame property definitions are unavailable and were normalized to an empty list.");
        }
        else
        {
            snapshot.FrameProperties = snapshot.FrameProperties.Where(property => property != null).ToList();
        }

        if (snapshot.AreaProperties == null)
        {
            snapshot.AreaProperties = [];
            snapshot.Metadata.AreaPropertiesReadStatus = ModelCompareSnapshotReadStatus.Failed;
            warnings.Add("Snapshot area property definitions are unavailable and were normalized to an empty list.");
        }
        else
        {
            snapshot.AreaProperties = snapshot.AreaProperties.Where(property => property != null).ToList();
        }

        if (snapshot.Materials == null)
        {
            snapshot.Materials = [];
            snapshot.Metadata.MaterialsReadStatus = ModelCompareSnapshotReadStatus.Failed;
            warnings.Add("Snapshot material definitions are unavailable and were normalized to an empty list.");
        }
        else
        {
            snapshot.Materials = snapshot.Materials.Where(material => material != null).ToList();
        }

        foreach (ModelCompareFrameSnapshot frame in snapshot.Frames)
            frame.GroupNames ??= [];

        return new ModelCompareSnapshotValidationResult
        {
            IsError = false,
            Message = warnings.Count == 0
                ? "Model compare snapshot loaded."
                : $"Model compare snapshot loaded with {warnings.Count} compatibility warning(s).",
            Warnings = warnings
        };
    }

    private static void NormalizeReadStatuses(ModelCompareSnapshotMetadata metadata)
    {
        metadata.FramesReadStatus = NormalizeReadStatus(metadata.FramesReadStatus);
        metadata.AreasReadStatus = NormalizeReadStatus(metadata.AreasReadStatus);
        metadata.FramePropertiesReadStatus = NormalizeReadStatus(metadata.FramePropertiesReadStatus);
        metadata.AreaPropertiesReadStatus = NormalizeReadStatus(metadata.AreaPropertiesReadStatus);
        metadata.MaterialsReadStatus = NormalizeReadStatus(metadata.MaterialsReadStatus);
        metadata.GroupsReadStatus = NormalizeReadStatus(metadata.GroupsReadStatus);
    }

    private static ModelCompareSnapshotReadStatus NormalizeReadStatus(ModelCompareSnapshotReadStatus status)
    {
        return Enum.IsDefined(typeof(ModelCompareSnapshotReadStatus), status)
            ? status
            : ModelCompareSnapshotReadStatus.Unknown;
    }

    private static bool HasUnknownCompleteness(ModelCompareSnapshotMetadata metadata)
    {
        return metadata.FramesReadStatus == ModelCompareSnapshotReadStatus.Unknown ||
            metadata.AreasReadStatus == ModelCompareSnapshotReadStatus.Unknown ||
            metadata.FramePropertiesReadStatus == ModelCompareSnapshotReadStatus.Unknown ||
            metadata.AreaPropertiesReadStatus == ModelCompareSnapshotReadStatus.Unknown ||
            metadata.MaterialsReadStatus == ModelCompareSnapshotReadStatus.Unknown ||
            metadata.GroupsReadStatus == ModelCompareSnapshotReadStatus.Unknown;
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
    public List<string> Warnings { get; set; } = [];
}

internal sealed class ModelCompareSnapshotValidationResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
}
