using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Capturius.Helpers;
using Capturius.Models;
using Capturius.Services;

namespace Capturius.Tools;

public sealed class NumberTool : ITool
{
    private readonly ISettingsStore _settings;
    private readonly StackPanel     _panel;

    private Color  _fillColor       = Color.FromRgb(0xE0, 0x31, 0x31);
    private Color  _borderColor     = Colors.White;
    private Color  _fontColor       = Colors.White;
    private double _borderThickness = 3;
    private double _opacity         = 100;
    private double _shadow          = 5;
    private int    _size            = 30;
    private int    _next            = 1;

    private ItemsControl? _fillPicker;
    private ItemsControl? _borderPicker;
    private ItemsControl? _fontPicker;
    private TextBox?      _counterBox;
    private TextBox?      _sizeBox;

    public event Action? PropertiesChanged;

    public string    Name            => "Number";
    public UIElement PropertiesPanel => _panel;

    public NumberTool(ISettingsStore settings)
    {
        _settings = settings;
        LoadSettings();
        _panel = BuildPanel();
    }

    public Annotation? CommitAt(Point click)
    {
        double r = _size / 2.0;
        var ann = new NumberAnnotation
        {
            Tool            = this,
            Number          = _next,
            Size            = _size,
            FillColor       = _fillColor,
            BorderColor     = _borderColor,
            FontColor       = _fontColor,
            BorderThickness = _borderThickness,
            Opacity         = _opacity,
            Shadow          = _shadow,
            Start           = new Point(click.X - r, click.Y - r),
            End             = new Point(click.X + r, click.Y + r),
            Element         = null!,
        };
        ann.Element = RenderCircle(ann);
        _next = Math.Min(_next + 1, 99);
        if (_counterBox != null) _counterBox.Text = _next.ToString();
        SaveSettings();
        return ann;
    }

    public UIElement? Preview(Point start, Point current) => null;
    public Annotation? Commit(Point start, Point end)     => null;

    public UIElement Render(Annotation ann) => RenderCircle((NumberAnnotation)ann);

    public void SyncFrom(Annotation ann)
    {
        var n            = (NumberAnnotation)ann;
        _size            = (int)n.Size;
        _fillColor       = n.FillColor;
        _borderColor     = n.BorderColor;
        _fontColor       = n.FontColor;
        _borderThickness = n.BorderThickness;
        _opacity         = n.Opacity;
        _shadow          = n.Shadow;

        if (_fillPicker   != null) ShapeHelper.UpdatePickerSelection(_fillPicker,   _fillColor);
        if (_borderPicker != null) ShapeHelper.UpdatePickerSelection(_borderPicker, _borderColor);
        if (_fontPicker   != null) ShapeHelper.UpdatePickerSelection(_fontPicker,   _fontColor);
        SelectRadio("NumberBorderThickness", (int)_borderThickness);
        SelectRadio("NumberOpacity",         (int)_opacity);
        SelectRadio("NumberShadow",          (int)_shadow);
        if (_sizeBox != null) _sizeBox.Text = _size.ToString();
    }

    public void ApplyTo(Annotation ann)
    {
        var n      = (NumberAnnotation)ann;
        double cx  = (n.Start.X + n.End.X) / 2;
        double cy  = (n.Start.Y + n.End.Y) / 2;
        double r   = _size / 2.0;
        n.Start           = new Point(cx - r, cy - r);
        n.End             = new Point(cx + r, cy + r);
        n.Size            = _size;
        n.FillColor       = _fillColor;
        n.BorderColor     = _borderColor;
        n.FontColor       = _fontColor;
        n.BorderThickness = _borderThickness;
        n.Opacity         = _opacity;
        n.Shadow          = _shadow;
    }

    public void LoadSettings()
    {
        _fillColor       = ShapeHelper.ParseColor(_settings.GetString("NumberFillColor"),   _fillColor);
        _borderColor     = ShapeHelper.ParseColor(_settings.GetString("NumberBorderColor"), _borderColor);
        _fontColor       = ShapeHelper.ParseColor(_settings.GetString("NumberFontColor"),   _fontColor);
        _borderThickness = _settings.GetDouble("NumberBorderThickness") ?? _borderThickness;
        _opacity         = _settings.GetDouble("NumberOpacity")         ?? _opacity;
        _shadow          = _settings.GetDouble("NumberShadow")          ?? _shadow;
        _size            = (int?)_settings.GetDouble("NumberSize")      ?? _size;
        _size            = Math.Clamp(_size, 20, 60);
    }

    public void SaveSettings()
    {
        _settings.Set("NumberFillColor",       ShapeHelper.ColorToString(_fillColor));
        _settings.Set("NumberBorderColor",     ShapeHelper.ColorToString(_borderColor));
        _settings.Set("NumberFontColor",       ShapeHelper.ColorToString(_fontColor));
        _settings.Set("NumberBorderThickness", _borderThickness);
        _settings.Set("NumberOpacity",         _opacity);
        _settings.Set("NumberShadow",          _shadow);
        _settings.Set("NumberSize",            (double)_size);
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

        panel.Children.Add(UiHelper.MakeLabel("Font:", 10));
        _fontPicker = UiHelper.MakePicker();
        ShapeHelper.BuildColorPickerItems(_fontPicker, ShapeHelper.ColorPresets, () => _fontColor, c =>
        {
            _fontColor = c;
            ShapeHelper.UpdatePickerSelection(_fontPicker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_fontPicker);

        panel.Children.Add(UiHelper.MakeLabel("Thickness:", 10));
        foreach (var v in new[] { 0, 1, 2, 3, 5 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "NumberBorderThickness", val == (int)_borderThickness, () =>
            {
                _borderThickness = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Opacity:", 20));
        foreach (var v in new[] { 25, 50, 75, 100 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "NumberOpacity", val == (int)_opacity, () =>
            {
                _opacity = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Shadow:", 20));
        foreach (var v in new[] { 0, 1, 3, 5, 7, 10, 15 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "NumberShadow", val == (int)_shadow, () =>
            {
                _shadow = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Size:", 20));

        var btnSizeMinus = MakeSpinButton("−");
        _sizeBox = new TextBox
        {
            Text                     = _size.ToString(),
            Width                    = 32,
            TextAlignment            = TextAlignment.Center,
            Foreground               = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
            Background               = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            BorderBrush              = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            BorderThickness          = new Thickness(1),
            FontSize                 = 12,
            FontFamily               = new FontFamily("Segoe UI"),
            VerticalAlignment        = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding                  = new Thickness(2),
        };
        _sizeBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _sizeBox.LostFocus        += (_, _) => CommitSizeBox();
        _sizeBox.KeyDown          += (_, e) => { if (e.Key == Key.Enter) CommitSizeBox(); };

        var btnSizePlus = MakeSpinButton("+");
        btnSizeMinus.Click += (_, _) => { _size = Math.Max(20, _size - 1); _sizeBox.Text = _size.ToString(); SaveSettings(); PropertiesChanged?.Invoke(); };
        btnSizePlus.Click  += (_, _) => { _size = Math.Min(60, _size + 1); _sizeBox.Text = _size.ToString(); SaveSettings(); PropertiesChanged?.Invoke(); };

        panel.Children.Add(btnSizeMinus);
        panel.Children.Add(_sizeBox);
        panel.Children.Add(btnSizePlus);

        panel.Children.Add(UiHelper.MakeLabel("Next #:", 20));

        var btnMinus = MakeSpinButton("−");
        _counterBox = new TextBox
        {
            Text                     = _next.ToString(),
            Width                    = 32,
            TextAlignment            = TextAlignment.Center,
            Foreground               = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
            Background               = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            BorderBrush              = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            BorderThickness          = new Thickness(1),
            FontSize                 = 12,
            FontFamily               = new FontFamily("Segoe UI"),
            VerticalAlignment        = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding                  = new Thickness(2),
        };
        _counterBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _counterBox.LostFocus        += (_, _) => CommitCounterBox();
        _counterBox.KeyDown          += (_, e) => { if (e.Key == Key.Enter) CommitCounterBox(); };

        var btnPlus = MakeSpinButton("+");
        btnMinus.Click += (_, _) => { _next = Math.Max(1,  _next - 1); _counterBox.Text = _next.ToString(); SaveSettings(); };
        btnPlus.Click  += (_, _) => { _next = Math.Min(99, _next + 1); _counterBox.Text = _next.ToString(); SaveSettings(); };

        panel.Children.Add(btnMinus);
        panel.Children.Add(_counterBox);
        panel.Children.Add(btnPlus);

        return panel;
    }

    private void CommitSizeBox()
    {
        if (_sizeBox == null) return;
        if (int.TryParse(_sizeBox.Text, out int v))
            _size = Math.Clamp(v, 20, 60);
        _sizeBox.Text = _size.ToString();
        SaveSettings();
        PropertiesChanged?.Invoke();
    }

    private void CommitCounterBox()
    {
        if (_counterBox == null) return;
        if (int.TryParse(_counterBox.Text, out int v))
            _next = Math.Clamp(v, 1, 99);
        _counterBox.Text = _next.ToString();
        SaveSettings();
    }

    private void SelectRadio(string group, int value)
    {
        string tag = value.ToString();
        foreach (UIElement child in _panel.Children)
            if (child is System.Windows.Controls.RadioButton rb && rb.GroupName == group)
                rb.IsChecked = rb.Tag?.ToString() == tag;
    }

    private static Button MakeSpinButton(string content) => new Button
    {
        Content           = content,
        Width             = 22,
        Foreground        = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
        Background        = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
        BorderBrush       = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
        BorderThickness   = new Thickness(1),
        Padding           = new Thickness(0),
        FontSize          = 14,
        FontFamily        = new FontFamily("Segoe UI"),
        Cursor            = Cursors.Hand,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ── Render ────────────────────────────────────────────────────────────────

    private static UIElement RenderCircle(NumberAnnotation ann)
    {
        var r    = ShapeHelper.NormRect(ann.Start, ann.End);
        double w = Math.Max(r.Width,  1);
        double h = Math.Max(r.Height, 1);

        var ellipse = new Ellipse
        {
            Width           = w,
            Height          = h,
            Fill            = new SolidColorBrush(ann.FillColor),
            Stroke          = new SolidColorBrush(ann.BorderColor),
            StrokeThickness = ann.BorderThickness,
        };

        var text = new TextBlock
        {
            Text                = ann.Number.ToString(),
            Foreground          = new SolidColorBrush(ann.FontColor),
            FontSize            = Math.Max(w * 0.48, 8),
            FontWeight          = FontWeights.Bold,
            FontFamily          = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 1),
        };

        var grid = new Grid { Width = w, Height = h };
        grid.Children.Add(ellipse);
        grid.Children.Add(text);

        // Fully opaque or no shadow: DropShadowEffect is safe
        if (ann.Shadow <= 0 || ann.Opacity >= 100)
        {
            grid.Opacity = ann.Opacity / 100.0;
            if (ann.Shadow > 0) grid.Effect = ShapeHelper.MakeShadow(ann.Shadow);
            Canvas.SetLeft(grid, r.X);
            Canvas.SetTop(grid,  r.Y);
            return grid;
        }

        // Semi-transparent + shadow: outer-glow bitmap prevents shadow bleeding through fill.
        // Rendered at 2× DPI for smooth ellipse edges.
        const double renderScale = 4.0;
        const double renderDpi   = 96 * renderScale;
        int pad = (int)(ann.Shadow * 2 + 4);
        int bw  = (int)Math.Ceiling(w) + pad * 2;
        int bh  = (int)Math.Ceiling(h) + pad * 2;
        int pbw = (int)(bw * renderScale);
        int pbh = (int)(bh * renderScale);

        var shapeRtb = new RenderTargetBitmap(pbw, pbh, renderDpi, renderDpi, PixelFormats.Pbgra32);
        var shapeVis = new DrawingVisual();
        using (var dc = shapeVis.RenderOpen())
            dc.DrawEllipse(Brushes.White, null, new Point(pad + w / 2, pad + h / 2), w / 2, h / 2);
        shapeRtb.Render(shapeVis);

        var blurRtb = new RenderTargetBitmap(pbw, pbh, renderDpi, renderDpi, PixelFormats.Pbgra32);
        var blurImg = new Image
        {
            Source = shapeRtb, Width = bw, Height = bh,
            Effect = new BlurEffect { Radius = ann.Shadow, KernelType = KernelType.Gaussian },
        };
        blurImg.Measure(new Size(bw, bh));
        blurImg.Arrange(new Rect(0, 0, bw, bh));
        blurRtb.Render(blurImg);

        int stride  = pbw * 4;
        var blurPx  = new byte[pbh * stride];
        var shapePx = new byte[pbh * stride];
        blurRtb.CopyPixels(blurPx, stride, 0);
        shapeRtb.CopyPixels(shapePx, stride, 0);

        for (int i = 0; i < blurPx.Length; i += 4)
        {
            byte shapeA = shapePx[i + 3];
            byte blurA  = blurPx[i + 3];
            blurPx[i]     = 0;
            blurPx[i + 1] = 0;
            blurPx[i + 2] = 0;
            blurPx[i + 3] = shapeA > 0 ? (byte)0 : (byte)(blurA / 2);
        }

        var glowBitmap = BitmapSource.Create(pbw, pbh, renderDpi, renderDpi, PixelFormats.Pbgra32, null, blurPx, stride);
        var glowImage  = new Image { Source = glowBitmap, Width = bw, Height = bh, Stretch = Stretch.Fill };

        grid.Opacity = ann.Opacity / 100.0;

        var container = new Canvas
        {
            Width        = w,
            Height       = h,
            ClipToBounds = false,
            Background   = Brushes.Transparent,
        };
        Canvas.SetLeft(glowImage, -pad);
        Canvas.SetTop(glowImage,  -pad);
        container.Children.Add(glowImage);
        Canvas.SetLeft(grid, 0);
        Canvas.SetTop(grid,  0);
        container.Children.Add(grid);

        Canvas.SetLeft(container, r.X);
        Canvas.SetTop(container,  r.Y);
        return container;
    }
}
