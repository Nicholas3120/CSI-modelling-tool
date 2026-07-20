using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CotArchGeometryRegression;

internal static class Program
{
    private const double Tolerance = 0.000001;

    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("Sample geometry matches expected counts", SampleGeometryMatchesExpectedCounts),
            ("Arch endpoints and crown are exact", ArchEndpointsAndCrownAreExact),
            ("Upper beam terminates at end posts", UpperBeamTerminatesAtEndPosts),
            ("Vertical posts stand on arch nodes", VerticalPostsStandOnArchNodes),
            ("Zero-height crown post becomes shared joint", ZeroHeightCrownPostBecomesSharedJoint),
            ("Springing joints are shared", SpringingJointsAreShared),
            ("Extra arch segments subdivide post bays", ExtraArchSegmentsSubdividePostBays),
            ("Upper beam UDL validation requires pattern and magnitude", UpperBeamUdlValidationRequiresPatternAndMagnitude),
            ("Upper beam point load validation requires pattern and magnitude", UpperBeamPointLoadValidationRequiresPatternAndMagnitude)
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
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} CoT Arch geometry regression tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void SampleGeometryMatchesExpectedCounts()
    {
        CotArchModel model = BuildSample();
        Equal(8, model.ArchSegmentCount, "Sample should create 8 arch frame objects.");
        Equal(9, model.VerticalPostCount, "Sample should create 9 vertical posts.");
        Equal(8, model.UpperBeamSegmentCount, "Sample should create 8 upper-beam frame objects.");
        Equal(1, model.TensionTieCount, "Sample should create 1 tension tie.");
        Equal(2, model.SupportColumnCount, "Sample should create 2 support columns.");
        Equal(28, model.FrameMemberCount, "Sample should create 28 total frame objects.");
    }

    private static void ArchEndpointsAndCrownAreExact()
    {
        CotArchModel model = BuildSample();
        CotArchNode left = model.ArchNodes.First();
        CotArchNode crown = model.ArchNodes.Single(node => Math.Abs(node.Xi - 0.5) <= Tolerance);
        CotArchNode right = model.ArchNodes.Last();

        NearlyEqual(0, left.X, "Left arch endpoint X should equal origin.");
        NearlyEqual(0, left.Z, "Left arch endpoint Z should equal springing.");
        NearlyEqual(20, crown.X, "Crown X should be at midspan.");
        NearlyEqual(8, crown.Z, "Crown Z should equal springing plus rise.");
        NearlyEqual(40, right.X, "Right arch endpoint X should equal origin plus span.");
        NearlyEqual(0, right.Z, "Right arch endpoint Z should equal springing.");
    }

    private static void UpperBeamTerminatesAtEndPosts()
    {
        CotArchModel model = BuildSample();
        Dictionary<string, CotArchNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        List<CotArchMember> beams = model.Members.Where(member => member.Kind == CotArchMemberKind.UpperBeam).ToList();

        CotArchNode firstStart = nodes[beams.First().StartNodeId];
        CotArchNode lastEnd = nodes[beams.Last().EndNodeId];
        NearlyEqual(0, firstStart.X, "First upper-beam node must be the left end post top.");
        NearlyEqual(40, lastEnd.X, "Last upper-beam node must be the right end post top.");
        True(beams.All(member => nodes[member.StartNodeId].X >= -Tolerance && nodes[member.EndNodeId].X <= 40 + Tolerance),
            "Upper-beam members must not extend outside the span.");
    }

    private static void VerticalPostsStandOnArchNodes()
    {
        CotArchModel model = BuildSample();
        for (int index = 0; index < model.PostBottomNodes.Count; index++)
        {
            CotArchNode bottom = model.PostBottomNodes[index];
            CotArchNode top = model.PostTopNodes[index];
            NearlyEqual(bottom.X, top.X, $"Post {index} bottom/top X should match.");
            NearlyEqual(bottom.Y, top.Y, $"Post {index} bottom/top Y should match.");
            True(top.Z > bottom.Z, $"Post {index} should have positive length.");
            True(model.ArchNodes.Any(node => node.Id == bottom.Id), $"Post {index} bottom should reuse an arch node.");
        }
    }

    private static void SpringingJointsAreShared()
    {
        CotArchModel model = BuildSample();
        string left = model.LeftSpringing?.Id ?? "";
        string right = model.RightSpringing?.Id ?? "";

        Equal(left, model.Members.First(member => member.Kind == CotArchMemberKind.Arch).StartNodeId, "First arch segment should start at left springing.");
        Equal(right, model.Members.Last(member => member.Kind == CotArchMemberKind.Arch).EndNodeId, "Last arch segment should end at right springing.");
        Equal(left, model.Members.First(member => member.Kind == CotArchMemberKind.VerticalPost).StartNodeId, "Left end post should start at left springing.");
        Equal(right, model.Members.Last(member => member.Kind == CotArchMemberKind.VerticalPost).StartNodeId, "Right end post should start at right springing.");
        Equal(left, model.Members.Single(member => member.Kind == CotArchMemberKind.TensionTie).StartNodeId, "Tie should start at left springing.");
        Equal(right, model.Members.Single(member => member.Kind == CotArchMemberKind.TensionTie).EndNodeId, "Tie should end at right springing.");
        Equal(left, model.Members.Single(member => member.Id.EndsWith("_F_SUPPORT_L", StringComparison.OrdinalIgnoreCase)).EndNodeId, "Left support column should end at left springing.");
        Equal(right, model.Members.Single(member => member.Id.EndsWith("_F_SUPPORT_R", StringComparison.OrdinalIgnoreCase)).EndNodeId, "Right support column should end at right springing.");
    }

    private static void ZeroHeightCrownPostBecomesSharedJoint()
    {
        CotArchInput input = SampleInput();
        input.UpperBeamZ = input.SpringingZ + input.Rise;
        CotArchModel model = new CotArchGeometryBuilder().Build(input);
        var validator = new CotArchValidator();
        ParametricValidationResult validation = validator.Validate(model);

        Equal(8, model.VerticalPostCount, "The zero-height crown post should not be generated as a frame object.");
        Equal(model.PostBottomNodes[4].Id, model.PostTopNodes[4].Id, "The crown post top should reuse the arch node.");
        False(model.Members.Any(member => IsZeroLength(member, model)), "Generated CoT Arch members should not contain zero-length frames.");
        False(validation.HasCriticalIssues, "A zero-height post station at the arch crown should not block generation.");
    }

    private static void ExtraArchSegmentsSubdividePostBays()
    {
        CotArchInput input = SampleInput();
        input.ArchSegmentsPerPostBay = 2;
        CotArchModel model = new CotArchGeometryBuilder().Build(input);
        Equal(16, model.ArchSegmentCount, "Two arch segments per bay should create 16 arch members for 9 posts.");
        Equal(17, model.ArchNodes.Count, "Two arch segments per bay should create 17 arch nodes for 9 posts.");
        Equal(9, model.VerticalPostCount, "Extra arch nodes must not create extra vertical posts.");
    }

    private static void UpperBeamUdlValidationRequiresPatternAndMagnitude()
    {
        CotArchInput input = SampleInput();
        input.UpperBeamLoadType = CotArchUpperBeamLoadType.Udl;
        input.UpperBeamLoadPattern = "";
        input.UpperBeamUdlKnPerM = 10;
        True(new CotArchValidator().Validate(new CotArchGeometryBuilder().Build(input)).HasCriticalIssues,
            "UDL loading should require a load pattern.");

        input.UpperBeamLoadPattern = "DEAD";
        input.UpperBeamUdlKnPerM = 0;
        True(new CotArchValidator().Validate(new CotArchGeometryBuilder().Build(input)).HasCriticalIssues,
            "UDL loading should require a positive magnitude.");

        input.UpperBeamUdlKnPerM = 10;
        False(new CotArchValidator().Validate(new CotArchGeometryBuilder().Build(input)).HasCriticalIssues,
            "UDL loading with pattern and positive magnitude should pass geometry validation.");
    }

    private static void UpperBeamPointLoadValidationRequiresPatternAndMagnitude()
    {
        CotArchInput input = SampleInput();
        input.UpperBeamLoadType = CotArchUpperBeamLoadType.PointLoadAtJoints;
        input.UpperBeamLoadPattern = "";
        input.UpperBeamPointLoadKn = 50;
        True(new CotArchValidator().Validate(new CotArchGeometryBuilder().Build(input)).HasCriticalIssues,
            "Point loading should require a load pattern.");

        input.UpperBeamLoadPattern = "LIVE";
        input.UpperBeamPointLoadKn = 0;
        True(new CotArchValidator().Validate(new CotArchGeometryBuilder().Build(input)).HasCriticalIssues,
            "Point loading should require a positive magnitude.");

        input.UpperBeamPointLoadKn = 50;
        False(new CotArchValidator().Validate(new CotArchGeometryBuilder().Build(input)).HasCriticalIssues,
            "Point loading with pattern and positive magnitude should pass geometry validation.");
    }

    private static CotArchModel BuildSample()
    {
        return new CotArchGeometryBuilder().Build(SampleInput());
    }

    private static CotArchInput SampleInput()
    {
        return new CotArchInput
        {
            ModelPrefix = "TA01",
            OriginX = 0,
            PlaneY = 0,
            BaseZ = -8,
            SpringingZ = 0,
            Span = 40,
            Rise = 8,
            UpperBeamZ = 12,
            PostCount = 9,
            ArchSegmentsPerPostBay = 1,
            ProfileType = CotArchProfileType.Parabolic,
            ArchSection = "SEC",
            PostSection = "SEC",
            UpperBeamSection = "SEC",
            TieSection = "SEC",
            SupportColumnSection = "SEC"
        };
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

    private static bool IsZeroLength(CotArchMember member, CotArchModel model)
    {
        Dictionary<string, CotArchNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        CotArchNode start = nodes[member.StartNodeId];
        CotArchNode end = nodes[member.EndNodeId];
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= Tolerance;
    }
}
