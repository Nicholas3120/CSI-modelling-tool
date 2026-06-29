using System.Diagnostics;
using System.Globalization;

namespace CSIModellingTools.Features.IfcImport;

public sealed class RealEtabsAreaExportGateway : IEtabsAreaExportGateway
{
    private const string EtabsApiObjectProgId = "CSI.ETABS.API.ETABSObject";
    private const ETABSv1.eItemType EtabsObjects = ETABSv1.eItemType.Objects;

    private ETABSv1.cOAPI? _etabsObject;
    private ETABSv1.cSapModel? _sapModel;

    public void Connect(string? etabsInstanceId)
    {
        _etabsObject = GetEtabsObject(etabsInstanceId);
        _sapModel = _etabsObject.SapModel;
        if (_sapModel == null)
            throw new InvalidOperationException("Connected to ETABS, but SapModel was not available.");
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
            // Downstream ETABS calls report model lock or API failures more specifically.
        }
    }

    public bool MaterialExists(string materialName)
    {
        string normalized = (materialName ?? "").Trim();
        if (normalized.Length == 0)
            return false;

        ETABSv1.cSapModel sapModel = GetSapModel();
        foreach (ETABSv1.eMatType materialType in Enum.GetValues(typeof(ETABSv1.eMatType)).Cast<ETABSv1.eMatType>())
        {
            int numberNames = 0;
            string[] materialNames = [];
            try
            {
                if (sapModel.PropMaterial.GetNameList(ref numberNames, ref materialNames, materialType) == 0 &&
                    materialNames.Take(Math.Min(numberNames, materialNames.Length)).Any(name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
                // Missing material families are expected in many models.
            }
        }

        return false;
    }

    public void CreateMaterial(string materialName, EtabsMaterialKind materialKind)
    {
        string name = RequireName(materialName, "ETABS material name");
        ETABSv1.cSapModel sapModel = GetSapModel();
        ETABSv1.eMatType materialType = materialKind == EtabsMaterialKind.Steel
            ? ETABSv1.eMatType.Steel
            : ETABSv1.eMatType.Concrete;

        int ret = sapModel.PropMaterial.SetMaterial(name, materialType, -1, "Created from IFC Structural Area Importer", "");
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create material '{name}'. Return code: {ret}.");

        if (materialKind == EtabsMaterialKind.Steel)
        {
            ret = sapModel.PropMaterial.SetMPIsotropic(name, 200_000_000.0, 0.3, 0.0000117, 0);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not assign steel isotropic properties to '{name}'. Return code: {ret}.");

            ret = sapModel.PropMaterial.SetWeightAndMass(name, 1, 78.5, 0);
            if (ret != 0)
                throw new InvalidOperationException($"ETABS could not assign steel weight to '{name}'. Return code: {ret}.");
            return;
        }

        ret = sapModel.PropMaterial.SetMPIsotropic(name, 30_000_000.0, 0.2, 0.00001, 0);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not assign concrete isotropic properties to '{name}'. Return code: {ret}.");

        ret = sapModel.PropMaterial.SetWeightAndMass(name, 1, 24.0, 0);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not assign concrete weight to '{name}'. Return code: {ret}.");
    }

    public bool AreaPropertyExists(string propertyName)
    {
        string normalized = (propertyName ?? "").Trim();
        if (normalized.Length == 0)
            return false;

        int numberNames = 0;
        string[] names = [];
        try
        {
            return GetSapModel().PropArea.GetNameList(ref numberNames, ref names, 0) == 0 &&
                names.Take(Math.Min(numberNames, names.Length)).Any(name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public void CreateAreaProperty(string propertyName, string materialName, double thickness, bool isWall)
    {
        string name = RequireName(propertyName, "ETABS area property name");
        string material = RequireName(materialName, "ETABS material name");
        double positiveThickness = EnsurePositive(thickness, "Thickness");
        ETABSv1.cSapModel sapModel = GetSapModel();

        int ret = isWall
            ? sapModel.PropArea.SetWall(
                name,
                ETABSv1.eWallPropType.Specified,
                ETABSv1.eShellType.ShellThin,
                material,
                positiveThickness,
                -1,
                "Created from IFC Structural Area Importer",
                "")
            : sapModel.PropArea.SetSlab(
                name,
                ETABSv1.eSlabType.Slab,
                ETABSv1.eShellType.ShellThin,
                material,
                positiveThickness,
                -1,
                "Created from IFC Structural Area Importer",
                "");

        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create area property '{name}'. Return code: {ret}.");
    }

    public void EnsureGroup(string groupName)
    {
        string name = RequireName(groupName, "ETABS group name");
        int ret = GetSapModel().GroupDef.SetGroup(name, -1, true, false, false, false, false, false, false, false, false, false, false);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not create/update group '{name}'. Return code: {ret}.");
    }

    public string AddAreaByCoordinates(IReadOnlyList<AnalyticalPoint> boundaryPoints, string propertyName, string preferredName)
    {
        if (boundaryPoints.Count < 3)
            throw new InvalidOperationException("At least three boundary points are required to create an ETABS area object.");

        double[] xs = boundaryPoints.Select(point => point.X).ToArray();
        double[] ys = boundaryPoints.Select(point => point.Y).ToArray();
        double[] zs = boundaryPoints.Select(point => point.Z).ToArray();
        string areaName = "";
        string property = RequireName(propertyName, "ETABS area property name");
        string preferred = (preferredName ?? "").Trim();

        int ret = GetSapModel().AreaObj.AddByCoord(boundaryPoints.Count, ref xs, ref ys, ref zs, ref areaName, property, preferred, "Global");
        if (ret != 0)
        {
            areaName = "";
            ret = GetSapModel().AreaObj.AddByCoord(boundaryPoints.Count, ref xs, ref ys, ref zs, ref areaName, property, "", "Global");
        }

        if (ret != 0 || string.IsNullOrWhiteSpace(areaName))
            throw new InvalidOperationException($"ETABS could not create area object. Return code: {ret}.");

        return areaName;
    }

    public void AssignAreaToGroup(string areaName, string groupName)
    {
        string area = RequireName(areaName, "ETABS area object name");
        string group = RequireName(groupName, "ETABS group name");
        EnsureGroup(group);
        int ret = GetSapModel().AreaObj.SetGroupAssign(area, group, false, EtabsObjects);
        if (ret != 0)
            throw new InvalidOperationException($"ETABS could not assign area '{area}' to group '{group}'. Return code: {ret}.");
    }

    public void Dispose()
    {
        _sapModel = null;
        _etabsObject = null;
    }

    private ETABSv1.cSapModel GetSapModel()
    {
        return _sapModel ?? throw new InvalidOperationException("ETABS area exporter is not connected.");
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

        throw new InvalidOperationException("No active ETABS API instance was found. Open ETABS before exporting IFC areas.");
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
