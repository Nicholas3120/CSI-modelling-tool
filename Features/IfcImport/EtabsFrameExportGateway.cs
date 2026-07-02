namespace CSIModellingTools.Features.IfcImport;

public interface IEtabsFrameExportGateway : IDisposable
{
    void Connect(string? etabsInstanceId);
    void CreateNewModel(IfcEtabsUnits units);
    void SetUnits(IfcEtabsUnits units);
    void EnsureUnlocked();
    void SetupStories(IReadOnlyList<IfcStoreyLevel> storeyLevels);
    bool MaterialExists(string materialName);
    void CreateMaterial(string materialName, EtabsMaterialKind materialKind);
    bool FrameSectionExists(string sectionName);
    void CreateFrameSection(string sectionName, string materialName, SectionInfo sectionInfo);
    void EnsureGroup(string groupName);
    string AddFrameByCoordinates(AnalyticalPoint start, AnalyticalPoint end, string sectionName, string preferredName);
    void AssignFrameToGroup(string frameName, string groupName);
    int AssignRigidDiaphragms();
}

public enum EtabsMaterialKind
{
    Concrete,
    Steel
}

public sealed class MockEtabsFrameExportGateway : IEtabsFrameExportGateway
{
    private readonly HashSet<string> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _groups = new(StringComparer.OrdinalIgnoreCase);
    private int _frameCounter;

    public List<string> CreatedFrames { get; } = [];

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

    public void SetupStories(IReadOnlyList<IfcStoreyLevel> storeyLevels)
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

    public bool FrameSectionExists(string sectionName)
    {
        return _sections.Contains(sectionName);
    }

    public void CreateFrameSection(string sectionName, string materialName, SectionInfo sectionInfo)
    {
        _sections.Add(sectionName);
    }

    public void EnsureGroup(string groupName)
    {
        _groups.Add(groupName);
    }

    public string AddFrameByCoordinates(AnalyticalPoint start, AnalyticalPoint end, string sectionName, string preferredName)
    {
        string frameName = string.IsNullOrWhiteSpace(preferredName)
            ? $"MOCK_IFC_{++_frameCounter:0000}"
            : preferredName;
        CreatedFrames.Add(frameName);
        return frameName;
    }

    public void AssignFrameToGroup(string frameName, string groupName)
    {
        _groups.Add(groupName);
    }

    public int AssignRigidDiaphragms()
    {
        return 0;
    }

    public void Dispose()
    {
    }
}
