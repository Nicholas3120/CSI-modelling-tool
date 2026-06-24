using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed class PlateGirderOptions
{
    public string PlateGirderId { get; set; } = "PG01";
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double OriginZ { get; set; }
    public double Length { get; set; } = 12.0;
    public double Depth { get; set; } = 1.8;
    public double FlangeWidth { get; set; } = 0.45;
    public double WebThickness { get; set; } = 0.012;
    public double FlangeThickness { get; set; } = 0.02;
    public double StiffenerThickness { get; set; } = 0.012;
    public double WebSteelYieldStrengthMpa { get; set; } = 355.0;
    public double FlangeSteelYieldStrengthMpa { get; set; } = 355.0;
    public double StiffenerSteelYieldStrengthMpa { get; set; } = 355.0;
    public double ElasticModulusGpa { get; set; } = 200.0;
    public double WebElasticModulusGpa { get; set; } = 200.0;
    public double FlangeElasticModulusGpa { get; set; } = 200.0;
    public double StiffenerElasticModulusGpa { get; set; } = 200.0;
    public double AnalysisUniformLoadKnPerM { get; set; } = 30.0;
    public bool ApplyTopFlangeAreaLoad { get; set; }
    public string LoadPattern { get; set; } = "";
    public int LengthDivisions { get; set; } = 24;
    public int DepthDivisions { get; set; } = 8;
    public int FlangeWidthDivisions { get; set; } = 2;
    public bool GenerateTopFlange { get; set; } = true;
    public bool GenerateBottomFlange { get; set; } = true;
    public bool HasWebOpening { get; set; } = true;
    public double OpeningCenterX { get; set; } = 6.0;
    public double OpeningCenterZ { get; set; } = 0.9;
    public double OpeningWidth { get; set; } = 1.5;
    public double OpeningHeight { get; set; } = 0.7;
    public bool StrengthenOpening { get; set; } = true;
    public bool StrengthenOpeningTop { get; set; } = true;
    public bool StrengthenOpeningBottom { get; set; } = true;
    public bool StrengthenOpeningLeft { get; set; } = true;
    public bool StrengthenOpeningRight { get; set; } = true;
    public double OpeningStiffenerWidth { get; set; } = 0.15;
    public double OpeningStiffenerExtension { get; set; }
    public string WebShellPropertyName { get; set; } = "";
    public string FlangeShellPropertyName { get; set; } = "";
    public string StiffenerShellPropertyName { get; set; } = "";
    public double GammaM0 { get; set; } = 1.0;
    public List<PlateGirderOpening> Openings { get; set; } = [];
}

public sealed class PlateGirderGenerator
{
    private const double Tolerance = 0.000001;

    public ParametricPlateGirderModel Generate(PlateGirderOptions options)
    {
        string plateGirderId = EtabsNameUtility.BuildSafeName("", options.PlateGirderId, 24);
        double length = PositiveOrDefault(options.Length, 12.0);
        double depth = PositiveOrDefault(options.Depth, 1.8);
        double flangeWidth = PositiveOrDefault(options.FlangeWidth, 0.45);
        int lengthDivisions = Math.Clamp(options.LengthDivisions, 1, 500);
        int depthDivisions = Math.Clamp(options.DepthDivisions, 1, 300);
        int flangeWidthDivisions = Math.Clamp(options.FlangeWidthDivisions, 1, 20);

        var model = new ParametricPlateGirderModel
        {
            PlateGirderId = plateGirderId,
            GroupName = EtabsNameUtility.BuildSafeName("WPF_PLATE_GIRDER_", plateGirderId),
            OriginX = FiniteOrDefault(options.OriginX, 0.0),
            OriginY = FiniteOrDefault(options.OriginY, 0.0),
            OriginZ = FiniteOrDefault(options.OriginZ, 0.0),
            Length = length,
            Depth = depth,
            FlangeWidth = flangeWidth,
            WebThickness = PositiveOrDefault(options.WebThickness, 0.012),
            FlangeThickness = PositiveOrDefault(options.FlangeThickness, 0.02),
            StiffenerThickness = PositiveOrDefault(options.StiffenerThickness, 0.012),
            WebSteelYieldStrengthMpa = PositiveOrDefault(options.WebSteelYieldStrengthMpa, 355.0),
            FlangeSteelYieldStrengthMpa = PositiveOrDefault(options.FlangeSteelYieldStrengthMpa, 355.0),
            StiffenerSteelYieldStrengthMpa = PositiveOrDefault(options.StiffenerSteelYieldStrengthMpa, 355.0),
            ElasticModulusGpa = PositiveOrDefault(options.ElasticModulusGpa, 200.0),
            WebElasticModulusGpa = PositiveOrDefault(options.WebElasticModulusGpa, PositiveOrDefault(options.ElasticModulusGpa, 200.0)),
            FlangeElasticModulusGpa = PositiveOrDefault(options.FlangeElasticModulusGpa, PositiveOrDefault(options.ElasticModulusGpa, 200.0)),
            StiffenerElasticModulusGpa = PositiveOrDefault(options.StiffenerElasticModulusGpa, PositiveOrDefault(options.ElasticModulusGpa, 200.0)),
            AnalysisUniformLoadKnPerM = Math.Max(0.0, FiniteOrDefault(options.AnalysisUniformLoadKnPerM, 30.0)),
            ApplyTopFlangeAreaLoad = options.ApplyTopFlangeAreaLoad,
            LoadPattern = options.LoadPattern ?? "",
            LengthDivisions = lengthDivisions,
            DepthDivisions = depthDivisions,
            FlangeWidthDivisions = flangeWidthDivisions,
            GenerateTopFlange = options.GenerateTopFlange,
            GenerateBottomFlange = options.GenerateBottomFlange,
            HasWebOpening = options.HasWebOpening,
            OpeningCenterX = FiniteOrDefault(options.OpeningCenterX, length / 2.0),
            OpeningCenterZ = FiniteOrDefault(options.OpeningCenterZ, depth / 2.0),
            OpeningWidth = PositiveOrDefault(options.OpeningWidth, length * 0.15),
            OpeningHeight = PositiveOrDefault(options.OpeningHeight, depth * 0.3),
            StrengthenOpening = options.StrengthenOpening,
            StrengthenOpeningTop = options.StrengthenOpeningTop,
            StrengthenOpeningBottom = options.StrengthenOpeningBottom,
            StrengthenOpeningLeft = options.StrengthenOpeningLeft,
            StrengthenOpeningRight = options.StrengthenOpeningRight,
            OpeningStiffenerWidth = PositiveOrDefault(options.OpeningStiffenerWidth, 0.15),
            OpeningStiffenerExtension = Math.Max(0.0, FiniteOrDefault(options.OpeningStiffenerExtension, 0.0)),
            WebShellPropertyName = options.WebShellPropertyName ?? "",
            FlangeShellPropertyName = options.FlangeShellPropertyName ?? "",
            StiffenerShellPropertyName = options.StiffenerShellPropertyName ?? "",
            GammaM0 = PositiveOrDefault(options.GammaM0, 1.0)
        };
        model.WebGroupName = EtabsNameUtility.BuildSafeName("", $"{model.GroupName}_WEB");
        model.FlangeGroupName = EtabsNameUtility.BuildSafeName("", $"{model.GroupName}_FLANGE");
        model.StiffenerGroupName = EtabsNameUtility.BuildSafeName("", $"{model.GroupName}_STIFFENER");

        List<OpeningBounds> openings = ResolveOpenings(model, options.Openings);
        var nodesByKey = new Dictionary<string, PlateGirderNode>(StringComparer.OrdinalIgnoreCase);
        int panelIndex = 1;

        AddWebMesh(model, nodesByKey, ref panelIndex, openings);
        if (model.GenerateTopFlange)
            AddFlangeMesh(model, nodesByKey, ref panelIndex, model.OriginZ + depth, PlateGirderShellGroup.TopFlange);
        if (model.GenerateBottomFlange)
            AddFlangeMesh(model, nodesByKey, ref panelIndex, model.OriginZ, PlateGirderShellGroup.BottomFlange);
        AddOpeningStiffeners(model, nodesByKey, ref panelIndex, openings);

        if (openings.Count > 0)
            model.Warnings.Add($"{openings.Count} web opening(s) generated by omitting quad web shell cells inside each opening boundary.");
        if (openings.Any(opening => opening.Definition.Strengthen))
            model.Warnings.Add("Opening stiffeners are generated as transverse shell plates normal to the web.");

        return model;
    }

    private static List<OpeningBounds> ResolveOpenings(ParametricPlateGirderModel model, IReadOnlyCollection<PlateGirderOpening> optionOpenings)
    {
        if (!model.HasWebOpening)
            return [];

        List<PlateGirderOpening> requestedOpenings = optionOpenings.Count > 0
            ? optionOpenings.Select(opening => opening.Clone()).ToList()
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

        double edgeMarginX = Math.Min(Math.Max(model.Length * 0.01, Tolerance), model.Length * 0.2);
        double edgeMarginZ = Math.Min(Math.Max(model.Depth * 0.01, Tolerance), model.Depth * 0.2);
        double maxWidth = Math.Max(Tolerance, model.Length - 2.0 * edgeMarginX);
        double maxHeight = Math.Max(Tolerance, model.Depth - 2.0 * edgeMarginZ);
        var bounds = new List<OpeningBounds>();
        int openingIndex = 1;

        foreach (PlateGirderOpening requestedOpening in requestedOpenings)
        {
            string openingId = EtabsNameUtility.BuildSafeName("", requestedOpening.Id, 16);
            if (openingId.Length == 0)
                openingId = $"OP{openingIndex:00}";

            double width = Math.Clamp(PositiveOrDefault(requestedOpening.Width, model.Length * 0.15), Tolerance, maxWidth);
            double height = Math.Clamp(PositiveOrDefault(requestedOpening.Height, model.Depth * 0.3), Tolerance, maxHeight);
            double centerX = Math.Clamp(FiniteOrDefault(requestedOpening.CenterX, model.Length / 2.0), width / 2.0 + edgeMarginX, model.Length - width / 2.0 - edgeMarginX);
            double centerZ = Math.Clamp(FiniteOrDefault(requestedOpening.CenterZ, model.Depth / 2.0), height / 2.0 + edgeMarginZ, model.Depth - height / 2.0 - edgeMarginZ);
            var adjustedOpening = new PlateGirderOpening
            {
                Id = openingId,
                CenterX = centerX,
                CenterZ = centerZ,
                Width = width,
                Height = height,
                Strengthen = requestedOpening.Strengthen,
                StrengthenTop = requestedOpening.StrengthenTop,
                StrengthenBottom = requestedOpening.StrengthenBottom,
                StrengthenLeft = requestedOpening.StrengthenLeft,
                StrengthenRight = requestedOpening.StrengthenRight,
                StiffenerOutstand = PositiveOrDefault(requestedOpening.StiffenerOutstand, model.OpeningStiffenerWidth),
                StiffenerExtension = Math.Max(0.0, FiniteOrDefault(requestedOpening.StiffenerExtension, 0.0))
            };

            if (Math.Abs(width - requestedOpening.Width) > 0.001 ||
                Math.Abs(height - requestedOpening.Height) > 0.001 ||
                Math.Abs(centerX - requestedOpening.CenterX) > 0.001 ||
                Math.Abs(centerZ - requestedOpening.CenterZ) > 0.001)
            {
                model.Warnings.Add($"Opening '{openingId}' geometry was adjusted so it remains fully inside the web plate.");
            }

            double left = model.OriginX + centerX - width / 2.0;
            double right = model.OriginX + centerX + width / 2.0;
            double bottom = model.OriginZ + centerZ - height / 2.0;
            double top = model.OriginZ + centerZ + height / 2.0;
            bounds.Add(new OpeningBounds(openingId, left, right, bottom, top, adjustedOpening));
            model.Openings.Add(adjustedOpening);
            openingIndex++;
        }

        if (model.Openings.Count > 0)
        {
            PlateGirderOpening firstOpening = model.Openings[0];
            model.OpeningCenterX = firstOpening.CenterX;
            model.OpeningCenterZ = firstOpening.CenterZ;
            model.OpeningWidth = firstOpening.Width;
            model.OpeningHeight = firstOpening.Height;
            model.StrengthenOpening = firstOpening.Strengthen;
            model.StrengthenOpeningTop = firstOpening.StrengthenTop;
            model.StrengthenOpeningBottom = firstOpening.StrengthenBottom;
            model.StrengthenOpeningLeft = firstOpening.StrengthenLeft;
            model.StrengthenOpeningRight = firstOpening.StrengthenRight;
            model.OpeningStiffenerWidth = firstOpening.StiffenerOutstand;
            model.OpeningStiffenerExtension = firstOpening.StiffenerExtension;
        }

        foreach (IGrouping<string, OpeningBounds> duplicate in bounds
            .GroupBy(opening => opening.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            model.Warnings.Add($"Duplicate opening ID '{duplicate.Key}' was found. ETABS shell IDs remain unique, but opening labels should be unique for review.");
        }

        for (int i = 0; i < bounds.Count; i++)
        {
            for (int j = i + 1; j < bounds.Count; j++)
            {
                if (bounds[i].Intersects(bounds[j]))
                    model.Warnings.Add($"Openings '{bounds[i].Id}' and '{bounds[j].Id}' overlap in the web elevation.");
            }
        }

        return bounds;
    }

    private static void AddWebMesh(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        IReadOnlyList<OpeningBounds> openings)
    {
        var xExtras = new List<double>();
        var zExtras = new List<double>();
        AddOpeningAndStiffenerLines(model, openings, xExtras, zExtras);

        List<double> xLines = BuildLines(model.OriginX, model.OriginX + model.Length, model.LengthDivisions, xExtras);
        List<double> zLines = BuildLines(model.OriginZ, model.OriginZ + model.Depth, model.DepthDivisions, zExtras);

        for (int ix = 0; ix < xLines.Count - 1; ix++)
        {
            for (int iz = 0; iz < zLines.Count - 1; iz++)
            {
                double centerX = (xLines[ix] + xLines[ix + 1]) / 2.0;
                double centerZ = (zLines[iz] + zLines[iz + 1]) / 2.0;
                if (openings.Any(opening => opening.Contains(centerX, centerZ)))
                    continue;

                AddXzPanel(
                    model,
                    nodesByKey,
                    ref panelIndex,
                    PlateGirderShellGroup.Web,
                    xLines[ix],
                    xLines[ix + 1],
                    model.OriginY,
                    zLines[iz],
                    zLines[iz + 1]);
            }
        }
    }

    private static void AddFlangeMesh(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        double z,
        PlateGirderShellGroup group)
    {
        List<double> xLines = BuildLines(model.OriginX, model.OriginX + model.Length, model.LengthDivisions, []);
        List<double> yLines = BuildLines(
            model.OriginY - model.FlangeWidth / 2.0,
            model.OriginY + model.FlangeWidth / 2.0,
            model.FlangeWidthDivisions,
            [model.OriginY]);

        for (int ix = 0; ix < xLines.Count - 1; ix++)
        {
            for (int iy = 0; iy < yLines.Count - 1; iy++)
            {
                AddXyPanel(
                    model,
                    nodesByKey,
                    ref panelIndex,
                    group,
                    xLines[ix],
                    xLines[ix + 1],
                    yLines[iy],
                    yLines[iy + 1],
                    z);
            }
        }
    }

    private static void AddOpeningStiffeners(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        IReadOnlyList<OpeningBounds> openings)
    {
        foreach (OpeningBounds opening in openings.Where(opening => opening.Definition.Strengthen))
        {
            double stiffenerOutstand = Math.Max(Tolerance, opening.Definition.StiffenerOutstand);
            double extension = Math.Max(0.0, opening.Definition.StiffenerExtension);
            double y1 = model.OriginY - stiffenerOutstand / 2.0;
            double y2 = model.OriginY + stiffenerOutstand / 2.0;
            double webLeft = model.OriginX;
            double webRight = model.OriginX + model.Length;
            double webBottom = model.OriginZ;
            double webTop = model.OriginZ + model.Depth;
            double horizontalLeft = Math.Max(webLeft, opening.Left - extension);
            double horizontalRight = Math.Min(webRight, opening.Right + extension);
            double verticalBottom = Math.Max(webBottom, opening.Bottom - extension);
            double verticalTop = Math.Min(webTop, opening.Top + extension);

            if (opening.Definition.StrengthenTop)
                AddXyRectMesh(model, nodesByKey, ref panelIndex, PlateGirderShellGroup.OpeningTopStiffener, horizontalLeft, horizontalRight, y1, y2, opening.Top);

            if (opening.Definition.StrengthenBottom)
                AddXyRectMesh(model, nodesByKey, ref panelIndex, PlateGirderShellGroup.OpeningBottomStiffener, horizontalLeft, horizontalRight, y1, y2, opening.Bottom);

            if (opening.Definition.StrengthenLeft)
                AddYzRectMesh(model, nodesByKey, ref panelIndex, PlateGirderShellGroup.OpeningLeftStiffener, opening.Left, y1, y2, verticalBottom, verticalTop);

            if (opening.Definition.StrengthenRight)
                AddYzRectMesh(model, nodesByKey, ref panelIndex, PlateGirderShellGroup.OpeningRightStiffener, opening.Right, y1, y2, verticalBottom, verticalTop);
        }
    }

    private static void AddOpeningAndStiffenerLines(
        ParametricPlateGirderModel model,
        IReadOnlyList<OpeningBounds> openings,
        List<double> xExtras,
        List<double> zExtras)
    {
        foreach (OpeningBounds opening in openings)
        {
            xExtras.AddRange([opening.Left, opening.Right]);
            zExtras.AddRange([opening.Bottom, opening.Top]);

            if (opening.Definition.Strengthen)
            {
                double extension = Math.Max(0.0, opening.Definition.StiffenerExtension);
                if (extension > Tolerance)
                {
                    double horizontalLeft = Math.Max(model.OriginX, opening.Left - extension);
                    double horizontalRight = Math.Min(model.OriginX + model.Length, opening.Right + extension);
                    double verticalBottom = Math.Max(model.OriginZ, opening.Bottom - extension);
                    double verticalTop = Math.Min(model.OriginZ + model.Depth, opening.Top + extension);
                    xExtras.AddRange([horizontalLeft, horizontalRight]);
                    zExtras.AddRange([verticalBottom, verticalTop]);
                }
            }
        }
    }

    private static void AddXzRectMesh(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        PlateGirderShellGroup group,
        double x1,
        double x2,
        double y,
        double z1,
        double z2)
    {
        if (x2 - x1 <= Tolerance || z2 - z1 <= Tolerance)
            return;

        int xDivisions = Math.Clamp((int)Math.Ceiling((x2 - x1) / Math.Max(model.Length / Math.Max(model.LengthDivisions, 1), Tolerance)), 1, 80);
        int zDivisions = Math.Clamp((int)Math.Ceiling((z2 - z1) / Math.Max(model.Depth / Math.Max(model.DepthDivisions, 1), Tolerance)), 1, 80);
        List<double> xLines = BuildLines(x1, x2, xDivisions, []);
        List<double> zLines = BuildLines(z1, z2, zDivisions, []);

        for (int ix = 0; ix < xLines.Count - 1; ix++)
        {
            for (int iz = 0; iz < zLines.Count - 1; iz++)
                AddXzPanel(model, nodesByKey, ref panelIndex, group, xLines[ix], xLines[ix + 1], y, zLines[iz], zLines[iz + 1]);
        }
    }

    private static void AddXyRectMesh(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        PlateGirderShellGroup group,
        double x1,
        double x2,
        double y1,
        double y2,
        double z)
    {
        if (x2 - x1 <= Tolerance || y2 - y1 <= Tolerance)
            return;

        int xDivisions = Math.Clamp((int)Math.Ceiling((x2 - x1) / Math.Max(model.Length / Math.Max(model.LengthDivisions, 1), Tolerance)), 1, 80);
        int yDivisions = Math.Clamp(model.FlangeWidthDivisions, 1, 20);
        List<double> xLines = BuildLines(x1, x2, xDivisions, []);
        List<double> yLines = BuildLines(y1, y2, yDivisions, [model.OriginY]);

        for (int ix = 0; ix < xLines.Count - 1; ix++)
        {
            for (int iy = 0; iy < yLines.Count - 1; iy++)
                AddXyPanel(model, nodesByKey, ref panelIndex, group, xLines[ix], xLines[ix + 1], yLines[iy], yLines[iy + 1], z);
        }
    }

    private static void AddYzRectMesh(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        PlateGirderShellGroup group,
        double x,
        double y1,
        double y2,
        double z1,
        double z2)
    {
        if (y2 - y1 <= Tolerance || z2 - z1 <= Tolerance)
            return;

        int yDivisions = Math.Clamp(model.FlangeWidthDivisions, 1, 20);
        int zDivisions = Math.Clamp((int)Math.Ceiling((z2 - z1) / Math.Max(model.Depth / Math.Max(model.DepthDivisions, 1), Tolerance)), 1, 80);
        List<double> yLines = BuildLines(y1, y2, yDivisions, [model.OriginY]);
        List<double> zLines = BuildLines(z1, z2, zDivisions, []);

        for (int iy = 0; iy < yLines.Count - 1; iy++)
        {
            for (int iz = 0; iz < zLines.Count - 1; iz++)
                AddYzPanel(model, nodesByKey, ref panelIndex, group, x, yLines[iy], yLines[iy + 1], zLines[iz], zLines[iz + 1]);
        }
    }

    private static void AddXzPanel(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        PlateGirderShellGroup group,
        double x1,
        double x2,
        double y,
        double z1,
        double z2)
    {
        PlateGirderNode n00 = AddNode(model, nodesByKey, x1, y, z1);
        PlateGirderNode n10 = AddNode(model, nodesByKey, x2, y, z1);
        PlateGirderNode n11 = AddNode(model, nodesByKey, x2, y, z2);
        PlateGirderNode n01 = AddNode(model, nodesByKey, x1, y, z2);
        AddPanel(model, ref panelIndex, group, [n00.Id, n10.Id, n11.Id, n01.Id]);
    }

    private static void AddXyPanel(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        PlateGirderShellGroup group,
        double x1,
        double x2,
        double y1,
        double y2,
        double z)
    {
        PlateGirderNode n00 = AddNode(model, nodesByKey, x1, y1, z);
        PlateGirderNode n10 = AddNode(model, nodesByKey, x2, y1, z);
        PlateGirderNode n11 = AddNode(model, nodesByKey, x2, y2, z);
        PlateGirderNode n01 = AddNode(model, nodesByKey, x1, y2, z);
        AddPanel(model, ref panelIndex, group, [n00.Id, n10.Id, n11.Id, n01.Id]);
    }

    private static void AddYzPanel(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        ref int panelIndex,
        PlateGirderShellGroup group,
        double x,
        double y1,
        double y2,
        double z1,
        double z2)
    {
        PlateGirderNode n00 = AddNode(model, nodesByKey, x, y1, z1);
        PlateGirderNode n10 = AddNode(model, nodesByKey, x, y2, z1);
        PlateGirderNode n11 = AddNode(model, nodesByKey, x, y2, z2);
        PlateGirderNode n01 = AddNode(model, nodesByKey, x, y1, z2);
        AddPanel(model, ref panelIndex, group, [n00.Id, n10.Id, n11.Id, n01.Id]);
    }

    private static PlateGirderNode AddNode(
        ParametricPlateGirderModel model,
        Dictionary<string, PlateGirderNode> nodesByKey,
        double x,
        double y,
        double z)
    {
        string key = CoordinateKey(x, y, z);
        if (nodesByKey.TryGetValue(key, out PlateGirderNode? existing))
            return existing;

        var node = new PlateGirderNode
        {
            Id = $"N_{model.Nodes.Count + 1:00000}",
            X = x,
            Y = y,
            Z = z
        };
        nodesByKey[key] = node;
        model.Nodes.Add(node);
        return node;
    }

    private static void AddPanel(
        ParametricPlateGirderModel model,
        ref int panelIndex,
        PlateGirderShellGroup group,
        List<string> nodeIds)
    {
        model.ShellPanels.Add(new PlateGirderShellPanel
        {
            Id = $"SH_{panelIndex:00000}",
            NodeIds = nodeIds,
            Group = group,
            ShellPropertyName = ResolveShellProperty(model, group)
        });
        panelIndex++;
    }

    private static string ResolveShellProperty(ParametricPlateGirderModel model, PlateGirderShellGroup group)
    {
        return group switch
        {
            PlateGirderShellGroup.Web => model.WebShellPropertyName,
            PlateGirderShellGroup.TopFlange or PlateGirderShellGroup.BottomFlange => model.FlangeShellPropertyName,
            _ => model.StiffenerShellPropertyName
        };
    }

    private static List<double> BuildLines(double start, double end, int divisions, IEnumerable<double> extraLines)
    {
        var values = new List<double>();
        int count = Math.Max(1, divisions);
        for (int index = 0; index <= count; index++)
            values.Add(start + (end - start) * index / count);

        values.AddRange(extraLines.Where(value => double.IsFinite(value)).Select(value => Math.Clamp(value, start, end)));
        values.Sort();

        var result = new List<double>();
        foreach (double value in values)
        {
            if (result.Count == 0 || Math.Abs(value - result[^1]) > Tolerance)
                result.Add(value);
        }

        return result;
    }

    private static string CoordinateKey(double x, double y, double z)
    {
        return $"{Math.Round(x, 6):0.######}|{Math.Round(y, 6):0.######}|{Math.Round(z, 6):0.######}";
    }

    private static double PositiveOrDefault(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    private static double FiniteOrDefault(double value, double fallback)
    {
        return double.IsFinite(value) ? value : fallback;
    }

    private sealed record OpeningBounds(string Id, double Left, double Right, double Bottom, double Top, PlateGirderOpening Definition)
    {
        public bool Contains(double x, double z)
        {
            return x > Left + Tolerance &&
                x < Right - Tolerance &&
                z > Bottom + Tolerance &&
                z < Top - Tolerance;
        }

        public bool Intersects(OpeningBounds other)
        {
            return Left < other.Right - Tolerance &&
                Right > other.Left + Tolerance &&
                Bottom < other.Top - Tolerance &&
                Top > other.Bottom + Tolerance;
        }
    }
}
