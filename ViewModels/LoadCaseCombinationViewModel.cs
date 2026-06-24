using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TrussModelling.Models;
using TrussModelling.Services;

namespace TrussModelling.ViewModels;

public sealed class LoadCaseCombinationViewModel : ObservableObject
{
    private const string ComboMatrixStatusColumn = "Status";
    private const string ComboMatrixNameColumn = "LoadCombo";
    private const string ComboMatrixTypeColumn = "Type";

    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly Dictionary<string, string> _comboMatrixLoadCaseNamesByColumn = new(StringComparer.OrdinalIgnoreCase);
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private EtabsLoadPatternRow? _selectedLoadPattern;
    private EtabsLoadCaseRow? _selectedLoadCase;
    private EtabsLoadCombinationRow? _selectedLoadCombination;
    private StaticLoadCaseItemRow? _selectedCaseItem;
    private EtabsComboItemRow? _selectedComboItem;
    private DataView? _loadCombinationFactorMatrix;
    private DataRowView? _selectedLoadCombinationMatrixRow;
    private bool _loadingDetailRows;
    private bool _syncingCombinationMatrixSelection;
    private bool _updatingCombinationMatrix;
    private string _connectionStatus = "Not connected";
    private string _editorStatus = "Read ETABS data to begin.";
    private string _operationStatus = "Ready.";
    private bool _isBusy;
    private string _patternName = "Dead";
    private string _selectedPatternType = "Dead";
    private double _selfWeightMultiplier = 1.0;
    private string _staticCaseName = "DEAD";
    private string _selectedCaseLoadPattern = "";
    private double _staticCaseScaleFactor = 1.0;
    private string _comboName = "COMBO1";
    private string _selectedComboType = "Linear Additive";
    private string _selectedComboSourceType = "Load Case";
    private string _selectedComboSourceName = "";
    private double _comboItemFactor = 1.0;

    public LoadCaseCombinationViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Refreshing ETABS instances...", RefreshEtabsInstances), _ => !IsBusy);
        ReadEtabsDataCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Reading ETABS load data...", () => ReadEtabsData(true)), _ => !IsBusy);
        RevertLoadDataCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Re-reading ETABS load data...", () => ReadEtabsData(true)), _ => !IsBusy);
        ApplyAllChangesCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Applying all load changes to ETABS...", ApplyAllChanges), _ => !IsBusy);
        DeleteMarkedRowsCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Deleting marked load rows from ETABS...", DeleteMarkedRows), _ => !IsBusy);

        NewLoadPatternCommand = new RelayCommand(_ => AddNewLoadPattern());
        DuplicateLoadPatternCommand = new RelayCommand(_ => DuplicateLoadPattern());
        SaveLoadPatternCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Applying selected load pattern to ETABS...", ApplySelectedPattern), _ => !IsBusy);
        DeleteLoadPatternCommand = new RelayCommand(_ => MarkSelectedPatternDeleted());

        NewStaticCaseCommand = new RelayCommand(_ => AddNewStaticCase());
        DuplicateStaticCaseCommand = new RelayCommand(_ => DuplicateStaticCase());
        SaveStaticCaseCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Applying selected load case to ETABS...", ApplySelectedCase), _ => !IsBusy);
        DeleteStaticCaseCommand = new RelayCommand(_ => MarkSelectedCaseDeleted());
        AddCaseItemCommand = new RelayCommand(_ => AddCaseItem());
        RemoveCaseItemCommand = new RelayCommand(parameter => RemoveCaseItem(parameter));
        ClearCaseItemsCommand = new RelayCommand(_ => ClearCaseItems());
        AddTableCaseItemCommand = new RelayCommand(parameter => AddTableCaseItem(parameter as EtabsLoadCaseRow));
        RemoveTableCaseItemCommand = new RelayCommand(parameter => RemoveTableCaseItem(parameter as StaticLoadCaseItemRow));
        ClearTableCaseItemsCommand = new RelayCommand(parameter => ClearTableCaseItems(parameter as EtabsLoadCaseRow));

        NewComboCommand = new RelayCommand(_ => AddNewCombination());
        DuplicateComboCommand = new RelayCommand(_ => DuplicateCombination());
        AddComboItemCommand = new RelayCommand(_ => AddComboItem());
        RemoveComboItemCommand = new RelayCommand(parameter => RemoveComboItem(parameter));
        ClearComboItemsCommand = new RelayCommand(_ => ClearComboItems());
        AddTableComboLoadCaseCommand = new RelayCommand(parameter => AddTableComboItem(parameter as EtabsLoadCombinationRow, "Load Case"));
        AddTableComboCombinationCommand = new RelayCommand(parameter => AddTableComboItem(parameter as EtabsLoadCombinationRow, "Combination"));
        RemoveTableComboItemCommand = new RelayCommand(parameter => RemoveTableComboItem(parameter as EtabsComboItemRow));
        ClearTableComboItemsCommand = new RelayCommand(parameter => ClearTableComboItems(parameter as EtabsLoadCombinationRow));
        LoadSelectedComboCommand = new RelayCommand(_ => LoadSelectedCombinationToEditor());
        SaveComboCommand = new RelayCommand(async _ => await RunBusyCommandAsync("Applying selected load combination to ETABS...", ApplySelectedCombination), _ => !IsBusy);
        DeleteComboCommand = new RelayCommand(_ => MarkSelectedCombinationDeleted());

        UpdateComboSourceNames();
    }

    public IReadOnlyList<string> PatternTypes { get; } = BuildPatternTypeList();
    public IReadOnlyList<string> CaseLoadTypes { get; } = ["Load", "Accel"];
    public IReadOnlyList<string> ComboTypes { get; } =
    [
        "Linear Additive",
        "Envelope",
        "Absolute Additive",
        "SRSS",
        "Range Additive"
    ];

    public IReadOnlyList<string> ComboSourceTypes { get; } =
    [
        "Load Case",
        "Combination"
    ];

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<EtabsLoadPatternRow> LoadPatterns { get; } = [];
    public ObservableCollection<EtabsLoadCaseRow> LoadCases { get; } = [];
    public ObservableCollection<EtabsLoadCombinationRow> LoadCombinations { get; } = [];
    public ObservableCollection<string> LoadPatternNames { get; } = [];
    public ObservableCollection<string> LoadCaseNames { get; } = [];
    public ObservableCollection<string> LoadCombinationNames { get; } = [];
    public ObservableCollection<string> ComboSourceNames { get; } = [];
    public ObservableCollection<string> TableComboSourceNames { get; } = [];
    public ObservableCollection<StaticLoadCaseItemRow> CaseItems { get; } = [];
    public ObservableCollection<EtabsComboItemRow> ComboItems { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public DataView? LoadCombinationFactorMatrix
    {
        get => _loadCombinationFactorMatrix;
        private set => SetProperty(ref _loadCombinationFactorMatrix, value);
    }

    public DataRowView? SelectedLoadCombinationMatrixRow
    {
        get => _selectedLoadCombinationMatrixRow;
        set
        {
            if (!SetProperty(ref _selectedLoadCombinationMatrixRow, value) ||
                _syncingCombinationMatrixSelection ||
                value == null)
            {
                return;
            }

            string comboName = Convert.ToString(value[ComboMatrixNameColumn], CultureInfo.CurrentCulture) ?? "";
            EtabsLoadCombinationRow? combo = LoadCombinations.FirstOrDefault(row =>
                string.Equals(row.Name, comboName, StringComparison.OrdinalIgnoreCase));
            if (combo != null && !ReferenceEquals(SelectedLoadCombination, combo))
                SelectedLoadCombination = combo;
        }
    }

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ReadEtabsDataCommand { get; }
    public ICommand RevertLoadDataCommand { get; }
    public ICommand ApplyAllChangesCommand { get; }
    public ICommand DeleteMarkedRowsCommand { get; }
    public ICommand NewLoadPatternCommand { get; }
    public ICommand DuplicateLoadPatternCommand { get; }
    public ICommand SaveLoadPatternCommand { get; }
    public ICommand DeleteLoadPatternCommand { get; }
    public ICommand NewStaticCaseCommand { get; }
    public ICommand DuplicateStaticCaseCommand { get; }
    public ICommand SaveStaticCaseCommand { get; }
    public ICommand DeleteStaticCaseCommand { get; }
    public ICommand AddCaseItemCommand { get; }
    public ICommand RemoveCaseItemCommand { get; }
    public ICommand ClearCaseItemsCommand { get; }
    public ICommand AddTableCaseItemCommand { get; }
    public ICommand RemoveTableCaseItemCommand { get; }
    public ICommand ClearTableCaseItemsCommand { get; }
    public ICommand NewComboCommand { get; }
    public ICommand DuplicateComboCommand { get; }
    public ICommand AddComboItemCommand { get; }
    public ICommand RemoveComboItemCommand { get; }
    public ICommand ClearComboItemsCommand { get; }
    public ICommand AddTableComboLoadCaseCommand { get; }
    public ICommand AddTableComboCombinationCommand { get; }
    public ICommand RemoveTableComboItemCommand { get; }
    public ICommand ClearTableComboItemsCommand { get; }
    public ICommand LoadSelectedComboCommand { get; }
    public ICommand SaveComboCommand { get; }
    public ICommand DeleteComboCommand { get; }

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

    public EtabsLoadPatternRow? SelectedLoadPattern
    {
        get => _selectedLoadPattern;
        set
        {
            if (SetProperty(ref _selectedLoadPattern, value) && value != null)
            {
                PatternName = value.Name;
                SelectedPatternType = value.PatternType;
                SelfWeightMultiplier = value.SelfWeightMultiplier;
            }
        }
    }

    public EtabsLoadCaseRow? SelectedLoadCase
    {
        get => _selectedLoadCase;
        set
        {
            if (SetProperty(ref _selectedLoadCase, value))
                LoadSelectedCaseToEditor();
        }
    }

    public EtabsLoadCombinationRow? SelectedLoadCombination
    {
        get => _selectedLoadCombination;
        set
        {
            if (SetProperty(ref _selectedLoadCombination, value))
            {
                LoadSelectedCombinationToEditor();
                SelectCombinationMatrixRow(value?.Name ?? "");
            }
        }
    }

    public StaticLoadCaseItemRow? SelectedCaseItem
    {
        get => _selectedCaseItem;
        set => SetProperty(ref _selectedCaseItem, value);
    }

    public EtabsComboItemRow? SelectedComboItem
    {
        get => _selectedComboItem;
        set => SetProperty(ref _selectedComboItem, value);
    }

    public string PatternName
    {
        get => _patternName;
        set => SetProperty(ref _patternName, value ?? "");
    }

    public string SelectedPatternType
    {
        get => _selectedPatternType;
        set => SetProperty(ref _selectedPatternType, value ?? "Other");
    }

    public double SelfWeightMultiplier
    {
        get => _selfWeightMultiplier;
        set => SetProperty(ref _selfWeightMultiplier, double.IsFinite(value) ? value : 0.0);
    }

    public string StaticCaseName
    {
        get => _staticCaseName;
        set => SetProperty(ref _staticCaseName, value ?? "");
    }

    public string SelectedCaseLoadPattern
    {
        get => _selectedCaseLoadPattern;
        set => SetProperty(ref _selectedCaseLoadPattern, value ?? "");
    }

    public double StaticCaseScaleFactor
    {
        get => _staticCaseScaleFactor;
        set => SetProperty(ref _staticCaseScaleFactor, double.IsFinite(value) ? value : 1.0);
    }

    public string ComboName
    {
        get => _comboName;
        set => SetProperty(ref _comboName, value ?? "");
    }

    public string SelectedComboType
    {
        get => _selectedComboType;
        set => SetProperty(ref _selectedComboType, value ?? "Linear Additive");
    }

    public string SelectedComboSourceType
    {
        get => _selectedComboSourceType;
        set
        {
            string next = IsCombinationSource(value) ? "Combination" : "Load Case";
            if (SetProperty(ref _selectedComboSourceType, next))
                UpdateComboSourceNames();
        }
    }

    public string SelectedComboSourceName
    {
        get => _selectedComboSourceName;
        set => SetProperty(ref _selectedComboSourceName, value ?? "");
    }

    public double ComboItemFactor
    {
        get => _comboItemFactor;
        set => SetProperty(ref _comboItemFactor, double.IsFinite(value) ? value : 1.0);
    }

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
            ConnectionStatus = "Command failed";
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
        return ConnectionStatus.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            ConnectionStatus.Contains("not connected", StringComparison.OrdinalIgnoreCase) ||
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

    private void ReadEtabsData(bool showMessages)
    {
        LoadCaseCombinationDataResult result = _etabsService.ListLoadCaseCombinationData(new LoadCaseCombinationDataRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId
        });

        string previousInstanceId = SelectedEtabsInstanceId;
        string previousPattern = SelectedLoadPattern?.Name ?? PatternName;
        string previousCase = SelectedLoadCase?.Name ?? StaticCaseName;
        string previousCombination = SelectedLoadCombination?.Name ?? ComboName;
        string previousComboSourceName = SelectedComboSourceName;

        ReplaceCollection(EtabsInstances, result.Instances);
        SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
            string.Equals(instance.Id, result.SelectedInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault(instance =>
                string.Equals(instance.Id, previousInstanceId, StringComparison.OrdinalIgnoreCase)) ??
            EtabsInstances.FirstOrDefault();

        ReplaceCollection(LoadPatterns, result.LoadPatterns);
        ReplaceCollection(LoadCases, result.LoadCases);
        ReplaceCollection(LoadCombinations, result.LoadCombinations);
        SubscribeTableRows();
        RefreshNameCollections();
        UpdatePatternUsage();

        SelectedLoadPattern = LoadPatterns.FirstOrDefault(row =>
            string.Equals(row.Name, previousPattern, StringComparison.OrdinalIgnoreCase)) ??
            LoadPatterns.FirstOrDefault();
        SelectedLoadCase = LoadCases.FirstOrDefault(row =>
            string.Equals(row.Name, previousCase, StringComparison.OrdinalIgnoreCase)) ??
            LoadCases.FirstOrDefault();
        SelectedLoadCombination = LoadCombinations.FirstOrDefault(row =>
            string.Equals(row.Name, previousCombination, StringComparison.OrdinalIgnoreCase)) ??
            LoadCombinations.FirstOrDefault();

        if (!PatternTypes.Contains(SelectedPatternType, StringComparer.OrdinalIgnoreCase))
            SelectedPatternType = PatternTypes.FirstOrDefault() ?? "Other";

        UpdateComboSourceNames();
        SelectedComboSourceName = ComboSourceNames.FirstOrDefault(name =>
            string.Equals(name, previousComboSourceName, StringComparison.OrdinalIgnoreCase)) ??
            ComboSourceNames.FirstOrDefault() ??
            "";

        ConnectionStatus = result.IsError ? "Not connected" : "Connected";
        EditorStatus = result.Message;

        if (showMessages)
            ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private void AddNewLoadPattern()
    {
        var row = new EtabsLoadPatternRow
        {
            Name = GetUniqueName("NEW_PATTERN", LoadPatterns.Select(item => item.Name)),
            PatternType = "Dead",
            SelfWeightMultiplier = 0.0
        };
        row.MarkNew();
        row.PropertyChanged += PatternRow_PropertyChanged;
        LoadPatterns.Add(row);
        SelectedLoadPattern = row;
        RefreshNameCollections();
        EditorStatus = "New load pattern added. Apply selected or apply all changes to update ETABS.";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void DuplicateLoadPattern()
    {
        if (SelectedLoadPattern == null)
            return;

        EtabsLoadPatternRow row = SelectedLoadPattern.CloneAsNew(GetUniqueCopyName(SelectedLoadPattern.Name, LoadPatterns.Select(item => item.Name)));
        row.PropertyChanged += PatternRow_PropertyChanged;
        LoadPatterns.Add(row);
        SelectedLoadPattern = row;
        RefreshNameCollections();
    }

    private void MarkSelectedPatternDeleted()
    {
        if (SelectedLoadPattern == null)
            return;

        if (SelectedLoadPattern.Status == EditableRowStatus.New)
            LoadPatterns.Remove(SelectedLoadPattern);
        else
            SelectedLoadPattern.MarkDeleted();

        RefreshNameCollections();
    }

    private void ApplySelectedPattern()
    {
        if (SelectedLoadPattern == null)
        {
            EditorStatus = "Select a load pattern row first.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        LoadCaseCombinationUpdateResult result = ApplyPatternRow(SelectedLoadPattern);
        FinishSingleApply(result);
    }

    private void AddNewStaticCase()
    {
        var item = new StaticLoadCaseItemRow
        {
            LoadType = "Load",
            Name = LoadPatternNames.FirstOrDefault() ?? "",
            ScaleFactor = 1.0
        };
        var row = new EtabsLoadCaseRow
        {
            Name = GetUniqueName("NEW_CASE", LoadCases.Select(caseRow => caseRow.Name)),
            CaseType = "LinearStatic",
            IsEditable = true,
            Items = [item],
            ItemsSummary = BuildStaticCaseItemsSummary([item])
        };
        row.MarkNew();
        row.PropertyChanged += CaseRow_PropertyChanged;
        LoadCases.Add(row);
        SelectedLoadCase = row;
        RefreshNameCollections();
        EditorStatus = "New load case added. Edit its loads, then apply selected or apply all changes to update ETABS.";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void DuplicateStaticCase()
    {
        if (SelectedLoadCase == null)
            return;

        EtabsLoadCaseRow row = SelectedLoadCase.CloneAsNew(GetUniqueCopyName(SelectedLoadCase.Name, LoadCases.Select(item => item.Name)));
        row.PropertyChanged += CaseRow_PropertyChanged;
        LoadCases.Add(row);
        SelectedLoadCase = row;
        RefreshNameCollections();
    }

    private void MarkSelectedCaseDeleted()
    {
        if (SelectedLoadCase == null)
            return;

        if (SelectedLoadCase.Status == EditableRowStatus.New)
            LoadCases.Remove(SelectedLoadCase);
        else
            SelectedLoadCase.MarkDeleted();

        RefreshNameCollections();
    }

    private void ApplySelectedCase()
    {
        if (SelectedLoadCase == null)
        {
            EditorStatus = "Select a load case row first.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        LoadCaseCombinationUpdateResult result = ApplyCaseRow(SelectedLoadCase);
        FinishSingleApply(result);
    }

    private void AddCaseItem()
    {
        var item = new StaticLoadCaseItemRow
        {
            LoadType = "Load",
            Name = LoadPatternNames.FirstOrDefault() ?? "",
            ScaleFactor = 1.0
        };
        AddCaseItemToDetail(item);
        SelectedCaseItem = item;
        MarkSelectedCaseEdited();
    }

    private void RemoveCaseItem(object? parameter)
    {
        StaticLoadCaseItemRow? item = parameter as StaticLoadCaseItemRow ?? SelectedCaseItem;
        if (item == null)
            return;

        item.PropertyChanged -= CaseItem_PropertyChanged;
        CaseItems.Remove(item);
        SelectedCaseItem = CaseItems.FirstOrDefault();
        MarkSelectedCaseEdited();
    }

    private void ClearCaseItems()
    {
        foreach (StaticLoadCaseItemRow item in CaseItems)
            item.PropertyChanged -= CaseItem_PropertyChanged;

        CaseItems.Clear();
        SelectedCaseItem = null;
        MarkSelectedCaseEdited();
    }

    private void AddTableCaseItem(EtabsLoadCaseRow? row)
    {
        row ??= SelectedLoadCase;
        if (row == null || row.Status == EditableRowStatus.Deleted)
            return;

        row.Items.Add(new StaticLoadCaseItemRow
        {
            LoadType = "Load",
            Name = LoadPatternNames.FirstOrDefault() ?? "",
            ScaleFactor = 1.0
        });
        SelectedLoadCase = row;
    }

    private void RemoveTableCaseItem(StaticLoadCaseItemRow? item)
    {
        if (item == null)
            return;

        EtabsLoadCaseRow? row = LoadCases.FirstOrDefault(loadCase => loadCase.Items.Contains(item));
        if (row == null || row.Status == EditableRowStatus.Deleted)
            return;

        row.Items.Remove(item);
        SelectedLoadCase = row;
    }

    private void ClearTableCaseItems(EtabsLoadCaseRow? row)
    {
        row ??= SelectedLoadCase;
        if (row == null || row.Status == EditableRowStatus.Deleted)
            return;

        row.Items.Clear();
        SelectedLoadCase = row;
    }

    private void AddNewCombination()
    {
        var row = new EtabsLoadCombinationRow
        {
            Name = GetUniqueName("NEW_COMBO", LoadCombinations.Select(combo => combo.Name)),
            ComboType = "Linear Additive",
            Items = [],
            ItemsSummary = ""
        };
        row.MarkNew();
        row.PropertyChanged += ComboRow_PropertyChanged;
        LoadCombinations.Add(row);
        SelectedLoadCombination = row;
        RefreshNameCollections();
        EditorStatus = "New load combination added. Edit its factors, then apply selected or apply all changes to update ETABS.";
        OperationStatus = $"Done: {EditorStatus}";
    }

    private void DuplicateCombination()
    {
        if (SelectedLoadCombination == null)
            return;

        EtabsLoadCombinationRow row = SelectedLoadCombination.CloneAsNew(GetUniqueCopyName(SelectedLoadCombination.Name, LoadCombinations.Select(item => item.Name)));
        row.PropertyChanged += ComboRow_PropertyChanged;
        LoadCombinations.Add(row);
        SelectedLoadCombination = row;
        RefreshNameCollections();
    }

    private void MarkSelectedCombinationDeleted()
    {
        if (SelectedLoadCombination == null)
            return;

        if (SelectedLoadCombination.Status == EditableRowStatus.New)
            LoadCombinations.Remove(SelectedLoadCombination);
        else
            SelectedLoadCombination.MarkDeleted();

        RefreshNameCollections();
    }

    private void AddComboItem()
    {
        string sourceName = (SelectedComboSourceName ?? "").Trim();
        if (sourceName.Length == 0)
        {
            EditorStatus = "Select a load case or combination before adding an item.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        if (IsCombinationSource(SelectedComboSourceType) &&
            string.Equals(sourceName, SelectedLoadCombination?.Name ?? ComboName, StringComparison.OrdinalIgnoreCase))
        {
            EditorStatus = "A combination cannot include itself.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        var item = new EtabsComboItemRow
        {
            SourceType = SelectedComboSourceType,
            Name = sourceName,
            Factor = ComboItemFactor
        };
        AddComboItemToDetail(item);
        SelectedComboItem = item;
        MarkSelectedCombinationEdited();
    }

    private void RemoveComboItem(object? parameter)
    {
        EtabsComboItemRow? item = parameter as EtabsComboItemRow ?? SelectedComboItem;
        if (item == null)
            return;

        item.PropertyChanged -= ComboItem_PropertyChanged;
        ComboItems.Remove(item);
        SelectedComboItem = ComboItems.FirstOrDefault();
        MarkSelectedCombinationEdited();
    }

    private void ClearComboItems()
    {
        foreach (EtabsComboItemRow item in ComboItems)
            item.PropertyChanged -= ComboItem_PropertyChanged;

        ComboItems.Clear();
        SelectedComboItem = null;
        MarkSelectedCombinationEdited();
    }

    private void AddTableComboItem(EtabsLoadCombinationRow? row, string sourceType)
    {
        row ??= SelectedLoadCombination;
        if (row == null || row.Status == EditableRowStatus.Deleted)
            return;

        bool isCombination = IsCombinationSource(sourceType);
        string sourceName = isCombination
            ? LoadCombinationNames.FirstOrDefault(name => !string.Equals(name, row.Name, StringComparison.OrdinalIgnoreCase)) ?? ""
            : LoadCaseNames.FirstOrDefault() ?? "";

        row.Items.Add(new EtabsComboItemRow
        {
            SourceType = isCombination ? "Combination" : "Load Case",
            Name = sourceName,
            Factor = 1.0
        });
        SelectedLoadCombination = row;
    }

    private void RemoveTableComboItem(EtabsComboItemRow? item)
    {
        if (item == null)
            return;

        EtabsLoadCombinationRow? row = LoadCombinations.FirstOrDefault(combo => combo.Items.Contains(item));
        if (row == null || row.Status == EditableRowStatus.Deleted)
            return;

        row.Items.Remove(item);
        SelectedLoadCombination = row;
    }

    private void ClearTableComboItems(EtabsLoadCombinationRow? row)
    {
        row ??= SelectedLoadCombination;
        if (row == null || row.Status == EditableRowStatus.Deleted)
            return;

        row.Items.Clear();
        SelectedLoadCombination = row;
    }

    private void LoadSelectedCaseToEditor()
    {
        _loadingDetailRows = true;
        try
        {
            foreach (StaticLoadCaseItemRow item in CaseItems)
                item.PropertyChanged -= CaseItem_PropertyChanged;
            CaseItems.Clear();

            if (SelectedLoadCase == null)
                return;

            StaticCaseName = SelectedLoadCase.Name;
            foreach (StaticLoadCaseItemRow item in SelectedLoadCase.Items.Select(item => item.Clone()))
                AddCaseItemToDetail(item);

            SelectedCaseItem = CaseItems.FirstOrDefault();
            SelectedCaseLoadPattern = CaseItems.FirstOrDefault()?.Name ?? LoadPatternNames.FirstOrDefault() ?? "";
            StaticCaseScaleFactor = CaseItems.FirstOrDefault()?.ScaleFactor ?? 1.0;
        }
        finally
        {
            _loadingDetailRows = false;
        }
    }

    private void LoadSelectedCombinationToEditor()
    {
        _loadingDetailRows = true;
        try
        {
            foreach (EtabsComboItemRow item in ComboItems)
                item.PropertyChanged -= ComboItem_PropertyChanged;
            ComboItems.Clear();

            if (SelectedLoadCombination == null)
                return;

            ComboName = SelectedLoadCombination.Name;
            SelectedComboType = ComboTypes.FirstOrDefault(type =>
                string.Equals(type, SelectedLoadCombination.ComboType, StringComparison.OrdinalIgnoreCase)) ??
                SelectedLoadCombination.ComboType;

            foreach (EtabsComboItemRow item in SelectedLoadCombination.Items.Select(item => item.Clone()))
                AddComboItemToDetail(item);

            SelectedComboItem = ComboItems.FirstOrDefault();
            EditorStatus = $"Loaded combination '{ComboName}' into the editor.";
        }
        finally
        {
            _loadingDetailRows = false;
        }
    }

    private void ApplySelectedCombination()
    {
        if (SelectedLoadCombination == null)
        {
            EditorStatus = "Select a combination row first.";
            OperationStatus = $"Failed: {EditorStatus}";
            ShowMessages([], ValidationSeverity.Critical, EditorStatus);
            return;
        }

        LoadCaseCombinationUpdateResult result = ApplyCombinationRow(SelectedLoadCombination);
        FinishSingleApply(result);
    }

    private void ApplyAllChanges()
    {
        var messages = new List<string>();
        bool hasError = false;

        foreach (EtabsLoadCombinationRow row in LoadCombinations.Where(row => row.Status == EditableRowStatus.Deleted).ToList())
            ApplyBatchResult(ApplyCombinationRow(row), messages, ref hasError);
        foreach (EtabsLoadCaseRow row in LoadCases.Where(row => row.Status == EditableRowStatus.Deleted).ToList())
            ApplyBatchResult(ApplyCaseRow(row), messages, ref hasError);
        foreach (EtabsLoadPatternRow row in LoadPatterns.Where(row => row.Status == EditableRowStatus.Deleted).ToList())
            ApplyBatchResult(ApplyPatternRow(row), messages, ref hasError);

        foreach (EtabsLoadPatternRow row in LoadPatterns.Where(row => row.Status is EditableRowStatus.New or EditableRowStatus.Edited or EditableRowStatus.Error).ToList())
            ApplyBatchResult(ApplyPatternRow(row), messages, ref hasError);
        foreach (EtabsLoadCaseRow row in LoadCases.Where(row => row.Status is EditableRowStatus.New or EditableRowStatus.Edited or EditableRowStatus.Error).ToList())
            ApplyBatchResult(ApplyCaseRow(row), messages, ref hasError);
        foreach (EtabsLoadCombinationRow row in LoadCombinations.Where(row => row.Status is EditableRowStatus.New or EditableRowStatus.Edited or EditableRowStatus.Error).ToList())
            ApplyBatchResult(ApplyCombinationRow(row), messages, ref hasError);

        ReadEtabsData(false);
        string summary = messages.Count == 0 ? "No pending changes to apply." : string.Join(" ", messages);
        ConnectionStatus = hasError ? "Update failed" : "Connected";
        EditorStatus = summary;
        ShowMessages([], hasError ? ValidationSeverity.Critical : ValidationSeverity.Info, summary);
    }

    private void DeleteMarkedRows()
    {
        var messages = new List<string>();
        bool hasError = false;

        foreach (EtabsLoadCombinationRow row in LoadCombinations.Where(row => row.Status == EditableRowStatus.Deleted).ToList())
            ApplyBatchResult(ApplyCombinationRow(row), messages, ref hasError);
        foreach (EtabsLoadCaseRow row in LoadCases.Where(row => row.Status == EditableRowStatus.Deleted).ToList())
            ApplyBatchResult(ApplyCaseRow(row), messages, ref hasError);
        foreach (EtabsLoadPatternRow row in LoadPatterns.Where(row => row.Status == EditableRowStatus.Deleted).ToList())
            ApplyBatchResult(ApplyPatternRow(row), messages, ref hasError);

        ReadEtabsData(false);
        string summary = messages.Count == 0 ? "No rows are marked delete." : string.Join(" ", messages);
        ConnectionStatus = hasError ? "Update failed" : "Connected";
        EditorStatus = summary;
        ShowMessages([], hasError ? ValidationSeverity.Critical : ValidationSeverity.Info, summary);
    }

    private LoadCaseCombinationUpdateResult ApplyPatternRow(EtabsLoadPatternRow row)
    {
        if (row.Status == EditableRowStatus.Deleted)
        {
            return _etabsService.DeleteLoadPattern(new LoadCaseCombinationDeleteRequest
            {
                EtabsInstanceId = SelectedEtabsInstanceId,
                Name = row.Name
            });
        }

        LoadCaseCombinationUpdateResult validation = ValidatePatternRow(row);
        if (validation.IsError)
        {
            row.MarkError(validation.Message);
            return validation;
        }

        return _etabsService.UpdateLoadPattern(new LoadPatternUpdateRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Name = row.Name,
            PatternType = row.PatternType,
            SelfWeightMultiplier = row.SelfWeightMultiplier
        });
    }

    private LoadCaseCombinationUpdateResult ApplyCaseRow(EtabsLoadCaseRow row)
    {
        if (row.Status == EditableRowStatus.Deleted)
        {
            return _etabsService.DeleteLoadCase(new LoadCaseCombinationDeleteRequest
            {
                EtabsInstanceId = SelectedEtabsInstanceId,
                Name = row.Name
            });
        }

        LoadCaseCombinationUpdateResult validation = ValidateCaseRow(row);
        if (validation.IsError)
        {
            row.MarkError(validation.Message);
            return validation;
        }

        return _etabsService.UpdateStaticLoadCase(new StaticLoadCaseUpdateRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Name = row.Name,
            Items = row.Items.Select(item => item.Clone()).ToList()
        });
    }

    private LoadCaseCombinationUpdateResult ApplyCombinationRow(EtabsLoadCombinationRow row)
    {
        if (row.Status == EditableRowStatus.Deleted)
        {
            return _etabsService.DeleteLoadCombination(new LoadCaseCombinationDeleteRequest
            {
                EtabsInstanceId = SelectedEtabsInstanceId,
                Name = row.Name
            });
        }

        LoadCaseCombinationUpdateResult validation = ValidateCombinationRow(row);
        if (validation.IsError)
        {
            row.MarkError(validation.Message);
            return validation;
        }

        return _etabsService.UpdateLoadCombination(new LoadCombinationUpdateRequest
        {
            EtabsInstanceId = SelectedEtabsInstanceId,
            Name = row.Name,
            ComboType = row.ComboType,
            Items = row.Items.Select(item => item.Clone()).ToList()
        });
    }

    private LoadCaseCombinationUpdateResult ValidatePatternRow(EtabsLoadPatternRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
            return ErrorResult("Load pattern name cannot be blank.");
        if (!double.IsFinite(row.SelfWeightMultiplier))
            return ErrorResult($"Load pattern '{row.Name}' has an invalid self-weight multiplier.");
        if (LoadPatterns.Count(item => item != row && item.Status != EditableRowStatus.Deleted && string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase)) > 0)
            return ErrorResult($"Duplicate load pattern name '{row.Name}'.");

        return OkResult();
    }

    private LoadCaseCombinationUpdateResult ValidateCaseRow(EtabsLoadCaseRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
            return ErrorResult("Load case name cannot be blank.");
        if (!row.IsEditable || !string.Equals(row.CaseType, "LinearStatic", StringComparison.OrdinalIgnoreCase))
            return ErrorResult($"Only Linear Static cases can be edited here. '{row.Name}' is '{row.CaseType}'.");
        if (row.Items.Count == 0)
            return ErrorResult($"Load case '{row.Name}' needs at least one load item.");
        if (LoadCases.Count(item => item != row && item.Status != EditableRowStatus.Deleted && string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase)) > 0)
            return ErrorResult($"Duplicate load case name '{row.Name}'.");

        HashSet<string> patternNames = LoadPatterns
            .Where(pattern => pattern.Status != EditableRowStatus.Deleted)
            .Select(pattern => pattern.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (StaticLoadCaseItemRow item in row.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                return ErrorResult($"Load case '{row.Name}' has a blank item name.");
            if (!double.IsFinite(item.ScaleFactor))
                return ErrorResult($"Load case '{row.Name}' has an invalid scale factor.");
            if (string.Equals(item.LoadType, "Load", StringComparison.OrdinalIgnoreCase) && !patternNames.Contains(item.Name))
                return ErrorResult($"Load case '{row.Name}' references load pattern '{item.Name}', which is not in the pattern table.");
        }

        return OkResult();
    }

    private LoadCaseCombinationUpdateResult ValidateCombinationRow(EtabsLoadCombinationRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
            return ErrorResult("Combination name cannot be blank.");
        if (row.Items.Count == 0)
            return ErrorResult($"Combination '{row.Name}' needs at least one item.");
        if (LoadCombinations.Count(item => item != row && item.Status != EditableRowStatus.Deleted && string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase)) > 0)
            return ErrorResult($"Duplicate combination name '{row.Name}'.");

        HashSet<string> caseNames = LoadCases
            .Where(loadCase => loadCase.Status != EditableRowStatus.Deleted)
            .Select(loadCase => loadCase.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> comboNames = LoadCombinations
            .Where(combo => combo.Status != EditableRowStatus.Deleted)
            .Select(combo => combo.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (EtabsComboItemRow item in row.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                return ErrorResult($"Combination '{row.Name}' has a blank item name.");
            if (!double.IsFinite(item.Factor))
                return ErrorResult($"Combination '{row.Name}' has an invalid factor.");
            if (IsCombinationSource(item.SourceType))
            {
                if (string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase))
                    return ErrorResult($"Combination '{row.Name}' cannot include itself.");
                if (!comboNames.Contains(item.Name))
                    return ErrorResult($"Combination '{row.Name}' references combination '{item.Name}', which is not in the combination table.");
            }
            else if (!caseNames.Contains(item.Name))
            {
                return ErrorResult($"Combination '{row.Name}' references load case '{item.Name}', which is not in the load case table.");
            }
        }

        return OkResult();
    }

    private void FinishSingleApply(LoadCaseCombinationUpdateResult result)
    {
        if (!result.IsError)
            ReadEtabsData(false);

        ConnectionStatus = result.IsError ? "Update failed" : "Connected";
        EditorStatus = result.Message;
        ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
    }

    private static void ApplyBatchResult(LoadCaseCombinationUpdateResult result, List<string> messages, ref bool hasError)
    {
        if (result.IsError)
            hasError = true;
        if (!string.IsNullOrWhiteSpace(result.Message))
            messages.Add(result.Message);
        messages.AddRange(result.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));
    }

    private void AddCaseItemToDetail(StaticLoadCaseItemRow item)
    {
        item.PropertyChanged += CaseItem_PropertyChanged;
        CaseItems.Add(item);
    }

    private void AddComboItemToDetail(EtabsComboItemRow item)
    {
        item.PropertyChanged += ComboItem_PropertyChanged;
        ComboItems.Add(item);
    }

    private void CaseItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loadingDetailRows)
            MarkSelectedCaseEdited();
    }

    private void ComboItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loadingDetailRows)
            MarkSelectedCombinationEdited();
    }

    private void MarkSelectedCaseEdited()
    {
        if (_loadingDetailRows || SelectedLoadCase == null)
            return;

        SyncSelectedCaseItemsFromDetail();
        if (SelectedLoadCase.Status == EditableRowStatus.Ok)
            SelectedLoadCase.Status = EditableRowStatus.Edited;
    }

    private void MarkSelectedCombinationEdited()
    {
        if (_loadingDetailRows || SelectedLoadCombination == null)
            return;

        SyncSelectedCombinationItemsFromDetail();
        if (SelectedLoadCombination.Status == EditableRowStatus.Ok)
            SelectedLoadCombination.Status = EditableRowStatus.Edited;
    }

    private void SyncSelectedCaseItemsFromDetail()
    {
        if (SelectedLoadCase == null)
            return;

        SelectedLoadCase.Items = new ObservableCollection<StaticLoadCaseItemRow>(CaseItems.Select(item => item.Clone()));
        SelectedLoadCase.ItemsSummary = BuildStaticCaseItemsSummary(SelectedLoadCase.Items);
        StaticCaseName = SelectedLoadCase.Name;
        SelectedCaseLoadPattern = CaseItems.FirstOrDefault()?.Name ?? "";
        StaticCaseScaleFactor = CaseItems.FirstOrDefault()?.ScaleFactor ?? 1.0;
    }

    private void SyncSelectedCombinationItemsFromDetail()
    {
        if (SelectedLoadCombination == null)
            return;

        SelectedLoadCombination.Items = new ObservableCollection<EtabsComboItemRow>(ComboItems.Select(item => item.Clone()));
        SelectedLoadCombination.ItemsSummary = BuildComboItemsSummary(SelectedLoadCombination.Items);
        ComboName = SelectedLoadCombination.Name;
        SelectedComboType = SelectedLoadCombination.ComboType;
    }

    private void RefreshNameCollections()
    {
        ReplaceCollection(LoadPatternNames, LoadPatterns.Where(row => row.Status != EditableRowStatus.Deleted).Select(row => row.Name));
        ReplaceCollection(LoadCaseNames, LoadCases.Where(row => row.Status != EditableRowStatus.Deleted).Select(row => row.Name));
        ReplaceCollection(LoadCombinationNames, LoadCombinations.Where(row => row.Status != EditableRowStatus.Deleted).Select(row => row.Name));
        ReplaceCollection(
            TableComboSourceNames,
            LoadCaseNames
                .Concat(LoadCombinationNames)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        UpdateComboSourceNames();
        RebuildLoadCombinationFactorMatrix();
    }

    private void RebuildLoadCombinationFactorMatrix()
    {
        string selectedName = SelectedLoadCombination?.Name ?? "";
        _comboMatrixLoadCaseNamesByColumn.Clear();

        var table = new DataTable("LoadCombinationFactors");
        table.Columns.Add(ComboMatrixStatusColumn, typeof(string));
        table.Columns.Add(ComboMatrixNameColumn, typeof(string));
        table.Columns.Add(ComboMatrixTypeColumn, typeof(string));

        var usedColumnNames = new HashSet<string>(
            [ComboMatrixStatusColumn, ComboMatrixNameColumn, ComboMatrixTypeColumn],
            StringComparer.OrdinalIgnoreCase);

        foreach (EtabsLoadCaseRow loadCase in LoadCases.Where(row => row.Status != EditableRowStatus.Deleted))
        {
            string caseName = (loadCase.Name ?? "").Trim();
            if (caseName.Length == 0 ||
                _comboMatrixLoadCaseNamesByColumn.Values.Contains(caseName, StringComparer.OrdinalIgnoreCase))
                continue;

            string columnName = GetUniqueMatrixColumnName(caseName, usedColumnNames);
            table.Columns.Add(columnName, typeof(string));
            _comboMatrixLoadCaseNamesByColumn[columnName] = caseName;
        }

        foreach (EtabsLoadCombinationRow combo in LoadCombinations)
        {
            DataRow row = table.NewRow();
            row[ComboMatrixStatusColumn] = combo.StatusText;
            row[ComboMatrixNameColumn] = combo.Name;
            row[ComboMatrixTypeColumn] = combo.ComboType;

            foreach ((string columnName, string caseName) in _comboMatrixLoadCaseNamesByColumn)
            {
                double factor = combo.Items
                    .Where(item => !IsCombinationSource(item.SourceType) &&
                        string.Equals((item.Name ?? "").Trim(), caseName, StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.Factor);

                row[columnName] = Math.Abs(factor) <= 0.000001
                    ? ""
                    : factor.ToString("0.###", CultureInfo.InvariantCulture);
            }

            table.Rows.Add(row);
        }

        foreach (string readOnlyColumnName in new[] { ComboMatrixStatusColumn, ComboMatrixNameColumn, ComboMatrixTypeColumn })
        {
            if (table.Columns[readOnlyColumnName] is DataColumn readOnlyColumn)
                readOnlyColumn.ReadOnly = true;
        }
        table.ColumnChanged += LoadCombinationFactorMatrix_ColumnChanged;

        _updatingCombinationMatrix = true;
        try
        {
            LoadCombinationFactorMatrix = table.DefaultView;
        }
        finally
        {
            _updatingCombinationMatrix = false;
        }

        SelectCombinationMatrixRow(selectedName);
    }

    private void LoadCombinationFactorMatrix_ColumnChanged(object sender, DataColumnChangeEventArgs e)
    {
        if (_updatingCombinationMatrix ||
            e.Column == null ||
            !_comboMatrixLoadCaseNamesByColumn.TryGetValue(e.Column.ColumnName, out string? caseName))
        {
            return;
        }

        string comboName = Convert.ToString(e.Row[ComboMatrixNameColumn], CultureInfo.CurrentCulture) ?? "";
        EtabsLoadCombinationRow? combo = LoadCombinations.FirstOrDefault(row =>
            string.Equals(row.Name, comboName, StringComparison.OrdinalIgnoreCase));
        if (combo == null)
            return;

        string rawValue = Convert.ToString(e.Row[e.Column], CultureInfo.CurrentCulture)?.Trim() ?? "";
        _updatingCombinationMatrix = true;
        try
        {
            if (rawValue.Length == 0)
            {
                RemoveLoadCaseFactors(combo, caseName);
                SetMatrixCellValue(e.Row, ComboMatrixStatusColumn, combo.StatusText);
                return;
            }

            if (!TryParseFactor(rawValue, out double factor))
            {
                combo.MarkError($"Invalid factor '{rawValue}' for load case '{caseName}'.");
                SetMatrixCellValue(e.Row, ComboMatrixStatusColumn, combo.StatusText);
                ShowMessages([], ValidationSeverity.Critical, combo.Remarks);
                return;
            }

            if (Math.Abs(factor) <= 0.000001)
                RemoveLoadCaseFactors(combo, caseName);
            else
                SetLoadCaseFactor(combo, caseName, factor);

            SetMatrixCellValue(e.Row, ComboMatrixStatusColumn, combo.StatusText);
            SelectedLoadCombination = combo;
        }
        finally
        {
            _updatingCombinationMatrix = false;
        }
    }

    private void SelectCombinationMatrixRow(string comboName)
    {
        if (LoadCombinationFactorMatrix == null)
            return;

        DataRowView? row = LoadCombinationFactorMatrix
            .Cast<DataRowView>()
            .FirstOrDefault(viewRow => string.Equals(
                Convert.ToString(viewRow[ComboMatrixNameColumn], CultureInfo.CurrentCulture),
                comboName,
                StringComparison.OrdinalIgnoreCase));

        _syncingCombinationMatrixSelection = true;
        try
        {
            SelectedLoadCombinationMatrixRow = row;
        }
        finally
        {
            _syncingCombinationMatrixSelection = false;
        }
    }

    private static void SetLoadCaseFactor(EtabsLoadCombinationRow combo, string caseName, double factor)
    {
        List<EtabsComboItemRow> matchingItems = combo.Items
            .Where(item => !IsCombinationSource(item.SourceType) &&
                string.Equals((item.Name ?? "").Trim(), caseName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingItems.Count == 0)
        {
            combo.Items.Add(new EtabsComboItemRow
            {
                SourceType = "Load Case",
                Name = caseName,
                Factor = factor
            });
            return;
        }

        matchingItems[0].SourceType = "Load Case";
        matchingItems[0].Name = caseName;
        matchingItems[0].Factor = factor;

        foreach (EtabsComboItemRow duplicate in matchingItems.Skip(1))
            combo.Items.Remove(duplicate);
    }

    private static void RemoveLoadCaseFactors(EtabsLoadCombinationRow combo, string caseName)
    {
        foreach (EtabsComboItemRow item in combo.Items
            .Where(item => !IsCombinationSource(item.SourceType) &&
                string.Equals((item.Name ?? "").Trim(), caseName, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            combo.Items.Remove(item);
        }
    }

    private static bool TryParseFactor(string value, out double factor)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out factor) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out factor);
    }

    private static void SetMatrixCellValue(DataRow row, string columnName, string value)
    {
        DataColumn? column = row.Table.Columns[columnName];
        if (column == null)
            return;

        bool wasReadOnly = column.ReadOnly;
        column.ReadOnly = false;
        row[columnName] = value;
        column.ReadOnly = wasReadOnly;
    }

    private static string GetUniqueMatrixColumnName(string preferredName, HashSet<string> usedColumnNames)
    {
        string baseName = string.IsNullOrWhiteSpace(preferredName) ? "Load Case" : preferredName.Trim();
        if (usedColumnNames.Add(baseName))
            return baseName;

        for (int index = 2; index < 1000; index++)
        {
            string candidate = $"{baseName}_{index}";
            if (usedColumnNames.Add(candidate))
                return candidate;
        }

        string fallback = $"{baseName}_{DateTime.Now:HHmmss}";
        usedColumnNames.Add(fallback);
        return fallback;
    }

    private void UpdatePatternUsage()
    {
        foreach (EtabsLoadPatternRow pattern in LoadPatterns)
        {
            List<string> caseNames = LoadCases
                .Where(loadCase => loadCase.Items.Any(item =>
                    string.Equals(item.LoadType, "Load", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Name, pattern.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(loadCase => loadCase.Name)
                .ToList();

            pattern.UsedBy = caseNames.Count == 0 ? "-" : string.Join(", ", caseNames);
        }
    }

    private void SubscribeTableRows()
    {
        foreach (EtabsLoadPatternRow row in LoadPatterns)
            row.PropertyChanged += PatternRow_PropertyChanged;
        foreach (EtabsLoadCaseRow row in LoadCases)
            row.PropertyChanged += CaseRow_PropertyChanged;
        foreach (EtabsLoadCombinationRow row in LoadCombinations)
            row.PropertyChanged += ComboRow_PropertyChanged;
    }

    private void PatternRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EtabsLoadPatternRow.Name) or nameof(EtabsLoadPatternRow.Status))
        {
            RefreshNameCollections();
            UpdatePatternUsage();
        }
    }

    private void CaseRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EtabsLoadCaseRow.Name) or nameof(EtabsLoadCaseRow.Status) or nameof(EtabsLoadCaseRow.ItemsSummary))
        {
            RefreshNameCollections();
            UpdatePatternUsage();
        }
    }

    private void ComboRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EtabsLoadCombinationRow.Name) or nameof(EtabsLoadCombinationRow.Status) or nameof(EtabsLoadCombinationRow.ItemsSummary))
        {
            if (_updatingCombinationMatrix)
                return;

            RefreshNameCollections();
        }
    }

    private void UpdateComboSourceNames()
    {
        IEnumerable<string> names = IsCombinationSource(SelectedComboSourceType)
            ? LoadCombinationNames
            : LoadCaseNames;

        string previous = SelectedComboSourceName;
        ReplaceCollection(ComboSourceNames, names);
        SelectedComboSourceName = ComboSourceNames.FirstOrDefault(name =>
            string.Equals(name, previous, StringComparison.OrdinalIgnoreCase)) ??
            ComboSourceNames.FirstOrDefault() ??
            "";
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

    private static LoadCaseCombinationUpdateResult OkResult()
    {
        return new LoadCaseCombinationUpdateResult();
    }

    private static LoadCaseCombinationUpdateResult ErrorResult(string message)
    {
        return new LoadCaseCombinationUpdateResult
        {
            IsError = true,
            Message = message
        };
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
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

    private static string GetUniqueCopyName(string originalName, IEnumerable<string> existingNames)
    {
        string baseName = string.IsNullOrWhiteSpace(originalName) ? "COPY" : $"{originalName}_COPY";
        return GetUniqueName(baseName, existingNames);
    }

    private static string BuildStaticCaseItemsSummary(IEnumerable<StaticLoadCaseItemRow> items)
    {
        List<StaticLoadCaseItemRow> itemList = items.ToList();
        if (itemList.Count == 0)
            return "";

        return string.Join(" + ", itemList.Select(item => $"{item.ScaleFactor:0.###} {item.Name}".Trim()));
    }

    private static string BuildComboItemsSummary(IEnumerable<EtabsComboItemRow> items)
    {
        List<EtabsComboItemRow> itemList = items.ToList();
        if (itemList.Count == 0)
            return "";

        return string.Join(" + ", itemList.Select(item => $"{item.Factor:0.###} {item.Name}".Trim()));
    }

    private static IReadOnlyList<string> BuildPatternTypeList()
    {
        string[] preferred =
        [
            "Dead",
            "SuperDead",
            "Live",
            "ReduceLive",
            "Wind",
            "Quake",
            "Snow",
            "Other",
            "HorizontalEarthPressure",
            "VerticalEarthPressure",
            "EarthSurcharge",
            "WaterloadPressure",
            "LiveLoadSurcharge",
            "EarthHydrostatic",
            "PassiveEarthPressure",
            "ActiveEarthPressure"
        ];

        string[] allTypes = Enum.GetNames(typeof(ETABSv1.eLoadPatternType));
        return preferred
            .Concat(allTypes.Except(preferred, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCombinationSource(string? value)
    {
        string normalized = new((value ?? "").Where(char.IsLetterOrDigit).ToArray());
        return string.Equals(normalized, "LoadCombo", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Combination", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Combo", StringComparison.OrdinalIgnoreCase);
    }
}
