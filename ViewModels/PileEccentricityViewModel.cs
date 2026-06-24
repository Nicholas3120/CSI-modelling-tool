using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class PileEccentricityViewModel : ObservableObject
{
    private readonly PileEccentricityCalculator _calculator = new();
    private PileEccentricityPileGroupRow? _selectedPileGroup;
    private PileEccentricityPileRow? _selectedPile;
    private PileEccentricityTieBeamRow? _selectedTieBeam;
    private PileEccentricityTieBeamSummary? _selectedTieBeamSummary;
    private bool _useIdealTieBeamTransfer;
    private bool _suspendAutoRefresh;
    private bool _isRefreshing;
    private string _calculationStatus = "Ready";
    private PileEccentricityPreviewModel _currentPreview = new();

    private string _builderLocalGroupId = "PG1";
    private string _builderAdjacentGroupId = "PG2";
    private double _builderLocalColumnX;
    private double _builderLocalColumnY;
    private double _builderColumnSpacingX = 5.25;
    private double _builderColumnSpacingY;
    private string _builderLocalPileGroupLayout = StandardPileGroupLayoutLabels.FourPiles;
    private string _builderAdjacentPileGroupLayout = StandardPileGroupLayoutLabels.FourPiles;
    private double _builderPileDiameter = 0.6;
    private double _builderSpacingDiameters = 3.0;
    private double _builderLocalPileCapRotationDegrees;
    private double _builderAdjacentPileCapRotationDegrees;
    private double _builderLocalPileShiftX = 0.25;
    private double _builderLocalPileShiftY;
    private double _builderAdjacentPileShiftX;
    private double _builderAdjacentPileShiftY;
    private double _builderLocalLoadkN = 4000.0;
    private double _builderAdjacentLoadkN = 3000.0;
    private double _builderCompressionCapacitykN = 1200.0;
    private double _builderTensionCapacitykN = 300.0;

    public PileEccentricityViewModel()
    {
        WatchCollection(PileGroups);
        WatchCollection(Piles);
        WatchCollection(TieBeams);

        AddPileGroupCommand = new RelayCommand(_ => AddPileGroup());
        RemovePileGroupCommand = new RelayCommand(_ => RemoveSelectedPileGroup(), _ => SelectedPileGroup != null);
        AddPileCommand = new RelayCommand(_ => AddPile());
        RemovePileCommand = new RelayCommand(_ => RemoveSelectedPile(), _ => SelectedPile != null);
        AddTieBeamCommand = new RelayCommand(_ => AddTieBeam());
        RemoveTieBeamCommand = new RelayCommand(_ => RemoveSelectedTieBeam(), _ => SelectedTieBeam != null);
        CalculateIsolatedCommand = new RelayCommand(_ => Calculate(false));
        RecalculateWithTieBeamCommand = new RelayCommand(_ => Calculate(true));
        ApplyLayoutBuilderCommand = new RelayCommand(_ => ApplyLayoutBuilder());
        LoadExampleCommand = new RelayCommand(_ => LoadExample());

        LoadExample();
    }

    public ObservableCollection<PileEccentricityPileGroupRow> PileGroups { get; } = [];
    public ObservableCollection<PileEccentricityPileRow> Piles { get; } = [];
    public ObservableCollection<PileEccentricityTieBeamRow> TieBeams { get; } = [];
    public ObservableCollection<PileEccentricityGeometrySummary> GeometrySummaries { get; } = [];
    public ObservableCollection<PileEccentricityPileLoadResult> IsolatedPileLoads { get; } = [];
    public ObservableCollection<PileEccentricityTieBeamSummary> TieBeamSummaries { get; } = [];
    public ObservableCollection<PileEccentricityPileLoadResult> RevisedPileLoads { get; } = [];
    public ObservableCollection<PileEccentricityComparisonRow> Comparisons { get; } = [];
    public ObservableCollection<PileEccentricityCalculationStep> CalculationSteps { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public IReadOnlyList<string> DirectionEffects => TieBeamDirectionEffectLabels.All;
    public IReadOnlyList<string> StandardPileGroupLayouts => StandardPileGroupLayoutLabels.All;

    public ICommand AddPileGroupCommand { get; }
    public ICommand RemovePileGroupCommand { get; }
    public ICommand AddPileCommand { get; }
    public ICommand RemovePileCommand { get; }
    public ICommand AddTieBeamCommand { get; }
    public ICommand RemoveTieBeamCommand { get; }
    public ICommand CalculateIsolatedCommand { get; }
    public ICommand RecalculateWithTieBeamCommand { get; }
    public ICommand ApplyLayoutBuilderCommand { get; }
    public ICommand LoadExampleCommand { get; }

    public PileEccentricityPileGroupRow? SelectedPileGroup
    {
        get => _selectedPileGroup;
        set
        {
            if (SetProperty(ref _selectedPileGroup, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public PileEccentricityPileRow? SelectedPile
    {
        get => _selectedPile;
        set
        {
            if (SetProperty(ref _selectedPile, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public PileEccentricityTieBeamRow? SelectedTieBeam
    {
        get => _selectedTieBeam;
        set
        {
            if (SetProperty(ref _selectedTieBeam, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public PileEccentricityTieBeamSummary? SelectedTieBeamSummary
    {
        get => _selectedTieBeamSummary;
        set => SetProperty(ref _selectedTieBeamSummary, value);
    }

    public bool UseIdealTieBeamTransfer
    {
        get => _useIdealTieBeamTransfer;
        set
        {
            if (SetProperty(ref _useIdealTieBeamTransfer, value))
                RefreshCalculation();
        }
    }

    public string CalculationStatus
    {
        get => _calculationStatus;
        set => SetProperty(ref _calculationStatus, value);
    }

    public PileEccentricityPreviewModel CurrentPreview
    {
        get => _currentPreview;
        private set => SetProperty(ref _currentPreview, value);
    }

    public string ResultSummary =>
        $"{GeometrySummaries.Count} group(s), {IsolatedPileLoads.Count} isolated pile row(s), {TieBeamSummaries.Count} tie transfer row(s)";

    public string BuilderLocalGroupId { get => _builderLocalGroupId; set => SetBuilderProperty(ref _builderLocalGroupId, value ?? "PG1"); }
    public string BuilderAdjacentGroupId { get => _builderAdjacentGroupId; set => SetBuilderProperty(ref _builderAdjacentGroupId, value ?? "PG2"); }
    public double BuilderLocalColumnX { get => _builderLocalColumnX; set => SetBuilderFinite(ref _builderLocalColumnX, value); }
    public double BuilderLocalColumnY { get => _builderLocalColumnY; set => SetBuilderFinite(ref _builderLocalColumnY, value); }
    public double BuilderColumnSpacingX { get => _builderColumnSpacingX; set => SetBuilderFinite(ref _builderColumnSpacingX, value); }
    public double BuilderColumnSpacingY { get => _builderColumnSpacingY; set => SetBuilderFinite(ref _builderColumnSpacingY, value); }
    public string BuilderLocalPileGroupLayout { get => _builderLocalPileGroupLayout; set => SetBuilderProperty(ref _builderLocalPileGroupLayout, NormalizePileGroupLayout(value)); }
    public string BuilderAdjacentPileGroupLayout { get => _builderAdjacentPileGroupLayout; set => SetBuilderProperty(ref _builderAdjacentPileGroupLayout, NormalizePileGroupLayout(value)); }

    public double BuilderPileDiameter
    {
        get => _builderPileDiameter;
        set
        {
            if (SetPositive(ref _builderPileDiameter, value, 0.6))
            {
                OnPropertyChanged(nameof(BuilderSpacingDisplay));
                OnPropertyChanged(nameof(BuilderPileDiameterMm));
                RebuildLayoutFromBuilderChange();
            }
        }
    }

    public double BuilderPileDiameterMm
    {
        get => BuilderPileDiameter * 1000.0;
        set => BuilderPileDiameter = value / 1000.0;
    }

    public double BuilderSpacingDiameters
    {
        get => _builderSpacingDiameters;
        set
        {
            if (SetPositive(ref _builderSpacingDiameters, value, 3.0))
            {
                OnPropertyChanged(nameof(BuilderSpacingDisplay));
                RebuildLayoutFromBuilderChange();
            }
        }
    }

    public string BuilderSpacingDisplay => $"{BuilderPileDiameter * BuilderSpacingDiameters * 1000.0:0.#} mm ({BuilderPileDiameter * BuilderSpacingDiameters:0.###} m) c/c";

    public double BuilderLocalPileCapRotationDegrees { get => _builderLocalPileCapRotationDegrees; set => SetBuilderFinite(ref _builderLocalPileCapRotationDegrees, value); }
    public double BuilderAdjacentPileCapRotationDegrees { get => _builderAdjacentPileCapRotationDegrees; set => SetBuilderFinite(ref _builderAdjacentPileCapRotationDegrees, value); }
    public double BuilderLocalPileShiftX { get => _builderLocalPileShiftX; set => SetBuilderShift(ref _builderLocalPileShiftX, value, nameof(BuilderLocalPileShiftX), nameof(BuilderLocalPileShiftXmm)); }
    public double BuilderLocalPileShiftY { get => _builderLocalPileShiftY; set => SetBuilderShift(ref _builderLocalPileShiftY, value, nameof(BuilderLocalPileShiftY), nameof(BuilderLocalPileShiftYmm)); }
    public double BuilderAdjacentPileShiftX { get => _builderAdjacentPileShiftX; set => SetBuilderShift(ref _builderAdjacentPileShiftX, value, nameof(BuilderAdjacentPileShiftX), nameof(BuilderAdjacentPileShiftXmm)); }
    public double BuilderAdjacentPileShiftY { get => _builderAdjacentPileShiftY; set => SetBuilderShift(ref _builderAdjacentPileShiftY, value, nameof(BuilderAdjacentPileShiftY), nameof(BuilderAdjacentPileShiftYmm)); }
    public double BuilderLocalPileShiftXmm { get => BuilderLocalPileShiftX * 1000.0; set => BuilderLocalPileShiftX = value / 1000.0; }
    public double BuilderLocalPileShiftYmm { get => BuilderLocalPileShiftY * 1000.0; set => BuilderLocalPileShiftY = value / 1000.0; }
    public double BuilderAdjacentPileShiftXmm { get => BuilderAdjacentPileShiftX * 1000.0; set => BuilderAdjacentPileShiftX = value / 1000.0; }
    public double BuilderAdjacentPileShiftYmm { get => BuilderAdjacentPileShiftY * 1000.0; set => BuilderAdjacentPileShiftY = value / 1000.0; }
    public double BuilderLocalLoadkN { get => _builderLocalLoadkN; set => SetBuilderFinite(ref _builderLocalLoadkN, value); }
    public double BuilderAdjacentLoadkN { get => _builderAdjacentLoadkN; set => SetBuilderFinite(ref _builderAdjacentLoadkN, value); }
    public double BuilderCompressionCapacitykN { get => _builderCompressionCapacitykN; set => SetBuilderNonNegative(ref _builderCompressionCapacitykN, value); }
    public double BuilderTensionCapacitykN { get => _builderTensionCapacitykN; set => SetBuilderNonNegative(ref _builderTensionCapacitykN, value); }

    private void LoadExample()
    {
        _suspendAutoRefresh = true;
        try
        {
            BuilderLocalGroupId = "PG1";
            BuilderAdjacentGroupId = "PG2";
            BuilderLocalColumnX = 0.0;
            BuilderLocalColumnY = 0.0;
            BuilderColumnSpacingX = 5.25;
            BuilderColumnSpacingY = 0.0;
            BuilderLocalPileGroupLayout = StandardPileGroupLayoutLabels.FourPiles;
            BuilderAdjacentPileGroupLayout = StandardPileGroupLayoutLabels.FourPiles;
            BuilderPileDiameter = 0.6;
            BuilderSpacingDiameters = 3.0;
            BuilderLocalPileCapRotationDegrees = 0.0;
            BuilderAdjacentPileCapRotationDegrees = 0.0;
            BuilderLocalPileShiftX = 0.25;
            BuilderLocalPileShiftY = 0.0;
            BuilderAdjacentPileShiftX = 0.0;
            BuilderAdjacentPileShiftY = 0.0;
            BuilderLocalLoadkN = 4000.0;
            BuilderAdjacentLoadkN = 3000.0;
            BuilderCompressionCapacitykN = 1200.0;
            BuilderTensionCapacitykN = 300.0;
        }
        finally
        {
            _suspendAutoRefresh = false;
        }

        ApplyLayoutBuilder();
    }

    private void ApplyLayoutBuilder()
    {
        _suspendAutoRefresh = true;
        try
        {
            ClearInputs();

            string localGroupId = string.IsNullOrWhiteSpace(BuilderLocalGroupId) ? "PG1" : BuilderLocalGroupId.Trim();
            string adjacentGroupId = string.IsNullOrWhiteSpace(BuilderAdjacentGroupId) ? "PG2" : BuilderAdjacentGroupId.Trim();
            if (string.Equals(localGroupId, adjacentGroupId, StringComparison.OrdinalIgnoreCase))
                adjacentGroupId = BuildUniqueId("PG", [localGroupId], 2);

            double localColumnX = BuilderLocalColumnX;
            double localColumnY = BuilderLocalColumnY;
            double adjacentColumnX = localColumnX + BuilderColumnSpacingX;
            double adjacentColumnY = localColumnY + BuilderColumnSpacingY;

            var localGroup = new PileEccentricityPileGroupRow
            {
                GroupId = localGroupId,
                ColumnX = localColumnX,
                ColumnY = localColumnY,
                VerticalLoadNkN = BuilderLocalLoadkN,
                IsActive = true
            };
            var adjacentGroup = new PileEccentricityPileGroupRow
            {
                GroupId = adjacentGroupId,
                ColumnX = adjacentColumnX,
                ColumnY = adjacentColumnY,
                VerticalLoadNkN = BuilderAdjacentLoadkN,
                IsActive = true
            };

            PileGroups.Add(localGroup);
            PileGroups.Add(adjacentGroup);

            AddGeneratedStandardPileGroup(localGroupId, localColumnX, localColumnY, BuilderLocalPileGroupLayout, BuilderLocalPileCapRotationDegrees, BuilderLocalPileShiftX, BuilderLocalPileShiftY);
            AddGeneratedStandardPileGroup(adjacentGroupId, adjacentColumnX, adjacentColumnY, BuilderAdjacentPileGroupLayout, BuilderAdjacentPileCapRotationDegrees, BuilderAdjacentPileShiftX, BuilderAdjacentPileShiftY);

            TieBeams.Add(new PileEccentricityTieBeamRow
            {
                TieBeamId = "TB1",
                FromGroupId = localGroupId,
                ToGroupId = adjacentGroupId,
                Width = 0.6,
                Depth = 0.9,
                UseAutoLength = true,
                TransferPercentagePercent = 100.0,
                AutoDirectionByCoordinate = false,
                IsActive = true
            });

            SelectedPileGroup = localGroup;
            SelectedPile = Piles.FirstOrDefault();
            SelectedTieBeam = TieBeams.FirstOrDefault();
        }
        finally
        {
            _suspendAutoRefresh = false;
        }

        RefreshCalculation();
    }

    private void AddGeneratedStandardPileGroup(
        string groupId,
        double columnX,
        double columnY,
        string layout,
        double rotationDegrees,
        double shiftX,
        double shiftY)
    {
        double spacing = Math.Max(0.001, BuilderPileDiameter * BuilderSpacingDiameters);
        List<(double X, double Y)> localPositions = BuildStandardPilePositions(layout, spacing);
        double angle = rotationDegrees * Math.PI / 180.0;
        int pileIndex = 1;

        foreach ((double localX, double localY) in localPositions)
        {
            (double rotatedX, double rotatedY) = Rotate(localX, localY, angle);
            var pile = new PileEccentricityPileRow
            {
                GroupId = groupId,
                PileId = $"P{pileIndex}",
                Diameter = BuilderPileDiameter,
                CompressionCapacitykN = BuilderCompressionCapacitykN,
                TensionCapacitykN = BuilderTensionCapacitykN,
                VerticalStiffnessKv = 1.0,
                IsActive = true
            };

            pile.SetOriginalPosition(columnX + rotatedX, columnY + rotatedY);
            pile.ShiftX = shiftX;
            pile.ShiftY = shiftY;
            Piles.Add(pile);
            pileIndex++;
        }
    }

    private static (double X, double Y) Rotate(double x, double y, double angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        return (x * cos - y * sin, x * sin + y * cos);
    }

    private static List<(double X, double Y)> BuildStandardPilePositions(string layout, double spacing)
    {
        string normalized = NormalizePileGroupLayout(layout);
        return normalized switch
        {
            StandardPileGroupLayoutLabels.OnePile =>
            [
                (0.0, 0.0)
            ],
            StandardPileGroupLayoutLabels.TwoPiles =>
            [
                (-spacing / 2.0, 0.0),
                (spacing / 2.0, 0.0)
            ],
            StandardPileGroupLayoutLabels.ThreePiles =>
            [
                (-spacing / 2.0, -Math.Sqrt(3.0) * spacing / 6.0),
                (spacing / 2.0, -Math.Sqrt(3.0) * spacing / 6.0),
                (0.0, Math.Sqrt(3.0) * spacing / 3.0)
            ],
            _ =>
            [
                (-spacing / 2.0, -spacing / 2.0),
                (spacing / 2.0, -spacing / 2.0),
                (-spacing / 2.0, spacing / 2.0),
                (spacing / 2.0, spacing / 2.0)
            ]
        };
    }

    private void AddPileGroup()
    {
        string groupId = BuildUniqueId("PG", PileGroups.Select(group => group.GroupId), PileGroups.Count + 1);
        var group = new PileEccentricityPileGroupRow
        {
            GroupId = groupId,
            ColumnX = 0.0,
            ColumnY = 0.0,
            VerticalLoadNkN = 1000.0,
            IsActive = true
        };

        PileGroups.Add(group);
        SelectedPileGroup = group;
    }

    private void RemoveSelectedPileGroup()
    {
        if (SelectedPileGroup == null)
            return;

        string groupId = SelectedPileGroup.GroupId;
        PileGroups.Remove(SelectedPileGroup);
        foreach (PileEccentricityPileRow pile in Piles.Where(pile => string.Equals(pile.GroupId, groupId, StringComparison.OrdinalIgnoreCase)).ToList())
            Piles.Remove(pile);
        foreach (PileEccentricityTieBeamRow tieBeam in TieBeams
            .Where(tieBeam => string.Equals(tieBeam.FromGroupId, groupId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tieBeam.ToGroupId, groupId, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            TieBeams.Remove(tieBeam);
        }

        SelectedPileGroup = PileGroups.FirstOrDefault();
        SelectedPile = Piles.FirstOrDefault();
        SelectedTieBeam = TieBeams.FirstOrDefault();
    }

    private void AddPile()
    {
        string groupId = SelectedPileGroup?.GroupId ?? PileGroups.FirstOrDefault()?.GroupId ?? "PG1";
        int groupPileCount = Piles.Count(pile => string.Equals(pile.GroupId, groupId, StringComparison.OrdinalIgnoreCase)) + 1;
        string pileId = BuildUniqueId("P", Piles.Where(pile => string.Equals(pile.GroupId, groupId, StringComparison.OrdinalIgnoreCase)).Select(pile => pile.PileId), groupPileCount);
        var pileRow = new PileEccentricityPileRow
        {
            GroupId = groupId,
            PileId = pileId,
            Diameter = BuilderPileDiameter,
            CompressionCapacitykN = BuilderCompressionCapacitykN,
            TensionCapacitykN = BuilderTensionCapacitykN,
            VerticalStiffnessKv = 1.0,
            IsActive = true
        };
        pileRow.SetOriginalPosition(0.0, 0.0);

        Piles.Add(pileRow);
        SelectedPile = pileRow;
    }

    private void RemoveSelectedPile()
    {
        if (SelectedPile == null)
            return;

        Piles.Remove(SelectedPile);
        SelectedPile = Piles.FirstOrDefault();
    }

    private void AddTieBeam()
    {
        string fromGroup = SelectedPileGroup?.GroupId ?? PileGroups.FirstOrDefault()?.GroupId ?? "";
        string toGroup = PileGroups.FirstOrDefault(group => !string.Equals(group.GroupId, fromGroup, StringComparison.OrdinalIgnoreCase))?.GroupId ?? fromGroup;
        var tieBeam = new PileEccentricityTieBeamRow
        {
            TieBeamId = BuildUniqueId("TB", TieBeams.Select(tie => tie.TieBeamId), TieBeams.Count + 1),
            FromGroupId = fromGroup,
            ToGroupId = toGroup,
            Width = 0.6,
            Depth = 0.9,
            UseAutoLength = true,
            TransferPercentagePercent = 100.0,
            AutoDirectionByCoordinate = false,
            IsActive = true
        };

        TieBeams.Add(tieBeam);
        SelectedTieBeam = tieBeam;
    }

    private void RemoveSelectedTieBeam()
    {
        if (SelectedTieBeam == null)
            return;

        TieBeams.Remove(SelectedTieBeam);
        SelectedTieBeam = TieBeams.FirstOrDefault();
    }

    private void Calculate(bool useTieBeamTransfer)
    {
        if (_useIdealTieBeamTransfer != useTieBeamTransfer)
        {
            _useIdealTieBeamTransfer = useTieBeamTransfer;
            OnPropertyChanged(nameof(UseIdealTieBeamTransfer));
        }

        RefreshCalculation();
    }

    private void RefreshCalculation()
    {
        if (_suspendAutoRefresh || _isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            PileEccentricityCalculationResult result = _calculator.Calculate(new PileEccentricityCalculationInput
            {
                PileGroups = PileGroups.Select(group => group.Clone()).ToList(),
                Piles = Piles.Select(pile => pile.Clone()).ToList(),
                TieBeams = TieBeams.Select(tieBeam => tieBeam.Clone()).ToList(),
                UseIdealTieBeamTransfer = UseIdealTieBeamTransfer
            });

            ReplaceCollection(GeometrySummaries, result.GeometrySummaries);
            ReplaceCollection(IsolatedPileLoads, result.IsolatedPileLoads);
            ReplaceCollection(TieBeamSummaries, result.TieBeamSummaries);
            SelectedTieBeamSummary = SelectTieBeamSummary();
            ReplaceCollection(RevisedPileLoads, result.RevisedPileLoads);
            ReplaceCollection(Comparisons, result.Comparisons);
            ReplaceCollection(CalculationSteps, result.CalculationSteps);
            ReplaceCollection(Messages, result.Messages);
            CurrentPreview = result.Preview;
            CalculationStatus = UseIdealTieBeamTransfer
                ? "Preview/results auto-updated with ideal tie beam"
                : "Preview/results auto-updated for isolated group";
            OnPropertyChanged(nameof(ResultSummary));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private PileEccentricityTieBeamSummary? SelectTieBeamSummary()
    {
        if (TieBeamSummaries.Count == 0)
            return null;

        if (SelectedTieBeam != null)
        {
            PileEccentricityTieBeamSummary? matchingSummary = TieBeamSummaries.FirstOrDefault(summary =>
                string.Equals(summary.TieBeamId, SelectedTieBeam.TieBeamId, StringComparison.OrdinalIgnoreCase));
            if (matchingSummary != null)
                return matchingSummary;
        }

        return TieBeamSummaries.FirstOrDefault();
    }

    private void WatchCollection<T>(ObservableCollection<T> collection)
        where T : PileEccentricityEditableRow
    {
        collection.CollectionChanged += InputCollectionChanged;
    }

    private void InputCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (PileEccentricityEditableRow row in e.OldItems)
                row.PropertyChanged -= InputRowPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (PileEccentricityEditableRow row in e.NewItems)
                row.PropertyChanged += InputRowPropertyChanged;
        }

        RefreshCalculation();
    }

    private void InputRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshCalculation();
    }

    private void ClearInputs()
    {
        foreach (PileEccentricityPileGroupRow row in PileGroups)
            row.PropertyChanged -= InputRowPropertyChanged;
        foreach (PileEccentricityPileRow row in Piles)
            row.PropertyChanged -= InputRowPropertyChanged;
        foreach (PileEccentricityTieBeamRow row in TieBeams)
            row.PropertyChanged -= InputRowPropertyChanged;

        PileGroups.Clear();
        Piles.Clear();
        TieBeams.Clear();
    }

    private void SetBuilderShift(ref double field, double value, string propertyName, string millimetrePropertyName)
    {
        if (!SetProperty(ref field, double.IsFinite(value) ? value : 0.0, propertyName))
            return;

        OnPropertyChanged(millimetrePropertyName);
        RebuildLayoutFromBuilderChange();
    }

    private void SetBuilderProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return;

        RebuildLayoutFromBuilderChange();
    }

    private void SetBuilderFinite(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, double.IsFinite(value) ? value : 0.0, propertyName))
            return;

        RebuildLayoutFromBuilderChange();
    }

    private void SetBuilderNonNegative(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, double.IsFinite(value) ? Math.Max(0.0, value) : 0.0, propertyName))
            return;

        RebuildLayoutFromBuilderChange();
    }

    private void RebuildLayoutFromBuilderChange()
    {
        if (_suspendAutoRefresh || _isRefreshing)
            return;

        ApplyLayoutBuilder();
    }

    private bool SetFinite(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, double.IsFinite(value) ? value : 0.0, propertyName);
    }

    private bool SetPositive(ref double field, double value, double fallback, [CallerMemberName] string? propertyName = null)
    {
        double next = double.IsFinite(value) ? Math.Max(0.001, value) : fallback;
        return SetProperty(ref field, next, propertyName);
    }

    private bool SetNonNegative(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, double.IsFinite(value) ? Math.Max(0.0, value) : 0.0, propertyName);
    }

    private bool SetInt(ref int field, int value, int minimum, int maximum, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, Math.Clamp(value, minimum, maximum), propertyName);
    }

    private static string NormalizePileGroupLayout(string? value)
    {
        if (string.Equals(value, StandardPileGroupLayoutLabels.OnePile, StringComparison.OrdinalIgnoreCase))
            return StandardPileGroupLayoutLabels.OnePile;
        if (string.Equals(value, StandardPileGroupLayoutLabels.TwoPiles, StringComparison.OrdinalIgnoreCase))
            return StandardPileGroupLayoutLabels.TwoPiles;
        if (string.Equals(value, StandardPileGroupLayoutLabels.ThreePiles, StringComparison.OrdinalIgnoreCase))
            return StandardPileGroupLayoutLabels.ThreePiles;

        return StandardPileGroupLayoutLabels.FourPiles;
    }

    private static string BuildUniqueId(string prefix, IEnumerable<string> existingIds, int firstIndex)
    {
        HashSet<string> existing = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int index = Math.Max(1, firstIndex);
        string id;
        do
        {
            id = $"{prefix}{index}";
            index++;
        }
        while (existing.Contains(id));

        return id;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
