using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TrussModelling.Models;
using TrussModelling.Services;

namespace TrussModelling.ViewModels;

public sealed class WallDrainViewModel : ObservableObject
{
    private const string OneSidedWallLabel = "1-sided retaining wall";
    private const string LWallLabel = "L retaining wall";
    private const string UDrainLabel = "U drain (3-sided)";
    private const string BoxDrainLabel = "Box drain (4-sided)";
    private const string FrameModelLabel = "Frame";
    private const string ShellModelLabel = "Shell";
    private const string CounterfortLabel = "Counterfort";
    private const string ButtressLabel = "Buttress";
    private const string NormalInwardLabel = "Normal / inward";
    private const string GlobalXPositiveLabel = "Global X +";
    private const string GlobalXNegativeLabel = "Global X -";
    private const string EraseAndRedrawLabel = "Erase and redraw";
    private const string AddAsNewLabel = "Add as new";

    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly WallDrainGenerator _generator = new();
    private readonly WallDrainValidator _validator = new();
    private bool _etabsDataLoaded;
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _connectionStatus = "Not connected";
    private string _wallDrainStatus = "Preview generated";
    private string _structureId = "WD01";
    private string _selectedShapeMode = LWallLabel;
    private string _selectedModelingMode = FrameModelLabel;
    private double _lengthY = 1.0;
    private double _originX;
    private double _originY;
    private double _originZ;
    private double _height = 3.0;
    private double _clearWidth = 1.5;
    private double _toeLength = 1.0;
    private double _heelLength = 2.0;
    private double _lengthMeshSize = 1.0;
    private int _heightDivisions = 4;
    private bool _generateBaseSlab = true;
    private bool _generateButtressOrCounterfort;
    private string _selectedButtressType = CounterfortLabel;
    private double _buttressProjection = 1.0;
    private double _buttressSpacing = 2.0;
    private string _selectedWallFrameSection = "";
    private string _selectedSlabFrameSection = "";
    private string _selectedButtressFrameSection = "";
    private string _selectedWallShellProperty = "";
    private string _selectedSlabShellProperty = "";
    private string _selectedButtressShellProperty = "";
    private bool _applyUdl;
    private string _selectedUdlLoadPattern = "";
    private string _selectedUdlDirection = NormalInwardLabel;
    private double _udlPressureKnPerM2 = 10.0;
    private bool _applyTriangularLoad = true;
    private string _selectedTriangularLoadPattern = "";
    private string _selectedTriangularDirection = NormalInwardLabel;
    private double _triangularTopPressureKnPerM2;
    private double _triangularBottomPressureKnPerM2 = 30.0;
    private string _selectedExportMode = EraseAndRedrawLabel;
    private double _addAsNewOffsetX;
    private double _addAsNewOffsetY = 3.0;
    private double _addAsNewOffsetZ;
    private WallDrainModel _currentWallDrainModel = new();

    public WallDrainViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ValidateWallDrainCommand = new RelayCommand(_ => ValidateCurrentWallDrain(true));
        DrawWallDrainCommand = new RelayCommand(_ => DrawWallDrainToEtabs());
        RegeneratePreview();
    }

    public IReadOnlyList<string> ShapeModes { get; } =
    [
        LWallLabel,
        OneSidedWallLabel,
        UDrainLabel,
        BoxDrainLabel
    ];

    public IReadOnlyList<string> ModelingModes { get; } =
    [
        FrameModelLabel,
        ShellModelLabel
    ];

    public IReadOnlyList<string> ButtressTypes { get; } =
    [
        CounterfortLabel,
        ButtressLabel
    ];

    public IReadOnlyList<string> LoadDirections { get; } =
    [
        NormalInwardLabel,
        GlobalXPositiveLabel,
        GlobalXNegativeLabel
    ];

    public IReadOnlyList<string> ExportModes { get; } =
    [
        EraseAndRedrawLabel,
        AddAsNewLabel
    ];

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<string> FrameSections { get; } = [];
    public ObservableCollection<string> ShellProperties { get; } = [];
    public ObservableCollection<string> LoadPatterns { get; } = [];
    public ObservableCollection<string> LoadCombinations { get; } = [];
    public ObservableCollection<string> Stories { get; } = [];
    public ObservableCollection<string> Groups { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand ValidateWallDrainCommand { get; }
    public ICommand DrawWallDrainCommand { get; }

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

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string WallDrainStatus
    {
        get => _wallDrainStatus;
        set => SetProperty(ref _wallDrainStatus, value);
    }

    public string StructureId
    {
        get => _structureId;
        set
        {
            if (SetProperty(ref _structureId, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedShapeMode
    {
        get => _selectedShapeMode;
        set
        {
            if (SetProperty(ref _selectedShapeMode, NormalizeShapeMode(value)))
            {
                OnPropertyChanged(nameof(DrainGeometryVisibility));
                OnPropertyChanged(nameof(RetainingGeometryVisibility));
                OnPropertyChanged(nameof(BaseSlabVisibility));
                OnPropertyChanged(nameof(ButtressInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public string SelectedModelingMode
    {
        get => _selectedModelingMode;
        set
        {
            if (SetProperty(ref _selectedModelingMode, NormalizeModelingMode(value)))
            {
                OnPropertyChanged(nameof(FrameAssignmentVisibility));
                OnPropertyChanged(nameof(ShellAssignmentVisibility));
                RegeneratePreview();
            }
        }
    }

    public double LengthY
    {
        get => _lengthY;
        set => SetPositiveAndRegenerate(ref _lengthY, value, 1.0);
    }

    public double OriginX
    {
        get => _originX;
        set => SetFiniteAndRegenerate(ref _originX, value);
    }

    public double OriginY
    {
        get => _originY;
        set => SetFiniteAndRegenerate(ref _originY, value);
    }

    public double OriginZ
    {
        get => _originZ;
        set => SetFiniteAndRegenerate(ref _originZ, value);
    }

    public double Height
    {
        get => _height;
        set => SetPositiveAndRegenerate(ref _height, value, 3.0);
    }

    public double ClearWidth
    {
        get => _clearWidth;
        set => SetPositiveAndRegenerate(ref _clearWidth, value, 1.5);
    }

    public double ToeLength
    {
        get => _toeLength;
        set => SetNonNegativeAndRegenerate(ref _toeLength, value);
    }

    public double HeelLength
    {
        get => _heelLength;
        set => SetNonNegativeAndRegenerate(ref _heelLength, value);
    }

    public double LengthMeshSize
    {
        get => _lengthMeshSize;
        set => SetPositiveAndRegenerate(ref _lengthMeshSize, value, 1.0);
    }

    public int HeightDivisions
    {
        get => _heightDivisions;
        set
        {
            int next = Math.Clamp(value, 1, 100);
            if (SetProperty(ref _heightDivisions, next))
                RegeneratePreview();
        }
    }

    public bool GenerateBaseSlab
    {
        get => _generateBaseSlab;
        set
        {
            if (SetProperty(ref _generateBaseSlab, value))
                RegeneratePreview();
        }
    }

    public bool GenerateButtressOrCounterfort
    {
        get => _generateButtressOrCounterfort;
        set
        {
            if (SetProperty(ref _generateButtressOrCounterfort, value))
            {
                OnPropertyChanged(nameof(ButtressInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public string SelectedButtressType
    {
        get => _selectedButtressType;
        set
        {
            if (SetProperty(ref _selectedButtressType, NormalizeButtressType(value)))
                RegeneratePreview();
        }
    }

    public double ButtressProjection
    {
        get => _buttressProjection;
        set => SetPositiveAndRegenerate(ref _buttressProjection, value, 1.0);
    }

    public double ButtressSpacing
    {
        get => _buttressSpacing;
        set => SetPositiveAndRegenerate(ref _buttressSpacing, value, 2.0);
    }

    public Visibility DrainGeometryVisibility =>
        IsDrainShape ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RetainingGeometryVisibility =>
        IsDrainShape ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BaseSlabVisibility =>
        ParseShapeMode(SelectedShapeMode) == WallDrainShapeMode.OneSidedWall ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ButtressInputVisibility =>
        !IsDrainShape && GenerateButtressOrCounterfort ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FrameAssignmentVisibility =>
        IsShellModel ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ShellAssignmentVisibility =>
        IsShellModel ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedWallFrameSection
    {
        get => _selectedWallFrameSection;
        set
        {
            if (SetProperty(ref _selectedWallFrameSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedWallShellProperty
    {
        get => _selectedWallShellProperty;
        set
        {
            if (SetProperty(ref _selectedWallShellProperty, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedSlabShellProperty
    {
        get => _selectedSlabShellProperty;
        set
        {
            if (SetProperty(ref _selectedSlabShellProperty, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedButtressShellProperty
    {
        get => _selectedButtressShellProperty;
        set
        {
            if (SetProperty(ref _selectedButtressShellProperty, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedSlabFrameSection
    {
        get => _selectedSlabFrameSection;
        set
        {
            if (SetProperty(ref _selectedSlabFrameSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedButtressFrameSection
    {
        get => _selectedButtressFrameSection;
        set
        {
            if (SetProperty(ref _selectedButtressFrameSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public bool ApplyUdl
    {
        get => _applyUdl;
        set
        {
            if (SetProperty(ref _applyUdl, value))
            {
                OnPropertyChanged(nameof(UdlInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public Visibility UdlInputVisibility =>
        ApplyUdl ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedUdlLoadPattern
    {
        get => _selectedUdlLoadPattern;
        set
        {
            if (SetProperty(ref _selectedUdlLoadPattern, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedUdlDirection
    {
        get => _selectedUdlDirection;
        set
        {
            if (SetProperty(ref _selectedUdlDirection, NormalizeLoadDirection(value)))
                RegeneratePreview();
        }
    }

    public double UdlPressureKnPerM2
    {
        get => _udlPressureKnPerM2;
        set => SetFiniteAndRegenerate(ref _udlPressureKnPerM2, value);
    }

    public bool ApplyTriangularLoad
    {
        get => _applyTriangularLoad;
        set
        {
            if (SetProperty(ref _applyTriangularLoad, value))
            {
                OnPropertyChanged(nameof(TriangularInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public Visibility TriangularInputVisibility =>
        ApplyTriangularLoad ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedTriangularLoadPattern
    {
        get => _selectedTriangularLoadPattern;
        set
        {
            if (SetProperty(ref _selectedTriangularLoadPattern, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedTriangularDirection
    {
        get => _selectedTriangularDirection;
        set
        {
            if (SetProperty(ref _selectedTriangularDirection, NormalizeLoadDirection(value)))
                RegeneratePreview();
        }
    }

    public double TriangularTopPressureKnPerM2
    {
        get => _triangularTopPressureKnPerM2;
        set => SetFiniteAndRegenerate(ref _triangularTopPressureKnPerM2, value);
    }

    public double TriangularBottomPressureKnPerM2
    {
        get => _triangularBottomPressureKnPerM2;
        set => SetFiniteAndRegenerate(ref _triangularBottomPressureKnPerM2, value);
    }

    public string SelectedExportMode
    {
        get => _selectedExportMode;
        set
        {
            if (SetProperty(ref _selectedExportMode, NormalizeExportMode(value)))
                OnPropertyChanged(nameof(AddAsNewOffsetVisibility));
        }
    }

    public Visibility AddAsNewOffsetVisibility =>
        IsAddAsNewMode ? Visibility.Visible : Visibility.Collapsed;

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

    public WallDrainModel CurrentWallDrainModel
    {
        get => _currentWallDrainModel;
        private set
        {
            if (SetProperty(ref _currentWallDrainModel, value))
            {
                OnPropertyChanged(nameof(GeneratedWallDrainCountDisplay));
                OnPropertyChanged(nameof(WallDrainGroupDisplay));
            }
        }
    }

    public string GeneratedWallDrainCountDisplay =>
        $"{CurrentWallDrainModel.Nodes.Count} nodes / {CurrentWallDrainModel.FrameMembers.Count} frames / {CurrentWallDrainModel.ShellPanels.Count} shells / {CurrentWallDrainModel.SurfaceLoads.Count} loads";

    public string WallDrainGroupDisplay => CurrentWallDrainModel.GroupName;

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
        WallDrainEtabsDataResult result = _etabsService.ListWallDrainEtabsData(new WallDrainEtabsDataRequest
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
        PickDefaultAssignments();
        ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        RegeneratePreview();
    }

    private ParametricValidationResult ValidateCurrentWallDrain(bool requireEtabsConnection)
    {
        ParametricValidationResult validation = _validator.Validate(
            CurrentWallDrainModel,
            SelectedEtabsInstanceId,
            _etabsDataLoaded,
            FrameSections,
            ShellProperties,
            LoadPatterns,
            requireEtabsConnection);

        ReplaceCollection(Messages, validation.Issues);
        return validation;
    }

    private void DrawWallDrainToEtabs()
    {
        ParametricValidationResult validation = ValidateCurrentWallDrain(true);
        if (validation.HasCriticalIssues)
        {
            WallDrainStatus = "Validation failed";
            return;
        }

        WallDrainDrawResult result = _etabsService.DrawOrUpdateWallDrain(new WallDrainDrawRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Model = CurrentWallDrainModel,
            UpdateExistingGroup = IsEraseAndRedrawMode,
            AddAsNew = IsAddAsNewMode,
            OffsetX = IsAddAsNewMode ? AddAsNewOffsetX : 0,
            OffsetY = IsAddAsNewMode ? AddAsNewOffsetY : 0,
            OffsetZ = IsAddAsNewMode ? AddAsNewOffsetZ : 0
        });

        WallDrainStatus = result.IsError ? "Draw failed" : "Wall/drain sent to ETABS";
        ReplaceCollection(Messages, BuildIssues(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message));
    }

    private void RegeneratePreview()
    {
        CurrentWallDrainModel = _generator.Generate(BuildOptions());
        ValidateCurrentWallDrain(false);
    }

    private WallDrainOptions BuildOptions()
    {
        return new WallDrainOptions
        {
            StructureId = StructureId,
            ShapeMode = ParseShapeMode(SelectedShapeMode),
            ModelingMode = ParseModelingMode(SelectedModelingMode),
            LengthY = LengthY,
            OriginX = OriginX,
            OriginY = OriginY,
            OriginZ = OriginZ,
            Height = Height,
            ClearWidth = ClearWidth,
            ToeLength = ToeLength,
            HeelLength = HeelLength,
            LengthMeshSize = LengthMeshSize,
            HeightDivisions = HeightDivisions,
            GenerateBaseSlab = GenerateBaseSlab,
            GenerateButtressOrCounterfort = GenerateButtressOrCounterfort,
            UseCounterfort = string.Equals(SelectedButtressType, CounterfortLabel, StringComparison.OrdinalIgnoreCase),
            ButtressProjection = ButtressProjection,
            ButtressSpacing = ButtressSpacing,
            WallFrameSectionName = SelectedWallFrameSection,
            SlabFrameSectionName = SelectedSlabFrameSection,
            ButtressFrameSectionName = SelectedButtressFrameSection,
            WallShellPropertyName = SelectedWallShellProperty,
            SlabShellPropertyName = SelectedSlabShellProperty,
            ButtressShellPropertyName = SelectedButtressShellProperty,
            ApplyUdl = ApplyUdl,
            UdlLoadPattern = SelectedUdlLoadPattern,
            UdlDirection = ParseLoadDirection(SelectedUdlDirection),
            UdlPressureKnPerM2 = UdlPressureKnPerM2,
            ApplyTriangularLoad = ApplyTriangularLoad,
            TriangularLoadPattern = SelectedTriangularLoadPattern,
            TriangularDirection = ParseLoadDirection(SelectedTriangularDirection),
            TriangularTopPressureKnPerM2 = TriangularTopPressureKnPerM2,
            TriangularBottomPressureKnPerM2 = TriangularBottomPressureKnPerM2
        };
    }

    private void PickDefaultAssignments()
    {
        string firstSection = FrameSections.FirstOrDefault() ?? "";
        if (SelectedWallFrameSection.Length == 0 || !FrameSections.Contains(SelectedWallFrameSection))
            SelectedWallFrameSection = firstSection;
        if (SelectedSlabFrameSection.Length == 0 || !FrameSections.Contains(SelectedSlabFrameSection))
            SelectedSlabFrameSection = firstSection;
        if (SelectedButtressFrameSection.Length == 0 || !FrameSections.Contains(SelectedButtressFrameSection))
            SelectedButtressFrameSection = firstSection;
        string firstShellProperty = ShellProperties.FirstOrDefault() ?? "";
        if (SelectedWallShellProperty.Length == 0 || !ShellProperties.Contains(SelectedWallShellProperty))
            SelectedWallShellProperty = firstShellProperty;
        if (SelectedSlabShellProperty.Length == 0 || !ShellProperties.Contains(SelectedSlabShellProperty))
            SelectedSlabShellProperty = firstShellProperty;
        if (SelectedButtressShellProperty.Length == 0 || !ShellProperties.Contains(SelectedButtressShellProperty))
            SelectedButtressShellProperty = firstShellProperty;
        if (SelectedUdlLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedUdlLoadPattern))
            SelectedUdlLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
        if (SelectedTriangularLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedTriangularLoadPattern))
            SelectedTriangularLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
    }

    private void SetFiniteAndRegenerate(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, double.IsFinite(value) ? value : 0.0, propertyName))
            RegeneratePreview();
    }

    private void SetPositiveAndRegenerate(ref double field, double value, double fallback, [CallerMemberName] string? propertyName = null)
    {
        double next = double.IsFinite(value) && value > 0.000001 ? value : fallback;
        if (SetProperty(ref field, next, propertyName))
            RegeneratePreview();
    }

    private void SetNonNegativeAndRegenerate(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        double next = double.IsFinite(value) ? Math.Max(0, value) : 0.0;
        if (SetProperty(ref field, next, propertyName))
            RegeneratePreview();
    }

    private bool IsDrainShape =>
        ParseShapeMode(SelectedShapeMode) is WallDrainShapeMode.UDrain or WallDrainShapeMode.BoxDrain;

    private bool IsShellModel =>
        ParseModelingMode(SelectedModelingMode) == WallDrainModelingMode.Shell;

    private static string NormalizeShapeMode(string? value)
    {
        if (string.Equals(value, OneSidedWallLabel, StringComparison.OrdinalIgnoreCase))
            return OneSidedWallLabel;
        if (string.Equals(value, UDrainLabel, StringComparison.OrdinalIgnoreCase))
            return UDrainLabel;
        if (string.Equals(value, BoxDrainLabel, StringComparison.OrdinalIgnoreCase))
            return BoxDrainLabel;
        return LWallLabel;
    }

    private static WallDrainShapeMode ParseShapeMode(string? value)
    {
        if (string.Equals(value, OneSidedWallLabel, StringComparison.OrdinalIgnoreCase))
            return WallDrainShapeMode.OneSidedWall;
        if (string.Equals(value, UDrainLabel, StringComparison.OrdinalIgnoreCase))
            return WallDrainShapeMode.UDrain;
        if (string.Equals(value, BoxDrainLabel, StringComparison.OrdinalIgnoreCase))
            return WallDrainShapeMode.BoxDrain;
        return WallDrainShapeMode.LWall;
    }

    private static string NormalizeModelingMode(string? value)
    {
        return string.Equals(value, ShellModelLabel, StringComparison.OrdinalIgnoreCase)
            ? ShellModelLabel
            : FrameModelLabel;
    }

    private static WallDrainModelingMode ParseModelingMode(string? value)
    {
        return string.Equals(value, ShellModelLabel, StringComparison.OrdinalIgnoreCase)
            ? WallDrainModelingMode.Shell
            : WallDrainModelingMode.Frame;
    }

    private static string NormalizeButtressType(string? value)
    {
        return string.Equals(value, ButtressLabel, StringComparison.OrdinalIgnoreCase)
            ? ButtressLabel
            : CounterfortLabel;
    }

    private static string NormalizeLoadDirection(string? value)
    {
        if (string.Equals(value, GlobalXPositiveLabel, StringComparison.OrdinalIgnoreCase))
            return GlobalXPositiveLabel;
        if (string.Equals(value, GlobalXNegativeLabel, StringComparison.OrdinalIgnoreCase))
            return GlobalXNegativeLabel;
        return NormalInwardLabel;
    }

    private static WallDrainLoadDirection ParseLoadDirection(string? value)
    {
        if (string.Equals(value, GlobalXPositiveLabel, StringComparison.OrdinalIgnoreCase))
            return WallDrainLoadDirection.GlobalXPositive;
        if (string.Equals(value, GlobalXNegativeLabel, StringComparison.OrdinalIgnoreCase))
            return WallDrainLoadDirection.GlobalXNegative;
        return WallDrainLoadDirection.NormalInward;
    }

    private static string NormalizeExportMode(string? value)
    {
        return string.Equals(value, AddAsNewLabel, StringComparison.OrdinalIgnoreCase)
            ? AddAsNewLabel
            : EraseAndRedrawLabel;
    }

    private bool IsEraseAndRedrawMode =>
        string.Equals(SelectedExportMode, EraseAndRedrawLabel, StringComparison.OrdinalIgnoreCase);

    private bool IsAddAsNewMode =>
        string.Equals(SelectedExportMode, AddAsNewLabel, StringComparison.OrdinalIgnoreCase);

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
