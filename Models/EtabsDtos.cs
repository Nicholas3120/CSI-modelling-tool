using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CSIModellingTools.Models;

public sealed class EtabsInstanceInfo
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

public sealed class EtabsInstanceListResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsParametricModelDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class EtabsParametricModelDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<string> FrameSections { get; set; } = [];
    public List<string> ShellProperties { get; set; } = [];
    public List<string> Materials { get; set; } = [];
    public List<string> LoadPatterns { get; set; } = [];
    public List<string> LoadCombinations { get; set; } = [];
    public List<string> Stories { get; set; } = [];
    public List<EtabsStoryInfo> StoryInfos { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsStoryInfo
{
    public string Name { get; set; } = "";
    public double Elevation { get; set; }

    public string DisplayName => $"{Name} ({Elevation:0.###} m)";

    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed class EtabsSelectedInsertionPointsRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class EtabsSelectedInsertionPointsResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsPointInfo> Points { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsPointInfo
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public ModelPoint3d ToModelPoint()
    {
        return new ModelPoint3d { X = X, Y = Y, Z = Z };
    }
}

public sealed class EtabsTrussDrawRequest
{
    public string? EtabsInstanceId { get; set; }
    public ParametricTrussModel Model { get; set; } = new();
    public bool ReplaceExistingGroup { get; set; } = true;
    public bool AddAsNew { get; set; }
    public EtabsTrussOverlapDrawMode OverlapDrawMode { get; set; } = EtabsTrussOverlapDrawMode.Current;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double OffsetZ { get; set; }
}

public sealed class EtabsTrussDrawResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int DrawnCount { get; set; }
    public int ShellCount { get; set; }
    public List<GeneratedEtabsFrame> Frames { get; set; } = [];
    public List<string> ShellNames { get; set; } = [];
    public List<string> ObjectNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class GeneratedEtabsFrame
{
    public string MemberId { get; set; } = "";
    public string EtabsFrameName { get; set; } = "";
    public string Group { get; set; } = "";
    public string SectionName { get; set; } = "";
}

public sealed class EtabsTrussCrashCheckRequest
{
    public string? EtabsInstanceId { get; set; }
    public ParametricTrussModel Model { get; set; } = new();
    public double Tolerance { get; set; } = 0.01;
}

public sealed class EtabsTrussCrashCheckResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsTrussCrashRow> Crashes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsTrussCrashRow
{
    public string MemberId { get; set; } = "";
    public string MemberGroup { get; set; } = "";
    public string ExistingFrameName { get; set; } = "";
    public string ExistingFrameLabel { get; set; } = "";
    public string ExistingFrameStory { get; set; } = "";
    public double OverlapLength { get; set; }
    public double Distance { get; set; }

    public string DisplayFrame => string.IsNullOrWhiteSpace(ExistingFrameLabel)
        ? ExistingFrameName
        : $"{ExistingFrameLabel} ({ExistingFrameName})";
}

public sealed class EtabsFrameSelectionRequest
{
    public string? EtabsInstanceId { get; set; }
    public List<string> FrameNames { get; set; } = [];
}

public sealed class EtabsFrameSelectionResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int SelectedCount { get; set; }
    public List<string> SelectedFrameNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsFrameSectionImportRequest
{
    public string? EtabsInstanceId { get; set; }
    public string? Sap2000InstanceId { get; set; }
    public bool UseSelectedFrames { get; set; } = true;
    public string GroupName { get; set; } = "";
}

public sealed class EtabsFrameSectionImportResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsFrameSectionRow> Frames { get; set; } = [];
    public List<string> FrameSections { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsFrameSectionRow : INotifyPropertyChanged
{
    private bool _include = true;
    private string _frameName = "";
    private string _label = "";
    private string _story = "";
    private string _groupName = "";
    private string _currentSection = "";
    private string _newSection = "";
    private string _pointI = "";
    private string _pointJ = "";
    private double _lengthM;
    private double _iX;
    private double _iY;
    private double _iZ;
    private double _jX;
    private double _jY;
    private double _jZ;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Include
    {
        get => _include;
        set => SetProperty(ref _include, value);
    }

    public string FrameName
    {
        get => _frameName;
        set => SetProperty(ref _frameName, value ?? "");
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value ?? "");
    }

    public string Story
    {
        get => _story;
        set => SetProperty(ref _story, value ?? "");
    }

    public string GroupName
    {
        get => _groupName;
        set => SetProperty(ref _groupName, value ?? "");
    }

    public string CurrentSection
    {
        get => _currentSection;
        set => SetProperty(ref _currentSection, value ?? "");
    }

    public string NewSection
    {
        get => _newSection;
        set => SetProperty(ref _newSection, value ?? "");
    }

    public string PointI
    {
        get => _pointI;
        set => SetProperty(ref _pointI, value ?? "");
    }

    public string PointJ
    {
        get => _pointJ;
        set => SetProperty(ref _pointJ, value ?? "");
    }

    public double LengthM
    {
        get => _lengthM;
        set => SetProperty(ref _lengthM, value);
    }

    public double IX
    {
        get => _iX;
        set => SetProperty(ref _iX, value);
    }

    public double IY
    {
        get => _iY;
        set => SetProperty(ref _iY, value);
    }

    public double IZ
    {
        get => _iZ;
        set => SetProperty(ref _iZ, value);
    }

    public double JX
    {
        get => _jX;
        set => SetProperty(ref _jX, value);
    }

    public double JY
    {
        get => _jY;
        set => SetProperty(ref _jY, value);
    }

    public double JZ
    {
        get => _jZ;
        set => SetProperty(ref _jZ, value);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class EtabsFrameSectionUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string? Sap2000InstanceId { get; set; }
    public List<EtabsFrameSectionRow> Frames { get; set; } = [];
}

public sealed class EtabsFrameSectionUpdateResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int UpdatedCount { get; set; }
    public List<string> UpdatedFrameNames { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> FrameSections { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsFrameLoadUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string? Sap2000InstanceId { get; set; }
    public List<EtabsFrameSectionRow> Frames { get; set; } = [];
    public string LoadPattern { get; set; } = "";
    public double LineLoadKnPerM { get; set; }
    public bool ReplaceSelectedPatternLoads { get; set; } = true;
}

public sealed class EtabsFrameLoadUpdateResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int UpdatedCount { get; set; }
    public List<string> UpdatedFrameNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsFrameGroupAssignRequest
{
    public string? EtabsInstanceId { get; set; }
    public string? Sap2000InstanceId { get; set; }
    public string GroupName { get; set; } = "";
}

public sealed class EtabsFrameGroupAssignResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int AssignedCount { get; set; }
    public string GroupName { get; set; } = "";
    public List<string> AssignedFrameNames { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class EtabsFrameGroupSectionUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string? Sap2000InstanceId { get; set; }
    public string GroupName { get; set; } = "";
    public string SectionName { get; set; } = "";
}
