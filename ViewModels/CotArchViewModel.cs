using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class CotArchViewModel : ObservableObject
{
    private const string NoLoadLabel = "None";
    private const string UdlLoadLabel = "UDL";
    private const string PointLoadLabel = "Point load at joints";

    private readonly CotArchGeometryBuilder _builder = new();
    private readonly CotArchValidator _validator = new();
    private readonly EtabsParametricModellingService _etabs = new();
    private readonly Sap2000ModellingService _sap2000 = new();
    private List<string> _lastEtabsFrameSectionsForTension = [];
    private List<string> _lastSap2000FrameSectionsForTension = [];
    private List<string> _lastSap2000TensionSections = [];
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private Sap2000InstanceInfo? _selectedSap2000Instance;
    private string _connectionStatus = "Not connected";
    private string _sap2000ConnectionStatus = "Not connected";
    private string _modelPrefix = "TA01";
    private double _originX;
    private double _planeY;
    private double _baseZ = -8.0;
    private double _springingZ;
    private double _upperBeamZ = 12.0;
    private double _span = 40.0;
    private double _rise = 8.0;
    private int _postCount = 9;
    private int _archSegmentsPerPostBay = 1;
    private string _selectedProfileType = CotArchProfileType.Parabolic.ToString();
    private double _shapeExponent = 2.0;
    private string _customPostStationsText = "";
    private string _selectedSupportCondition = CotArchSupportCondition.Pinned.ToString();
    private string _selectedArchReleasePreset = CotArchMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedPostReleasePreset = CotArchMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedTieReleasePreset = CotArchMemberReleasePreset.PinnedBothEnds.ToString();
    private string _selectedBeamReleasePreset = CotArchMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedSupportColumnReleasePreset = CotArchMemberReleasePreset.FullyContinuous.ToString();
    private string _archSection = "";
    private string _postSection = "";
    private string _upperBeamSection = "";
    private string _tieSection = "";
    private string _supportColumnSection = "";
    private string _selectedUpperBeamLoadType = NoLoadLabel;
    private string _selectedLoadPattern = "";
    private double _upperBeamUdlKnPerM;
    private double _upperBeamPointLoadKn;
    private string _generationReport = "Adjust parameters, read ETABS sections, validate, then generate into the selected open ETABS model.";
    private string _appliedUpperBeamLoadStatus = "Applied loads will be listed after Generate or Update Loads.";
    private CotArchModel _currentModel = new();

    public CotArchViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ValidateCommand = new RelayCommand(_ => Validate(true));
        GenerateStructureCommand = new RelayCommand(_ => Draw(false));
        RegenerateStructureCommand = new RelayCommand(_ => Draw(true));
        UpdateLoadsCommand = new RelayCommand(_ => UpdateLoads());
        UpdateSap2000LoadsCommand = new RelayCommand(_ => UpdateSap2000Loads());
        ClearGeneratedStructureCommand = new RelayCommand(_ => Clear());
        RefreshSap2000InstancesCommand = new RelayCommand(_ => RefreshSap2000Instances());
        ReadSap2000DataCommand = new RelayCommand(_ => ReadSap2000Data());
        GenerateSap2000StructureCommand = new RelayCommand(_ => DrawSap2000(false));
        RegenerateSap2000StructureCommand = new RelayCommand(_ => DrawSap2000(true));
        ClearSap2000StructureCommand = new RelayCommand(_ => ClearSap2000());
        Rebuild();
    }

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<Sap2000InstanceInfo> Sap2000Instances { get; } = [];
    public ObservableCollection<string> FrameSections { get; } = [];
    public ObservableCollection<string> TensionMemberSections { get; } = [];
    public ObservableCollection<string> LoadPatterns { get; } = [];
    public ObservableCollection<CotArchAppliedUpperBeamLoad> AppliedUpperBeamLoads { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];
    public IReadOnlyList<string> ProfileTypes { get; } = Enum.GetNames(typeof(CotArchProfileType));
    public IReadOnlyList<string> SupportConditions { get; } = [CotArchSupportCondition.Pinned.ToString()];
    public IReadOnlyList<string> ReleasePresets { get; } = Enum.GetNames(typeof(CotArchMemberReleasePreset));
    public IReadOnlyList<string> UpperBeamLoadTypes { get; } = [NoLoadLabel, UdlLoadLabel, PointLoadLabel];
    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand GenerateStructureCommand { get; }
    public ICommand RegenerateStructureCommand { get; }
    public ICommand UpdateLoadsCommand { get; }
    public ICommand UpdateSap2000LoadsCommand { get; }
    public ICommand ClearGeneratedStructureCommand { get; }
    public ICommand RefreshSap2000InstancesCommand { get; }
    public ICommand ReadSap2000DataCommand { get; }
    public ICommand GenerateSap2000StructureCommand { get; }
    public ICommand RegenerateSap2000StructureCommand { get; }
    public ICommand ClearSap2000StructureCommand { get; }

    public EtabsInstanceInfo? SelectedEtabsInstance { get => _selectedEtabsInstance; set => SetProperty(ref _selectedEtabsInstance, value); }
    public Sap2000InstanceInfo? SelectedSap2000Instance { get => _selectedSap2000Instance; set => SetProperty(ref _selectedSap2000Instance, value); }
    public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }
    public string Sap2000ConnectionStatus { get => _sap2000ConnectionStatus; set => SetProperty(ref _sap2000ConnectionStatus, value); }
    public string ModelPrefix
    {
        get => _modelPrefix;
        set
        {
            if (SetProperty(ref _modelPrefix, value ?? ""))
            {
                ClearAppliedUpperBeamLoads("Applied loads will be listed after Generate or Update Loads.");
                Rebuild();
            }
        }
    }
    public double OriginX { get => _originX; set { if (SetProperty(ref _originX, Finite(value, 0))) Rebuild(); } }
    public double PlaneY { get => _planeY; set { if (SetProperty(ref _planeY, Finite(value, 0))) Rebuild(); } }
    public double BaseZ { get => _baseZ; set { if (SetProperty(ref _baseZ, Finite(value, -8))) Rebuild(); } }
    public double SpringingZ { get => _springingZ; set { if (SetProperty(ref _springingZ, Finite(value, 0))) Rebuild(); } }
    public double UpperBeamZ { get => _upperBeamZ; set { if (SetProperty(ref _upperBeamZ, Finite(value, 12))) Rebuild(); } }
    public double Span { get => _span; set { if (SetProperty(ref _span, Math.Max(Finite(value, 40), 0.001))) Rebuild(); } }
    public double Rise { get => _rise; set { if (SetProperty(ref _rise, Math.Max(Finite(value, 8), 0.001))) Rebuild(); } }
    public int PostCount { get => _postCount; set { if (SetProperty(ref _postCount, Math.Clamp(value, 3, 101))) Rebuild(); } }
    public int ArchSegmentsPerPostBay { get => _archSegmentsPerPostBay; set { if (SetProperty(ref _archSegmentsPerPostBay, Math.Clamp(value, 1, 20))) Rebuild(); } }
    public string SelectedProfileType { get => _selectedProfileType; set { if (SetProperty(ref _selectedProfileType, value ?? CotArchProfileType.Parabolic.ToString())) Rebuild(); } }
    public double ShapeExponent { get => _shapeExponent; set { if (SetProperty(ref _shapeExponent, Math.Max(Finite(value, 2), 1))) Rebuild(); } }
    public string CustomPostStationsText { get => _customPostStationsText; set { if (SetProperty(ref _customPostStationsText, value ?? "")) Rebuild(); } }
    public string SelectedSupportCondition { get => _selectedSupportCondition; set { if (SetProperty(ref _selectedSupportCondition, CotArchSupportCondition.Pinned.ToString())) Rebuild(); } }
    public string SelectedArchReleasePreset { get => _selectedArchReleasePreset; set { if (SetProperty(ref _selectedArchReleasePreset, value ?? CotArchMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedPostReleasePreset { get => _selectedPostReleasePreset; set { if (SetProperty(ref _selectedPostReleasePreset, value ?? CotArchMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedTieReleasePreset { get => _selectedTieReleasePreset; set { if (SetProperty(ref _selectedTieReleasePreset, value ?? CotArchMemberReleasePreset.PinnedBothEnds.ToString())) Rebuild(); } }
    public string SelectedBeamReleasePreset { get => _selectedBeamReleasePreset; set { if (SetProperty(ref _selectedBeamReleasePreset, value ?? CotArchMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedSupportColumnReleasePreset { get => _selectedSupportColumnReleasePreset; set { if (SetProperty(ref _selectedSupportColumnReleasePreset, value ?? CotArchMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string ArchSection { get => _archSection; set { if (SetProperty(ref _archSection, value ?? "")) Rebuild(); } }
    public string PostSection { get => _postSection; set { if (SetProperty(ref _postSection, value ?? "")) Rebuild(); } }
    public string UpperBeamSection { get => _upperBeamSection; set { if (SetProperty(ref _upperBeamSection, value ?? "")) Rebuild(); } }
    public string TieSection { get => _tieSection; set { if (SetProperty(ref _tieSection, value ?? "")) Rebuild(); } }
    public string SupportColumnSection { get => _supportColumnSection; set { if (SetProperty(ref _supportColumnSection, value ?? "")) Rebuild(); } }
    public string SelectedUpperBeamLoadType
    {
        get => _selectedUpperBeamLoadType;
        set
        {
            if (SetProperty(ref _selectedUpperBeamLoadType, NormalizeUpperBeamLoadType(value)))
            {
                OnPropertyChanged(nameof(UpperBeamLoadInputVisibility));
                OnPropertyChanged(nameof(UpperBeamUdlInputVisibility));
                OnPropertyChanged(nameof(UpperBeamPointLoadInputVisibility));
                Rebuild();
            }
        }
    }

    public Visibility UpperBeamLoadInputVisibility =>
        IsUpperBeamLoadEnabled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UpperBeamUdlInputVisibility =>
        ParseUpperBeamLoadType(SelectedUpperBeamLoadType) == CotArchUpperBeamLoadType.Udl ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UpperBeamPointLoadInputVisibility =>
        ParseUpperBeamLoadType(SelectedUpperBeamLoadType) == CotArchUpperBeamLoadType.PointLoadAtJoints ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedLoadPattern { get => _selectedLoadPattern; set { if (SetProperty(ref _selectedLoadPattern, value ?? "")) Rebuild(); } }
    public double UpperBeamUdlKnPerM { get => _upperBeamUdlKnPerM; set { if (SetProperty(ref _upperBeamUdlKnPerM, Math.Abs(Finite(value, 0)))) Rebuild(); } }
    public double UpperBeamPointLoadKn { get => _upperBeamPointLoadKn; set { if (SetProperty(ref _upperBeamPointLoadKn, Math.Abs(Finite(value, 0)))) Rebuild(); } }
    public string GenerationReport { get => _generationReport; set => SetProperty(ref _generationReport, value); }
    public string AppliedUpperBeamLoadStatus { get => _appliedUpperBeamLoadStatus; set => SetProperty(ref _appliedUpperBeamLoadStatus, value); }

    public CotArchModel CurrentModel
    {
        get => _currentModel;
        private set
        {
            if (!SetProperty(ref _currentModel, value))
                return;

            OnPropertyChanged(nameof(NodeCount));
            OnPropertyChanged(nameof(FrameCount));
            OnPropertyChanged(nameof(ArchSegmentCount));
            OnPropertyChanged(nameof(VerticalPostCount));
            OnPropertyChanged(nameof(UpperBeamSegmentCount));
            OnPropertyChanged(nameof(GroupName));
        }
    }

    public int NodeCount => CurrentModel.Nodes.Count;
    public int FrameCount => CurrentModel.FrameMemberCount;
    public int ArchSegmentCount => CurrentModel.ArchSegmentCount;
    public int VerticalPostCount => CurrentModel.VerticalPostCount;
    public int UpperBeamSegmentCount => CurrentModel.UpperBeamSegmentCount;
    public string GroupName => CurrentModel.GroupName;

    private void RefreshInstances()
    {
        EtabsInstanceListResult result = _etabs.ListEtabsInstances();
        Replace(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault();
        ConnectionStatus = result.Message;
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void ReadEtabsData()
    {
        EtabsParametricModelDataResult result = _etabs.ListParametricModelData(new EtabsParametricModelDataRequest { EtabsInstanceId = SelectedEtabsInstance?.Id });
        Replace(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance => instance.Id == result.SelectedInstanceId) ?? EtabsInstances.FirstOrDefault();
        Replace(FrameSections, result.FrameSections);
        _lastEtabsFrameSectionsForTension = result.FrameSections;
        RefreshTensionMemberSections();
        Replace(LoadPatterns, result.LoadPatterns);
        PickSections();
        PickLoadPattern();
        ConnectionStatus = result.Message;
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void RefreshSap2000Instances()
    {
        Sap2000InstanceListResult result = _sap2000.ListSap2000Instances();
        Replace(Sap2000Instances, result.Instances);
        SelectedSap2000Instance = Sap2000Instances.FirstOrDefault();
        Sap2000ConnectionStatus = result.Message;
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void ReadSap2000Data()
    {
        Sap2000ModelDataResult result = _sap2000.ListModelData(new Sap2000ModelDataRequest { Sap2000InstanceId = SelectedSap2000Instance?.Id });
        Replace(Sap2000Instances, result.Instances);
        SelectedSap2000Instance = Sap2000Instances.FirstOrDefault(instance => instance.Id == result.SelectedInstanceId) ?? Sap2000Instances.FirstOrDefault();
        Replace(FrameSections, result.FrameSections);
        _lastSap2000FrameSectionsForTension = result.FrameSections;
        _lastSap2000TensionSections = result.TensionMemberSections;
        RefreshTensionMemberSections();
        Replace(LoadPatterns, result.LoadPatterns);
        PickSections();
        PickLoadPattern();
        Sap2000ConnectionStatus = result.Message;
        Show(result.Warnings, result.Message, result.IsError);
    }

    private bool Validate(bool show)
    {
        Rebuild();
        ParametricValidationResult result = _validator.Validate(CurrentModel);
        if (show)
            Replace(Messages, result.Issues);

        return !result.HasCriticalIssues;
    }

    private void Draw(bool replace)
    {
        if (!Validate(true))
        {
            GenerationReport = "Generation blocked: resolve critical validation messages.";
            return;
        }

        CotArchDrawResult result = _etabs.DrawCotArch(new CotArchDrawRequest
        {
            EtabsInstanceId = SelectedEtabsInstance?.Id,
            Model = CurrentModel,
            ReplaceExistingStructure = replace
        });

        GenerationReport = result.Message;
        RefreshAppliedUpperBeamLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void UpdateLoads()
    {
        if (!Validate(true))
        {
            GenerationReport = "Load update blocked: resolve critical validation messages.";
            return;
        }

        CotArchDrawResult result = _etabs.UpdateCotArchLoads(new CotArchLoadUpdateRequest
        {
            EtabsInstanceId = SelectedEtabsInstance?.Id,
            Model = CurrentModel
        });

        GenerationReport = result.Message;
        RefreshAppliedUpperBeamLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void UpdateSap2000Loads()
    {
        if (!Validate(true))
        {
            GenerationReport = "SAP2000 load update blocked: resolve critical validation messages.";
            return;
        }

        CotArchDrawResult result = _sap2000.UpdateCotArchLoads(new Sap2000CotArchLoadUpdateRequest
        {
            Sap2000InstanceId = SelectedSap2000Instance?.Id,
            Model = CurrentModel
        });

        GenerationReport = result.Message;
        RefreshAppliedUpperBeamLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void Clear()
    {
        CotArchDrawResult result = _etabs.ClearCotArch(new CotArchClearRequest
        {
            EtabsInstanceId = SelectedEtabsInstance?.Id,
            ModelPrefix = CurrentModel.ModelPrefix,
            GroupName = CurrentModel.GroupName
        });

        GenerationReport = result.Message;
        if (!result.IsError)
            ClearAppliedUpperBeamLoads("No upper-beam loads are currently listed for this CoT Arch model.");
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void DrawSap2000(bool replace)
    {
        if (!Validate(true))
        {
            GenerationReport = "SAP2000 generation blocked: resolve critical validation messages.";
            return;
        }

        CotArchDrawResult result = _sap2000.DrawCotArch(new Sap2000CotArchDrawRequest
        {
            Sap2000InstanceId = SelectedSap2000Instance?.Id,
            Model = CurrentModel,
            ReplaceExistingStructure = replace
        });

        GenerationReport = result.Message + Environment.NewLine + "Tension tie: SAP2000 uses a cable/tendon object when the selected tie property is a cable/tendon section; otherwise it keeps the frame workflow.";
        RefreshAppliedUpperBeamLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void ClearSap2000()
    {
        CotArchDrawResult result = _sap2000.ClearCotArch(new Sap2000CotArchClearRequest
        {
            Sap2000InstanceId = SelectedSap2000Instance?.Id,
            ModelPrefix = CurrentModel.ModelPrefix,
            GroupName = CurrentModel.GroupName
        });

        GenerationReport = result.Message;
        if (!result.IsError)
            ClearAppliedUpperBeamLoads("No upper-beam loads are currently listed for this CoT Arch model.");
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void Rebuild()
    {
        (List<double>? customStations, string customStationError) = ParseCustomPostStations(CustomPostStationsText);
        CurrentModel = _builder.Build(new CotArchInput
        {
            ModelPrefix = ModelPrefix,
            OriginX = OriginX,
            PlaneY = PlaneY,
            BaseZ = BaseZ,
            SpringingZ = SpringingZ,
            UpperBeamZ = UpperBeamZ,
            Span = Span,
            Rise = Rise,
            PostCount = PostCount,
            CustomPostStations = customStations,
            CustomPostStationsError = customStationError,
            ArchSegmentsPerPostBay = ArchSegmentsPerPostBay,
            ProfileType = ParseEnum(SelectedProfileType, CotArchProfileType.Parabolic),
            ShapeExponent = ShapeExponent,
            ArchSection = ArchSection,
            PostSection = PostSection,
            UpperBeamSection = UpperBeamSection,
            TieSection = TieSection,
            SupportColumnSection = SupportColumnSection,
            GenerateAsPlanarModel = false,
            SupportCondition = CotArchSupportCondition.Pinned,
            ArchReleasePreset = ParseEnum(SelectedArchReleasePreset, CotArchMemberReleasePreset.FullyContinuous),
            PostReleasePreset = ParseEnum(SelectedPostReleasePreset, CotArchMemberReleasePreset.FullyContinuous),
            TieReleasePreset = ParseEnum(SelectedTieReleasePreset, CotArchMemberReleasePreset.PinnedBothEnds),
            BeamReleasePreset = ParseEnum(SelectedBeamReleasePreset, CotArchMemberReleasePreset.FullyContinuous),
            SupportColumnReleasePreset = ParseEnum(SelectedSupportColumnReleasePreset, CotArchMemberReleasePreset.FullyContinuous),
            UpperBeamLoadType = ParseUpperBeamLoadType(SelectedUpperBeamLoadType),
            UpperBeamLoadPattern = IsUpperBeamLoadEnabled ? SelectedLoadPattern : "",
            UpperBeamUdlKnPerM = UpperBeamUdlKnPerM,
            UpperBeamPointLoadKn = UpperBeamPointLoadKn
        });
    }

    private void PickSections()
    {
        string first = FrameSections.FirstOrDefault() ?? "";
        string firstTension = TensionMemberSections.FirstOrDefault() ?? first;
        ArchSection = PickTension(ArchSection, firstTension);
        PostSection = PickTension(PostSection, firstTension);
        UpperBeamSection = PickTension(UpperBeamSection, firstTension);
        TieSection = PickTension(TieSection, firstTension);
        SupportColumnSection = PickTension(SupportColumnSection, firstTension);
    }

    private void PickLoadPattern()
    {
        if (SelectedLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedLoadPattern))
            SelectedLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
    }

    private void RefreshAppliedUpperBeamLoads(CotArchDrawResult result)
    {
        if (result.IsError)
        {
            AppliedUpperBeamLoadStatus = "Applied load list was not refreshed because the CSI operation did not complete.";
            return;
        }

        Replace(AppliedUpperBeamLoads, result.AppliedUpperBeamLoads);
        AppliedUpperBeamLoadStatus = AppliedUpperBeamLoads.Count == 0
            ? "No upper-beam loads are currently listed for this CoT Arch model."
            : $"{AppliedUpperBeamLoads.Count} upper-beam load row(s) listed from the CSI model.";
    }

    private void ClearAppliedUpperBeamLoads(string status)
    {
        AppliedUpperBeamLoads.Clear();
        AppliedUpperBeamLoadStatus = status;
    }

    private string PickTension(string current, string fallback)
    {
        return current.Length > 0 && TensionMemberSections.Contains(current) ? current : fallback;
    }

    private void RefreshTensionMemberSections()
    {
        Replace(TensionMemberSections, _lastEtabsFrameSectionsForTension
            .Concat(_lastSap2000FrameSectionsForTension)
            .Concat(_lastSap2000TensionSections)
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .Select(section => section.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private bool IsUpperBeamLoadEnabled => ParseUpperBeamLoadType(SelectedUpperBeamLoadType) != CotArchUpperBeamLoadType.None;

    private static string NormalizeUpperBeamLoadType(string? value)
    {
        string text = (value ?? "").Trim();
        if (string.Equals(text, UdlLoadLabel, StringComparison.OrdinalIgnoreCase))
            return UdlLoadLabel;
        if (string.Equals(text, PointLoadLabel, StringComparison.OrdinalIgnoreCase))
            return PointLoadLabel;

        return NoLoadLabel;
    }

    private static CotArchUpperBeamLoadType ParseUpperBeamLoadType(string? value)
    {
        string text = NormalizeUpperBeamLoadType(value);
        if (string.Equals(text, UdlLoadLabel, StringComparison.OrdinalIgnoreCase))
            return CotArchUpperBeamLoadType.Udl;
        if (string.Equals(text, PointLoadLabel, StringComparison.OrdinalIgnoreCase))
            return CotArchUpperBeamLoadType.PointLoadAtJoints;

        return CotArchUpperBeamLoadType.None;
    }

    private static (List<double>? Stations, string Error) ParseCustomPostStations(string text)
    {
        string value = (text ?? "").Trim();
        if (value.Length == 0)
            return (null, "");

        string[] parts = value.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stations = new List<double>();
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double station) &&
                !double.TryParse(part, NumberStyles.Float, CultureInfo.CurrentCulture, out station))
            {
                return (null, $"Custom post station '{part}' is not a valid number.");
            }

            stations.Add(station);
        }

        return (stations, "");
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct
    {
        return Enum.TryParse(value, true, out TEnum parsed) ? parsed : fallback;
    }

    private void Show(IEnumerable<string> warnings, string summary, bool error)
    {
        var issues = new List<ValidationIssue> { new() { Severity = error ? ValidationSeverity.Critical : ValidationSeverity.Info, Message = summary } };
        issues.AddRange(warnings.Select(warning => new ValidationIssue { Severity = ValidationSeverity.Warning, Message = warning }));
        Replace(Messages, issues);
    }

    private static double Finite(double value, double fallback)
    {
        return double.IsFinite(value) ? value : fallback;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
