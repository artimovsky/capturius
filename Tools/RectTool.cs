using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Capturius.Helpers;
using Capturius.Models;
using Capturius.Services;

namespace Capturius.Tools;

public sealed class RectTool : ITool
{
    private readonly ISettingsStore _settings;

    private Color  _fillColor       = Colors.Transparent;
    private Color  _borderColor     = Color.FromRgb(0xE0, 0x31, 0x31);
    private double _borderThickness = 2;
    private double _cornerRadius    = 0;
    private double _opacity         = 100;
    private double _shadow          = 0;

    private ItemsControl? _fillPicker;
    private ItemsControl? _borderPicker;
    private readonly StackPanel _panel;

    public event Action? PropertiesChanged;

    public RectTool(ISettingsStore settings)
    {
        _settings = settings;
        LoadSettings();
        _panel = BuildPanel();
    }

    public string    Name            => "Rect";
    public UIElement PropertiesPanel => _panel;

    public UIElement? Preview(Point start, Point current)
        => Render(start, current, _fillColor, _borderColor, _borderThickness, _cornerRadius, _opacity, _shadow);

    public Annotation? Commit(Point start, Point end) => new RectAnnotation
    {
        Tool            = this,
        Element         = Render(start, end, _fillColor, _borderColor, _borderThickness, _cornerRadius, _opacity, _shadow),
        Start           = start,
        End             = end,
        FillColor       = _fillColor,
        BorderColor     = _borderColor,
        BorderThickness = _borderThickness,
        CornerRadius    = _cornerRadius,
        Opacity         = _opacity,
        Shadow          = _shadow,
    };

    public UIElement Render(Annotation annotation)
    {
        var ann = (RectAnnotation)annotation;
        return Render(ann.Start, ann.End, ann.FillColor, ann.BorderColor, ann.BorderThickness, ann.CornerRadius, ann.Opacity, ann.Shadow);
    }

    public void SyncFrom(Annotation annotation)
    {
        var ann          = (RectAnnotation)annotation;
        _fillColor       = ann.FillColor;
        _borderColor     = ann.BorderColor;
        _borderThickness = ann.BorderThickness;
        _cornerRadius    = ann.CornerRadius;
        _opacity         = ann.Opacity;
        _shadow          = ann.Shadow;

        if (_fillPicker   != null) ShapeHelper.UpdatePickerSelection(_fillPicker,   _fillColor);
        if (_borderPicker != null) ShapeHelper.UpdatePickerSelection(_borderPicker, _borderColor);
        SelectRadio("RectBorderThickness", (int)_borderThickness);
        SelectRadio("RectCornerRadius",    (int)_cornerRadius);
        SelectRadio("RectOpacity",         (int)_opacity);
        SelectRadio("RectShadow",          (int)_shadow);
    }

    public void ApplyTo(Annotation annotation)
    {
        var ann          = (RectAnnotation)annotation;
        ann.FillColor       = _fillColor;
        ann.BorderColor     = _borderColor;
        ann.BorderThickness = _borderThickness;
        ann.CornerRadius    = _cornerRadius;
        ann.Opacity         = _opacity;
        ann.Shadow          = _shadow;
    }

    public void LoadSettings()
    {
        _fillColor       = ShapeHelper.ParseColor(_settings.GetString("RectFillColor"),   _fillColor);
        _borderColor     = ShapeHelper.ParseColor(_settings.GetString("RectBorderColor"), _borderColor);
        _borderThickness = _settings.GetDouble("RectBorderThickness") ?? _borderThickness;
        _cornerRadius    = _settings.GetDouble("RectCornerRadius")    ?? _cornerRadius;
        _opacity         = _settings.GetDouble("RectOpacity")         ?? _opacity;
        _shadow          = _settings.GetDouble("RectShadow")          ?? _shadow;
    }

    public void SaveSettings()
    {
        _settings.Set("RectFillColor",       ShapeHelper.ColorToString(_fillColor));
        _settings.Set("RectBorderColor",     ShapeHelper.ColorToString(_borderColor));
        _settings.Set("RectBorderThickness", _borderThickness);
        _settings.Set("RectCornerRadius",    _cornerRadius);
        _settings.Set("RectOpacity",         _opacity);
        _settings.Set("RectShadow",          _shadow);
    }

    // ── Panel ─────────────────────────────────────────────────────────────────

    private StackPanel BuildPanel()
    {
        var panel = UiHelper.MakePanel();

        panel.Children.Add(UiHelper.MakeLabel("Fill:"));
        _fillPicker = UiHelper.MakePicker();
        ShapeHelper.BuildFillColorPickerItems(_fillPicker, ShapeHelper.ColorPresets, () => _fillColor, c =>
        {
            _fillColor = c;
            ShapeHelper.UpdatePickerSelection(_fillPicker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_fillPicker);

        panel.Children.Add(UiHelper.MakeLabel("Border:", 10));
        _borderPicker = UiHelper.MakePicker();
        ShapeHelper.BuildColorPickerItems(_borderPicker, ShapeHelper.ColorPresets, () => _borderColor, c =>
        {
            _borderColor = c;
            ShapeHelper.UpdatePickerSelection(_borderPicker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_borderPicker);

        panel.Children.Add(UiHelper.MakeLabel("Thickness:", 10));
        foreach (var v in new[] { 0, 1, 2, 3, 5 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "RectBorderThickness", val == (int)_borderThickness, () =>
            {
                _borderThickness = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Radius:", 20));
        foreach (var v in new[] { 0, 2, 4, 8, 16 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "RectCornerRadius", val == (int)_cornerRadius, () =>
            {
                _cornerRadius = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Opacity:", 20));
        foreach (var v in new[] { 25, 50, 75, 100 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "RectOpacity", val == (int)_opacity, () =>
            {
                _opacity = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Shadow:", 20));
        foreach (var v in new[] { 0, 1, 3, 5, 7, 10, 15 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "RectShadow", val == (int)_shadow, () =>
            {
                _shadow = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        return panel;
    }

    private void SelectRadio(string group, int value)
    {
        string tag = value.ToString();
        foreach (UIElement child in _panel.Children)
            if (child is System.Windows.Controls.RadioButton rb && rb.GroupName == group)
                rb.IsChecked = rb.Tag?.ToString() == tag;
    }

    // ── Shape factory ─────────────────────────────────────────────────────────

    private static UIElement Render(Point start, Point end,
        Color fillColor, Color borderColor, double borderThickness, double cornerRadius, double opacity, double shadow)
    {
        var r    = ShapeHelper.NormRect(start, end);
        var fill = Color.FromArgb((byte)(fillColor.A * opacity / 100.0), fillColor.R, fillColor.G, fillColor.B);
        var bgBrush     = new SolidColorBrush(fill);        bgBrush.Freeze();
        var borderBrush = new SolidColorBrush(borderColor); borderBrush.Freeze();

        var visual = new Border
        {
            Width           = r.Width,
            Height          = r.Height,
            Background      = bgBrush,
            BorderBrush     = borderBrush,
            BorderThickness = new Thickness(borderThickness),
            CornerRadius    = new CornerRadius(cornerRadius),
        };

        if (shadow <= 0)
        {
            Canvas.SetLeft(visual, r.X);
            Canvas.SetTop(visual, r.Y);
            return visual;
        }

        if (fill.A == 0 || fill.A >= 255)
        {
            visual.Effect = ShapeHelper.MakeShadow(shadow);
            Canvas.SetLeft(visual, r.X);
            Canvas.SetTop(visual, r.Y);
            return visual;
        }

        // Semi-transparent fill: DropShadowEffect on the element itself bleeds through the
        // fill and darkens it. Fix: compute an outer-glow bitmap via pixel manipulation —
        // blur the element's solid shape, then erase the interior, leaving shadow only outside.
        int pad = (int)(shadow * 2 + 4);
        int bw  = (int)Math.Ceiling(r.Width)  + pad * 2;
        int bh  = (int)Math.Ceiling(r.Height) + pad * 2;

        // 1. Render solid shape (white on transparent) into RTB
        var shapeRtb = new RenderTargetBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32);
        var shapeVis = new DrawingVisual();
        using (var dc = shapeVis.RenderOpen())
            dc.DrawRoundedRectangle(Brushes.White, null,
                new Rect(pad, pad, r.Width, r.Height), cornerRadius, cornerRadius);
        shapeRtb.Render(shapeVis);

        // 2. Blur the shape
        var blurRtb = new RenderTargetBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32);
        var blurImg = new Image
        {
            Source = shapeRtb, Width = bw, Height = bh,
            Effect = new BlurEffect { Radius = shadow, KernelType = KernelType.Gaussian },
        };
        blurImg.Measure(new Size(bw, bh));
        blurImg.Arrange(new Rect(0, 0, bw, bh));
        blurRtb.Render(blurImg);

        // 3. Pixel pass: erase interior (where shape was), tint rest black at 50% opacity
        int stride  = bw * 4;
        var blurPx  = new byte[bh * stride];
        var shapePx = new byte[bh * stride];
        blurRtb.CopyPixels(blurPx, stride, 0);
        shapeRtb.CopyPixels(shapePx, stride, 0);

        for (int i = 0; i < blurPx.Length; i += 4)
        {
            byte shapeA = shapePx[i + 3];
            byte blurA  = blurPx[i + 3];
            blurPx[i]     = 0;                                          // B = black
            blurPx[i + 1] = 0;                                          // G = black
            blurPx[i + 2] = 0;                                          // R = black
            blurPx[i + 3] = shapeA > 0 ? (byte)0 : (byte)(blurA / 2); // erase interior, 50% shadow
        }

        var glowBitmap = BitmapSource.Create(bw, bh, 96, 96, PixelFormats.Pbgra32, null, blurPx, stride);
        var glowImage  = new Image { Source = glowBitmap, Width = bw, Height = bh, Stretch = Stretch.None };

        var container = new Canvas { Width = r.Width, Height = r.Height, ClipToBounds = false, Background = Brushes.Transparent };
        Canvas.SetLeft(glowImage, -pad);
        Canvas.SetTop(glowImage, -pad);
        container.Children.Add(glowImage);
        Canvas.SetLeft(visual, 0);
        Canvas.SetTop(visual, 0);
        container.Children.Add(visual);

        Canvas.SetLeft(container, r.X);
        Canvas.SetTop(container, r.Y);
        return container;
    }
}
