using System.Globalization;
using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class ModelCompareService
{
    public List<ModelCompareResultRow> CompareSnapshots(
        ModelCompareSnapshot oldSnapshot,
        ModelCompareSnapshot newSnapshot,
        ModelCompareToleranceSettings? tolerances = null)
    {
        ModelCompareToleranceSettings settings = NormalizeTolerances(tolerances);
        var results = new List<ModelCompareResultRow>();

        CompareFrames(oldSnapshot.Frames, newSnapshot.Frames, settings, results);
        CompareFrameProperties(oldSnapshot.FrameProperties, newSnapshot.FrameProperties, settings, results);
        CompareAreaProperties(oldSnapshot.AreaProperties, newSnapshot.AreaProperties, settings, results);
        CompareMaterials(oldSnapshot.Materials, newSnapshot.Materials, settings, results);

        return results
            .OrderBy(row => row.ObjectType)
            .ThenBy(row => row.ObjectDescription, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.OldValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.NewValue, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CompareFrames(
        IReadOnlyList<ModelCompareFrameSnapshot> oldFrames,
        IReadOnlyList<ModelCompareFrameSnapshot> newFrames,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        List<ModelCompareFrameSnapshot> unmatchedNewFrames = newFrames
            .OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (ModelCompareFrameSnapshot oldFrame in oldFrames.OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase))
        {
            ModelCompareFrameSnapshot? newFrame = unmatchedNewFrames.FirstOrDefault(candidate => IsSamePhysicalFrame(oldFrame, candidate, settings.CoordinateTolerance));
            if (newFrame == null)
            {
                results.Add(BuildResult(
                    ModelCompareChangeType.Removed,
                    ModelCompareObjectType.Frame,
                    DescribeFrame(oldFrame),
                    oldFrame.FrameName,
                    "",
                    ModelCompareChangeImportance.High));
                continue;
            }

            unmatchedNewFrames.Remove(newFrame);
            CompareMatchedFrame(oldFrame, newFrame, settings, results);
        }

        foreach (ModelCompareFrameSnapshot newFrame in unmatchedNewFrames)
        {
            results.Add(BuildResult(
                ModelCompareChangeType.Added,
                ModelCompareObjectType.Frame,
                DescribeFrame(newFrame),
                "",
                newFrame.FrameName,
                ModelCompareChangeImportance.High));
        }
    }

    private static void CompareMatchedFrame(
        ModelCompareFrameSnapshot oldFrame,
        ModelCompareFrameSnapshot newFrame,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        string description = $"{DescribeFrame(newFrame)} matched to {oldFrame.FrameName}";

        AddStringDifference(
            results,
            ModelCompareObjectType.Frame,
            $"{description} / section",
            oldFrame.SectionName,
            newFrame.SectionName,
            ModelCompareChangeImportance.High);

        AddStringDifference(
            results,
            ModelCompareObjectType.Frame,
            $"{description} / material",
            oldFrame.MaterialName,
            newFrame.MaterialName,
            ModelCompareChangeImportance.Medium);

        AddNumericDifference(
            results,
            ModelCompareObjectType.Frame,
            $"{description} / length",
            oldFrame.Length,
            newFrame.Length,
            settings.LengthTolerance,
            ModelCompareChangeImportance.Medium);
    }

    private static bool IsSamePhysicalFrame(ModelCompareFrameSnapshot oldFrame, ModelCompareFrameSnapshot newFrame, double coordinateTolerance)
    {
        return PointsMatch(oldFrame.IX, oldFrame.IY, oldFrame.IZ, newFrame.IX, newFrame.IY, newFrame.IZ, coordinateTolerance) &&
            PointsMatch(oldFrame.JX, oldFrame.JY, oldFrame.JZ, newFrame.JX, newFrame.JY, newFrame.JZ, coordinateTolerance) ||
            PointsMatch(oldFrame.IX, oldFrame.IY, oldFrame.IZ, newFrame.JX, newFrame.JY, newFrame.JZ, coordinateTolerance) &&
            PointsMatch(oldFrame.JX, oldFrame.JY, oldFrame.JZ, newFrame.IX, newFrame.IY, newFrame.IZ, coordinateTolerance);
    }

    private static bool PointsMatch(
        double ax,
        double ay,
        double az,
        double bx,
        double by,
        double bz,
        double coordinateTolerance)
    {
        return Math.Abs(ax - bx) <= coordinateTolerance &&
            Math.Abs(ay - by) <= coordinateTolerance &&
            Math.Abs(az - bz) <= coordinateTolerance;
    }

    private static void CompareFrameProperties(
        IReadOnlyList<ModelCompareFramePropertySnapshot> oldProperties,
        IReadOnlyList<ModelCompareFramePropertySnapshot> newProperties,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        Dictionary<string, ModelCompareFramePropertySnapshot> oldByName = BuildUniqueLookup(oldProperties, property => property.SectionName);
        Dictionary<string, ModelCompareFramePropertySnapshot> newByName = BuildUniqueLookup(newProperties, property => property.SectionName);

        foreach (string name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            ModelCompareFramePropertySnapshot oldProperty = oldByName[name];
            ModelCompareFramePropertySnapshot newProperty = newByName[name];
            string description = $"Frame section '{name}'";

            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / depth", oldProperty.Depth, newProperty.Depth, settings.DimensionTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / width", oldProperty.Width, newProperty.Width, settings.DimensionTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / flange thickness", oldProperty.FlangeThickness, newProperty.FlangeThickness, settings.DimensionTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.FrameProperty, $"{description} / web thickness", oldProperty.WebThickness, newProperty.WebThickness, settings.DimensionTolerance, ModelCompareChangeImportance.High);
        }
    }

    private static void CompareAreaProperties(
        IReadOnlyList<ModelCompareAreaPropertySnapshot> oldProperties,
        IReadOnlyList<ModelCompareAreaPropertySnapshot> newProperties,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        Dictionary<string, ModelCompareAreaPropertySnapshot> oldByName = BuildUniqueLookup(oldProperties, property => property.PropertyName);
        Dictionary<string, ModelCompareAreaPropertySnapshot> newByName = BuildUniqueLookup(newProperties, property => property.PropertyName);

        foreach (string name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            AddNumericDifference(
                results,
                ModelCompareObjectType.AreaProperty,
                $"Area property '{name}' / thickness",
                oldByName[name].Thickness,
                newByName[name].Thickness,
                settings.DimensionTolerance,
                ModelCompareChangeImportance.High);
        }
    }

    private static void CompareMaterials(
        IReadOnlyList<ModelCompareMaterialSnapshot> oldMaterials,
        IReadOnlyList<ModelCompareMaterialSnapshot> newMaterials,
        ModelCompareToleranceSettings settings,
        List<ModelCompareResultRow> results)
    {
        Dictionary<string, ModelCompareMaterialSnapshot> oldByName = BuildUniqueLookup(oldMaterials, material => material.MaterialName);
        Dictionary<string, ModelCompareMaterialSnapshot> newByName = BuildUniqueLookup(newMaterials, material => material.MaterialName);

        foreach (string name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            ModelCompareMaterialSnapshot oldMaterial = oldByName[name];
            ModelCompareMaterialSnapshot newMaterial = newByName[name];
            string description = $"Material '{name}'";

            AddStringDifference(results, ModelCompareObjectType.Material, $"{description} / type", oldMaterial.MaterialType, newMaterial.MaterialType, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.Material, $"{description} / elastic modulus", oldMaterial.ElasticModulus, newMaterial.ElasticModulus, settings.MaterialPropertyTolerance, ModelCompareChangeImportance.High);
            AddNumericDifference(results, ModelCompareObjectType.Material, $"{description} / Poisson ratio", oldMaterial.PoissonRatio, newMaterial.PoissonRatio, settings.MaterialPropertyTolerance, ModelCompareChangeImportance.Medium);
            AddNumericDifference(results, ModelCompareObjectType.Material, $"{description} / unit weight", oldMaterial.UnitWeight, newMaterial.UnitWeight, settings.MaterialPropertyTolerance, ModelCompareChangeImportance.Medium);
        }
    }

    private static Dictionary<string, Queue<ModelCompareFrameSnapshot>> BuildFrameLookup(
        IEnumerable<ModelCompareFrameSnapshot> frames,
        ModelCompareToleranceSettings settings)
    {
        var lookup = new Dictionary<string, Queue<ModelCompareFrameSnapshot>>(StringComparer.Ordinal);

        foreach (ModelCompareFrameSnapshot frame in frames.OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase))
        {
            string key = BuildFrameCoordinateKey(frame, settings.CoordinateTolerance);
            if (!lookup.TryGetValue(key, out Queue<ModelCompareFrameSnapshot>? queue))
            {
                queue = new Queue<ModelCompareFrameSnapshot>();
                lookup[key] = queue;
            }

            queue.Enqueue(frame);
        }

        return lookup;
    }

    private static string BuildFrameCoordinateKey(ModelCompareFrameSnapshot frame, double tolerance)
    {
        string iKey = BuildPointCoordinateKey(frame.IX, frame.IY, frame.IZ, tolerance);
        string jKey = BuildPointCoordinateKey(frame.JX, frame.JY, frame.JZ, tolerance);

        return string.CompareOrdinal(iKey, jKey) <= 0
            ? $"{iKey}|{jKey}"
            : $"{jKey}|{iKey}";
    }

    private static string BuildPointCoordinateKey(double x, double y, double z, double tolerance)
    {
        return $"{Quantize(x, tolerance)},{Quantize(y, tolerance)},{Quantize(z, tolerance)}";
    }

    private static long Quantize(double value, double tolerance)
    {
        if (!double.IsFinite(value))
            return 0;

        return (long)Math.Round(value / tolerance, MidpointRounding.AwayFromZero);
    }

    private static Dictionary<string, T> BuildUniqueLookup<T>(IEnumerable<T> values, Func<T, string> keySelector)
    {
        return values
            .Select(value => new { Value = value, Key = (keySelector(value) ?? "").Trim() })
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddStringDifference(
        List<ModelCompareResultRow> results,
        ModelCompareObjectType objectType,
        string description,
        string oldValue,
        string newValue,
        ModelCompareChangeImportance importance)
    {
        if (string.Equals((oldValue ?? "").Trim(), (newValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        results.Add(BuildResult(
            ModelCompareChangeType.Modified,
            objectType,
            description,
            oldValue ?? "",
            newValue ?? "",
            importance));
    }

    private static void AddNumericDifference(
        List<ModelCompareResultRow> results,
        ModelCompareObjectType objectType,
        string description,
        double oldValue,
        double newValue,
        double tolerance,
        ModelCompareChangeImportance importance)
    {
        if (!double.IsFinite(oldValue) && !double.IsFinite(newValue))
            return;

        if (Math.Abs(oldValue - newValue) <= tolerance)
            return;

        results.Add(BuildResult(
            ModelCompareChangeType.Modified,
            objectType,
            description,
            FormatNumber(oldValue),
            FormatNumber(newValue),
            importance));
    }

    private static ModelCompareResultRow BuildResult(
        ModelCompareChangeType changeType,
        ModelCompareObjectType objectType,
        string description,
        string oldValue,
        string newValue,
        ModelCompareChangeImportance importance)
    {
        return new ModelCompareResultRow
        {
            ChangeType = changeType,
            ObjectType = objectType,
            ObjectDescription = description,
            OldValue = oldValue ?? "",
            NewValue = newValue ?? "",
            Importance = importance,
            Confidence = 1.0
        };
    }

    private static string DescribeFrame(ModelCompareFrameSnapshot frame)
    {
        return string.IsNullOrWhiteSpace(frame.FrameName)
            ? $"Frame at ({FormatNumber(frame.IX)}, {FormatNumber(frame.IY)}, {FormatNumber(frame.IZ)}) to ({FormatNumber(frame.JX)}, {FormatNumber(frame.JY)}, {FormatNumber(frame.JZ)})"
            : $"Frame '{frame.FrameName}'";
    }

    private static string FormatNumber(double value)
    {
        return double.IsFinite(value)
            ? value.ToString("0.########", CultureInfo.InvariantCulture)
            : "";
    }

    private static ModelCompareToleranceSettings NormalizeTolerances(ModelCompareToleranceSettings? tolerances)
    {
        ModelCompareToleranceSettings settings = tolerances ?? new ModelCompareToleranceSettings();

        return new ModelCompareToleranceSettings
        {
            CoordinateTolerance = EnsurePositiveTolerance(settings.CoordinateTolerance, 0.001),
            LengthTolerance = EnsurePositiveTolerance(settings.LengthTolerance, 0.001),
            DimensionTolerance = EnsurePositiveTolerance(settings.DimensionTolerance, 0.001),
            MaterialPropertyTolerance = EnsurePositiveTolerance(settings.MaterialPropertyTolerance, 0.001)
        };
    }

    private static double EnsurePositiveTolerance(double value, double defaultValue)
    {
        return double.IsFinite(value) && value > 0
            ? value
            : defaultValue;
    }
}
