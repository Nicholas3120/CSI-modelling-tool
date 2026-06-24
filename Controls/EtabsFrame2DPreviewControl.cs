using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class EtabsFrame2DPreviewControl : FrameworkElement
{
    private const double PaddingSize = 28.0;
    private INotifyCollectionChanged? _observedCollection;
    private readonly List<INotifyPropertyChanged> _observedRows = [];

    public static readonly DependencyProperty FramesProperty =
        DependencyProperty.Register(
            nameof(Frames),
            typeof(IEnumerable),
            typeof(EtabsFrame2DPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnFramesChanged));

    public static readonly DependencyProperty FrameProperty =
        DependencyProperty.Register(
            nameof(Frame),
            typeof(EtabsFrameSectionRow),
            typeof(EtabsFrame2DPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

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

    private static void OnFramesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not EtabsFrame2DPreviewControl control)
            return;

        control.AttachFrameCollection(e.NewValue as IEnumerable);
        control.InvalidateVisual();
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
        InvalidateVisual();
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
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect boundsRect = new(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(Brushes.Transparent, null, boundsRect);

        List<EtabsFrameSectionRow> frames = EnumerateFrames(Frames)
            .Where(frame => double.IsFinite(frame.LengthM) && frame.LengthM > 0.000001)
            .ToList();

        EtabsFrameSectionRow? selectedFrame = Frame;
        if (frames.Count == 0 && selectedFrame != null && double.IsFinite(selectedFrame.LengthM) && selectedFrame.LengthM > 0.000001)
            frames.Add(selectedFrame);

        if (ActualWidth < 20 || ActualHeight < 20)
            return;

        if (frames.Count == 0)
        {
            DrawEmptyState(drawingContext);
            return;
        }

        Projection projection = BuildProjection(frames);
        DrawGrid(drawingContext);

        foreach (EtabsFrameSectionRow row in frames)
        {
            bool isSelected = ReferenceEquals(row, selectedFrame);
            Color color = isSelected
                ? Color.FromRgb(245, 158, 11)
                : row.Include ? Color.FromRgb(37, 99, 235) : Color.FromRgb(148, 163, 184);
            Pen pen = BuildPen(color, isSelected ? 4.0 : 2.2);
            Point start = Project(row.IX, row.IY, row.IZ, projection);
            Point end = Project(row.JX, row.JY, row.JZ, projection);
            drawingContext.DrawLine(pen, start, end);
        }

        if (selectedFrame != null && frames.Contains(selectedFrame))
        {
            Point start = Project(selectedFrame.IX, selectedFrame.IY, selectedFrame.IZ, projection);
            Point end = Project(selectedFrame.JX, selectedFrame.JY, selectedFrame.JZ, projection);
            drawingContext.DrawEllipse(BrushFrom(Color.FromRgb(14, 116, 144)), null, start, 5.0, 5.0);
            drawingContext.DrawEllipse(BrushFrom(Color.FromRgb(124, 58, 237)), null, end, 5.0, 5.0);
        }
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

    private Projection BuildProjection(IReadOnlyCollection<EtabsFrameSectionRow> frames)
    {
        (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ) bounds = GetBounds(frames);
        var axes = new[]
        {
            new AxisRange(Axis.X, bounds.MinX, bounds.MaxX),
            new AxisRange(Axis.Y, bounds.MinY, bounds.MaxY),
            new AxisRange(Axis.Z, bounds.MinZ, bounds.MaxZ)
        }
        .OrderByDescending(axis => axis.Range)
        .ToArray();

        AxisRange horizontal = axes[0];
        AxisRange vertical = axes[1];
        if (horizontal.Axis == Axis.Z)
            (horizontal, vertical) = (vertical, horizontal);

        double drawWidth = Math.Max(ActualWidth - PaddingSize * 2.0, 1.0);
        double drawHeight = Math.Max(ActualHeight - PaddingSize * 2.0, 1.0);
        double xRange = Math.Max(horizontal.Range, 0.000001);
        double yRange = Math.Max(vertical.Range, 0.000001);
        double scale = Math.Min(drawWidth / xRange, drawHeight / yRange);
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1.0;

        double usedWidth = xRange * scale;
        double usedHeight = yRange * scale;
        double offsetX = PaddingSize + (drawWidth - usedWidth) / 2.0;
        double offsetY = PaddingSize + (drawHeight - usedHeight) / 2.0;

        return new Projection(horizontal, vertical, scale, offsetX, offsetY);
    }

    private Point Project(double x, double y, double z, Projection projection)
    {
        double horizontal = ReadAxis(x, y, z, projection.Horizontal.Axis);
        double vertical = ReadAxis(x, y, z, projection.Vertical.Axis);
        double screenX = projection.OffsetX + (horizontal - projection.Horizontal.Minimum) * projection.Scale;
        double screenY = ActualHeight - projection.OffsetY - (vertical - projection.Vertical.Minimum) * projection.Scale;
        return new Point(screenX, screenY);
    }

    private static (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ) GetBounds(IEnumerable<EtabsFrameSectionRow> frames)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        foreach (EtabsFrameSectionRow frame in frames)
        {
            UpdateBounds(frame.IX, frame.IY, frame.IZ, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            UpdateBounds(frame.JX, frame.JY, frame.JZ, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        }

        return (minX, minY, minZ, maxX, maxY, maxZ);
    }

    private static void UpdateBounds(
        double x,
        double y,
        double z,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        minX = Math.Min(minX, x);
        minY = Math.Min(minY, y);
        minZ = Math.Min(minZ, z);
        maxX = Math.Max(maxX, x);
        maxY = Math.Max(maxY, y);
        maxZ = Math.Max(maxZ, z);
    }

    private void DrawGrid(DrawingContext drawingContext)
    {
        Pen gridPen = BuildPen(Color.FromRgb(226, 232, 240), 1.0);
        int columns = 6;
        int rows = 6;
        double left = PaddingSize;
        double right = Math.Max(ActualWidth - PaddingSize, left);
        double top = PaddingSize;
        double bottom = Math.Max(ActualHeight - PaddingSize, top);

        for (int column = 0; column <= columns; column++)
        {
            double x = left + (right - left) * column / columns;
            drawingContext.DrawLine(gridPen, new Point(x, top), new Point(x, bottom));
        }

        for (int row = 0; row <= rows; row++)
        {
            double y = top + (bottom - top) * row / rows;
            drawingContext.DrawLine(gridPen, new Point(left, y), new Point(right, y));
        }
    }

    private void DrawEmptyState(DrawingContext drawingContext)
    {
        FormattedText text = new(
            "No frames imported",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            BrushFrom(Color.FromRgb(100, 116, 139)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point((ActualWidth - text.Width) / 2.0, (ActualHeight - text.Height) / 2.0));
    }

    private static double ReadAxis(double x, double y, double z, Axis axis)
    {
        return axis switch
        {
            Axis.X => x,
            Axis.Y => y,
            _ => z
        };
    }

    private static Pen BuildPen(Color color, double thickness)
    {
        var pen = new Pen(BrushFrom(color), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Brush BrushFrom(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private enum Axis
    {
        X,
        Y,
        Z
    }

    private readonly record struct AxisRange(Axis Axis, double Minimum, double Maximum)
    {
        public double Range => Math.Max(Maximum - Minimum, 0.0);
    }

    private readonly record struct Projection(AxisRange Horizontal, AxisRange Vertical, double Scale, double OffsetX, double OffsetY);
}
