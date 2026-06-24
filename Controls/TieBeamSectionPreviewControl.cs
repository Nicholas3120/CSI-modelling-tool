using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class TieBeamSectionPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty SummaryProperty =
        DependencyProperty.Register(
            nameof(Summary),
            typeof(PileEccentricityTieBeamSummary),
            typeof(TieBeamSectionPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public PileEccentricityTieBeamSummary? Summary
    {
        get => (PileEccentricityTieBeamSummary?)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        drawingContext.DrawRectangle(background, null, new Rect(new Point(0, 0), RenderSize));

        if (ActualWidth < 220 || ActualHeight < 160)
            return;

        PileEccentricityTieBeamSummary? summary = Summary;
        if (summary == null)
        {
            DrawText(drawingContext, "Select a tie beam result to preview section rebar", new Point(16, 14), 12.0, Brushes.DimGray);
            return;
        }

        DrawText(drawingContext, $"Tie beam section - {summary.TieBeamId}", new Point(16, 12), 13.0, new SolidColorBrush(Color.FromRgb(15, 23, 42)), FontWeights.SemiBold);
        DrawText(drawingContext, $"MEd {summary.DesignMomentkNm:0.##} kNm {summary.MomentType}, tension face: {summary.TensionFace}", new Point(16, 34), 11.0, Brushes.DimGray);

        double textPanelWidth = Math.Min(320.0, Math.Max(245.0, ActualWidth * 0.38));
        double sectionAreaWidth = Math.Max(180.0, ActualWidth - textPanelWidth - 28.0);
        double sectionAreaHeight = Math.Max(120.0, ActualHeight - 58.0);
        double b = Safe(summary.BeamWidthMm, 600.0);
        double h = Safe(summary.BeamDepthMm, 900.0);
        double cover = Safe(summary.CoverMm, 50.0);
        double scale = Math.Min((sectionAreaWidth - 50.0) / b, (sectionAreaHeight - 30.0) / h);
        scale = Math.Max(0.05, scale);
        double sectionWidth = b * scale;
        double sectionHeight = h * scale;
        double sectionX = 22.0 + (sectionAreaWidth - sectionWidth) / 2.0;
        double sectionY = 58.0 + (sectionAreaHeight - sectionHeight) / 2.0;
        var sectionRect = new Rect(sectionX, sectionY, sectionWidth, sectionHeight);

        DrawSection(drawingContext, sectionRect, cover * scale);
        DrawBars(drawingContext, summary, sectionRect, scale);
        DrawSectionLabels(drawingContext, summary, sectionRect);
        DrawDesignNotes(drawingContext, summary, new Point(ActualWidth - textPanelWidth + 8.0, 60.0));
    }

    private static void DrawSection(DrawingContext dc, Rect sectionRect, double coverPx)
    {
        var concreteFill = new SolidColorBrush(Color.FromRgb(236, 239, 244));
        var concretePen = new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1.5);
        dc.DrawRectangle(concreteFill, concretePen, sectionRect);

        if (coverPx > 2.0 && sectionRect.Width > 2.0 * coverPx && sectionRect.Height > 2.0 * coverPx)
        {
            var coverPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.0)
            {
                DashStyle = DashStyles.Dash
            };
            dc.DrawRectangle(null, coverPen, new Rect(
                sectionRect.Left + coverPx,
                sectionRect.Top + coverPx,
                sectionRect.Width - 2.0 * coverPx,
                sectionRect.Height - 2.0 * coverPx));
        }
    }

    private static void DrawBars(
        DrawingContext dc,
        PileEccentricityTieBeamSummary summary,
        Rect sectionRect,
        double scale)
    {
        bool topTension = string.Equals(summary.TensionFace, "Top", StringComparison.OrdinalIgnoreCase);
        double tensionDiameter = Safe(summary.TensionBarDiameterMm, 20.0);
        double compressionDiameter = Safe(summary.CompressionBarDiameterMm, tensionDiameter);
        double tensionOffsetPx = Math.Max(16.0, (Safe(summary.BeamDepthMm, 900.0) - Safe(summary.EffectiveDepthMm, 800.0)) * scale);
        double compressionOffsetPx = Math.Max(16.0, Safe(summary.CompressionSteelDepthMm, 70.0) * scale);
        double topY = sectionRect.Top + tensionOffsetPx;
        double bottomY = sectionRect.Bottom - tensionOffsetPx;
        double compTopY = sectionRect.Top + compressionOffsetPx;
        double compBottomY = sectionRect.Bottom - compressionOffsetPx;
        Brush tensionBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        Brush compressionBrush = new SolidColorBrush(Color.FromRgb(14, 116, 144));
        Pen barBorder = new(Brushes.White, 1.0);

        if (topTension)
        {
            DrawBarRow(dc, sectionRect, topY, summary.SuggestedTensionBarCount, tensionDiameter, scale, tensionBrush, barBorder);
            DrawBarRow(dc, sectionRect, compBottomY, summary.SuggestedCompressionBarCount, compressionDiameter, scale, compressionBrush, barBorder);
        }
        else
        {
            DrawBarRow(dc, sectionRect, bottomY, summary.SuggestedTensionBarCount, tensionDiameter, scale, tensionBrush, barBorder);
            DrawBarRow(dc, sectionRect, compTopY, summary.SuggestedCompressionBarCount, compressionDiameter, scale, compressionBrush, barBorder);
        }
    }

    private static void DrawBarRow(
        DrawingContext dc,
        Rect sectionRect,
        double y,
        int count,
        double diameterMm,
        double scale,
        Brush fill,
        Pen border)
    {
        if (count <= 0)
            return;

        double radius = Math.Max(4.0, diameterMm * scale / 2.0);
        double sideMargin = Math.Max(18.0, radius + 9.0);
        double usableWidth = Math.Max(1.0, sectionRect.Width - 2.0 * sideMargin);

        for (int index = 0; index < count; index++)
        {
            double x = count == 1
                ? sectionRect.Left + sectionRect.Width / 2.0
                : sectionRect.Left + sideMargin + usableWidth * index / (count - 1);
            dc.DrawEllipse(fill, border, new Point(x, y), radius, radius);
        }
    }

    private static void DrawSectionLabels(DrawingContext dc, PileEccentricityTieBeamSummary summary, Rect sectionRect)
    {
        Brush labelBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        DrawText(dc, $"b {summary.BeamWidthMm:0.#} mm", new Point(sectionRect.Left + sectionRect.Width / 2.0 - 36.0, sectionRect.Bottom + 8.0), 10.5, labelBrush);
        DrawText(dc, $"h {summary.BeamDepthMm:0.#} mm", new Point(sectionRect.Right + 10.0, sectionRect.Top + sectionRect.Height / 2.0 - 8.0), 10.5, labelBrush);
    }

    private static void DrawDesignNotes(DrawingContext dc, PileEccentricityTieBeamSummary summary, Point origin)
    {
        Brush heading = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        Brush muted = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        Brush red = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        Brush blue = new SolidColorBrush(Color.FromRgb(14, 116, 144));

        DrawText(dc, "Eurocode flexure", origin, 12.0, heading, FontWeights.SemiBold);
        double y = origin.Y + 24.0;
        DrawText(dc, $"fck {summary.ConcreteStrengthNmm2:0.#}, fyk {summary.SteelYieldStrengthNmm2:0.#} N/mm2", new Point(origin.X, y), 10.5, muted);
        y += 20.0;
        DrawText(dc, $"cover {summary.CoverMm:0.#} mm, d {summary.EffectiveDepthMm:0.#} mm", new Point(origin.X, y), 10.5, muted);
        y += 20.0;
        DrawText(dc, $"z {summary.LeverArmMm:0.#} mm, K {summary.EurocodeK:0.####} / 0.167", new Point(origin.X, y), 10.5, muted);
        y += 20.0;
        DrawText(dc, $"Compression bar: {summary.CompressionBarRequired}", new Point(origin.X, y), 10.5, summary.CompressionBarRequired == "Yes" ? red : muted);
        y += 24.0;
        DrawLegendLine(dc, new Point(origin.X + 8.0, y + 7.0), red, $"Ast {summary.RequiredTensionSteelMm2:0.#} mm2, {summary.SuggestedTensionBars}");
        y += 22.0;
        DrawLegendLine(dc, new Point(origin.X + 8.0, y + 7.0), blue, $"Asc {summary.RequiredCompressionSteelMm2:0.#} mm2, {summary.SuggestedCompressionBars}");
        y += 24.0;
        DrawText(dc, summary.SectionDesignStatus, new Point(origin.X, y), 10.5, heading, FontWeights.SemiBold);
    }

    private static void DrawLegendLine(DrawingContext dc, Point point, Brush brush, string label)
    {
        dc.DrawEllipse(brush, new Pen(Brushes.White, 1.0), point, 5.0, 5.0);
        DrawText(dc, label, new Point(point.X + 12.0, point.Y - 8.0), 10.5, new SolidColorBrush(Color.FromRgb(71, 85, 105)));
    }

    private static double Safe(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0.0 ? value : fallback;
    }

    private static void DrawText(DrawingContext dc, string text, Point point, double fontSize, Brush brush, FontWeight? fontWeight = null)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, fontWeight ?? FontWeights.Normal, FontStretches.Normal),
            fontSize,
            brush,
            pixelsPerDip);
        dc.DrawText(formattedText, point);
    }
}
