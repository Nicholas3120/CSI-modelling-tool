using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class ParametricModellingViewModel : ObservableObject
{
    private const double GeometrySliderStep = 0.05;
    private const string TopChordLoadTargetLabel = "Top chord";
    private const string BottomChordLoadTargetLabel = "Bottom chord";
    private const string LineLoadInputLabel = "Line load (kN/m)";
    private const string AreaLoadInputLabel = "Area load (kPa)";
    private const string PanelNodesApplicationLabel = "Panel nodes";
    private const string MemberLineApplicationLabel = "Line load";

    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly ParametricTrussGenerator _generator = new();
    private readonly ParametricTrussValidator _validator = new();
    private bool _etabsDataLoaded;
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _connectionStatus = "Not connected";
    private string _trussId = "TR01";
    private string _groupName = "WPF_TRUSS_TR01";
    private string _selectedTrussType = TrussType.Warren.ToString();
    private bool _useSelectedInsertionPoints;
    private double _manualSpan = 12.0;
    private double _height = 2.5;
    private int _panelCount = 6;
    private double _spiralCentreX;
    private double _spiralCentreY;
    private double _spiralBaseZ;
    private double _spiralTotalHeight = 3.6;
    private double _spiralInnerRadius = 1.0;
    private double _spiralOuterRadius = 2.0;
    private int _spiralStepCount = 24;
    private double _spiralTotalRotationDegrees = 360.0;
    private double _spiralStartAngleDegrees;
    private string _selectedSpiralRotationDirection = SpiralRotationDirectionLabels.Anticlockwise;
    private bool _spiralCreateInnerStringer = true;
    private bool _spiralCreateOuterStringer = true;
    private bool _spiralCreateRadialTreadBeams = true;
    private bool _spiralCreateTreadShellPlates;
    private bool _spiralCreateCentralColumn;
    private bool _spiralCreateTopLandingBeam;
    private bool _spiralCreateBottomLandingBeam;
    private string _selectedInnerStringerSection = "";
    private string _selectedOuterStringerSection = "";
    private string _selectedRadialTreadSection = "";
    private string _selectedCentralColumnSection = "";
    private string _selectedLandingBeamSection = "";
    private string _selectedTreadShellProperty = "";
    private double _fishStartX;
    private double _fishStartY;
    private double _fishStartZ = 10.0;
    private double _fishSpanLength = 24.0;
    private int _fishPanelCount = 12;
    private double _fishEndDepth = 1.0;
    private double _fishMiddleDepth = 3.0;
    private double _fishDirectionAngleDegrees;
    private double _fishTopChordSlopeDegrees;
    private string _selectedFishBottomChordShape = FishBottomChordShapeLabels.Parabolic;
    private string _selectedFishWebPattern = FishWebPatternLabels.VerticalAlternating;
    private bool _fishUseSameSection;
    private string _selectedFishSameSection = "";
    private string _selectedFishTopChordSection = "";
    private string _selectedFishBottomChordSection = "";
    private string _selectedFishVerticalSection = "";
    private string _selectedFishDiagonalSection = "";
    private bool _fishReleaseMoments = true;
    private double _variableStartX;
    private double _variableStartY;
    private double _variableStartZ = 10.0;
    private double _variableSpanLength = 24.0;
    private int _variablePanelCount = 12;
    private double _variableTrussDepth = 2.5;
    private double _variableEndPanelWidthRatio = 0.5;
    private double _variableMiddlePanelWidthRatio = 1.5;
    private double _variableDirectionAngleDegrees;
    private string _selectedVariablePanelWidthVariation = VariablePanelWidthVariationLabels.Parabolic;
    private string _selectedVariableWebPattern = FishWebPatternLabels.VerticalAlternating;
    private bool _variableUseSameSection;
    private string _selectedVariableSameSection = "";
    private string _selectedVariableTopChordSection = "";
    private string _selectedVariableBottomChordSection = "";
    private string _selectedVariableVerticalSection = "";
    private string _selectedVariableDiagonalSection = "";
    private bool _variableReleaseMoments = true;
    private double _roofSlopePercent;
    private double _bottomChordSlopePercent;
    private string _selectedTopChordSlopeMode = ChordSlopeMode.Pitch.ToString();
    private string _selectedBottomChordSlopeMode = ChordSlopeMode.Pitch.ToString();
    private string _selectedTopChordSection = "";
    private string _selectedBottomChordSection = "";
    private string _selectedDiagonalSection = "";
    private string _selectedVerticalSection = "";
    private string _selectedEndPostSection = "";
    private string _selectedSecondarySection = "";
    private bool _applyTopChordLoad = true;
    private bool _applyTopChordLoadToPanelNodes = true;
    private string _selectedLoadPattern = "";
    private double _topChordGravityLoadKnPerM = 5.0;
    private bool _applyBottomChordLoad;
    private bool _applyBottomChordLoadToPanelNodes = true;
    private double _bottomChordLoadMagnitude = 10.0;
    private string _selectedLoadTarget = TopChordLoadTargetLabel;
    private string _selectedLoadInputType = LineLoadInputLabel;
    private string _selectedLoadApplicationMode = PanelNodesApplicationLabel;
    private double _loadMagnitude = 5.0;
    private double _loadPanelWidth = 1.0;
    private int _loadDefinitionCounter;
    private string _selectedEtabsExportMode = EtabsExportModeLabels.EraseAndRedraw;
    private string _selectedSupportNodeMode = SupportNodeModeLabels.EndBottomNodes;
    private string _selectedSupportRestraintType = SupportRestraintTypeLabels.FirstPinOthersRoller;
    private double _addAsNewOffsetX;
    private double _addAsNewOffsetY = 3.0;
    private double _addAsNewOffsetZ;
    private string _selectedInsertionSummary = "Manual coordinates";
    private bool _sectionEditorUseSelectedFrames = true;
    private string _sectionEditorGroupName = "";
    private bool _sectionEditorAssignToNewGroup;
    private string _sectionEditorNewGroupName = "";
    private string _sectionEditorBulkSection = "";
    private string _sectionEditorStatus = "No ETABS frames imported";
    private string _sectionEditorLoadPattern = "";
    private string _sectionEditorLoadInputType = LineLoadInputLabel;
    private double _sectionEditorLoadMagnitude = 5.0;
    private double _sectionEditorLoadPanelWidth = 1.0;
    private bool _sectionEditorReplaceSelectedPatternLoads = true;
    private string _selectedSectionPreviewMode = SectionPreviewModeLabels.ThreeD;
    private EtabsFrameSectionRow? _selectedSectionEditorFrame;
    private ParametricTrussModel _currentModel = new();

    public ParametricModellingViewModel()
    {
        Dome = new DomeStructureViewModel();
        PlateGirder = new PlateGirderViewModel();
        BeamSplice = new BeamSpliceConnectionViewModel();
        PileEccMoment = new PileEccentricityViewModel();
        Railing = new SteelRailingViewModel();
        WallDrain = new WallDrainViewModel();
        HydrostaticShellLoad = new HydrostaticShellLoadViewModel();
        LoadCaseCombination = new LoadCaseCombinationViewModel();
        SectionProperty = new SectionPropertyViewModel();
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ReadSelectedPointsCommand = new RelayCommand(_ => ReadSelectedPoints());
        ValidateCommand = new RelayCommand(_ => ValidateCurrentModel(true));
        SendToEtabsCommand = new RelayCommand(_ => SendToEtabs());
        ImportEtabsFramesCommand = new RelayCommand(_ => ImportEtabsFramesForSectionEditing());
        ImportEtabsGroupFramesCommand = new RelayCommand(_ => ImportEtabsGroupFramesForSectionEditing());
        AssignSelectedEtabsFramesToGroupCommand = new RelayCommand(_ => AssignSelectedEtabsFramesToGroup());
        ApplyBulkSectionCommand = new RelayCommand(_ => ApplyBulkSectionToCheckedRows());
        UpdateEtabsFrameSectionsCommand = new RelayCommand(_ => UpdateEtabsFrameSections());
        UpdateEtabsGroupFrameSectionsCommand = new RelayCommand(_ => UpdateEtabsGroupFrameSections());
        UpdateEtabsFrameLoadsCommand = new RelayCommand(_ => UpdateEtabsFrameLoads());
        CheckAllSectionRowsCommand = new RelayCommand(_ => SetSectionRowsChecked(true));
        UncheckAllSectionRowsCommand = new RelayCommand(_ => SetSectionRowsChecked(false));
        AddTrussLoadCommand = new RelayCommand(_ => AddTrussLoad());
        RemoveTrussLoadCommand = new RelayCommand(RemoveTrussLoad);
        ClearTrussLoadsCommand = new RelayCommand(_ => ClearTrussLoads(), _ => TrussLoads.Count > 0);
        TrussLoads.CollectionChanged += (_, _) =>
        {
            CommandManager.InvalidateRequerySuggested();
            RegeneratePreview();
        };

        RegeneratePreview();
    }

    public DomeStructureViewModel Dome { get; }
    public PlateGirderViewModel PlateGirder { get; }
    public BeamSpliceConnectionViewModel BeamSplice { get; }
    public PileEccentricityViewModel PileEccMoment { get; }
    public SteelRailingViewModel Railing { get; }
    public WallDrainViewModel WallDrain { get; }
    public HydrostaticShellLoadViewModel HydrostaticShellLoad { get; }
    public LoadCaseCombinationViewModel LoadCaseCombination { get; }
    public SectionPropertyViewModel SectionProperty { get; }

    public IReadOnlyList<string> TrussTypes { get; } =
    [
        TrussType.Warren.ToString(),
        TrussType.Pratt.ToString(),
        TrussType.Howe.ToString(),
        TrussType.K.ToString(),
        TrussType.SimpleFrame.ToString(),
        TrussTypeLabels.SpiralStaircase,
        TrussTypeLabels.FishBellyTruss,
        TrussTypeLabels.VariablePanelWidthTruss
    ];
    public IReadOnlyList<string> SlopeModes { get; } = Enum.GetNames(typeof(ChordSlopeMode));
    public IReadOnlyList<string> SpiralRotationDirections { get; } =
    [
        SpiralRotationDirectionLabels.Anticlockwise,
        SpiralRotationDirectionLabels.Clockwise
    ];
    public IReadOnlyList<string> FishBottomChordShapes { get; } =
    [
        FishBottomChordShapeLabels.Parabolic,
        FishBottomChordShapeLabels.LinearToMiddle,
        FishBottomChordShapeLabels.CircularArc
    ];
    public IReadOnlyList<string> FishWebPatterns { get; } =
    [
        FishWebPatternLabels.VerticalAlternating,
        FishWebPatternLabels.VerticalSameDirection,
        FishWebPatternLabels.Warren,
        FishWebPatternLabels.Pratt,
        FishWebPatternLabels.Howe,
        FishWebPatternLabels.CrossBracing
    ];
    public IReadOnlyList<string> VariablePanelWidthVariations { get; } =
    [
        VariablePanelWidthVariationLabels.Parabolic,
        VariablePanelWidthVariationLabels.SmoothCosine,
        VariablePanelWidthVariationLabels.LinearToMiddle
    ];
    public IReadOnlyList<string> VariableWebPatterns { get; } =
    [
        FishWebPatternLabels.VerticalAlternating,
        FishWebPatternLabels.VerticalSameDirection,
        FishWebPatternLabels.Warren,
        FishWebPatternLabels.Pratt,
        FishWebPatternLabels.Howe,
        FishWebPatternLabels.CrossBracing
    ];
    public IReadOnlyList<string> EtabsExportModes { get; } =
    [
        EtabsExportModeLabels.EraseAndRedraw,
        EtabsExportModeLabels.AddAsNew
    ];
    public IReadOnlyList<string> SupportNodeModes { get; } =
    [
        SupportNodeModeLabels.EndBottomNodes,
        SupportNodeModeLabels.AllBottomChordNodes,
        SupportNodeModeLabels.NoSupports
    ];
    public IReadOnlyList<string> SupportRestraintTypes { get; } =
    [
        SupportRestraintTypeLabels.FirstPinOthersRoller,
        SupportRestraintTypeLabels.AllPinned,
        SupportRestraintTypeLabels.AllZRollers
    ];
    public IReadOnlyList<string> LoadTargets { get; } =
    [
        TopChordLoadTargetLabel,
        BottomChordLoadTargetLabel
    ];
    public IReadOnlyList<string> LoadInputTypes { get; } =
    [
        LineLoadInputLabel,
        AreaLoadInputLabel
    ];
    public IReadOnlyList<string> LoadApplicationModes { get; } =
    [
        PanelNodesApplicationLabel,
        MemberLineApplicationLabel
    ];
    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<string> FrameSections { get; } = [];
    public ObservableCollection<string> ShellProperties { get; } = [];
    public ObservableCollection<string> LoadPatterns { get; } = [];
    public ObservableCollection<string> LoadCombinations { get; } = [];
    public ObservableCollection<string> Stories { get; } = [];
    public ObservableCollection<string> Groups { get; } = [];
    public ObservableCollection<EtabsPointInfo> SelectedInsertionPoints { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];
    public ObservableCollection<ParametricTrussLoadDefinition> TrussLoads { get; } = [];
    public ObservableCollection<EtabsFrameSectionRow> SectionEditorFrames { get; } = [];
    public ObservableCollection<ValidationIssue> SectionEditorMessages { get; } = [];
    public IReadOnlyList<string> SectionPreviewModes { get; } =
    [
        SectionPreviewModeLabels.ThreeD,
        SectionPreviewModeLabels.TwoD
    ];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand ReadSelectedPointsCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand SendToEtabsCommand { get; }
    public ICommand ImportEtabsFramesCommand { get; }
    public ICommand ImportEtabsGroupFramesCommand { get; }
    public ICommand AssignSelectedEtabsFramesToGroupCommand { get; }
    public ICommand ApplyBulkSectionCommand { get; }
    public ICommand UpdateEtabsFrameSectionsCommand { get; }
    public ICommand UpdateEtabsGroupFrameSectionsCommand { get; }
    public ICommand UpdateEtabsFrameLoadsCommand { get; }
    public ICommand CheckAllSectionRowsCommand { get; }
    public ICommand UncheckAllSectionRowsCommand { get; }
    public ICommand AddTrussLoadCommand { get; }
    public ICommand RemoveTrussLoadCommand { get; }
    public ICommand ClearTrussLoadsCommand { get; }

    public EtabsInstanceInfo? SelectedEtabsInstance
    {
        get => _selectedEtabsInstance;
        set
        {
            if (SetProperty(ref _selectedEtabsInstance, value))
            {
                OnPropertyChanged(nameof(SelectedEtabsInstanceId));
                RegeneratePreview();
            }
        }
    }

    public string SelectedEtabsInstanceId => SelectedEtabsInstance?.Id ?? "";

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string TrussId
    {
        get => _trussId;
        set
        {
            if (SetProperty(ref _trussId, value))
            {
                if (string.IsNullOrWhiteSpace(GroupName) || GroupName.StartsWith("WPF_TRUSS_", StringComparison.OrdinalIgnoreCase))
                    GroupName = EtabsNameUtility.BuildSafeName("WPF_TRUSS_", _trussId);
                RegeneratePreview();
            }
        }
    }

    public string GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedTrussType
    {
        get => _selectedTrussType;
        set
        {
            if (SetProperty(ref _selectedTrussType, value))
            {
                ApplyDefaultNameForSelectedType();
                OnPropertyChanged(nameof(StandardTrussInputsVisibility));
                OnPropertyChanged(nameof(SpiralInputsVisibility));
                OnPropertyChanged(nameof(FishBellyInputsVisibility));
                OnPropertyChanged(nameof(VariablePanelInputsVisibility));
                OnPropertyChanged(nameof(StandardSectionsVisibility));
                OnPropertyChanged(nameof(SpiralSectionsVisibility));
                OnPropertyChanged(nameof(FishSectionsVisibility));
                OnPropertyChanged(nameof(VariableSectionsVisibility));
                OnPropertyChanged(nameof(LoadsVisibility));
                OnPropertyChanged(nameof(StandardSupportOptionsVisibility));
                RegeneratePreview();
            }
        }
    }

    public Visibility StandardTrussInputsVisibility => IsStandardTrussType ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SpiralInputsVisibility => CurrentTrussType == TrussType.SpiralStaircase ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FishBellyInputsVisibility => CurrentTrussType == TrussType.FishBellyTruss ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VariablePanelInputsVisibility => CurrentTrussType == TrussType.VariablePanelWidthTruss ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StandardSectionsVisibility => IsStandardTrussType ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SpiralSectionsVisibility => CurrentTrussType == TrussType.SpiralStaircase ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FishSectionsVisibility => CurrentTrussType == TrussType.FishBellyTruss ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VariableSectionsVisibility => CurrentTrussType == TrussType.VariablePanelWidthTruss ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LoadsVisibility => IsStandardTrussType ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StandardSupportOptionsVisibility => IsStandardTrussType ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SpiralShellPropertyVisibility => SpiralCreateTreadShellPlates ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SpiralCentralColumnSectionVisibility => SpiralCreateCentralColumn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SpiralLandingBeamSectionVisibility => SpiralCreateTopLandingBeam || SpiralCreateBottomLandingBeam ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FishSameSectionVisibility => FishUseSameSection ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FishSeparateSectionsVisibility => FishUseSameSection ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VariableSameSectionVisibility => VariableUseSameSection ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VariableSeparateSectionsVisibility => VariableUseSameSection ? Visibility.Collapsed : Visibility.Visible;

    private TrussType CurrentTrussType => ToTrussType(SelectedTrussType);

    private bool IsStandardTrussType =>
        CurrentTrussType != TrussType.SpiralStaircase &&
        CurrentTrussType != TrussType.FishBellyTruss &&
        CurrentTrussType != TrussType.VariablePanelWidthTruss;

    public bool UseSelectedInsertionPoints
    {
        get => _useSelectedInsertionPoints;
        set
        {
            if (SetProperty(ref _useSelectedInsertionPoints, value))
            {
                OnPropertyChanged(nameof(IsManualSpanEnabled));
                RegeneratePreview();
            }
        }
    }

    public bool IsManualSpanEnabled => !UseSelectedInsertionPoints;

    public double ManualSpan
    {
        get => _manualSpan;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.001, RoundToGeometryStep(value)) : 12.0;
            if (SetProperty(ref _manualSpan, next))
                RegeneratePreview();
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.001, RoundToGeometryStep(value)) : 2.5;
            if (SetProperty(ref _height, next))
                RegeneratePreview();
        }
    }

    public int PanelCount
    {
        get => _panelCount;
        set
        {
            int next = Math.Clamp(value, 2, 60);
            if (SetProperty(ref _panelCount, next))
                RegeneratePreview();
        }
    }

    public double SpiralCentreX { get => _spiralCentreX; set { if (SetProperty(ref _spiralCentreX, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double SpiralCentreY { get => _spiralCentreY; set { if (SetProperty(ref _spiralCentreY, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double SpiralBaseZ { get => _spiralBaseZ; set { if (SetProperty(ref _spiralBaseZ, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double SpiralTotalHeight { get => _spiralTotalHeight; set { if (SetProperty(ref _spiralTotalHeight, double.IsFinite(value) ? Math.Max(0.001, value) : 3.6)) RegeneratePreview(); } }
    public double SpiralInnerRadius { get => _spiralInnerRadius; set { if (SetProperty(ref _spiralInnerRadius, double.IsFinite(value) ? Math.Max(0.001, value) : 1.0)) RegeneratePreview(); } }
    public double SpiralOuterRadius { get => _spiralOuterRadius; set { if (SetProperty(ref _spiralOuterRadius, double.IsFinite(value) ? Math.Max(0.001, value) : 2.0)) RegeneratePreview(); } }
    public int SpiralStepCount { get => _spiralStepCount; set { if (SetProperty(ref _spiralStepCount, Math.Clamp(value, 3, 400))) RegeneratePreview(); } }
    public double SpiralTotalRotationDegrees { get => _spiralTotalRotationDegrees; set { if (SetProperty(ref _spiralTotalRotationDegrees, double.IsFinite(value) ? Math.Max(0.001, Math.Abs(value)) : 360.0)) RegeneratePreview(); } }
    public double SpiralStartAngleDegrees { get => _spiralStartAngleDegrees; set { if (SetProperty(ref _spiralStartAngleDegrees, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public string SelectedSpiralRotationDirection { get => _selectedSpiralRotationDirection; set { if (SetProperty(ref _selectedSpiralRotationDirection, value ?? SpiralRotationDirectionLabels.Anticlockwise)) RegeneratePreview(); } }
    public bool SpiralCreateInnerStringer { get => _spiralCreateInnerStringer; set { if (SetProperty(ref _spiralCreateInnerStringer, value)) RegeneratePreview(); } }
    public bool SpiralCreateOuterStringer { get => _spiralCreateOuterStringer; set { if (SetProperty(ref _spiralCreateOuterStringer, value)) RegeneratePreview(); } }
    public bool SpiralCreateRadialTreadBeams { get => _spiralCreateRadialTreadBeams; set { if (SetProperty(ref _spiralCreateRadialTreadBeams, value)) RegeneratePreview(); } }
    public bool SpiralCreateTreadShellPlates
    {
        get => _spiralCreateTreadShellPlates;
        set
        {
            if (SetProperty(ref _spiralCreateTreadShellPlates, value))
            {
                OnPropertyChanged(nameof(SpiralShellPropertyVisibility));
                RegeneratePreview();
            }
        }
    }
    public bool SpiralCreateCentralColumn
    {
        get => _spiralCreateCentralColumn;
        set
        {
            if (SetProperty(ref _spiralCreateCentralColumn, value))
            {
                OnPropertyChanged(nameof(SpiralCentralColumnSectionVisibility));
                RegeneratePreview();
            }
        }
    }
    public bool SpiralCreateTopLandingBeam
    {
        get => _spiralCreateTopLandingBeam;
        set
        {
            if (SetProperty(ref _spiralCreateTopLandingBeam, value))
            {
                OnPropertyChanged(nameof(SpiralLandingBeamSectionVisibility));
                RegeneratePreview();
            }
        }
    }
    public bool SpiralCreateBottomLandingBeam
    {
        get => _spiralCreateBottomLandingBeam;
        set
        {
            if (SetProperty(ref _spiralCreateBottomLandingBeam, value))
            {
                OnPropertyChanged(nameof(SpiralLandingBeamSectionVisibility));
                RegeneratePreview();
            }
        }
    }
    public string SelectedInnerStringerSection { get => _selectedInnerStringerSection; set { if (SetProperty(ref _selectedInnerStringerSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedOuterStringerSection { get => _selectedOuterStringerSection; set { if (SetProperty(ref _selectedOuterStringerSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedRadialTreadSection { get => _selectedRadialTreadSection; set { if (SetProperty(ref _selectedRadialTreadSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedCentralColumnSection { get => _selectedCentralColumnSection; set { if (SetProperty(ref _selectedCentralColumnSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedLandingBeamSection { get => _selectedLandingBeamSection; set { if (SetProperty(ref _selectedLandingBeamSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedTreadShellProperty { get => _selectedTreadShellProperty; set { if (SetProperty(ref _selectedTreadShellProperty, value ?? "")) RegeneratePreview(); } }
    public double FishStartX { get => _fishStartX; set { if (SetProperty(ref _fishStartX, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double FishStartY { get => _fishStartY; set { if (SetProperty(ref _fishStartY, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double FishStartZ { get => _fishStartZ; set { if (SetProperty(ref _fishStartZ, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double FishSpanLength { get => _fishSpanLength; set { if (SetProperty(ref _fishSpanLength, double.IsFinite(value) ? Math.Max(0.001, value) : 24.0)) RegeneratePreview(); } }
    public int FishPanelCount { get => _fishPanelCount; set { if (SetProperty(ref _fishPanelCount, Math.Clamp(value, 2, 60))) RegeneratePreview(); } }
    public double FishEndDepth { get => _fishEndDepth; set { if (SetProperty(ref _fishEndDepth, double.IsFinite(value) ? Math.Max(0.001, value) : 1.0)) RegeneratePreview(); } }
    public double FishMiddleDepth { get => _fishMiddleDepth; set { if (SetProperty(ref _fishMiddleDepth, double.IsFinite(value) ? Math.Max(0.001, value) : 3.0)) RegeneratePreview(); } }
    public double FishDirectionAngleDegrees { get => _fishDirectionAngleDegrees; set { if (SetProperty(ref _fishDirectionAngleDegrees, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double FishTopChordSlopeDegrees { get => _fishTopChordSlopeDegrees; set { if (SetProperty(ref _fishTopChordSlopeDegrees, double.IsFinite(value) ? Math.Clamp(value, -30.0, 30.0) : 0.0)) RegeneratePreview(); } }
    public string SelectedFishBottomChordShape { get => _selectedFishBottomChordShape; set { if (SetProperty(ref _selectedFishBottomChordShape, value ?? FishBottomChordShapeLabels.Parabolic)) RegeneratePreview(); } }
    public string SelectedFishWebPattern { get => _selectedFishWebPattern; set { if (SetProperty(ref _selectedFishWebPattern, value ?? FishWebPatternLabels.VerticalAlternating)) RegeneratePreview(); } }
    public bool FishUseSameSection
    {
        get => _fishUseSameSection;
        set
        {
            if (SetProperty(ref _fishUseSameSection, value))
            {
                OnPropertyChanged(nameof(FishSameSectionVisibility));
                OnPropertyChanged(nameof(FishSeparateSectionsVisibility));
                RegeneratePreview();
            }
        }
    }
    public string SelectedFishSameSection { get => _selectedFishSameSection; set { if (SetProperty(ref _selectedFishSameSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedFishTopChordSection { get => _selectedFishTopChordSection; set { if (SetProperty(ref _selectedFishTopChordSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedFishBottomChordSection { get => _selectedFishBottomChordSection; set { if (SetProperty(ref _selectedFishBottomChordSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedFishVerticalSection { get => _selectedFishVerticalSection; set { if (SetProperty(ref _selectedFishVerticalSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedFishDiagonalSection { get => _selectedFishDiagonalSection; set { if (SetProperty(ref _selectedFishDiagonalSection, value ?? "")) RegeneratePreview(); } }
    public bool FishReleaseMoments { get => _fishReleaseMoments; set { if (SetProperty(ref _fishReleaseMoments, value)) RegeneratePreview(); } }
    public double VariableStartX { get => _variableStartX; set { if (SetProperty(ref _variableStartX, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double VariableStartY { get => _variableStartY; set { if (SetProperty(ref _variableStartY, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public double VariableStartZ { get => _variableStartZ; set { if (SetProperty(ref _variableStartZ, double.IsFinite(value) ? value : 10.0)) RegeneratePreview(); } }
    public double VariableSpanLength { get => _variableSpanLength; set { if (SetProperty(ref _variableSpanLength, double.IsFinite(value) ? Math.Max(0.001, value) : 24.0)) RegeneratePreview(); } }
    public int VariablePanelCount { get => _variablePanelCount; set { if (SetProperty(ref _variablePanelCount, Math.Clamp(value, 2, 80))) RegeneratePreview(); } }
    public double VariableTrussDepth { get => _variableTrussDepth; set { if (SetProperty(ref _variableTrussDepth, double.IsFinite(value) ? Math.Max(0.001, value) : 2.5)) RegeneratePreview(); } }
    public double VariableEndPanelWidthRatio { get => _variableEndPanelWidthRatio; set { if (SetProperty(ref _variableEndPanelWidthRatio, double.IsFinite(value) ? Math.Max(0.001, value) : 0.5)) RegeneratePreview(); } }
    public double VariableMiddlePanelWidthRatio { get => _variableMiddlePanelWidthRatio; set { if (SetProperty(ref _variableMiddlePanelWidthRatio, double.IsFinite(value) ? Math.Max(0.001, value) : 1.5)) RegeneratePreview(); } }
    public double VariableDirectionAngleDegrees { get => _variableDirectionAngleDegrees; set { if (SetProperty(ref _variableDirectionAngleDegrees, double.IsFinite(value) ? value : 0.0)) RegeneratePreview(); } }
    public string SelectedVariablePanelWidthVariation { get => _selectedVariablePanelWidthVariation; set { if (SetProperty(ref _selectedVariablePanelWidthVariation, value ?? VariablePanelWidthVariationLabels.Parabolic)) RegeneratePreview(); } }
    public string SelectedVariableWebPattern { get => _selectedVariableWebPattern; set { if (SetProperty(ref _selectedVariableWebPattern, value ?? FishWebPatternLabels.VerticalAlternating)) RegeneratePreview(); } }
    public bool VariableUseSameSection
    {
        get => _variableUseSameSection;
        set
        {
            if (SetProperty(ref _variableUseSameSection, value))
            {
                OnPropertyChanged(nameof(VariableSameSectionVisibility));
                OnPropertyChanged(nameof(VariableSeparateSectionsVisibility));
                RegeneratePreview();
            }
        }
    }
    public string SelectedVariableSameSection { get => _selectedVariableSameSection; set { if (SetProperty(ref _selectedVariableSameSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedVariableTopChordSection { get => _selectedVariableTopChordSection; set { if (SetProperty(ref _selectedVariableTopChordSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedVariableBottomChordSection { get => _selectedVariableBottomChordSection; set { if (SetProperty(ref _selectedVariableBottomChordSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedVariableVerticalSection { get => _selectedVariableVerticalSection; set { if (SetProperty(ref _selectedVariableVerticalSection, value ?? "")) RegeneratePreview(); } }
    public string SelectedVariableDiagonalSection { get => _selectedVariableDiagonalSection; set { if (SetProperty(ref _selectedVariableDiagonalSection, value ?? "")) RegeneratePreview(); } }
    public bool VariableReleaseMoments { get => _variableReleaseMoments; set { if (SetProperty(ref _variableReleaseMoments, value)) RegeneratePreview(); } }

    public double RoofSlopePercent
    {
        get => _roofSlopePercent;
        set
        {
            double next = double.IsFinite(value) ? Math.Clamp(value, -50.0, 50.0) : 0.0;
            if (SetProperty(ref _roofSlopePercent, next))
                RegeneratePreview();
        }
    }

    public double BottomChordSlopePercent
    {
        get => _bottomChordSlopePercent;
        set
        {
            double next = double.IsFinite(value) ? Math.Clamp(value, -50.0, 50.0) : 0.0;
            if (SetProperty(ref _bottomChordSlopePercent, next))
                RegeneratePreview();
        }
    }

    public string SelectedTopChordSlopeMode
    {
        get => _selectedTopChordSlopeMode;
        set
        {
            if (SetProperty(ref _selectedTopChordSlopeMode, value ?? ChordSlopeMode.Pitch.ToString()))
                RegeneratePreview();
        }
    }

    public string SelectedBottomChordSlopeMode
    {
        get => _selectedBottomChordSlopeMode;
        set
        {
            if (SetProperty(ref _selectedBottomChordSlopeMode, value ?? ChordSlopeMode.Pitch.ToString()))
                RegeneratePreview();
        }
    }

    public string SelectedTopChordSection
    {
        get => _selectedTopChordSection;
        set
        {
            if (SetProperty(ref _selectedTopChordSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedBottomChordSection
    {
        get => _selectedBottomChordSection;
        set
        {
            if (SetProperty(ref _selectedBottomChordSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedDiagonalSection
    {
        get => _selectedDiagonalSection;
        set
        {
            if (SetProperty(ref _selectedDiagonalSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedVerticalSection
    {
        get => _selectedVerticalSection;
        set
        {
            if (SetProperty(ref _selectedVerticalSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedEndPostSection
    {
        get => _selectedEndPostSection;
        set
        {
            if (SetProperty(ref _selectedEndPostSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedSecondarySection
    {
        get => _selectedSecondarySection;
        set
        {
            if (SetProperty(ref _selectedSecondarySection, value ?? ""))
                RegeneratePreview();
        }
    }

    public bool ApplyTopChordLoad
    {
        get => _applyTopChordLoad;
        set
        {
            if (SetProperty(ref _applyTopChordLoad, value))
                RegeneratePreview();
        }
    }

    public bool ApplyTopChordLoadToPanelNodes
    {
        get => _applyTopChordLoadToPanelNodes;
        set
        {
            if (SetProperty(ref _applyTopChordLoadToPanelNodes, value))
                RegeneratePreview();
        }
    }

    public string SelectedLoadPattern
    {
        get => _selectedLoadPattern;
        set => SetProperty(ref _selectedLoadPattern, value ?? "");
    }

    public string SelectedLoadTarget
    {
        get => _selectedLoadTarget;
        set => SetProperty(ref _selectedLoadTarget, NormalizeLoadTarget(value));
    }

    public string SelectedLoadInputType
    {
        get => _selectedLoadInputType;
        set
        {
            if (SetProperty(ref _selectedLoadInputType, NormalizeLoadInputType(value)))
            {
                OnPropertyChanged(nameof(LoadPanelWidthVisibility));
                OnPropertyChanged(nameof(SelectedLoadMagnitudeLabel));
            }
        }
    }

    public string SelectedLoadApplicationMode
    {
        get => _selectedLoadApplicationMode;
        set => SetProperty(ref _selectedLoadApplicationMode, NormalizeLoadApplicationMode(value));
    }

    public string SelectedLoadMagnitudeLabel =>
        IsAreaLoadInput ? "Load kPa" : "Load kN/m";

    public Visibility LoadPanelWidthVisibility =>
        IsAreaLoadInput ? Visibility.Visible : Visibility.Collapsed;

    public double LoadMagnitude
    {
        get => _loadMagnitude;
        set => SetProperty(ref _loadMagnitude, double.IsFinite(value) ? Math.Abs(value) : 0.0);
    }

    public double LoadPanelWidth
    {
        get => _loadPanelWidth;
        set => SetProperty(ref _loadPanelWidth, double.IsFinite(value) ? Math.Max(0.001, value) : 1.0);
    }

    public double TopChordGravityLoadKnPerM
    {
        get => _topChordGravityLoadKnPerM;
        set
        {
            double next = double.IsFinite(value) ? value : 0.0;
            if (SetProperty(ref _topChordGravityLoadKnPerM, next))
                RegeneratePreview();
        }
    }

    public bool ApplyBottomChordLoad
    {
        get => _applyBottomChordLoad;
        set
        {
            if (SetProperty(ref _applyBottomChordLoad, value))
                RegeneratePreview();
        }
    }

    public bool ApplyBottomChordLoadToPanelNodes
    {
        get => _applyBottomChordLoadToPanelNodes;
        set
        {
            if (SetProperty(ref _applyBottomChordLoadToPanelNodes, value))
                RegeneratePreview();
        }
    }

    public double BottomChordLoadMagnitude
    {
        get => _bottomChordLoadMagnitude;
        set
        {
            double next = double.IsFinite(value) ? value : 0.0;
            if (SetProperty(ref _bottomChordLoadMagnitude, next))
                RegeneratePreview();
        }
    }

    public string SelectedEtabsExportMode
    {
        get => _selectedEtabsExportMode;
        set => SetProperty(ref _selectedEtabsExportMode, value ?? EtabsExportModeLabels.EraseAndRedraw);
    }

    public string SelectedSupportNodeMode
    {
        get => _selectedSupportNodeMode;
        set
        {
            string next = NormalizeSupportNodeMode(value);
            if (SetProperty(ref _selectedSupportNodeMode, next))
                RegeneratePreview();
        }
    }

    public string SelectedSupportRestraintType
    {
        get => _selectedSupportRestraintType;
        set
        {
            string next = NormalizeSupportRestraintType(value);
            if (SetProperty(ref _selectedSupportRestraintType, next))
                RegeneratePreview();
        }
    }

    public double AddAsNewOffsetX
    {
        get => _addAsNewOffsetX;
        set => SetProperty(ref _addAsNewOffsetX, double.IsFinite(value) ? value : 0.0);
    }

    public double AddAsNewOffsetY
    {
        get => _addAsNewOffsetY;
        set => SetProperty(ref _addAsNewOffsetY, double.IsFinite(value) ? value : 0.0);
    }

    public double AddAsNewOffsetZ
    {
        get => _addAsNewOffsetZ;
        set => SetProperty(ref _addAsNewOffsetZ, double.IsFinite(value) ? value : 0.0);
    }

    public bool SectionEditorUseSelectedFrames
    {
        get => _sectionEditorUseSelectedFrames;
        set => SetProperty(ref _sectionEditorUseSelectedFrames, value);
    }

    public string SectionEditorGroupName
    {
        get => _sectionEditorGroupName;
        set => SetProperty(ref _sectionEditorGroupName, value ?? "");
    }

    public bool SectionEditorAssignToNewGroup
    {
        get => _sectionEditorAssignToNewGroup;
        set
        {
            if (SetProperty(ref _sectionEditorAssignToNewGroup, value))
                OnPropertyChanged(nameof(SectionEditorNewGroupAssignmentVisibility));
        }
    }

    public string SectionEditorNewGroupName
    {
        get => _sectionEditorNewGroupName;
        set => SetProperty(ref _sectionEditorNewGroupName, value ?? "");
    }

    public Visibility SectionEditorNewGroupAssignmentVisibility =>
        SectionEditorAssignToNewGroup
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string SectionEditorBulkSection
    {
        get => _sectionEditorBulkSection;
        set => SetProperty(ref _sectionEditorBulkSection, value ?? "");
    }

    public string SectionEditorStatus
    {
        get => _sectionEditorStatus;
        set => SetProperty(ref _sectionEditorStatus, value);
    }

    public string SectionEditorLoadPattern
    {
        get => _sectionEditorLoadPattern;
        set => SetProperty(ref _sectionEditorLoadPattern, value ?? "");
    }

    public string SectionEditorLoadInputType
    {
        get => _sectionEditorLoadInputType;
        set
        {
            if (SetProperty(ref _sectionEditorLoadInputType, NormalizeLoadInputType(value)))
            {
                OnPropertyChanged(nameof(SectionEditorLoadPanelWidthVisibility));
                OnPropertyChanged(nameof(SectionEditorLoadMagnitudeLabel));
                OnPropertyChanged(nameof(SectionEditorEquivalentLineLoadDisplay));
            }
        }
    }

    public string SectionEditorLoadMagnitudeLabel =>
        IsSectionEditorAreaLoadInput ? "Load kPa" : "Load kN/m";

    public Visibility SectionEditorLoadPanelWidthVisibility =>
        IsSectionEditorAreaLoadInput ? Visibility.Visible : Visibility.Collapsed;

    public double SectionEditorLoadMagnitude
    {
        get => _sectionEditorLoadMagnitude;
        set
        {
            if (SetProperty(ref _sectionEditorLoadMagnitude, double.IsFinite(value) ? Math.Abs(value) : 0.0))
                OnPropertyChanged(nameof(SectionEditorEquivalentLineLoadDisplay));
        }
    }

    public double SectionEditorLoadPanelWidth
    {
        get => _sectionEditorLoadPanelWidth;
        set
        {
            if (SetProperty(ref _sectionEditorLoadPanelWidth, double.IsFinite(value) ? Math.Max(0.001, value) : 1.0))
                OnPropertyChanged(nameof(SectionEditorEquivalentLineLoadDisplay));
        }
    }

    public bool SectionEditorReplaceSelectedPatternLoads
    {
        get => _sectionEditorReplaceSelectedPatternLoads;
        set => SetProperty(ref _sectionEditorReplaceSelectedPatternLoads, value);
    }

    public string SectionEditorEquivalentLineLoadDisplay =>
        $"Equivalent line load: {SectionEditorEquivalentLineLoadKnPerM:0.###} kN/m downward Global Z";

    public string SelectedSectionPreviewMode
    {
        get => _selectedSectionPreviewMode;
        set
        {
            string next = string.Equals(value, SectionPreviewModeLabels.TwoD, StringComparison.OrdinalIgnoreCase)
                ? SectionPreviewModeLabels.TwoD
                : SectionPreviewModeLabels.ThreeD;

            if (SetProperty(ref _selectedSectionPreviewMode, next))
            {
                OnPropertyChanged(nameof(SectionPreview3DVisibility));
                OnPropertyChanged(nameof(SectionPreview2DVisibility));
            }
        }
    }

    public Visibility SectionPreview3DVisibility =>
        string.Equals(SelectedSectionPreviewMode, SectionPreviewModeLabels.ThreeD, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility SectionPreview2DVisibility =>
        string.Equals(SelectedSectionPreviewMode, SectionPreviewModeLabels.TwoD, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public EtabsFrameSectionRow? SelectedSectionEditorFrame
    {
        get => _selectedSectionEditorFrame;
        set => SetProperty(ref _selectedSectionEditorFrame, value);
    }

    public string SelectedInsertionSummary
    {
        get => _selectedInsertionSummary;
        set => SetProperty(ref _selectedInsertionSummary, value);
    }

    public ParametricTrussModel CurrentModel
    {
        get => _currentModel;
        private set
        {
            if (SetProperty(ref _currentModel, value))
            {
                OnPropertyChanged(nameof(SpanDisplay));
                OnPropertyChanged(nameof(GeneratedCountDisplay));
            }
        }
    }

    public string SpanDisplay => $"{CurrentModel.Span:0.###} m";
    public string GeneratedCountDisplay => $"{CurrentModel.Nodes.Count} nodes / {CurrentModel.Members.Count} members / {CurrentModel.Shells.Count} shells / {CurrentModel.Loads.Count} loads";

    private void RefreshEtabsInstances()
    {
        EtabsInstanceListResult result = _etabsService.ListEtabsInstances();
        string previousId = SelectedEtabsInstanceId;
        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, previousId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();

        ConnectionStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void ReadEtabsData()
    {
        EtabsParametricModelDataResult result = _etabsService.ListParametricModelData(new EtabsParametricModelDataRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId
        });

        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, result.SelectedInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();

        ReplaceCollection(FrameSections, result.FrameSections);
        ReplaceCollection(ShellProperties, result.ShellProperties);
        ReplaceCollection(LoadPatterns, result.LoadPatterns);
        ReplaceCollection(LoadCombinations, result.LoadCombinations);
        ReplaceCollection(Stories, result.Stories);
        ReplaceCollection(Groups, result.Groups);

        _etabsDataLoaded = !result.IsError && (FrameSections.Count > 0 || ShellProperties.Count > 0);
        ApplyDefaultNameForSelectedType();
        PickDefaultSections();
        PickDefaultSectionEditorGroup();
        PickDefaultLoadSelections();
        ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        RegeneratePreview();
    }

    private void ReadSelectedPoints()
    {
        EtabsSelectedInsertionPointsResult result = _etabsService.ReadSelectedInsertionPoints(new EtabsSelectedInsertionPointsRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId
        });

        ReplaceCollection(SelectedInsertionPoints, result.Points);

        if (!result.IsError && result.Points.Count == 2)
        {
            UseSelectedInsertionPoints = true;
            ManualSpan = Distance(result.Points[0], result.Points[1]);
            SelectedInsertionSummary = $"{result.Points[0].Name} to {result.Points[1].Name}";
        }

        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        RegeneratePreview();
    }

    private ParametricValidationResult ValidateCurrentModel(bool requireEtabsConnection)
    {
        ParametricValidationResult validation = _validator.Validate(
            CurrentModel,
            SelectedEtabsInstanceId,
            _etabsDataLoaded,
            FrameSections,
            ShellProperties,
            LoadPatterns,
            requireEtabsConnection);

        AddCurrentInputValidationIssues(validation);
        ReplaceCollection(Messages, validation.Issues);
        return validation;
    }

    private void AddCurrentInputValidationIssues(ParametricValidationResult validation)
    {
        int issueCount = validation.Issues.Count;
        if (CurrentTrussType == TrussType.SpiralStaircase)
        {
            if (SpiralOuterRadius <= SpiralInnerRadius)
                AddValidationIssue(validation, ValidationSeverity.Critical, "Spiral outer radius must be greater than inner radius.");
            if (SpiralTotalRotationDegrees <= 0)
                AddValidationIssue(validation, ValidationSeverity.Critical, "Spiral total rotation angle must be greater than zero.");
            else if (SpiralTotalRotationDegrees < 90)
                AddValidationIssue(validation, ValidationSeverity.Warning, "Spiral total rotation angle is small; verify the intended stair geometry.");
        }
        else if (CurrentTrussType == TrussType.FishBellyTruss)
        {
            if (FishMiddleDepth <= FishEndDepth)
                AddValidationIssue(validation, ValidationSeverity.Critical, "Fish-belly middle depth must be greater than end depth.");
            if (FishEndDepth < 0.1)
                AddValidationIssue(validation, ValidationSeverity.Warning, "Fish-belly end depth is shallow; verify member clearance and connection assumptions.");
        }
        else if (CurrentTrussType == TrussType.VariablePanelWidthTruss)
        {
            if (VariableTrussDepth <= 0)
                AddValidationIssue(validation, ValidationSeverity.Critical, "Variable panel truss depth must be greater than zero.");
            if (VariableEndPanelWidthRatio <= 0 || VariableMiddlePanelWidthRatio <= 0)
                AddValidationIssue(validation, ValidationSeverity.Critical, "Variable panel width ratios must be greater than zero.");
            if (VariableMiddlePanelWidthRatio <= VariableEndPanelWidthRatio)
                AddValidationIssue(validation, ValidationSeverity.Critical, "Variable middle panel width ratio must be greater than end panel width ratio.");
        }

        if (validation.Issues.Count > issueCount)
        {
            validation.Issues.RemoveAll(issue =>
                issue.Severity == ValidationSeverity.Info &&
                string.Equals(issue.Message, "Validation passed.", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AddValidationIssue(ParametricValidationResult validation, ValidationSeverity severity, string message)
    {
        validation.Issues.Add(new ValidationIssue
        {
            Severity = severity,
            Message = message
        });
    }

    private void SendToEtabs()
    {
        ParametricValidationResult validation = ValidateCurrentModel(true);
        if (validation.HasCriticalIssues)
        {
            ConnectionStatus = "Validation failed";
            return;
        }

        EtabsTrussDrawResult result = _etabsService.DrawOrUpdateTruss(new EtabsTrussDrawRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Model = CurrentModel,
            ReplaceExistingGroup = IsEraseAndRedrawMode,
            AddAsNew = IsAddAsNewMode,
            OffsetX = IsAddAsNewMode ? AddAsNewOffsetX : 0,
            OffsetY = IsAddAsNewMode ? AddAsNewOffsetY : 0,
            OffsetZ = IsAddAsNewMode ? AddAsNewOffsetZ : 0
        });

        ConnectionStatus = result.IsError ? "Draw failed" : "Model sent to ETABS";
        var issues = BuildIssues(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        ReplaceCollection(Messages, issues);
    }

    private void AddTrussLoad()
    {
        string loadPattern = (SelectedLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
        {
            ShowMessages([], ValidationSeverity.Critical, "Select a load pattern before adding a truss load.");
            return;
        }

        if (!double.IsFinite(LoadMagnitude) || Math.Abs(LoadMagnitude) < 0.000001)
        {
            ShowMessages([], ValidationSeverity.Critical, "Enter a non-zero load magnitude before adding a truss load.");
            return;
        }

        ParametricTrussLoadInputType inputType = ToLoadInputType(SelectedLoadInputType);
        if (inputType == ParametricTrussLoadInputType.AreaLoadKpa &&
            (!double.IsFinite(LoadPanelWidth) || LoadPanelWidth <= 0.000001))
        {
            ShowMessages([], ValidationSeverity.Critical, "Enter a panel width greater than zero before adding a kPa load.");
            return;
        }

        _loadDefinitionCounter++;
        TrussLoads.Add(new ParametricTrussLoadDefinition
        {
            Id = $"LOAD_{_loadDefinitionCounter:000}",
            LoadPattern = loadPattern,
            Target = ToLoadTarget(SelectedLoadTarget),
            InputType = inputType,
            ApplicationMode = ToLoadApplicationMode(SelectedLoadApplicationMode),
            Magnitude = Math.Abs(LoadMagnitude),
            PanelWidth = inputType == ParametricTrussLoadInputType.AreaLoadKpa ? LoadPanelWidth : 1.0
        });
    }

    private void RemoveTrussLoad(object? parameter)
    {
        if (parameter is ParametricTrussLoadDefinition load)
            TrussLoads.Remove(load);
    }

    private void ClearTrussLoads()
    {
        TrussLoads.Clear();
    }

    private void ImportEtabsFramesForSectionEditing()
    {
        EtabsFrameSectionImportResult result = _etabsService.ImportFrameSections(new EtabsFrameSectionImportRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            UseSelectedFrames = SectionEditorUseSelectedFrames
        });

        ApplySectionEditorImportResult(result);
    }

    private void ImportEtabsGroupFramesForSectionEditing()
    {
        string groupName = (SectionEditorGroupName ?? "").Trim();
        if (groupName.Length == 0)
        {
            ShowSectionEditorMessages([], ValidationSeverity.Critical, "Select or enter an ETABS group before importing group frames.");
            SectionEditorStatus = "Group import failed";
            return;
        }

        EtabsFrameSectionImportResult result = _etabsService.ImportFrameSections(new EtabsFrameSectionImportRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            UseSelectedFrames = false,
            GroupName = groupName
        });

        ApplySectionEditorImportResult(result);
    }

    private void ApplySectionEditorImportResult(EtabsFrameSectionImportResult result)
    {
        if (!result.IsError && result.FrameSections.Count > 0)
        {
            ReplaceCollection(FrameSections, result.FrameSections);
            PickDefaultSections();
        }

        if (!result.IsError && result.Groups.Count > 0)
        {
            ReplaceCollection(Groups, result.Groups);
            PickDefaultSectionEditorGroup();
        }

        ReplaceCollection(SectionEditorFrames, result.Frames);
        SelectedSectionEditorFrame = SectionEditorFrames.FirstOrDefault();
        SectionEditorStatus = result.IsError ? "Import failed" : $"{result.Frames.Count} frame(s) imported";
        ShowSectionEditorMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void AssignSelectedEtabsFramesToGroup()
    {
        string groupName = GetSectionEditorAssignmentGroupName();
        if (groupName.Length == 0)
        {
            string message = SectionEditorAssignToNewGroup
                ? "Enter a new ETABS group name before assigning selected frames."
                : "Select an existing ETABS group before assigning selected frames.";
            ShowSectionEditorMessages([], ValidationSeverity.Critical, message);
            SectionEditorStatus = "Group assignment failed";
            return;
        }

        EtabsFrameGroupAssignResult result = _etabsService.AssignSelectedFramesToGroup(new EtabsFrameGroupAssignRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            GroupName = groupName
        });

        if (!string.IsNullOrWhiteSpace(result.GroupName))
        {
            SectionEditorGroupName = result.GroupName;
            if (SectionEditorAssignToNewGroup)
                SectionEditorNewGroupName = result.GroupName;
        }

        if (!result.IsError && result.Groups.Count > 0)
            ReplaceCollection(Groups, result.Groups);

        if (!result.IsError && result.AssignedFrameNames.Count > 0)
        {
            HashSet<string> assignedFrameNames = result.AssignedFrameNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (EtabsFrameSectionRow row in SectionEditorFrames.Where(row => assignedFrameNames.Contains(row.FrameName)))
                row.GroupName = result.GroupName;
        }

        SectionEditorStatus = result.IsError ? "Group assignment failed" : $"{result.AssignedCount} selected frame(s) assigned to group";
        ShowSectionEditorMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private string GetSectionEditorAssignmentGroupName()
    {
        return SectionEditorAssignToNewGroup
            ? (SectionEditorNewGroupName ?? "").Trim()
            : (SectionEditorGroupName ?? "").Trim();
    }

    private void SetSectionRowsChecked(bool isChecked)
    {
        foreach (EtabsFrameSectionRow row in SectionEditorFrames)
            row.Include = isChecked;
    }

    private void ApplyBulkSectionToCheckedRows()
    {
        string section = (SectionEditorBulkSection ?? "").Trim();
        if (section.Length == 0)
        {
            ShowSectionEditorMessages([], ValidationSeverity.Critical, "Select a frame section before applying it to checked rows.");
            return;
        }

        int count = 0;
        foreach (EtabsFrameSectionRow row in SectionEditorFrames.Where(row => row.Include))
        {
            row.NewSection = section;
            count++;
        }

        ShowSectionEditorMessages([], ValidationSeverity.Info, $"Applied section '{section}' to {count} checked frame row(s).");
    }

    private void UpdateEtabsFrameSections()
    {
        EtabsFrameSectionUpdateResult result = _etabsService.UpdateFrameSections(new EtabsFrameSectionUpdateRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Frames = SectionEditorFrames.ToList()
        });

        ApplySectionUpdateToImportedRows(result, "");
        SectionEditorStatus = result.IsError ? "Update failed" : $"{result.UpdatedCount} frame section(s) updated";
        ShowSectionEditorMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void UpdateEtabsFrameLoads()
    {
        string loadPattern = (SectionEditorLoadPattern ?? "").Trim();
        if (loadPattern.Length == 0)
        {
            ShowSectionEditorMessages([], ValidationSeverity.Critical, "Select a load pattern before updating existing frame loads.");
            SectionEditorStatus = "Load update failed";
            return;
        }

        double equivalentLineLoad = SectionEditorEquivalentLineLoadKnPerM;
        if (!double.IsFinite(equivalentLineLoad) || Math.Abs(equivalentLineLoad) < 0.000001)
        {
            ShowSectionEditorMessages([], ValidationSeverity.Critical, "Enter a non-zero load before updating existing frame loads.");
            SectionEditorStatus = "Load update failed";
            return;
        }

        EtabsFrameLoadUpdateResult result = _etabsService.UpdateFrameDistributedLoads(new EtabsFrameLoadUpdateRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Frames = SectionEditorFrames.ToList(),
            LoadPattern = loadPattern,
            LineLoadKnPerM = equivalentLineLoad,
            ReplaceSelectedPatternLoads = SectionEditorReplaceSelectedPatternLoads
        });

        SectionEditorStatus = result.IsError ? "Load update failed" : $"{result.UpdatedCount} frame load(s) updated";
        ShowSectionEditorMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void UpdateEtabsGroupFrameSections()
    {
        string groupName = (SectionEditorGroupName ?? "").Trim();
        if (groupName.Length == 0)
        {
            ShowSectionEditorMessages([], ValidationSeverity.Critical, "Select an ETABS group before applying a section to the group.");
            SectionEditorStatus = "Group section update failed";
            return;
        }

        string sectionName = (SectionEditorBulkSection ?? "").Trim();
        if (sectionName.Length == 0)
        {
            ShowSectionEditorMessages([], ValidationSeverity.Critical, "Select a frame section before applying it to the group.");
            SectionEditorStatus = "Group section update failed";
            return;
        }

        EtabsFrameSectionUpdateResult result = _etabsService.UpdateFrameGroupSection(new EtabsFrameGroupSectionUpdateRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            GroupName = groupName,
            SectionName = sectionName
        });

        ApplySectionUpdateToImportedRows(result, groupName);
        if (!result.IsError && result.FrameSections.Count > 0)
        {
            ReplaceCollection(FrameSections, result.FrameSections);
            PickDefaultSections();
        }

        if (!result.IsError && result.Groups.Count > 0)
            ReplaceCollection(Groups, result.Groups);

        SectionEditorStatus = result.IsError ? "Group section update failed" : $"{result.UpdatedCount} group frame section(s) updated";
        ShowSectionEditorMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void ApplySectionUpdateToImportedRows(EtabsFrameSectionUpdateResult result, string groupName)
    {
        if (result.IsError || result.UpdatedFrameNames.Count == 0)
            return;

        HashSet<string> updatedFrameNames = result.UpdatedFrameNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (EtabsFrameSectionRow row in SectionEditorFrames.Where(row => updatedFrameNames.Contains(row.FrameName)))
        {
            row.CurrentSection = row.NewSection;
            if (!string.IsNullOrWhiteSpace(groupName))
                row.GroupName = groupName;
        }
    }

    private void RegeneratePreview()
    {
        ParametricTrussOptions options = BuildGeneratorOptions();
        ParametricTrussModel model = _generator.Generate(options);
        if (UseSelectedInsertionPoints && SelectedInsertionPoints.Count != 2)
            model.Warnings.Add("Selected point insertion is enabled, but two ETABS insertion points have not been loaded.");

        CurrentModel = model;
        ValidateCurrentModel(false);
    }

    private ParametricTrussOptions BuildGeneratorOptions()
    {
        TrussType trussType = CurrentTrussType;
        ChordSlopeMode topSlopeMode = Enum.TryParse(SelectedTopChordSlopeMode, out ChordSlopeMode parsedTopSlopeMode)
            ? parsedTopSlopeMode
            : ChordSlopeMode.Pitch;
        ChordSlopeMode bottomSlopeMode = Enum.TryParse(SelectedBottomChordSlopeMode, out ChordSlopeMode parsedBottomSlopeMode)
            ? parsedBottomSlopeMode
            : ChordSlopeMode.Pitch;

        ModelPoint3d start;
        ModelPoint3d end;
        if (UseSelectedInsertionPoints && SelectedInsertionPoints.Count == 2)
        {
            start = SelectedInsertionPoints[0].ToModelPoint();
            end = SelectedInsertionPoints[1].ToModelPoint();
        }
        else
        {
            start = new ModelPoint3d();
            end = new ModelPoint3d { X = ManualSpan };
        }

        return new ParametricTrussOptions
        {
            TrussId = TrussId,
            GroupName = GroupName,
            TrussType = trussType,
            StartPoint = start,
            EndPoint = end,
            Height = Height,
            PanelCount = PanelCount,
            RoofSlopePercent = RoofSlopePercent,
            BottomChordSlopePercent = BottomChordSlopePercent,
            TopChordSlopeMode = topSlopeMode,
            BottomChordSlopeMode = bottomSlopeMode,
            SupportNodeMode = ToSupportNodeMode(SelectedSupportNodeMode),
            SupportRestraintType = ToSupportRestraintType(SelectedSupportRestraintType),
            SectionAssignments = BuildSectionAssignments(),
            ApplyTopChordLoad = false,
            ApplyBottomChordLoad = false,
            LoadDefinitions = trussType == TrussType.SpiralStaircase ||
                trussType == TrussType.FishBellyTruss ||
                trussType == TrussType.VariablePanelWidthTruss
                ? []
                : TrussLoads.Select(load => load.Clone()).ToList(),
            SpiralCentreX = SpiralCentreX,
            SpiralCentreY = SpiralCentreY,
            SpiralBaseZ = SpiralBaseZ,
            SpiralTotalHeight = SpiralTotalHeight,
            SpiralInnerRadius = SpiralInnerRadius,
            SpiralOuterRadius = SpiralOuterRadius,
            SpiralStepCount = SpiralStepCount,
            SpiralTotalRotationDegrees = SpiralTotalRotationDegrees,
            SpiralStartAngleDegrees = SpiralStartAngleDegrees,
            SpiralRotationDirection = ToSpiralRotationDirection(SelectedSpiralRotationDirection),
            SpiralCreateInnerStringer = SpiralCreateInnerStringer,
            SpiralCreateOuterStringer = SpiralCreateOuterStringer,
            SpiralCreateRadialTreadBeams = SpiralCreateRadialTreadBeams,
            SpiralCreateTreadShellPlates = SpiralCreateTreadShellPlates,
            SpiralCreateCentralColumn = SpiralCreateCentralColumn,
            SpiralCreateTopLandingBeam = SpiralCreateTopLandingBeam,
            SpiralCreateBottomLandingBeam = SpiralCreateBottomLandingBeam,
            SpiralTreadShellProperty = SelectedTreadShellProperty,
            FishStartX = FishStartX,
            FishStartY = FishStartY,
            FishStartZ = FishStartZ,
            FishSpanLength = FishSpanLength,
            FishPanelCount = FishPanelCount,
            FishEndDepth = FishEndDepth,
            FishMiddleDepth = FishMiddleDepth,
            FishDirectionAngleDegrees = FishDirectionAngleDegrees,
            FishTopChordSlopeDegrees = FishTopChordSlopeDegrees,
            FishBottomChordShape = ToFishBottomChordShape(SelectedFishBottomChordShape),
            FishWebPattern = ToFishWebPattern(SelectedFishWebPattern),
            FishReleaseMoments = FishReleaseMoments,
            VariablePanelStartX = VariableStartX,
            VariablePanelStartY = VariableStartY,
            VariablePanelStartZ = VariableStartZ,
            VariablePanelSpanLength = VariableSpanLength,
            VariablePanelCount = VariablePanelCount,
            VariablePanelTrussDepth = VariableTrussDepth,
            VariablePanelEndWidthRatio = VariableEndPanelWidthRatio,
            VariablePanelMiddleWidthRatio = VariableMiddlePanelWidthRatio,
            VariablePanelDirectionAngleDegrees = VariableDirectionAngleDegrees,
            VariablePanelWidthVariation = ToVariablePanelWidthVariation(SelectedVariablePanelWidthVariation),
            VariablePanelWebPattern = ToFishWebPattern(SelectedVariableWebPattern),
            VariablePanelReleaseMoments = VariableReleaseMoments
        };
    }

    private Dictionary<string, string> BuildSectionAssignments()
    {
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (CurrentTrussType == TrussType.SpiralStaircase)
        {
            assignments[ParametricMemberGroups.InnerStringer] = SelectedInnerStringerSection;
            assignments[ParametricMemberGroups.OuterStringer] = SelectedOuterStringerSection;
            assignments[ParametricMemberGroups.RadialTread] = SelectedRadialTreadSection;
            assignments[ParametricMemberGroups.CentralColumn] = SelectedCentralColumnSection;
            assignments[ParametricMemberGroups.LandingBeam] = SelectedLandingBeamSection;
            return assignments;
        }

        if (CurrentTrussType == TrussType.FishBellyTruss)
        {
            if (FishUseSameSection)
            {
                assignments[ParametricMemberGroups.TopChord] = SelectedFishSameSection;
                assignments[ParametricMemberGroups.BottomChord] = SelectedFishSameSection;
                assignments[ParametricMemberGroups.Vertical] = SelectedFishSameSection;
                assignments[ParametricMemberGroups.Diagonal] = SelectedFishSameSection;
            }
            else
            {
                assignments[ParametricMemberGroups.TopChord] = SelectedFishTopChordSection;
                assignments[ParametricMemberGroups.BottomChord] = SelectedFishBottomChordSection;
                assignments[ParametricMemberGroups.Vertical] = SelectedFishVerticalSection;
                assignments[ParametricMemberGroups.Diagonal] = SelectedFishDiagonalSection;
            }

            return assignments;
        }

        if (CurrentTrussType == TrussType.VariablePanelWidthTruss)
        {
            if (VariableUseSameSection)
            {
                assignments[ParametricMemberGroups.TopChord] = SelectedVariableSameSection;
                assignments[ParametricMemberGroups.BottomChord] = SelectedVariableSameSection;
                assignments[ParametricMemberGroups.Vertical] = SelectedVariableSameSection;
                assignments[ParametricMemberGroups.Diagonal] = SelectedVariableSameSection;
            }
            else
            {
                assignments[ParametricMemberGroups.TopChord] = SelectedVariableTopChordSection;
                assignments[ParametricMemberGroups.BottomChord] = SelectedVariableBottomChordSection;
                assignments[ParametricMemberGroups.Vertical] = SelectedVariableVerticalSection;
                assignments[ParametricMemberGroups.Diagonal] = SelectedVariableDiagonalSection;
            }

            return assignments;
        }

        assignments[ParametricMemberGroups.TopChord] = SelectedTopChordSection;
        assignments[ParametricMemberGroups.BottomChord] = SelectedBottomChordSection;
        assignments[ParametricMemberGroups.Diagonal] = SelectedDiagonalSection;
        assignments[ParametricMemberGroups.Vertical] = SelectedVerticalSection;
        assignments[ParametricMemberGroups.EndPost] = SelectedEndPostSection;
        assignments[ParametricMemberGroups.Secondary] = SelectedSecondarySection;
        return assignments;
    }

    private void PickDefaultSections()
    {
        string firstSection = FrameSections.FirstOrDefault() ?? "";
        if (SelectedTopChordSection.Length == 0 || !FrameSections.Contains(SelectedTopChordSection))
            SelectedTopChordSection = firstSection;
        if (SelectedBottomChordSection.Length == 0 || !FrameSections.Contains(SelectedBottomChordSection))
            SelectedBottomChordSection = firstSection;
        if (SelectedDiagonalSection.Length == 0 || !FrameSections.Contains(SelectedDiagonalSection))
            SelectedDiagonalSection = firstSection;
        if (SelectedVerticalSection.Length == 0 || !FrameSections.Contains(SelectedVerticalSection))
            SelectedVerticalSection = firstSection;
        if (SelectedEndPostSection.Length == 0 || !FrameSections.Contains(SelectedEndPostSection))
            SelectedEndPostSection = firstSection;
        if (SelectedSecondarySection.Length == 0 || !FrameSections.Contains(SelectedSecondarySection))
            SelectedSecondarySection = firstSection;
        if (SelectedInnerStringerSection.Length == 0 || !FrameSections.Contains(SelectedInnerStringerSection))
            SelectedInnerStringerSection = firstSection;
        if (SelectedOuterStringerSection.Length == 0 || !FrameSections.Contains(SelectedOuterStringerSection))
            SelectedOuterStringerSection = firstSection;
        if (SelectedRadialTreadSection.Length == 0 || !FrameSections.Contains(SelectedRadialTreadSection))
            SelectedRadialTreadSection = firstSection;
        if (SelectedCentralColumnSection.Length == 0 || !FrameSections.Contains(SelectedCentralColumnSection))
            SelectedCentralColumnSection = firstSection;
        if (SelectedLandingBeamSection.Length == 0 || !FrameSections.Contains(SelectedLandingBeamSection))
            SelectedLandingBeamSection = firstSection;
        if (SelectedFishSameSection.Length == 0 || !FrameSections.Contains(SelectedFishSameSection))
            SelectedFishSameSection = firstSection;
        if (SelectedFishTopChordSection.Length == 0 || !FrameSections.Contains(SelectedFishTopChordSection))
            SelectedFishTopChordSection = firstSection;
        if (SelectedFishBottomChordSection.Length == 0 || !FrameSections.Contains(SelectedFishBottomChordSection))
            SelectedFishBottomChordSection = firstSection;
        if (SelectedFishVerticalSection.Length == 0 || !FrameSections.Contains(SelectedFishVerticalSection))
            SelectedFishVerticalSection = firstSection;
        if (SelectedFishDiagonalSection.Length == 0 || !FrameSections.Contains(SelectedFishDiagonalSection))
            SelectedFishDiagonalSection = firstSection;
        if (SelectedVariableSameSection.Length == 0 || !FrameSections.Contains(SelectedVariableSameSection))
            SelectedVariableSameSection = firstSection;
        if (SelectedVariableTopChordSection.Length == 0 || !FrameSections.Contains(SelectedVariableTopChordSection))
            SelectedVariableTopChordSection = firstSection;
        if (SelectedVariableBottomChordSection.Length == 0 || !FrameSections.Contains(SelectedVariableBottomChordSection))
            SelectedVariableBottomChordSection = firstSection;
        if (SelectedVariableVerticalSection.Length == 0 || !FrameSections.Contains(SelectedVariableVerticalSection))
            SelectedVariableVerticalSection = firstSection;
        if (SelectedVariableDiagonalSection.Length == 0 || !FrameSections.Contains(SelectedVariableDiagonalSection))
            SelectedVariableDiagonalSection = firstSection;
        if (SectionEditorBulkSection.Length == 0 || !FrameSections.Contains(SectionEditorBulkSection))
            SectionEditorBulkSection = firstSection;

        string firstShellProperty = ShellProperties.FirstOrDefault() ?? "";
        if (SelectedTreadShellProperty.Length == 0 || !ShellProperties.Contains(SelectedTreadShellProperty))
            SelectedTreadShellProperty = firstShellProperty;
    }

    private void PickDefaultSectionEditorGroup()
    {
        if (SectionEditorGroupName.Length > 0 &&
            Groups.Any(group => string.Equals(group, SectionEditorGroupName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SectionEditorGroupName = Groups.FirstOrDefault(group => !string.Equals(group, "All", StringComparison.OrdinalIgnoreCase)) ??
            Groups.FirstOrDefault() ??
            "";
    }

    private void PickDefaultLoadSelections()
    {
        if (SelectedLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedLoadPattern))
            SelectedLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
        if (SectionEditorLoadPattern.Length == 0 || !LoadPatterns.Contains(SectionEditorLoadPattern))
            SectionEditorLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
    }

    private static List<ValidationIssue> BuildIssues(IEnumerable<string> warnings, ValidationSeverity summarySeverity, string summary)
    {
        var issues = new List<ValidationIssue>();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            issues.Add(new ValidationIssue
            {
                Severity = summarySeverity,
                Message = summary
            });
        }

        issues.AddRange(warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = warning
            }));

        return issues;
    }

    private void ShowMessages(IEnumerable<string> warnings, ValidationSeverity summarySeverity, string summary)
    {
        ReplaceCollection(Messages, BuildIssues(warnings, summarySeverity, summary));
    }

    private void ShowSectionEditorMessages(IEnumerable<string> warnings, ValidationSeverity summarySeverity, string summary)
    {
        ReplaceCollection(SectionEditorMessages, BuildIssues(warnings, summarySeverity, summary));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }

    private static double Distance(EtabsPointInfo first, EtabsPointInfo second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        double dz = second.Z - first.Z;
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return double.IsFinite(length) ? length : 0;
    }

    private bool IsAreaLoadInput =>
        string.Equals(SelectedLoadInputType, AreaLoadInputLabel, StringComparison.OrdinalIgnoreCase);

    private bool IsSectionEditorAreaLoadInput =>
        string.Equals(SectionEditorLoadInputType, AreaLoadInputLabel, StringComparison.OrdinalIgnoreCase);

    private double SectionEditorEquivalentLineLoadKnPerM =>
        IsSectionEditorAreaLoadInput
            ? SectionEditorLoadMagnitude * SectionEditorLoadPanelWidth
            : SectionEditorLoadMagnitude;

    private static double RoundToGeometryStep(double value)
    {
        double rounded = Math.Round(value / GeometrySliderStep, MidpointRounding.AwayFromZero) * GeometrySliderStep;
        return Math.Round(rounded, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeLoadTarget(string? value)
    {
        return string.Equals(value, BottomChordLoadTargetLabel, StringComparison.OrdinalIgnoreCase)
            ? BottomChordLoadTargetLabel
            : TopChordLoadTargetLabel;
    }

    private static string NormalizeLoadInputType(string? value)
    {
        return string.Equals(value, AreaLoadInputLabel, StringComparison.OrdinalIgnoreCase)
            ? AreaLoadInputLabel
            : LineLoadInputLabel;
    }

    private static string NormalizeLoadApplicationMode(string? value)
    {
        return string.Equals(value, MemberLineApplicationLabel, StringComparison.OrdinalIgnoreCase)
            ? MemberLineApplicationLabel
            : PanelNodesApplicationLabel;
    }

    private static ParametricTrussLoadTarget ToLoadTarget(string? value)
    {
        return string.Equals(value, BottomChordLoadTargetLabel, StringComparison.OrdinalIgnoreCase)
            ? ParametricTrussLoadTarget.BottomChord
            : ParametricTrussLoadTarget.TopChord;
    }

    private static ParametricTrussLoadInputType ToLoadInputType(string? value)
    {
        return string.Equals(value, AreaLoadInputLabel, StringComparison.OrdinalIgnoreCase)
            ? ParametricTrussLoadInputType.AreaLoadKpa
            : ParametricTrussLoadInputType.LineLoadKnPerM;
    }

    private static ParametricTrussLoadApplicationMode ToLoadApplicationMode(string? value)
    {
        return string.Equals(value, MemberLineApplicationLabel, StringComparison.OrdinalIgnoreCase)
            ? ParametricTrussLoadApplicationMode.MemberLine
            : ParametricTrussLoadApplicationMode.PanelNodes;
    }

    private bool IsEraseAndRedrawMode =>
        string.Equals(SelectedEtabsExportMode, EtabsExportModeLabels.EraseAndRedraw, StringComparison.OrdinalIgnoreCase);

    private bool IsAddAsNewMode =>
        string.Equals(SelectedEtabsExportMode, EtabsExportModeLabels.AddAsNew, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSupportNodeMode(string? value)
    {
        if (string.Equals(value, SupportNodeModeLabels.AllBottomChordNodes, StringComparison.OrdinalIgnoreCase))
            return SupportNodeModeLabels.AllBottomChordNodes;
        if (string.Equals(value, SupportNodeModeLabels.NoSupports, StringComparison.OrdinalIgnoreCase))
            return SupportNodeModeLabels.NoSupports;

        return SupportNodeModeLabels.EndBottomNodes;
    }

    private static string NormalizeSupportRestraintType(string? value)
    {
        if (string.Equals(value, SupportRestraintTypeLabels.AllPinned, StringComparison.OrdinalIgnoreCase))
            return SupportRestraintTypeLabels.AllPinned;
        if (string.Equals(value, SupportRestraintTypeLabels.AllZRollers, StringComparison.OrdinalIgnoreCase))
            return SupportRestraintTypeLabels.AllZRollers;

        return SupportRestraintTypeLabels.FirstPinOthersRoller;
    }

    private static SupportNodeMode ToSupportNodeMode(string? value)
    {
        if (string.Equals(value, SupportNodeModeLabels.AllBottomChordNodes, StringComparison.OrdinalIgnoreCase))
            return SupportNodeMode.AllBottomChordNodes;
        if (string.Equals(value, SupportNodeModeLabels.NoSupports, StringComparison.OrdinalIgnoreCase))
            return SupportNodeMode.NoSupports;

        return SupportNodeMode.EndBottomNodes;
    }

    private static SupportRestraintType ToSupportRestraintType(string? value)
    {
        if (string.Equals(value, SupportRestraintTypeLabels.AllPinned, StringComparison.OrdinalIgnoreCase))
            return SupportRestraintType.AllPinned;
        if (string.Equals(value, SupportRestraintTypeLabels.AllZRollers, StringComparison.OrdinalIgnoreCase))
            return SupportRestraintType.AllZRollers;

        return SupportRestraintType.FirstPinOthersRoller;
    }

    private void ApplyDefaultNameForSelectedType()
    {
        if (IsAutomaticTrussId(TrussId))
        {
            _trussId = CurrentTrussType switch
            {
                TrussType.SpiralStaircase => "SPIRAL_001",
                TrussType.FishBellyTruss => "FBT_001",
                TrussType.VariablePanelWidthTruss => "VPT_001",
                _ => "TR01"
            };
            OnPropertyChanged(nameof(TrussId));
        }

        if (!IsAutomaticGroupName(GroupName))
            return;

        GroupName = CurrentTrussType switch
        {
            TrussType.SpiralStaircase => BuildAvailableGroupName("PM_SPIRAL_STAIR_", 1),
            TrussType.FishBellyTruss => BuildAvailableGroupName("PM_FISH_BELLY_TRUSS_", 1),
            TrussType.VariablePanelWidthTruss => BuildAvailableGroupName("PM_VARIABLE_PANEL_TRUSS_", 1),
            _ => EtabsNameUtility.BuildSafeName("WPF_TRUSS_", TrussId)
        };
    }

    private string BuildAvailableGroupName(string prefix, int startIndex)
    {
        for (int index = Math.Max(1, startIndex); index < 1000; index++)
        {
            string candidate = $"{prefix}{index:000}";
            if (!Groups.Any(group => string.Equals(group, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{prefix}{DateTime.Now:HHmmss}";
    }

    private static bool IsAutomaticGroupName(string? value)
    {
        string text = (value ?? "").Trim();
        return text.Length == 0 ||
            text.StartsWith("WPF_TRUSS_", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("PM_SPIRAL_STAIR_", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("PM_FISH_BELLY_TRUSS_", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("PM_VARIABLE_PANEL_TRUSS_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAutomaticTrussId(string? value)
    {
        string text = (value ?? "").Trim();
        return text.Length == 0 ||
            string.Equals(text, "TR01", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "SPIRAL_001", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "FBT_001", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "VPT_001", StringComparison.OrdinalIgnoreCase);
    }

    private static TrussType ToTrussType(string? value)
    {
        if (string.Equals(value, TrussTypeLabels.SpiralStaircase, StringComparison.OrdinalIgnoreCase))
            return TrussType.SpiralStaircase;
        if (string.Equals(value, TrussTypeLabels.FishBellyTruss, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Fish Belly Truss", StringComparison.OrdinalIgnoreCase))
            return TrussType.FishBellyTruss;
        if (string.Equals(value, TrussTypeLabels.VariablePanelWidthTruss, StringComparison.OrdinalIgnoreCase))
            return TrussType.VariablePanelWidthTruss;

        return Enum.TryParse(value, out TrussType trussType) ? trussType : TrussType.Warren;
    }

    private static SpiralStairRotationDirection ToSpiralRotationDirection(string? value)
    {
        return string.Equals(value, SpiralRotationDirectionLabels.Clockwise, StringComparison.OrdinalIgnoreCase)
            ? SpiralStairRotationDirection.Clockwise
            : SpiralStairRotationDirection.Anticlockwise;
    }

    private static FishBellyBottomChordShape ToFishBottomChordShape(string? value)
    {
        if (string.Equals(value, FishBottomChordShapeLabels.LinearToMiddle, StringComparison.OrdinalIgnoreCase))
            return FishBellyBottomChordShape.LinearToMiddle;
        if (string.Equals(value, FishBottomChordShapeLabels.CircularArc, StringComparison.OrdinalIgnoreCase))
            return FishBellyBottomChordShape.CircularArcApproximation;

        return FishBellyBottomChordShape.Parabolic;
    }

    private static FishBellyWebPattern ToFishWebPattern(string? value)
    {
        if (string.Equals(value, FishWebPatternLabels.VerticalSameDirection, StringComparison.OrdinalIgnoreCase))
            return FishBellyWebPattern.VerticalSameDirectionDiagonal;
        if (string.Equals(value, FishWebPatternLabels.Warren, StringComparison.OrdinalIgnoreCase))
            return FishBellyWebPattern.Warren;
        if (string.Equals(value, FishWebPatternLabels.Pratt, StringComparison.OrdinalIgnoreCase))
            return FishBellyWebPattern.Pratt;
        if (string.Equals(value, FishWebPatternLabels.Howe, StringComparison.OrdinalIgnoreCase))
            return FishBellyWebPattern.Howe;
        if (string.Equals(value, FishWebPatternLabels.CrossBracing, StringComparison.OrdinalIgnoreCase))
            return FishBellyWebPattern.CrossBracing;

        return FishBellyWebPattern.VerticalAlternatingDiagonal;
    }

    private static VariablePanelWidthVariation ToVariablePanelWidthVariation(string? value)
    {
        if (string.Equals(value, VariablePanelWidthVariationLabels.SmoothCosine, StringComparison.OrdinalIgnoreCase))
            return VariablePanelWidthVariation.SmoothCosine;
        if (string.Equals(value, VariablePanelWidthVariationLabels.LinearToMiddle, StringComparison.OrdinalIgnoreCase))
            return VariablePanelWidthVariation.LinearToMiddle;

        return VariablePanelWidthVariation.Parabolic;
    }

    private static class TrussTypeLabels
    {
        public const string SpiralStaircase = "Spiral Staircase";
        public const string FishBellyTruss = "Fish-Belly Truss";
        public const string VariablePanelWidthTruss = "Variable Panel Width Truss";
    }

    private static class SpiralRotationDirectionLabels
    {
        public const string Anticlockwise = "Anti-clockwise";
        public const string Clockwise = "Clockwise";
    }

    private static class FishBottomChordShapeLabels
    {
        public const string Parabolic = "Parabolic";
        public const string LinearToMiddle = "Linear to middle";
        public const string CircularArc = "Circular arc approximation";
    }

    private static class FishWebPatternLabels
    {
        public const string VerticalAlternating = "Alternating Diagonal";
        public const string VerticalSameDirection = "Single Diagonal Same Direction";
        public const string Warren = "Warren";
        public const string Pratt = "Pratt";
        public const string Howe = "Howe";
        public const string CrossBracing = "Cross Bracing";
    }

    private static class VariablePanelWidthVariationLabels
    {
        public const string Parabolic = "Parabolic";
        public const string SmoothCosine = "Smooth cosine";
        public const string LinearToMiddle = "Linear to middle";
    }

    private static class EtabsExportModeLabels
    {
        public const string EraseAndRedraw = "Erase and redraw";
        public const string AddAsNew = "Add as new";
    }

    private static class SupportNodeModeLabels
    {
        public const string EndBottomNodes = "End bottom nodes";
        public const string AllBottomChordNodes = "All bottom chord nodes";
        public const string NoSupports = "No supports";
    }

    private static class SupportRestraintTypeLabels
    {
        public const string FirstPinOthersRoller = "First pin, others roller";
        public const string AllPinned = "All selected pinned";
        public const string AllZRollers = "All selected Z rollers";
    }

    private static class SectionPreviewModeLabels
    {
        public const string ThreeD = "3D";
        public const string TwoD = "2D";
    }
}
