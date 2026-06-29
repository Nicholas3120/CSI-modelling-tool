namespace CSIModellingTools.Features.IfcImport;

public interface IEtabsAreaExportGateway : IDisposable
{
    void Connect(string? etabsInstanceId);
    void CreateNewModel(IfcEtabsUnits units);
    void SetUnits(IfcEtabsUnits units);
    void EnsureUnlocked();
    bool MaterialExists(string materialName);
    void CreateMaterial(string materialName, EtabsMaterialKind materialKind);
    bool AreaPropertyExists(string propertyName);
    void CreateAreaProperty(string propertyName, string materialName, double thickness, bool isWall);
    void EnsureGroup(string groupName);
    string AddAreaByCoordinates(IReadOnlyList<AnalyticalPoint> boundaryPoints, string propertyName, string preferredName);
    void AssignAreaToGroup(string areaName, string groupName);
}

public sealed class MockEtabsAreaExportGateway : IEtabsAreaExportGateway
{
    private readonly HashSet<string> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _areaProperties = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _groups = new(StringComparer.OrdinalIgnoreCase);
    private int _areaCounter;

    public List<string> CreatedAreas { get; } = [];

    public void Connect(string? etabsInstanceId)
    {
    }

    public void CreateNewModel(IfcEtabsUnits units)
    {
    }

    public void SetUnits(IfcEtabsUnits units)
    {
    }

    public void EnsureUnlocked()
    {
    }

    public bool MaterialExists(string materialName)
    {
        return _materials.Contains(materialName);
    }

    public void CreateMaterial(string materialName, EtabsMaterialKind materialKind)
    {
        _materials.Add(materialName);
    }

    public bool AreaPropertyExists(string propertyName)
    {
        return _areaProperties.Contains(propertyName);
    }

    public void CreateAreaProperty(string propertyName, string materialName, double thickness, bool isWall)
    {
        _areaProperties.Add(propertyName);
    }

    public void EnsureGroup(string groupName)
    {
        _groups.Add(groupName);
    }

    public string AddAreaByCoordinates(IReadOnlyList<AnalyticalPoint> boundaryPoints, string propertyName, string preferredName)
    {
        string areaName = string.IsNullOrWhiteSpace(preferredName)
            ? $"MOCK_IFC_AREA_{++_areaCounter:0000}"
            : preferredName;
        CreatedAreas.Add(areaName);
        return areaName;
    }

    public void AssignAreaToGroup(string areaName, string groupName)
    {
        _groups.Add(groupName);
    }

    public void Dispose()
    {
    }
}
