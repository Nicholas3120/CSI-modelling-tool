using System.IO;
using System.Text.Json;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

internal static class CityOfTomorrowManifestRepository
{
    public static void Save(CityOfTomorrowGenerationManifest manifest)
    {
        string path = GetPath(manifest.StructureId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete(string structureId)
    {
        string path = GetPath(structureId);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string GetPath(string structureId)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSI Modelling Tools", "City of Tomorrow");
        return Path.Combine(root, $"{EtabsNameUtility.BuildSafeName("", structureId)}.json");
    }
}
