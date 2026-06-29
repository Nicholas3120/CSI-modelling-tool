using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;
using CSIModellingTools.ViewModels;
using Microsoft.Win32;

namespace CSIModellingTools.Features.IfcImport;

public sealed class IfcStructuralImportViewModel : ObservableObject
{
    private const int MaximumDisplayedRows = 1000;
    private readonly IfcImportService _importService = new();
    private readonly IfcImportJsonExporter _jsonExporter = new();
    private readonly ImportReviewReportGenerator _reportGenerator = new();
    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly IEtabsFrameExporter _etabsExporter = new EtabsFrameExporter();
    private IfcImportResult? _currentResult;
    private EtabsInstanceInfo? _selectedEtabsInstance;
    private string _ifcFilePath = "";
    private string _jsonOutputPath = "";
    private string _reportOutputPath = "";
    private string _operationStatus = "Choose an IFC file to begin.";
    private string _etabsExportStatus = "ETABS export is available after a successful import.";
    private bool _includeBeams = true;
    private bool _includeColumns = true;
    private bool _resetCoordinateOrigin = true;
    private bool _exportMediumConfidenceToEtabs = true;
    private bool _useDefaultSectionForUnknown = true;
    private bool _useDefaultMaterialForUnknown;
    private bool _isBusy;
    private double _nodeSnapToleranceMm = 20.0;
    private CancellationTokenSource? _importCts;
    private double _progressPercent;
    private string _progressText = "";
    private bool _isProgressDeterminate;

    public IfcStructuralImportViewModel()
    {
        BrowseIfcCommand = new RelayCommand(_ => BrowseIfc(), _ => !IsBusy);
        RunImportCommand = new RelayCommand(_ => RunImport(), _ => !IsBusy);
        CancelImportCommand = new RelayCommand(_ => CancelImport(), _ => IsBusy && _importCts is { IsCancellationRequested: false });
        ExportJsonCommand = new RelayCommand(_ => ExportJson(), _ => !IsBusy && HasResult);
        OpenJsonCommand = new RelayCommand(_ => OpenJson(), _ => !IsBusy && HasResult);
        ExportReportCommand = new RelayCommand(_ => ExportReport(), _ => !IsBusy && HasResult);
        OpenReportCommand = new RelayCommand(_ => OpenReport(), _ => !IsBusy && HasResult);
        RefreshEtabsInstancesCommand = new RelayCommand(_ => RefreshEtabsInstances(), _ => !IsBusy);
        ExportToEtabsCommand = new RelayCommand(_ => ExportToEtabs(), _ => !IsBusy && HasResult);
    }

    public ObservableCollection<IfcImportWarning> Warnings { get; } = [];
    public ObservableCollection<SkippedIfcElement> SkippedElements { get; } = [];
    public ObservableCollection<EtabsInstanceInfo> EtabsInstances { get; } = [];

    public ICommand BrowseIfcCommand { get; }
    public ICommand RunImportCommand { get; }
    public ICommand CancelImportCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand OpenJsonCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand RefreshEtabsInstancesCommand { get; }
    public ICommand ExportToEtabsCommand { get; }

    public string IfcFilePath
    {
        get => _ifcFilePath;
        set
        {
            if (SetProperty(ref _ifcFilePath, value ?? ""))
                SetDefaultOutputPaths();
        }
    }

    public bool IncludeBeams
    {
        get => _includeBeams;
        set => SetProperty(ref _includeBeams, value);
    }

    public bool IncludeColumns
    {
        get => _includeColumns;
        set => SetProperty(ref _includeColumns, value);
    }

    public double NodeSnapToleranceMm
    {
        get => _nodeSnapToleranceMm;
        set => SetProperty(ref _nodeSnapToleranceMm, Math.Max(0.0, value));
    }

    public bool ResetCoordinateOrigin
    {
        get => _resetCoordinateOrigin;
        set => SetProperty(ref _resetCoordinateOrigin, value);
    }

    public bool ExportMediumConfidenceToEtabs
    {
        get => _exportMediumConfidenceToEtabs;
        set => SetProperty(ref _exportMediumConfidenceToEtabs, value);
    }

    public bool UseDefaultSectionForUnknown
    {
        get => _useDefaultSectionForUnknown;
        set => SetProperty(ref _useDefaultSectionForUnknown, value);
    }

    public bool UseDefaultMaterialForUnknown
    {
        get => _useDefaultMaterialForUnknown;
        set => SetProperty(ref _useDefaultMaterialForUnknown, value);
    }

    public string JsonOutputPath
    {
        get => _jsonOutputPath;
        set => SetProperty(ref _jsonOutputPath, value ?? "");
    }

    public string ReportOutputPath
    {
        get => _reportOutputPath;
        set => SetProperty(ref _reportOutputPath, value ?? "");
    }

    public string OperationStatus
    {
        get => _operationStatus;
        set => SetProperty(ref _operationStatus, value);
    }

    public string EtabsExportStatus
    {
        get => _etabsExportStatus;
        set => SetProperty(ref _etabsExportStatus, value);
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

    // The bar shows percentage once element counts are known; until then (file open /
    // model read) it animates as indeterminate.
    public bool IsProgressIndeterminate => IsBusy && !_isProgressDeterminate;

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
    public bool HasResult => _currentResult != null;
    public int ImportedCount => _currentResult?.ImportedCount ?? 0;
    public int SkippedCount => _currentResult?.SkippedCount ?? 0;
    public int WarningCount => _currentResult?.WarningCount ?? 0;
    public string GeometrySummary => BuildGeometrySummary(_currentResult);

    private void BrowseIfc()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open IFC Structural Model",
            Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*",
            DefaultExt = ".ifc",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            IfcFilePath = dialog.FileName;
    }

    private async void RunImport()
    {
        if (IsBusy)
            return;

        if (!File.Exists(IfcFilePath))
        {
            OperationStatus = "Choose an existing IFC file before importing.";
            return;
        }

        // Build options and capture inputs on the UI thread before handing the heavy
        // parse/recognition work to a background thread, so the window stays responsive.
        var options = new IfcImportOptions
        {
            IncludeBeams = IncludeBeams,
            IncludeColumns = IncludeColumns,
            NodeSnapTolerance = NodeSnapToleranceMm / 1000.0,
            CoordinateOriginReset = ResetCoordinateOrigin
                ? IfcImportCoordinateOriginMode.ResetToFirstImportedPoint
                : IfcImportCoordinateOriginMode.PreserveIfcCoordinates
        };
        string ifcPath = IfcFilePath;

        _importCts = new CancellationTokenSource();
        CancellationToken token = _importCts.Token;
        // Constructed on the UI thread, so callbacks are marshalled back to it.
        var progress = new Progress<IfcImportProgress>(OnImportProgress);
        IsBusy = true;
        ResetProgress(indeterminate: true, "Importing IFC structural frames...");
        OperationStatus = "Importing IFC structural frames...";
        CommandManager.InvalidateRequerySuggested();

        try
        {
            IfcImportResult result = await Task.Run(
                () => _importService.ImportStructuralFrames(ifcPath, options, token, progress),
                token);

            _currentResult = result;
            ReplaceCollection(Warnings, result.Warnings.Take(MaximumDisplayedRows));
            ReplaceCollection(SkippedElements, result.SkippedElements.Take(MaximumDisplayedRows));
            RefreshResultProperties();
            string displayLimitMessage = result.Warnings.Count > MaximumDisplayedRows || result.SkippedElements.Count > MaximumDisplayedRows
                ? $" Showing first {MaximumDisplayedRows} warning/skipped rows; JSON and report include all results."
                : "";
            string offsetMessage = result.CoordinateOffset.Applied
                ? $" Origin shifted by X={result.CoordinateOffset.X:0.##}, Y={result.CoordinateOffset.Y:0.##}, Z={result.CoordinateOffset.Z:0.##} m so the model sits at the ETABS origin."
                : "";
            OperationStatus = $"Imported {result.ImportedCount} element(s), skipped {result.SkippedCount}, warnings {result.WarningCount}. {GeometrySummary}{offsetMessage}{displayLimitMessage}";
            EtabsExportStatus = "Ready to export high-confidence recognised frames to ETABS.";
        }
        catch (OperationCanceledException)
        {
            OperationStatus = "Import cancelled.";
        }
        catch (Exception ex)
        {
            OperationStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            _importCts.Dispose();
            _importCts = null;
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CancelImport()
    {
        if (_importCts is { IsCancellationRequested: false })
        {
            _importCts.Cancel();
            OperationStatus = "Cancelling import...";
            ProgressText = "Cancelling...";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnImportProgress(IfcImportProgress update)
    {
        if (_isProgressDeterminate != update.IsDeterminate)
        {
            _isProgressDeterminate = update.IsDeterminate;
            OnPropertyChanged(nameof(IsProgressIndeterminate));
        }

        ProgressPercent = update.IsDeterminate ? update.Percent : 0;
        ProgressText = update.IsDeterminate ? $"{update.Percent:0}% — {update.Stage}" : update.Stage;
    }

    private void ResetProgress(bool indeterminate, string text)
    {
        ProgressPercent = 0;
        ProgressText = text;
        bool determinate = !indeterminate;
        if (_isProgressDeterminate != determinate)
        {
            _isProgressDeterminate = determinate;
            OnPropertyChanged(nameof(IsProgressIndeterminate));
        }
    }

    private void ExportJson()
    {
        RunCommand("Exporting IFC import JSON...", () =>
        {
            if (!EnsureResult())
                return;

            if (string.IsNullOrWhiteSpace(JsonOutputPath))
                JsonOutputPath = ChooseSavePath("Save IFC Import JSON", "IFC import result (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*", "ifc-frames.json") ?? "";

            if (string.IsNullOrWhiteSpace(JsonOutputPath))
            {
                OperationStatus = "JSON export cancelled.";
                return;
            }

            _jsonExporter.WriteResult(_currentResult!, JsonOutputPath);
            OperationStatus = "JSON result exported.";
        });
    }

    private void OpenJson()
    {
        RunCommand("Opening IFC import JSON...", () =>
        {
            if (!EnsureJsonExported())
                return;

            OpenFile(JsonOutputPath);
            OperationStatus = "JSON result opened.";
        });
    }

    private void ExportReport()
    {
        RunCommand("Exporting IFC import review report...", () =>
        {
            if (!EnsureResult())
                return;

            if (string.IsNullOrWhiteSpace(ReportOutputPath))
                ReportOutputPath = ChooseSavePath("Save IFC Import Review Report", "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt|All files (*.*)|*.*", "ifc-frames-review.md") ?? "";

            if (string.IsNullOrWhiteSpace(ReportOutputPath))
            {
                OperationStatus = "Review report export cancelled.";
                return;
            }

            _reportGenerator.WriteMarkdownReport(_currentResult!, ReportOutputPath);
            OperationStatus = "Review report exported.";
        });
    }

    private void OpenReport()
    {
        RunCommand("Opening IFC import review report...", () =>
        {
            if (!EnsureReportExported())
                return;

            OpenFile(ReportOutputPath);
            OperationStatus = "Review report opened.";
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

            EtabsExportStatus = result.Message;
        });
    }

    private void ExportToEtabs()
    {
        RunCommand("Exporting IFC frames to ETABS...", () =>
        {
            if (!EnsureResult())
                return;
            if (_currentResult!.Frames.Count == 0)
            {
                EtabsExportStatus = "No imported IFC frame elements are available for ETABS export.";
                OperationStatus = "ETABS export skipped.";
                return;
            }

            var options = new EtabsExportOptions
            {
                EtabsInstanceId = SelectedEtabsInstanceId,
                ExportOnlyHighConfidence = !ExportMediumConfidenceToEtabs,
                ExportMediumConfidenceWithWarnings = ExportMediumConfidenceToEtabs,
                SkipUnknownMaterials = !UseDefaultMaterialForUnknown,
                SkipUnknownSections = !UseDefaultSectionForUnknown,
                PreserveSourceGuid = true
            };

            EtabsExportResult result = _etabsExporter.ExportFramesToEtabs(_currentResult!, options);
            string topWarningSummary = BuildEtabsWarningSummary(result);
            EtabsExportStatus = $"{result.Message} Exported {result.ExportedFrameCount}, skipped {result.SkippedFrameCount}, warnings {result.Warnings.Count}.{topWarningSummary}";
            OperationStatus = result.IsError ? "ETABS export failed." : "ETABS export complete.";
        });
    }

    private static string BuildEtabsWarningSummary(EtabsExportResult result)
    {
        if (result.Warnings.Count == 0)
            return "";

        string summary = string.Join(
            "; ",
            result.Warnings
                .GroupBy(warning => warning.Category)
                .OrderByDescending(group => group.Count())
                .Take(3)
                .Select(group => $" {group.Key}: {group.Count()}"));
        return " Top issues:" + summary + ".";
    }

    private bool EnsureJsonExported()
    {
        if (!EnsureResult())
            return false;

        if (string.IsNullOrWhiteSpace(JsonOutputPath))
            JsonOutputPath = DefaultJsonPath();

        if (!File.Exists(JsonOutputPath))
            _jsonExporter.WriteResult(_currentResult!, JsonOutputPath);

        return true;
    }

    private bool EnsureReportExported()
    {
        if (!EnsureResult())
            return false;

        if (string.IsNullOrWhiteSpace(ReportOutputPath))
            ReportOutputPath = DefaultReportPath();

        if (!File.Exists(ReportOutputPath))
            _reportGenerator.WriteMarkdownReport(_currentResult!, ReportOutputPath);

        return true;
    }

    private bool EnsureResult()
    {
        if (_currentResult != null)
            return true;

        OperationStatus = "Run an IFC import before exporting results.";
        return false;
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
        }
        catch (Exception ex)
        {
            OperationStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Mouse.OverrideCursor = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void RefreshResultProperties()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ImportedCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(GeometrySummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private static string BuildGeometrySummary(IfcImportResult? result)
    {
        if (result == null || result.Frames.Count == 0)
            return "No imported frame geometry.";

        var points = result.Frames.SelectMany(frame => new[] { frame.StartPoint, frame.EndPoint }).ToList();
        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        double minZ = points.Min(point => point.Z);
        double maxZ = points.Max(point => point.Z);
        double averageLength = result.Frames
            .Select(frame => Distance(frame.StartPoint, frame.EndPoint))
            .DefaultIfEmpty()
            .Average();

        return $"Extents: X {maxX - minX:0.##} m, Y {maxY - minY:0.##} m, Z {maxZ - minZ:0.##} m; average frame length {averageLength:0.##} m.";
    }

    private static double Distance(AnalyticalPoint first, AnalyticalPoint second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private void SetDefaultOutputPaths()
    {
        if (!string.IsNullOrWhiteSpace(IfcFilePath))
        {
            JsonOutputPath = DefaultJsonPath();
            ReportOutputPath = DefaultReportPath();
        }
    }

    private string DefaultJsonPath()
    {
        return BuildDefaultOutputPath("ifc-frames.json");
    }

    private string DefaultReportPath()
    {
        return BuildDefaultOutputPath("ifc-frames-review.md");
    }

    private string BuildDefaultOutputPath(string suffix)
    {
        string directory = Path.GetDirectoryName(IfcFilePath) ?? "";
        string fileName = Path.GetFileNameWithoutExtension(IfcFilePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "ifc-structural-import";

        return Path.Combine(directory, $"{fileName}.{suffix}");
    }

    private static string? ChooseSavePath(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = fileName,
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The export file could not be found.", path);

        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
