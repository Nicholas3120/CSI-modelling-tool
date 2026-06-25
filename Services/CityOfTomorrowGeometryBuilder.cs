using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class CityOfTomorrowGeometryBuilder
{
    private const double Tolerance = 0.000001;

    public CityOfTomorrowModel Build(CityOfTomorrowInput input)
    {
        int n = Math.Max(1, input.PanelsPerHalfN);
        int p = 2 * n;
        double span = Math.Max(input.ClearSpanL, 0.001);
        double panelWidth = span / p;
        double zBottom = input.BottomChordLevelZ;
        double zMid = zBottom + input.MidRailRatio * input.VierendeelDepthH;
        double zTop = zBottom + input.VierendeelDepthH;
        string structureId = EtabsNameUtility.BuildSafeName("", input.StructureId);
        var model = new CityOfTomorrowModel
        {
            StructureId = structureId,
            GroupName = EtabsNameUtility.BuildSafeName("GEN_VIERENDEEL_", structureId),
            Input = input
        };
        var points = new PointRegistry(model.Nodes);

        for (int index = 0; index <= p; index++)
        {
            double x = -span / 2.0 + index * panelWidth;
            points.AddOrGet(BottomKey(index), x, 0, zBottom, primary: true);
            points.AddOrGet(MidKey(index), x, 0, zMid, primary: true);
            points.AddOrGet(TopKey(index), x, 0, zTop, primary: true);
        }

        for (int index = 0; index < p; index++)
        {
            AddFrame(model, $"VFR_TOP_{index:000}", TopKey(index), TopKey(index + 1), CityMemberGroups.TopChord, input.TopChordSection);
            AddFrame(model, $"VFR_MID_{index:000}", MidKey(index), MidKey(index + 1), CityMemberGroups.MidRail, input.MidRailSection);
            AddFrame(model, $"VFR_BOT_{index:000}", BottomKey(index), BottomKey(index + 1), CityMemberGroups.BottomChord, input.BottomChordSection);
        }

        for (int index = 1; index < p; index++)
        {
            AddFrame(model, $"VFR_VERT_L_{index:000}", BottomKey(index), MidKey(index), CityMemberGroups.VerticalPost, input.VerticalPostSection);
            AddFrame(model, $"VFR_VERT_U_{index:000}", MidKey(index), TopKey(index), CityMemberGroups.VerticalPost, input.VerticalPostSection);
        }

        for (int index = 1; index <= n; index++)
        {
            AddCable(model, $"VFR_CABLE_L_{index:000}", TopKey(0), BottomKey(index), CityMemberGroups.InternalCable, input.CableSection);
            AddCable(model, $"VFR_CABLE_R_{index:000}", TopKey(p), BottomKey(p - index), CityMemberGroups.InternalCable, input.CableSection);
        }

        BuildEndSupport(model, points, true, p, zBottom, zMid, zTop);
        BuildEndSupport(model, points, false, p, zBottom, zMid, zTop);
        AddCable(model, "VFR_GLOBAL_TIE", "VFR.LeftTie", "VFR.RightTie", CityMemberGroups.GlobalTie, input.TieCableSection, CityMemberKind.Tie);
        return model;
    }

    private static void BuildEndSupport(CityOfTomorrowModel model, PointRegistry points, bool left, int p, double zBottom, double zMid, double zTop)
    {
        CityOfTomorrowInput input = model.Input;
        string side = left ? "Left" : "Right";
        double sign = left ? -1 : 1;
        double towerX = sign * input.ClearSpanL / 2;
        double outerX = towerX + sign * input.ExternalAnchorWidth;
        double upperZ = input.PileCapLevelZ + input.ExternalSideFrameHeight;
        double pileDepth = Math.Max(1.5, input.ExternalSideFrameHeight * 0.3);
        int endIndex = left ? 0 : p;
        string baseKey = $"VFR.{side}TowerBase";
        string tieKey = $"VFR.{side}Tie";
        string outerLower = $"VFR.{side}OuterLower";
        string outerUpper = $"VFR.{side}OuterUpper";
        string innerToe = $"VFR.{side}InnerPileToe";
        string outerToe = $"VFR.{side}OuterPileToe";

        points.AddOrGet(baseKey, towerX, 0, input.PileCapLevelZ);
        points.AddOrGet(tieKey, towerX, 0, input.TieLevelZ);
        points.AddOrGet(outerLower, outerX, 0, input.PileCapLevelZ);
        points.AddOrGet(outerUpper, outerX, 0, upperZ);
        points.AddOrGet(innerToe, towerX, 0, input.PileCapLevelZ - pileDepth, support: true);
        points.AddOrGet(outerToe, outerX, 0, input.PileCapLevelZ - pileDepth, support: true);
        string upperTower = ResolveTowerLevel(points, side, towerX, upperZ, endIndex, input, zBottom, zMid, zTop);

        var levels = new[] { baseKey, tieKey, BottomKey(endIndex), MidKey(endIndex), upperTower, TopKey(endIndex) }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(points.GetRequired)
            .OrderBy(node => node.Z)
            .GroupBy(node => Math.Round(node.Z, 6))
            .Select(group => group.First())
            .ToList();
        for (int index = 0; index < levels.Count - 1; index++)
            AddFrame(model, $"VFR_{side.ToUpperInvariant()}_TOWER_{index:000}", levels[index].Key, levels[index + 1].Key, CityMemberGroups.Tower, input.TowerSection);

        AddFrame(model, $"VFR_{side.ToUpperInvariant()}_PILE_CAP", baseKey, outerLower, CityMemberGroups.Foundation, input.SideFrameSection, CityMemberKind.Support);
        AddFrame(model, $"VFR_{side.ToUpperInvariant()}_PILE_INNER", baseKey, innerToe, CityMemberGroups.Foundation, input.TowerSection, CityMemberKind.Support);
        AddFrame(model, $"VFR_{side.ToUpperInvariant()}_PILE_OUTER", outerLower, outerToe, CityMemberGroups.Foundation, input.TowerSection, CityMemberKind.Support);
        AddFrame(model, $"VFR_{side.ToUpperInvariant()}_SIDE_VERTICAL", outerLower, outerUpper, CityMemberGroups.SideFrame, input.SideFrameSection);
        AddFrame(model, $"VFR_{side.ToUpperInvariant()}_SIDE_STRUT", upperTower, outerUpper, CityMemberGroups.SideFrame, input.SideFrameSection);
        AddFrame(model, $"VFR_{side.ToUpperInvariant()}_SIDE_X1", baseKey, outerUpper, CityMemberGroups.SideFrame, input.SideFrameSection);
        AddFrame(model, $"VFR_{side.ToUpperInvariant()}_SIDE_X2", upperTower, outerLower, CityMemberGroups.SideFrame, input.SideFrameSection);
        AddCable(model, $"VFR_{side.ToUpperInvariant()}_BACKSTAY", TopKey(endIndex), outerUpper, CityMemberGroups.Backstay, input.CableSection);
    }

    private static string ResolveTowerLevel(PointRegistry points, string side, double x, double z, int endIndex, CityOfTomorrowInput input, double bottom, double mid, double top)
    {
        if (Near(z, input.PileCapLevelZ)) return $"VFR.{side}TowerBase";
        if (Near(z, input.TieLevelZ)) return $"VFR.{side}Tie";
        if (Near(z, bottom)) return BottomKey(endIndex);
        if (Near(z, mid)) return MidKey(endIndex);
        if (Near(z, top)) return TopKey(endIndex);
        string key = $"VFR.{side}TowerStrut";
        points.AddOrGet(key, x, 0, z);
        return key;
    }

    private static void AddFrame(CityOfTomorrowModel model, string id, string start, string end, string group, string section, CityMemberKind kind = CityMemberKind.Frame) =>
        model.Members.Add(new CityMember { Id = id, StartNodeKey = start, EndNodeKey = end, Group = group, SectionName = section ?? "", Kind = kind });

    private static void AddCable(CityOfTomorrowModel model, string id, string start, string end, string group, string section, CityMemberKind kind = CityMemberKind.Cable) =>
        model.Members.Add(new CityMember { Id = id, StartNodeKey = start, EndNodeKey = end, Group = group, SectionName = section ?? "", Kind = kind, IsTensionOnly = true });

    private static bool Near(double a, double b) => Math.Abs(a - b) <= Tolerance;
    public static string BottomKey(int index) => $"VFR.B.{index:000}";
    public static string MidKey(int index) => $"VFR.M.{index:000}";
    public static string TopKey(int index) => $"VFR.T.{index:000}";

    private sealed class PointRegistry
    {
        private readonly List<CityNode> _nodes;
        private readonly Dictionary<string, CityNode> _byKey = new(StringComparer.OrdinalIgnoreCase);
        public PointRegistry(List<CityNode> nodes) => _nodes = nodes;
        public CityNode AddOrGet(string key, double x, double y, double z, bool support = false, bool primary = false)
        {
            if (_byKey.TryGetValue(key, out CityNode? existing)) return existing;
            var node = new CityNode { Key = key, X = x, Y = y, Z = z, IsSupport = support, IsPrimaryJoint = primary };
            _byKey.Add(key, node);
            _nodes.Add(node);
            return node;
        }
        public CityNode GetRequired(string key) => _byKey.TryGetValue(key, out CityNode? node) ? node : throw new InvalidOperationException($"Point '{key}' is not registered.");
    }
}
