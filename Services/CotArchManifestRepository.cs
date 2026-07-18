using System.IO;
using System.Text.Json;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

internal static class CotArchManifestRepository
{
    public static CotArchGenerationManifest? TryLoad(string modelPrefix)
    {
        string path = GetPath(modelPrefix);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<CotArchGenerationManifest>(File.ReadAllText(path));
    }

    public static void Save(CotArchGenerationManifest manifest)
    {
        string path = GetPath(manifest.ModelPrefix);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete(string modelPrefix)
    {
        string path = GetPath(modelPrefix);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetPath(string modelPrefix)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSI Modelling Tools", "CoT Arch");
        return Path.Combine(root, $"{EtabsNameUtility.BuildSafeName("", modelPrefix)}.json");
    }
}
