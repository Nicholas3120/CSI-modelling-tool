using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace ParametricTrussRegression;

internal static class Program
{
    private const double Tolerance = 0.000001;

    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("Y bay duplication clones base X-Z truss", YBayDuplicationClonesBaseXZTruss),
            ("Orthogonal Y-Z trusses use constant-X planes", OrthogonalYZTrussesUseConstantXPlanes),
            ("Orthogonal Y-Z requires at least two Y bay lines", OrthogonalYZRequiresAtLeastTwoYBayLines)
        ];

        int failed = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine("      " + ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} Parametric Truss regression tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void YBayDuplicationClonesBaseXZTruss()
    {
        var generator = new ParametricTrussGenerator();
        ParametricTrussModel singleBay = generator.Generate(BaseOptions());

        ParametricTrussOptions options = BaseOptions();
        options.YBayCount = 3;
        options.YBaySpacing = 10.0;

        ParametricTrussModel model = generator.Generate(options);

        Equal(3, model.YBayCount, "Model should record the requested Y bay count.");
        NearlyEqual(10.0, model.YBaySpacing, "Model should record the requested Y bay spacing.");
        Equal(singleBay.Nodes.Count * 3, model.Nodes.Count, "Y bay array should clone the base X-Z truss nodes once per Y bay line.");
        Equal(singleBay.Members.Count * 3, model.Members.Count, "Y bay array should clone the base X-Z truss members once per Y bay line.");
        False(model.Members.Any(member => IsYZGroup(member.Group)), "Pure Y bay duplication should not add orthogonal Y-Z members.");

        List<double> yCoordinates = model.Nodes
            .Select(node => Round(node.Y))
            .Distinct()
            .Order()
            .ToList();

        SequenceEqual([0.0, 10.0, 20.0], yCoordinates, "Y bay duplication should place truss lines at 0, spacing, and 2 x spacing.");
    }

    private static void OrthogonalYZTrussesUseConstantXPlanes()
    {
        var generator = new ParametricTrussGenerator();
        ParametricTrussOptions options = BaseOptions();
        options.YBayCount = 2;
        options.YBaySpacing = 10.0;
        options.GenerateOrthogonalYZTrusses = true;
        options.OrthogonalYZTrussType = TrussType.Warren;
        options.OrthogonalYZPlacementMode = OrthogonalTrussPlacementMode.EndLinesOnly;
        options.OrthogonalYZPanelsPerBay = 2;

        ParametricTrussModel model = generator.Generate(options);
        Dictionary<string, ParametricNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        List<ParametricMember> yzMembers = model.Members.Where(member => IsYZGroup(member.Group)).ToList();

        Equal(2, model.OrthogonalYZTrussLineCount, "End-lines-only placement should create one Y-Z truss at each end X station.");
        Equal(2, model.OrthogonalYZPanelsPerBay, "Model should record the requested Y-Z panels per Y bay.");
        True(yzMembers.Count > 0, "Orthogonal Y-Z generation should add Y-Z member groups.");

        foreach (ParametricMember member in yzMembers)
        {
            ParametricNode start = nodes[member.StartNodeId];
            ParametricNode end = nodes[member.EndNodeId];
            NearlyEqual(start.X, end.X, $"Y-Z member '{member.Id}' should stay in a constant-X plane.");
        }

        List<double> xStations = yzMembers
            .SelectMany(member => new[] { nodes[member.StartNodeId].X, nodes[member.EndNodeId].X })
            .Select(Round)
            .Distinct()
            .Order()
            .ToList();

        SequenceEqual([0.0, 12.0], xStations, "End-lines-only Y-Z placement should use only the first and last X stations.");
        True(model.Nodes.Any(node => Math.Abs(node.Y - 5.0) <= Tolerance && (node.IsTopChord || node.IsBottomChord)),
            "Two Y-Z panels inside a 10 m Y bay should create intermediate chord nodes at Y = 5 m.");
    }

    private static void OrthogonalYZRequiresAtLeastTwoYBayLines()
    {
        var generator = new ParametricTrussGenerator();
        ParametricTrussOptions options = BaseOptions();
        options.YBayCount = 1;
        options.GenerateOrthogonalYZTrusses = true;
        options.OrthogonalYZPanelsPerBay = 2;

        ParametricTrussModel model = generator.Generate(options);

        Equal(0, model.OrthogonalYZTrussLineCount, "A single Y bay line cannot span an orthogonal Y-Z truss.");
        False(model.Members.Any(member => IsYZGroup(member.Group)), "A single Y bay line should not produce Y-Z members.");
        True(model.Warnings.Any(warning => warning.Contains("at least 2 Y bay lines", StringComparison.OrdinalIgnoreCase)),
            "Generator should warn the user why orthogonal Y-Z trusses were skipped.");
    }

    private static ParametricTrussOptions BaseOptions()
    {
        return new ParametricTrussOptions
        {
            TrussId = "REG_TRUSS",
            GroupName = "REG_GROUP",
            TrussType = TrussType.Warren,
            StartPoint = new ModelPoint3d { X = 0, Y = 0, Z = 0 },
            EndPoint = new ModelPoint3d { X = 12, Y = 0, Z = 0 },
            Height = 3,
            PanelCount = 4,
            ApplyTopChordLoad = false,
            ApplyBottomChordLoad = false
        };
    }

    private static bool IsYZGroup(string group)
    {
        return string.Equals(group, ParametricMemberGroups.YZTopChord, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(group, ParametricMemberGroups.YZBottomChord, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(group, ParametricMemberGroups.YZDiagonal, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(group, ParametricMemberGroups.YZVertical, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(group, ParametricMemberGroups.YZEndPost, StringComparison.OrdinalIgnoreCase);
    }

    private static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', actual '{actual}'.");
    }

    private static void NearlyEqual(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > Tolerance)
            throw new InvalidOperationException($"{message} Expected {expected:0.######}, actual {actual:0.######}.");
    }

    private static void SequenceEqual(IReadOnlyList<double> expected, IReadOnlyList<double> actual, string message)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException($"{message} Expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");

        for (int index = 0; index < expected.Count; index++)
            NearlyEqual(expected[index], actual[index], message);
    }
}
