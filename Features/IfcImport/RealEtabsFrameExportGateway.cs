using System.Diagnostics;
using System.Globalization;

namespace CSIModellingTools.Features.IfcImport;

public sealed class RealEtabsFrameExportGateway : IEtabsFrameExportGateway
{
    private const string EtabsApiObjectProgId = "CSI.ETABS.API.ETABSObject";
    private const ETABSv1.eItemType EtabsObjects = ETABSv1.eItemType.Objects;

    private ETABSv1.cOAPI? _etabsObject;
    private ETABSv1.cSapModel? _sapModel;

    // Existence caches scoped to a single export run. The first lookup scans ETABS
    // once; later lookups (and names we create) hit the cache instead of re-querying
    // every material/section family over COM for every frame.
    private HashSet<string>? _materialNames;
    private HashSet<string>? _sectionNames;

    public void Connect(string? etabsInstanceId)
    {
        _etabsObject = GetEtabsObject(etabsInstanceId);
        _sapModel = _etabsObject.SapModel;
        if (_sapModel == null)
            throw new InvalidOperationException("Connected to ETABS, but SapModel was not available.");

        _materialNames = null;
        _sectionNames = null;
    }

    public void CreateNewModel(IfcEtabsUnits units)
    {
        ETABSv1.cSapModel sapModel = GetSapModel();
        int ret = sapModel.InitializeNewModel(MapUnits(units));
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not initialize a new model. Return code: {ret}.");

        ret = sapModel.File.NewBlank();
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create a blank model. Return code: {ret}.");
    }

    public void SetUnits(IfcEtabsUnits units)
    {
        int ret = GetSapModel().SetPresentUnits(MapUnits(units));
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not set present units to {units}. Return code: {ret}.");
    }

    public void EnsureUnlocked()
    {
        ETABSv1.cSapModel sapModel = GetSapModel();
        try
        {
            if (sapModel.GetModelIsLocked())
                sapModel.SetModelIsLocked(false);
        }
        catch
        {
            // Some ETABS versions/models do not expose lock state reliably; downstream calls still report failures.
        }
    }

    public void SetupStories(IReadOnlyList<IfcStoreyLevel> storeyLevels)
    {
        EtabsStorySetup.Configure(GetSapModel(), storeyLevels);
    }

    public bool MaterialExists(string materialName)
    {
        string normalized = (materialName ?? "").Trim();
        if (normalized.Length == 0)
            return false;

        return GetMaterialNameCache().Contains(normalized);
    }

    private HashSet<string> GetMaterialNameCache()
    {
        if (_materialNames != null)
            return _materialNames;

        _materialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ETABSv1.cSapModel sapModel = GetSapModel();
        foreach (ETABSv1.eMatType materialType in Enum.GetValues(typeof(ETABSv1.eMatType)).Cast<ETABSv1.eMatType>())
        {
            int numberNames = 0;
            string[] materialNames = [];
            try
            {
                if (sapModel.PropMaterial.GetNameList(ref numberNames, ref materialNames, materialType) == 0)
                {
                    foreach (string name in materialNames.Take(Math.Min(numberNames, materialNames.Length)))
                        _materialNames.Add(name);
                }
            }
            catch
            {
                // Missing material families are expected in many models.
            }
        }

        return _materialNames;
    }

    public void CreateMaterial(string materialName, EtabsMaterialKind materialKind)
    {
        string name = RequireName(materialName, "ETABS material name");
        ETABSv1.cSapModel sapModel = GetSapModel();
        ETABSv1.eMatType materialType = materialKind == EtabsMaterialKind.Steel
            ? ETABSv1.eMatType.Steel
            : ETABSv1.eMatType.Concrete;

        int ret = sapModel.PropMaterial.SetMaterial(name, materialType, -1, "Created from IFC Structural Frame Importer", "");
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create material '{name}'. Return code: {ret}.");

        _materialNames?.Add(name);

        if (materialKind == EtabsMaterialKind.Steel)
        {
            ret = sapModel.PropMaterial.SetMPIsotropic(name, 200_000_000.0, 0.3, 0.0000117, 0);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not assign steel isotropic properties to '{name}'. Return code: {ret}.");

            ret = sapModel.PropMaterial.SetWeightAndMass(name, 1, 78.5, 0);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not assign steel weight to '{name}'. Return code: {ret}.");

            ret = sapModel.PropMaterial.SetOSteel(name, 355_000.0, 510_000.0, 355_000.0, 510_000.0, 1, 1, 0.015, 0.11, 0.17, 0);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not assign steel design properties to '{name}'. Return code: {ret}.");
            return;
        }

        ret = sapModel.PropMaterial.SetMPIsotropic(name, 30_000_000.0, 0.2, 0.00001, 0);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not assign concrete isotropic properties to '{name}'. Return code: {ret}.");

        ret = sapModel.PropMaterial.SetWeightAndMass(name, 1, 24.0, 0);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not assign concrete weight to '{name}'. Return code: {ret}.");

        ret = sapModel.PropMaterial.SetOConcrete(name, 30_000.0, false, 0, 0, 0, 0.0022, 0.0052, 0, 0, 0);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not assign concrete design properties to '{name}'. Return code: {ret}.");
    }

    public bool FrameSectionExists(string sectionName)
    {
        string normalized = (sectionName ?? "").Trim();
        if (normalized.Length == 0)
            return false;

        return GetSectionNameCache().Contains(normalized);
    }

    private HashSet<string> GetSectionNameCache()
    {
        if (_sectionNames != null)
            return _sectionNames;

        _sectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ETABSv1.cSapModel sapModel = GetSapModel();
        foreach (ETABSv1.eFramePropType propType in Enum.GetValues(typeof(ETABSv1.eFramePropType)).Cast<ETABSv1.eFramePropType>())
        {
            int numberNames = 0;
            string[] frameNames = [];
            try
            {
                if (sapModel.PropFrame.GetNameList(ref numberNames, ref frameNames, propType) == 0)
                {
                    foreach (string name in frameNames.Take(Math.Min(numberNames, frameNames.Length)))
                        _sectionNames.Add(name);
                }
            }
            catch
            {
                // Some ETABS versions return errors for section families not present in a model.
            }
        }

        return _sectionNames;
    }

    public void CreateFrameSection(string sectionName, string materialName, SectionInfo sectionInfo)
    {
        string name = RequireName(sectionName, "ETABS frame section name");
        string material = RequireName(materialName, "ETABS material name");
        ETABSv1.cSapModel sapModel = GetSapModel();
        int ret = sectionInfo.ShapeType switch
        {
            IfcSectionShapeType.Circle => sapModel.PropFrame.SetCircle(
                name,
                material,
                EnsurePositive(sectionInfo.Diameter, "Diameter"),
                -1,
                "Created from IFC Structural Frame Importer",
                ""),
            IfcSectionShapeType.ISection => sapModel.PropFrame.SetISection(
                name,
                material,
                EnsurePositive(sectionInfo.Depth, "Depth"),
                EnsurePositive(sectionInfo.FlangeWidth, "Flange width"),
                EnsurePositive(sectionInfo.FlangeThickness, "Flange thickness"),
                EnsurePositive(sectionInfo.WebThickness, "Web thickness"),
                EnsurePositive(sectionInfo.FlangeWidth, "Bottom flange width"),
                EnsurePositive(sectionInfo.FlangeThickness, "Bottom flange thickness"),
                -1,
                "Created from IFC Structural Frame Importer",
                ""),
            _ => sapModel.PropFrame.SetRectangle(
                name,
                material,
                EnsurePositive(sectionInfo.Depth, "Depth"),
                EnsurePositive(sectionInfo.Width, "Width"),
                -1,
                "Created from IFC Structural Frame Importer",
                "")
        };

        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create frame section '{name}'. Return code: {ret}.");

        _sectionNames?.Add(name);
    }

    public void EnsureGroup(string groupName)
    {
        string name = RequireName(groupName, "ETABS group name");
        int ret = GetSapModel().GroupDef.SetGroup(name, -1, true, false, false, false, false, false, false, false, false, false, false);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create/update group '{name}'. Return code: {ret}.");
    }

    public string AddFrameByCoordinates(AnalyticalPoint start, AnalyticalPoint end, string sectionName, string preferredName)
    {
        string frameName = "";
        string section = RequireName(sectionName, "ETABS frame section name");
        string preferred = (preferredName ?? "").Trim();
        int ret = GetSapModel().FrameObj.AddByCoord(start.X, start.Y, start.Z, end.X, end.Y, end.Z, ref frameName, section, preferred, "Global");
        if (ret != 0)
        {
            frameName = "";
            ret = GetSapModel().FrameObj.AddByCoord(start.X, start.Y, start.Z, end.X, end.Y, end.Z, ref frameName, section, "", "Global");
        }

        if (ret != 0 || string.IsNullOrWhiteSpace(frameName))
            throw new InvalidOperationException($"ETABS could not create frame object. Return code: {ret}.");

        return frameName;
    }

    public void AssignFrameToGroup(string frameName, string groupName)
    {
        string frame = RequireName(frameName, "ETABS frame object name");
        string group = RequireName(groupName, "ETABS group name");
        EnsureGroup(group);
        int ret = GetSapModel().FrameObj.SetGroupAssign(frame, group, false, EtabsObjects);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not assign frame '{frame}' to group '{group}'. Return code: {ret}.");
    }

    public int AssignRigidDiaphragms()
    {
        return EtabsDiaphragmSetup.Configure(GetSapModel());
    }

    public void Dispose()
    {
        _sapModel = null;
        _etabsObject = null;
    }

    private ETABSv1.cSapModel GetSapModel()
    {
        return _sapModel ?? throw new InvalidOperationException("ETABS exporter is not connected.");
    }

    private static ETABSv1.cOAPI GetEtabsObject(string? instanceId)
    {
        string id = (instanceId ?? "").Trim();
        if (id.StartsWith("process:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(id["process:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
        {
            return GetEtabsObjectForProcess(processId);
        }

        return GetActiveEtabsObject();
    }

    private static ETABSv1.cOAPI GetActiveEtabsObject()
    {
        ETABSv1.cOAPI? helperObject = TryGetHelperEtabsObject(null);
        if (helperObject != null)
            return helperObject;

        Process[] processes = Process.GetProcessesByName("ETABS");
        if (processes.Length == 1)
        {
            ETABSv1.cOAPI? processObject = TryGetHelperEtabsObject(processes[0].Id);
            if (processObject != null)
                return processObject;
        }

        throw new InvalidOperationException("No active ETABS API instance was found. Open ETABS before exporting IFC frames.");
    }

    private static ETABSv1.cOAPI GetEtabsObjectForProcess(int processId)
    {
        ETABSv1.cOAPI? etabsObject = TryGetHelperEtabsObject(processId);
        if (etabsObject != null)
            return etabsObject;

        throw new InvalidOperationException($"Unable to connect to ETABS process {processId}.");
    }

    private static ETABSv1.cOAPI? TryGetHelperEtabsObject(int? processId)
    {
        try
        {
            ETABSv1.cHelper helper = new ETABSv1.Helper();
            return processId.HasValue
                ? helper.GetObjectProcess(EtabsApiObjectProgId, processId.Value)
                : helper.GetObject(EtabsApiObjectProgId);
        }
        catch
        {
            return null;
        }
    }

    private static ETABSv1.eUnits MapUnits(IfcEtabsUnits units)
    {
        return units switch
        {
            IfcEtabsUnits.N_m_C => ETABSv1.eUnits.N_m_C,
            IfcEtabsUnits.kN_mm_C => ETABSv1.eUnits.kN_mm_C,
            _ => ETABSv1.eUnits.kN_m_C
        };
    }

    private static string RequireName(string? value, string label)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            throw new InvalidOperationException($"{label} is required.");

        return text;
    }

    private static double EnsurePositive(double value, string label)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new InvalidOperationException($"{label} must be greater than zero.");

        return value;
    }
}
