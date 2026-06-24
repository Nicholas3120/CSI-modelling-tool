using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TrussModelling.Models;
using TrussModelling.Services;

namespace TrussModelling.ViewModels;

public sealed class DomeStructureViewModel : ObservableObject
{
    private const string RiseAndCutHeightsModeLabel = "Rise + cut heights";
    private const string PartialHeightTopRadiusModeLabel = "Partial height + top radius";
    private const string EqualHeightSpacingModeLabel = "Equal height";
    private const string EqualRadiusSpacingModeLabel = "Equal radius";
    private const string HybridTopEqualRadiusSpacingModeLabel = "Equal height + refined top";

    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly ParametricDomeGenerator _generator = new();
    private readonly ParametricDomeValidator _validator = new();
    private bool _etabsDataLoaded;
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _connectionStatus = "Not connected";
    private string _domeStatus = "Preview generated";
    private string _domeId = "D01";
    private string _selectedDomeType = DomeType.SphericalCap.ToString();
    private string _selectedShellMeshType = DomeShellMeshType.Triangular.ToString();
    private string _selectedGeometryInputMode = RiseAndCutHeightsModeLabel;
    private string _selectedRingSpacingMode = HybridTopEqualRadiusSpacingModeLabel;
    private double _baseCenterX;
    private double _baseCenterY;
    private double _baseElevationZ;
    private double _baseRadius = 20.0;
    private double _domeRise = 8.0;
    private double _lowerCutHeight;
    private double _upperCutHeight = 8.0;
    private double _partialDomeHeight = 3.0;
    private double _crownRingRadius;
    private int _ringCount = 8;
    private int _segmentCount = 24;
    private double _startAngleDeg;
    private double _endAngleDeg = 360.0;
    private bool _full360 = true;
    private bool _generateShellPanels = true;
    private bool _generateRingFrames = true;
    private bool _generateRadialFrames;
    private bool _generateDiagonalFrames;
    private bool _generateBaseRing = true;
    private bool _generateCrownRing = true;
    private bool _generateSupportsAtBase;
    private bool _updateExistingGroup = true;
    private string _selectedShellProperty = "";
    private string _selectedRingSection = "";
    private string _selectedRadialSection = "";
    private string _selectedDiagonalSection = "";
    private string _selectedBaseRingSection = "";
    private string _selectedCrownRingSection = "";
    private string _selectedLoadPattern = "";
    private ParametricDomeModel _currentDomeModel = new();

    public DomeStructureViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ValidateDomeCommand = new RelayCommand(_ => ValidateCurrentDome(true));
        DrawDomeCommand = new RelayCommand(_ => DrawDomeToEtabs());
        RegeneratePreview();
    }

    public IReadOnlyList<string> DomeTypes { get; } = Enum.GetNames(typeof(DomeType));
    public IReadOnlyList<string> ShellMeshTypes { get; } = Enum.GetNames(typeof(DomeShellMeshType));
    public IReadOnlyList<string> GeometryInputModes { get; } =
    [
        RiseAndCutHeightsModeLabel,
        PartialHeightTopRadiusModeLabel
    ];
    public IReadOnlyList<string> RingSpacingModes { get; } =
    [
        HybridTopEqualRadiusSpacingModeLabel,
        EqualHeightSpacingModeLabel,
        EqualRadiusSpacingModeLabel
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
    public ICommand ValidateDomeCommand { get; }
    public ICommand DrawDomeCommand { get; }

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

    public string DomeStatus
    {
        get => _domeStatus;
        set => SetProperty(ref _domeStatus, value);
    }

    public string DomeId
    {
        get => _domeId;
        set
        {
            if (SetProperty(ref _domeId, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedDomeType
    {
        get => _selectedDomeType;
        set
        {
            if (SetProperty(ref _selectedDomeType, value ?? DomeType.SphericalCap.ToString()))
                RegeneratePreview();
        }
    }

    public string SelectedShellMeshType
    {
        get => _selectedShellMeshType;
        set
        {
            if (SetProperty(ref _selectedShellMeshType, value ?? DomeShellMeshType.Triangular.ToString()))
                RegeneratePreview();
        }
    }

    public string SelectedGeometryInputMode
    {
        get => _selectedGeometryInputMode;
        set
        {
            if (SetProperty(ref _selectedGeometryInputMode, value ?? RiseAndCutHeightsModeLabel))
            {
                OnPropertyChanged(nameof(ManualGeometryInputVisibility));
                OnPropertyChanged(nameof(PartialGeometryInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public Visibility ManualGeometryInputVisibility =>
        IsGeometryInputMode(DomeGeometryInputMode.RiseAndCutHeights)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility PartialGeometryInputVisibility =>
        IsGeometryInputMode(DomeGeometryInputMode.PartialHeightTopRadius)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string SelectedRingSpacingMode
    {
        get => _selectedRingSpacingMode;
        set
        {
            if (SetProperty(ref _selectedRingSpacingMode, value ?? HybridTopEqualRadiusSpacingModeLabel))
                RegeneratePreview();
        }
    }

    public double BaseCenterX
    {
        get => _baseCenterX;
        set => SetFiniteAndRegenerate(ref _baseCenterX, value);
    }

    public double BaseCenterY
    {
        get => _baseCenterY;
        set => SetFiniteAndRegenerate(ref _baseCenterY, value);
    }

    public double BaseElevationZ
    {
        get => _baseElevationZ;
        set => SetFiniteAndRegenerate(ref _baseElevationZ, value);
    }

    public double BaseRadius
    {
        get => _baseRadius;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.001, value) : 20.0;
            if (SetProperty(ref _baseRadius, next))
                RegeneratePreview();
        }
    }

    public double DomeRise
    {
        get => _domeRise;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.001, value) : 8.0;
            if (SetProperty(ref _domeRise, next))
            {
                if (UpperCutHeight > next)
                    UpperCutHeight = next;
                RegeneratePreview();
            }
        }
    }

    public double LowerCutHeight
    {
        get => _lowerCutHeight;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;
            if (SetProperty(ref _lowerCutHeight, next))
                RegeneratePreview();
        }
    }

    public double UpperCutHeight
    {
        get => _upperCutHeight;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.0, value) : DomeRise;
            if (SetProperty(ref _upperCutHeight, next))
                RegeneratePreview();
        }
    }

    public double PartialDomeHeight
    {
        get => _partialDomeHeight;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.001, value) : 3.0;
            if (SetProperty(ref _partialDomeHeight, next))
                RegeneratePreview();
        }
    }

    public double CrownRingRadius
    {
        get => _crownRingRadius;
        set
        {
            double next = double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;
            if (SetProperty(ref _crownRingRadius, next))
                RegeneratePreview();
        }
    }

    public int RingCount
    {
        get => _ringCount;
        set
        {
            int next = Math.Clamp(value, 2, 200);
            if (SetProperty(ref _ringCount, next))
                RegeneratePreview();
        }
    }

    public int SegmentCount
    {
        get => _segmentCount;
        set
        {
            int next = Math.Clamp(value, 1, 360);
            if (SetProperty(ref _segmentCount, next))
                RegeneratePreview();
        }
    }

    public double StartAngleDeg
    {
        get => _startAngleDeg;
        set => SetFiniteAndRegenerate(ref _startAngleDeg, value);
    }

    public double EndAngleDeg
    {
        get => _endAngleDeg;
        set => SetFiniteAndRegenerate(ref _endAngleDeg, value);
    }

    public bool Full360
    {
        get => _full360;
        set
        {
            if (SetProperty(ref _full360, value))
                RegeneratePreview();
        }
    }

    public bool GenerateShellPanels
    {
        get => _generateShellPanels;
        set
        {
            if (SetProperty(ref _generateShellPanels, value))
                RegeneratePreview();
        }
    }

    public bool GenerateRingFrames
    {
        get => _generateRingFrames;
        set
        {
            if (SetProperty(ref _generateRingFrames, value))
                RegeneratePreview();
        }
    }

    public bool GenerateRadialFrames
    {
        get => _generateRadialFrames;
        set
        {
            if (SetProperty(ref _generateRadialFrames, value))
                RegeneratePreview();
        }
    }

    public bool GenerateDiagonalFrames
    {
        get => _generateDiagonalFrames;
        set
        {
            if (SetProperty(ref _generateDiagonalFrames, value))
                RegeneratePreview();
        }
    }

    public bool GenerateBaseRing
    {
        get => _generateBaseRing;
        set
        {
            if (SetProperty(ref _generateBaseRing, value))
                RegeneratePreview();
        }
    }

    public bool GenerateCrownRing
    {
        get => _generateCrownRing;
        set
        {
            if (SetProperty(ref _generateCrownRing, value))
                RegeneratePreview();
        }
    }

    public bool GenerateSupportsAtBase
    {
        get => _generateSupportsAtBase;
        set
        {
            if (SetProperty(ref _generateSupportsAtBase, value))
                RegeneratePreview();
        }
    }

    public bool UpdateExistingGroup
    {
        get => _updateExistingGroup;
        set => SetProperty(ref _updateExistingGroup, value);
    }

    public string SelectedShellProperty
    {
        get => _selectedShellProperty;
        set
        {
            if (SetProperty(ref _selectedShellProperty, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedRingSection
    {
        get => _selectedRingSection;
        set
        {
            if (SetProperty(ref _selectedRingSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedRadialSection
    {
        get => _selectedRadialSection;
        set
        {
            if (SetProperty(ref _selectedRadialSection, value ?? ""))
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

    public string SelectedBaseRingSection
    {
        get => _selectedBaseRingSection;
        set
        {
            if (SetProperty(ref _selectedBaseRingSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedCrownRingSection
    {
        get => _selectedCrownRingSection;
        set
        {
            if (SetProperty(ref _selectedCrownRingSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedLoadPattern
    {
        get => _selectedLoadPattern;
        set => SetProperty(ref _selectedLoadPattern, value ?? "");
    }

    public ParametricDomeModel CurrentDomeModel
    {
        get => _currentDomeModel;
        private set
        {
            if (SetProperty(ref _currentDomeModel, value))
            {
                OnPropertyChanged(nameof(GeneratedDomeCountDisplay));
                OnPropertyChanged(nameof(DomeGroupDisplay));
            }
        }
    }

    public string GeneratedDomeCountDisplay =>
        $"{CurrentDomeModel.Nodes.Count} nodes / {CurrentDomeModel.FrameMembers.Count} frames / {CurrentDomeModel.ShellPanels.Count} shells";

    public string DomeGroupDisplay => CurrentDomeModel.GroupName;

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
        DomeEtabsDataResult result = _etabsService.ListDomeEtabsData(new DomeEtabsDataRequest
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

    private ParametricValidationResult ValidateCurrentDome(bool requireEtabsConnection)
    {
        ParametricValidationResult validation = _validator.Validate(
            CurrentDomeModel,
            SelectedEtabsInstanceId,
            _etabsDataLoaded,
            FrameSections,
            ShellProperties,
            requireEtabsConnection);

        ReplaceCollection(Messages, validation.Issues);
        return validation;
    }

    private void DrawDomeToEtabs()
    {
        ParametricValidationResult validation = ValidateCurrentDome(true);
        if (validation.HasCriticalIssues)
        {
            DomeStatus = "Validation failed";
            return;
        }

        DomeEtabsDrawResult result = _etabsService.DrawOrUpdateDome(new DomeEtabsDrawRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Model = CurrentDomeModel,
            UpdateExistingGroup = UpdateExistingGroup
        });

        DomeStatus = result.IsError ? "Draw failed" : "Dome sent to ETABS";
        ReplaceCollection(Messages, BuildIssues(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message));
    }

    private void RegeneratePreview()
    {
        CurrentDomeModel = _generator.Generate(BuildOptions());
        ValidateCurrentDome(false);
    }

    private ParametricDomeOptions BuildOptions()
    {
        DomeType domeType = Enum.TryParse(SelectedDomeType, out DomeType parsedDomeType)
            ? parsedDomeType
            : DomeType.SphericalCap;
        DomeShellMeshType shellMeshType = Enum.TryParse(SelectedShellMeshType, out DomeShellMeshType parsedShellMeshType)
            ? parsedShellMeshType
            : DomeShellMeshType.Triangular;
        DomeGeometryInputMode geometryInputMode = ParseGeometryInputMode(SelectedGeometryInputMode);
        DomeRingSpacingMode ringSpacingMode = ParseRingSpacingMode(SelectedRingSpacingMode);
        if (geometryInputMode == DomeGeometryInputMode.PartialHeightTopRadius)
            ringSpacingMode = DomeRingSpacingMode.EqualHeight;

        return new ParametricDomeOptions
        {
            DomeId = DomeId,
            DomeType = domeType,
            ShellMeshType = shellMeshType,
            GeometryInputMode = geometryInputMode,
            RingSpacingMode = ringSpacingMode,
            BaseCenterX = BaseCenterX,
            BaseCenterY = BaseCenterY,
            BaseElevationZ = BaseElevationZ,
            BaseRadius = BaseRadius,
            DomeRise = DomeRise,
            LowerCutHeight = LowerCutHeight,
            UpperCutHeight = UpperCutHeight,
            PartialDomeHeight = PartialDomeHeight,
            CrownRingRadius = geometryInputMode == DomeGeometryInputMode.PartialHeightTopRadius ? CrownRingRadius : 0.0,
            RingCount = RingCount,
            SegmentCount = SegmentCount,
            StartAngleDeg = StartAngleDeg,
            EndAngleDeg = EndAngleDeg,
            Full360 = Full360,
            GenerateShellPanels = GenerateShellPanels,
            GenerateRingFrames = GenerateRingFrames,
            GenerateRadialFrames = GenerateRadialFrames,
            GenerateDiagonalFrames = GenerateDiagonalFrames,
            GenerateBaseRing = GenerateBaseRing,
            GenerateCrownRing = GenerateCrownRing,
            GenerateSupportsAtBase = GenerateSupportsAtBase,
            ShellPropertyName = SelectedShellProperty,
            RingSectionName = SelectedRingSection,
            RadialSectionName = SelectedRadialSection,
            DiagonalSectionName = SelectedDiagonalSection,
            BaseRingSectionName = SelectedBaseRingSection,
            CrownRingSectionName = SelectedCrownRingSection
        };
    }

    private void PickDefaultAssignments()
    {
        string firstSection = FrameSections.FirstOrDefault() ?? "";
        string firstShell = ShellProperties.FirstOrDefault() ?? "";
        if (SelectedShellProperty.Length == 0 || !ShellProperties.Contains(SelectedShellProperty))
            SelectedShellProperty = firstShell;
        if (SelectedRingSection.Length == 0 || !FrameSections.Contains(SelectedRingSection))
            SelectedRingSection = firstSection;
        if (SelectedRadialSection.Length == 0 || !FrameSections.Contains(SelectedRadialSection))
            SelectedRadialSection = firstSection;
        if (SelectedDiagonalSection.Length == 0 || !FrameSections.Contains(SelectedDiagonalSection))
            SelectedDiagonalSection = firstSection;
        if (SelectedBaseRingSection.Length == 0 || !FrameSections.Contains(SelectedBaseRingSection))
            SelectedBaseRingSection = firstSection;
        if (SelectedCrownRingSection.Length == 0 || !FrameSections.Contains(SelectedCrownRingSection))
            SelectedCrownRingSection = firstSection;
        if (SelectedLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedLoadPattern))
            SelectedLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
    }

    private void SetFiniteAndRegenerate(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, double.IsFinite(value) ? value : 0.0, propertyName))
            RegeneratePreview();
    }

    private bool IsGeometryInputMode(DomeGeometryInputMode mode)
    {
        return ParseGeometryInputMode(SelectedGeometryInputMode) == mode;
    }

    private static DomeGeometryInputMode ParseGeometryInputMode(string? value)
    {
        return value switch
        {
            PartialHeightTopRadiusModeLabel => DomeGeometryInputMode.PartialHeightTopRadius,
            RiseAndCutHeightsModeLabel => DomeGeometryInputMode.RiseAndCutHeights,
            _ when Enum.TryParse(value, out DomeGeometryInputMode parsedMode) => parsedMode,
            _ => DomeGeometryInputMode.RiseAndCutHeights
        };
    }

    private static DomeRingSpacingMode ParseRingSpacingMode(string? value)
    {
        return value switch
        {
            EqualRadiusSpacingModeLabel => DomeRingSpacingMode.EqualRadius,
            EqualHeightSpacingModeLabel => DomeRingSpacingMode.EqualHeight,
            HybridTopEqualRadiusSpacingModeLabel => DomeRingSpacingMode.HybridTopEqualRadius,
            _ when Enum.TryParse(value, out DomeRingSpacingMode parsedMode) => parsedMode,
            _ => DomeRingSpacingMode.HybridTopEqualRadius
        };
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
