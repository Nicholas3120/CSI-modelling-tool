using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class HydrostaticShellLoadViewModel : ObservableObject
{
    private const string SelectedShellsLabel = "Selected ETABS shell objects";
    private const string EtabsGroupLabel = "ETABS group";
    private const string ShellNameListLabel = "Shell object name list";
    private const string FullWallHeightLabel = "Full wall height";
    private const string WaterTableToBottomLabel = "Water table to wall bottom";
    private const string CustomTopBottomLabel = "Custom top and bottom";
    private const string GlobalXLabel = "Global X";
    private const string GlobalYLabel = "Global Y";
    private const string GlobalZLabel = "Global Z";
    private const string PositiveLabel = "Positive";
    private const string NegativeLabel = "Negative";
    private const string UseAllValuesLabel = "Use All Values";
    private const string ZeroNegativeValuesLabel = "Zero Negative Values";
    private const string ZeroPositiveValuesLabel = "Zero Positive Values";
    private const string ReplaceExistingLabel = "Replace Existing Loads";
    private const string AddExistingLabel = "Add to Existing Loads";
    private const string DeleteExistingLabel = "Delete Existing Loads";

    private readonly EtabsParametricModellingService _etabsService = new();
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _connectionStatus = "Not connected";
    private string _status = "Select shell walls in ETABS, then preview.";
    private string _loadPatternName = "WATER";
    private bool _createLoadPatternIfMissing = true;
    private string _selectedTargetMode = SelectedShellsLabel;
    private string _selectedGroupName = "";
    private string _shellNameList = "";
    private string _selectedHeightMode = FullWallHeightLabel;
    private double _waterTableZ;
    private double _customTopZ;
    private double _customBottomZ;
    private double _gammaKnPerM3 = 9.81;
    private double _surchargeKnPerM2;
    private string _selectedDirection = GlobalXLabel;
    private string _selectedSign = NegativeLabel;
    private string _selectedRestriction = UseAllValuesLabel;
    private string _selectedAssignment = ReplaceExistingLabel;
    private string _previewReport = "No preview yet.";

    public HydrostaticShellLoadViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances());
        ReadEtabsDataCommand = new RelayCommand(_ => ReadEtabsData());
        PreviewCommand = new RelayCommand(_ => Preview());
        AssignCommand = new RelayCommand(_ => AssignToEtabs());
    }

    public IReadOnlyList<string> TargetModes { get; } =
    [
        SelectedShellsLabel,
        EtabsGroupLabel,
        ShellNameListLabel
    ];

    public IReadOnlyList<string> HeightModes { get; } =
    [
        FullWallHeightLabel,
        WaterTableToBottomLabel,
        CustomTopBottomLabel
    ];

    public IReadOnlyList<string> Directions { get; } =
    [
        GlobalXLabel,
        GlobalYLabel,
        GlobalZLabel
    ];

    public IReadOnlyList<string> Signs { get; } =
    [
        NegativeLabel,
        PositiveLabel
    ];

    public IReadOnlyList<string> Restrictions { get; } =
    [
        UseAllValuesLabel,
        ZeroNegativeValuesLabel,
        ZeroPositiveValuesLabel
    ];

    public IReadOnlyList<string> AssignmentOptions { get; } =
    [
        ReplaceExistingLabel,
        AddExistingLabel,
        DeleteExistingLabel
    ];

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<string> LoadPatterns { get; } = [];
    public ObservableCollection<string> Groups { get; } = [];
    public ObservableCollection<HydrostaticPreviewRow> PreviewRows { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand AssignCommand { get; }

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

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string LoadPatternName
    {
        get => _loadPatternName;
        set => SetProperty(ref _loadPatternName, string.IsNullOrWhiteSpace(value) ? "WATER" : value.Trim());
    }

    public bool CreateLoadPatternIfMissing
    {
        get => _createLoadPatternIfMissing;
        set => SetProperty(ref _createLoadPatternIfMissing, value);
    }

    public string SelectedTargetMode
    {
        get => _selectedTargetMode;
        set
        {
            if (SetProperty(ref _selectedTargetMode, NormalizeTargetMode(value)))
            {
                OnPropertyChanged(nameof(GroupTargetVisibility));
                OnPropertyChanged(nameof(ShellNameListVisibility));
            }
        }
    }

    public string SelectedGroupName
    {
        get => _selectedGroupName;
        set => SetProperty(ref _selectedGroupName, value ?? "");
    }

    public string ShellNameList
    {
        get => _shellNameList;
        set => SetProperty(ref _shellNameList, value ?? "");
    }

    public string SelectedHeightMode
    {
        get => _selectedHeightMode;
        set
        {
            if (SetProperty(ref _selectedHeightMode, NormalizeHeightMode(value)))
            {
                OnPropertyChanged(nameof(WaterTableVisibility));
                OnPropertyChanged(nameof(CustomLevelsVisibility));
            }
        }
    }

    public double WaterTableZ
    {
        get => _waterTableZ;
        set => SetProperty(ref _waterTableZ, double.IsFinite(value) ? value : 0.0);
    }

    public double CustomTopZ
    {
        get => _customTopZ;
        set => SetProperty(ref _customTopZ, double.IsFinite(value) ? value : 0.0);
    }

    public double CustomBottomZ
    {
        get => _customBottomZ;
        set => SetProperty(ref _customBottomZ, double.IsFinite(value) ? value : 0.0);
    }

    public double GammaKnPerM3
    {
        get => _gammaKnPerM3;
        set => SetProperty(ref _gammaKnPerM3, double.IsFinite(value) ? Math.Max(0.0, value) : 9.81);
    }

    public double SurchargeKnPerM2
    {
        get => _surchargeKnPerM2;
        set => SetProperty(ref _surchargeKnPerM2, double.IsFinite(value) ? Math.Max(0.0, value) : 0.0);
    }

    public string SelectedDirection
    {
        get => _selectedDirection;
        set => SetProperty(ref _selectedDirection, NormalizeDirection(value));
    }

    public string SelectedSign
    {
        get => _selectedSign;
        set => SetProperty(ref _selectedSign, NormalizeSign(value));
    }

    public string SelectedRestriction
    {
        get => _selectedRestriction;
        set => SetProperty(ref _selectedRestriction, NormalizeRestriction(value));
    }

    public string SelectedAssignment
    {
        get => _selectedAssignment;
        set => SetProperty(ref _selectedAssignment, NormalizeAssignment(value));
    }

    public Visibility GroupTargetVisibility =>
        string.Equals(SelectedTargetMode, EtabsGroupLabel, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShellNameListVisibility =>
        string.Equals(SelectedTargetMode, ShellNameListLabel, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility WaterTableVisibility =>
        string.Equals(SelectedHeightMode, WaterTableToBottomLabel, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CustomLevelsVisibility =>
        string.Equals(SelectedHeightMode, CustomTopBottomLabel, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public string PreviewReport
    {
        get => _previewReport;
        set => SetProperty(ref _previewReport, value);
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
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void ReadEtabsData()
    {
        HydrostaticShellLoadDataResult result = _etabsService.ListHydrostaticShellLoadData(new HydrostaticShellLoadDataRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId
        });

        string previousId = SelectedEtabsInstanceId;
        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, result.SelectedInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault(instance =>
                string.Equals(instance.Id, previousId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();

        ReplaceCollection(LoadPatterns, result.LoadPatterns);
        ReplaceCollection(Groups, result.Groups);
        PickDefaults();

        ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        Status = result.IsError ? "Read failed" : "ETABS data loaded";
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void Preview()
    {
        HydrostaticShellLoadPreviewResult result = _etabsService.PreviewHydrostaticShellLoad(BuildInput());
        if (result.Preview != null)
        {
            UpdatePreview(result.Preview);
            Status = result.IsError ? "Preview failed" : "Preview ready";
        }
        else
        {
            PreviewReport = "No preview available.";
            PreviewRows.Clear();
            Status = "Preview failed";
        }

        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void AssignToEtabs()
    {
        HydrostaticShellLoadAssignResult result = _etabsService.AssignHydrostaticShellLoad(BuildInput());
        if (result.Preview != null)
            UpdatePreview(result.Preview);

        Status = result.IsError ? "Assignment failed" : $"Assigned to {result.AppliedCount} shell(s)";
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private HydrostaticShellLoadInput BuildInput()
    {
        return new HydrostaticShellLoadInput
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            LoadPatternName = LoadPatternName,
            CreateLoadPatternIfMissing = CreateLoadPatternIfMissing,
            TargetMode = ParseTargetMode(SelectedTargetMode),
            GroupName = SelectedGroupName,
            ShellNames = [ShellNameList],
            HeightMode = ParseHeightMode(SelectedHeightMode),
            UserZTop = string.Equals(SelectedHeightMode, WaterTableToBottomLabel, StringComparison.OrdinalIgnoreCase)
                ? WaterTableZ
                : CustomTopZ,
            UserZBottom = CustomBottomZ,
            GammaKnPerM3 = GammaKnPerM3,
            SurchargeKnPerM2 = SurchargeKnPerM2,
            Direction = ParseDirection(SelectedDirection),
            Sign = ParseSign(SelectedSign),
            RestrictionOption = ParseRestriction(SelectedRestriction),
            AssignmentOption = ParseAssignment(SelectedAssignment)
        };
    }

    private void UpdatePreview(HydrostaticShellLoadPreview preview)
    {
        ReplaceCollection(PreviewRows, BuildPreviewRows(preview));
        PreviewReport =
            $"Shells: {preview.ShellCount}\n" +
            $"Load Pattern: {preview.LoadPatternName}\n" +
            $"Direction: {FormatDirection(preview.Direction)} / {preview.Sign}\n" +
            $"zTop = {preview.ZTop:0.###} m, zBottom = {preview.ZBottom:0.###} m, H = {preview.Height:0.###} m\n" +
            $"gamma = {preview.GammaKnPerM3:0.###} kN/m3, pMax = {preview.PMaxKnPerM2:0.###} kN/m2\n" +
            $"A = {preview.A:0.###}, B = {preview.B:0.###}, C = {preview.C:0.###}, D = {preview.D:0.###}\n" +
            $"qTop = {preview.QTopKnPerM2:0.###} kN/m2, qBottom = {preview.QBottomKnPerM2:0.###} kN/m2";
    }

    private static List<HydrostaticPreviewRow> BuildPreviewRows(HydrostaticShellLoadPreview preview)
    {
        return
        [
            Row("Load Pattern", preview.LoadPatternName),
            Row("Direction", FormatDirection(preview.Direction)),
            Row("Sign", preview.Sign.ToString()),
            Row("zTop", FormatM(preview.ZTop)),
            Row("zBottom", FormatM(preview.ZBottom)),
            Row("H", FormatM(preview.Height)),
            Row("gamma", $"{preview.GammaKnPerM3:0.###} kN/m3"),
            Row("surcharge", $"{preview.SurchargeKnPerM2:0.###} kN/m2"),
            Row("pBottom", $"{preview.PMaxKnPerM2:0.###} kN/m2"),
            Row("A", FormatNumber(preview.A)),
            Row("B", FormatNumber(preview.B)),
            Row("C", FormatNumber(preview.C)),
            Row("D", FormatNumber(preview.D)),
            Row("qTop Check", $"{preview.QTopKnPerM2:0.###} kN/m2"),
            Row("qBottom Check", $"{preview.QBottomKnPerM2:0.###} kN/m2"),
            Row("Shell Count", preview.ShellCount.ToString(CultureInfo.InvariantCulture)),
            Row("Restriction", FormatRestriction(preview.RestrictionOption)),
            Row("Assignment", FormatAssignment(preview.AssignmentOption))
        ];
    }

    private void PickDefaults()
    {
        if (string.IsNullOrWhiteSpace(LoadPatternName))
            LoadPatternName = "WATER";
        if (SelectedGroupName.Length == 0 || !Groups.Contains(SelectedGroupName))
            SelectedGroupName = Groups.FirstOrDefault(group => !string.Equals(group, "All", StringComparison.OrdinalIgnoreCase)) ??
                Groups.FirstOrDefault() ??
                "";
    }

    private static HydrostaticPreviewRow Row(string item, string value)
    {
        return new HydrostaticPreviewRow
        {
            Item = item,
            Value = value
        };
    }

    private static string FormatM(double value)
    {
        return $"{value:0.###} m";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatDirection(HydroLoadDirection direction)
    {
        return direction switch
        {
            HydroLoadDirection.GlobalY => GlobalYLabel,
            HydroLoadDirection.GlobalZ => GlobalZLabel,
            _ => GlobalXLabel
        };
    }

    private static string FormatRestriction(HydroLoadRestrictionOption restriction)
    {
        return restriction switch
        {
            HydroLoadRestrictionOption.ZeroNegativeValues => ZeroNegativeValuesLabel,
            HydroLoadRestrictionOption.ZeroPositiveValues => ZeroPositiveValuesLabel,
            _ => UseAllValuesLabel
        };
    }

    private static string FormatAssignment(HydroLoadAssignmentOption assignment)
    {
        return assignment switch
        {
            HydroLoadAssignmentOption.AddToExisting => AddExistingLabel,
            HydroLoadAssignmentOption.DeleteExisting => DeleteExistingLabel,
            _ => ReplaceExistingLabel
        };
    }

    private static string NormalizeTargetMode(string? value)
    {
        if (string.Equals(value, EtabsGroupLabel, StringComparison.OrdinalIgnoreCase))
            return EtabsGroupLabel;
        if (string.Equals(value, ShellNameListLabel, StringComparison.OrdinalIgnoreCase))
            return ShellNameListLabel;
        return SelectedShellsLabel;
    }

    private static string NormalizeHeightMode(string? value)
    {
        if (string.Equals(value, WaterTableToBottomLabel, StringComparison.OrdinalIgnoreCase))
            return WaterTableToBottomLabel;
        if (string.Equals(value, CustomTopBottomLabel, StringComparison.OrdinalIgnoreCase))
            return CustomTopBottomLabel;
        return FullWallHeightLabel;
    }

    private static string NormalizeDirection(string? value)
    {
        if (string.Equals(value, GlobalYLabel, StringComparison.OrdinalIgnoreCase))
            return GlobalYLabel;
        if (string.Equals(value, GlobalZLabel, StringComparison.OrdinalIgnoreCase))
            return GlobalZLabel;
        return GlobalXLabel;
    }

    private static string NormalizeSign(string? value)
    {
        return string.Equals(value, PositiveLabel, StringComparison.OrdinalIgnoreCase) ? PositiveLabel : NegativeLabel;
    }

    private static string NormalizeRestriction(string? value)
    {
        if (string.Equals(value, ZeroNegativeValuesLabel, StringComparison.OrdinalIgnoreCase))
            return ZeroNegativeValuesLabel;
        if (string.Equals(value, ZeroPositiveValuesLabel, StringComparison.OrdinalIgnoreCase))
            return ZeroPositiveValuesLabel;
        return UseAllValuesLabel;
    }

    private static string NormalizeAssignment(string? value)
    {
        if (string.Equals(value, AddExistingLabel, StringComparison.OrdinalIgnoreCase))
            return AddExistingLabel;
        if (string.Equals(value, DeleteExistingLabel, StringComparison.OrdinalIgnoreCase))
            return DeleteExistingLabel;
        return ReplaceExistingLabel;
    }

    private static HydroLoadTargetMode ParseTargetMode(string? value)
    {
        if (string.Equals(value, EtabsGroupLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadTargetMode.EtabsGroup;
        if (string.Equals(value, ShellNameListLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadTargetMode.ShellNameList;
        return HydroLoadTargetMode.SelectedEtabsShells;
    }

    private static HydroLoadHeightMode ParseHeightMode(string? value)
    {
        if (string.Equals(value, WaterTableToBottomLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadHeightMode.WaterTableToWallBottom;
        if (string.Equals(value, CustomTopBottomLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadHeightMode.CustomTopBottom;
        return HydroLoadHeightMode.FullWallHeight;
    }

    private static HydroLoadDirection ParseDirection(string? value)
    {
        if (string.Equals(value, GlobalYLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadDirection.GlobalY;
        if (string.Equals(value, GlobalZLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadDirection.GlobalZ;
        return HydroLoadDirection.GlobalX;
    }

    private static HydroLoadSign ParseSign(string? value)
    {
        return string.Equals(value, PositiveLabel, StringComparison.OrdinalIgnoreCase) ? HydroLoadSign.Positive : HydroLoadSign.Negative;
    }

    private static HydroLoadRestrictionOption ParseRestriction(string? value)
    {
        if (string.Equals(value, ZeroNegativeValuesLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadRestrictionOption.ZeroNegativeValues;
        if (string.Equals(value, ZeroPositiveValuesLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadRestrictionOption.ZeroPositiveValues;
        return HydroLoadRestrictionOption.UseAllValues;
    }

    private static HydroLoadAssignmentOption ParseAssignment(string? value)
    {
        if (string.Equals(value, AddExistingLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadAssignmentOption.AddToExisting;
        if (string.Equals(value, DeleteExistingLabel, StringComparison.OrdinalIgnoreCase))
            return HydroLoadAssignmentOption.DeleteExisting;
        return HydroLoadAssignmentOption.ReplaceExisting;
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
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
