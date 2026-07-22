using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class SectionPropertyViewModel : ObservableObject
{
    private const double MillimetresPerMetre = 1000.0;
    private const string EtabsProduct = "ETABS";
    private const string Sap2000Product = "SAP2000";

    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly Sap2000ModellingService _sap2000Service = new();
    private readonly List<SteelCatalogSectionRow> _allSteelCatalogSectionRows = [];
    private bool _hasLoadedSteelCatalog;
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private Sap2000InstanceInfo? _selectedSap2000Instance;
    private EtabsMaterialPropertyRow? _selectedMaterial;
    private EtabsFramePropertyRow? _selectedFrameProperty;
    private EtabsAreaPropertyRow? _selectedAreaProperty;
    private string _connectionStatus = "Not connected";
    private string _sap2000ConnectionStatus = "Not connected";
    private string _activeProduct = EtabsProduct;
    private string _editorStatus = "Read CSI properties to begin.";
    private string _operationStatus = "Ready.";
    private bool _isBusy;
    private string _materialName = "CONC30";
    private string _selectedMaterialType = "Concrete";
    private double _elasticModulusMpa = 30000;
    private double _poissonRatio = 0.2;
    private double _thermalExpansion = 0.0000099;
    private double _unitWeightKnPerM3 = 24.0;
    private double _concreteFcMpa = 30.0;
    private double _steelFyMpa = 345.0;
    private double _steelFuMpa = 450.0;
    private string _steelDatabaseFile = "BSSHAPE2006";
    private string _selectedSteelCatalogShape = "I";
    private string _selectedSteelDatabaseSection = "";
    private string _importedSteelPropertyName = "";
    private string _steelCatalogSearchText = "";
    private string _selectedSteelMaterialName = "";
    private SteelCatalogSectionRow? _selectedSteelCatalogSectionRow;
    private string _framePropertyName = "B300X500";
    private string _selectedFrameShapeType = "Concrete Rectangular";
    private string _selectedFrameMaterialName = "";
    private string _selectedSectionRole = "Beam / General";
    private double _frameDepthMm = 500;
    private double _frameWidthMm = 300;
    private double _flangeThicknessMm = 16;
    private double _webThicknessMm = 10;
    private string _areaPropertyName = "SLAB150";
    private string _selectedAreaType = "Slab";
    private string _selectedSlabType = "Slab";
    private string _selectedShellType = "ShellThin";
    private string _selectedAreaMaterialName = "";
    private double _areaThicknessMm = 150;
    private TaperedSteelSelection? _taperedSelection;
    private TaperedSteelGenerationPreview? _currentTaperedPreview;
    private string _selectedTaperedBaseSectionName = "";
    private double _taperedTipDepthMm = 200;
    private string _selectedTaperedTipEnd = "J-End";
    private string _selectedTaperedType = "Tapered I-section";
    private string _selectedTaperedReferenceLine = "Keep top flange straight";
    private int _taperedStationCount = 1;
    private bool _showTaperedStations = true;
    private string _taperedPreviewReport = "No ETABS frame members loaded.";

    public SectionPropertyViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Refreshing ETABS instances...", RefreshEtabsInstances), _ => !IsBusy);
        ReadEtabsDataCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Reading ETABS section properties...", () => ReadEtabsData(true)), _ => !IsBusy);
        RefreshSap2000InstancesCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Refreshing SAP2000 instances...", RefreshSap2000Instances), _ => !IsBusy);
        ReadSap2000DataCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Reading SAP2000 section properties...", ReadSap2000Data), _ => !IsBusy);
        NewMaterialCommand = new RelayCommand(_ => AddNewMaterial());
        SaveMaterialCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Applying material property to CSI model...", SaveMaterial), _ => !IsBusy);
        DeleteMaterialCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Deleting material property from CSI model...", DeleteSelectedMaterial), _ => !IsBusy);
        LoadSteelCatalogCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Loading steel database sections...", LoadSteelCatalog), _ => !IsBusy);
        ImportSteelSectionCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Importing selected steel section to CSI model...", ImportSteelSection), _ => !IsBusy);
        CheckAllSteelCatalogCommand = new RelayCommand(_ => SetSteelCatalogRowsChecked(true));
        UncheckAllSteelCatalogCommand = new RelayCommand(_ => SetSteelCatalogRowsChecked(false));
        TickSelectedSteelCatalogCommand = new RelayCommand(parameter => TickSelectedSteelCatalogRows(parameter));
        ImportCheckedSteelSectionsCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Importing checked steel sections to CSI model...", ImportCheckedSteelSections), _ => !IsBusy);
        ClearSteelCatalogSearchCommand = new RelayCommand(_ => SteelCatalogSearchText = "", _ => !string.IsNullOrWhiteSpace(SteelCatalogSearchText));
        NewFramePropertyCommand = new RelayCommand(_ => AddNewFrameProperty());
        SaveFramePropertyCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Applying frame section to CSI model...", SaveFrameProperty), _ => !IsBusy);
        DeleteFramePropertyCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Deleting frame section from CSI model...", DeleteSelectedFrameProperty), _ => !IsBusy);
        NewAreaPropertyCommand = new RelayCommand(_ => AddNewAreaProperty());
        SaveAreaPropertyCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Applying slab/wall property to CSI model...", SaveAreaProperty), _ => !IsBusy);
        DeleteAreaPropertyCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Deleting slab/wall property from CSI model...", DeleteSelectedAreaProperty), _ => !IsBusy);
        LoadTaperedBaseSectionCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Loading tapered steel base section...", LoadTaperedBaseSection), _ => !IsBusy);
        PreviewTaperedSectionCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Previewing tapered steel section...", PreviewTaperedSection), _ => !IsBusy);
        CreateTaperedSectionCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Creating tapered steel section...", CreateTaperedSection), _ => !IsBusy);
        AssignTaperedSectionCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Assigning tapered steel section to selected ETABS members...", AssignTaperedSection), _ => !IsBusy);
    }

    public IReadOnlyList<string> MaterialTypes { get; } =
    [
        "Concrete",
        "Steel",
        "Rebar",
        "NoDesign",
        "Aluminum",
        "ColdFormed",
        "Masonry"
    ];

    public IReadOnlyList<string> SteelDatabaseFiles { get; } =
    [
        "BSSHAPE2006",
        "AISC15.xml",
        "AISC14.xml",
        "AISC13.xml"
    ];

    public IReadOnlyList<string> SteelCatalogShapeTypes { get; } =
    [
        "I",
        "Channel",
        "T",
        "Angle",
        "Tube",
        "Pipe"
    ];

    public IReadOnlyList<string> FrameShapeTypes { get; } =
    [
        "Concrete Rectangular",
        "Concrete Circular",
        "Steel I",
        "Steel Channel",
        "Steel Tube",
        "Steel Pipe"
    ];

    public IReadOnlyList<string> SectionRoles { get; } =
    [
        "Beam / General",
        "Column"
    ];

    public IReadOnlyList<string> AreaTypes { get; } =
    [
        "Slab",
        "Wall"
    ];

    public IReadOnlyList<string> SlabTypes { get; } =
    [
        "Slab",
        "Drop",
        "Mat",
        "Footing"
    ];

    public IReadOnlyList<string> ShellTypes { get; } =
    [
        "ShellThin",
        "ShellThick",
        "Membrane"
    ];

    public IReadOnlyList<string> TaperedTipEnds { get; } =
    [
        "I-End",
        "J-End"
    ];

    public IReadOnlyList<string> TaperedTypes { get; } =
    [
        "Tapered I-section",
        "Tapered T-section (remove bottom flange)",
        "Tapered tube/box",
        "Tapered U-section (remove bottom flange)"
    ];

    public IReadOnlyList<string> TaperedReferenceLines { get; } =
    [
        "Keep top flange straight",
        "Keep centroid line straight",
        "Keep bottom flange straight"
    ];

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<Sap2000InstanceInfo> Sap2000Instances { get; } = [];
    public ObservableCollection<EtabsMaterialPropertyRow> Materials { get; } = [];
    public ObservableCollection<EtabsFramePropertyRow> FrameProperties { get; } = [];
    public ObservableCollection<EtabsAreaPropertyRow> AreaProperties { get; } = [];
    public ObservableCollection<string> MaterialNames { get; } = [];
    public ObservableCollection<string> SteelCatalogSections { get; } = [];
    public ObservableCollection<SteelCatalogSectionRow> SteelCatalogSectionRows { get; } = [];
    public ObservableCollection<TaperedSteelFrameInfo> TaperedSelectedFrames { get; } = [];
    public ObservableCollection<TaperedSteelStationPreview> TaperedStations { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand RefreshSap2000InstancesCommand { get; }
    public ICommand ReadSap2000DataCommand { get; }
    public ICommand NewMaterialCommand { get; }
    public ICommand SaveMaterialCommand { get; }
    public ICommand DeleteMaterialCommand { get; }
    public ICommand LoadSteelCatalogCommand { get; }
    public ICommand ImportSteelSectionCommand { get; }
    public ICommand CheckAllSteelCatalogCommand { get; }
    public ICommand UncheckAllSteelCatalogCommand { get; }
    public ICommand TickSelectedSteelCatalogCommand { get; }
    public ICommand ImportCheckedSteelSectionsCommand { get; }
    public ICommand ClearSteelCatalogSearchCommand { get; }
    public ICommand NewFramePropertyCommand { get; }
    public ICommand SaveFramePropertyCommand { get; }
    public ICommand DeleteFramePropertyCommand { get; }
    public ICommand NewAreaPropertyCommand { get; }
    public ICommand SaveAreaPropertyCommand { get; }
    public ICommand DeleteAreaPropertyCommand { get; }
    public ICommand LoadTaperedBaseSectionCommand { get; }
    public ICommand PreviewTaperedSectionCommand { get; }
    public ICommand CreateTaperedSectionCommand { get; }
    public ICommand AssignTaperedSectionCommand { get; }

    public EtabsInstanceInfo? SelectedEtabsInstance
    {
        get => _selectedEtabsInstance;
        set
        {
            if (SetProperty(ref _selectedEtabsInstance, value))
                OnPropertyChanged(nameof(SelectedEtabsInstanceId));
        }
    }

    public string SelectedEtabsInstanceId => SelectedEtabsInstance?.Id ?? "";

    public Sap2000InstanceInfo? SelectedSap2000Instance
    {
        get => _selectedSap2000Instance;
        set
        {
            if (SetProperty(ref _selectedSap2000Instance, value))
                OnPropertyChanged(nameof(SelectedSap2000InstanceId));
        }
    }

    public string SelectedSap2000InstanceId => SelectedSap2000Instance?.Id ?? "";

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string Sap2000ConnectionStatus
    {
        get => _sap2000ConnectionStatus;
        set => SetProperty(ref _sap2000ConnectionStatus, value);
    }

    public string ActiveProduct
    {
        get => _activeProduct;
        private set => SetProperty(ref _activeProduct, value);
    }

    private bool IsSap2000Active => string.Equals(ActiveProduct, Sap2000Product, StringComparison.OrdinalIgnoreCase);

    public string EditorStatus
    {
        get => _editorStatus;
        set => SetProperty(ref _editorStatus, value);
    }

    public string OperationStatus
    {
        get => _operationStatus;
        set => SetProperty(ref _operationStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(BusyVisibility));
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public EtabsMaterialPropertyRow? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (SetProperty(ref _selectedMaterial, value) && value != null)
            {
                MaterialName = value.Name;
                SelectedMaterialType = value.MaterialType;
                ElasticModulusMpa = value.ElasticModulusMpa > 0 ? value.ElasticModulusMpa : ElasticModulusMpa;
                PoissonRatio = value.PoissonRatio > 0 ? value.PoissonRatio : PoissonRatio;
                UnitWeightKnPerM3 = value.UnitWeightKnPerM3 > 0 ? value.UnitWeightKnPerM3 : UnitWeightKnPerM3;
            }
        }
    }

    public EtabsFramePropertyRow? SelectedFrameProperty
    {
        get => _selectedFrameProperty;
        set
        {
            if (SetProperty(ref _selectedFrameProperty, value) && value != null)
            {
                FramePropertyName = value.Name;
                SelectedFrameMaterialName = value.MaterialName;
                SelectedFrameShapeType = ToFrameShapeLabel(value.ShapeType);
                if (value.DepthMm > 0)
                    FrameDepthMm = value.DepthMm;
                if (value.WidthMm > 0)
                    FrameWidthMm = value.WidthMm;
                if (value.FlangeThicknessMm > 0)
                    FlangeThicknessMm = value.FlangeThicknessMm;
                if (value.WebThicknessMm > 0)
                    WebThicknessMm = value.WebThicknessMm;
            }
        }
    }

    public EtabsAreaPropertyRow? SelectedAreaProperty
    {
        get => _selectedAreaProperty;
        set
        {
            if (SetProperty(ref _selectedAreaProperty, value) && value != null)
            {
                AreaPropertyName = value.Name;
                SelectedAreaType = string.Equals(value.AreaType, "Wall", StringComparison.OrdinalIgnoreCase) ? "Wall" : "Slab";
                SelectedShellType = value.ShellType;
                SelectedAreaMaterialName = value.MaterialName;
                AreaThicknessMm = value.ThicknessMm > 0 ? value.ThicknessMm : AreaThicknessMm;
            }
        }
    }

    public string MaterialName
    {
        get => _materialName;
        set
        {
            if (SetProperty(ref _materialName, value ?? "") && SelectedMaterial != null)
                SelectedMaterial.Name = _materialName;
        }
    }

    public string SelectedMaterialType
    {
        get => _selectedMaterialType;
        set
        {
            if (SetProperty(ref _selectedMaterialType, value ?? "Concrete"))
            {
                if (SelectedMaterial != null)
                    SelectedMaterial.MaterialType = _selectedMaterialType;
                OnPropertyChanged(nameof(ConcreteMaterialVisibility));
                OnPropertyChanged(nameof(SteelMaterialVisibility));
            }
        }
    }

    public double ElasticModulusMpa
    {
        get => _elasticModulusMpa;
        set
        {
            if (SetProperty(ref _elasticModulusMpa, double.IsFinite(value) ? value : 0.0) && SelectedMaterial != null)
                SelectedMaterial.ElasticModulusMpa = _elasticModulusMpa;
        }
    }

    public double PoissonRatio
    {
        get => _poissonRatio;
        set
        {
            if (SetProperty(ref _poissonRatio, double.IsFinite(value) ? value : 0.2) && SelectedMaterial != null)
                SelectedMaterial.PoissonRatio = _poissonRatio;
        }
    }

    public double ThermalExpansion
    {
        get => _thermalExpansion;
        set => SetProperty(ref _thermalExpansion, double.IsFinite(value) ? value : 0.0);
    }

    public double UnitWeightKnPerM3
    {
        get => _unitWeightKnPerM3;
        set
        {
            if (SetProperty(ref _unitWeightKnPerM3, double.IsFinite(value) ? value : 0.0) && SelectedMaterial != null)
                SelectedMaterial.UnitWeightKnPerM3 = _unitWeightKnPerM3;
        }
    }

    public double ConcreteFcMpa
    {
        get => _concreteFcMpa;
        set => SetProperty(ref _concreteFcMpa, double.IsFinite(value) ? value : 0.0);
    }

    public double SteelFyMpa
    {
        get => _steelFyMpa;
        set => SetProperty(ref _steelFyMpa, double.IsFinite(value) ? value : 0.0);
    }

    public double SteelFuMpa
    {
        get => _steelFuMpa;
        set => SetProperty(ref _steelFuMpa, double.IsFinite(value) ? value : 0.0);
    }

    public Visibility ConcreteMaterialVisibility =>
        string.Equals(SelectedMaterialType, "Concrete", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility SteelMaterialVisibility =>
        string.Equals(SelectedMaterialType, "Steel", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string SteelDatabaseFile
    {
        get => _steelDatabaseFile;
        set
        {
            if (SetProperty(ref _steelDatabaseFile, value ?? ""))
                ReloadSteelCatalogAfterSelectionChange();
        }
    }

    public string SelectedSteelCatalogShape
    {
        get => _selectedSteelCatalogShape;
        set
        {
            if (SetProperty(ref _selectedSteelCatalogShape, value ?? "I"))
                ReloadSteelCatalogAfterSelectionChange();
        }
    }

    public string SelectedSteelDatabaseSection
    {
        get => _selectedSteelDatabaseSection;
        set
        {
            if (SetProperty(ref _selectedSteelDatabaseSection, value ?? "") &&
                (string.IsNullOrWhiteSpace(ImportedSteelPropertyName) || SteelCatalogSections.Contains(ImportedSteelPropertyName)))
            {
                ImportedSteelPropertyName = SelectedSteelDatabaseSection;
            }
        }
    }

    public string ImportedSteelPropertyName
    {
        get => _importedSteelPropertyName;
        set => SetProperty(ref _importedSteelPropertyName, value ?? "");
    }

    public string SteelCatalogSearchText
    {
        get => _steelCatalogSearchText;
        set
        {
            if (SetProperty(ref _steelCatalogSearchText, value ?? ""))
                ApplySteelCatalogSearch();
        }
    }

    public string SteelCatalogSearchSummary
    {
        get
        {
            if (_allSteelCatalogSectionRows.Count == 0)
                return "No steel catalog sections loaded.";

            return string.IsNullOrWhiteSpace(SteelCatalogSearchText)
                ? $"Showing {SteelCatalogSectionRows.Count} section(s)."
                : $"Showing {SteelCatalogSectionRows.Count} of {_allSteelCatalogSectionRows.Count} section(s).";
        }
    }

    public string SelectedSteelMaterialName
    {
        get => _selectedSteelMaterialName;
        set => SetProperty(ref _selectedSteelMaterialName, value ?? "");
    }

    public SteelCatalogSectionRow? SelectedSteelCatalogSectionRow
    {
        get => _selectedSteelCatalogSectionRow;
        set
        {
            if (SetProperty(ref _selectedSteelCatalogSectionRow, value) && value != null)
            {
                SelectedSteelDatabaseSection = value.SectionName;
                ImportedSteelPropertyName = value.PropertyName;
            }
        }
    }

    public string FramePropertyName
    {
        get => _framePropertyName;
        set
        {
            if (SetProperty(ref _framePropertyName, value ?? "") && SelectedFrameProperty != null)
                SelectedFrameProperty.Name = _framePropertyName;
        }
    }

    public string SelectedFrameShapeType
    {
        get => _selectedFrameShapeType;
        set
        {
            if (SetProperty(ref _selectedFrameShapeType, value ?? "Concrete Rectangular"))
            {
                if (SelectedFrameProperty != null)
                    SelectedFrameProperty.ShapeType = _selectedFrameShapeType;
                OnPropertyChanged(nameof(SteelFrameDimensionVisibility));
                OnPropertyChanged(nameof(RectangularWidthVisibility));
                OnPropertyChanged(nameof(CircularLabel));
            }
        }
    }

    public string SelectedFrameMaterialName
    {
        get => _selectedFrameMaterialName;
        set
        {
            if (SetProperty(ref _selectedFrameMaterialName, value ?? "") && SelectedFrameProperty != null)
                SelectedFrameProperty.MaterialName = _selectedFrameMaterialName;
        }
    }

    public string SelectedSectionRole
    {
        get => _selectedSectionRole;
        set => SetProperty(ref _selectedSectionRole, value ?? "Beam / General");
    }

    public double FrameDepthMm
    {
        get => _frameDepthMm;
        set
        {
            if (SetProperty(ref _frameDepthMm, double.IsFinite(value) ? value : 0.0) && SelectedFrameProperty != null)
                SelectedFrameProperty.DepthMm = _frameDepthMm;
        }
    }

    public double FrameWidthMm
    {
        get => _frameWidthMm;
        set
        {
            if (SetProperty(ref _frameWidthMm, double.IsFinite(value) ? value : 0.0) && SelectedFrameProperty != null)
                SelectedFrameProperty.WidthMm = _frameWidthMm;
        }
    }

    public double FlangeThicknessMm
    {
        get => _flangeThicknessMm;
        set
        {
            if (SetProperty(ref _flangeThicknessMm, double.IsFinite(value) ? value : 0.0) && SelectedFrameProperty != null)
                SelectedFrameProperty.FlangeThicknessMm = _flangeThicknessMm;
        }
    }

    public double WebThicknessMm
    {
        get => _webThicknessMm;
        set
        {
            if (SetProperty(ref _webThicknessMm, double.IsFinite(value) ? value : 0.0) && SelectedFrameProperty != null)
                SelectedFrameProperty.WebThicknessMm = _webThicknessMm;
        }
    }

    public Visibility SteelFrameDimensionVisibility =>
        SelectedFrameShapeType.StartsWith("Steel", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility RectangularWidthVisibility =>
        SelectedFrameShapeType.Contains("Circular", StringComparison.OrdinalIgnoreCase) ||
        SelectedFrameShapeType.Contains("Pipe", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public string CircularLabel =>
        SelectedFrameShapeType.Contains("Circular", StringComparison.OrdinalIgnoreCase) ||
        SelectedFrameShapeType.Contains("Pipe", StringComparison.OrdinalIgnoreCase)
            ? "Diameter"
            : "Depth";

    public string AreaPropertyName
    {
        get => _areaPropertyName;
        set
        {
            if (SetProperty(ref _areaPropertyName, value ?? "") && SelectedAreaProperty != null)
                SelectedAreaProperty.Name = _areaPropertyName;
        }
    }

    public string SelectedAreaType
    {
        get => _selectedAreaType;
        set
        {
            if (SetProperty(ref _selectedAreaType, value ?? "Slab"))
            {
                if (SelectedAreaProperty != null)
                    SelectedAreaProperty.AreaType = _selectedAreaType;
                OnPropertyChanged(nameof(SlabTypeVisibility));
            }
        }
    }

    public string SelectedSlabType
    {
        get => _selectedSlabType;
        set => SetProperty(ref _selectedSlabType, value ?? "Slab");
    }

    public string SelectedShellType
    {
        get => _selectedShellType;
        set
        {
            if (SetProperty(ref _selectedShellType, value ?? "ShellThin") && SelectedAreaProperty != null)
                SelectedAreaProperty.ShellType = _selectedShellType;
        }
    }

    public string SelectedAreaMaterialName
    {
        get => _selectedAreaMaterialName;
        set
        {
            if (SetProperty(ref _selectedAreaMaterialName, value ?? "") && SelectedAreaProperty != null)
                SelectedAreaProperty.MaterialName = _selectedAreaMaterialName;
        }
    }

    public double AreaThicknessMm
    {
        get => _areaThicknessMm;
        set
        {
            if (SetProperty(ref _areaThicknessMm, double.IsFinite(value) ? value : 0.0) && SelectedAreaProperty != null)
                SelectedAreaProperty.ThicknessMm = _areaThicknessMm;
        }
    }

    public Visibility SlabTypeVisibility =>
        string.Equals(SelectedAreaType, "Slab", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string SelectedTaperedBaseSectionName
    {
        get => _selectedTaperedBaseSectionName;
        set => SetProperty(ref _selectedTaperedBaseSectionName, value ?? "");
    }

    public TaperedSteelGenerationPreview? CurrentTaperedPreview
    {
        get => _currentTaperedPreview;
        set => SetProperty(ref _currentTaperedPreview, value);
    }

    public double TaperedTipDepthMm
    {
        get => _taperedTipDepthMm;
        set => SetProperty(ref _taperedTipDepthMm, double.IsFinite(value) ? value : 0.0);
    }

    public string SelectedTaperedTipEnd
    {
        get => _selectedTaperedTipEnd;
        set => SetProperty(ref _selectedTaperedTipEnd, value ?? "J-End");
    }

    public string SelectedTaperedType
    {
        get => _selectedTaperedType;
        set
        {
            if (SetProperty(ref _selectedTaperedType, value ?? "Tapered I-section"))
                OnPropertyChanged(nameof(TaperedStationCountVisibility));
        }
    }

    public string SelectedTaperedReferenceLine
    {
        get => _selectedTaperedReferenceLine;
        set => SetProperty(ref _selectedTaperedReferenceLine, value ?? "Keep top flange straight");
    }

    public int TaperedStationCount
    {
        get => _taperedStationCount;
        set => SetProperty(ref _taperedStationCount, Math.Max(1, value));
    }

    public bool ShowTaperedStations
    {
        get => _showTaperedStations;
        set
        {
            if (SetProperty(ref _showTaperedStations, value))
                OnPropertyChanged(nameof(TaperedStationsVisibility));
        }
    }

    public string TaperedPreviewReport
    {
        get => _taperedPreviewReport;
        set => SetProperty(ref _taperedPreviewReport, value ?? "");
    }

    public string TaperedSelectedMembersSummary =>
        _taperedSelection == null
            ? "No base steel section loaded."
            : $"Base section {_taperedSelection.BaseSectionName}, material {_taperedSelection.MaterialName}.";

    public string TaperedBaseGeometrySummary =>
        _taperedSelection == null
            ? "Import/select a steel I or steel tube/box section, then click Load Base Section."
            : FormatTaperedGeometrySummary(_taperedSelection.BaseGeometry);

    public Visibility TaperedStationsVisibility => ShowTaperedStations ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TaperedStationCountVisibility =>
        ToTaperedSectionType(SelectedTaperedType) is TaperedSectionType.TSection or TaperedSectionType.USection
            ? Visibility.Visible
            : Visibility.Collapsed;

    private async Task RunBusyCommandAsync(string busyMessage, Action action)
    {
        if (IsBusy)
            return;

        string startingOperationStatus = OperationStatus;
        IsBusy = true;
        OperationStatus = busyMessage;
        Mouse.OverrideCursor = Cursors.Wait;
        CommandManager.InvalidateRequerySuggested();

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(
                () => { },
                System.Windows.Threading.DispatcherPriority.Background);

            action();

            if (string.Equals(OperationStatus, busyMessage, StringComparison.Ordinal) ||
                string.Equals(OperationStatus, startingOperationStatus, StringComparison.Ordinal))
            {
                OperationStatus = IsCurrentStatusFailure()
                    ? $"Failed: {EditorStatus}"
                    : $"Done: {EditorStatus}";
            }
        }
        catch (Exception ex)
        {
            SetActiveConnectionStatus("Command failed");
            EditorStatus = ex.Message;
            OperationStatus = $"Failed: {ex.Message}";
            ShowMessages([], ValidationSeverity.Critical, ex.Message);
        }
        finally
        {
            IsBusy = false;
            Mouse.OverrideCursor = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool IsCurrentStatusFailure()
    {
        string activeStatus = IsSap2000Active ? Sap2000ConnectionStatus : ConnectionStatus;
        return activeStatus.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            activeStatus.Contains("not connected", StringComparison.OrdinalIgnoreCase) ||
            Messages.Any(message => message.Severity == ValidationSeverity.Critical);
    }

    private void RefreshEtabsInstances()
    {
        EtabsInstanceListResult result = _etabsService.ListEtabsInstances();
        string previousId = SelectedEtabsInstanceId;
        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, previousId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();

        ConnectionStatus = result.Message;
        EditorStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void RefreshSap2000Instances()
    {
        Sap2000InstanceListResult result = _sap2000Service.ListSap2000Instances();
        string previousId = SelectedSap2000InstanceId;
        ReplaceCollection(Sap2000Instances, result.Instances);
        SelectedSap2000Instance = Sap2000Instances.FirstOrDefault(instance =>
            string.Equals(instance.Id, previousId, StringComparison.OrdinalIgnoreCase)) ??
            Sap2000Instances.FirstOrDefault();

        Sap2000ConnectionStatus = result.Message;
        EditorStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void ReadEtabsData(bool showMessages)
    {
        ActiveProduct = EtabsProduct;
        SectionPropertyDataResult result = _etabsService.ListSectionPropertyData(new SectionPropertyDataRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId
        });

        string previousInstanceId = SelectedEtabsInstanceId;
        string previousMaterial = SelectedFrameMaterialName;
        string previousAreaMaterial = SelectedAreaMaterialName;
        string previousSteelMaterial = SelectedSteelMaterialName;

        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, result.SelectedInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault(instance =>
                string.Equals(instance.Id, previousInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();

        ReplaceCollection(Materials, result.Materials);
        ReplaceCollection(FrameProperties, result.FrameProperties);
        ReplaceCollection(AreaProperties, result.AreaProperties);
        ReplaceCollection(MaterialNames, Materials.Select(row => row.Name));

        if (string.IsNullOrWhiteSpace(SelectedTaperedBaseSectionName) && FrameProperties.Count > 0)
            SelectedTaperedBaseSectionName = FrameProperties.FirstOrDefault(row => string.Equals(ToFrameShapeLabel(row.ShapeType), "Steel I", StringComparison.OrdinalIgnoreCase))?.Name ??
                FrameProperties.First().Name;

        PickMaterialSelections(previousMaterial, previousAreaMaterial, previousSteelMaterial);
        SelectedMaterial = Materials.FirstOrDefault(row => string.Equals(row.Name, MaterialName, StringComparison.OrdinalIgnoreCase)) ??
            Materials.FirstOrDefault();

        ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        EditorStatus = result.Message;

        if (showMessages)
            ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void ReadSap2000Data()
    {
        ActiveProduct = Sap2000Product;
        SectionPropertyDataResult result = _sap2000Service.ListSectionPropertyData(new SectionPropertyDataRequest
        {
            Sap2000InstanceId = SelectedSap2000InstanceId
        });

        string previousInstanceId = SelectedSap2000InstanceId;
        string previousMaterial = SelectedFrameMaterialName;
        string previousAreaMaterial = SelectedAreaMaterialName;
        string previousSteelMaterial = SelectedSteelMaterialName;

        ReplaceCollection(Sap2000Instances, result.Sap2000Instances);
        SelectedSap2000Instance = Sap2000Instances.FirstOrDefault(instance =>
            string.Equals(instance.Id, result.SelectedInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            Sap2000Instances.FirstOrDefault(instance =>
                string.Equals(instance.Id, previousInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            Sap2000Instances.FirstOrDefault();

        ReplaceCollection(Materials, result.Materials);
        ReplaceCollection(FrameProperties, result.FrameProperties);
        ReplaceCollection(AreaProperties, result.AreaProperties);
        ReplaceCollection(MaterialNames, Materials.Select(row => row.Name));

        PickMaterialSelections(previousMaterial, previousAreaMaterial, previousSteelMaterial);
        SelectedMaterial = Materials.FirstOrDefault(row => string.Equals(row.Name, MaterialName, StringComparison.OrdinalIgnoreCase)) ??
            Materials.FirstOrDefault();

        Sap2000ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        EditorStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void AddNewMaterial()
    {
        var row = new EtabsMaterialPropertyRow
        {
            Name = GetUniqueName("NEW_MATERIAL", Materials.Select(material => material.Name)),
            MaterialType = SelectedMaterialType,
            ElasticModulusMpa = ElasticModulusMpa,
            PoissonRatio = PoissonRatio,
            UnitWeightKnPerM3 = UnitWeightKnPerM3,
            DesignSummary = "(new)",
            IsPendingNew = true
        };

        Materials.Add(row);
        ReplaceCollection(MaterialNames, Materials.Select(material => material.Name));
        SelectedMaterial = row;
        EditorStatus = "New material row added. Edit the values below, then apply to the active CSI model.";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void SaveMaterial()
    {
        EtabsMaterialPropertyRow? selected = SelectedMaterial;
        MaterialPropertyUpdateRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            Name = selected?.Name ?? MaterialName,
            MaterialType = selected?.MaterialType ?? SelectedMaterialType,
            ElasticModulusMpa = selected?.ElasticModulusMpa ?? ElasticModulusMpa,
            PoissonRatio = selected?.PoissonRatio ?? PoissonRatio,
            ThermalExpansion = ThermalExpansion,
            UnitWeightKnPerM3 = selected?.UnitWeightKnPerM3 ?? UnitWeightKnPerM3,
            ConcreteFcMpa = ConcreteFcMpa,
            SteelFyMpa = SteelFyMpa,
            SteelFuMpa = SteelFuMpa
        };

        SectionPropertyUpdateResult result = IsSap2000Active
            ? _sap2000Service.UpdateMaterialProperty(request)
            : _etabsService.UpdateMaterialProperty(request);

        ApplyUpdateResult(result);
    }

    private void DeleteSelectedMaterial()
    {
        if (SelectedMaterial == null)
        {
            EditorStatus = "Select a material row first.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        if (SelectedMaterial.IsPendingNew)
        {
            Materials.Remove(SelectedMaterial);
            ReplaceCollection(MaterialNames, Materials.Select(material => material.Name));
            SelectedMaterial = Materials.FirstOrDefault();
            EditorStatus = "Removed new unsaved material row.";
            OperationStatus = $"Done: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Info, EditorStatus);
            return;
        }

        SectionPropertyDeleteRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            Name = SelectedMaterial.Name
        };

        SectionPropertyUpdateResult result = IsSap2000Active
            ? _sap2000Service.DeleteMaterialProperty(request)
            : _etabsService.DeleteMaterialProperty(request);

        ApplyUpdateResult(result);
    }

    private void LoadSteelCatalog()
    {
        EnsureActiveModelPropertiesLoadedForSteelCatalog();

        SteelSectionCatalogRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            DatabaseFile = SteelDatabaseFile,
            ShapeType = SelectedSteelCatalogShape
        };

        SteelSectionCatalogResult result = IsSap2000Active
            ? _sap2000Service.ListSteelSectionCatalog(request)
            : _etabsService.ListSteelSectionCatalog(request);

        ReplaceCollection(SteelCatalogSections, result.SectionNames);
        _allSteelCatalogSectionRows.Clear();
        _allSteelCatalogSectionRows.AddRange(result.SectionNames.Select(name => new SteelCatalogSectionRow
        {
            SectionName = name,
            PropertyName = name
        }));
        _hasLoadedSteelCatalog = true;
        ApplySteelCatalogSearch();

        if (SelectedSteelCatalogSectionRow == null)
        {
            SelectedSteelDatabaseSection = "";
            ImportedSteelPropertyName = "";
        }

        EditorStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void ImportSteelSection()
    {
        string databaseSectionName = SelectedSteelCatalogSectionRow?.SectionName ?? SelectedSteelDatabaseSection;
        string propertyName = SelectedSteelCatalogSectionRow?.PropertyName ?? ImportedSteelPropertyName;
        if (string.IsNullOrWhiteSpace(propertyName))
            propertyName = databaseSectionName;

        SteelSectionImportRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            PropertyName = propertyName,
            MaterialName = SelectedSteelMaterialName,
            DatabaseFile = SteelDatabaseFile,
            DatabaseSectionName = databaseSectionName
        };

        SectionPropertyUpdateResult result = IsSap2000Active
            ? _sap2000Service.ImportSteelFrameProperty(request)
            : _etabsService.ImportSteelFrameProperty(request);

        if (!result.IsError && !string.IsNullOrWhiteSpace(propertyName))
            SelectedTaperedBaseSectionName = propertyName;

        ApplyUpdateResult(result);
    }

    private void SetSteelCatalogRowsChecked(bool include)
    {
        List<SteelCatalogSectionRow> rows = include
            ? SteelCatalogSectionRows.ToList()
            : _allSteelCatalogSectionRows;

        foreach (SteelCatalogSectionRow row in rows)
            row.Include = include;

        EditorStatus = include
            ? $"Checked {rows.Count} shown steel catalog section(s)."
            : "Unchecked all steel catalog sections.";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void TickSelectedSteelCatalogRows(object? parameter)
    {
        List<SteelCatalogSectionRow> selectedRows = GetSelectedSteelCatalogRows(parameter);
        if (selectedRows.Count == 0)
        {
            EditorStatus = "Select one or more steel catalog rows first.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        foreach (SteelCatalogSectionRow row in selectedRows)
            row.Include = true;

        EditorStatus = $"Ticked {selectedRows.Count} selected steel catalog section(s).";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void ImportCheckedSteelSections()
    {
        List<SteelCatalogSectionRow> checkedRows = _allSteelCatalogSectionRows
            .Where(row => row.Include)
            .ToList();

        if (checkedRows.Count == 0)
        {
            EditorStatus = "Check at least one steel catalog section to import.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        var messages = new List<string>();
        int importedCount = 0;
        int failedCount = 0;

        foreach (SteelCatalogSectionRow row in checkedRows)
        {
            string databaseSectionName = (row.SectionName ?? "").Trim();
            string propertyName = string.IsNullOrWhiteSpace(row.PropertyName)
                ? databaseSectionName
                : row.PropertyName.Trim();

            if (databaseSectionName.Length == 0)
            {
                failedCount++;
                messages.Add("Skipped a checked row with a blank database section name.");
                continue;
            }

            SteelSectionImportRequest request = new()
            {
                EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
                Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
                PropertyName = propertyName,
                MaterialName = SelectedSteelMaterialName,
                DatabaseFile = SteelDatabaseFile,
                DatabaseSectionName = databaseSectionName
            };

            SectionPropertyUpdateResult result = IsSap2000Active
                ? _sap2000Service.ImportSteelFrameProperty(request)
                : _etabsService.ImportSteelFrameProperty(request);

            if (result.IsError)
            {
                failedCount++;
                if (!string.IsNullOrWhiteSpace(result.Message))
                    messages.Add(result.Message);
            }
            else
            {
                importedCount++;
                if (!string.IsNullOrWhiteSpace(result.Message))
                    messages.Add(result.Message);
            }

            messages.AddRange(result.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));
        }

        if (importedCount > 0)
            ReadActivePropertyData(false);

        string summary = failedCount == 0
            ? $"Imported {importedCount} steel frame section(s)."
            : $"Imported {importedCount} of {checkedRows.Count} checked steel frame section(s); {failedCount} failed.";

        SetActiveConnectionStatus(failedCount > 0 && importedCount == 0 ? "Update failed" : "Connected");
        EditorStatus = summary;
        ShowMessages(messages, failedCount > 0 ? ValidationSeverity.Critical : ValidationSeverity.Info, summary);
    }

    private List<SteelCatalogSectionRow> GetSelectedSteelCatalogRows(object? parameter)
    {
        var rows = new List<SteelCatalogSectionRow>();

        if (parameter is System.Collections.IEnumerable selectedItems)
        {
            foreach (object? item in selectedItems)
            {
                if (item is SteelCatalogSectionRow row)
                    rows.Add(row);
            }
        }

        if (rows.Count == 0 && SelectedSteelCatalogSectionRow != null)
            rows.Add(SelectedSteelCatalogSectionRow);

        return rows
            .Distinct()
            .ToList();
    }

    private void ApplySteelCatalogSearch()
    {
        SteelCatalogSectionRow? previousSelection = SelectedSteelCatalogSectionRow;
        ReplaceCollection(SteelCatalogSectionRows, _allSteelCatalogSectionRows.Where(MatchesSteelCatalogSearch));

        SelectedSteelCatalogSectionRow = previousSelection != null && SteelCatalogSectionRows.Contains(previousSelection)
            ? previousSelection
            : SteelCatalogSectionRows.FirstOrDefault();

        if (SelectedSteelCatalogSectionRow == null)
        {
            SelectedSteelDatabaseSection = "";
            ImportedSteelPropertyName = "";
        }

        OnPropertyChanged(nameof(SteelCatalogSearchSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool MatchesSteelCatalogSearch(SteelCatalogSectionRow row)
    {
        string searchText = (SteelCatalogSearchText ?? "").Trim();
        if (searchText.Length == 0)
            return true;

        string[] terms = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term =>
            ContainsSearchText(row.SectionName, term) ||
            ContainsSearchText(row.PropertyName, term));
    }

    private static bool ContainsSearchText(string? value, string term)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void AddNewFrameProperty()
    {
        string materialName = SelectedFrameMaterialName.Length > 0
            ? SelectedFrameMaterialName
            : MaterialNames.FirstOrDefault() ?? "";
        var row = new EtabsFramePropertyRow
        {
            Name = GetUniqueName("NEW_FRAME_SECTION", FrameProperties.Select(frame => frame.Name)),
            ShapeType = ToEtabsFrameShapeType(SelectedFrameShapeType),
            MaterialName = materialName,
            DepthMm = FrameDepthMm,
            WidthMm = FrameWidthMm,
            FlangeThicknessMm = FlangeThicknessMm,
            WebThicknessMm = WebThicknessMm,
            SectionSummary = "(new)",
            IsPendingNew = true
        };

        FrameProperties.Add(row);
        SelectedFrameProperty = row;
        EditorStatus = "New frame section row added. Edit dimensions in mm below, then apply to the active CSI model.";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void SaveFrameProperty()
    {
        EtabsFramePropertyRow? selected = SelectedFrameProperty;
        FramePropertyUpdateRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            Name = selected?.Name ?? FramePropertyName,
            ShapeType = selected?.ShapeType ?? SelectedFrameShapeType,
            MaterialName = selected?.MaterialName ?? SelectedFrameMaterialName,
            SectionRole = SelectedSectionRole,
            Depth = MmToM(selected?.DepthMm ?? FrameDepthMm),
            Width = MmToM(selected?.WidthMm ?? FrameWidthMm),
            FlangeThickness = MmToM(selected?.FlangeThicknessMm ?? FlangeThicknessMm),
            WebThickness = MmToM(selected?.WebThicknessMm ?? WebThicknessMm)
        };

        SectionPropertyUpdateResult result = IsSap2000Active
            ? _sap2000Service.UpdateFrameProperty(request)
            : _etabsService.UpdateFrameProperty(request);

        ApplyUpdateResult(result);
    }

    private void DeleteSelectedFrameProperty()
    {
        if (SelectedFrameProperty == null)
        {
            EditorStatus = "Select a frame section row first.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        if (SelectedFrameProperty.IsPendingNew)
        {
            FrameProperties.Remove(SelectedFrameProperty);
            SelectedFrameProperty = FrameProperties.FirstOrDefault();
            EditorStatus = "Removed new unsaved frame section row.";
            OperationStatus = $"Done: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Info, EditorStatus);
            return;
        }

        SectionPropertyDeleteRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            Name = SelectedFrameProperty.Name
        };

        SectionPropertyUpdateResult result = IsSap2000Active
            ? _sap2000Service.DeleteFrameProperty(request)
            : _etabsService.DeleteFrameProperty(request);

        ApplyUpdateResult(result);
    }

    private void AddNewAreaProperty()
    {
        string materialName = SelectedAreaMaterialName.Length > 0
            ? SelectedAreaMaterialName
            : MaterialNames.FirstOrDefault() ?? "";
        var row = new EtabsAreaPropertyRow
        {
            Name = GetUniqueName("NEW_SLAB_WALL", AreaProperties.Select(area => area.Name)),
            AreaType = SelectedAreaType,
            ShellType = SelectedShellType,
            MaterialName = materialName,
            Thickness = MmToM(AreaThicknessMm),
            IsPendingNew = true
        };

        AreaProperties.Add(row);
        SelectedAreaProperty = row;
        EditorStatus = "New slab/wall row added. Edit thickness in mm below, then apply to the active CSI model.";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void SaveAreaProperty()
    {
        EtabsAreaPropertyRow? selected = SelectedAreaProperty;
        AreaPropertyUpdateRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            Name = selected?.Name ?? AreaPropertyName,
            AreaType = selected?.AreaType ?? SelectedAreaType,
            SlabType = SelectedSlabType,
            ShellType = selected?.ShellType ?? SelectedShellType,
            MaterialName = selected?.MaterialName ?? SelectedAreaMaterialName,
            Thickness = selected?.Thickness ?? MmToM(AreaThicknessMm)
        };

        SectionPropertyUpdateResult result = IsSap2000Active
            ? _sap2000Service.UpdateAreaProperty(request)
            : _etabsService.UpdateAreaProperty(request);

        ApplyUpdateResult(result);
    }

    private void DeleteSelectedAreaProperty()
    {
        if (SelectedAreaProperty == null)
        {
            EditorStatus = "Select a slab/wall row first.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        if (SelectedAreaProperty.IsPendingNew)
        {
            AreaProperties.Remove(SelectedAreaProperty);
            SelectedAreaProperty = AreaProperties.FirstOrDefault();
            EditorStatus = "Removed new unsaved slab/wall row.";
            OperationStatus = $"Done: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Info, EditorStatus);
            return;
        }

        SectionPropertyDeleteRequest request = new()
        {
            EtabsInstanceId = IsSap2000Active ? null : SelectedEtabsInstanceId,
            Sap2000InstanceId = IsSap2000Active ? SelectedSap2000InstanceId : null,
            Name = SelectedAreaProperty.Name
        };

        SectionPropertyUpdateResult result = IsSap2000Active
            ? _sap2000Service.DeleteAreaProperty(request)
            : _etabsService.DeleteAreaProperty(request);

        ApplyUpdateResult(result);
    }

    private void LoadTaperedBaseSection()
    {
        if (IsSap2000Active)
        {
            ShowTaperedEtabsOnlyMessage("Load an ETABS property set before using tapered steel tools.");
            return;
        }

        TaperedSteelSelectionResult result = _etabsService.ReadTaperedSteelBaseSection(new TaperedSteelBaseSectionRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            SectionName = SelectedTaperedBaseSectionName
        });

        if (!result.IsError && result.Selection != null)
        {
            _taperedSelection = result.Selection;
            SelectedTaperedBaseSectionName = result.Selection.BaseSectionName;
            if (result.Selection.BaseGeometry.SectionKind == TaperedBaseSectionKind.TubeSection &&
                ToTaperedSectionType(SelectedTaperedType) is not (TaperedSectionType.TubeSection or TaperedSectionType.USection))
            {
                SelectedTaperedType = "Tapered tube/box";
            }
            else if (result.Selection.BaseGeometry.SectionKind == TaperedBaseSectionKind.ISection &&
                ToTaperedSectionType(SelectedTaperedType) is not (TaperedSectionType.ISection or TaperedSectionType.TSection))
            {
                SelectedTaperedType = "Tapered I-section";
            }

            ReplaceCollection(TaperedSelectedFrames, []);
            ReplaceCollection(TaperedStations, []);
            CurrentTaperedPreview = null;
            TaperedPreviewReport = FormatTaperedSelection(result.Selection);
            OnPropertyChanged(nameof(TaperedSelectedMembersSummary));
            OnPropertyChanged(nameof(TaperedBaseGeometrySummary));
        }
        else
        {
            _taperedSelection = null;
            ReplaceCollection(TaperedSelectedFrames, []);
            ReplaceCollection(TaperedStations, []);
            CurrentTaperedPreview = null;
            TaperedPreviewReport = result.Message;
            OnPropertyChanged(nameof(TaperedSelectedMembersSummary));
            OnPropertyChanged(nameof(TaperedBaseGeometrySummary));
        }

        ConnectionStatus = result.IsError ? "Base section failed" : "Connected";
        EditorStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void PreviewTaperedSection()
    {
        if (IsSap2000Active)
        {
            ShowTaperedEtabsOnlyMessage("Tapered steel preview is currently ETABS-only.");
            return;
        }

        TaperedSteelApplyResult result = _etabsService.PreviewTaperedSteelSection(BuildTaperedSteelApplyRequest());
        ApplyTaperedSteelResult(result, assignResult: false);
    }

    private void CreateTaperedSection()
    {
        if (IsSap2000Active)
        {
            ShowTaperedEtabsOnlyMessage("Tapered steel creation is currently ETABS-only.");
            return;
        }

        TaperedSteelApplyResult result = _etabsService.CreateTaperedSteelSection(BuildTaperedSteelApplyRequest());
        ApplyTaperedSteelResult(result, assignResult: false);

        if (!result.IsError)
            ReadEtabsData(false);
    }

    private void AssignTaperedSection()
    {
        if (IsSap2000Active)
        {
            ShowTaperedEtabsOnlyMessage("Tapered steel assignment is currently ETABS-only.");
            return;
        }

        TaperedSteelApplyResult result = _etabsService.AssignTaperedSteelSectionToSelectedFrames(BuildTaperedSteelApplyRequest());
        ApplyTaperedSteelResult(result, assignResult: true);
    }

    private void ShowTaperedEtabsOnlyMessage(string message)
    {
        EditorStatus = message;
        OperationStatus = $"Failed: {message}";
        ShowMessages([], ValidationSeverity.Critical, message);
    }

    private TaperedSteelApplyRequest BuildTaperedSteelApplyRequest()
    {
        if (_taperedSelection == null)
            throw new InvalidOperationException("Load a base steel section before previewing, creating, or assigning a tapered steel section.");

        return new TaperedSteelApplyRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Selection = _taperedSelection,
            TipDepthM = MmToM(TaperedTipDepthMm),
            TipEnd = ToTaperedTipEnd(SelectedTaperedTipEnd),
            TaperType = ToTaperedSectionType(SelectedTaperedType),
            ReferenceLine = ToTaperedReferenceLine(SelectedTaperedReferenceLine),
            FullMemberLength = true,
            StationCount = TaperedStationCount + 1
        };
    }

    private void ApplyTaperedSteelResult(TaperedSteelApplyResult result, bool assignResult)
    {
        ReplaceCollection(TaperedStations, result.Preview?.Stations ?? []);
        CurrentTaperedPreview = result.Preview;
        TaperedPreviewReport = FormatTaperedResult(result, assignResult);

        IEnumerable<string> warnings = (result.Preview?.Warnings ?? [])
            .Concat(result.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        ConnectionStatus = result.IsError ? (assignResult ? "Assignment failed" : "Preview failed") : "Connected";
        EditorStatus = result.Message;
        ShowMessages(warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private static string FormatTaperedSelection(TaperedSteelSelection selection)
    {
        TaperedSteelSectionGeometry geometry = selection.BaseGeometry;
        string sectionType = geometry.SectionKind == TaperedBaseSectionKind.TubeSection ? "Steel Tube/Box" : "Steel I";
        return string.Join(Environment.NewLine,
            $"Base section: {selection.BaseSectionName}",
            $"Section type: {sectionType}",
            $"Material: {selection.MaterialName}",
            FormatTaperedGeometrySummary(geometry),
            "Ready to preview/create tapered section.");
    }

    private static string FormatTaperedGeometrySummary(TaperedSteelSectionGeometry geometry)
    {
        if (geometry.SectionKind == TaperedBaseSectionKind.TubeSection)
        {
            return $"Steel Tube/Box: depth {geometry.DepthM * 1000.0:0.#} mm, width {geometry.TopFlangeWidthM * 1000.0:0.#} mm, top/bottom wall {geometry.TopFlangeThicknessM * 1000.0:0.#} mm, side wall {geometry.WebThicknessM * 1000.0:0.#} mm.";
        }

        return $"Steel I: depth {geometry.DepthM * 1000.0:0.#} mm, top flange {geometry.TopFlangeWidthM * 1000.0:0.#} x {geometry.TopFlangeThicknessM * 1000.0:0.#} mm, web {geometry.WebThicknessM * 1000.0:0.#} mm, bottom flange {geometry.BottomFlangeWidthM * 1000.0:0.#} x {geometry.BottomFlangeThicknessM * 1000.0:0.#} mm.";
    }

    private static string FormatTaperedResult(TaperedSteelApplyResult result, bool assignResult)
    {
        if (result.Preview == null)
            return result.Message;

        TaperedSteelGenerationPreview preview = result.Preview;
        var lines = new List<string>
        {
            $"Base section: {preview.BaseSectionName}",
            $"Original depth: {preview.OriginalDepthM * 1000.0:0.#} mm",
            $"Tip end: {FormatTaperedTipEnd(preview.TipEnd)}",
            $"Tip remaining depth: {preview.TipDepthM * 1000.0:0.#} mm",
            $"Taper type: {FormatTaperedSectionType(preview.TaperType)}",
            $"Reference line: {FormatTaperedReferenceLine(preview.ReferenceLine)}",
            $"Generated nonprismatic section: {preview.NonPrismaticSectionName}"
        };

        if (preview.Stations.Count > 0)
        {
            lines.Add("Generated stations:");
            lines.AddRange(preview.Stations.Select(station =>
                $"  {station.PositionRatio * 100.0:0.#}%: {station.DepthM * 1000.0:0.#} mm, {station.SectionName}"));
        }

        if (assignResult && result.CreatedOrReusedSections.Count > 0)
        {
            lines.Add("Created / updated sections:");
            lines.AddRange(result.CreatedOrReusedSections.Select(name => $"  {name}"));
        }

        if (assignResult && result.AssignedFrameNames.Count > 0)
        {
            lines.Add("Assigned frames:");
            lines.AddRange(result.AssignedFrameNames.Select(name => $"  {name}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ApplyUpdateResult(SectionPropertyUpdateResult result)
    {
        if (!result.IsError)
            ReadActivePropertyData(false);

        SetActiveConnectionStatus(result.IsError ? "Update failed" : "Connected");
        EditorStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private async void ReloadSteelCatalogAfterSelectionChange()
    {
        if (!_hasLoadedSteelCatalog || IsBusy)
            return;

        await RunBusyCommandAsync("Loading steel database sections...", LoadSteelCatalog);
    }

    private void EnsureActiveModelPropertiesLoadedForSteelCatalog()
    {
        if (MaterialNames.Count > 0 && !string.IsNullOrWhiteSpace(SelectedSteelMaterialName))
            return;

        ReadActivePropertyData(false);
    }

    private void ReadActivePropertyData(bool showMessages)
    {
        if (IsSap2000Active)
            ReadSap2000Data();
        else
            ReadEtabsData(showMessages);
    }

    private void SetActiveConnectionStatus(string status)
    {
        if (IsSap2000Active)
            Sap2000ConnectionStatus = status;
        else
            ConnectionStatus = status;
    }

    private void PickMaterialSelections(string previousMaterial, string previousAreaMaterial, string previousSteelMaterial)
    {
        string firstConcrete = Materials.FirstOrDefault(row => string.Equals(row.MaterialType, "Concrete", StringComparison.OrdinalIgnoreCase))?.Name ?? "";
        string firstSteel = Materials.FirstOrDefault(row => string.Equals(row.MaterialType, "Steel", StringComparison.OrdinalIgnoreCase))?.Name ?? "";
        string firstMaterial = MaterialNames.FirstOrDefault() ?? "";

        SelectedFrameMaterialName = MaterialNames.FirstOrDefault(name => string.Equals(name, previousMaterial, StringComparison.OrdinalIgnoreCase)) ??
            (firstConcrete.Length > 0 ? firstConcrete : firstMaterial);

        SelectedAreaMaterialName = MaterialNames.FirstOrDefault(name => string.Equals(name, previousAreaMaterial, StringComparison.OrdinalIgnoreCase)) ??
            (firstConcrete.Length > 0 ? firstConcrete : firstMaterial);

        SelectedSteelMaterialName = MaterialNames.FirstOrDefault(name => string.Equals(name, previousSteelMaterial, StringComparison.OrdinalIgnoreCase)) ??
            (firstSteel.Length > 0 ? firstSteel : firstMaterial);

        if (SelectedFrameMaterialName.Length == 0)
            SelectedFrameMaterialName = firstMaterial;
        if (SelectedAreaMaterialName.Length == 0)
            SelectedAreaMaterialName = firstMaterial;
        if (SelectedSteelMaterialName.Length == 0)
            SelectedSteelMaterialName = firstMaterial;
    }

    private void ShowMessages(IEnumerable<string> warnings, ValidationSeverity summarySeverity, string summary)
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

        ReplaceCollection(Messages, issues);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }

    private static TaperedTipEnd ToTaperedTipEnd(string? value)
    {
        return string.Equals(value, "I-End", StringComparison.OrdinalIgnoreCase)
            ? TaperedTipEnd.IEnd
            : TaperedTipEnd.JEnd;
    }

    private static TaperedSectionType ToTaperedSectionType(string? value)
    {
        string text = value ?? "";
        if (text.Contains("U-section", StringComparison.OrdinalIgnoreCase))
            return TaperedSectionType.USection;
        if (text.Contains("tube", StringComparison.OrdinalIgnoreCase) || text.Contains("box", StringComparison.OrdinalIgnoreCase))
            return TaperedSectionType.TubeSection;
        if (text.Contains("T-section", StringComparison.OrdinalIgnoreCase))
            return TaperedSectionType.TSection;

        return TaperedSectionType.ISection;
    }

    private static TaperedReferenceLine ToTaperedReferenceLine(string? value)
    {
        string normalized = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray());
        return normalized switch
        {
            "KeepCentroidLineStraight" => TaperedReferenceLine.KeepCentroidLineStraight,
            "KeepBottomFlangeStraight" => TaperedReferenceLine.KeepBottomFlangeStraight,
            _ => TaperedReferenceLine.KeepTopFlangeStraight
        };
    }

    private static string FormatTaperedTipEnd(TaperedTipEnd value)
    {
        return value == TaperedTipEnd.IEnd ? "I-End" : "J-End";
    }

    private static string FormatTaperedSectionType(TaperedSectionType value)
    {
        return value switch
        {
            TaperedSectionType.TSection => "Tapered T-section, bottom flange removed",
            TaperedSectionType.TubeSection => "Tapered tube/box, closed section",
            TaperedSectionType.USection => "Tapered U-section, bottom flange removed",
            _ => "Tapered I-section, both flanges remain"
        };
    }

    private static string FormatTaperedReferenceLine(TaperedReferenceLine value)
    {
        return value switch
        {
            TaperedReferenceLine.KeepCentroidLineStraight => "Keep centroid line straight",
            TaperedReferenceLine.KeepBottomFlangeStraight => "Keep bottom flange straight",
            _ => "Keep top flange straight"
        };
    }

    private static string ToFrameShapeLabel(string shapeType)
    {
        string normalized = new string((shapeType ?? "").Where(char.IsLetterOrDigit).ToArray());
        return normalized switch
        {
            "ConcreteCircular" or "Circle" or "Circular" => "Concrete Circular",
            "SteelI" or "I" or "ISection" => "Steel I",
            "SteelChannel" or "Channel" => "Steel Channel",
            "SteelTube" or "Box" or "Tube" => "Steel Tube",
            "SteelPipe" or "Pipe" => "Steel Pipe",
            _ => "Concrete Rectangular"
        };
    }

    private static string ToEtabsFrameShapeType(string shapeType)
    {
        string normalized = new string((shapeType ?? "").Where(char.IsLetterOrDigit).ToArray());
        return normalized switch
        {
            "ConcreteCircular" or "Circle" or "Circular" => "Circle",
            "SteelI" or "I" or "ISection" => "I",
            "SteelChannel" or "Channel" => "Channel",
            "SteelTube" or "Tube" or "Box" => "Box",
            "SteelPipe" or "Pipe" => "Pipe",
            _ => "Rectangular"
        };
    }

    private static string GetUniqueName(string baseName, IEnumerable<string> existingNames)
    {
        HashSet<string> names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
            return baseName;

        for (int index = 1; index < 10000; index++)
        {
            string candidate = $"{baseName}_{index}";
            if (!names.Contains(candidate))
                return candidate;
        }

        return $"{baseName}_{DateTime.Now:HHmmss}";
    }

    private static double MmToM(double value)
    {
        return value / MillimetresPerMetre;
    }
}
