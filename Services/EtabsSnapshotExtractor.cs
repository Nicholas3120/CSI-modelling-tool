using CSIModellingTools.Models;
using static CSIModellingTools.Services.EtabsParametricModellingService;

namespace CSIModellingTools.Services;

internal sealed class EtabsSnapshotExtractor
{
    private const ETABSv1.eUnits CanonicalUnits = ETABSv1.eUnits.kN_m_C;
    private const int EtabsSelectedFrameObjectType = 2;
    private const int EtabsSelectedAreaObjectType = 5;

    private readonly ETABSv1.cSapModel _sapModel;
    private readonly string _sourceModelFileName;

    public EtabsSnapshotExtractor(ETABSv1.cSapModel sapModel, string sourceModelFileName)
    {
        _sapModel = sapModel ?? throw new ArgumentNullException(nameof(sapModel));
        _sourceModelFileName = sourceModelFileName ?? "";
    }

    public EtabsSnapshotExtractionResult Extract()
    {
        var framePropertyWarnings = new List<string>();
        bool framePropertyNamesRead = TryGetFrameSectionNamesForSnapshot(_sapModel, framePropertyWarnings, out List<string> frameSectionNames);
        List<ModelCompareFramePropertySnapshot> frameProperties = framePropertyNamesRead
            ? GetModelCompareFrameProperties(_sapModel, framePropertyWarnings, frameSectionNames)
            : [];
        Dictionary<string, string> materialBySection = frameProperties
            .Where(section => !string.IsNullOrWhiteSpace(section.SectionName))
            .GroupBy(section => section.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().MaterialName, StringComparer.OrdinalIgnoreCase);

        var groupWarnings = new List<string>();
        bool groupNamesRead = TryGetGroupNamesForSnapshot(_sapModel, groupWarnings, out List<string> groupNames);
        Dictionary<string, List<string>> groupsByFrameName = groupNamesRead
            ? GetModelCompareFrameGroups(_sapModel, groupWarnings, groupNames)
            : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> groupsByAreaName = groupNamesRead
            ? GetModelCompareAreaGroups(_sapModel, groupWarnings, groupNames)
            : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var frameWarnings = new List<string>();
        bool frameNamesRead = TryGetAllFrameNamesForSnapshot(_sapModel, frameWarnings, out List<string> frameNames);
        if (!framePropertyNamesRead)
            frameWarnings.Add("Frame section material data is unavailable because frame property definitions could not be read.");
        List<ModelCompareFrameSnapshot> frames = (frameNamesRead ? frameNames : [])
            .Select(frameName => ReadModelCompareFrameSnapshot(_sapModel, frameName, materialBySection, groupsByFrameName, frameWarnings))
            .Where(frame => frame != null)
            .Select(frame => frame!)
            .OrderBy(frame => frame.FrameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool framesRead = frameNamesRead && frames.Count == frameNames.Count;
        if (frameNamesRead && frames.Count != frameNames.Count)
            frameWarnings.Add($"Frame extraction was marked failed because {frameNames.Count - frames.Count} of {frameNames.Count} listed ETABS frames had unreadable geometry.");

        var areaPropertyWarnings = new List<string>();
        bool areaPropertyNamesRead = TryGetAreaPropertyNamesForSnapshot(_sapModel, areaPropertyWarnings, out List<string> areaPropertyNames);
        List<ModelCompareAreaPropertySnapshot> areaProperties = areaPropertyNamesRead
            ? GetAreaPropertyRows(_sapModel, areaPropertyWarnings, areaPropertyNames)
                .Select(MapModelCompareAreaProperty)
                .ToList()
            : [];
        Dictionary<string, ModelCompareAreaPropertySnapshot> areaPropertyByName = areaProperties
            .Where(property => !string.IsNullOrWhiteSpace(property.PropertyName))
            .GroupBy(property => property.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var areaWarnings = new List<string>();
        bool areaNamesRead = TryGetAllAreaNamesForSnapshot(_sapModel, areaWarnings, out List<string> areaNames);
        if (!areaPropertyNamesRead)
            areaWarnings.Add("Area material and thickness data are unavailable because area property definitions could not be read.");
        List<ModelCompareAreaObjectSnapshot> areas = (areaNamesRead ? areaNames : [])
            .Select(areaName => ReadModelCompareAreaSnapshot(_sapModel, areaName, areaPropertyByName, groupsByAreaName, areaWarnings))
            .Where(area => area != null)
            .Select(area => area!)
            .OrderBy(area => area.AreaName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool areasRead = areaNamesRead && areas.Count == areaNames.Count;
        if (areaNamesRead && areas.Count != areaNames.Count)
            areaWarnings.Add($"Area extraction was marked failed because {areaNames.Count - areas.Count} of {areaNames.Count} listed ETABS areas had unreadable geometry.");

        var materialWarnings = new List<string>();
        bool materialNamesRead = TryGetMaterialNamesForSnapshot(_sapModel, materialWarnings, out List<string> materialNames);
        List<ModelCompareMaterialSnapshot> materials = materialNamesRead
            ? GetMaterialPropertyRows(_sapModel, materialWarnings, materialNames)
                .Select(MapModelCompareMaterial)
                .ToList()
            : [];

        var extractionWarnings = new List<string>();
        extractionWarnings.AddRange(frameWarnings);
        extractionWarnings.AddRange(areaWarnings);
        extractionWarnings.AddRange(framePropertyWarnings);
        extractionWarnings.AddRange(areaPropertyWarnings);
        extractionWarnings.AddRange(materialWarnings);
        extractionWarnings.AddRange(groupWarnings);
        extractionWarnings = extractionWarnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var snapshot = new ModelCompareSnapshot
        {
            Metadata = new ModelCompareSnapshotMetadata
            {
                SchemaVersion = ModelCompareSchema.CurrentVersion,
                ProductName = TryGetEtabsProductName(_sapModel),
                SourceModelFileName = _sourceModelFileName,
                SnapshotCreatedAt = DateTimeOffset.Now,
                Units = CanonicalUnits.ToString(),
                LengthUnit = "m",
                ForceUnit = "kN",
                StressUnit = "MPa",
                UnitWeightUnit = "kN/m^3",
                UnitWeightConvention = "Weight per unit volume",
                FramesReadStatus = BuildModelCompareReadStatus(framesRead, frameWarnings),
                AreasReadStatus = BuildModelCompareReadStatus(areasRead, areaWarnings),
                FramePropertiesReadStatus = BuildModelCompareReadStatus(framePropertyNamesRead, framePropertyWarnings),
                AreaPropertiesReadStatus = BuildModelCompareReadStatus(areaPropertyNamesRead, areaPropertyWarnings),
                MaterialsReadStatus = BuildModelCompareReadStatus(materialNamesRead, materialWarnings),
                GroupsReadStatus = BuildModelCompareReadStatus(groupNamesRead, groupWarnings),
                ExtractionWarnings = extractionWarnings
            },
            Frames = frames,
            Areas = areas,
            FrameProperties = frameProperties,
            AreaProperties = areaProperties,
            Materials = materials
        };

        return new EtabsSnapshotExtractionResult
        {
            Snapshot = snapshot,
            Warnings = extractionWarnings
        };
    }

    private static ModelCompareSnapshotReadStatus BuildModelCompareReadStatus(bool readSucceeded, IReadOnlyCollection<string> warnings)
    {
        if (!readSucceeded)
            return ModelCompareSnapshotReadStatus.Failed;

        return warnings.Count == 0
            ? ModelCompareSnapshotReadStatus.Success
            : ModelCompareSnapshotReadStatus.SuccessWithWarnings;
    }

    private static bool TryGetAllFrameNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.FrameObj.GetNameList(ref numberNames, ref rawNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS frame object list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS frame object list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetAllAreaNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.AreaObj.GetNameList(ref numberNames, ref rawNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS area object list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS area object list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetAreaPropertyNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.PropArea.GetNameList(ref numberNames, ref rawNames, 0);
            if (ret != 0)
            {
                warnings.Add($"ETABS area property list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS area property list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetGroupNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        int numberNames = 0;
        string[] rawNames = [];
        try
        {
            int ret = sapModel.GroupDef.GetNameList(ref numberNames, ref rawNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS group list could not be loaded for snapshot extraction. Return code: {ret}.");
                names = [];
                return false;
            }

            names = NormalizeModelCompareNames(numberNames, rawNames);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS group list could not be loaded for snapshot extraction: " + ex.Message);
            names = [];
            return false;
        }
    }

    private static bool TryGetFrameSectionNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        var collectedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int successfulCalls = 0;

        foreach (ETABSv1.eFramePropType propType in Enum.GetValues(typeof(ETABSv1.eFramePropType)).Cast<ETABSv1.eFramePropType>())
        {
            int numberNames = 0;
            string[] rawNames = [];
            try
            {
                int ret = sapModel.PropFrame.GetNameList(ref numberNames, ref rawNames, propType);
                if (ret != 0)
                    continue;

                successfulCalls++;
                foreach (string name in NormalizeModelCompareNames(numberNames, rawNames))
                    collectedNames.Add(name);
            }
            catch
            {
                // Missing property families are expected; the category fails only if every family call fails.
            }
        }

        names = collectedNames.ToList();
        if (successfulCalls > 0)
            return true;

        warnings.Add("ETABS frame property definition list could not be loaded for snapshot extraction.");
        return false;
    }

    private static bool TryGetMaterialNamesForSnapshot(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        out List<string> names)
    {
        var collectedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int successfulCalls = 0;

        foreach (ETABSv1.eMatType materialType in Enum.GetValues(typeof(ETABSv1.eMatType)).Cast<ETABSv1.eMatType>())
        {
            int numberNames = 0;
            string[] rawNames = [];
            try
            {
                int ret = sapModel.PropMaterial.GetNameList(ref numberNames, ref rawNames, materialType);
                if (ret != 0)
                    continue;

                successfulCalls++;
                foreach (string name in NormalizeModelCompareNames(numberNames, rawNames))
                    collectedNames.Add(name);
            }
            catch
            {
                // Missing material families are expected; the category fails only if every family call fails.
            }
        }

        names = collectedNames.ToList();
        if (successfulCalls > 0)
            return true;

        warnings.Add("ETABS material definition list could not be loaded for snapshot extraction.");
        return false;
    }

    private static List<string> NormalizeModelCompareNames(int numberNames, string[] names)
    {
        return names
            .Take(Math.Min(numberNames, names.Length))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModelCompareFrameSnapshot? ReadModelCompareFrameSnapshot(
        ETABSv1.cSapModel sapModel,
        string frameName,
        IReadOnlyDictionary<string, string> materialBySection,
        IReadOnlyDictionary<string, List<string>> groupsByFrameName,
        List<string> warnings)
    {
        string label = "";
        string story = "";
        try
        {
            sapModel.FrameObj.GetLabelFromName(frameName, ref label, ref story);
        }
        catch
        {
            // Label/story are optional display fields.
        }

        string currentSection = "";
        try
        {
            string autoSelectList = "";
            int ret = sapModel.FrameObj.GetSection(frameName, ref currentSection, ref autoSelectList);
            if (ret != 0)
                warnings.Add($"ETABS could not read current section for frame '{frameName}'. Return code: {ret}.");

            if (string.IsNullOrWhiteSpace(currentSection))
                currentSection = autoSelectList;
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS could not read current section for frame '{frameName}': {ex.Message}");
        }

        string pointI = "";
        string pointJ = "";
        int pointsRet;
        try
        {
            pointsRet = sapModel.FrameObj.GetPoints(frameName, ref pointI, ref pointJ);
        }
        catch (Exception ex)
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because its endpoint names could not be read: {ex.Message}");
            return null;
        }

        if (pointsRet != 0 || string.IsNullOrWhiteSpace(pointI) || string.IsNullOrWhiteSpace(pointJ))
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because its endpoint names could not be read. Return code: {pointsRet}.");
            return null;
        }

        (double X, double Y, double Z) pointICoord;
        (double X, double Y, double Z) pointJCoord;
        try
        {
            pointICoord = GetPointCoordinates(sapModel, pointI);
            pointJCoord = GetPointCoordinates(sapModel, pointJ);
        }
        catch (Exception ex)
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because its endpoint coordinates could not be read: {ex.Message}");
            return null;
        }

        if (!double.IsFinite(pointICoord.X) || !double.IsFinite(pointICoord.Y) || !double.IsFinite(pointICoord.Z) ||
            !double.IsFinite(pointJCoord.X) || !double.IsFinite(pointJCoord.Y) || !double.IsFinite(pointJCoord.Z))
        {
            warnings.Add($"Frame '{frameName}' was skipped from the model compare snapshot because ETABS returned non-finite endpoint coordinates.");
            return null;
        }

        string materialName = materialBySection.TryGetValue(currentSection, out string? sectionMaterial)
            ? sectionMaterial
            : "";

        return new ModelCompareFrameSnapshot
        {
            FrameName = frameName,
            Label = label,
            Story = story,
            PointIName = pointI,
            PointJName = pointJ,
            IX = pointICoord.X,
            IY = pointICoord.Y,
            IZ = pointICoord.Z,
            JX = pointJCoord.X,
            JY = pointJCoord.Y,
            JZ = pointJCoord.Z,
            Length = CalculateFrameLength(pointICoord, pointJCoord),
            SectionName = currentSection,
            MaterialName = materialName,
            GroupNames = groupsByFrameName.TryGetValue(frameName, out List<string>? groupNames)
                ? groupNames.ToList()
                : []
        };
    }

    private static Dictionary<string, List<string>> GetModelCompareFrameGroups(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetModelCompareFrameGroups(sapModel, warnings, GetGroupNames(sapModel, warnings));
    }

    private static Dictionary<string, List<string>> GetModelCompareFrameGroups(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        IReadOnlyList<string> groupNames)
    {
        var groupsByFrameName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string groupName in groupNames)
        {
            int numberItems = 0;
            int[] objectTypes = [];
            string[] objectNames = [];

            try
            {
                int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
                if (ret != 0)
                {
                    warnings.Add($"ETABS group '{groupName}' assignments could not be read for model compare snapshot. Return code: {ret}.");
                    continue;
                }

                int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
                for (int index = 0; index < count; index++)
                {
                    if (objectTypes[index] != EtabsSelectedFrameObjectType)
                        continue;

                    string frameName = (objectNames[index] ?? "").Trim();
                    if (frameName.Length == 0)
                        continue;

                    if (!groupsByFrameName.TryGetValue(frameName, out List<string>? frameGroups))
                    {
                        frameGroups = [];
                        groupsByFrameName[frameName] = frameGroups;
                    }

                    if (!frameGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                        frameGroups.Add(groupName);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS group '{groupName}' assignments could not be read for model compare snapshot: {ex.Message}");
            }
        }

        foreach (List<string> frameGroups in groupsByFrameName.Values)
            frameGroups.Sort(StringComparer.OrdinalIgnoreCase);

        return groupsByFrameName;
    }

    private static List<string> GetAllAreaNames(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        int numberNames = 0;
        string[] names = [];
        try
        {
            int ret = sapModel.AreaObj.GetNameList(ref numberNames, ref names);
            if (ret != 0)
            {
                warnings.Add($"ETABS area object list could not be loaded. Return code: {ret}.");
                return [];
            }

            return names
                .Take(Math.Min(numberNames, names.Length))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            warnings.Add("ETABS area object list could not be loaded: " + ex.Message);
            return [];
        }
    }

    private static ModelCompareAreaObjectSnapshot? ReadModelCompareAreaSnapshot(
        ETABSv1.cSapModel sapModel,
        string areaName,
        IReadOnlyDictionary<string, ModelCompareAreaPropertySnapshot> areaPropertyByName,
        IReadOnlyDictionary<string, List<string>> groupsByAreaName,
        List<string> warnings)
    {
        string label = "";
        string story = "";
        try
        {
            sapModel.AreaObj.GetLabelFromName(areaName, ref label, ref story);
        }
        catch
        {
            // Label/story are optional display fields.
        }

        string propertyName = "";
        try
        {
            int ret = sapModel.AreaObj.GetProperty(areaName, ref propertyName);
            if (ret != 0)
                warnings.Add($"ETABS could not read area property assignment for area '{areaName}'. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS area property assignment read failed for area '{areaName}': {ex.Message}");
        }

        List<ModelComparePointSnapshot> corners = ReadModelCompareAreaCorners(sapModel, areaName, warnings);
        if (corners.Count < 3)
        {
            warnings.Add($"ETABS area '{areaName}' was skipped in model compare snapshot because fewer than three corner points were readable.");
            return null;
        }

        areaPropertyByName.TryGetValue(propertyName, out ModelCompareAreaPropertySnapshot? areaProperty);

        return new ModelCompareAreaObjectSnapshot
        {
            AreaName = areaName,
            Label = label,
            Story = story,
            PropertyName = propertyName,
            MaterialName = areaProperty?.MaterialName ?? "",
            Thickness = areaProperty?.Thickness ?? 0,
            Corners = corners,
            GroupNames = groupsByAreaName.TryGetValue(areaName, out List<string>? groupNames)
                ? groupNames.ToList()
                : []
        };
    }

    private static List<ModelComparePointSnapshot> ReadModelCompareAreaCorners(ETABSv1.cSapModel sapModel, string areaName, List<string> warnings)
    {
        int numberPoints = 0;
        string[] pointNames = [];
        try
        {
            int ret = sapModel.AreaObj.GetPoints(areaName, ref numberPoints, ref pointNames);
            if (ret != 0)
            {
                warnings.Add($"ETABS could not read corner points for area '{areaName}'. Return code: {ret}.");
                return [];
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"ETABS corner point read failed for area '{areaName}': {ex.Message}");
            return [];
        }

        var corners = new List<ModelComparePointSnapshot>();
        int count = Math.Min(numberPoints, pointNames.Length);
        for (int index = 0; index < count; index++)
        {
            string pointName = (pointNames[index] ?? "").Trim();
            if (pointName.Length == 0)
                continue;

            try
            {
                (double X, double Y, double Z) point = GetPointCoordinates(sapModel, pointName);
                corners.Add(new ModelComparePointSnapshot
                {
                    PointName = pointName,
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z
                });
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS corner point '{pointName}' coordinates for area '{areaName}' could not be read: {ex.Message}");
            }
        }

        return corners;
    }

    private static Dictionary<string, List<string>> GetModelCompareAreaGroups(ETABSv1.cSapModel sapModel, List<string> warnings)
    {
        return GetModelCompareAreaGroups(sapModel, warnings, GetGroupNames(sapModel, warnings));
    }

    private static Dictionary<string, List<string>> GetModelCompareAreaGroups(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        IReadOnlyList<string> groupNames)
    {
        var groupsByAreaName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string groupName in groupNames)
        {
            int numberItems = 0;
            int[] objectTypes = [];
            string[] objectNames = [];

            try
            {
                int ret = sapModel.GroupDef.GetAssignments(groupName, ref numberItems, ref objectTypes, ref objectNames);
                if (ret != 0)
                {
                    warnings.Add($"ETABS group '{groupName}' assignments could not be read for model compare area snapshot. Return code: {ret}.");
                    continue;
                }

                int count = Math.Min(numberItems, Math.Min(objectTypes.Length, objectNames.Length));
                for (int index = 0; index < count; index++)
                {
                    if (objectTypes[index] != EtabsSelectedAreaObjectType)
                        continue;

                    string areaName = (objectNames[index] ?? "").Trim();
                    if (areaName.Length == 0)
                        continue;

                    if (!groupsByAreaName.TryGetValue(areaName, out List<string>? areaGroups))
                    {
                        areaGroups = [];
                        groupsByAreaName[areaName] = areaGroups;
                    }

                    if (!areaGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                        areaGroups.Add(groupName);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"ETABS group '{groupName}' area assignments could not be read for model compare snapshot: {ex.Message}");
            }
        }

        foreach (List<string> areaGroups in groupsByAreaName.Values)
            areaGroups.Sort(StringComparer.OrdinalIgnoreCase);

        return groupsByAreaName;
    }

    private static List<ModelCompareFramePropertySnapshot> GetModelCompareFrameProperties(
        ETABSv1.cSapModel sapModel,
        List<string> warnings,
        IReadOnlyList<string>? frameSectionNames = null)
    {
        return (frameSectionNames ?? GetFrameSectionNames(sapModel, warnings))
            .Select(name =>
            {
                ETABSv1.eFramePropType propType = ETABSv1.eFramePropType.General;
                string materialName = "";

                try
                {
                    int typeRet = sapModel.PropFrame.GetTypeOAPI(name, ref propType);
                    if (typeRet != 0)
                        warnings.Add($"ETABS could not read frame section shape for '{name}' in model compare snapshot. Return code: {typeRet}.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"ETABS frame section shape read failed for '{name}' in model compare snapshot: {ex.Message}");
                }

                try
                {
                    int matRet = sapModel.PropFrame.GetMaterial(name, ref materialName);
                    if (matRet != 0)
                        warnings.Add($"ETABS could not read frame section material for '{name}' in model compare snapshot. Return code: {matRet}.");
                }
                catch
                {
                    // Imported or auto-select properties may not expose one simple material name.
                }

                (double Depth, double Width, double FlangeThickness, double WebThickness) dimensions = GetFrameSectionDimensions(sapModel, name, propType);

                return new ModelCompareFramePropertySnapshot
                {
                    SectionName = name,
                    SectionType = FormatFramePropType(propType),
                    MaterialName = materialName,
                    Depth = dimensions.Depth,
                    Width = dimensions.Width,
                    FlangeThickness = dimensions.FlangeThickness,
                    WebThickness = dimensions.WebThickness,
                    SummaryText = GetFrameSectionSummary(sapModel, name)
                };
            })
            .OrderBy(row => row.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModelCompareAreaPropertySnapshot MapModelCompareAreaProperty(EtabsAreaPropertyRow row)
    {
        return new ModelCompareAreaPropertySnapshot
        {
            PropertyName = row.Name,
            AreaType = row.AreaType,
            ShellType = row.ShellType,
            MaterialName = row.MaterialName,
            Thickness = row.Thickness
        };
    }

    private static ModelCompareMaterialSnapshot MapModelCompareMaterial(EtabsMaterialPropertyRow row)
    {
        return new ModelCompareMaterialSnapshot
        {
            MaterialName = row.Name,
            MaterialType = row.MaterialType,
            ElasticModulus = row.ElasticModulusMpa,
            PoissonRatio = row.PoissonRatio,
            UnitWeight = row.UnitWeightKnPerM3,
            DesignSummary = row.DesignSummary
        };
    }

    private static string TryGetEtabsProductName(ETABSv1.cSapModel sapModel)
    {
        try
        {
            dynamic dynamicSapModel = sapModel;
            string version = "";
            int ret = dynamicSapModel.GetVersion(ref version);
            if (ret == 0 && !string.IsNullOrWhiteSpace(version))
            {
                string trimmedVersion = version.Trim();
                return trimmedVersion.Contains("ETABS", StringComparison.OrdinalIgnoreCase)
                    ? trimmedVersion
                    : $"ETABS {trimmedVersion}";
            }
        }
        catch
        {
            // Some ETABS API versions do not expose version information through SapModel.
        }

        return "ETABS";
    }
}

internal sealed class EtabsSnapshotExtractionResult
{
    public ModelCompareSnapshot Snapshot { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
}
