using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private double _coordinateToleranceMm = 1.0;
    private double _lengthToleranceMm = 1.0;
    private double _orientationToleranceDegrees = 0.1;
    private double _movedFrameSearchDistanceMm = 500.0;
    private ModelCompareConfidenceLevel _minimumMovedFrameConfidence = ModelCompareConfidenceLevel.Medium;
    private string _selectedChangeTypeFilter = "All";
    private string _selectedObjectTypeFilter = "All";
    private string _selectedConfidenceFilter = "All";
    private string _selectedMemberTypeFilter = "All";
    private string _selectedStoryFilter = "All";
    private bool _hideReviewedAndIgnored;
    private string _resultSearchText = "";

    public ModelCompareViewModel()
    {
        FilteredResults = CollectionViewSource.GetDefaultView(ObjectResults);
        FilteredResults.Filter = FilterObjectResult;
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances(), _ => !IsBusy);
        CreateEtabsSnapshotCommand = new RelayCommand(_ => CreateEtabsSnapshot(), _ => !IsBusy);
        AssignMemberIdsCommand = new RelayCommand(_ => AssignMemberIds(), _ => !IsBusy);
        BrowseOldSnapshotCommand = new RelayCommand(_ => BrowseOldSnapshot(), _ => !IsBusy);
        BrowseNewSnapshotCommand = new RelayCommand(_ => BrowseNewSnapshot(), _ => !IsBusy);
        LoadOldSnapshotCommand = new RelayCommand(_ => LoadOldSnapshot(), _ => !IsBusy);
        LoadNewSnapshotCommand = new RelayCommand(_ => LoadNewSnapshot(), _ => !IsBusy);
        CompareSnapshotsCommand = new RelayCommand(_ => CompareSnapshots(), _ => !IsBusy);
        SelectResultObjectInEtabsCommand = new RelayCommand(SelectResultObjectInEtabs, CanSelectResultObjectInEtabs);
        SelectHighlightedResultsInEtabsCommand = new RelayCommand(SelectHighlightedResultsInEtabs, _ => !IsBusy);
    }

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<ModelCompareResultRowViewModel> Results { get; } = [];
    public ObservableCollection<ModelCompareObjectResultViewModel> ObjectResults { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];
    public ICollectionView FilteredResults { get; }
    public IReadOnlyList<ModelCompareConfidenceLevel> MovedFrameConfidenceLevels { get; } = Enum.GetValues<ModelCompareConfidenceLevel>();
    public IReadOnlyList<string> ReviewStatusOptions { get; } = ["Unreviewed", "Reviewed", "Ignored", "Needs checking"];
    public IReadOnlyList<string> ChangeTypeFilterOptions { get; } = ["All", .. Enum.GetNames<ModelCompareChangeType>()];
    public IReadOnlyList<string> ObjectTypeFilterOptions { get; } = ["All", .. Enum.GetNames<ModelCompareObjectType>()];
    public IReadOnlyList<string> ConfidenceFilterOptions { get; } = ["All", .. Enum.GetNames<ModelCompareConfidenceLevel>()];
    public IReadOnlyList<string> MemberTypeFilterOptions { get; } = ["All", "Beam", "Column", "Brace", "Area", "Other"];
    public ObservableCollection<string> StoryFilterOptions { get; } = ["All"];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand CreateEtabsSnapshotCommand { get; }
    public ICommand AssignMemberIdsCommand { get; }
    public ICommand BrowseOldSnapshotCommand { get; }
    public ICommand BrowseNewSnapshotCommand { get; }
    public ICommand LoadOldSnapshotCommand { get; }
    public ICommand LoadNewSnapshotCommand { get; }
    public ICommand CompareSnapshotsCommand { get; }
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
                OnPropertyChanged(nameof(BusyVisibility));
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public double CoordinateToleranceMm
    {
        get => _coordinateToleranceMm;
        set
        {
            double normalized = double.IsFinite(value) && value > 0 ? value : 1.0;
            if (SetProperty(ref _coordinateToleranceMm, normalized))
                ClearComparisonResults();
        }
    }

    public double LengthToleranceMm
    {
        get => _lengthToleranceMm;
        set
        {
            double normalized = double.IsFinite(value) && value > 0 ? value : 1.0;
            if (SetProperty(ref _lengthToleranceMm, normalized))
                ClearComparisonResults();
        }
    }

    public double OrientationToleranceDegrees
    {
        get => _orientationToleranceDegrees;
        set
        {
            double normalized = double.IsFinite(value) && value > 0 ? value : 0.1;
            if (SetProperty(ref _orientationToleranceDegrees, normalized))
                ClearComparisonResults();
        }
    }

    public double MovedFrameSearchDistanceMm
    {
        get => _movedFrameSearchDistanceMm;
        set
        {
            double normalized = double.IsFinite(value) && value > 0 ? value : 500.0;
            if (SetProperty(ref _movedFrameSearchDistanceMm, normalized))
                ClearComparisonResults();
        }
    }

    public ModelCompareConfidenceLevel MinimumMovedFrameConfidence
    {
        get => _minimumMovedFrameConfidence;
        set
        {
            if (SetProperty(ref _minimumMovedFrameConfidence, value))
                ClearComparisonResults();
        }
    }

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

    private void AssignMemberIds()
    {
        MessageBoxResult confirmation = MessageBox.Show(
            "This assigns a persistent member ID to every frame in the selected ETABS model that does not already have one.\n\n" +
            "The model will be unlocked (which clears any existing analysis results), and you should save the model afterwards so the IDs persist for future comparisons.\n\n" +
            "Continue?",
            "Assign member IDs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        RunCommand("Assigning member IDs in ETABS...", () =>
        {
            ModelCompareMemberIdResult result = _etabsService.AssignFrameMemberIds(new ModelCompareMemberIdRequest
            {
                EtabsInstanceId = SelectedEtabsInstanceId
            });
            ShowMessages(
                result.Warnings,
                result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info,
                result.Message);
        });
    }

    private void RefreshEtabsInstances()
    {
        RunCommand("Refreshing ETABS instances...", () =>
        {
            EtabsInstanceListResult result = _etabsService.ListEtabsInstances();
            string previousId = SelectedEtabsInstanceId;
            ReplaceCollection(EtabsInstances, result.Instances);
            SelectedEtabsInstance = EtabsInstances.FirstOrDefault(instance =>
                string.Equals(instance.Id, previousId, StringComparison.OrdinalIgnoreCase)) ??
                EtabsInstances.FirstOrDefault();

            ConnectionStatus = result.Message;
            ShowMessages(result.Warnings, result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        });
    }

    private void CreateEtabsSnapshot()
    {
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

        RunCommand("Creating ETABS model snapshot...", () =>
        {
            ModelCompareSnapshotResult snapshotResult = _etabsService.ExtractModelCompareSnapshot(new ModelCompareSnapshotRequest
            {
                EtabsInstanceId = SelectedEtabsInstanceId
            });

            if (snapshotResult.Snapshot == null || snapshotResult.IsError)
            {
                ConnectionStatus = "Snapshot failed";
                ShowMessages(snapshotResult.Warnings, ValidationSeverity.Critical, snapshotResult.Message);
                return;
            }

            ModelCompareSnapshotSaveResult saveResult = _jsonService.SaveSnapshot(snapshotResult.Snapshot, dialog.FileName);
            if (!saveResult.IsError)
            {
                NewSnapshotPath = saveResult.FilePath;
                _newSnapshot = snapshotResult.Snapshot;
                OnPropertyChanged(nameof(NewSnapshotSummary));
            }

            ConnectionStatus = "Connected";
            string message = saveResult.IsError ? saveResult.Message : $"{snapshotResult.Message} {saveResult.Message}";
            ShowMessages(snapshotResult.Warnings, saveResult.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, message);
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
            ReplaceCollection(Results, comparison.Differences.Select(row => new ModelCompareResultRowViewModel(row)));
            ReplaceObjectResults(BuildObjectResults(Results));
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
        });
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

    private void SelectResultsInEtabs(IReadOnlyList<ModelCompareResultRowViewModel> rows)
    {
        RunCommand("Selecting comparison objects in ETABS...", () =>
        {
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

            ModelCompareEtabsSelectionResult result = _etabsService.SelectModelCompareObjects(
                new ModelCompareEtabsSelectionRequest
                {
                    EtabsInstanceId = SelectedEtabsInstanceId,
                    Targets = targets
                });

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
        RebuildStoryFilterOptions();
        RefreshSummaryCounts();
        RefreshResultFilter();
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

    private ModelCompareToleranceSettings BuildToleranceSettings()
    {
        double coordinateTolerance = CoordinateToleranceMm / 1000.0;
        double lengthTolerance = LengthToleranceMm / 1000.0;
        double movementSearchDistance = MovedFrameSearchDistanceMm / 1000.0;

        return new ModelCompareToleranceSettings
        {
            CoordinateTolerance = coordinateTolerance,
            LengthTolerance = lengthTolerance,
            MovedFrameLengthTolerance = lengthTolerance,
            MovedFrameOrientationToleranceDegrees = OrientationToleranceDegrees,
            MovementSearchDistance = movementSearchDistance,
            MovementTolerance = movementSearchDistance,
            MinimumMovedFrameConfidence = MinimumMovedFrameConfidence
        };
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
