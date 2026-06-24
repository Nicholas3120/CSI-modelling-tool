namespace TrussModelling.Models;

public sealed class BeamSpliceCheckResult
{
    public string Check { get; set; } = "";
    public string Target { get; set; } = "";
    public double DemandKn { get; set; }
    public double ResistanceKn { get; set; }
    public double Utilization { get; set; }
    public string Status => Utilization <= 1.0 ? "OK" : "NG";
}

public sealed class BeamSpliceConnectionInput
{
    public double DesignMomentKnM { get; set; } = 250.0;
    public double DesignShearKn { get; set; } = 150.0;
    public double LeverArmM { get; set; } = 0.72;
    public double BeamDepthMm { get; set; } = 800.0;
    public double BoltDiameterMm { get; set; } = 20.0;
    public double BoltHoleDiameterMm { get; set; } = 22.0;
    public double BoltUltimateStrengthMpa { get; set; } = 800.0;
    public double BoltAlphaV { get; set; } = 0.6;
    public int FlangeBoltsPerSide { get; set; } = 8;
    public int WebBoltsPerSide { get; set; } = 8;
    public int FlangeBoltShearPlanes { get; set; } = 1;
    public int WebBoltShearPlanes { get; set; } = 1;
    public double CoverPlateWidthMm { get; set; } = 220.0;
    public double CoverPlateThicknessMm { get; set; } = 12.0;
    public double WebPlateDepthMm { get; set; } = 520.0;
    public double WebPlateThicknessMm { get; set; } = 10.0;
    public double PlateYieldStrengthMpa { get; set; } = 355.0;
    public double PlateUltimateStrengthMpa { get; set; } = 510.0;
    public double EndDistanceMm { get; set; } = 40.0;
    public double EdgeDistanceMm { get; set; } = 40.0;
    public double PitchMm { get; set; } = 70.0;
    public double GaugeMm { get; set; } = 80.0;
    public int BoltRowsAlongForce { get; set; } = 4;
    public int BoltLinesAcrossForce { get; set; } = 2;
    public double GammaM0 { get; set; } = 1.0;
    public double GammaM2 { get; set; } = 1.25;
}

public sealed class BeamSpliceConnectionResult
{
    public double FlangeForceKn { get; set; }
    public double WebShearKn { get; set; }
    public double BoltShearResistancePerPlaneKn { get; set; }
    public double BearingResistancePerBoltCoverKn { get; set; }
    public double BearingResistancePerBoltWebKn { get; set; }
    public double MaximumUtilization { get; set; }
    public List<BeamSpliceCheckResult> Checks { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}
