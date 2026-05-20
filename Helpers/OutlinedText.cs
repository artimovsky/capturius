using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Capturius.Helpers;

public sealed class OutlinedText : FrameworkElement
{
    public string        Text           { get; set; } = "";
    public string        FontFamilyName { get; set; } = "Segoe UI";
    public double        FontSizeValue  { get; set; } = 18;
    public Brush         Fill           { get; set; } = Brushes.White;
    public Brush?        Stroke         { get; set; }
    public double        StrokeWidth    { get; set; } = 0;
    public TextAlignment TextAlign      { get; set; } = TextAlignment.Left;

    protected override Size MeasureOverride(Size available)
    {
        if (string.IsNullOrEmpty(Text)) return new Size(0, 0);
        double pad = StrokeWidth;
        double maxW = double.IsInfinity(available.Width) ? double.PositiveInfinity
                                                         : Math.Max(1, available.Width - pad * 2);
        var ft = BuildFt(maxW);
        return new Size(ft.Width + pad * 2, ft.Height + pad * 2);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Text)) return;
        double pad  = StrokeWidth;
        double maxW = ActualWidth > 0 ? Math.Max(1, ActualWidth - pad * 2) : double.PositiveInfinity;
        var ft  = BuildFt(maxW);
        var geo = ft.BuildGeometry(new Point(pad, pad));
        if (StrokeWidth > 0 && Stroke != null)
            dc.DrawGeometry(null, new Pen(Stroke, StrokeWidth * 2) { LineJoin = PenLineJoin.Round }, geo);
        dc.DrawGeometry(Fill, null, geo);
    }

    private FormattedText BuildFt(double maxWidth)
    {
        var ft = new FormattedText(
            Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(FontFamilyName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            FontSizeValue,
            Fill,
            1.0);
        ft.TextAlignment = TextAlign;
        if (maxWidth > 0 && !double.IsInfinity(maxWidth))
            ft.MaxTextWidth = maxWidth;
        return ft;
    }
}
