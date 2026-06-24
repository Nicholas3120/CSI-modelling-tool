using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using TrussModelling.Models;

namespace TrussModelling.Controls;

public sealed class EtabsFrame3DPreviewControl : HelixViewport3D
{
    private INotifyCollectionChanged? _observedCollection;
    private readonly List<INotifyPropertyChanged> _observedRows = [];

    public static readonly DependencyProperty FramesProperty =
        DependencyProperty.Register(
            nameof(Frames),
            typeof(IEnumerable),
            typeof(EtabsFrame3DPreviewControl),
            new PropertyMetadata(null, OnFramesChanged));

    public static readonly DependencyProperty FrameProperty =
        DependencyProperty.Register(
            nameof(Frame),
            typeof(EtabsFrameSectionRow),
            typeof(EtabsFrame3DPreviewControl),
            new PropertyMetadata(null, OnFrameChanged));

    public IEnumerable? Frames
    {
        get => (IEnumerable?)GetValue(FramesProperty);
        set => SetValue(FramesProperty, value);
    }

    public EtabsFrameSectionRow? Frame
    {
        get => (EtabsFrameSectionRow?)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public EtabsFrame3DPreviewControl()
    {
        Background = Brushes.Transparent;
        IsRotationEnabled = true;
        IsPanEnabled = true;
        IsZoomEnabled = true;
        IsMoveEnabled = true;
        IsInertiaEnabled = true;
        IsHeadLightEnabled = true;
        RotateAroundMouseDownPoint = true;
        ZoomAroundMouseDownPoint = true;
        ShowCoordinateSystem = true;
        ShowViewCube = true;
        ZoomExtentsWhenLoaded = true;
        ModelUpDirection = new Vector3D(0, 0, 1);
        Loaded += (_, _) => RebuildScene();
    }

    private static void OnFrameChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is EtabsFrame3DPreviewControl control)
            control.RebuildScene();
    }

    private static void OnFramesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not EtabsFrame3DPreviewControl control)
            return;

        control.AttachFrameCollection(e.NewValue as IEnumerable);
        control.RebuildScene();
    }

    private void AttachFrameCollection(IEnumerable? frames)
    {
        if (_observedCollection != null)
            _observedCollection.CollectionChanged -= OnFrameCollectionChanged;

        foreach (INotifyPropertyChanged row in _observedRows)
            row.PropertyChanged -= OnFrameRowPropertyChanged;

        _observedRows.Clear();
        _observedCollection = frames as INotifyCollectionChanged;
        if (_observedCollection != null)
            _observedCollection.CollectionChanged += OnFrameCollectionChanged;

        foreach (EtabsFrameSectionRow row in EnumerateFrames(frames))
        {
            if (row is INotifyPropertyChanged notifyingRow)
            {
                notifyingRow.PropertyChanged += OnFrameRowPropertyChanged;
                _observedRows.Add(notifyingRow);
            }
        }
    }

    private void OnFrameCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachFrameCollection(Frames);
        RebuildScene();
    }

    private void OnFrameRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EtabsFrameSectionRow.Include) or
            nameof(EtabsFrameSectionRow.LengthM) or
            nameof(EtabsFrameSectionRow.IX) or
            nameof(EtabsFrameSectionRow.IY) or
            nameof(EtabsFrameSectionRow.IZ) or
            nameof(EtabsFrameSectionRow.JX) or
            nameof(EtabsFrameSectionRow.JY) or
            nameof(EtabsFrameSectionRow.JZ))
        {
            RebuildScene();
        }
    }

    private void RebuildScene()
    {
        Children.Clear();
        Children.Add(new DefaultLights());

        List<EtabsFrameSectionRow> frames = EnumerateFrames(Frames)
            .Where(frame => double.IsFinite(frame.LengthM) && frame.LengthM > 0.000001)
            .ToList();

        EtabsFrameSectionRow? selectedFrame = Frame;
        if (frames.Count == 0 && selectedFrame != null && double.IsFinite(selectedFrame.LengthM) && selectedFrame.LengthM > 0.000001)
            frames.Add(selectedFrame);

        if (frames.Count == 0)
        {
            Camera = new PerspectiveCamera(new Point3D(4, -6, 4), new Vector3D(-4, 6, -4), new Vector3D(0, 0, 1), 40);
            DefaultCamera = Camera;
            return;
        }

        (Point3D Min, Point3D Max) bounds = GetBounds(frames);
        Point3D center = new(
            (bounds.Min.X + bounds.Max.X) / 2.0,
            (bounds.Min.Y + bounds.Max.Y) / 2.0,
            (bounds.Min.Z + bounds.Max.Z) / 2.0);
        double sceneSize = Math.Max((bounds.Max - bounds.Min).Length, frames.Max(frame => frame.LengthM));
        sceneSize = Math.Max(sceneSize, 1.0);
        double memberDiameter = Math.Max(sceneSize * 0.012, 0.04);
        double selectedDiameter = memberDiameter * 1.7;
        double nodeRadius = selectedDiameter * 1.05;

        foreach (EtabsFrameSectionRow row in frames)
        {
            bool isSelected = ReferenceEquals(row, selectedFrame);
            Color color = isSelected
                ? Color.FromRgb(245, 158, 11)
                : row.Include ? Color.FromRgb(37, 99, 235) : Color.FromRgb(148, 163, 184);

            Children.Add(new PipeVisual3D
            {
                Point1 = ToPointI(row),
                Point2 = ToPointJ(row),
                Diameter = isSelected ? selectedDiameter : memberDiameter,
                InnerDiameter = 0,
                ThetaDiv = 18,
                Fill = BuildBrush(color)
            });
        }

        if (selectedFrame != null && frames.Contains(selectedFrame))
        {
            Point3D start = ToPointI(selectedFrame);
            Point3D end = ToPointJ(selectedFrame);
            Children.Add(BuildSphere(start, nodeRadius, Color.FromRgb(14, 116, 144)));
            Children.Add(BuildSphere(end, nodeRadius, Color.FromRgb(124, 58, 237)));
            AddAxes(start, Math.Max(selectedFrame.LengthM * 0.22, sceneSize * 0.12), selectedDiameter * 0.45);
        }

        Camera = BuildCamera(center, sceneSize);
        DefaultCamera = Camera;
        if (IsLoaded)
            Dispatcher.BeginInvoke(() => ZoomExtents(250), DispatcherPriority.Background);
    }

    private static IEnumerable<EtabsFrameSectionRow> EnumerateFrames(IEnumerable? frames)
    {
        if (frames == null)
            yield break;

        foreach (object? item in frames)
        {
            if (item is EtabsFrameSectionRow row)
                yield return row;
        }
    }

    private static (Point3D Min, Point3D Max) GetBounds(IEnumerable<EtabsFrameSectionRow> frames)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        foreach (EtabsFrameSectionRow frame in frames)
        {
            foreach (Point3D point in new[] { ToPointI(frame), ToPointJ(frame) })
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }
        }

        return (new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
    }

    private static Point3D ToPointI(EtabsFrameSectionRow frame)
    {
        return new Point3D(frame.IX, frame.IY, frame.IZ);
    }

    private static Point3D ToPointJ(EtabsFrameSectionRow frame)
    {
        return new Point3D(frame.JX, frame.JY, frame.JZ);
    }

    private void AddAxes(Point3D origin, double axisLength, double axisDiameter)
    {
        Children.Add(BuildAxis(origin, new Point3D(origin.X + axisLength, origin.Y, origin.Z), axisDiameter, Color.FromRgb(220, 38, 38)));
        Children.Add(BuildAxis(origin, new Point3D(origin.X, origin.Y + axisLength, origin.Z), axisDiameter, Color.FromRgb(22, 163, 74)));
        Children.Add(BuildAxis(origin, new Point3D(origin.X, origin.Y, origin.Z + axisLength), axisDiameter, Color.FromRgb(37, 99, 235)));
    }

    private static PipeVisual3D BuildAxis(Point3D start, Point3D end, double diameter, Color color)
    {
        return new PipeVisual3D
        {
            Point1 = start,
            Point2 = end,
            Diameter = diameter,
            InnerDiameter = 0,
            ThetaDiv = 12,
            Fill = BuildBrush(color)
        };
    }

    private static SphereVisual3D BuildSphere(Point3D center, double radius, Color color)
    {
        return new SphereVisual3D
        {
            Center = center,
            Radius = radius,
            ThetaDiv = 20,
            PhiDiv = 12,
            Fill = BuildBrush(color)
        };
    }

    private static PerspectiveCamera BuildCamera(Point3D center, double length)
    {
        double distance = Math.Max(length * 2.4, 4.0);
        Point3D position = new(center.X + distance, center.Y - distance * 1.25, center.Z + distance * 0.75);
        Vector3D lookDirection = center - position;
        return new PerspectiveCamera(position, lookDirection, new Vector3D(0, 0, 1), 38);
    }

    private static Brush BuildBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
