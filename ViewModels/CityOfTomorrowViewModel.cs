using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class CityOfTomorrowViewModel : ObservableObject
{
    private const string NoLoadLabel = "None";
    private const string UdlLoadLabel = "UDL";
    private const string PointLoadLabel = "Point load at joints";

    private readonly CityOfTomorrowGeometryBuilder _builder = new();
    private readonly CityOfTomorrowValidator _validator = new();
    private readonly EtabsParametricModellingService _etabs = new();
    private readonly Sap2000ModellingService _sap2000 = new();
    private List<string> _lastEtabsFrameSectionsForFlexible = [];
    private List<string> _lastSap2000FrameSectionsForFlexible = [];
    private List<string> _lastSap2000TensionSections = [];
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private Sap2000InstanceInfo? _selectedSap2000Instance;
    private string _connectionStatus = "Not connected";
    private string _sap2000ConnectionStatus = "Not connected";
    private string _structureId = "VFR_01";
    private double _clearSpanL = 100, _depth = 18, _bottomZ = 20, _midRatio = 0.5, _tieZ = 2, _anchorWidth = 10, _sideHeight = 8, _pileCapZ;
    private int _panelsPerHalf = 5;
    private string _topSection = "", _midSection = "", _bottomSection = "", _verticalSection = "", _towerSection = "", _sideSection = "", _sideVerticalSection = "", _sideX1Section = "", _sideX2Section = "", _cableSection = "", _tieSection = "";
    private string _selectedTopChordLoadType = NoLoadLabel;
    private string _selectedLoadPattern = "";
    private double _topChordUdlKnPerM;
    private double _topChordPointLoadKn;
    private string _selectedTopChordReleasePreset = CityMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedMidRailReleasePreset = CityMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedBottomChordReleasePreset = CityMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedVerticalPostReleasePreset = CityMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedTowerReleasePreset = CityMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedSideFrameReleasePreset = CityMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedSideVerticalReleasePreset = CityMemberReleasePreset.FullyContinuous.ToString();
    private string _selectedSideX1ReleasePreset = CityMemberReleasePreset.PinnedBothEnds.ToString();
    private string _selectedSideX2ReleasePreset = CityMemberReleasePreset.PinnedBothEnds.ToString();
    private string _selectedCableReleasePreset = CityMemberReleasePreset.PinnedBothEnds.ToString();
    private string _selectedTieReleasePreset = CityMemberReleasePreset.PinnedBothEnds.ToString();
    private string _generationReport = "Adjust parameters, load ETABS sections, validate, then generate.";
    private string _appliedTopChordLoadStatus = "Applied loads will be listed after Generate or Update Loads.";
    private CityOfTomorrowModel _currentModel = new();

    public CityOfTomorrowViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ValidateCommand = new RelayCommand(_ => Validate(true));
        GenerateNewModelCommand = new RelayCommand(_ => Draw(false));
        RegenerateStructureCommand = new RelayCommand(_ => Draw(true));
        UpdateLoadsCommand = new RelayCommand(_ => UpdateLoads());
        UpdateSap2000LoadsCommand = new RelayCommand(_ => UpdateSap2000Loads());
        ClearGeneratedStructureCommand = new RelayCommand(_ => Clear());
        RefreshSap2000InstancesCommand = new RelayCommand(_ => RefreshSap2000Instances());
        ReadSap2000DataCommand = new RelayCommand(_ => ReadSap2000Data());
        GenerateNewSap2000ModelCommand = new RelayCommand(_ => DrawSap2000(false));
        RegenerateSap2000StructureCommand = new RelayCommand(_ => DrawSap2000(true));
        ClearSap2000StructureCommand = new RelayCommand(_ => ClearSap2000());
        Rebuild();
    }

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<Sap2000InstanceInfo> Sap2000Instances { get; } = [];
    public ObservableCollection<string> FrameSections { get; } = [];
    public ObservableCollection<string> TensionMemberSections { get; } = [];
    public ObservableCollection<string> FrameOrTensionSections { get; } = [];
    public ObservableCollection<string> LoadPatterns { get; } = [];
    public ObservableCollection<CityAppliedTopChordLoad> AppliedTopChordLoads { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];
    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand GenerateNewModelCommand { get; }
    public ICommand RegenerateStructureCommand { get; }
    public ICommand UpdateLoadsCommand { get; }
    public ICommand UpdateSap2000LoadsCommand { get; }
    public ICommand ClearGeneratedStructureCommand { get; }
    public ICommand RefreshSap2000InstancesCommand { get; }
    public ICommand ReadSap2000DataCommand { get; }
    public ICommand GenerateNewSap2000ModelCommand { get; }
    public ICommand RegenerateSap2000StructureCommand { get; }
    public ICommand ClearSap2000StructureCommand { get; }

    public EtabsInstanceInfo? SelectedEtabsInstance { get => _selectedEtabsInstance; set => SetProperty(ref _selectedEtabsInstance, value); }
    public Sap2000InstanceInfo? SelectedSap2000Instance { get => _selectedSap2000Instance; set => SetProperty(ref _selectedSap2000Instance, value); }
    public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }
    public string Sap2000ConnectionStatus { get => _sap2000ConnectionStatus; set => SetProperty(ref _sap2000ConnectionStatus, value); }
    public string StructureId
    {
        get => _structureId;
        set
        {
            if (SetProperty(ref _structureId, value ?? ""))
            {
                ClearAppliedTopChordLoads("Applied loads will be listed after Generate or Update Loads.");
                Rebuild();
            }
        }
    }
    public double ClearSpanL { get => _clearSpanL; set { if (SetProperty(ref _clearSpanL, Finite(value, 100))) Rebuild(); } }
    public int PanelsPerHalfN { get => _panelsPerHalf; set { if (SetProperty(ref _panelsPerHalf, Math.Clamp(value, 1, 50))) Rebuild(); } }
    public double VierendeelDepthH { get => _depth; set { if (SetProperty(ref _depth, Finite(value, 18))) Rebuild(); } }
    public double BottomChordLevelZ { get => _bottomZ; set { if (SetProperty(ref _bottomZ, Finite(value, 20))) Rebuild(); } }
    public double MidRailRatio { get => _midRatio; set { if (SetProperty(ref _midRatio, Finite(value, 0.5))) Rebuild(); } }
    public double TieLevelZ { get => _tieZ; set { if (SetProperty(ref _tieZ, Finite(value, 2))) Rebuild(); } }
    public double ExternalAnchorWidth { get => _anchorWidth; set { if (SetProperty(ref _anchorWidth, Finite(value, 10))) Rebuild(); } }
    public double ExternalSideFrameHeight { get => _sideHeight; set { if (SetProperty(ref _sideHeight, Finite(value, 8))) Rebuild(); } }
    public double PileCapLevelZ { get => _pileCapZ; set { if (SetProperty(ref _pileCapZ, Finite(value, 0))) Rebuild(); } }
    public string TopChordSection { get => _topSection; set { if (SetProperty(ref _topSection, value ?? "")) Rebuild(); } }
    public string MidRailSection { get => _midSection; set { if (SetProperty(ref _midSection, value ?? "")) Rebuild(); } }
    public string BottomChordSection { get => _bottomSection; set { if (SetProperty(ref _bottomSection, value ?? "")) Rebuild(); } }
    public string VerticalPostSection { get => _verticalSection; set { if (SetProperty(ref _verticalSection, value ?? "")) Rebuild(); } }
    public string TowerSection { get => _towerSection; set { if (SetProperty(ref _towerSection, value ?? "")) Rebuild(); } }
    public string SideFrameSection { get => _sideSection; set { if (SetProperty(ref _sideSection, value ?? "")) Rebuild(); } }
    public string SideVerticalSection { get => _sideVerticalSection; set { if (SetProperty(ref _sideVerticalSection, value ?? "")) Rebuild(); } }
    public string SideX1Section { get => _sideX1Section; set { if (SetProperty(ref _sideX1Section, value ?? "")) Rebuild(); } }
    public string SideX2Section { get => _sideX2Section; set { if (SetProperty(ref _sideX2Section, value ?? "")) Rebuild(); } }
    public string CableSection { get => _cableSection; set { if (SetProperty(ref _cableSection, value ?? "")) Rebuild(); } }
    public string TieCableSection { get => _tieSection; set { if (SetProperty(ref _tieSection, value ?? "")) Rebuild(); } }
    public string SelectedTopChordLoadType
    {
        get => _selectedTopChordLoadType;
        set
        {
            if (SetProperty(ref _selectedTopChordLoadType, NormalizeTopChordLoadType(value)))
            {
                OnPropertyChanged(nameof(TopChordLoadInputVisibility));
                OnPropertyChanged(nameof(TopChordUdlInputVisibility));
                OnPropertyChanged(nameof(TopChordPointLoadInputVisibility));
                Rebuild();
            }
        }
    }

    public Visibility TopChordLoadInputVisibility =>
        IsTopChordLoadEnabled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TopChordUdlInputVisibility =>
        ParseTopChordLoadType(SelectedTopChordLoadType) == CityTopChordLoadType.Udl ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TopChordPointLoadInputVisibility =>
        ParseTopChordLoadType(SelectedTopChordLoadType) == CityTopChordLoadType.PointLoadAtJoints ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> TopChordLoadTypes { get; } = [NoLoadLabel, UdlLoadLabel, PointLoadLabel];
    public IReadOnlyList<string> ReleasePresets { get; } = Enum.GetNames(typeof(CityMemberReleasePreset));
    public string SelectedLoadPattern { get => _selectedLoadPattern; set { if (SetProperty(ref _selectedLoadPattern, value ?? "")) Rebuild(); } }
    public double TopChordUdlKnPerM { get => _topChordUdlKnPerM; set { if (SetProperty(ref _topChordUdlKnPerM, Math.Abs(Finite(value, 0)))) Rebuild(); } }
    public double TopChordPointLoadKn { get => _topChordPointLoadKn; set { if (SetProperty(ref _topChordPointLoadKn, Math.Abs(Finite(value, 0)))) Rebuild(); } }
    public string SelectedTopChordReleasePreset { get => _selectedTopChordReleasePreset; set { if (SetProperty(ref _selectedTopChordReleasePreset, value ?? CityMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedMidRailReleasePreset { get => _selectedMidRailReleasePreset; set { if (SetProperty(ref _selectedMidRailReleasePreset, value ?? CityMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedBottomChordReleasePreset { get => _selectedBottomChordReleasePreset; set { if (SetProperty(ref _selectedBottomChordReleasePreset, value ?? CityMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedVerticalPostReleasePreset { get => _selectedVerticalPostReleasePreset; set { if (SetProperty(ref _selectedVerticalPostReleasePreset, value ?? CityMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedTowerReleasePreset { get => _selectedTowerReleasePreset; set { if (SetProperty(ref _selectedTowerReleasePreset, value ?? CityMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedSideFrameReleasePreset { get => _selectedSideFrameReleasePreset; set { if (SetProperty(ref _selectedSideFrameReleasePreset, value ?? CityMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedSideVerticalReleasePreset { get => _selectedSideVerticalReleasePreset; set { if (SetProperty(ref _selectedSideVerticalReleasePreset, value ?? CityMemberReleasePreset.FullyContinuous.ToString())) Rebuild(); } }
    public string SelectedSideX1ReleasePreset { get => _selectedSideX1ReleasePreset; set { if (SetProperty(ref _selectedSideX1ReleasePreset, value ?? CityMemberReleasePreset.PinnedBothEnds.ToString())) Rebuild(); } }
    public string SelectedSideX2ReleasePreset { get => _selectedSideX2ReleasePreset; set { if (SetProperty(ref _selectedSideX2ReleasePreset, value ?? CityMemberReleasePreset.PinnedBothEnds.ToString())) Rebuild(); } }
    public string SelectedCableReleasePreset { get => _selectedCableReleasePreset; set { if (SetProperty(ref _selectedCableReleasePreset, value ?? CityMemberReleasePreset.PinnedBothEnds.ToString())) Rebuild(); } }
    public string SelectedTieReleasePreset { get => _selectedTieReleasePreset; set { if (SetProperty(ref _selectedTieReleasePreset, value ?? CityMemberReleasePreset.PinnedBothEnds.ToString())) Rebuild(); } }
    public string GenerationReport { get => _generationReport; set => SetProperty(ref _generationReport, value); }
    public string AppliedTopChordLoadStatus { get => _appliedTopChordLoadStatus; set => SetProperty(ref _appliedTopChordLoadStatus, value); }

    public CityOfTomorrowModel CurrentModel
    {
        get => _currentModel;
        private set
        {
            if (!SetProperty(ref _currentModel, value)) return;
            OnPropertyChanged(nameof(TotalPanels)); OnPropertyChanged(nameof(PanelWidth)); OnPropertyChanged(nameof(NodeCount));
            OnPropertyChanged(nameof(FrameCount)); OnPropertyChanged(nameof(TensionOnlyCount)); OnPropertyChanged(nameof(GroupName));
        }
    }
    public int TotalPanels => CurrentModel.TotalPanels;
    public double PanelWidth => CurrentModel.PanelWidth;
    public int NodeCount => CurrentModel.Nodes.Count;
    public int FrameCount => CurrentModel.FrameMemberCount;
    public int TensionOnlyCount => CurrentModel.TensionOnlyMemberCount;
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
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(x => x.Id == result.SelectedInstanceId) ?? EtabsInstances.FirstOrDefault();
        Replace(FrameSections, result.FrameSections);
        Replace(TensionMemberSections, result.FrameSections);
        _lastEtabsFrameSectionsForFlexible = result.FrameSections;
        RefreshFrameOrTensionSections();
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
        SelectedSap2000Instance = Sap2000Instances.FirstOrDefault(x => x.Id == result.SelectedInstanceId) ?? Sap2000Instances.FirstOrDefault();
        Replace(FrameSections, result.FrameSections);
        Replace(TensionMemberSections, result.TensionMemberSections);
        _lastSap2000FrameSectionsForFlexible = result.FrameSections;
        _lastSap2000TensionSections = result.TensionMemberSections;
        RefreshFrameOrTensionSections();
        Replace(LoadPatterns, result.LoadPatterns);
        PickSections(allowFrameFallbackForTension: false);
        PickLoadPattern();
        Sap2000ConnectionStatus = result.Message;
        Show(result.Warnings, result.Message, result.IsError);
    }

    private bool Validate(bool show, bool validateTopChordLoading = true)
    {
        Rebuild();
        ParametricValidationResult result = _validator.Validate(CurrentModel, validateTopChordLoading);
        if (show) Replace(Messages, result.Issues);
        return !result.HasCriticalIssues;
    }

    private void Draw(bool replace)
    {
        if (!Validate(true)) { GenerationReport = "Generation blocked: resolve critical validation messages."; return; }
        CityOfTomorrowDrawResult result = _etabs.DrawCityOfTomorrow(new CityOfTomorrowDrawRequest { EtabsInstanceId = SelectedEtabsInstance?.Id, Model = CurrentModel, ReplaceExistingStructure = replace });
        GenerationReport = result.Message + Environment.NewLine + "Cables: pin-ended frame objects with zero compression capacity; use nonlinear analysis.";
        RefreshAppliedTopChordLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void UpdateLoads()
    {
        if (!Validate(true)) { GenerationReport = "Load update blocked: resolve critical validation messages."; return; }
        CityOfTomorrowDrawResult result = _etabs.UpdateCityOfTomorrowLoads(new CityOfTomorrowLoadUpdateRequest { EtabsInstanceId = SelectedEtabsInstance?.Id, Model = CurrentModel });
        GenerationReport = result.Message;
        RefreshAppliedTopChordLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void UpdateSap2000Loads()
    {
        if (!Validate(true)) { GenerationReport = "SAP2000 load update blocked: resolve critical validation messages."; return; }
        CityOfTomorrowDrawResult result = _sap2000.UpdateCityOfTomorrowLoads(new Sap2000CityOfTomorrowLoadUpdateRequest { Sap2000InstanceId = SelectedSap2000Instance?.Id, Model = CurrentModel });
        GenerationReport = result.Message;
        RefreshAppliedTopChordLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void Clear()
    {
        CityOfTomorrowDrawResult result = _etabs.ClearCityOfTomorrow(new CityOfTomorrowClearRequest { EtabsInstanceId = SelectedEtabsInstance?.Id, GroupName = GroupName, StructureId = CurrentModel.StructureId });
        GenerationReport = result.Message;
        if (!result.IsError)
            ClearAppliedTopChordLoads("No top-chord loads are currently listed for this City of Tomorrow model.");
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void DrawSap2000(bool replace)
    {
        if (!Validate(true, validateTopChordLoading: false)) { GenerationReport = "SAP2000 generation blocked: resolve critical validation messages."; return; }
        CityOfTomorrowDrawResult result = _sap2000.DrawCityOfTomorrow(new Sap2000CityOfTomorrowDrawRequest { Sap2000InstanceId = SelectedSap2000Instance?.Id, Model = CurrentModel, ReplaceExistingStructure = replace });
        GenerationReport = result.Message + Environment.NewLine + "Cables/ties: SAP2000 cable or tendon objects using the selected cable/tendon property; use nonlinear analysis.";
        RefreshAppliedTopChordLoads(result);
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void ClearSap2000()
    {
        CityOfTomorrowDrawResult result = _sap2000.ClearCityOfTomorrow(new Sap2000CityOfTomorrowClearRequest { Sap2000InstanceId = SelectedSap2000Instance?.Id, GroupName = GroupName });
        GenerationReport = result.Message;
        if (!result.IsError)
            ClearAppliedTopChordLoads("No top-chord loads are currently listed for this City of Tomorrow model.");
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void Rebuild() => CurrentModel = _builder.Build(new CityOfTomorrowInput
    {
        StructureId = StructureId, ClearSpanL = ClearSpanL, PanelsPerHalfN = PanelsPerHalfN, VierendeelDepthH = VierendeelDepthH,
        BottomChordLevelZ = BottomChordLevelZ, MidRailRatio = MidRailRatio, TieLevelZ = TieLevelZ, ExternalAnchorWidth = ExternalAnchorWidth,
        ExternalSideFrameHeight = ExternalSideFrameHeight, PileCapLevelZ = PileCapLevelZ, TopChordSection = TopChordSection,
        MidRailSection = MidRailSection, BottomChordSection = BottomChordSection, VerticalPostSection = VerticalPostSection,
        TowerSection = TowerSection,
        SideFrameSection = SideFrameSection,
        SideVerticalSection = SideVerticalSection,
        SideX1Section = SideX1Section,
        SideX2Section = SideX2Section,
        CableSection = CableSection,
        TieCableSection = TieCableSection,
        TopChordReleasePreset = ParseEnum(SelectedTopChordReleasePreset, CityMemberReleasePreset.FullyContinuous),
        MidRailReleasePreset = ParseEnum(SelectedMidRailReleasePreset, CityMemberReleasePreset.FullyContinuous),
        BottomChordReleasePreset = ParseEnum(SelectedBottomChordReleasePreset, CityMemberReleasePreset.FullyContinuous),
        VerticalPostReleasePreset = ParseEnum(SelectedVerticalPostReleasePreset, CityMemberReleasePreset.FullyContinuous),
        TowerReleasePreset = ParseEnum(SelectedTowerReleasePreset, CityMemberReleasePreset.FullyContinuous),
        SideFrameReleasePreset = ParseEnum(SelectedSideFrameReleasePreset, CityMemberReleasePreset.FullyContinuous),
        SideVerticalReleasePreset = ParseEnum(SelectedSideVerticalReleasePreset, CityMemberReleasePreset.FullyContinuous),
        SideX1ReleasePreset = ParseEnum(SelectedSideX1ReleasePreset, CityMemberReleasePreset.PinnedBothEnds),
        SideX2ReleasePreset = ParseEnum(SelectedSideX2ReleasePreset, CityMemberReleasePreset.PinnedBothEnds),
        CableReleasePreset = ParseEnum(SelectedCableReleasePreset, CityMemberReleasePreset.PinnedBothEnds),
        TieReleasePreset = ParseEnum(SelectedTieReleasePreset, CityMemberReleasePreset.PinnedBothEnds),
        TopChordLoadType = ParseTopChordLoadType(SelectedTopChordLoadType),
        TopChordLoadPattern = IsTopChordLoadEnabled ? SelectedLoadPattern : "",
        TopChordUdlKnPerM = TopChordUdlKnPerM,
        TopChordPointLoadKn = TopChordPointLoadKn
    });

    private void PickSections(bool allowFrameFallbackForTension = true)
    {
        string first = FrameSections.FirstOrDefault() ?? "";
        string firstTension = TensionMemberSections.FirstOrDefault() ?? (allowFrameFallbackForTension ? first : "");
        string firstFlexible = FrameOrTensionSections.FirstOrDefault() ?? firstTension;
        TopChordSection = PickFrameOrTension(TopChordSection, firstFlexible);
        MidRailSection = PickFrameOrTension(MidRailSection, firstFlexible);
        BottomChordSection = PickFrameOrTension(BottomChordSection, firstFlexible);
        VerticalPostSection = PickFrameOrTension(VerticalPostSection, firstFlexible);
        TowerSection = PickFrameOrTension(TowerSection, firstFlexible);
        SideFrameSection = PickFrameOrTension(SideFrameSection, firstFlexible);
        SideVerticalSection = PickFrameOrTension(SideVerticalSection, firstFlexible);
        SideX1Section = PickFrameOrTension(SideX1Section, firstFlexible);
        SideX2Section = PickFrameOrTension(SideX2Section, firstFlexible);
        CableSection = PickFrameOrTension(CableSection, firstFlexible);
        TieCableSection = PickFrameOrTension(TieCableSection, firstFlexible);
    }
    private string PickFrameOrTension(string current, string fallback) => current.Length > 0 && FrameOrTensionSections.Contains(current) ? current : fallback;

    private void RefreshFrameOrTensionSections()
    {
        Replace(FrameOrTensionSections, _lastEtabsFrameSectionsForFlexible
            .Concat(_lastSap2000FrameSectionsForFlexible)
            .Concat(_lastSap2000TensionSections)
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .Select(section => section.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void PickLoadPattern()
    {
        if (SelectedLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedLoadPattern))
            SelectedLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
    }

    private void RefreshAppliedTopChordLoads(CityOfTomorrowDrawResult result)
    {
        if (result.IsError)
        {
            AppliedTopChordLoadStatus = "Applied load list was not refreshed because the CSI operation did not complete.";
            return;
        }

        Replace(AppliedTopChordLoads, result.AppliedTopChordLoads);
        AppliedTopChordLoadStatus = AppliedTopChordLoads.Count == 0
            ? "No top-chord loads are currently listed for this City of Tomorrow model."
            : $"{AppliedTopChordLoads.Count} top-chord load row(s) listed from the CSI model.";
    }

    private void ClearAppliedTopChordLoads(string status)
    {
        AppliedTopChordLoads.Clear();
        AppliedTopChordLoadStatus = status;
    }

    private bool IsTopChordLoadEnabled => ParseTopChordLoadType(SelectedTopChordLoadType) != CityTopChordLoadType.None;

    private static string NormalizeTopChordLoadType(string? value)
    {
        string text = (value ?? "").Trim();
        if (string.Equals(text, UdlLoadLabel, StringComparison.OrdinalIgnoreCase))
            return UdlLoadLabel;
        if (string.Equals(text, PointLoadLabel, StringComparison.OrdinalIgnoreCase))
            return PointLoadLabel;

        return NoLoadLabel;
    }

    private static CityTopChordLoadType ParseTopChordLoadType(string? value)
    {
        string text = NormalizeTopChordLoadType(value);
        if (string.Equals(text, UdlLoadLabel, StringComparison.OrdinalIgnoreCase))
            return CityTopChordLoadType.Udl;
        if (string.Equals(text, PointLoadLabel, StringComparison.OrdinalIgnoreCase))
            return CityTopChordLoadType.PointLoadAtJoints;

        return CityTopChordLoadType.None;
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum
    {
        return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
    }

    private void Show(IEnumerable<string> warnings, string summary, bool error)
    {
        var issues = new List<ValidationIssue> { new() { Severity = error ? ValidationSeverity.Critical : ValidationSeverity.Info, Message = summary } };
        issues.AddRange(warnings.Select(w => new ValidationIssue { Severity = ValidationSeverity.Warning, Message = w }));
        Replace(Messages, issues);
    }
    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (T value in values) target.Add(value); }
}
