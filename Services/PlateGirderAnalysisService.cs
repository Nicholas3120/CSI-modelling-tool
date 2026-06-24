using TrussModelling.Models;

namespace TrussModelling.Services;

public sealed class PlateGirderAnalysisService
{
    private const double Tolerance = 0.000001;
    private const double Root3 = 1.7320508075688772;
    private const double AssumedPoissonRatio = 0.3;

    public PlateGirderAnalysisResult Analyze(ParametricPlateGirderModel model)
    {
        var result = new PlateGirderAnalysisResult();
        if (model.Length <= 0 || model.Depth <= 0)
        {
            result.IsError = true;
            result.Message = "Plate girder geometry is invalid.";
            return result;
        }

        try
        {
            int elementCount = Math.Clamp(model.LengthDivisions, 4, 400);
            int stationCount = elementCount + 1;
            double[] xStations = Enumerable.Range(0, stationCount)
                .Select(index => model.Length * index / elementCount)
                .ToArray();

            SectionProperties[] sectionProperties = xStations
                .Select(x => CalculateSectionProperties(model, x))
                .ToArray();

            double[] deflections = SolveSimplySupportedDeflection(model, elementCount);
            double q = model.AnalysisUniformLoadKnPerM;

            for (int index = 0; index < stationCount; index++)
            {
                double x = xStations[index];
                SectionProperties section = sectionProperties[index];
                double demandMoment = q * x * (model.Length - x) / 2.0;
                double demandShear = Math.Abs(q * (model.Length / 2.0 - x));
                double momentUtilization = section.MomentCapacityKnM > Tolerance
                    ? demandMoment / section.MomentCapacityKnM
                    : double.PositiveInfinity;
                double shearUtilization = section.ShearCapacityKn > Tolerance
                    ? demandShear / section.ShearCapacityKn
                    : double.PositiveInfinity;

                result.Stations.Add(new PlateGirderSectionResult
                {
                    X = x,
                    InertiaY = section.InertiaY,
                    NeutralAxisZ = section.NeutralAxisZ,
                    MomentCapacityKnM = section.MomentCapacityKnM,
                    DemandMomentKnM = demandMoment,
                    Utilization = momentUtilization,
                    ShearCapacityKn = section.ShearCapacityKn,
                    DemandShearKn = demandShear,
                    ShearUtilization = shearUtilization,
                    SectionClass = section.SectionClass,
                    FlangeClass = section.FlangeClass,
                    WebClass = section.WebClass,
                    MomentCapacityBasis = section.MomentCapacityBasis,
                    ShearCapacityBasis = section.ShearCapacityBasis,
                    DeflectionMm = Math.Abs(deflections[index]) * 1000.0,
                    WithinOpening = section.WithinOpening,
                    HasStiffener = section.HasStiffener
                });
            }

            result.MinimumMomentCapacityKnM = result.Stations
                .Where(station => station.MomentCapacityKnM > Tolerance)
                .Select(station => station.MomentCapacityKnM)
                .DefaultIfEmpty(0.0)
                .Min();
            result.MaximumDemandMomentKnM = result.Stations
                .Select(station => station.DemandMomentKnM)
                .DefaultIfEmpty(0.0)
                .Max();
            result.MaximumUtilization = result.Stations
                .Select(station => Math.Max(station.Utilization, station.ShearUtilization))
                .Where(double.IsFinite)
                .DefaultIfEmpty(0.0)
                .Max();
            result.MinimumShearCapacityKn = result.Stations
                .Where(station => station.ShearCapacityKn > Tolerance)
                .Select(station => station.ShearCapacityKn)
                .DefaultIfEmpty(0.0)
                .Min();
            result.MaximumDemandShearKn = result.Stations
                .Select(station => station.DemandShearKn)
                .DefaultIfEmpty(0.0)
                .Max();
            result.MaximumShearUtilization = result.Stations
                .Where(station => double.IsFinite(station.ShearUtilization))
                .Select(station => station.ShearUtilization)
                .DefaultIfEmpty(0.0)
                .Max();
            result.MaximumDeflectionMm = result.Stations
                .Select(station => station.DeflectionMm)
                .DefaultIfEmpty(0.0)
                .Max();
            result.Message = "Variable-section Timoshenko beam stiffness analysis completed.";

            if (model.Openings.Count > 0)
                result.Warnings.Add("Opening effects are included by removing web area at stations inside each opening width.");
            if (model.Openings.Any(opening => opening.Strengthen))
                result.Warnings.Add("Opening stiffener contribution is included at stations crossing each stiffener strip.");
            result.Warnings.Add("Moment capacity uses EN 1993-1-1 section classification per station: Class 1/2 plastic resistance, Class 3 elastic resistance, Class 4 flagged as effective-section required.");
            result.Warnings.Add("Shear capacity uses EN 1993-1-1 plastic web shear area per station. Slender web shear buckling per EN 1993-1-5 is flagged but not reduced here.");
            if (result.Stations.Any(station => station.SectionClass >= 4))
                result.Warnings.Add("One or more stations are Class 4. Moment resistance is not reported for those stations until EN 1993-1-5 effective section properties are implemented.");

            return result;
        }
        catch (Exception ex)
        {
            result.IsError = true;
            result.Message = ex.Message;
            return result;
        }
    }

    private static double[] SolveSimplySupportedDeflection(ParametricPlateGirderModel model, int elementCount)
    {
        int nodeCount = elementCount + 1;
        int dofCount = nodeCount * 2;
        double[,] stiffness = new double[dofCount, dofCount];
        double[] loads = new double[dofCount];
        double elementLength = model.Length / elementCount;
        double uniformLoad = model.AnalysisUniformLoadKnPerM * 1000.0;

        for (int element = 0; element < elementCount; element++)
        {
            double xMid = (element + 0.5) * elementLength;
            SectionProperties section = CalculateSectionProperties(model, xMid);
            double ei = Math.Max(section.FlexuralRigidity, Tolerance);
            double shearRigidity = Math.Max(section.ShearRigidity, Tolerance);
            double l = elementLength;
            double l2 = l * l;
            double l3 = l2 * l;
            double phi = 12.0 * ei / (shearRigidity * l2);
            double factor = ei / ((1.0 + phi) * l3);
            double[,] k =
            {
                { 12 * factor, 6 * l * factor, -12 * factor, 6 * l * factor },
                { 6 * l * factor, (4 + phi) * l2 * factor, -6 * l * factor, (2 - phi) * l2 * factor },
                { -12 * factor, -6 * l * factor, 12 * factor, -6 * l * factor },
                { 6 * l * factor, (2 - phi) * l2 * factor, -6 * l * factor, (4 + phi) * l2 * factor }
            };
            double[] f =
            [
                uniformLoad * l / 2.0,
                uniformLoad * l2 / 12.0,
                uniformLoad * l / 2.0,
                -uniformLoad * l2 / 12.0
            ];
            int[] dofs = [element * 2, element * 2 + 1, (element + 1) * 2, (element + 1) * 2 + 1];

            for (int row = 0; row < 4; row++)
            {
                loads[dofs[row]] += f[row];
                for (int column = 0; column < 4; column++)
                    stiffness[dofs[row], dofs[column]] += k[row, column];
            }
        }

        List<int> freeDofs = Enumerable.Range(0, dofCount)
            .Where(dof => dof != 0 && dof != (nodeCount - 1) * 2)
            .ToList();

        double[,] reducedK = new double[freeDofs.Count, freeDofs.Count];
        double[] reducedF = new double[freeDofs.Count];
        for (int row = 0; row < freeDofs.Count; row++)
        {
            reducedF[row] = loads[freeDofs[row]];
            for (int column = 0; column < freeDofs.Count; column++)
                reducedK[row, column] = stiffness[freeDofs[row], freeDofs[column]];
        }

        double[] reducedD = SolveLinearSystem(reducedK, reducedF);
        double[] displacements = new double[dofCount];
        for (int index = 0; index < freeDofs.Count; index++)
            displacements[freeDofs[index]] = reducedD[index];

        double[] vertical = new double[nodeCount];
        for (int node = 0; node < nodeCount; node++)
            vertical[node] = displacements[node * 2];

        return vertical;
    }

    private static SectionProperties CalculateSectionProperties(ParametricPlateGirderModel model, double x)
    {
        var components = new List<SectionComponent>();
        List<OpeningLocalBounds> openings = BuildOpeningBounds(model);
        double depth = model.Depth;
        double webThickness = model.WebThickness;
        double flangeThickness = model.FlangeThickness;
        double stiffenerThickness = model.StiffenerThickness;
        double webFy = model.WebSteelYieldStrengthMpa;
        double flangeFy = model.FlangeSteelYieldStrengthMpa;
        double stiffenerFy = model.StiffenerSteelYieldStrengthMpa;
        double webElasticModulus = Math.Max(Tolerance, model.WebElasticModulusGpa * 1_000_000_000.0);
        double flangeElasticModulus = Math.Max(Tolerance, model.FlangeElasticModulusGpa * 1_000_000_000.0);
        double stiffenerElasticModulus = Math.Max(Tolerance, model.StiffenerElasticModulusGpa * 1_000_000_000.0);
        bool withinOpening = false;
        List<Interval> webSegments = [new Interval(0.0, depth)];
        foreach (OpeningLocalBounds opening in openings)
        {
            if (x < opening.Left - Tolerance || x > opening.Right + Tolerance)
                continue;

            withinOpening = true;
            webSegments = SubtractInterval(webSegments, new Interval(opening.Bottom, opening.Top));
        }

        foreach (Interval segment in webSegments)
        {
            double height = Math.Max(0.0, segment.Top - segment.Bottom);
            AddRect(components, PlateGirderComponentKind.Web, webThickness, height, segment.Bottom + height / 2.0, segment.Bottom, segment.Top, webFy, webElasticModulus);
        }

        if (model.GenerateTopFlange)
            AddRect(components, PlateGirderComponentKind.Flange, model.FlangeWidth, flangeThickness, depth + flangeThickness / 2.0, depth, depth + flangeThickness, flangeFy, flangeElasticModulus);
        if (model.GenerateBottomFlange)
            AddRect(components, PlateGirderComponentKind.Flange, model.FlangeWidth, flangeThickness, -flangeThickness / 2.0, -flangeThickness, 0.0, flangeFy, flangeElasticModulus);

        bool hasStiffener = false;
        foreach (OpeningLocalBounds opening in openings.Where(opening => opening.Definition.Strengthen))
        {
            double stiffenerOutstand = opening.Definition.StiffenerOutstand;
            double sideStiffenerBand = Math.Max(model.StiffenerThickness, model.Length / Math.Max(model.LengthDivisions, 1));
            double stiffenerExtension = Math.Max(0.0, opening.Definition.StiffenerExtension);
            double horizontalLeft = Math.Max(0.0, opening.Left - stiffenerExtension);
            double horizontalRight = Math.Min(model.Length, opening.Right + stiffenerExtension);
            double verticalBottom = Math.Max(0.0, opening.Bottom - stiffenerExtension);
            double verticalTop = Math.Min(model.Depth, opening.Top + stiffenerExtension);
            double verticalStiffenerHeight = Math.Max(0.0, verticalTop - verticalBottom);
            double verticalStiffenerCentroid = (verticalBottom + verticalTop) / 2.0;
            if (opening.Definition.StrengthenTop && x >= horizontalLeft - Tolerance && x <= horizontalRight + Tolerance)
            {
                AddRect(components, PlateGirderComponentKind.Stiffener, stiffenerOutstand, stiffenerThickness, opening.Top + stiffenerThickness / 2.0, opening.Top, opening.Top + stiffenerThickness, stiffenerFy, stiffenerElasticModulus);
                hasStiffener = true;
            }

            if (opening.Definition.StrengthenBottom && x >= horizontalLeft - Tolerance && x <= horizontalRight + Tolerance)
            {
                AddRect(components, PlateGirderComponentKind.Stiffener, stiffenerOutstand, stiffenerThickness, opening.Bottom - stiffenerThickness / 2.0, opening.Bottom - stiffenerThickness, opening.Bottom, stiffenerFy, stiffenerElasticModulus);
                hasStiffener = true;
            }

            if (opening.Definition.StrengthenLeft && Math.Abs(x - opening.Left) <= sideStiffenerBand / 2.0 + Tolerance)
            {
                AddRect(components, PlateGirderComponentKind.Stiffener, stiffenerOutstand, verticalStiffenerHeight, verticalStiffenerCentroid, verticalBottom, verticalTop, stiffenerFy, stiffenerElasticModulus);
                hasStiffener = true;
            }

            if (opening.Definition.StrengthenRight && Math.Abs(x - opening.Right) <= sideStiffenerBand / 2.0 + Tolerance)
            {
                AddRect(components, PlateGirderComponentKind.Stiffener, stiffenerOutstand, verticalStiffenerHeight, verticalStiffenerCentroid, verticalBottom, verticalTop, stiffenerFy, stiffenerElasticModulus);
                hasStiffener = true;
            }
        }

        double area = components.Sum(component => component.Area);
        double elasticArea = components.Sum(component => component.Area * component.ElasticModulusPa);
        double neutralAxis = elasticArea > Tolerance
            ? components.Sum(component => component.Area * component.ElasticModulusPa * component.CentroidZ) / elasticArea
            : depth / 2.0;
        double inertia = components.Sum(component => component.LocalIy + component.Area * Math.Pow(component.CentroidZ - neutralAxis, 2));
        double flexuralRigidity = components.Sum(component =>
            component.ElasticModulusPa * (component.LocalIy + component.Area * Math.Pow(component.CentroidZ - neutralAxis, 2)));
        double gammaM0 = Math.Max(Tolerance, model.GammaM0);
        double webShearArea = webSegments.Sum(segment => Math.Max(0.0, segment.Top - segment.Bottom) * webThickness);
        double webShearModulus = webElasticModulus / (2.0 * (1.0 + AssumedPoissonRatio));
        double shearRigidity = webShearArea * webShearModulus;
        double shearCapacity = webShearArea * webFy * 1_000_000.0 / (Root3 * gammaM0) / 1000.0;
        SectionClassification classification = ClassifySection(model, components, webSegments, neutralAxis);
        double elasticMomentCapacity = CalculateElasticMomentCapacity(components, neutralAxis, flexuralRigidity, gammaM0);
        double plasticMomentCapacity = CalculatePlasticMomentCapacity(components, gammaM0);
        double momentCapacity = classification.SectionClass <= 2
            ? plasticMomentCapacity
            : classification.SectionClass == 3
                ? elasticMomentCapacity
                : 0.0;
        string momentBasis = classification.SectionClass <= 2
            ? "EC3 Class 1/2 plastic"
            : classification.SectionClass == 3
                ? "EC3 Class 3 elastic"
                : "EC3 Class 4 effective required";
        string shearBasis = IsWebShearBucklingLikely(webSegments, webThickness, webFy)
            ? "EC3 Vpl,Rd; shear buckling required"
            : "EC3 Vpl,Rd";

        return new SectionProperties(
            inertia,
            neutralAxis,
            momentCapacity,
            shearCapacity,
            flexuralRigidity,
            shearRigidity,
            classification.SectionClass,
            classification.FlangeClass,
            classification.WebClass,
            momentBasis,
            shearBasis,
            withinOpening,
            hasStiffener);
    }

    private static double CalculateElasticMomentCapacity(
        IReadOnlyCollection<SectionComponent> components,
        double neutralAxis,
        double flexuralRigidity,
        double gammaM0)
    {
        return components
            .Select(component =>
            {
                double c = Math.Max(
                    Math.Abs(component.TopZ - neutralAxis),
                    Math.Abs(component.BottomZ - neutralAxis));
                return c > Tolerance && component.ElasticModulusPa > Tolerance
                    ? component.YieldStrengthMpa * 1_000_000.0 * flexuralRigidity / (component.ElasticModulusPa * c) / 1000.0 / gammaM0
                    : double.PositiveInfinity;
            })
            .Where(double.IsFinite)
            .DefaultIfEmpty(0.0)
            .Min();
    }

    private static double CalculatePlasticMomentCapacity(IReadOnlyCollection<SectionComponent> components, double gammaM0)
    {
        if (components.Count == 0)
            return 0.0;

        double minZ = components.Min(component => component.BottomZ);
        double maxZ = components.Max(component => component.TopZ);
        double totalYieldForce = components.Sum(YieldForce);
        double targetForce = totalYieldForce / 2.0;
        double low = minZ;
        double high = maxZ;
        for (int iteration = 0; iteration < 80; iteration++)
        {
            double mid = (low + high) / 2.0;
            double compressionForce = components.Sum(component => YieldForceAbove(component, mid));
            if (compressionForce > targetForce)
                low = mid;
            else
                high = mid;
        }

        double plasticNeutralAxis = (low + high) / 2.0;
        double moment = components.Sum(component => YieldMomentAbout(component, plasticNeutralAxis));
        return moment / Math.Max(Tolerance, gammaM0) / 1000.0;
    }

    private static double YieldForce(SectionComponent component)
    {
        return component.Area * component.YieldStrengthMpa * 1_000_000.0;
    }

    private static double YieldForceAbove(SectionComponent component, double z)
    {
        double heightAbove = Math.Max(0.0, component.TopZ - Math.Max(z, component.BottomZ));
        return component.Width * heightAbove * component.YieldStrengthMpa * 1_000_000.0;
    }

    private static double YieldMomentAbout(SectionComponent component, double z)
    {
        double fy = component.YieldStrengthMpa * 1_000_000.0;
        double width = component.Width;
        double lower = component.BottomZ;
        double upper = component.TopZ;
        if (upper <= lower + Tolerance)
            return 0.0;

        if (z <= lower)
            return fy * width * ((upper - z) * (upper - z) - (lower - z) * (lower - z)) / 2.0;
        if (z >= upper)
            return fy * width * ((z - lower) * (z - lower) - (z - upper) * (z - upper)) / 2.0;

        double lowerMoment = fy * width * (z - lower) * (z - lower) / 2.0;
        double upperMoment = fy * width * (upper - z) * (upper - z) / 2.0;
        return lowerMoment + upperMoment;
    }

    private static SectionClassification ClassifySection(
        ParametricPlateGirderModel model,
        IReadOnlyCollection<SectionComponent> components,
        IReadOnlyCollection<Interval> webSegments,
        double neutralAxis)
    {
        double compressionFlangeFy = model.FlangeSteelYieldStrengthMpa;
        double epsilonFlange = Epsilon(compressionFlangeFy);
        double flangeOutstand = Math.Max(0.0, (model.FlangeWidth - model.WebThickness) / 2.0);
        int flangeClass = ClassifyOutstand(flangeOutstand / Math.Max(model.FlangeThickness, Tolerance), epsilonFlange);

        double maxWebRatio = webSegments
            .Select(segment => Math.Max(0.0, segment.Top - segment.Bottom) / Math.Max(model.WebThickness, Tolerance))
            .DefaultIfEmpty(0.0)
            .Max();
        int webClass = ClassifyInternalBending(maxWebRatio, Epsilon(model.WebSteelYieldStrengthMpa));

        int stiffenerClass = components
            .Where(component => component.Kind == PlateGirderComponentKind.Stiffener)
            .Select(component => ClassifyOutstand(component.Width / Math.Max(component.TopZ - component.BottomZ, Tolerance), Epsilon(component.YieldStrengthMpa)))
            .DefaultIfEmpty(1)
            .Max();

        int sectionClass = Math.Max(Math.Max(flangeClass, webClass), stiffenerClass);
        return new SectionClassification(sectionClass, flangeClass, webClass);
    }

    private static bool IsWebShearBucklingLikely(IEnumerable<Interval> webSegments, double webThickness, double webFy)
    {
        double epsilon = Epsilon(webFy);
        double limit = 72.0 * epsilon;
        return webSegments.Any(segment => (segment.Top - segment.Bottom) / Math.Max(webThickness, Tolerance) > limit);
    }

    private static double Epsilon(double yieldStrengthMpa)
    {
        return Math.Sqrt(235.0 / Math.Max(yieldStrengthMpa, Tolerance));
    }

    private static int ClassifyOutstand(double ratio, double epsilon)
    {
        if (ratio <= 9.0 * epsilon)
            return 1;
        if (ratio <= 10.0 * epsilon)
            return 2;
        if (ratio <= 14.0 * epsilon)
            return 3;
        return 4;
    }

    private static int ClassifyInternalBending(double ratio, double epsilon)
    {
        if (ratio <= 72.0 * epsilon)
            return 1;
        if (ratio <= 83.0 * epsilon)
            return 2;
        if (ratio <= 124.0 * epsilon)
            return 3;
        return 4;
    }

    private static List<OpeningLocalBounds> BuildOpeningBounds(ParametricPlateGirderModel model)
    {
        List<PlateGirderOpening> openings = model.Openings.Count > 0
            ? model.Openings
            :
            [
                new PlateGirderOpening
                {
                    Id = "OP01",
                    CenterX = model.OpeningCenterX,
                    CenterZ = model.OpeningCenterZ,
                    Width = model.OpeningWidth,
                    Height = model.OpeningHeight,
                    Strengthen = model.StrengthenOpening,
                    StrengthenTop = model.StrengthenOpeningTop,
                    StrengthenBottom = model.StrengthenOpeningBottom,
                    StrengthenLeft = model.StrengthenOpeningLeft,
                    StrengthenRight = model.StrengthenOpeningRight,
                    StiffenerOutstand = model.OpeningStiffenerWidth,
                    StiffenerExtension = model.OpeningStiffenerExtension
                }
            ];

        if (!model.HasWebOpening)
            return [];

        return openings
            .Where(opening => opening.Width > Tolerance && opening.Height > Tolerance)
            .Select(opening =>
            {
                double left = Math.Max(0.0, opening.CenterX - opening.Width / 2.0);
                double right = Math.Min(model.Length, opening.CenterX + opening.Width / 2.0);
                double bottom = Math.Max(0.0, opening.CenterZ - opening.Height / 2.0);
                double top = Math.Min(model.Depth, opening.CenterZ + opening.Height / 2.0);
                return new OpeningLocalBounds(left, right, bottom, top, opening);
            })
            .Where(opening => opening.Right - opening.Left > Tolerance && opening.Top - opening.Bottom > Tolerance)
            .ToList();
    }

    private static List<Interval> SubtractInterval(IEnumerable<Interval> source, Interval cut)
    {
        var result = new List<Interval>();
        foreach (Interval segment in source)
        {
            double cutBottom = Math.Max(segment.Bottom, cut.Bottom);
            double cutTop = Math.Min(segment.Top, cut.Top);
            if (cutTop <= cutBottom + Tolerance)
            {
                result.Add(segment);
                continue;
            }

            if (cutBottom > segment.Bottom + Tolerance)
                result.Add(new Interval(segment.Bottom, cutBottom));
            if (cutTop < segment.Top - Tolerance)
                result.Add(new Interval(cutTop, segment.Top));
        }

        return result;
    }

    private static void AddRect(
        List<SectionComponent> components,
        PlateGirderComponentKind kind,
        double width,
        double height,
        double centroidZ,
        double bottomZ,
        double topZ,
        double yieldStrengthMpa,
        double elasticModulusPa)
    {
        if (width <= Tolerance || height <= Tolerance)
            return;

        double area = width * height;
        double localIy = width * Math.Pow(height, 3) / 12.0;
        components.Add(new SectionComponent(kind, width, area, centroidZ, localIy, bottomZ, topZ, yieldStrengthMpa, elasticModulusPa));
    }

    private static double[] SolveLinearSystem(double[,] a, double[] b)
    {
        int n = b.Length;
        double[,] matrix = new double[n, n];
        double[] rhs = new double[n];
        Array.Copy(a, matrix, a.Length);
        Array.Copy(b, rhs, b.Length);

        for (int pivot = 0; pivot < n; pivot++)
        {
            int maxRow = pivot;
            double maxValue = Math.Abs(matrix[pivot, pivot]);
            for (int row = pivot + 1; row < n; row++)
            {
                double value = Math.Abs(matrix[row, pivot]);
                if (value > maxValue)
                {
                    maxValue = value;
                    maxRow = row;
                }
            }

            if (maxValue <= Tolerance)
                throw new InvalidOperationException("The plate girder stiffness matrix is singular.");

            if (maxRow != pivot)
            {
                for (int column = pivot; column < n; column++)
                    (matrix[pivot, column], matrix[maxRow, column]) = (matrix[maxRow, column], matrix[pivot, column]);
                (rhs[pivot], rhs[maxRow]) = (rhs[maxRow], rhs[pivot]);
            }

            double diagonal = matrix[pivot, pivot];
            for (int column = pivot; column < n; column++)
                matrix[pivot, column] /= diagonal;
            rhs[pivot] /= diagonal;

            for (int row = 0; row < n; row++)
            {
                if (row == pivot)
                    continue;

                double factor = matrix[row, pivot];
                if (Math.Abs(factor) <= Tolerance)
                    continue;

                for (int column = pivot; column < n; column++)
                    matrix[row, column] -= factor * matrix[pivot, column];
                rhs[row] -= factor * rhs[pivot];
            }
        }

        return rhs;
    }

    private readonly record struct SectionComponent(
        PlateGirderComponentKind Kind,
        double Width,
        double Area,
        double CentroidZ,
        double LocalIy,
        double BottomZ,
        double TopZ,
        double YieldStrengthMpa,
        double ElasticModulusPa);

    private readonly record struct SectionProperties(
        double InertiaY,
        double NeutralAxisZ,
        double MomentCapacityKnM,
        double ShearCapacityKn,
        double FlexuralRigidity,
        double ShearRigidity,
        int SectionClass,
        int FlangeClass,
        int WebClass,
        string MomentCapacityBasis,
        string ShearCapacityBasis,
        bool WithinOpening,
        bool HasStiffener);

    private readonly record struct SectionClassification(int SectionClass, int FlangeClass, int WebClass);

    private readonly record struct OpeningLocalBounds(
        double Left,
        double Right,
        double Bottom,
        double Top,
        PlateGirderOpening Definition);

    private readonly record struct Interval(double Bottom, double Top);

    private enum PlateGirderComponentKind
    {
        Web,
        Flange,
        Stiffener
    }
}
