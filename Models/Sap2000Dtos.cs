namespace CSIModellingTools.Models;

public sealed class Sap2000InstanceInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ModelFile { get; set; } = "";
    public string RotDisplayName { get; set; } = "";

    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed class Sap2000InstanceListResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<Sap2000InstanceInfo> Instances { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class Sap2000ModelDataRequest
{
    public string? Sap2000InstanceId { get; set; }
}

public sealed class Sap2000ModelDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<Sap2000InstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<string> Materials { get; set; } = [];
    public List<string> FrameSections { get; set; } = [];
    public List<string> CableSections { get; set; } = [];
    public List<string> TendonSections { get; set; } = [];
    public List<string> TensionMemberSections { get; set; } = [];
    public List<string> LoadPatterns { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class Sap2000CityOfTomorrowDrawRequest
{
    public string? Sap2000InstanceId { get; set; }
    public CityOfTomorrowModel Model { get; set; } = new();
    public bool ReplaceExistingStructure { get; set; }
}

public sealed class Sap2000CityOfTomorrowLoadUpdateRequest
{
    public string? Sap2000InstanceId { get; set; }
    public CityOfTomorrowModel Model { get; set; } = new();
}

public sealed class Sap2000CityOfTomorrowClearRequest
{
    public string? Sap2000InstanceId { get; set; }
    public string GroupName { get; set; } = "";
}

public sealed class Sap2000CotArchDrawRequest
{
    public string? Sap2000InstanceId { get; set; }
    public CotArchModel Model { get; set; } = new();
    public bool ReplaceExistingStructure { get; set; }
}

public sealed class Sap2000CotArchLoadUpdateRequest
{
    public string? Sap2000InstanceId { get; set; }
    public CotArchModel Model { get; set; } = new();
}

public sealed class Sap2000CotArchClearRequest
{
    public string? Sap2000InstanceId { get; set; }
    public string ModelPrefix { get; set; } = "";
    public string GroupName { get; set; } = "";
}
