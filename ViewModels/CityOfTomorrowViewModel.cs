using System.Collections.ObjectModel;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class CityOfTomorrowViewModel : ObservableObject
{
    private readonly CityOfTomorrowGeometryBuilder _builder = new();
    private readonly CityOfTomorrowValidator _validator = new();
    private readonly EtabsParametricModellingService _etabs = new();
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _connectionStatus = "Not connected";
    private string _structureId = "VFR_01";
    private double _clearSpanL = 100, _depth = 18, _bottomZ = 20, _midRatio = 0.5, _tieZ = 2, _anchorWidth = 10, _sideHeight = 8, _pileCapZ;
    private int _panelsPerHalf = 5;
    private string _topSection = "", _midSection = "", _bottomSection = "", _verticalSection = "", _towerSection = "", _sideSection = "", _cableSection = "", _tieSection = "";
    private string _generationReport = "Adjust parameters, load ETABS sections, validate, then generate.";
    private CityOfTomorrowModel _currentModel = new();

    public CityOfTomorrowViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ValidateCommand = new RelayCommand(_ => Validate(true));
        GenerateNewModelCommand = new RelayCommand(_ => Draw(false));
        RegenerateStructureCommand = new RelayCommand(_ => Draw(true));
        ClearGeneratedStructureCommand = new RelayCommand(_ => Clear());
        Rebuild();
    }

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<string> FrameSections { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];
    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand GenerateNewModelCommand { get; }
    public ICommand RegenerateStructureCommand { get; }
    public ICommand ClearGeneratedStructureCommand { get; }

    public EtabsInstanceInfo? SelectedEtabsInstance { get => _selectedEtabsInstance; set => SetProperty(ref _selectedEtabsInstance, value); }
    public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }
    public string StructureId { get => _structureId; set { if (SetProperty(ref _structureId, value ?? "")) Rebuild(); } }
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
    public string CableSection { get => _cableSection; set { if (SetProperty(ref _cableSection, value ?? "")) Rebuild(); } }
    public string TieCableSection { get => _tieSection; set { if (SetProperty(ref _tieSection, value ?? "")) Rebuild(); } }
    public string GenerationReport { get => _generationReport; set => SetProperty(ref _generationReport, value); }

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
        PickSections();
        ConnectionStatus = result.Message;
        Show(result.Warnings, result.Message, result.IsError);
    }

    private bool Validate(bool show)
    {
        Rebuild();
        ParametricValidationResult result = _validator.Validate(CurrentModel);
        if (show) Replace(Messages, result.Issues);
        return !result.HasCriticalIssues;
    }

    private void Draw(bool replace)
    {
        if (!Validate(true)) { GenerationReport = "Generation blocked: resolve critical validation messages."; return; }
        CityOfTomorrowDrawResult result = _etabs.DrawCityOfTomorrow(new CityOfTomorrowDrawRequest { EtabsInstanceId = SelectedEtabsInstance?.Id, Model = CurrentModel, ReplaceExistingStructure = replace });
        GenerationReport = result.Message + Environment.NewLine + "Cables: pin-ended frame objects with zero compression capacity; use nonlinear analysis.";
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void Clear()
    {
        CityOfTomorrowDrawResult result = _etabs.ClearCityOfTomorrow(new CityOfTomorrowClearRequest { EtabsInstanceId = SelectedEtabsInstance?.Id, GroupName = GroupName, StructureId = CurrentModel.StructureId });
        GenerationReport = result.Message;
        Show(result.Warnings, result.Message, result.IsError);
    }

    private void Rebuild() => CurrentModel = _builder.Build(new CityOfTomorrowInput
    {
        StructureId = StructureId, ClearSpanL = ClearSpanL, PanelsPerHalfN = PanelsPerHalfN, VierendeelDepthH = VierendeelDepthH,
        BottomChordLevelZ = BottomChordLevelZ, MidRailRatio = MidRailRatio, TieLevelZ = TieLevelZ, ExternalAnchorWidth = ExternalAnchorWidth,
        ExternalSideFrameHeight = ExternalSideFrameHeight, PileCapLevelZ = PileCapLevelZ, TopChordSection = TopChordSection,
        MidRailSection = MidRailSection, BottomChordSection = BottomChordSection, VerticalPostSection = VerticalPostSection,
        TowerSection = TowerSection, SideFrameSection = SideFrameSection, CableSection = CableSection, TieCableSection = TieCableSection
    });

    private void PickSections()
    {
        string first = FrameSections.FirstOrDefault() ?? "";
        TopChordSection = Pick(TopChordSection, first); MidRailSection = Pick(MidRailSection, first); BottomChordSection = Pick(BottomChordSection, first);
        VerticalPostSection = Pick(VerticalPostSection, first); TowerSection = Pick(TowerSection, first); SideFrameSection = Pick(SideFrameSection, first);
        CableSection = Pick(CableSection, first); TieCableSection = Pick(TieCableSection, first);
    }
    private string Pick(string current, string fallback) => current.Length > 0 && FrameSections.Contains(current) ? current : fallback;
    private void Show(IEnumerable<string> warnings, string summary, bool error)
    {
        var issues = new List<ValidationIssue> { new() { Severity = error ? ValidationSeverity.Critical : ValidationSeverity.Info, Message = summary } };
        issues.AddRange(warnings.Select(w => new ValidationIssue { Severity = ValidationSeverity.Warning, Message = w }));
        Replace(Messages, issues);
    }
    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (T value in values) target.Add(value); }
}
