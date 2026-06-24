using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TrussModelling.Models;
using TrussModelling.Services;

namespace TrussModelling.ViewModels;

public sealed class PlateGirderViewModel : ObservableObject
{
    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly PlateGirderGenerator _generator = new();
    private readonly PlateGirderValidator _validator = new();
    private readonly PlateGirderAnalysisService _analysisService = new();
    private bool _etabsDataLoaded;
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _connectionStatus = "Not connected";
    private string _plateGirderStatus = "Preview generated";
    private string _plateGirderId = "PG01";
    private double _originX;
    private double _originY;
    private double _originZ;
    private double _length = 12.0;
    private double _depth = 1.8;
    private double _flangeWidth = 0.45;
    private double _webThickness = 0.012;
    private double _flangeThickness = 0.02;
    private double _stiffenerThickness = 0.012;
    private double _webSteelYieldStrengthMpa = 355.0;
    private double _flangeSteelYieldStrengthMpa = 355.0;
    private double _stiffenerSteelYieldStrengthMpa = 355.0;
    private double _elasticModulusGpa = 200.0;
    private double _analysisUniformLoadKnPerM = 30.0;
    private bool _applyTopFlangeAreaLoad = true;
    private string _selectedLoadPattern = "";
    private int _lengthDivisions = 24;
    private int _depthDivisions = 8;
    private int _flangeWidthDivisions = 2;
    private bool _generateTopFlange = true;
    private bool _generateBottomFlange = true;
    private bool _hasWebOpening = true;
    private double _openingCenterX = 6.0;
    private double _openingCenterZ = 0.9;
    private double _openingWidth = 1.5;
    private double _openingHeight = 0.7;
    private bool _strengthenOpening = true;
    private bool _strengthenOpeningTop = true;
    private bool _strengthenOpeningBottom = true;
    private bool _strengthenOpeningLeft = true;
    private bool _strengthenOpeningRight = true;
    private double _openingStiffenerWidth = 0.15;
    private double _openingStiffenerExtension;
    private bool _updateExistingGroup = true;
    private string _selectedWebShellProperty = "";
    private string _selectedFlangeShellProperty = "";
    private string _selectedStiffenerShellProperty = "";
    private PlateGirderOpening? _selectedOpening;
    private ParametricPlateGirderModel _currentModel = new();
    private PlateGirderAnalysisResult _analysisResult = new();

    public PlateGirderViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ValidateCommand = new RelayCommand(_ => ValidateCurrentModel(true));
        DrawToEtabsCommand = new RelayCommand(_ => DrawToEtabs());
        AddOpeningCommand = new RelayCommand(_ => AddOpening());
        RemoveOpeningCommand = new RelayCommand(_ => RemoveSelectedOpening(), _ => SelectedOpening != null && Openings.Count > 1);
        Openings.CollectionChanged += OpeningsChanged;
        Openings.Add(CreateOpening("OP01", _openingCenterX, _openingCenterZ));
        SelectedOpening = Openings.FirstOrDefault();
        RegeneratePreview();
    }

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<PlateGirderOpening> Openings { get; } = [];
    public ObservableCollection<string> ShellProperties { get; } = [];
    public ObservableCollection<PlateGirderShellPropertyDefinition> ShellPropertyDefinitions { get; } = [];
    public ObservableCollection<string> LoadPatterns { get; } = [];
    public ObservableCollection<string> Stories { get; } = [];
    public ObservableCollection<string> Groups { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];
    public ObservableCollection<PlateGirderSectionResult> SectionResults { get; } = [];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand DrawToEtabsCommand { get; }
    public ICommand AddOpeningCommand { get; }
    public ICommand RemoveOpeningCommand { get; }

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

    public PlateGirderOpening? SelectedOpening
    {
        get => _selectedOpening;
        set
        {
            if (SetProperty(ref _selectedOpening, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string PlateGirderStatus
    {
        get => _plateGirderStatus;
        set => SetProperty(ref _plateGirderStatus, value);
    }

    public string PlateGirderId
    {
        get => _plateGirderId;
        set
        {
            if (SetProperty(ref _plateGirderId, value ?? ""))
                RegeneratePreview();
        }
    }

    public double OriginX { get => _originX; set => SetFiniteAndRegenerate(ref _originX, value); }
    public double OriginY { get => _originY; set => SetFiniteAndRegenerate(ref _originY, value); }
    public double OriginZ { get => _originZ; set => SetFiniteAndRegenerate(ref _originZ, value); }

    public double Length
    {
        get => _length;
        set => SetPositiveAndRegenerate(ref _length, value, 12.0);
    }

    public double Depth
    {
        get => _depth;
        set => SetPositiveAndRegenerate(ref _depth, value, 1.8);
    }

    public double FlangeWidth
    {
        get => _flangeWidth;
        set => SetPositiveAndRegenerate(ref _flangeWidth, value, 0.45);
    }

    public double WebThickness
    {
        get => _webThickness;
        set => SetPositiveAndRegenerate(ref _webThickness, value, 0.012);
    }

    public double FlangeThickness
    {
        get => _flangeThickness;
        set => SetPositiveAndRegenerate(ref _flangeThickness, value, 0.02);
    }

    public double StiffenerThickness
    {
        get => _stiffenerThickness;
        set => SetPositiveAndRegenerate(ref _stiffenerThickness, value, 0.012);
    }

    public double WebSteelYieldStrengthMpa { get => _webSteelYieldStrengthMpa; set => SetPositiveAndRegenerate(ref _webSteelYieldStrengthMpa, value, 355.0); }
    public double FlangeSteelYieldStrengthMpa { get => _flangeSteelYieldStrengthMpa; set => SetPositiveAndRegenerate(ref _flangeSteelYieldStrengthMpa, value, 355.0); }
    public double StiffenerSteelYieldStrengthMpa { get => _stiffenerSteelYieldStrengthMpa; set => SetPositiveAndRegenerate(ref _stiffenerSteelYieldStrengthMpa, value, 355.0); }

    public double ElasticModulusGpa
    {
        get => _elasticModulusGpa;
        set => SetPositiveAndRegenerate(ref _elasticModulusGpa, value, 200.0);
    }

    public double AnalysisUniformLoadKnPerM
    {
        get => _analysisUniformLoadKnPerM;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.0, value) : 30.0;
            if (SetProperty(ref _analysisUniformLoadKnPerM, next))
                RegeneratePreview();
        }
    }

    public bool ApplyTopFlangeAreaLoad
    {
        get => _applyTopFlangeAreaLoad;
        set
        {
            if (SetProperty(ref _applyTopFlangeAreaLoad, value))
                RegeneratePreview();
        }
    }

    public string SelectedLoadPattern
    {
        get => _selectedLoadPattern;
        set
        {
            if (SetProperty(ref _selectedLoadPattern, value ?? ""))
                RegeneratePreview();
        }
    }

    public int LengthDivisions
    {
        get => _lengthDivisions;
        set
        {
            int next = Math.Clamp(value, 1, 500);
            if (SetProperty(ref _lengthDivisions, next))
                RegeneratePreview();
        }
    }

    public int DepthDivisions
    {
        get => _depthDivisions;
        set
        {
            int next = Math.Clamp(value, 1, 300);
            if (SetProperty(ref _depthDivisions, next))
                RegeneratePreview();
        }
    }

    public int FlangeWidthDivisions
    {
        get => _flangeWidthDivisions;
        set
        {
            int next = Math.Clamp(value, 1, 20);
            if (SetProperty(ref _flangeWidthDivisions, next))
                RegeneratePreview();
        }
    }

    public bool GenerateTopFlange
    {
        get => _generateTopFlange;
        set
        {
            if (SetProperty(ref _generateTopFlange, value))
                RegeneratePreview();
        }
    }

    public bool GenerateBottomFlange
    {
        get => _generateBottomFlange;
        set
        {
            if (SetProperty(ref _generateBottomFlange, value))
                RegeneratePreview();
        }
    }

    public bool HasWebOpening
    {
        get => _hasWebOpening;
        set
        {
            if (SetProperty(ref _hasWebOpening, value))
            {
                OnPropertyChanged(nameof(OpeningInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public Visibility OpeningInputVisibility => HasWebOpening ? Visibility.Visible : Visibility.Collapsed;

    public double OpeningCenterX { get => _openingCenterX; set => SetFiniteAndRegenerate(ref _openingCenterX, value); }
    public double OpeningCenterZ { get => _openingCenterZ; set => SetFiniteAndRegenerate(ref _openingCenterZ, value); }
    public double OpeningWidth { get => _openingWidth; set => SetPositiveAndRegenerate(ref _openingWidth, value, 1.5); }
    public double OpeningHeight { get => _openingHeight; set => SetPositiveAndRegenerate(ref _openingHeight, value, 0.7); }

    public bool StrengthenOpening
    {
        get => _strengthenOpening;
        set
        {
            if (SetProperty(ref _strengthenOpening, value))
            {
                OnPropertyChanged(nameof(StiffenerInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public Visibility StiffenerInputVisibility => HasWebOpening && StrengthenOpening ? Visibility.Visible : Visibility.Collapsed;

    public bool StrengthenOpeningTop { get => _strengthenOpeningTop; set => SetBoolAndRegenerate(ref _strengthenOpeningTop, value); }
    public bool StrengthenOpeningBottom { get => _strengthenOpeningBottom; set => SetBoolAndRegenerate(ref _strengthenOpeningBottom, value); }
    public bool StrengthenOpeningLeft { get => _strengthenOpeningLeft; set => SetBoolAndRegenerate(ref _strengthenOpeningLeft, value); }
    public bool StrengthenOpeningRight { get => _strengthenOpeningRight; set => SetBoolAndRegenerate(ref _strengthenOpeningRight, value); }
    public double OpeningStiffenerWidth { get => _openingStiffenerWidth; set => SetPositiveAndRegenerate(ref _openingStiffenerWidth, value, 0.15); }
    public double OpeningStiffenerExtension
    {
        get => _openingStiffenerExtension;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;
            if (SetProperty(ref _openingStiffenerExtension, next))
                RegeneratePreview();
        }
    }

    public bool UpdateExistingGroup
    {
        get => _updateExistingGroup;
        set => SetProperty(ref _updateExistingGroup, value);
    }

    public string SelectedWebShellProperty
    {
        get => _selectedWebShellProperty;
        set
        {
            if (SetProperty(ref _selectedWebShellProperty, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedFlangeShellProperty
    {
        get => _selectedFlangeShellProperty;
        set
        {
            if (SetProperty(ref _selectedFlangeShellProperty, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedStiffenerShellProperty
    {
        get => _selectedStiffenerShellProperty;
        set
        {
            if (SetProperty(ref _selectedStiffenerShellProperty, value ?? ""))
                RegeneratePreview();
        }
    }

    public ParametricPlateGirderModel CurrentModel
    {
        get => _currentModel;
        private set
        {
            if (SetProperty(ref _currentModel, value))
            {
                OnPropertyChanged(nameof(GeneratedCountDisplay));
                OnPropertyChanged(nameof(GroupDisplay));
                OnPropertyChanged(nameof(AreaLoadDisplay));
            }
        }
    }

    public PlateGirderAnalysisResult AnalysisResult
    {
        get => _analysisResult;
        private set
        {
            if (SetProperty(ref _analysisResult, value))
            {
                OnPropertyChanged(nameof(AnalysisSummaryDisplay));
                OnPropertyChanged(nameof(DeflectionDisplay));
                OnPropertyChanged(nameof(UtilizationDisplay));
            }
        }
    }

    public string GeneratedCountDisplay => $"{CurrentModel.Nodes.Count} nodes / {CurrentModel.ShellPanels.Count} quad shells";
    public string GroupDisplay => CurrentModel.GroupName;
    public string AnalysisSummaryDisplay => AnalysisResult.IsError
        ? AnalysisResult.Message
        : $"Min Mcap {AnalysisResult.MinimumMomentCapacityKnM:0.##} kNm / Min Vcap {AnalysisResult.MinimumShearCapacityKn:0.##} kN";
    public string DeflectionDisplay => $"Max deflection {AnalysisResult.MaximumDeflectionMm:0.##} mm";
    public string UtilizationDisplay => $"Max M util {AnalysisResult.Stations.Select(station => station.Utilization).Where(double.IsFinite).DefaultIfEmpty(0.0).Max():0.###} / V util {AnalysisResult.MaximumShearUtilization:0.###}";
    public string AreaLoadDisplay => $"Area load {CurrentModel.TopFlangeAreaLoadKnPerM2:0.###} kN/m2 on top flange";
    public string PlateGirderCalculationNotes =>
        "Section properties are recalculated at every station from extracted shell thickness, material E, and Fy. " +
        "EC3 classification uses epsilon = sqrt(235/fy), outstand flange limits 9/10/14 epsilon, and web bending limits 72/83/124 epsilon. " +
        "Class 1/2 uses plastic moment resistance; Class 3 uses elastic resistance; Class 4 moment resistance is not reported until effective section properties are calculated. " +
        "Deflection uses a 1D variable-section Timoshenko beam stiffness matrix, so it will not exactly match ETABS shell FE deflection around openings, supports, and local plate bending.";

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
        PlateGirderEtabsDataResult result = _etabsService.ListPlateGirderEtabsData(new PlateGirderEtabsDataRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId
        });

        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, result.SelectedInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();
        ReplaceCollection(ShellProperties, result.ShellProperties);
        ReplaceCollection(ShellPropertyDefinitions, result.ShellPropertyDefinitions);
        ReplaceCollection(LoadPatterns, result.LoadPatterns);
        ReplaceCollection(Stories, result.Stories);
        ReplaceCollection(Groups, result.Groups);

        _etabsDataLoaded = !result.IsError && ShellProperties.Count > 0;
        PickDefaultAssignments();
        ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        RegeneratePreview();
    }

    private ParametricValidationResult ValidateCurrentModel(bool requireEtabsConnection)
    {
        ParametricValidationResult validation = _validator.Validate(
            CurrentModel,
            SelectedEtabsInstanceId,
            _etabsDataLoaded,
            ShellProperties,
            LoadPatterns,
            requireEtabsConnection);

        ReplaceCollection(Messages, validation.Issues);
        return validation;
    }

    private void DrawToEtabs()
    {
        ParametricValidationResult validation = ValidateCurrentModel(true);
        if (validation.HasCriticalIssues)
        {
            PlateGirderStatus = "Validation failed";
            return;
        }

        PlateGirderEtabsDrawResult result = _etabsService.DrawOrUpdatePlateGirder(new PlateGirderEtabsDrawRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Model = CurrentModel,
            UpdateExistingGroup = UpdateExistingGroup
        });

        PlateGirderStatus = result.IsError ? "Draw failed" : "Plate girder sent to ETABS";
        ReplaceCollection(Messages, BuildIssues(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message));
    }

    private void RegeneratePreview()
    {
        CurrentModel = _generator.Generate(BuildOptions());
        AnalysisResult = _analysisService.Analyze(CurrentModel);
        ReplaceCollection(SectionResults, AnalysisResult.Stations);
        ValidateCurrentModel(false);
    }

    private PlateGirderOptions BuildOptions()
    {
        PlateGirderShellPropertyDefinition? webShell = FindShellDefinition(SelectedWebShellProperty);
        PlateGirderShellPropertyDefinition? flangeShell = FindShellDefinition(SelectedFlangeShellProperty);
        PlateGirderShellPropertyDefinition? stiffenerShell = FindShellDefinition(SelectedStiffenerShellProperty);
        double webElasticModulus = ShellElasticModulusOrFallback(webShell, ElasticModulusGpa);
        double flangeElasticModulus = ShellElasticModulusOrFallback(flangeShell, ElasticModulusGpa);
        double stiffenerElasticModulus = ShellElasticModulusOrFallback(stiffenerShell, ElasticModulusGpa);

        return new PlateGirderOptions
        {
            PlateGirderId = PlateGirderId,
            OriginX = OriginX,
            OriginY = OriginY,
            OriginZ = OriginZ,
            Length = Length,
            Depth = Depth,
            FlangeWidth = FlangeWidth,
            WebThickness = ShellThicknessOrFallback(webShell, WebThickness),
            FlangeThickness = ShellThicknessOrFallback(flangeShell, FlangeThickness),
            StiffenerThickness = ShellThicknessOrFallback(stiffenerShell, StiffenerThickness),
            WebSteelYieldStrengthMpa = ShellYieldOrFallback(webShell, WebSteelYieldStrengthMpa),
            FlangeSteelYieldStrengthMpa = ShellYieldOrFallback(flangeShell, FlangeSteelYieldStrengthMpa),
            StiffenerSteelYieldStrengthMpa = ShellYieldOrFallback(stiffenerShell, StiffenerSteelYieldStrengthMpa),
            ElasticModulusGpa = webElasticModulus,
            WebElasticModulusGpa = webElasticModulus,
            FlangeElasticModulusGpa = flangeElasticModulus,
            StiffenerElasticModulusGpa = stiffenerElasticModulus,
            AnalysisUniformLoadKnPerM = AnalysisUniformLoadKnPerM,
            ApplyTopFlangeAreaLoad = ApplyTopFlangeAreaLoad,
            LoadPattern = SelectedLoadPattern,
            LengthDivisions = LengthDivisions,
            DepthDivisions = DepthDivisions,
            FlangeWidthDivisions = FlangeWidthDivisions,
            GenerateTopFlange = GenerateTopFlange,
            GenerateBottomFlange = GenerateBottomFlange,
            HasWebOpening = HasWebOpening,
            OpeningCenterX = OpeningCenterX,
            OpeningCenterZ = OpeningCenterZ,
            OpeningWidth = OpeningWidth,
            OpeningHeight = OpeningHeight,
            StrengthenOpening = StrengthenOpening,
            StrengthenOpeningTop = StrengthenOpeningTop,
            StrengthenOpeningBottom = StrengthenOpeningBottom,
            StrengthenOpeningLeft = StrengthenOpeningLeft,
            StrengthenOpeningRight = StrengthenOpeningRight,
            OpeningStiffenerWidth = OpeningStiffenerWidth,
            OpeningStiffenerExtension = OpeningStiffenerExtension,
            WebShellPropertyName = SelectedWebShellProperty,
            FlangeShellPropertyName = SelectedFlangeShellProperty,
            StiffenerShellPropertyName = SelectedStiffenerShellProperty,
            Openings = Openings.Select(opening => opening.Clone()).ToList()
        };
    }

    private void AddOpening()
    {
        int nextIndex = Openings.Count + 1;
        double centerX = Length * nextIndex / Math.Max(Openings.Count + 2, 2);
        var opening = CreateOpening($"OP{nextIndex:00}", Math.Clamp(centerX, OpeningWidth, Math.Max(OpeningWidth, Length - OpeningWidth)), Depth / 2.0);
        Openings.Add(opening);
        SelectedOpening = opening;
    }

    private void RemoveSelectedOpening()
    {
        if (SelectedOpening == null || Openings.Count <= 1)
            return;

        PlateGirderOpening opening = SelectedOpening;
        int index = Openings.IndexOf(opening);
        Openings.Remove(opening);
        SelectedOpening = Openings[Math.Clamp(index, 0, Openings.Count - 1)];
    }

    private PlateGirderOpening CreateOpening(string id, double centerX, double centerZ)
    {
        return new PlateGirderOpening
        {
            Id = id,
            CenterX = centerX,
            CenterZ = centerZ,
            Width = OpeningWidth,
            Height = OpeningHeight,
            Strengthen = StrengthenOpening,
            StrengthenTop = StrengthenOpeningTop,
            StrengthenBottom = StrengthenOpeningBottom,
            StrengthenLeft = StrengthenOpeningLeft,
            StrengthenRight = StrengthenOpeningRight,
            StiffenerOutstand = OpeningStiffenerWidth,
            StiffenerExtension = OpeningStiffenerExtension
        };
    }

    private void OpeningsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (PlateGirderOpening opening in e.OldItems)
                opening.PropertyChanged -= OpeningChanged;
        }

        if (e.NewItems != null)
        {
            foreach (PlateGirderOpening opening in e.NewItems)
                opening.PropertyChanged += OpeningChanged;
        }

        if (Openings.Count > 0)
        {
            PlateGirderOpening first = Openings[0];
            _openingCenterX = first.CenterX;
            _openingCenterZ = first.CenterZ;
            _openingWidth = first.Width;
            _openingHeight = first.Height;
            _strengthenOpening = first.Strengthen;
            _strengthenOpeningTop = first.StrengthenTop;
            _strengthenOpeningBottom = first.StrengthenBottom;
            _strengthenOpeningLeft = first.StrengthenLeft;
            _strengthenOpeningRight = first.StrengthenRight;
            _openingStiffenerWidth = first.StiffenerOutstand;
            _openingStiffenerExtension = first.StiffenerExtension;
        }

        CommandManager.InvalidateRequerySuggested();
        RegeneratePreview();
    }

    private void OpeningChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is PlateGirderOpening opening && ReferenceEquals(opening, Openings.FirstOrDefault()))
        {
            _openingCenterX = opening.CenterX;
            _openingCenterZ = opening.CenterZ;
            _openingWidth = opening.Width;
            _openingHeight = opening.Height;
            _strengthenOpening = opening.Strengthen;
            _strengthenOpeningTop = opening.StrengthenTop;
            _strengthenOpeningBottom = opening.StrengthenBottom;
            _strengthenOpeningLeft = opening.StrengthenLeft;
            _strengthenOpeningRight = opening.StrengthenRight;
            _openingStiffenerWidth = opening.StiffenerOutstand;
            _openingStiffenerExtension = opening.StiffenerExtension;
        }

        RegeneratePreview();
    }

    private void PickDefaultAssignments()
    {
        string firstShell = ShellProperties.FirstOrDefault() ?? "";
        if (SelectedWebShellProperty.Length == 0 || !ShellProperties.Contains(SelectedWebShellProperty))
            SelectedWebShellProperty = firstShell;
        if (SelectedFlangeShellProperty.Length == 0 || !ShellProperties.Contains(SelectedFlangeShellProperty))
            SelectedFlangeShellProperty = firstShell;
        if (SelectedStiffenerShellProperty.Length == 0 || !ShellProperties.Contains(SelectedStiffenerShellProperty))
            SelectedStiffenerShellProperty = firstShell;
        if (SelectedLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedLoadPattern))
            SelectedLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
    }

    private PlateGirderShellPropertyDefinition? FindShellDefinition(string shellPropertyName)
    {
        return ShellPropertyDefinitions.FirstOrDefault(definition =>
            string.Equals(definition.Name, shellPropertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static double ShellThicknessOrFallback(PlateGirderShellPropertyDefinition? definition, double fallback)
    {
        return definition != null && double.IsFinite(definition.Thickness) && definition.Thickness > 0.0
            ? definition.Thickness
            : fallback;
    }

    private static double ShellYieldOrFallback(PlateGirderShellPropertyDefinition? definition, double fallback)
    {
        return definition != null && double.IsFinite(definition.YieldStrengthMpa) && definition.YieldStrengthMpa > 0.0
            ? definition.YieldStrengthMpa
            : fallback;
    }

    private static double ShellElasticModulusOrFallback(PlateGirderShellPropertyDefinition? definition, double fallback)
    {
        return definition != null && double.IsFinite(definition.ElasticModulusGpa) && definition.ElasticModulusGpa > 0.0
            ? definition.ElasticModulusGpa
            : fallback;
    }

    private void SetFiniteAndRegenerate(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, double.IsFinite(value) ? value : 0.0, propertyName))
            RegeneratePreview();
    }

    private void SetPositiveAndRegenerate(ref double field, double value, double fallback, [CallerMemberName] string? propertyName = null)
    {
        double next = double.IsFinite(value) ? Math.Max(0.000001, value) : fallback;
        if (SetProperty(ref field, next, propertyName))
            RegeneratePreview();
    }

    private void SetBoolAndRegenerate(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
            RegeneratePreview();
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

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
