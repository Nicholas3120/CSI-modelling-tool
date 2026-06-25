using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;
using Microsoft.Win32;

namespace CSIModellingTools.ViewModels;

public sealed class ModelCompareViewModel : ObservableObject
{
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

    public ModelCompareViewModel()
    {
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances(), _ => !IsBusy);
        CreateEtabsSnapshotCommand = new RelayCommand(_ => CreateEtabsSnapshot(), _ => !IsBusy);
        BrowseOldSnapshotCommand = new RelayCommand(_ => BrowseOldSnapshot(), _ => !IsBusy);
        BrowseNewSnapshotCommand = new RelayCommand(_ => BrowseNewSnapshot(), _ => !IsBusy);
        LoadOldSnapshotCommand = new RelayCommand(_ => LoadOldSnapshot(), _ => !IsBusy);
        LoadNewSnapshotCommand = new RelayCommand(_ => LoadNewSnapshot(), _ => !IsBusy);
        CompareSnapshotsCommand = new RelayCommand(_ => CompareSnapshots(), _ => !IsBusy);
    }

    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];
    public ObservableCollection<ModelCompareResultRow> Results { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand CreateEtabsSnapshotCommand { get; }
    public ICommand BrowseOldSnapshotCommand { get; }
    public ICommand BrowseNewSnapshotCommand { get; }
    public ICommand LoadOldSnapshotCommand { get; }
    public ICommand LoadNewSnapshotCommand { get; }
    public ICommand CompareSnapshotsCommand { get; }

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
        set => SetProperty(ref _oldSnapshotPath, value ?? "");
    }

    public string NewSnapshotPath
    {
        get => _newSnapshotPath;
        set => SetProperty(ref _newSnapshotPath, value ?? "");
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

    public int FramesAddedCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Added);
    public int FramesDeletedCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ChangeType == ModelCompareChangeType.Removed);
    public int SectionChangesCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ObjectDescription.Contains("/ section", StringComparison.OrdinalIgnoreCase));
    public int MaterialChangesCount => Results.Count(row => row.ObjectType == ModelCompareObjectType.Frame && row.ObjectDescription.Contains("/ material", StringComparison.OrdinalIgnoreCase));
    public int PropertyDefinitionChangesCount => Results.Count(row =>
        row.ObjectType is ModelCompareObjectType.FrameProperty or ModelCompareObjectType.AreaProperty or ModelCompareObjectType.Material);

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
            ShowMessages([], result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        });
    }

    private void LoadNewSnapshot()
    {
        RunCommand("Loading new model snapshot...", () =>
        {
            ModelCompareSnapshotLoadResult result = LoadNewSnapshotCore();
            ShowMessages([], result.IsError ? ValidationSeverity.Critical : ValidationSeverity.Info, result.Message);
        });
    }

    private void CompareSnapshots()
    {
        RunCommand("Comparing model snapshots...", () =>
        {
            if (_oldSnapshot == null)
                LoadOldSnapshotCore();
            if (_newSnapshot == null)
                LoadNewSnapshotCore();

            if (_oldSnapshot == null || _newSnapshot == null)
            {
                ShowMessages([], ValidationSeverity.Critical, "Load both old and new model snapshots before comparing.");
                return;
            }

            ReplaceCollection(Results, _compareService.CompareSnapshots(_oldSnapshot, _newSnapshot));
            RefreshSummaryCounts();
            ShowMessages([], ValidationSeverity.Info, $"Comparison complete. Found {Results.Count} difference(s).");
        });
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

    private static string BuildSnapshotSummary(ModelCompareSnapshot snapshot)
    {
        string source = string.IsNullOrWhiteSpace(snapshot.Metadata.SourceModelFileName)
            ? "snapshot"
            : snapshot.Metadata.SourceModelFileName;

        return $"{source}: {snapshot.Frames.Count} frame(s), {snapshot.FrameProperties.Count} frame propertie(s), {snapshot.AreaProperties.Count} area propertie(s), {snapshot.Materials.Count} material(s)";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
