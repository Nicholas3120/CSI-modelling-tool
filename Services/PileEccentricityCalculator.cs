using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class PileEccentricityCalculator
{
    private const double Tolerance = 0.000001;
    private const double EurocodeKLimit = 0.167;
    private const double EurocodeAlphaCc = 0.85;
    private const double EurocodeGammaC = 1.5;
    private const double EurocodeGammaS = 1.15;
    private const double EurocodeEsNmm2 = 200000.0;
    private const double EurocodeLeverArmCapRatio = 0.95;

    public PileEccentricityCalculationResult Calculate(PileEccentricityCalculationInput input)
    {
        var result = new PileEccentricityCalculationResult();
        List<PileEccentricityPileGroupRow> activeGroups = input.PileGroups
            .Where(group => group.IsActive)
            .Where(group => !string.IsNullOrWhiteSpace(group.GroupId))
            .Select(group => group.Clone())
            .ToList();
        List<PileEccentricityPileRow> activePiles = input.Piles
            .Where(pile => pile.IsActive)
            .Where(pile => !string.IsNullOrWhiteSpace(pile.GroupId) && !string.IsNullOrWhiteSpace(pile.PileId))
            .Select(pile => pile.Clone())
            .ToList();

        Dictionary<string, PileGroupState> states = BuildGroupStates(activeGroups, activePiles, result);
        foreach (PileGroupState state in states.Values)
        {
            result.GeometrySummaries.Add(state.Geometry);
            List<PileEccentricityPileLoadResult> loads = CalculatePileLoads(state, state.Group.VerticalLoadNkN, state.Geometry.MxkNm, state.Geometry.MykNm);
            result.IsolatedPileLoads.AddRange(loads);
            AddPileLoadSteps(result, "Isolated pile load", state, state.Group.VerticalLoadNkN, state.Geometry.MxkNm, state.Geometry.MykNm, loads);
            AddComparison(result, state.Group.GroupId, "Without tie beam", state.Group.VerticalLoadNkN, state.Geometry.MxkNm, state.Geometry.MykNm, loads);
        }

        if (input.UseIdealTieBeamTransfer)
        {
            ApplyTieBeamTransfers(input.TieBeams.Select(tie => tie.Clone()), states, result);
            foreach (PileGroupState state in states.Values)
            {
                double revisedRv = state.Group.VerticalLoadNkN + state.VerticalDeltaKn;
                double mxRemaining = state.Geometry.MxkNm - state.MxTransferredkNm;
                double myRemaining = state.Geometry.MykNm - state.MyTransferredkNm;
                List<PileEccentricityPileLoadResult> revisedLoads = CalculatePileLoads(state, revisedRv, mxRemaining, myRemaining);
                result.RevisedPileLoads.AddRange(revisedLoads);
                AddPileLoadSteps(result, "Revised pile load", state, revisedRv, mxRemaining, myRemaining, revisedLoads);
                AddComparison(result, state.Group.GroupId, "With ideal tie beam", revisedRv, mxRemaining, myRemaining, revisedLoads);
            }

            AddInfo(result, "Ideal tie beam transfer treats the selected column eccentricity as a point load on a simply supported tie beam.");
            AddInfo(result, "Pile-group vertical effects are calculated from the point-load support reactions Rfrom = P b / L and Rto = P a / L.");
            AddInfo(result, "Tie beam support reactions are applied as additional pile-group axial effects; between-support loads add compression to both groups, while overhang loads create uplift at one group.");
            AddInfo(result, "Pile selfweight and tie beam selfweight are not included; only entered column/load values are used.");
        }

        result.Preview = BuildPreview(states, input.UseIdealTieBeamTransfer ? result.RevisedPileLoads : result.IsolatedPileLoads, result.TieBeamSummaries);

        if (result.Messages.Count == 0)
        {
            result.Messages.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Info,
                Message = input.UseIdealTieBeamTransfer
                    ? "Calculated isolated pile loads and ideal tie beam transfer effects."
                    : "Calculated isolated pile group eccentricity loads."
            });
        }

        return result;
    }

    private static Dictionary<string, PileGroupState> BuildGroupStates(
        List<PileEccentricityPileGroupRow> activeGroups,
        List<PileEccentricityPileRow> activePiles,
        PileEccentricityCalculationResult result)
    {
        var states = new Dictionary<string, PileGroupState>(StringComparer.OrdinalIgnoreCase);

        foreach (PileEccentricityPileGroupRow group in activeGroups)
        {
            if (states.ContainsKey(group.GroupId))
            {
                result.Messages.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Duplicate pile group ID {group.GroupId} was skipped."
                });
                continue;
            }

            List<PileEccentricityPileRow> groupPiles = activePiles
                .Where(pile => string.Equals(pile.GroupId, group.GroupId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (groupPiles.Count == 0)
            {
                result.Messages.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Critical,
                    Message = $"Pile group {group.GroupId} has no active piles."
                });
                continue;
            }

            double totalWeight = groupPiles.Sum(PileWeight);
            if (totalWeight <= Tolerance)
                totalWeight = groupPiles.Count;

            double centroidX = groupPiles.Sum(pile => PileWeight(pile) * pile.X) / totalWeight;
            double centroidY = groupPiles.Sum(pile => PileWeight(pile) * pile.Y) / totalWeight;
            double ex = group.ColumnX - centroidX;
            double ey = group.ColumnY - centroidY;
            double mx = group.VerticalLoadNkN * ey + group.AdditionalMxkNm;
            double my = group.VerticalLoadNkN * ex + group.AdditionalMykNm;
            double distributionCentroidX = groupPiles.Sum(pile => PileWeight(pile) * pile.OriginalX) / totalWeight;
            double distributionCentroidY = groupPiles.Sum(pile => PileWeight(pile) * pile.OriginalY) / totalWeight;
            double sumX2 = groupPiles.Sum(pile => PileWeight(pile) * Math.Pow(pile.OriginalX - distributionCentroidX, 2));
            double sumY2 = groupPiles.Sum(pile => PileWeight(pile) * Math.Pow(pile.OriginalY - distributionCentroidY, 2));
            double weightedX = groupPiles.Sum(pile => PileWeight(pile) * pile.X);
            double weightedY = groupPiles.Sum(pile => PileWeight(pile) * pile.Y);
            double weightedOriginalX = groupPiles.Sum(pile => PileWeight(pile) * pile.OriginalX);
            double weightedOriginalY = groupPiles.Sum(pile => PileWeight(pile) * pile.OriginalY);
            double xSpread = groupPiles.Max(pile => pile.OriginalX) - groupPiles.Min(pile => pile.OriginalX);
            double ySpread = groupPiles.Max(pile => pile.OriginalY) - groupPiles.Min(pile => pile.OriginalY);
            double averageDiameter = groupPiles.Select(pile => PositiveOrDefault(pile.Diameter, 0.6)).Average();
            double minimumStableSpread = Math.Max(0.05, averageDiameter * 0.25);
            bool canDistributeMy = xSpread >= minimumStableSpread && sumX2 > Tolerance;
            bool canDistributeMx = ySpread >= minimumStableSpread && sumY2 > Tolerance;

            if (!canDistributeMy && Math.Abs(my) > Tolerance)
            {
                AddWarning(
                    result,
                    $"Pile group {group.GroupId} has standard-layout X pile spread {Mm(xSpread)} below the minimum practical lever arm {Mm(minimumStableSpread)}, so My = {F(my)} kNm is retained but cannot be resisted by the local pile group. Use X-direction pile spacing or transfer My by tie beam.");
            }

            if (!canDistributeMx && Math.Abs(mx) > Tolerance)
            {
                AddWarning(
                    result,
                    $"Pile group {group.GroupId} has standard-layout Y pile spread {Mm(ySpread)} below the minimum practical lever arm {Mm(minimumStableSpread)}, so Mx = {F(mx)} kNm is retained but cannot be resisted by the local pile group. Use Y-direction pile spacing or transfer Mx by tie beam.");
            }

            states[group.GroupId] = new PileGroupState
            {
                Group = group,
                Piles = groupPiles,
                WeightSum = totalWeight,
                CanDistributeMx = canDistributeMx,
                CanDistributeMy = canDistributeMy,
                XSpread = xSpread,
                YSpread = ySpread,
                DistributionCentroidX = distributionCentroidX,
                DistributionCentroidY = distributionCentroidY,
                MinimumStableSpread = minimumStableSpread,
                Geometry = new PileEccentricityGeometrySummary
                {
                    GroupId = group.GroupId,
                    ActivePileCount = groupPiles.Count,
                    CentroidX = centroidX,
                    CentroidY = centroidY,
                    Ex = ex,
                    Ey = ey,
                    MxkNm = mx,
                    MykNm = my,
                    SumX2 = sumX2,
                    SumY2 = sumY2
                }
            };

            AddStep(result, "Geometry", $"{group.GroupId} centroid X", "xbar = sum(x) / n", $"{F(weightedX)} / {F(totalWeight)}", $"{F(centroidX)} m");
            AddStep(result, "Geometry", $"{group.GroupId} centroid Y", "ybar = sum(y) / n", $"{F(weightedY)} / {F(totalWeight)}", $"{F(centroidY)} m");
            AddStep(result, "Resistance geometry", $"{group.GroupId} standard centroid X", "xbar0 = sum(x0) / n", $"{F(weightedOriginalX)} / {F(totalWeight)}", $"{F(distributionCentroidX)} m");
            AddStep(result, "Resistance geometry", $"{group.GroupId} standard centroid Y", "ybar0 = sum(y0) / n", $"{F(weightedOriginalY)} / {F(totalWeight)}", $"{F(distributionCentroidY)} m");
            AddStep(result, "Eccentricity", $"{group.GroupId} ex", "ex = xcol - xbar", $"{F(group.ColumnX)} - {F(centroidX)}", $"{F(ex)} m");
            AddStep(result, "Eccentricity", $"{group.GroupId} ey", "ey = ycol - ybar", $"{F(group.ColumnY)} - {F(centroidY)}", $"{F(ey)} m");
            AddStep(result, "Moment", $"{group.GroupId} Mx", "Mx = N ey + Mx,add", $"{F(group.VerticalLoadNkN)} x {F(ey)} + {F(group.AdditionalMxkNm)}", $"{F(mx)} kNm");
            AddStep(result, "Moment", $"{group.GroupId} My", "My = N ex + My,add", $"{F(group.VerticalLoadNkN)} x {F(ex)} + {F(group.AdditionalMykNm)}", $"{F(my)} kNm");
            AddStep(result, "Pile distribution", $"{group.GroupId} sum x0'^2", "sum x0'^2 = sum((x0 - xbar0)^2)", BuildSquareSumSubstitution(groupPiles, true, distributionCentroidX), $"{F(sumX2)} m2");
            AddStep(result, "Pile distribution", $"{group.GroupId} sum y0'^2", "sum y0'^2 = sum((y0 - ybar0)^2)", BuildSquareSumSubstitution(groupPiles, false, distributionCentroidY), $"{F(sumY2)} m2");
        }

        return states;
    }

    private static List<PileEccentricityPileLoadResult> CalculatePileLoads(
        PileGroupState state,
        double verticalReactionKn,
        double mxkNm,
        double mykNm)
    {
        var loads = new List<PileEccentricityPileLoadResult>();
        foreach (PileEccentricityPileRow pile in state.Piles)
        {
            double weight = PileWeight(pile);
            double xLocal = pile.OriginalX - state.DistributionCentroidX;
            double yLocal = pile.OriginalY - state.DistributionCentroidY;
            double baseLoad = state.Piles.Count > 0 ? verticalReactionKn / state.Piles.Count : 0.0;
            double loadFromMx = state.CanDistributeMx && state.Geometry.SumY2 > Tolerance ? mxkNm * weight * yLocal / state.Geometry.SumY2 : 0.0;
            double loadFromMy = state.CanDistributeMy && state.Geometry.SumX2 > Tolerance ? mykNm * weight * xLocal / state.Geometry.SumX2 : 0.0;
            double finalLoad = baseLoad + loadFromMx + loadFromMy;

            loads.Add(new PileEccentricityPileLoadResult
            {
                GroupId = state.Group.GroupId,
                PileId = pile.PileId,
                XLocal = xLocal,
                YLocal = yLocal,
                BaseLoadkN = baseLoad,
                LoadFromMxkN = loadFromMx,
                LoadFromMykN = loadFromMy,
                FinalLoadkN = finalLoad,
                Status = DeterminePileStatus(finalLoad, pile.CompressionCapacitykN, pile.TensionCapacitykN)
            });
        }

        return loads;
    }

    private static void ApplyTieBeamTransfers(
        IEnumerable<PileEccentricityTieBeamRow> tieBeams,
        Dictionary<string, PileGroupState> states,
        PileEccentricityCalculationResult result)
    {
        foreach (PileEccentricityTieBeamRow tieBeam in tieBeams.Where(tie => tie.IsActive))
        {
            if (!states.TryGetValue(tieBeam.FromGroupId, out PileGroupState? fromState))
            {
                AddWarning(result, $"Tie beam {tieBeam.TieBeamId} references missing from group {tieBeam.FromGroupId}.");
                continue;
            }

            if (!states.TryGetValue(tieBeam.ToGroupId, out PileGroupState? toState))
            {
                AddWarning(result, $"Tie beam {tieBeam.TieBeamId} references missing to group {tieBeam.ToGroupId}.");
                continue;
            }

            double dx = toState.Group.ColumnX - fromState.Group.ColumnX;
            double dy = toState.Group.ColumnY - fromState.Group.ColumnY;
            double autoLength = Math.Sqrt(dx * dx + dy * dy);
            if (autoLength <= Tolerance)
            {
                dx = toState.Geometry.CentroidX - fromState.Geometry.CentroidX;
                dy = toState.Geometry.CentroidY - fromState.Geometry.CentroidY;
                autoLength = Math.Sqrt(dx * dx + dy * dy);
            }

            double length = autoLength;
            if (length <= Tolerance)
            {
                AddWarning(result, $"Tie beam {tieBeam.TieBeamId} has zero length and was skipped.");
                continue;
            }

            if (!tieBeam.UseAutoLength && tieBeam.Length > Tolerance && Math.Abs(tieBeam.Length - autoLength) > Tolerance)
            {
                AddInfo(result, $"Tie beam {tieBeam.TieBeamId} simple beam calculation uses centre-to-centre span L = {F(autoLength)} m; manual length {F(tieBeam.Length)} m was not used for reactions.");
            }

            double transferRatio = Math.Clamp(tieBeam.TransferPercentagePercent / 100.0, 0.0, 1.0);
            double pointLoad = Math.Abs(fromState.Group.VerticalLoadNkN) * transferRatio;
            if (pointLoad <= Tolerance)
                continue;

            SimpleBeamPointLoadResult beamResult = CalculateTieBeamPointLoad(
                tieBeam,
                pointLoad,
                fromState.Group.ColumnX,
                fromState.Group.ColumnY,
                fromState.Geometry.CentroidX,
                fromState.Geometry.CentroidY,
                dx,
                dy,
                autoLength,
                length,
                result);
            double unitX = dx / length;
            double unitY = dy / length;
            double projectedLoadX = beamResult.LoadDistanceA * unitX;
            double projectedLoadY = beamResult.LoadDistanceA * unitY;
            double mxTransfer = pointLoad * projectedLoadY;
            double myTransfer = pointLoad * projectedLoadX;
            double eccentricMomentMagnitude = Math.Sqrt(mxTransfer * mxTransfer + myTransfer * myTransfer);
            if (eccentricMomentMagnitude <= Tolerance)
                continue;

            (double fromDelta, double toDelta, string axialEffectFormula, string axialEffectSubstitution) =
                CalculateTieBeamAxialEffects(beamResult, pointLoad);
            double transferForce = Math.Max(Math.Abs(fromDelta), Math.Abs(toDelta));
            if (Math.Abs(fromDelta) <= Tolerance && Math.Abs(toDelta) <= Tolerance)
            {
                if (eccentricMomentMagnitude > Tolerance)
                    AddWarning(result, $"Tie beam {tieBeam.TieBeamId} calculated zero point-load support reaction, so its transfer was skipped.");

                continue;
            }

            fromState.VerticalDeltaKn += fromDelta;
            toState.VerticalDeltaKn += toDelta;
            fromState.MxTransferredkNm += mxTransfer;
            fromState.MyTransferredkNm += myTransfer;
            TieBeamSectionDesign sectionDesign = DesignTieBeamSection(tieBeam, beamResult, result);

            result.TieBeamSummaries.Add(new PileEccentricityTieBeamSummary
            {
                TieBeamId = tieBeam.TieBeamId,
                FromGroupId = fromState.Group.GroupId,
                ToGroupId = toState.Group.GroupId,
                MomentTransferred = BuildMomentLabel(mxTransfer, myTransfer),
                MxTransferredkNm = mxTransfer,
                MyTransferredkNm = myTransfer,
                MomentMagnitudekNm = eccentricMomentMagnitude,
                PointLoadkN = pointLoad,
                LoadDistanceA = beamResult.LoadDistanceA,
                LoadDistanceB = beamResult.LoadDistanceB,
                CaseType = beamResult.CaseType,
                FromSupportReactionkN = beamResult.LeftReaction,
                ToSupportReactionkN = beamResult.RightReaction,
                FromPileStatus = beamResult.LeftPileStatus,
                ToPileStatus = beamResult.RightPileStatus,
                FromPileMagnitudekN = beamResult.LeftPileMagnitude,
                ToPileMagnitudekN = beamResult.RightPileMagnitude,
                DesignMomentkNm = beamResult.DesignMoment,
                MomentType = beamResult.MomentType,
                MomentLocation = beamResult.MomentLocation,
                Length = length,
                TransferForcekN = transferForce,
                FromVerticalDeltaKn = fromDelta,
                ToVerticalDeltaKn = toDelta,
                FromEffect = FormatEffect(fromDelta),
                ToEffect = FormatEffect(toDelta),
                BeamWidthMm = sectionDesign.BeamWidthMm,
                BeamDepthMm = sectionDesign.BeamDepthMm,
                CoverMm = sectionDesign.CoverMm,
                LinkDiameterMm = sectionDesign.LinkDiameterMm,
                EffectiveDepthMm = sectionDesign.EffectiveDepthMm,
                CompressionSteelDepthMm = sectionDesign.CompressionSteelDepthMm,
                ConcreteStrengthNmm2 = sectionDesign.ConcreteStrengthNmm2,
                SteelYieldStrengthNmm2 = sectionDesign.SteelYieldStrengthNmm2,
                ConcreteDesignStrengthNmm2 = sectionDesign.ConcreteDesignStrengthNmm2,
                SteelDesignStrengthNmm2 = sectionDesign.SteelDesignStrengthNmm2,
                EurocodeK = sectionDesign.EurocodeK,
                EurocodeKLimit = sectionDesign.EurocodeKLimit,
                EurocodeKCheck = sectionDesign.EurocodeKCheck,
                CompressionBarRequired = sectionDesign.CompressionBarRequired,
                LeverArmMm = sectionDesign.LeverArmMm,
                RequiredTensionSteelMm2 = sectionDesign.RequiredTensionSteelMm2,
                RequiredCompressionSteelMm2 = sectionDesign.RequiredCompressionSteelMm2,
                TensionBarDiameterMm = sectionDesign.TensionBarDiameterMm,
                CompressionBarDiameterMm = sectionDesign.CompressionBarDiameterMm,
                SuggestedTensionBarCount = sectionDesign.SuggestedTensionBarCount,
                SuggestedCompressionBarCount = sectionDesign.SuggestedCompressionBarCount,
                SuggestedTensionBars = sectionDesign.SuggestedTensionBars,
                SuggestedCompressionBars = sectionDesign.SuggestedCompressionBars,
                TensionFace = sectionDesign.TensionFace,
                SectionDesignStatus = sectionDesign.SectionDesignStatus
            });

            AddStep(result, "Tie beam transfer", $"{tieBeam.TieBeamId} point load projection", "a = dot(C - A, unit centre-to-centre)", $"C ({F(fromState.Group.ColumnX)}, {F(fromState.Group.ColumnY)}), A ({F(fromState.Geometry.CentroidX)}, {F(fromState.Geometry.CentroidY)}), unit ({F(unitX)}, {F(unitY)})", $"a = {F(beamResult.LoadDistanceA)} m, case = {beamResult.CaseType}");
            AddStep(result, "Tie beam transfer", $"{tieBeam.TieBeamId} span basis", beamResult.SpanFormula, beamResult.SpanSubstitution, beamResult.SpanResult);
            AddStep(result, "Tie beam transfer", $"{tieBeam.TieBeamId} balanced moment component", "Mtie components = P a unit(A to B)", $"{F(pointLoad)} x {F(beamResult.LoadDistanceA)} x ({F(unitX)}, {F(unitY)})", $"Mx = {F(mxTransfer)} kNm, My = {F(myTransfer)} kNm");
            AddStep(result, "Tie beam transfer", $"{tieBeam.TieBeamId} support reactions", "RB = P a / L, RA = P - RB", $"RB = {F(pointLoad)} x {F(beamResult.LoadDistanceA)} / {F(length)}, RA = {F(pointLoad)} - {F(beamResult.RightReaction)}", $"{fromState.Group.GroupId} RA = {F(beamResult.LeftReaction)} kN ({beamResult.LeftPileStatus}), {toState.Group.GroupId} RB = {F(beamResult.RightReaction)} kN ({beamResult.RightPileStatus})");
            AddStep(result, "Tie beam transfer", $"{tieBeam.TieBeamId} tie beam design moment", beamResult.MomentFormula, beamResult.MomentSubstitution, $"{F(beamResult.DesignMoment)} kNm {beamResult.MomentType}, {beamResult.MomentLocation}");
            AddStep(result, "Tie beam transfer", tieBeam.TieBeamId, axialEffectFormula, axialEffectSubstitution, $"{fromState.Group.GroupId} {FormatEffect(fromDelta)}, {toState.Group.GroupId} {FormatEffect(toDelta)}");
            AddStep(result, "Tie beam transfer", $"{tieBeam.TieBeamId} {fromState.Group.GroupId} revised Rv", "Rv,from = Nfrom + Vfrom", $"{F(fromState.Group.VerticalLoadNkN)} + {F(fromState.VerticalDeltaKn)}", $"{F(fromState.Group.VerticalLoadNkN + fromState.VerticalDeltaKn)} kN");
            AddStep(result, "Tie beam transfer", $"{tieBeam.TieBeamId} {toState.Group.GroupId} revised Rv", "Rv,to = Nto + Vto", $"{F(toState.Group.VerticalLoadNkN)} + {F(toState.VerticalDeltaKn)}", $"{F(toState.Group.VerticalLoadNkN + toState.VerticalDeltaKn)} kN");
        }
    }

    private static SimpleBeamPointLoadResult CalculateTieBeamPointLoad(
        PileEccentricityTieBeamRow tieBeam,
        double pointLoad,
        double pointLoadX,
        double pointLoadY,
        double fromX,
        double fromY,
        double dx,
        double dy,
        double geometryLength,
        double length,
        PileEccentricityCalculationResult result)
    {
        if (pointLoad <= Tolerance)
        {
            AddWarning(
                result,
                $"Tie beam {tieBeam.TieBeamId} could not calculate point-load support reactions because the selected column vertical load is zero.");
            return new SimpleBeamPointLoadResult
            {
                LoadDistanceA = 0.0,
                LoadDistanceB = length,
                CaseType = "No load",
                MomentType = "None",
                MomentLocation = "None",
                SpanFormula = "No vertical point load",
                SpanSubstitution = $"L = {F(length)} m",
                SpanResult = "No support reaction"
            };
        }

        double loadDistanceA = geometryLength > Tolerance
            ? ((pointLoadX - fromX) * dx + (pointLoadY - fromY) * dy) / geometryLength
            : 0.0;
        double loadDistanceB = length - loadDistanceA;
        double rightReaction = pointLoad * loadDistanceA / length;
        double leftReaction = pointLoad - rightReaction;
        string caseType;
        double designMoment;
        string momentType;
        string momentLocation;
        string momentFormula;
        string momentSubstitution;
        string spanFormula;
        string spanSubstitution;
        string spanResult;

        if (loadDistanceA >= -Tolerance && loadDistanceA <= length + Tolerance)
        {
            double effectiveA = Math.Clamp(loadDistanceA, 0.0, length);
            double effectiveB = length - effectiveA;
            loadDistanceA = effectiveA;
            loadDistanceB = effectiveB;
            rightReaction = pointLoad * effectiveA / length;
            leftReaction = pointLoad - rightReaction;
            caseType = "Load Between Supports";
            designMoment = pointLoad * effectiveA * effectiveB / length;
            momentType = "Sagging";
            momentLocation = "At load position";
            momentFormula = "Mmax = P a b / L";
            momentSubstitution = $"{F(pointLoad)} x {F(effectiveA)} x {F(effectiveB)} / {F(length)}";
            spanFormula = "Between supports: b = L - a and L = a + b";
            spanSubstitution = $"b = {F(length)} - {F(effectiveA)} = {F(effectiveB)}";
            spanResult = $"L = {F(effectiveA)} + {F(effectiveB)} = {F(length)} m";
        }
        else if (loadDistanceA < -Tolerance)
        {
            double overhang = Math.Abs(loadDistanceA);
            caseType = "Left Overhang";
            designMoment = pointLoad * overhang;
            momentType = "Hogging";
            momentLocation = "At left support";
            momentFormula = "Mhogging = P e";
            momentSubstitution = $"{F(pointLoad)} x {F(overhang)}";
            spanFormula = "Left overhang: L is centre-to-centre support span, e = |a|, b = L + e";
            spanSubstitution = $"L = {F(length)} m, e = |{F(loadDistanceA)}| = {F(overhang)} m";
            spanResult = $"b = {F(loadDistanceB)} m";
        }
        else
        {
            double overhang = loadDistanceA - length;
            caseType = "Right Overhang";
            designMoment = pointLoad * overhang;
            momentType = "Hogging";
            momentLocation = "At right support";
            momentFormula = "Mhogging = P e";
            momentSubstitution = $"{F(pointLoad)} x {F(overhang)}";
            spanFormula = "Right overhang: L is centre-to-centre support span, e = a - L, b = -e";
            spanSubstitution = $"L = {F(length)} m, e = {F(loadDistanceA)} - {F(length)} = {F(overhang)} m";
            spanResult = $"b = {F(loadDistanceB)} m";
        }

        var simpleBeamResult = new SimpleBeamPointLoadResult
        {
            LoadDistanceA = loadDistanceA,
            LoadDistanceB = loadDistanceB,
            CaseType = caseType,
            LeftReaction = leftReaction,
            RightReaction = rightReaction,
            LeftPileStatus = SupportStatus(leftReaction),
            RightPileStatus = SupportStatus(rightReaction),
            LeftPileMagnitude = Math.Abs(leftReaction),
            RightPileMagnitude = Math.Abs(rightReaction),
            DesignMoment = designMoment,
            MomentType = momentType,
            MomentLocation = momentLocation,
            MomentFormula = momentFormula,
            MomentSubstitution = momentSubstitution,
            SpanFormula = spanFormula,
            SpanSubstitution = spanSubstitution,
            SpanResult = spanResult
        };

        AddUpliftWarning(result, tieBeam.TieBeamId, "left", leftReaction);
        AddUpliftWarning(result, tieBeam.TieBeamId, "right", rightReaction);
        return simpleBeamResult;
    }

    private static (double FromDelta, double ToDelta, string Formula, string Substitution) CalculateTieBeamAxialEffects(
        SimpleBeamPointLoadResult beamResult,
        double pointLoad)
    {
        if (string.Equals(beamResult.CaseType, "Load Between Supports", StringComparison.OrdinalIgnoreCase))
        {
            double addedCompression = Math.Abs(beamResult.RightReaction);
            return (
                addedCompression,
                addedCompression,
                "Between supports: tie beam adds compression to both pile groups",
                $"Vfrom = RB = {F(beamResult.RightReaction)}, Vto = RB = {F(beamResult.RightReaction)}");
        }

        if (string.Equals(beamResult.CaseType, "Left Overhang", StringComparison.OrdinalIgnoreCase))
        {
            return (
                beamResult.LeftReaction - pointLoad,
                beamResult.RightReaction,
                "Left overhang: eccentric group gets compression increment, other group gets uplift",
                $"Vfrom = RA - P = {F(beamResult.LeftReaction)} - {F(pointLoad)}, Vto = RB = {F(beamResult.RightReaction)}");
        }

        if (string.Equals(beamResult.CaseType, "Right Overhang", StringComparison.OrdinalIgnoreCase))
        {
            return (
                beamResult.LeftReaction,
                beamResult.RightReaction - pointLoad,
                "Right overhang: near group gets compression increment, other group gets uplift",
                $"Vfrom = RA = {F(beamResult.LeftReaction)}, Vto = RB - P = {F(beamResult.RightReaction)} - {F(pointLoad)}");
        }

        return (0.0, 0.0, "No tie beam axial effect", "No point load reaction");
    }

    private static TieBeamSectionDesign DesignTieBeamSection(
        PileEccentricityTieBeamRow tieBeam,
        SimpleBeamPointLoadResult beamResult,
        PileEccentricityCalculationResult result)
    {
        double b = PositiveOrDefault(tieBeam.Width * 1000.0, 600.0);
        double h = PositiveOrDefault(tieBeam.Depth * 1000.0, 900.0);
        double fck = PositiveOrDefault(tieBeam.ConcreteStrengthNmm2, 30.0);
        double fyk = PositiveOrDefault(tieBeam.SteelYieldStrengthNmm2, 500.0);
        double cover = PositiveOrDefault(tieBeam.CoverMm, 50.0);
        double linkDiameter = PositiveOrDefault(tieBeam.LinkDiameterMm, 10.0);
        double tensionBarDiameter = PositiveOrDefault(tieBeam.TensionBarDiameterMm, 20.0);
        double compressionBarDiameter = PositiveOrDefault(tieBeam.CompressionBarDiameterMm, tensionBarDiameter);
        double d = h - cover - linkDiameter - tensionBarDiameter / 2.0;
        double dPrime = cover + linkDiameter + compressionBarDiameter / 2.0;
        double fcd = EurocodeAlphaCc * fck / EurocodeGammaC;
        double fyd = fyk / EurocodeGammaS;
        double designMomentNmm = Math.Abs(beamResult.DesignMoment) * 1_000_000.0;
        string tensionFace = string.Equals(beamResult.MomentType, "Hogging", StringComparison.OrdinalIgnoreCase) ? "Top" : "Bottom";

        var design = new TieBeamSectionDesign
        {
            BeamWidthMm = b,
            BeamDepthMm = h,
            CoverMm = cover,
            LinkDiameterMm = linkDiameter,
            EffectiveDepthMm = d,
            CompressionSteelDepthMm = dPrime,
            ConcreteStrengthNmm2 = fck,
            SteelYieldStrengthNmm2 = fyk,
            ConcreteDesignStrengthNmm2 = fcd,
            SteelDesignStrengthNmm2 = fyd,
            EurocodeKLimit = EurocodeKLimit,
            TensionBarDiameterMm = tensionBarDiameter,
            CompressionBarDiameterMm = compressionBarDiameter,
            TensionFace = tensionFace
        };

        AddStep(result, "Tie beam section design", $"{tieBeam.TieBeamId} effective depth", "d = h - cover - link - phi_t / 2", $"{F(h)} - {F(cover)} - {F(linkDiameter)} - {F(tensionBarDiameter)} / 2", $"d = {F(d)} mm, d' = {F(dPrime)} mm");
        AddStep(result, "Tie beam section design", $"{tieBeam.TieBeamId} EC2 design strengths", "fcd = alpha_cc fck / gamma_c, fyd = fyk / gamma_s", $"0.85 x {F(fck)} / 1.5, {F(fyk)} / 1.15", $"fcd = {F(fcd)} N/mm2, fyd = {F(fyd)} N/mm2");

        if (designMomentNmm <= Tolerance)
        {
            design.EurocodeKCheck = "No moment";
            design.CompressionBarRequired = "No";
            design.SuggestedTensionBars = "No design moment";
            design.SuggestedCompressionBars = "Not required";
            design.SectionDesignStatus = "No tie-beam design moment";
            AddStep(result, "Tie beam section design", $"{tieBeam.TieBeamId} K check", "K = MEd / (fck b d^2)", "MEd = 0", "No section rebar design moment");
            return design;
        }

        if (d <= Tolerance || dPrime <= Tolerance || d <= dPrime + Tolerance)
        {
            design.EurocodeKCheck = "Invalid section";
            design.CompressionBarRequired = "Check";
            design.SuggestedTensionBars = "Check section";
            design.SuggestedCompressionBars = "Check section";
            design.SectionDesignStatus = "Invalid section depth or cover";
            AddWarning(result, $"Tie beam {tieBeam.TieBeamId} has invalid EC2 section geometry: d = {F(d)} mm and d' = {F(dPrime)} mm.");
            return design;
        }

        double k = designMomentNmm / (fck * b * d * d);
        bool compressionRequired = k > EurocodeKLimit + Tolerance;
        design.EurocodeK = k;
        design.EurocodeKCheck = compressionRequired ? "K > 0.167" : "K <= 0.167";
        design.CompressionBarRequired = compressionRequired ? "Yes" : "No";

        AddStep(
            result,
            "Tie beam section design",
            $"{tieBeam.TieBeamId} K check",
            "K = MEd / (fck b d^2)",
            $"{F(beamResult.DesignMoment)} x 10^6 / ({F(fck)} x {F(b)} x {F(d)}^2)",
            $"K = {F4(k)}; {design.EurocodeKCheck}; compression bar required = {design.CompressionBarRequired}");

        double lambda = EurocodeLambda(fck);
        double eta = EurocodeEta(fck);
        double kForLeverArm = compressionRequired ? EurocodeKLimit : k;
        double neutralAxisRatio = NeutralAxisRatio(kForLeverArm, fck, fcd, eta, lambda);
        if (!double.IsFinite(neutralAxisRatio) || neutralAxisRatio <= Tolerance)
        {
            design.SectionDesignStatus = "Neutral-axis calculation failed";
            design.SuggestedTensionBars = "Check section";
            design.SuggestedCompressionBars = compressionRequired ? "Check section" : "Not required";
            AddWarning(result, $"Tie beam {tieBeam.TieBeamId} EC2 neutral-axis calculation failed for K = {F4(kForLeverArm)}.");
            return design;
        }

        double x = neutralAxisRatio * d;
        double z = Math.Min(EurocodeLeverArmCapRatio * d, d - 0.5 * lambda * x);
        design.LeverArmMm = z;

        AddStep(
            result,
            "Tie beam section design",
            $"{tieBeam.TieBeamId} lever arm",
            "z = d - 0.5 lambda x, limited to 0.95d",
            $"lambda = {F(lambda)}, x = {F(x)} mm, 0.95d = {F(EurocodeLeverArmCapRatio * d)} mm",
            $"z = {F(z)} mm");

        if (!compressionRequired)
        {
            double ast = designMomentNmm / (fyd * z);
            design.RequiredTensionSteelMm2 = ast;
            design.RequiredCompressionSteelMm2 = 0.0;
            design.SuggestedTensionBarCount = RequiredBarCount(ast, tensionBarDiameter);
            design.SuggestedCompressionBarCount = 0;
            design.SuggestedTensionBars = FormatBarSet(design.SuggestedTensionBarCount, tensionBarDiameter);
            design.SuggestedCompressionBars = "Not required";
            design.SectionDesignStatus = "Tension steel only";

            AddStep(
                result,
                "Tie beam section design",
                $"{tieBeam.TieBeamId} tension steel",
                "As,req = MEd / (fyd z)",
                $"{F(beamResult.DesignMoment)} x 10^6 / ({F(fyd)} x {F(z)})",
                $"As = {F(ast)} mm2, provide {design.SuggestedTensionBars} at {tensionFace.ToLowerInvariant()}");

            return design;
        }

        double kLimitMomentNmm = EurocodeKLimit * fck * b * d * d;
        double as1 = kLimitMomentNmm / (fyd * z);
        double extraMomentNmm = Math.Max(0.0, designMomentNmm - kLimitMomentNmm);
        double compressionSteelStress = CompressionSteelStress(fyd, x, dPrime);
        if (compressionSteelStress <= Tolerance)
        {
            design.SectionDesignStatus = "Compression steel stress invalid";
            design.SuggestedTensionBars = "Check section";
            design.SuggestedCompressionBars = "Check d'";
            AddWarning(result, $"Tie beam {tieBeam.TieBeamId} compression steel is too close to or beyond the neutral axis. Check cover, bar diameter and section depth.");
            return design;
        }

        double asc = extraMomentNmm / (compressionSteelStress * (d - dPrime));
        double as2 = asc * compressionSteelStress / fyd;
        double astTotal = as1 + as2;
        design.RequiredTensionSteelMm2 = astTotal;
        design.RequiredCompressionSteelMm2 = asc;
        design.SuggestedTensionBarCount = RequiredBarCount(astTotal, tensionBarDiameter);
        design.SuggestedCompressionBarCount = RequiredBarCount(asc, compressionBarDiameter);
        design.SuggestedTensionBars = FormatBarSet(design.SuggestedTensionBarCount, tensionBarDiameter);
        design.SuggestedCompressionBars = FormatBarSet(design.SuggestedCompressionBarCount, compressionBarDiameter);
        design.SectionDesignStatus = "Compression steel required";

        AddStep(
            result,
            "Tie beam section design",
            $"{tieBeam.TieBeamId} limiting moment",
            "Mlim = Klim fck b d^2",
            $"0.167 x {F(fck)} x {F(b)} x {F(d)}^2",
            $"Mlim = {F(kLimitMomentNmm / 1_000_000.0)} kNm");
        AddStep(
            result,
            "Tie beam section design",
            $"{tieBeam.TieBeamId} compression steel",
            "Asc = (MEd - Mlim) / (fsc (d - d'))",
            $"({F(beamResult.DesignMoment)} - {F(kLimitMomentNmm / 1_000_000.0)}) x 10^6 / ({F(compressionSteelStress)} x ({F(d)} - {F(dPrime)}))",
            $"Asc = {F(asc)} mm2, provide {design.SuggestedCompressionBars}");
        AddStep(
            result,
            "Tie beam section design",
            $"{tieBeam.TieBeamId} total tension steel",
            "Ast = Mlim / (fyd z) + Asc fsc / fyd",
            $"{F(kLimitMomentNmm / 1_000_000.0)} x 10^6 / ({F(fyd)} x {F(z)}) + {F(asc)} x {F(compressionSteelStress)} / {F(fyd)}",
            $"Ast = {F(astTotal)} mm2, provide {design.SuggestedTensionBars} at {tensionFace.ToLowerInvariant()}");

        return design;
    }

    private static PileEccentricityPreviewModel BuildPreview(
        Dictionary<string, PileGroupState> states,
        List<PileEccentricityPileLoadResult> displayLoads,
        List<PileEccentricityTieBeamSummary> tieBeamSummaries)
    {
        var preview = new PileEccentricityPreviewModel();
        Dictionary<string, PileEccentricityPileLoadResult> loads = displayLoads
            .GroupBy(load => $"{load.GroupId}|{load.PileId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (PileGroupState state in states.Values)
        {
            preview.Groups.Add(new PileEccentricityPreviewGroup
            {
                GroupId = state.Group.GroupId,
                ColumnX = state.Group.ColumnX,
                ColumnY = state.Group.ColumnY,
                CentroidX = state.Geometry.CentroidX,
                CentroidY = state.Geometry.CentroidY,
                Ex = state.Geometry.Ex,
                Ey = state.Geometry.Ey,
                MxkNm = state.Geometry.MxkNm,
                MykNm = state.Geometry.MykNm
            });

            foreach (PileEccentricityPileRow pile in state.Piles)
            {
                loads.TryGetValue($"{state.Group.GroupId}|{pile.PileId}", out PileEccentricityPileLoadResult? pileLoad);
                preview.Piles.Add(new PileEccentricityPreviewPile
                {
                    GroupId = state.Group.GroupId,
                    PileId = pile.PileId,
                    X = pile.X,
                    Y = pile.Y,
                    FinalLoadkN = pileLoad?.FinalLoadkN ?? 0.0,
                    Status = pileLoad?.Status ?? "OK"
                });
            }
        }

        foreach (PileEccentricityTieBeamSummary summary in tieBeamSummaries)
        {
            if (!states.TryGetValue(summary.FromGroupId, out PileGroupState? fromState) ||
                !states.TryGetValue(summary.ToGroupId, out PileGroupState? toState))
            {
                continue;
            }

            preview.TieBeams.Add(new PileEccentricityPreviewTieBeam
            {
                TieBeamId = summary.TieBeamId,
                FromGroupId = summary.FromGroupId,
                ToGroupId = summary.ToGroupId,
                FromX = fromState.Geometry.CentroidX,
                FromY = fromState.Geometry.CentroidY,
                ToX = toState.Geometry.CentroidX,
                ToY = toState.Geometry.CentroidY,
                FromEffect = summary.FromEffect,
                ToEffect = summary.ToEffect,
                TransferForcekN = summary.TransferForcekN,
                MomentTransferred = summary.MomentTransferred
            });
        }

        return preview;
    }

    private static void AddComparison(
        PileEccentricityCalculationResult result,
        string groupId,
        string caseName,
        double rv,
        double mxRemaining,
        double myRemaining,
        List<PileEccentricityPileLoadResult> loads)
    {
        if (loads.Count == 0)
            return;

        result.Comparisons.Add(new PileEccentricityComparisonRow
        {
            GroupId = groupId,
            CaseName = caseName,
            RvkN = rv,
            MxRemainingkNm = mxRemaining,
            MyRemainingkNm = myRemaining,
            MaxPileLoadkN = loads.Max(load => load.FinalLoadkN),
            MinPileLoadkN = loads.Min(load => load.FinalLoadkN),
            Uplift = loads.Any(load => load.FinalLoadkN < -Tolerance) ? "Yes" : "No"
        });
    }

    private static void AddPileLoadSteps(
        PileEccentricityCalculationResult result,
        string section,
        PileGroupState state,
        double verticalReactionKn,
        double mxkNm,
        double mykNm,
        List<PileEccentricityPileLoadResult> loads)
    {
        foreach (PileEccentricityPileLoadResult load in loads)
        {
            PileEccentricityPileRow? pile = state.Piles.FirstOrDefault(pile =>
                string.Equals(pile.PileId, load.PileId, StringComparison.OrdinalIgnoreCase));
            if (pile == null)
                continue;

            string item = $"{state.Group.GroupId}-{pile.PileId}";
            AddStep(result, section, $"{item} base", "P0 = Rv / n", $"{F(verticalReactionKn)} / {state.Piles.Count}", $"{F(load.BaseLoadkN)} kN");
            if (state.CanDistributeMx)
                AddStep(result, section, $"{item} Mx effect", "dPmx = Mx y0' / sum(y0'^2)", $"{F(mxkNm)} x {F(load.YLocal)} / {F(state.Geometry.SumY2)}", $"{F(load.LoadFromMxkN)} kN");
            else
                AddStep(result, section, $"{item} Mx effect", "dPmx not distributed locally", $"Mx = {F(mxkNm)} kNm; standard-layout Y spread {Mm(state.YSpread)} < practical lever arm {Mm(state.MinimumStableSpread)}", "0 kN");

            if (state.CanDistributeMy)
                AddStep(result, section, $"{item} My effect", "dPmy = My x0' / sum(x0'^2)", $"{F(mykNm)} x {F(load.XLocal)} / {F(state.Geometry.SumX2)}", $"{F(load.LoadFromMykN)} kN");
            else
                AddStep(result, section, $"{item} My effect", "dPmy not distributed locally", $"My = {F(mykNm)} kNm; standard-layout X spread {Mm(state.XSpread)} < practical lever arm {Mm(state.MinimumStableSpread)}", "0 kN");

            AddStep(result, section, $"{item} final", "P = P0 + dPmx + dPmy", $"{F(load.BaseLoadkN)} + {F(load.LoadFromMxkN)} + {F(load.LoadFromMykN)}", $"{F(load.FinalLoadkN)} kN, {load.Status}");
        }
    }

    private static void AddStep(
        PileEccentricityCalculationResult result,
        string section,
        string item,
        string formula,
        string substitution,
        string stepResult)
    {
        result.CalculationSteps.Add(new PileEccentricityCalculationStep
        {
            Section = section,
            Item = item,
            Formula = formula,
            Substitution = substitution,
            Result = stepResult
        });
    }

    private static string BuildSquareSumSubstitution(List<PileEccentricityPileRow> piles, bool useX, double centroid)
    {
        return string.Join(" + ", piles.Select(pile =>
        {
            double coordinate = useX ? pile.OriginalX : pile.OriginalY;
            return $"({F(coordinate)} - {F(centroid)})^2";
        }));
    }

    private static string F(double value)
    {
        return double.IsFinite(value) ? value.ToString("0.###") : "NA";
    }

    private static string F4(double value)
    {
        return double.IsFinite(value) ? value.ToString("0.####") : "NA";
    }

    private static string Mm(double metres)
    {
        return double.IsFinite(metres) ? $"{metres * 1000.0:0.#} mm" : "NA";
    }

    private static double PositiveOrDefault(double value, double fallback)
    {
        return double.IsFinite(value) && value > Tolerance ? value : fallback;
    }

    private static double EurocodeLambda(double fck)
    {
        return fck <= 50.0 ? 0.8 : Math.Max(0.0, 0.8 - (fck - 50.0) / 400.0);
    }

    private static double EurocodeEta(double fck)
    {
        return fck <= 50.0 ? 1.0 : Math.Max(0.0, 1.0 - (fck - 50.0) / 200.0);
    }

    private static double NeutralAxisRatio(double k, double fck, double fcd, double eta, double lambda)
    {
        double stressBlockFactor = eta * lambda * fcd / fck;
        if (stressBlockFactor <= Tolerance || lambda <= Tolerance)
            return double.NaN;

        double discriminant = 1.0 - 2.0 * lambda * k / stressBlockFactor;
        if (discriminant < -Tolerance)
            return double.NaN;

        return (1.0 - Math.Sqrt(Math.Max(0.0, discriminant))) / lambda;
    }

    private static double CompressionSteelStress(double fyd, double neutralAxisDepthMm, double compressionSteelDepthMm)
    {
        if (neutralAxisDepthMm <= compressionSteelDepthMm + Tolerance)
            return 0.0;

        double strain = 0.0035 * (neutralAxisDepthMm - compressionSteelDepthMm) / neutralAxisDepthMm;
        return Math.Min(fyd, EurocodeEsNmm2 * strain);
    }

    private static double BarArea(double diameterMm)
    {
        return Math.PI * diameterMm * diameterMm / 4.0;
    }

    private static int RequiredBarCount(double requiredAreaMm2, double diameterMm)
    {
        if (requiredAreaMm2 <= Tolerance)
            return 0;

        return Math.Max(2, (int)Math.Ceiling(requiredAreaMm2 / BarArea(diameterMm)));
    }

    private static string FormatBarSet(int count, double diameterMm)
    {
        if (count <= 0)
            return "Not required";

        return $"{count}T{diameterMm:0.#}";
    }

    private static string DeterminePileStatus(double finalLoad, double compressionCapacity, double tensionCapacity)
    {
        if (finalLoad < -Tolerance)
        {
            if (tensionCapacity > Tolerance && Math.Abs(finalLoad) > tensionCapacity)
                return "Exceeds Tension";

            return "Uplift / Tension";
        }

        if (compressionCapacity > Tolerance && finalLoad > compressionCapacity)
            return "Exceeds Compression";

        return "OK";
    }

    private static string BuildMomentLabel(double mxTransfer, double myTransfer)
    {
        bool hasMx = Math.Abs(mxTransfer) > Tolerance;
        bool hasMy = Math.Abs(myTransfer) > Tolerance;
        if (hasMx && hasMy)
            return $"Mx {mxTransfer:0.##} kNm + My {myTransfer:0.##} kNm";
        if (hasMx)
            return $"Mx {mxTransfer:0.##} kNm";
        if (hasMy)
            return $"My {myTransfer:0.##} kNm";

        return "None";
    }

    private static string FormatEffect(double deltaKn)
    {
        string direction = deltaKn >= 0.0 ? TieBeamDirectionEffectLabels.Downward : TieBeamDirectionEffectLabels.Upward;
        return $"{direction} {Math.Abs(deltaKn):0.##} kN";
    }

    private static string SupportStatus(double reactionKn)
    {
        return reactionKn >= -Tolerance ? "Compression" : "Uplift";
    }

    private static void AddUpliftWarning(
        PileEccentricityCalculationResult result,
        string tieBeamId,
        string supportName,
        double reactionKn)
    {
        if (reactionKn >= -Tolerance)
            return;

        AddWarning(
            result,
            $"Tie beam {tieBeamId} {supportName} support uplift {F(Math.Abs(reactionKn))} kN detected. Check pile tension capacity and anchorage.");
    }

    private static void AddWarning(PileEccentricityCalculationResult result, string message)
    {
        result.Messages.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Warning,
            Message = message
        });
    }

    private static void AddCritical(PileEccentricityCalculationResult result, string message)
    {
        result.Messages.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Critical,
            Message = message
        });
    }

    private static void AddInfo(PileEccentricityCalculationResult result, string message)
    {
        result.Messages.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Info,
            Message = message
        });
    }

    private static double PileWeight(PileEccentricityPileRow pile)
    {
        return 1.0;
    }

    private sealed class PileGroupState
    {
        public PileEccentricityPileGroupRow Group { get; set; } = new();
        public List<PileEccentricityPileRow> Piles { get; set; } = [];
        public PileEccentricityGeometrySummary Geometry { get; set; } = new();
        public double WeightSum { get; set; }
        public bool CanDistributeMx { get; set; } = true;
        public bool CanDistributeMy { get; set; } = true;
        public double XSpread { get; set; }
        public double YSpread { get; set; }
        public double DistributionCentroidX { get; set; }
        public double DistributionCentroidY { get; set; }
        public double MinimumStableSpread { get; set; }
        public double VerticalDeltaKn { get; set; }
        public double MxTransferredkNm { get; set; }
        public double MyTransferredkNm { get; set; }
    }

    private sealed class SimpleBeamPointLoadResult
    {
        public double LoadDistanceA { get; set; }
        public double LoadDistanceB { get; set; }
        public string CaseType { get; set; } = "";
        public double LeftReaction { get; set; }
        public double RightReaction { get; set; }
        public string LeftPileStatus { get; set; } = "";
        public string RightPileStatus { get; set; } = "";
        public double LeftPileMagnitude { get; set; }
        public double RightPileMagnitude { get; set; }
        public double DesignMoment { get; set; }
        public string MomentType { get; set; } = "";
        public string MomentLocation { get; set; } = "";
        public string MomentFormula { get; set; } = "";
        public string MomentSubstitution { get; set; } = "";
        public string SpanFormula { get; set; } = "";
        public string SpanSubstitution { get; set; } = "";
        public string SpanResult { get; set; } = "";
    }

    private sealed class TieBeamSectionDesign
    {
        public double BeamWidthMm { get; set; }
        public double BeamDepthMm { get; set; }
        public double CoverMm { get; set; }
        public double LinkDiameterMm { get; set; }
        public double EffectiveDepthMm { get; set; }
        public double CompressionSteelDepthMm { get; set; }
        public double ConcreteStrengthNmm2 { get; set; }
        public double SteelYieldStrengthNmm2 { get; set; }
        public double ConcreteDesignStrengthNmm2 { get; set; }
        public double SteelDesignStrengthNmm2 { get; set; }
        public double EurocodeK { get; set; }
        public double EurocodeKLimit { get; set; }
        public string EurocodeKCheck { get; set; } = "";
        public string CompressionBarRequired { get; set; } = "";
        public double LeverArmMm { get; set; }
        public double RequiredTensionSteelMm2 { get; set; }
        public double RequiredCompressionSteelMm2 { get; set; }
        public double TensionBarDiameterMm { get; set; }
        public double CompressionBarDiameterMm { get; set; }
        public int SuggestedTensionBarCount { get; set; }
        public int SuggestedCompressionBarCount { get; set; }
        public string SuggestedTensionBars { get; set; } = "";
        public string SuggestedCompressionBars { get; set; } = "";
        public string TensionFace { get; set; } = "";
        public string SectionDesignStatus { get; set; } = "";
    }
}
