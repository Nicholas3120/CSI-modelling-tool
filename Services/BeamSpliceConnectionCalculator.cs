using TrussModelling.Models;

namespace TrussModelling.Services;

public sealed class BeamSpliceConnectionCalculator
{
    private const double Root3 = 1.7320508075688772;
    private const double Tolerance = 0.000001;

    public BeamSpliceConnectionResult Calculate(BeamSpliceConnectionInput input)
    {
        var result = new BeamSpliceConnectionResult();
        double leverArmM = Positive(input.LeverArmM, 0.72);
        double flangeForceKn = Math.Abs(input.DesignMomentKnM) / leverArmM;
        double webShearKn = Math.Abs(input.DesignShearKn);
        double boltArea = Math.PI * Math.Pow(Positive(input.BoltDiameterMm, 20.0), 2) / 4.0;
        double boltShearPerPlane = input.BoltAlphaV * input.BoltUltimateStrengthMpa * boltArea / (input.GammaM2 * 1000.0);
        double bearingCover = CalculateBearingResistance(input, input.CoverPlateThicknessMm);
        double bearingWeb = CalculateBearingResistance(input, input.WebPlateThicknessMm);
        int flangeBolts = Math.Max(1, input.FlangeBoltsPerSide);
        int webBolts = Math.Max(1, input.WebBoltsPerSide);
        int flangePlanes = Math.Max(1, input.FlangeBoltShearPlanes);
        int webPlanes = Math.Max(1, input.WebBoltShearPlanes);

        result.FlangeForceKn = flangeForceKn;
        result.WebShearKn = webShearKn;
        result.BoltShearResistancePerPlaneKn = boltShearPerPlane;
        result.BearingResistancePerBoltCoverKn = bearingCover;
        result.BearingResistancePerBoltWebKn = bearingWeb;

        AddCheck(result, "Bolt shear", "Top flange cover plate", flangeForceKn, flangeBolts * flangePlanes * boltShearPerPlane);
        AddCheck(result, "Bolt shear", "Bottom flange cover plate", flangeForceKn, flangeBolts * flangePlanes * boltShearPerPlane);
        AddCheck(result, "Bolt shear", "Web splice plate", webShearKn, webBolts * webPlanes * boltShearPerPlane);

        AddCheck(result, "Bolt bearing", "Top flange cover plate", flangeForceKn, flangeBolts * bearingCover);
        AddCheck(result, "Bolt bearing", "Bottom flange cover plate", flangeForceKn, flangeBolts * bearingCover);
        AddCheck(result, "Bolt bearing", "Web splice plate", webShearKn, webBolts * bearingWeb);

        AddCheck(result, "Plate shear", "Top flange cover plate", flangeForceKn, PlateShearResistance(input.CoverPlateWidthMm, input.CoverPlateThicknessMm, input.PlateYieldStrengthMpa, input.GammaM0));
        AddCheck(result, "Plate shear", "Bottom flange cover plate", flangeForceKn, PlateShearResistance(input.CoverPlateWidthMm, input.CoverPlateThicknessMm, input.PlateYieldStrengthMpa, input.GammaM0));
        AddCheck(result, "Plate shear", "Web splice plate", webShearKn, PlateShearResistance(input.WebPlateDepthMm, input.WebPlateThicknessMm, input.PlateYieldStrengthMpa, input.GammaM0));

        double coverBlockResistance = BlockTearingResistance(input, input.CoverPlateThicknessMm);
        double webBlockResistance = BlockTearingResistance(input, input.WebPlateThicknessMm);
        AddCheck(result, "Block tearing", "Top flange cover plate", flangeForceKn, coverBlockResistance);
        AddCheck(result, "Block tearing", "Bottom flange cover plate", flangeForceKn, coverBlockResistance);
        AddCheck(result, "Block tearing", "Web splice plate", webShearKn, webBlockResistance);

        result.MaximumUtilization = result.Checks
            .Where(check => double.IsFinite(check.Utilization))
            .Select(check => check.Utilization)
            .DefaultIfEmpty(0.0)
            .Max();
        result.Notes.Add("Flange bolt force is taken as design moment divided by lever arm.");
        result.Notes.Add("Web splice bolts are checked for design shear only.");
        result.Notes.Add("Bearing follows EC3-style k1 alpha_b fu d t / gammaM2 using the entered end/edge/pitch/gauge distances.");
        result.Notes.Add("Block tearing uses a simplified EC3 net tension plus shear-plane resistance check from entered bolt layout.");
        return result;
    }

    private static double CalculateBearingResistance(BeamSpliceConnectionInput input, double plateThickness)
    {
        double d = Positive(input.BoltDiameterMm, 20.0);
        double d0 = Positive(input.BoltHoleDiameterMm, d + 2.0);
        double e1 = Positive(input.EndDistanceMm, 40.0);
        double e2 = Positive(input.EdgeDistanceMm, 40.0);
        double p1 = Positive(input.PitchMm, 70.0);
        double p2 = Positive(input.GaugeMm, 80.0);
        double alphaB = Math.Min(Math.Min(e1 / (3.0 * d0), p1 / (3.0 * d0) - 0.25), Math.Min(input.BoltUltimateStrengthMpa / input.PlateUltimateStrengthMpa, 1.0));
        double k1 = Math.Min(Math.Min(2.8 * e2 / d0 - 1.7, 1.4 * p2 / d0 - 1.7), 2.5);
        alphaB = Math.Max(0.0, alphaB);
        k1 = Math.Max(0.0, k1);
        return k1 * alphaB * input.PlateUltimateStrengthMpa * d * Positive(plateThickness, 10.0) / (input.GammaM2 * 1000.0);
    }

    private static double PlateShearResistance(double widthOrDepth, double thickness, double fy, double gammaM0)
    {
        return Positive(widthOrDepth, 1.0) * Positive(thickness, 1.0) * Positive(fy, 355.0) / (Root3 * Positive(gammaM0, 1.0) * 1000.0);
    }

    private static double BlockTearingResistance(BeamSpliceConnectionInput input, double plateThickness)
    {
        double t = Positive(plateThickness, 10.0);
        double d0 = Positive(input.BoltHoleDiameterMm, input.BoltDiameterMm + 2.0);
        int rows = Math.Max(1, input.BoltRowsAlongForce);
        int lines = Math.Max(1, input.BoltLinesAcrossForce);
        double e1 = Positive(input.EndDistanceMm, 40.0);
        double e2 = Positive(input.EdgeDistanceMm, 40.0);
        double p1 = Positive(input.PitchMm, 70.0);
        double p2 = Positive(input.GaugeMm, 80.0);
        double shearLength = e1 + Math.Max(0, rows - 1) * p1;
        double tensionWidth = 2.0 * e2 + Math.Max(0, lines - 1) * p2;

        double agv = 2.0 * shearLength * t;
        double anv = Math.Max(0.0, 2.0 * (shearLength - Math.Max(0, rows - 0.5) * d0) * t);
        double ant = Math.Max(0.0, (tensionWidth - lines * d0) * t);
        double vEff1 = input.PlateUltimateStrengthMpa * ant / (input.GammaM2 * 1000.0) +
            input.PlateYieldStrengthMpa * anv / (Root3 * input.GammaM0 * 1000.0);
        double vEff2 = 0.5 * input.PlateUltimateStrengthMpa * ant / (input.GammaM2 * 1000.0) +
            input.PlateYieldStrengthMpa * agv / (Root3 * input.GammaM0 * 1000.0);

        return Math.Max(0.0, Math.Min(vEff1, vEff2));
    }

    private static void AddCheck(BeamSpliceConnectionResult result, string check, string target, double demand, double resistance)
    {
        result.Checks.Add(new BeamSpliceCheckResult
        {
            Check = check,
            Target = target,
            DemandKn = demand,
            ResistanceKn = resistance,
            Utilization = resistance > Tolerance ? demand / resistance : double.PositiveInfinity
        });
    }

    private static double Positive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }
}
