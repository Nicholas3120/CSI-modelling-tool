using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using TrussModelling.Models;

namespace TrussModelling.Controls;

public sealed class PlateGirder3DPreviewControl : HelixViewport3D
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(ParametricPlateGirderModel),
            typeof(PlateGirder3DPreviewControl),
            new PropertyMetadata(null, OnModelChanged));

    public ParametricPlateGirderModel? Model
    {
        get => (ParametricPlateGirderModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public PlateGirder3DPreviewControl()
    {
        Background = Brushes.Transparent;
        IsRotationEnabled = true;
        IsPanEnabled = true;
        IsZoomEnabled = true;
        IsMoveEnabled = true;
        IsHeadLightEnabled = true;
        ShowCoordinateSystem = true;
        ShowViewCube = true;
        ZoomExtentsWhenLoaded = true;
        ModelUpDirection = new Vector3D(0, 0, 1);
        Loaded += (_, _) => RebuildScene();
    }

    private static void OnModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is PlateGirder3DPreviewControl control)
            control.RebuildScene();
    }

    private void RebuildScene()
    {
        Children.Clear();
        Children.Add(new DefaultLights());

        ParametricPlateGirderModel? model = Model;
        if (model == null || model.Nodes.Count == 0)
            return;

        Dictionary<string, PlateGirderNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        AddShellGroup(model, nodes, PlateGirderShellGroup.Web, Color.FromArgb(76, 37, 99, 235));
        AddShellGroup(model, nodes, PlateGirderShellGroup.TopFlange, Color.FromArgb(92, 5, 150, 105));
        AddShellGroup(model, nodes, PlateGirderShellGroup.BottomFlange, Color.FromArgb(92, 5, 150, 105));
        AddStiffenerShells(model, nodes);
        AddOpeningEdges(model);

        (Point3D min, Point3D max) = GetBounds(model.Nodes);
        Point3D center = new((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
        double sceneSize = Math.Max((max - min).Length, 1.0);
        Camera = BuildCamera(center, sceneSize);
        DefaultCamera = Camera;
        if (IsLoaded)
            Dispatcher.BeginInvoke(() => ZoomExtents(250), DispatcherPriority.Background);
    }

    private void AddShellGroup(
        ParametricPlateGirderModel model,
        IReadOnlyDictionary<string, PlateGirderNode> nodes,
        PlateGirderShellGroup group,
        Color color)
    {
        var panels = model.ShellPanels
            .Where(panel => panel.Group == group)
            .ToList();
        if (panels.Count == 0)
            return;

        AddPanelMesh(panels, nodes, color);
    }

    private void AddStiffenerShells(ParametricPlateGirderModel model, IReadOnlyDictionary<string, PlateGirderNode> nodes)
    {
        var panels = model.ShellPanels
            .Where(panel => panel.Group is PlateGirderShellGroup.OpeningTopStiffener or
                PlateGirderShellGroup.OpeningBottomStiffener or
                PlateGirderShellGroup.OpeningLeftStiffener or
                PlateGirderShellGroup.OpeningRightStiffener)
            .ToList();
        if (panels.Count == 0)
            return;

        AddPanelMesh(panels, nodes, Color.FromArgb(132, 217, 119, 6));
    }

    private void AddPanelMesh(
        IEnumerable<PlateGirderShellPanel> panels,
        IReadOnlyDictionary<string, PlateGirderNode> nodes,
        Color color)
    {
        var mesh = new MeshGeometry3D();
        foreach (PlateGirderShellPanel panel in panels)
        {
            List<PlateGirderNode> panelNodes = panel.NodeIds
                .Where(nodes.ContainsKey)
                .Select(nodeId => nodes[nodeId])
                .ToList();
            if (panelNodes.Count != 4)
                continue;

            AddTriangle(mesh, panelNodes[0], panelNodes[1], panelNodes[2]);
            AddTriangle(mesh, panelNodes[0], panelNodes[2], panelNodes[3]);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });
    }

    private void AddOpeningEdges(ParametricPlateGirderModel model)
    {
        if (!model.HasWebOpening)
            return;

        double left = model.OriginX + model.OpeningCenterX - model.OpeningWidth / 2.0;
        double right = model.OriginX + model.OpeningCenterX + model.OpeningWidth / 2.0;
        double bottom = model.OriginZ + model.OpeningCenterZ - model.OpeningHeight / 2.0;
        double top = model.OriginZ + model.OpeningCenterZ + model.OpeningHeight / 2.0;
        double diameter = Math.Max(model.Depth * 0.006, 0.01);
        Point3D p1 = new(left, model.OriginY, bottom);
        Point3D p2 = new(right, model.OriginY, bottom);
        Point3D p3 = new(right, model.OriginY, top);
        Point3D p4 = new(left, model.OriginY, top);

        AddPipe(p1, p2, diameter);
        AddPipe(p2, p3, diameter);
        AddPipe(p3, p4, diameter);
        AddPipe(p4, p1, diameter);
    }

    private void AddPipe(Point3D start, Point3D end, double diameter)
    {
        Children.Add(new PipeVisual3D
        {
            Point1 = start,
            Point2 = end,
            Diameter = diameter,
            InnerDiameter = 0,
            ThetaDiv = 8,
            Fill = Brushes.Firebrick
        });
    }

    private static void AddTriangle(MeshGeometry3D mesh, PlateGirderNode a, PlateGirderNode b, PlateGirderNode c)
    {
        int index = mesh.Positions.Count;
        mesh.Positions.Add(ToPoint(a));
        mesh.Positions.Add(ToPoint(b));
        mesh.Positions.Add(ToPoint(c));
        mesh.TriangleIndices.Add(index);
        mesh.TriangleIndices.Add(index + 1);
        mesh.TriangleIndices.Add(index + 2);
    }

    private static (Point3D Min, Point3D Max) GetBounds(IEnumerable<PlateGirderNode> nodes)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        foreach (PlateGirderNode node in nodes)
        {
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            minZ = Math.Min(minZ, node.Z);
            maxX = Math.Max(maxX, node.X);
            maxY = Math.Max(maxY, node.Y);
            maxZ = Math.Max(maxZ, node.Z);
        }

        return (new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
    }

    private static PerspectiveCamera BuildCamera(Point3D center, double length)
    {
        double distance = Math.Max(length * 1.35, 5.0);
        Point3D position = new(center.X + distance, center.Y - distance * 0.8, center.Z + distance * 0.45);
        return new PerspectiveCamera(position, center - position, new Vector3D(0, 0, 1), 38);
    }

    private static Point3D ToPoint(PlateGirderNode node)
    {
        return new Point3D(node.X, node.Y, node.Z);
    }
}
