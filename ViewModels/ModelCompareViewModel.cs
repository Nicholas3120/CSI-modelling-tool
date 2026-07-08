using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Models.Etabs;
using CSIModellingTools.Services;
using Microsoft.Win32;

namespace CSIModellingTools.ViewModels;

public sealed class ModelCompareViewModel : ObservableObject
{
    private const int MaxNavigationDetailMessages = 50;
    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly ModelCompareSnapshotJsonService _jsonService = new();
    private readonly ModelCompareService _compareService = new();
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private ModelCompareSnapshot? _oldSnapshot;
    private ModelCompareSnapshot? _newSnapshot;
    private string _oldSnapshotPath = "";
    private string _newSnapshotPath = "";
    private string _connectionStatus = "Not connected";
    private string _operationStatus = "Ready.";
    private bool _isBusy;
    private string _selectedChangeTypeFilter = "All";
    private string _selectedObjectTypeFilter = "All";
    private string _selectedConfidenceFilter = "All";
    private string _selectedMemberTypeFilter = "All";
    private string _selectedStoryFilter = "All";
    private bool _hideReviewedAndIgnored;
    private string _resultSearchText = "";
    private CancellationTokenSource? _cts;
    private bool _cancellable;
    private double _progressPercent;
    private string _progressText = "";
    private bool _isProgressDeterminate;
    private ModelCompareCategoryNodeViewModel? _selectedCategoryNode;
    private ModelCompareObjectResultViewModel? _selectedObjectResult;
    private bool _baseUseLive = true;
    private EtabsInstanceInfo? _baseInstance;
    private bool _compareUseLive = true;
    private EtabsInstanceInfo? _compareInstance;
    private int _totalAdded;
    private int _totalRemoved;
    private int _totalModified;
    private int _totalUnchanged;

    public ModelCompareViewModel()
    {
        FilteredResults = CollectionViewSource.GetDefaultView(ObjectResults);
        FilteredResults.Filter = FilterObjectResult;
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances(), _ => !IsBusy);
        CreateEtabsSnapshotCommand = new RelayCommand(_ => CreateEtabsSnapshot(), _ => !IsBusy);
        AssignMemberIdsCommand = new RelayCommand(_ => AssignMemberIds(), _ => !IsBusy);
        CancelCommand = new RelayCommand(_ => CancelOperation(), _ => IsBusy && _cancellable && _cts is { IsCancellationRequested: false });
        BrowseOldSnapshotCommand = new RelayCommand(_ => BrowseOldSnapshot(), _ => !IsBusy);
        BrowseNewSnapshotCommand = new RelayCommand(_ => BrowseNewSnapshot(), _ => !IsBusy);
        LoadOldSnapshotCommand = new RelayCommand(_ => LoadOldSnapshot(), _ => !IsBusy);
        LoadNewSnapshotCommand = new RelayCommand(_ => LoadNewSnapshot(), _ => !IsBusy);
        CompareSnapshotsCommand = new RelayCommand(_ => CompareSnapshots(), _ => !IsBusy);
        RunComparisonCommand = new RelayCommand(_ => RunComparison(), _ => !IsBusy);
        SelectResultObjectInEtabsCommand = new RelayCommand(SelectResultObjectInEtabs, CanSelectResultObjectInEtabs);
        SelectHighlightedResultsInEtabsCommand = new RelayCommand(SelectHighlightedResultsInEtabs, _ => !IsBusy);
    }

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<ModelCompareResultRowViewModel> Results { get; } = [];
    public ObservableCollection<ModelCompareObjectResultViewModel> ObjectResults { get; } = [];
    public ObservableCollection<ModelCompareCategoryNodeViewModel> Categories { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];
    public ICollectionView FilteredResults { get; }
    public IReadOnlyList<string> ReviewStatusOptions { get; } = ["Unreviewed", "Reviewed", "Ignored", "Needs checking"];
    public IReadOnlyList<string> ChangeTypeFilterOptions { get; } = ["All", .. Enum.GetNames<ModelCompareChangeType>()];
    public IReadOnlyList<string> ObjectTypeFilterOptions { get; } = ["All", .. Enum.GetNames<ModelCompareObjectType>()];
    public IReadOnlyList<string> ConfidenceFilterOptions { get; } = ["All", .. Enum.GetNames<ModelCompareConfidenceLevel>()];
    public IReadOnlyList<string> MemberTypeFilterOptions { get; } = ["All", "Beam", "Column", "Brace", "Area", "Other"];
    public ObservableCollection<string> StoryFilterOptions { get; } = ["All"];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand CreateEtabsSnapshotCommand { get; }
    public ICommand AssignMemberIdsCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseOldSnapshotCommand { get; }
    public ICommand BrowseNewSnapshotCommand { get; }
    public ICommand LoadOldSnapshotCommand { get; }
    public ICommand LoadNewSnapshotCommand { get; }
    public ICommand CompareSnapshotsCommand { get; }
    public ICommand RunComparisonCommand { get; }
    public ICommand SelectResultObjectInEtabsCommand { get; }
    public ICommand SelectHighlightedResultsInEtabsCommand { get; }

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

    public string OldSnapshotPath
    {
        get => _oldSnapshotPath;
        set
        {
            if (SetProperty(ref _oldSnapshotPath, value ?? ""))
            {
                _oldSnapshot = null;
                OnPropertyChanged(nameof(OldSnapshotSummary));
                ClearComparisonResults();
            }
        }
    }

    public string NewSnapshotPath
    {
        get => _newSnapshotPath;
        set
        {
            if (SetProperty(ref _newSnapshotPath, value ?? ""))
            {
                _newSnapshot = null;
                OnPropertyChanged(nameof(NewSnapshotSummary));
                ClearComparisonResults();
            }
        }
    }

    public string OldSnapshotSummary => _oldSnapshot == null
        ? "Old snapshot not loaded"
        : BuildSnapshotSummary(_oldSnapshot);

    public string NewSnapshotSummary => _newSnapshot == null
        ? "New snapshot not loaded"
        : BuildSnapshotSummary(_newSnapshot);

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
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
            {
                OnPropertyChanged(nameof(BusyVisibility));
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    // The bar animates as indeterminate until an object count is known (bulk reads), then switches to a
    // determinate percentage once the per-object passes start reporting.
    public bool IsProgressIndeterminate => IsBusy && !_isProgressDeterminate;

    public string SelectedChangeTypeFilter
    {
        get => _selectedChangeTypeFilter;
        set
        {
            if (SetProperty(ref _selectedChangeTypeFilter, value ?? "All"))
                RefreshResultFilter();
        }
    }

    public string SelectedObjectTypeFilter
    {
        get => _selectedObjectTypeFilter;
        set
        {
            if (SetProperty(ref _selectedObjectTypeFilter, value ?? "All"))
                RefreshResultFilter();
        }
    }

    public string SelectedConfidenceFilter
    {
        get => _selectedConfidenceFilter;
        set
        {
            if (SetProperty(ref _selectedConfidenceFilter, value ?? "All"))
                RefreshResultFilter();
        }
    }

    public string SelectedMemberTypeFilter
    {
        get => _selectedMemberTypeFilter;
        set
        {
            if (SetProperty(ref _selectedMemberTypeFilter, value ?? "All"))
                RefreshResultFilter();
        }
    }

    public string SelectedStoryFilter
    {
        get => _selectedStoryFilter;
        set
        {
            if (SetProperty(ref _selectedStoryFilter, value ?? "All"))
                RefreshResultFilter();
        }
    }

    public bool HideReviewedAndIgnored
    {
        get => _hideReviewedAndIgnored;
        set
        {
            if (SetProperty(ref _hideReviewedAndIgnored, value))
                RefreshResultFilter();
        }
    }

    public string ResultSearchText
    {
        get => _resultSearchText;
        set
        {
            if (SetProperty(ref _resultSearchText, value ?? ""))
                RefreshResultFilter();
        }
    }

    public int FilteredResultCount => FilteredResults.Cast<object>().Count();

    // Project Explorer selection: choosing a category node drives the existing object-type filter, so the
    // results list narrows to that category.
    public ModelCompareCategoryNodeViewModel? SelectedCategoryNode
    {
        get => _selectedCategoryNode;
        set
        {
            if (SetProperty(ref _selectedCategoryNode, value) && value != null)
                SelectedObjectTypeFilter = value.ObjectTypeFilter;
        }
    }

    // Master-detail: the object highlighted in the results list drives the detail (base-vs-compare) pane.
    public ModelCompareObjectResultViewModel? SelectedObjectResult
    {
        get => _selectedObjectResult;
        set
        {
            if (SetProperty(ref _selectedObjectResult, value))
            {
                OnPropertyChanged(nameof(SelectedObjectRows));
                OnPropertyChanged(nameof(HasSelectedObject));
            }
        }
    }

    public IReadOnlyList<ModelCompareResultRowViewModel> SelectedObjectRows => _selectedObjectResult?.Rows ?? [];
    public bool HasSelectedObject => _selectedObjectResult != null;
    public bool HasComparison => Categories.Count > 0;

    public int TotalAdded { get => _totalAdded; private set => SetProperty(ref _totalAdded, value); }
    public int TotalRemoved { get => _totalRemoved; private set => SetProperty(ref _totalRemoved, value); }
    public int TotalModified { get => _totalModified; private set => SetProperty(ref _totalModified, value); }
    public int TotalUnchanged { get => _totalUnchanged; private set => SetProperty(ref _totalUnchanged, value); }

    // One-button comparison sources. Each side is either a saved snapshot file (the existing Old/New paths) or
    // a live ETABS instance chosen here.
    public bool BaseUseLive
    {
        get => _baseUseLive;
        set => SetProperty(ref _baseUseLive, value);
    }

    public EtabsInstanceInfo? BaseInstance
    {
        get => _baseInstance;
        set => SetProperty(ref _baseInstance, value);
    }

    public bool CompareUseLive
    {
        get => _compareUseLive;
        set => SetProperty(ref _compareUseLive, value);
    }

    public EtabsInstanceInfo? CompareInstance
    {
        get => _compareInstance;
        set => SetProperty(ref _compareInstance, value);
    }

    public int FramesAddedCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Added);
    public int FramesDeletedCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Removed);
    public int SectionChangesCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ObjectDescription.Contains("/ section", StringComparison.OrdinalIgnoreCase));
    public int MaterialChangesCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ObjectDescription.Contains("/ material", StringComparison.OrdinalIgnoreCase));
    public int PropertyDefinitionChangesCount => Results.Count(row =>
        row.ObjectType is ModelCompareObjectType.FrameProperty or ModelCompareObjectType.AreaProperty or ModelCompareObjectType.Material ||
        row.ObjectType == ModelCompareObjectType.Area && (
            row.ObjectDescription.Contains("/ property", StringComparison.OrdinalIgnoreCase) ||
            row.ObjectDescription.Contains("/ material", StringComparison.OrdinalIgnoreCase) ||
            row.ObjectDescription.Contains("/ thickness", StringComparison.OrdinalIgnoreCase)));

    private async void AssignMemberIds()
    {
        if (IsBusy)
            return;

        MessageBoxResult confirmation = MessageBox.Show(
            "This assigns a persistent member ID to every frame and area in the selected ETABS model that does not already have one.\n\n" +
            "The model will be unlocked (which clears any existing analysis results), and you should save the model afterwards so the IDs persist for future comparisons.\n\n" +
            "Continue?",
            "Assign member IDs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        string instanceId = SelectedEtabsInstanceId;
        await RunEtabsAsync("Assigning member IDs in ETABS...", cancellable: true, (progress, token) =>
        {
            ModelCompareMemberIdResult result = _etabsService.AssignMemberIds(
                new ModelCompareMemberIdRequest { EtabsInstanceId = instanceId },
                progress,
                token);
            return () => ShowMessages(
                result.Warnings,
                result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info,
                result.Message);
        });
    }

    private async void RefreshEtabsInstances()
    {
        string previousId = SelectedEtabsInstanceId;
        await RunEtabsAsync("Refreshing ETABS instances...", cancellable: false, (_, _) =>
        {
            EtabsInstanceListResult result = _etabsService.ListEtabsInstances();
            return () =>
            {
                ReplaceCollection(EtabsInstances, result.Instances);
                SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
                    string.Equals(instance.Id, previousId, StringComparison.OrdinalIgnoreCase)) ??
                    EtabsInstances.FirstOrDefault();

                ConnectionStatus = result.Message;
                ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
            };
        });
    }

    private async void CreateEtabsSnapshot()
    {
        if (IsBusy)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save ETABS Model Snapshot",
            Filter = "Model Compare Snapshot (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "model-compare-snapshot.json"
        };

        if (dialog.ShowDialog() != true)
            return;

        string fileName = dialog.FileName;
        string instanceId = SelectedEtabsInstanceId;

        await RunEtabsAsync("Creating ETABS model snapshot...", cancellable: true, (progress, token) =>
        {
            ModelCompareSnapshotResult snapshotResult = _etabsService.ExtractModelCompareSnapshot(
                new ModelCompareSnapshotRequest { EtabsInstanceId = instanceId },
                progress,
                token);

            if (snapshotResult.Snapshot == null || snapshotResult.IsError)
            {
                return () =>
                {
                    ConnectionStatus = "Snapshot failed";
                    ShowMessages(snapshotResult.Warnings, ValidationSeverity.Critical, snapshotResult.Message);
                };
            }

            // Writing the JSON is part of the background work so a large snapshot does not block the UI thread.
            ModelCompareSnapshotSaveResult saveResult = _jsonService.SaveSnapshot(snapshotResult.Snapshot, fileName);
            return () =>
            {
                if (!saveResult.IsError)
                {
                    NewSnapshotPath = saveResult.FilePath;
                    _newSnapshot = snapshotResult.Snapshot;
                    OnPropertyChanged(nameof(NewSnapshotSummary));
                }

                ConnectionStatus = "Connected";
                string message = saveResult.IsError ? saveResult.Message : $"{snapshotResult.Message} {saveResult.Message}";
                ShowMessages(snapshotResult.Warnings, saveResult.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, message);
            };
        });
    }

    private void BrowseOldSnapshot()
    {
        string? path = BrowseSnapshotFile("Open Old Model Snapshot");
        if (path != null)
            OldSnapshotPath = path;
    }

    private void BrowseNewSnapshot()
    {
        string? path = BrowseSnapshotFile("Open New Model Snapshot");
        if (path != null)
            NewSnapshotPath = path;
    }

    private void LoadOldSnapshot()
    {
        RunCommand("Loading old model snapshot...", () =>
        {
            ModelCompareSnapshotLoadResult result = LoadOldSnapshotCore();
            ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        });
    }

    private void LoadNewSnapshot()
    {
        RunCommand("Loading new model snapshot...", () =>
        {
            ModelCompareSnapshotLoadResult result = LoadNewSnapshotCore();
            ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        });
    }

    private void CompareSnapshots()
    {
        RunCommand("Comparing model snapshots...", () =>
        {
            ModelCompareSnapshotLoadResult oldResult = LoadOldSnapshotCore();
            ModelCompareSnapshotLoadResult newResult = LoadNewSnapshotCore();
            List<string> warnings = oldResult.Warnings.Concat(newResult.Warnings).ToList();

            if (oldResult.IsError || newResult.IsError || _oldSnapshot == null || _newSnapshot == null)
            {
                string message = $"Snapshots could not be compared. Old: {oldResult.Message} New: {newResult.Message}";
                ShowMessages(warnings, ValidationSeverity.Critical, message);
                return;
            }

            ModelCompareComparisonResult comparison = _compareService.CompareSnapshots(
                _oldSnapshot,
                _newSnapshot,
                BuildToleranceSettings());
            PopulateComparison(comparison, warnings);
        });
    }

    // One-button comparison: resolve each side (live ETABS or snapshot file) on the background STA thread, run
    // the diff, then apply the results on the UI thread. Replaces the manual snapshot/load/compare sequence.
    private async void RunComparison()
    {
        if (IsBusy)
            return;

        bool baseLive = BaseUseLive;
        string baseInstanceId = BaseInstance?.Id ?? "";
        string basePath = OldSnapshotPath;
        bool compareLive = CompareUseLive;
        string compareInstanceId = CompareInstance?.Id ?? "";
        string comparePath = NewSnapshotPath;
        ModelCompareToleranceSettings tolerances = BuildToleranceSettings();

        if (!ValidateSource(baseLive, baseInstanceId, basePath, "base") ||
            !ValidateSource(compareLive, compareInstanceId, comparePath, "compare"))
        {
            return;
        }

        await RunEtabsAsync("Comparing models...", cancellable: true, (progress, token) =>
        {
            var warnings = new List<string>();
            (ModelCompareSnapshot? baseSnapshot, string baseError) = ResolveSource(baseLive, baseInstanceId, basePath, "Base", progress, token, warnings);
            if (baseSnapshot == null)
                return () => ShowMessages(warnings, ValidationSeverity.Critical, baseError);

            token.ThrowIfCancellationRequested();
            (ModelCompareSnapshot? compareSnapshot, string compareError) = ResolveSource(compareLive, compareInstanceId, comparePath, "Compare", progress, token, warnings);
            if (compareSnapshot == null)
                return () => ShowMessages(warnings, ValidationSeverity.Critical, compareError);

            ModelCompareComparisonResult comparison = _compareService.CompareSnapshots(baseSnapshot, compareSnapshot, tolerances);
            return () =>
            {
                _oldSnapshot = baseSnapshot;
                _newSnapshot = compareSnapshot;
                OnPropertyChanged(nameof(OldSnapshotSummary));
                OnPropertyChanged(nameof(NewSnapshotSummary));
                PopulateComparison(comparison, warnings);
            };
        });
    }

    private bool ValidateSource(bool useLive, string instanceId, string path, string label)
    {
        if (useLive && string.IsNullOrWhiteSpace(instanceId))
        {
            ShowMessages([], ValidationSeverity.Warning, $"Choose a live ETABS instance for the {label} model, or refresh instances first.");
            return false;
        }

        if (!useLive && string.IsNullOrWhiteSpace(path))
        {
            ShowMessages([], ValidationSeverity.Warning, $"Choose a snapshot file for the {label} model.");
            return false;
        }

        return true;
    }

    // Runs on the background STA thread: reads a live model via COM or loads a snapshot file. Returns null on
    // failure with a message; never touches UI collections.
    private (ModelCompareSnapshot? Snapshot, string Error) ResolveSource(
        bool useLive,
        string instanceId,
        string path,
        string label,
        IProgress<ModelCompareExtractionProgress> progress,
        CancellationToken token,
        List<string> warnings)
    {
        if (useLive)
        {
            ModelCompareSnapshotResult result = _etabsService.ExtractModelCompareSnapshot(
                new ModelCompareSnapshotRequest { EtabsInstanceId = instanceId },
                progress,
                token);
            warnings.AddRange(result.Warnings);
            return result.Snapshot == null || result.IsError
                ? (null, $"{label} model could not be read from ETABS: {result.Message}")
                : (result.Snapshot, "");
        }

        ModelCompareSnapshotLoadResult loadResult = _jsonService.LoadSnapshot(path);
        warnings.AddRange(loadResult.Warnings);
        return loadResult.Snapshot == null || loadResult.IsError
            ? (null, $"{label} snapshot could not be loaded: {loadResult.Message}")
            : (loadResult.Snapshot, "");
    }

    private void PopulateComparison(ModelCompareComparisonResult comparison, List<string> warnings)
    {
        ReplaceCollection(Results, comparison.Differences.Select(row => new ModelCompareResultRowViewModel(row)));
        ReplaceObjectResults(BuildObjectResults(Results));
        RebuildCategories(comparison.CategorySummaries);
        RebuildStoryFilterOptions();
        RefreshSummaryCounts();
        RefreshResultFilter();
        warnings.AddRange(comparison.Warnings);
        string summary = comparison.Errors.Count > 0
            ? $"Comparison incomplete. Frame comparison is unavailable. Found {Results.Count} difference(s) in available categories."
            : $"Comparison complete. Found {Results.Count} difference(s).";
        ShowMessages(
            warnings,
            comparison.Errors.Count > 0 ? ValidationSeverity.Critical : ValidationSeverity.Info,
            summary,
            comparison.Errors);
    }

    private bool CanSelectResultObjectInEtabs(object? parameter)
    {
        return !IsBusy &&
            parameter is ModelCompareObjectResultViewModel result &&
            result.IsSelectableInEtabs;
    }

    private void SelectResultObjectInEtabs(object? parameter)
    {
        if (parameter is not ModelCompareObjectResultViewModel result || !result.IsSelectableInEtabs)
            return;

        SelectResultsInEtabs(result.Rows.ToList());
    }

    private void SelectHighlightedResultsInEtabs(object? parameter)
    {
        List<ModelCompareResultRowViewModel> rows = parameter is IList selectedItems
            ? selectedItems.Cast<object>().OfType<ModelCompareObjectResultViewModel>().SelectMany(result => result.Rows).ToList()
            : [];

        if (rows.Count == 0)
        {
            ShowMessages([], ValidationSeverity.Warning, "Highlight one or more frame or area result rows first.");
            return;
        }

        SelectResultsInEtabs(rows);
    }

    private async void SelectResultsInEtabs(IReadOnlyList<ModelCompareResultRowViewModel> rows)
    {
        if (IsBusy)
            return;

        // Resolve the object targets on the UI thread (reading the row view models), then hand the ETABS COM
        // selection off to a background STA thread.
        var warnings = new List<string>();
        var targetsByKey = new Dictionary<string, ModelCompareEtabsSelectionTarget>(StringComparer.OrdinalIgnoreCase);
        var locationsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int unsupportedCount = 0;
        int unnamedCount = 0;

        foreach (ModelCompareResultRowViewModel row in rows)
        {
            if (row.ObjectType is not (ModelCompareObjectType.Frame or ModelCompareObjectType.Area or ModelCompareObjectType.Joint))
            {
                unsupportedCount++;
                continue;
            }

            bool useOldObject = row.ChangeType == ModelCompareChangeType.Removed;
            string objectName = (useOldObject ? row.OldEtabsObjectName : row.NewEtabsObjectName).Trim();
            string location = useOldObject ? row.OldObjectLocation : row.NewObjectLocation;
            if (objectName.Length == 0)
            {
                unnamedCount++;
                if (unnamedCount <= MaxNavigationDetailMessages)
                {
                    warnings.Add($"{row.ObjectType} result '{row.ObjectDescription}' has no reliable ETABS object name; no selection was attempted.{FormatSnapshotCoordinatesMessage(location)}");
                }
                continue;
            }

            string key = BuildEtabsSelectionKey(row.ObjectType, objectName);
            if (!targetsByKey.ContainsKey(key))
            {
                targetsByKey[key] = new ModelCompareEtabsSelectionTarget
                {
                    ObjectType = row.ObjectType,
                    ObjectName = objectName
                };
                locationsByKey[key] = location;
            }
        }

        if (unsupportedCount > 0)
            warnings.Add($"Ignored {unsupportedCount} selected property-definition row(s); only frame and area object rows can be selected in ETABS.");
        if (unnamedCount > MaxNavigationDetailMessages)
            warnings.Add($"{unnamedCount - MaxNavigationDetailMessages} additional frame/area row(s) had no reliable ETABS object name.");

        List<ModelCompareEtabsSelectionTarget> targets = targetsByKey.Values.ToList();
        if (targets.Count == 0)
        {
            ShowMessages(warnings, ValidationSeverity.Warning, "No selectable ETABS frame or area objects were found in the highlighted rows.");
            return;
        }

        if (targets.Count > ModelCompareEtabsSelectionLimits.MaxObjects)
        {
            ShowMessages(
                warnings,
                ValidationSeverity.Warning,
                $"Selection was not attempted because {targets.Count} unique objects exceed the safety limit of {ModelCompareEtabsSelectionLimits.MaxObjects}. Filter the table or select fewer rows.");
            return;
        }

        string instanceId = SelectedEtabsInstanceId;
        await RunEtabsAsync("Selecting comparison objects in ETABS...", cancellable: false, (_, _) =>
        {
            ModelCompareEtabsSelectionResult result = _etabsService.SelectModelCompareObjects(
                new ModelCompareEtabsSelectionRequest
                {
                    EtabsInstanceId = instanceId,
                    Targets = targets
                });

            return () =>
            {
                foreach (ModelCompareEtabsSelectionFailure failure in result.Failures.Take(MaxNavigationDetailMessages))
                {
                    string key = BuildEtabsSelectionKey(failure.ObjectType, failure.ObjectName);
                    locationsByKey.TryGetValue(key, out string? location);
                    warnings.Add($"{failure.ObjectType} '{failure.ObjectName}': {failure.Reason}{FormatSnapshotCoordinatesMessage(location ?? "")}");
                }
                if (result.Failures.Count > MaxNavigationDetailMessages)
                    warnings.Add($"{result.Failures.Count - MaxNavigationDetailMessages} additional ETABS object(s) were skipped.");

                ShowMessages(
                    warnings,
                    result.IsError ? ValidationSeverity.Warning : ValidationSeverity.Info,
                    result.Message);
            };
        });
    }

    private static string BuildEtabsSelectionKey(ModelCompareObjectType objectType, string objectName)
    {
        return $"{(int)objectType}:{objectName.Trim()}";
    }

    private static string FormatSnapshotCoordinatesMessage(string location)
    {
        return string.IsNullOrWhiteSpace(location)
            ? " Snapshot coordinates are unavailable."
            : $" Snapshot coordinates: {location}.";
    }

    private ModelCompareSnapshotLoadResult LoadOldSnapshotCore()
    {
        ModelCompareSnapshotLoadResult result = _jsonService.LoadSnapshot(OldSnapshotPath);
        _oldSnapshot = result.Snapshot;
        OnPropertyChanged(nameof(OldSnapshotSummary));
        return result;
    }

    private ModelCompareSnapshotLoadResult LoadNewSnapshotCore()
    {
        ModelCompareSnapshotLoadResult result = _jsonService.LoadSnapshot(NewSnapshotPath);
        _newSnapshot = result.Snapshot;
        OnPropertyChanged(nameof(NewSnapshotSummary));
        return result;
    }

    private void RunCommand(string busyMessage, Action action)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        OperationStatus = busyMessage;
        Mouse.OverrideCursor = Cursors.Wait;
        CommandManager.InvalidateRequerySuggested();

        try
        {
            action();
            if (string.Equals(OperationStatus, busyMessage, StringComparison.Ordinal))
                OperationStatus = Messages.Any(message => message.IsCritical) ? "Failed." : "Done.";
        }
        catch (Exception ex)
        {
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

    // Runs an ETABS COM operation on a dedicated STA background thread so the window stays responsive and the
    // progress bar animates. The work returns an Action that applies its results (collection updates, messages)
    // back on the UI thread once the background work completes.
    private async Task RunEtabsAsync(
        string busyMessage,
        bool cancellable,
        Func<IProgress<ModelCompareExtractionProgress>, CancellationToken, Action> work)
    {
        if (IsBusy)
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        _cancellable = cancellable;
        var progress = new Progress<ModelCompareExtractionProgress>(OnExtractionProgress);

        IsBusy = true;
        ResetProgress(busyMessage);
        OperationStatus = busyMessage;
        CommandManager.InvalidateRequerySuggested();

        try
        {
            Action applyResult = await RunOnStaThread(() => work(progress, cts.Token));
            applyResult();
            if (string.Equals(OperationStatus, busyMessage, StringComparison.Ordinal))
                OperationStatus = Messages.Any(message => message.IsCritical) ? "Failed." : "Done.";
        }
        catch (OperationCanceledException)
        {
            OperationStatus = "Cancelled.";
            ShowMessages([], ValidationSeverity.Warning, "Operation cancelled before it finished. Any tracking IDs already written to the ETABS model remain; save the model if you want to keep them.");
        }
        catch (Exception ex)
        {
            OperationStatus = $"Failed: {ex.Message}";
            ShowMessages([], ValidationSeverity.Critical, ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cancellable = false;
            _cts = null;
            cts.Dispose();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CancelOperation()
    {
        if (_cts is { IsCancellationRequested: false })
        {
            _cts.Cancel();
            OperationStatus = "Cancelling...";
            ProgressText = "Cancelling...";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnExtractionProgress(ModelCompareExtractionProgress update)
    {
        if (_isProgressDeterminate != update.IsDeterminate)
        {
            _isProgressDeterminate = update.IsDeterminate;
            OnPropertyChanged(nameof(IsProgressIndeterminate));
        }

        ProgressPercent = update.IsDeterminate ? update.Percent : 0;
        ProgressText = update.IsDeterminate ? $"{update.Percent:0}% — {update.Stage}" : update.Stage;
    }

    private void ResetProgress(string text)
    {
        ProgressPercent = 0;
        ProgressText = text;
        if (_isProgressDeterminate)
        {
            _isProgressDeterminate = false;
            OnPropertyChanged(nameof(IsProgressIndeterminate));
        }
    }

    // Constructed on the UI thread so the Progress<T> callbacks marshal back to it; the COM object is acquired
    // and used entirely on this single STA apartment.
    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string? BrowseSnapshotFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Model Compare Snapshot (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void ShowMessages(
        IEnumerable<string> warnings,
        ValidationSeverity summarySeverity,
        string summary,
        IEnumerable<string>? errors = null)
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

        issues.AddRange((errors ?? [])
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => new ValidationIssue
            {
                Severity = ValidationSeverity.Critical,
                Message = error
            }));

        issues.AddRange(warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = warning
            }));

        ReplaceCollection(Messages, issues);
        OperationStatus = summary;
    }

    private void RefreshSummaryCounts()
    {
        OnPropertyChanged(nameof(FramesAddedCount));
        OnPropertyChanged(nameof(FramesDeletedCount));
        OnPropertyChanged(nameof(SectionChangesCount));
        OnPropertyChanged(nameof(MaterialChangesCount));
        OnPropertyChanged(nameof(PropertyDefinitionChangesCount));
    }

    private void ClearComparisonResults()
    {
        Results.Clear();
        ReplaceObjectResults([]);
        Categories.Clear();
        SelectedObjectResult = null;
        TotalAdded = TotalRemoved = TotalModified = TotalUnchanged = 0;
        OnPropertyChanged(nameof(HasComparison));
        RebuildStoryFilterOptions();
        RefreshSummaryCounts();
        RefreshResultFilter();
    }

    private static readonly (ModelCompareObjectType Type, string Display)[] CategoryOrder =
    [
        (ModelCompareObjectType.Frame, "Frames"),
        (ModelCompareObjectType.Area, "Areas"),
        (ModelCompareObjectType.Joint, "Joints"),
        (ModelCompareObjectType.FrameProperty, "Frame properties"),
        (ModelCompareObjectType.AreaProperty, "Area properties"),
        (ModelCompareObjectType.Material, "Materials")
    ];

    private void RebuildCategories(IReadOnlyDictionary<ModelCompareObjectType, ModelCompareCategorySummary> summaries)
    {
        var nodes = new List<ModelCompareCategoryNodeViewModel>();
        int added = 0, removed = 0, modified = 0, unchanged = 0;
        foreach ((ModelCompareObjectType type, string display) in CategoryOrder)
        {
            if (!summaries.TryGetValue(type, out ModelCompareCategorySummary? summary))
                continue;

            nodes.Add(new ModelCompareCategoryNodeViewModel(display, type.ToString(), summary.Added, summary.Removed, summary.Modified, summary.Unchanged));
            added += summary.Added;
            removed += summary.Removed;
            modified += summary.Modified;
            unchanged += summary.Unchanged;
        }

        var allNode = new ModelCompareCategoryNodeViewModel("All changes", "All", added, removed, modified, unchanged);
        ReplaceCollection(Categories, new[] { allNode }.Concat(nodes));
        TotalAdded = added;
        TotalRemoved = removed;
        TotalModified = modified;
        TotalUnchanged = unchanged;
        OnPropertyChanged(nameof(HasComparison));
        SelectedCategoryNode = Categories.FirstOrDefault();
    }

    private bool FilterObjectResult(object item)
    {
        if (item is not ModelCompareObjectResultViewModel result)
            return false;

        if (HideReviewedAndIgnored &&
            result.ReviewStatus is ModelCompareReviewStatus.Reviewed or ModelCompareReviewStatus.Ignored)
        {
            return false;
        }

        if (!IsAllFilter(SelectedChangeTypeFilter) &&
            (!Enum.TryParse(SelectedChangeTypeFilter, true, out ModelCompareChangeType changeType) || result.PrimaryChangeType != changeType))
        {
            return false;
        }

        if (!IsAllFilter(SelectedObjectTypeFilter) &&
            (!Enum.TryParse(SelectedObjectTypeFilter, true, out ModelCompareObjectType objectType) || result.ObjectType != objectType))
        {
            return false;
        }

        if (!IsAllFilter(SelectedConfidenceFilter) &&
            (!Enum.TryParse(SelectedConfidenceFilter, true, out ModelCompareConfidenceLevel confidence) || result.ConfidenceLevel != confidence))
        {
            return false;
        }

        if (!IsAllFilter(SelectedMemberTypeFilter) &&
            (!Enum.TryParse(SelectedMemberTypeFilter, true, out ModelCompareMemberType memberType) || result.MemberType != memberType))
        {
            return false;
        }

        if (!IsAllFilter(SelectedStoryFilter) &&
            !string.Equals(result.StoryGroup, SelectedStoryFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string search = ResultSearchText.Trim();
        if (search.Length == 0)
            return true;

        return result.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllFilter(string value) => string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);

    private void RefreshResultFilter()
    {
        FilteredResults.Refresh();
        OnPropertyChanged(nameof(FilteredResultCount));
    }

    private static List<ModelCompareObjectResultViewModel> BuildObjectResults(IEnumerable<ModelCompareResultRowViewModel> rows)
    {
        var orderedGroups = new List<List<ModelCompareResultRowViewModel>>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (ModelCompareResultRowViewModel row in rows)
        {
            string key = BuildObjectKey(row);
            if (!indexByKey.TryGetValue(key, out int index))
            {
                index = orderedGroups.Count;
                indexByKey[key] = index;
                orderedGroups.Add([]);
            }

            orderedGroups[index].Add(row);
        }

        var objects = new List<ModelCompareObjectResultViewModel>(orderedGroups.Count);
        foreach (List<ModelCompareResultRowViewModel> groupRows in orderedGroups)
        {
            ModelCompareResultRowViewModel first = groupRows[0];
            bool isFrameOrArea = first.ObjectType is ModelCompareObjectType.Frame or ModelCompareObjectType.Area;
            string name = isFrameOrArea
                ? (string.IsNullOrWhiteSpace(first.NewEtabsObjectName) ? first.OldEtabsObjectName : first.NewEtabsObjectName)
                : ObjectDescriptionHead(first.ObjectDescription);
            string location = isFrameOrArea
                ? (string.IsNullOrWhiteSpace(first.NewObjectLocation) ? first.OldObjectLocation : first.NewObjectLocation)
                : "";

            objects.Add(new ModelCompareObjectResultViewModel(
                first.ObjectType,
                first.MemberType,
                first.Story,
                name,
                location,
                groupRows));
        }

        return objects;
    }

    private static string BuildObjectKey(ModelCompareResultRowViewModel row)
    {
        if (row.ObjectType is ModelCompareObjectType.Frame or ModelCompareObjectType.Area)
            return $"{(int)row.ObjectType}|{row.OldEtabsObjectName.Trim()}|{row.NewEtabsObjectName.Trim()}";

        return $"{(int)row.ObjectType}|{ObjectDescriptionHead(row.ObjectDescription)}";
    }

    private static string ObjectDescriptionHead(string description)
    {
        int index = description.IndexOf(" / ", StringComparison.Ordinal);
        return (index >= 0 ? description[..index] : description).Trim();
    }

    private void ReplaceObjectResults(IReadOnlyList<ModelCompareObjectResultViewModel> values)
    {
        foreach (ModelCompareObjectResultViewModel existing in ObjectResults)
            existing.PropertyChanged -= OnObjectResultPropertyChanged;

        ObjectResults.Clear();
        foreach (ModelCompareObjectResultViewModel value in values)
        {
            value.PropertyChanged += OnObjectResultPropertyChanged;
            ObjectResults.Add(value);
        }
    }

    private void OnObjectResultPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (HideReviewedAndIgnored && e.PropertyName == nameof(ModelCompareObjectResultViewModel.ReviewStatus))
            RefreshResultFilter();
    }

    private void RebuildStoryFilterOptions()
    {
        List<string> stories = ObjectResults
            .Select(result => result.StoryGroup)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(story => story, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = new List<string> { "All" };
        options.AddRange(stories);
        ReplaceCollection(StoryFilterOptions, options);

        if (!options.Any(option => string.Equals(option, SelectedStoryFilter, StringComparison.OrdinalIgnoreCase)))
            SelectedStoryFilter = "All";
    }

    // Tolerances are fixed sensible defaults (1 mm coordinate, etc.). They were exposed as UI knobs, but with
    // ID-based matching the moved-frame knobs are near-vestigial and two versions of the same model have
    // essentially identical coordinates, so the defaults cover the real cases. The model's own defaults match.
    private static ModelCompareToleranceSettings BuildToleranceSettings()
    {
        return new ModelCompareToleranceSettings();
    }

    private static string BuildSnapshotSummary(ModelCompareSnapshot snapshot)
    {
        string source = string.IsNullOrWhiteSpace(snapshot.Metadata.SourceModelFileName)
            ? "snapshot"
            : snapshot.Metadata.SourceModelFileName;

        return $"{source}: {snapshot.Frames.Count} frame(s) [{snapshot.Metadata.FramesReadStatus}], {snapshot.Areas.Count} area(s) [{snapshot.Metadata.AreasReadStatus}], {snapshot.FrameProperties.Count} frame propertie(s), {snapshot.AreaProperties.Count} area propertie(s), {snapshot.Materials.Count} material(s)";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
