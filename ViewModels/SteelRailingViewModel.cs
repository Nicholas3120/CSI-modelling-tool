using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class SteelRailingViewModel : ObservableObject
{
    private const string FixedBaseLabel = "Fixed base";
    private const string PinnedBaseLabel = "Pinned base";
    private const string GlobalYLoadLabel = "Global Y (normal)";
    private const string GlobalXLoadLabel = "Global X";
    private const string LineLoadLabel = "Line load";
    private const string PointLoadLabel = "Point load";
    private const string TopRailTargetLabel = "Top rail";
    private const string VerticalPostsTargetLabel = "Vertical posts";

    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly SteelRailingGenerator _generator = new();
    private readonly SteelRailingValidator _validator = new();
    private bool _etabsDataLoaded;
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _connectionStatus = "Not connected";
    private string _railingStatus = "Preview generated";
    private string _railingId = "R01";
    private int _spanCount = 3;
    private double _postSpacing = 1.2;
    private double _railingHeight = 1.1;
    private double _baseElevation;
    private double _startX;
    private double _startY;
    private bool _generateMidRails = true;
    private int _midRailCount = 1;
    private double _midRailElevation = 0.55;
    private bool _generateBottomRail;
    private double _bottomRailElevation = 0.1;
    private string _selectedPostSection = "";
    private string _selectedTopRailSection = "";
    private string _selectedMidRailSection = "";
    private string _selectedBottomRailSection = "";
    private string _selectedBaseRestraint = FixedBaseLabel;
    private bool _applyTopRailLoad = true;
    private string _selectedLoadType = LineLoadLabel;
    private string _selectedLoadTarget = TopRailTargetLabel;
    private string _selectedLoadDirection = GlobalYLoadLabel;
    private string _selectedLoadPattern = "";
    private double _horizontalLoadKnPerM = 0.75;
    private double _horizontalPointLoadKn = 1.0;
    private double _pointLoadHeight = 1.0;
    private bool _updateExistingGroup = true;
    private SteelRailingModel _currentRailingModel = new();

    public SteelRailingViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        ValidateRailingCommand = new RelayCommand(_ => ValidateCurrentRailing(true));
        DrawRailingCommand = new RelayCommand(_ => DrawRailingToEtabs());
        RegeneratePreview();
    }

    public IReadOnlyList<string> BaseRestraintTypes { get; } =
    [
        FixedBaseLabel,
        PinnedBaseLabel
    ];

    public IReadOnlyList<string> LoadDirections { get; } =
    [
        GlobalYLoadLabel,
        GlobalXLoadLabel
    ];

    public IReadOnlyList<string> LoadTypes { get; } =
    [
        LineLoadLabel,
        PointLoadLabel
    ];

    public IReadOnlyList<string> LoadTargets { get; } =
    [
        TopRailTargetLabel,
        VerticalPostsTargetLabel
    ];

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<string> FrameSections { get; } = [];
    public ObservableCollection<string> LoadPatterns { get; } = [];
    public ObservableCollection<string> LoadCombinations { get; } = [];
    public ObservableCollection<string> Stories { get; } = [];
    public ObservableCollection<string> Groups { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand ValidateRailingCommand { get; }
    public ICommand DrawRailingCommand { get; }

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

    public string RailingStatus
    {
        get => _railingStatus;
        set => SetProperty(ref _railingStatus, value);
    }

    public string RailingId
    {
        get => _railingId;
        set
        {
            if (SetProperty(ref _railingId, value ?? ""))
                RegeneratePreview();
        }
    }

    public int SpanCount
    {
        get => _spanCount;
        set
        {
            int next = Math.Max(3, value);
            if (SetProperty(ref _spanCount, next))
                RegeneratePreview();
        }
    }

    public double PostSpacing
    {
        get => _postSpacing;
        set => SetPositiveAndRegenerate(ref _postSpacing, value, 1.2);
    }

    public double RailingHeight
    {
        get => _railingHeight;
        set
        {
            double next = double.IsFinite(value) && value > 0.000001 ? value : 1.1;
            if (SetProperty(ref _railingHeight, next))
            {
                double adjustedPointHeight = ClampPointLoadHeight(_pointLoadHeight, next);
                if (Math.Abs(adjustedPointHeight - _pointLoadHeight) > 0.000001)
                {
                    _pointLoadHeight = adjustedPointHeight;
                    OnPropertyChanged(nameof(PointLoadHeight));
                }

                RegeneratePreview();
            }
        }
    }

    public double BaseElevation
    {
        get => _baseElevation;
        set => SetFiniteAndRegenerate(ref _baseElevation, value);
    }

    public double StartX
    {
        get => _startX;
        set => SetFiniteAndRegenerate(ref _startX, value);
    }

    public double StartY
    {
        get => _startY;
        set => SetFiniteAndRegenerate(ref _startY, value);
    }

    public bool GenerateMidRails
    {
        get => _generateMidRails;
        set
        {
            if (SetProperty(ref _generateMidRails, value))
            {
                OnPropertyChanged(nameof(MidRailInputVisibility));
                OnPropertyChanged(nameof(MidRailSingleElevationVisibility));
                RegeneratePreview();
            }
        }
    }

    public int MidRailCount
    {
        get => _midRailCount;
        set
        {
            int next = Math.Max(0, value);
            if (SetProperty(ref _midRailCount, next))
            {
                OnPropertyChanged(nameof(MidRailInputVisibility));
                OnPropertyChanged(nameof(MidRailSingleElevationVisibility));
                RegeneratePreview();
            }
        }
    }

    public double MidRailElevation
    {
        get => _midRailElevation;
        set => SetPositiveAndRegenerate(ref _midRailElevation, value, 0.55);
    }

    public Visibility MidRailInputVisibility =>
        GenerateMidRails && MidRailCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MidRailSingleElevationVisibility =>
        GenerateMidRails && MidRailCount == 1 ? Visibility.Visible : Visibility.Collapsed;

    public bool GenerateBottomRail
    {
        get => _generateBottomRail;
        set
        {
            if (SetProperty(ref _generateBottomRail, value))
            {
                OnPropertyChanged(nameof(BottomRailInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public double BottomRailElevation
    {
        get => _bottomRailElevation;
        set => SetPositiveAndRegenerate(ref _bottomRailElevation, value, 0.1);
    }

    public Visibility BottomRailInputVisibility =>
        GenerateBottomRail ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedPostSection
    {
        get => _selectedPostSection;
        set
        {
            if (SetProperty(ref _selectedPostSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedTopRailSection
    {
        get => _selectedTopRailSection;
        set
        {
            if (SetProperty(ref _selectedTopRailSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedMidRailSection
    {
        get => _selectedMidRailSection;
        set
        {
            if (SetProperty(ref _selectedMidRailSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedBottomRailSection
    {
        get => _selectedBottomRailSection;
        set
        {
            if (SetProperty(ref _selectedBottomRailSection, value ?? ""))
                RegeneratePreview();
        }
    }

    public string SelectedBaseRestraint
    {
        get => _selectedBaseRestraint;
        set
        {
            if (SetProperty(ref _selectedBaseRestraint, NormalizeBaseRestraint(value)))
                RegeneratePreview();
        }
    }

    public bool ApplyTopRailLoad
    {
        get => _applyTopRailLoad;
        set
        {
            if (SetProperty(ref _applyTopRailLoad, value))
            {
                OnPropertyChanged(nameof(LoadInputVisibility));
                OnPropertyChanged(nameof(LineLoadInputVisibility));
                OnPropertyChanged(nameof(PointLoadInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public Visibility LoadInputVisibility =>
        ApplyTopRailLoad ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LineLoadInputVisibility =>
        ApplyTopRailLoad && IsLineLoad ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PointLoadInputVisibility =>
        ApplyTopRailLoad && IsPointLoad ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedLoadType
    {
        get => _selectedLoadType;
        set
        {
            if (SetProperty(ref _selectedLoadType, NormalizeLoadType(value)))
            {
                if (IsPointLoad && !string.Equals(SelectedLoadTarget, VerticalPostsTargetLabel, StringComparison.OrdinalIgnoreCase))
                    SelectedLoadTarget = VerticalPostsTargetLabel;
                OnPropertyChanged(nameof(LineLoadInputVisibility));
                OnPropertyChanged(nameof(PointLoadInputVisibility));
                RegeneratePreview();
            }
        }
    }

    public string SelectedLoadTarget
    {
        get => _selectedLoadTarget;
        set
        {
            if (SetProperty(ref _selectedLoadTarget, NormalizeLoadTarget(value)))
                RegeneratePreview();
        }
    }

    public string SelectedLoadDirection
    {
        get => _selectedLoadDirection;
        set
        {
            if (SetProperty(ref _selectedLoadDirection, NormalizeLoadDirection(value)))
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

    public double HorizontalLoadKnPerM
    {
        get => _horizontalLoadKnPerM;
        set
        {
            if (SetProperty(ref _horizontalLoadKnPerM, double.IsFinite(value) ? value : 0.0))
                RegeneratePreview();
        }
    }

    public double HorizontalPointLoadKn
    {
        get => _horizontalPointLoadKn;
        set
        {
            if (SetProperty(ref _horizontalPointLoadKn, double.IsFinite(value) ? value : 0.0))
                RegeneratePreview();
        }
    }

    public double PointLoadHeight
    {
        get => _pointLoadHeight;
        set
        {
            double next = ClampPointLoadHeight(value, RailingHeight);
            if (SetProperty(ref _pointLoadHeight, next))
                RegeneratePreview();
        }
    }

    public bool UpdateExistingGroup
    {
        get => _updateExistingGroup;
        set => SetProperty(ref _updateExistingGroup, value);
    }

    public SteelRailingModel CurrentRailingModel
    {
        get => _currentRailingModel;
        private set
        {
            if (SetProperty(ref _currentRailingModel, value))
            {
                OnPropertyChanged(nameof(GeneratedRailingCountDisplay));
                OnPropertyChanged(nameof(RailingGroupDisplay));
            }
        }
    }

    public string GeneratedRailingCountDisplay =>
        $"{CurrentRailingModel.Nodes.Count} nodes / {CurrentRailingModel.Members.Count} frames / {CurrentRailingModel.Loads.Count} loads";

    public string RailingGroupDisplay => CurrentRailingModel.GroupName;

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
        SteelRailingEtabsDataResult result = _etabsService.ListSteelRailingEtabsData(new SteelRailingEtabsDataRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId
        });

        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, result.SelectedInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();
        ReplaceCollection(FrameSections, result.FrameSections);
        ReplaceCollection(LoadPatterns, result.LoadPatterns);
        ReplaceCollection(LoadCombinations, result.LoadCombinations);
        ReplaceCollection(Stories, result.Stories);
        ReplaceCollection(Groups, result.Groups);

        _etabsDataLoaded = !result.IsError && FrameSections.Count > 0;
        PickDefaultAssignments();
        ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        RegeneratePreview();
    }

    private ParametricValidationResult ValidateCurrentRailing(bool requireEtabsConnection)
    {
        ParametricValidationResult validation = _validator.Validate(
            CurrentRailingModel,
            SelectedEtabsInstanceId,
            _etabsDataLoaded,
            FrameSections,
            LoadPatterns,
            requireEtabsConnection);

        ReplaceCollection(Messages, validation.Issues);
        return validation;
    }

    private void DrawRailingToEtabs()
    {
        ParametricValidationResult validation = ValidateCurrentRailing(true);
        if (validation.HasCriticalIssues)
        {
            RailingStatus = "Validation failed";
            return;
        }

        SteelRailingDrawResult result = _etabsService.DrawOrUpdateSteelRailing(new SteelRailingDrawRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Model = CurrentRailingModel,
            UpdateExistingGroup = UpdateExistingGroup
        });

        RailingStatus = result.IsError ? "Draw failed" : "Railing sent to ETABS";
        ReplaceCollection(Messages, BuildIssues(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message));
    }

    private void RegeneratePreview()
    {
        CurrentRailingModel = _generator.Generate(BuildOptions());
        ValidateCurrentRailing(false);
    }

    private SteelRailingOptions BuildOptions()
    {
        return new SteelRailingOptions
        {
            RailingId = RailingId,
            SpanCount = SpanCount,
            PostSpacing = PostSpacing,
            RailingHeight = RailingHeight,
            BaseElevation = BaseElevation,
            StartX = StartX,
            StartY = StartY,
            GenerateMidRails = GenerateMidRails,
            MidRailCount = MidRailCount,
            MidRailElevation = MidRailElevation,
            GenerateBottomRail = GenerateBottomRail,
            BottomRailElevation = BottomRailElevation,
            BaseRestraintType = ToBaseRestraintType(SelectedBaseRestraint),
            SectionAssignments = BuildSectionAssignments(),
            ApplyTopRailLoad = ApplyTopRailLoad,
            LoadPattern = SelectedLoadPattern,
            LoadType = ToLoadType(SelectedLoadType),
            LoadTargetGroup = ToLoadTargetGroup(SelectedLoadTarget),
            LoadDirection = ToLoadDirection(SelectedLoadDirection),
            HorizontalLoadKnPerM = HorizontalLoadKnPerM,
            HorizontalPointLoadKn = HorizontalPointLoadKn,
            PointLoadHeight = PointLoadHeight
        };
    }

    private Dictionary<string, string> BuildSectionAssignments()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SteelRailingMemberGroups.Post] = SelectedPostSection,
            [SteelRailingMemberGroups.TopRail] = SelectedTopRailSection,
            [SteelRailingMemberGroups.MidRail] = SelectedMidRailSection,
            [SteelRailingMemberGroups.BottomRail] = SelectedBottomRailSection
        };
    }

    private void PickDefaultAssignments()
    {
        string firstSection = FrameSections.FirstOrDefault() ?? "";
        if (SelectedPostSection.Length == 0 || !FrameSections.Contains(SelectedPostSection))
            SelectedPostSection = firstSection;
        if (SelectedTopRailSection.Length == 0 || !FrameSections.Contains(SelectedTopRailSection))
            SelectedTopRailSection = firstSection;
        if (SelectedMidRailSection.Length == 0 || !FrameSections.Contains(SelectedMidRailSection))
            SelectedMidRailSection = firstSection;
        if (SelectedBottomRailSection.Length == 0 || !FrameSections.Contains(SelectedBottomRailSection))
            SelectedBottomRailSection = firstSection;
        if (SelectedLoadPattern.Length == 0 || !LoadPatterns.Contains(SelectedLoadPattern))
            SelectedLoadPattern = LoadPatterns.FirstOrDefault() ?? "";
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

    private static double ClampPointLoadHeight(double value, double railingHeight)
    {
        double height = double.IsFinite(railingHeight) && railingHeight > 0.001 ? railingHeight : 1.1;
        double fallback = Math.Min(1.0, height * 0.9);
        double next = double.IsFinite(value) && value > 0 ? value : fallback;
        return Math.Clamp(next, 0.001, Math.Max(0.001, height - 0.001));
    }

    private static string NormalizeBaseRestraint(string? value)
    {
        return string.Equals(value, PinnedBaseLabel, StringComparison.OrdinalIgnoreCase)
            ? PinnedBaseLabel
            : FixedBaseLabel;
    }

    private static RailingBaseRestraintType ToBaseRestraintType(string? value)
    {
        return string.Equals(value, PinnedBaseLabel, StringComparison.OrdinalIgnoreCase)
            ? RailingBaseRestraintType.Pinned
            : RailingBaseRestraintType.Fixed;
    }

    private static string NormalizeLoadDirection(string? value)
    {
        return string.Equals(value, GlobalXLoadLabel, StringComparison.OrdinalIgnoreCase)
            ? GlobalXLoadLabel
            : GlobalYLoadLabel;
    }

    private static string NormalizeLoadType(string? value)
    {
        return string.Equals(value, PointLoadLabel, StringComparison.OrdinalIgnoreCase)
            ? PointLoadLabel
            : LineLoadLabel;
    }

    private static RailingLoadType ToLoadType(string? value)
    {
        return string.Equals(value, PointLoadLabel, StringComparison.OrdinalIgnoreCase)
            ? RailingLoadType.PointLoad
            : RailingLoadType.LineLoad;
    }

    private static string NormalizeLoadTarget(string? value)
    {
        return string.Equals(value, VerticalPostsTargetLabel, StringComparison.OrdinalIgnoreCase)
            ? VerticalPostsTargetLabel
            : TopRailTargetLabel;
    }

    private static string ToLoadTargetGroup(string? value)
    {
        return string.Equals(value, VerticalPostsTargetLabel, StringComparison.OrdinalIgnoreCase)
            ? SteelRailingMemberGroups.Post
            : SteelRailingMemberGroups.TopRail;
    }

    private static RailingLoadDirection ToLoadDirection(string? value)
    {
        return string.Equals(value, GlobalXLoadLabel, StringComparison.OrdinalIgnoreCase)
            ? RailingLoadDirection.GlobalX
            : RailingLoadDirection.GlobalY;
    }

    private bool IsLineLoad =>
        string.Equals(SelectedLoadType, LineLoadLabel, StringComparison.OrdinalIgnoreCase);

    private bool IsPointLoad =>
        string.Equals(SelectedLoadType, PointLoadLabel, StringComparison.OrdinalIgnoreCase);

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
