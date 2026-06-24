using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using TrussModelling.Models;
using TrussModelling.Services;

namespace TrussModelling.ViewModels;

public sealed class BeamSpliceConnectionViewModel : ObservableObject
{
    private readonly BeamSpliceConnectionCalculator _calculator = new();
    private double _designMomentKnM = 250.0;
    private double _designShearKn = 150.0;
    private double _leverArmM = 0.72;
    private double _beamDepthMm = 800.0;
    private double _boltDiameterMm = 20.0;
    private double _boltHoleDiameterMm = 22.0;
    private double _boltUltimateStrengthMpa = 800.0;
    private double _boltAlphaV = 0.6;
    private int _flangeBoltsPerSide = 8;
    private int _webBoltsPerSide = 8;
    private int _flangeBoltShearPlanes = 1;
    private int _webBoltShearPlanes = 1;
    private double _coverPlateWidthMm = 220.0;
    private double _coverPlateThicknessMm = 12.0;
    private double _webPlateDepthMm = 520.0;
    private double _webPlateThicknessMm = 10.0;
    private double _plateYieldStrengthMpa = 355.0;
    private double _plateUltimateStrengthMpa = 510.0;
    private double _endDistanceMm = 40.0;
    private double _edgeDistanceMm = 40.0;
    private double _pitchMm = 70.0;
    private double _gaugeMm = 80.0;
    private int _boltRowsAlongForce = 4;
    private int _boltLinesAcrossForce = 2;
    private double _gammaM0 = 1.0;
    private double _gammaM2 = 1.25;
    private BeamSpliceConnectionResult _result = new();

    public BeamSpliceConnectionViewModel()
    {
        Recalculate();
    }

    public ObservableCollection<BeamSpliceCheckResult> Checks { get; } = [];
    public ObservableCollection<string> Notes { get; } = [];

    public double DesignMomentKnM { get => _designMomentKnM; set => SetDoubleAndRecalculate(ref _designMomentKnM, value); }
    public double DesignShearKn { get => _designShearKn; set => SetDoubleAndRecalculate(ref _designShearKn, value); }
    public double LeverArmM { get => _leverArmM; set => SetPositiveAndRecalculate(ref _leverArmM, value, 0.72); }
    public double BeamDepthMm { get => _beamDepthMm; set => SetPositiveAndRecalculate(ref _beamDepthMm, value, 800.0); }
    public double BoltDiameterMm { get => _boltDiameterMm; set => SetPositiveAndRecalculate(ref _boltDiameterMm, value, 20.0); }
    public double BoltHoleDiameterMm { get => _boltHoleDiameterMm; set => SetPositiveAndRecalculate(ref _boltHoleDiameterMm, value, 22.0); }
    public double BoltUltimateStrengthMpa { get => _boltUltimateStrengthMpa; set => SetPositiveAndRecalculate(ref _boltUltimateStrengthMpa, value, 800.0); }
    public double BoltAlphaV { get => _boltAlphaV; set => SetPositiveAndRecalculate(ref _boltAlphaV, value, 0.6); }
    public int FlangeBoltsPerSide { get => _flangeBoltsPerSide; set => SetIntAndRecalculate(ref _flangeBoltsPerSide, value, 1, 200); }
    public int WebBoltsPerSide { get => _webBoltsPerSide; set => SetIntAndRecalculate(ref _webBoltsPerSide, value, 1, 200); }
    public int FlangeBoltShearPlanes { get => _flangeBoltShearPlanes; set => SetIntAndRecalculate(ref _flangeBoltShearPlanes, value, 1, 4); }
    public int WebBoltShearPlanes { get => _webBoltShearPlanes; set => SetIntAndRecalculate(ref _webBoltShearPlanes, value, 1, 4); }
    public double CoverPlateWidthMm { get => _coverPlateWidthMm; set => SetPositiveAndRecalculate(ref _coverPlateWidthMm, value, 220.0); }
    public double CoverPlateThicknessMm { get => _coverPlateThicknessMm; set => SetPositiveAndRecalculate(ref _coverPlateThicknessMm, value, 12.0); }
    public double WebPlateDepthMm { get => _webPlateDepthMm; set => SetPositiveAndRecalculate(ref _webPlateDepthMm, value, 520.0); }
    public double WebPlateThicknessMm { get => _webPlateThicknessMm; set => SetPositiveAndRecalculate(ref _webPlateThicknessMm, value, 10.0); }
    public double PlateYieldStrengthMpa { get => _plateYieldStrengthMpa; set => SetPositiveAndRecalculate(ref _plateYieldStrengthMpa, value, 355.0); }
    public double PlateUltimateStrengthMpa { get => _plateUltimateStrengthMpa; set => SetPositiveAndRecalculate(ref _plateUltimateStrengthMpa, value, 510.0); }
    public double EndDistanceMm { get => _endDistanceMm; set => SetPositiveAndRecalculate(ref _endDistanceMm, value, 40.0); }
    public double EdgeDistanceMm { get => _edgeDistanceMm; set => SetPositiveAndRecalculate(ref _edgeDistanceMm, value, 40.0); }
    public double PitchMm { get => _pitchMm; set => SetPositiveAndRecalculate(ref _pitchMm, value, 70.0); }
    public double GaugeMm { get => _gaugeMm; set => SetPositiveAndRecalculate(ref _gaugeMm, value, 80.0); }
    public int BoltRowsAlongForce { get => _boltRowsAlongForce; set => SetIntAndRecalculate(ref _boltRowsAlongForce, value, 1, 50); }
    public int BoltLinesAcrossForce { get => _boltLinesAcrossForce; set => SetIntAndRecalculate(ref _boltLinesAcrossForce, value, 1, 20); }
    public double GammaM0 { get => _gammaM0; set => SetPositiveAndRecalculate(ref _gammaM0, value, 1.0); }
    public double GammaM2 { get => _gammaM2; set => SetPositiveAndRecalculate(ref _gammaM2, value, 1.25); }

    public BeamSpliceConnectionResult Result
    {
        get => _result;
        private set
        {
            if (SetProperty(ref _result, value))
            {
                OnPropertyChanged(nameof(FlangeForceDisplay));
                OnPropertyChanged(nameof(WebShearDisplay));
                OnPropertyChanged(nameof(MaxUtilizationDisplay));
            }
        }
    }

    public string FlangeForceDisplay => $"Flange force: {Result.FlangeForceKn:0.##} kN";
    public string WebShearDisplay => $"Web shear: {Result.WebShearKn:0.##} kN";
    public string MaxUtilizationDisplay => $"Max utilization: {Result.MaximumUtilization:0.###}";

    private void Recalculate()
    {
        Result = _calculator.Calculate(BuildInput());
        ReplaceCollection(Checks, Result.Checks);
        ReplaceCollection(Notes, Result.Notes);
    }

    private BeamSpliceConnectionInput BuildInput()
    {
        return new BeamSpliceConnectionInput
        {
            DesignMomentKnM = DesignMomentKnM,
            DesignShearKn = DesignShearKn,
            LeverArmM = LeverArmM,
            BeamDepthMm = BeamDepthMm,
            BoltDiameterMm = BoltDiameterMm,
            BoltHoleDiameterMm = BoltHoleDiameterMm,
            BoltUltimateStrengthMpa = BoltUltimateStrengthMpa,
            BoltAlphaV = BoltAlphaV,
            FlangeBoltsPerSide = FlangeBoltsPerSide,
            WebBoltsPerSide = WebBoltsPerSide,
            FlangeBoltShearPlanes = FlangeBoltShearPlanes,
            WebBoltShearPlanes = WebBoltShearPlanes,
            CoverPlateWidthMm = CoverPlateWidthMm,
            CoverPlateThicknessMm = CoverPlateThicknessMm,
            WebPlateDepthMm = WebPlateDepthMm,
            WebPlateThicknessMm = WebPlateThicknessMm,
            PlateYieldStrengthMpa = PlateYieldStrengthMpa,
            PlateUltimateStrengthMpa = PlateUltimateStrengthMpa,
            EndDistanceMm = EndDistanceMm,
            EdgeDistanceMm = EdgeDistanceMm,
            PitchMm = PitchMm,
            GaugeMm = GaugeMm,
            BoltRowsAlongForce = BoltRowsAlongForce,
            BoltLinesAcrossForce = BoltLinesAcrossForce,
            GammaM0 = GammaM0,
            GammaM2 = GammaM2
        };
    }

    private void SetDoubleAndRecalculate(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, double.IsFinite(value) ? value : 0.0, propertyName))
            Recalculate();
    }

    private void SetPositiveAndRecalculate(ref double field, double value, double fallback, [CallerMemberName] string? propertyName = null)
    {
        double next = double.IsFinite(value) ? Math.Max(0.000001, value) : fallback;
        if (SetProperty(ref field, next, propertyName))
            Recalculate();
    }

    private void SetIntAndRecalculate(ref int field, int value, int minimum, int maximum, [CallerMemberName] string? propertyName = null)
    {
        int next = Math.Clamp(value, minimum, maximum);
        if (SetProperty(ref field, next, propertyName))
            Recalculate();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
