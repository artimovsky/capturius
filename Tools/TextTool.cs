using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Capturius.Helpers;
using Capturius.Models;
using Capturius.Services;

namespace Capturius.Tools;

public sealed class TextTool : ITool
{
    private readonly ISettingsStore _settings;

    private string           _fontFamily      = "Segoe UI";
    private double           _fontSize        = 18;
    private Color            _textColor       = Color.FromRgb(0xCD, 0xD6, 0xF4);
    private double           _strokeThickness = 0;
    private Color            _strokeColor     = Colors.Black;
    private double           _shadow          = 0;
    private TextAlignment    _hAlign          = TextAlignment.Left;
    private VerticalAlignment _vAlign         = VerticalAlignment.Top;

    private static readonly string[] FontList =
    {
        "Segoe UI", "Arial", "Arial Black", "Calibri", "Cambria",
        "Comic Sans MS", "Consolas", "Courier New", "Georgia",
        "Impact", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
    };

    private ComboBox?      _fontCombo;
    private ItemsControl?  _textColorPicker;
    private ItemsControl?  _strokeColorPicker;
    private readonly StackPanel _panel;
    private bool     _syncing;
    private Action?  _pendingCommit;
    private TextBox? _editingTextBox;

    public void CommitPending() => _pendingCommit?.Invoke();

    public void UpdateEditingTextBox()
    {
        if (_editingTextBox == null) return;
        _editingTextBox.Foreground               = new SolidColorBrush(_textColor);
        _editingTextBox.CaretBrush               = new SolidColorBrush(_textColor);
        _editingTextBox.FontFamily               = new FontFamily(_fontFamily);
        _editingTextBox.FontSize                 = _fontSize;
        _editingTextBox.TextAlignment            = _hAlign;
        _editingTextBox.VerticalContentAlignment = _vAlign;
        _editingTextBox.Focus();
    }

    public event Action? PropertiesChanged;

    public TextTool(ISettingsStore settings)
    {
        _settings = settings;
        LoadSettings();
        _panel = BuildPanel();
    }

    public string    Name            => "Text";
    public UIElement PropertiesPanel => _panel;

    public UIElement?  Preview(Point start, Point current) => null;
    public Annotation? Commit(Point start, Point end)      => null;

    public UIElement Render(Annotation annotation)
    {
        var ann = (TextAnnotation)annotation;
        var r   = ShapeHelper.NormRect(ann.Start, ann.End);
        return RenderBlock(ann.Text, ann.FontFamilyName, ann.FontSize, ann.TextColor,
            ann.StrokeThickness, ann.StrokeColor, ann.Shadow, r, ann.HAlign, ann.VAlign);
    }

    public void SyncFrom(Annotation annotation)
    {
        var ann          = (TextAnnotation)annotation;
        _fontFamily      = ann.FontFamilyName;
        _fontSize        = ann.FontSize;
        _textColor       = ann.TextColor;
        _strokeThickness = ann.StrokeThickness;
        _strokeColor     = ann.StrokeColor;
        _shadow          = ann.Shadow;
        _hAlign          = ann.HAlign;
        _vAlign          = ann.VAlign;

        _syncing = true;
        try
        {
            if (_fontCombo != null)
                _fontCombo.SelectedItem = FontList.Contains(_fontFamily) ? _fontFamily : FontList[0];
            if (_textColorPicker   != null) ShapeHelper.UpdatePickerSelection(_textColorPicker,   _textColor);
            if (_strokeColorPicker != null) ShapeHelper.UpdatePickerSelection(_strokeColorPicker, _strokeColor);

            SelectRadio("TextFontSize",    (int)_fontSize);
            SelectRadio("TextStrokeThick", (int)_strokeThickness);
            SelectRadio("TextShadow",      (int)_shadow);
            SelectRadioStr("TextHAlign", HAlignTag(_hAlign));
            SelectRadioStr("TextVAlign", VAlignTag(_vAlign));
        }
        finally
        {
            _syncing = false;
        }
    }

    public void ApplyTo(Annotation annotation)
    {
        var ann             = (TextAnnotation)annotation;
        ann.FontFamilyName  = _fontFamily;
        ann.FontSize        = _fontSize;
        ann.TextColor       = _textColor;
        ann.StrokeThickness = _strokeThickness;
        ann.StrokeColor     = _strokeColor;
        ann.Shadow          = _shadow;
        ann.HAlign          = _hAlign;
        ann.VAlign          = _vAlign;
    }

    public void LoadSettings()
    {
        _fontFamily      = _settings.GetString("TextFont")         ?? _fontFamily;
        _fontSize        = _settings.GetDouble("TextFontSize")     ?? _fontSize;
        _textColor       = ShapeHelper.ParseColor(_settings.GetString("TextColor"),       _textColor);
        _strokeThickness = _settings.GetDouble("TextStrokeThick")  ?? _strokeThickness;
        _strokeColor     = ShapeHelper.ParseColor(_settings.GetString("TextStrokeColor"), _strokeColor);
        _shadow          = _settings.GetDouble("TextShadow")        ?? _shadow;
        if (Enum.TryParse<TextAlignment>(_settings.GetString("TextHAlign"), out var ha))    _hAlign = ha;
        if (Enum.TryParse<VerticalAlignment>(_settings.GetString("TextVAlign"), out var va)) _vAlign = va;
    }

    public void SaveSettings()
    {
        _settings.Set("TextFont",        _fontFamily);
        _settings.Set("TextFontSize",    _fontSize);
        _settings.Set("TextColor",       ShapeHelper.ColorToString(_textColor));
        _settings.Set("TextStrokeThick", _strokeThickness);
        _settings.Set("TextStrokeColor", ShapeHelper.ColorToString(_strokeColor));
        _settings.Set("TextShadow",      _shadow);
        _settings.Set("TextHAlign",       _hAlign.ToString());
        _settings.Set("TextVAlign",       _vAlign.ToString());
    }

    // ── Text placement ────────────────────────────────────────────────────────

    public TextAnnotation PlaceOn(Canvas canvas, Point pos, Action onCancel, Action<TextAnnotation>? onCommit = null)
    {
        var tb = new TextBox
        {
            Background              = Brushes.Transparent,
            BorderThickness         = new Thickness(0),
            Foreground              = new SolidColorBrush(_textColor),
            CaretBrush              = new SolidColorBrush(_textColor),
            FontFamily              = new FontFamily(_fontFamily),
            FontSize                = _fontSize,
            TextWrapping            = TextWrapping.Wrap,
            AcceptsReturn           = true,
            TextAlignment           = _hAlign,
            VerticalContentAlignment = _vAlign,
        };

        var dashRect = new Rectangle
        {
            Stroke           = new SolidColorBrush(Color.FromArgb(140, 0x89, 0xB4, 0xFA)),
            StrokeThickness  = 1,
            StrokeDashArray  = new DoubleCollection { 4, 3 },
            Fill             = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        var editBorder = new Grid { Width = 200 };
        editBorder.Children.Add(dashRect);
        editBorder.Children.Add(tb);

        Canvas.SetLeft(editBorder, pos.X);
        Canvas.SetTop(editBorder, pos.Y);
        canvas.Children.Add(editBorder);

        var ann = new TextAnnotation
        {
            Tool           = this,
            Element        = editBorder,
            Start          = pos,
            End            = pos,
            IsEditing      = true,
            FontFamilyName  = _fontFamily,
            FontSize        = _fontSize,
            TextColor       = _textColor,
            StrokeThickness = _strokeThickness,
            StrokeColor     = _strokeColor,
            HAlign          = _hAlign,
            VAlign          = _vAlign,
        };

        bool committed = false;

        _editingTextBox = tb;
        _pendingCommit  = () => CommitPlace();

        void CommitPlace()
        {
            if (committed) return;
            committed       = true;
            ann.IsEditing   = false;
            _pendingCommit  = null;
            _editingTextBox = null;

            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                canvas.Children.Remove(editBorder);
                onCancel();
                return;
            }

            ann.Text = tb.Text;
            ApplyTo(ann);
            double w = Math.Max(editBorder.ActualWidth,  40);
            double h = Math.Max(editBorder.ActualHeight, 16);
            ann.Start = new Point(Canvas.GetLeft(editBorder), Canvas.GetTop(editBorder));
            ann.End   = new Point(ann.Start.X + w, ann.Start.Y + h);

            canvas.Children.Remove(editBorder);
            var rendered = RenderBlock(ann.Text, ann.FontFamilyName, ann.FontSize, ann.TextColor,
                ann.StrokeThickness, ann.StrokeColor, ann.Shadow,
                new Rect(ann.Start.X, ann.Start.Y, w, h),
                ann.HAlign, ann.VAlign);
            ann.Element = rendered;
            canvas.Children.Add(rendered);
            onCommit?.Invoke(ann);
        }

        tb.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CommitPlace();
        }), handledEventsToo: true);

        tb.Focus();
        return ann;
    }

    public void BeginEdit(Canvas canvas, TextAnnotation ann, Action onCommit)
    {
        canvas.Children.Remove(ann.Element);

        var r = ShapeHelper.NormRect(ann.Start, ann.End);

        var tb = new TextBox
        {
            Background               = Brushes.Transparent,
            BorderThickness          = new Thickness(0),
            Foreground               = new SolidColorBrush(ann.TextColor),
            CaretBrush               = new SolidColorBrush(ann.TextColor),
            FontFamily               = new FontFamily(ann.FontFamilyName),
            FontSize                 = ann.FontSize,
            TextWrapping             = TextWrapping.Wrap,
            AcceptsReturn            = true,
            Text                     = ann.Text,
            TextAlignment            = ann.HAlign,
            VerticalContentAlignment = ann.VAlign,
        };

        var dashRect = new Rectangle
        {
            Stroke           = new SolidColorBrush(Color.FromArgb(140, 0x89, 0xB4, 0xFA)),
            StrokeThickness  = 1,
            StrokeDashArray  = new DoubleCollection { 4, 3 },
            Fill             = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        var editBorder = new Grid { Width = Math.Max(r.Width, 40), Height = Math.Max(r.Height, 20) };
        editBorder.Children.Add(dashRect);
        editBorder.Children.Add(tb);

        Canvas.SetLeft(editBorder, r.X);
        Canvas.SetTop(editBorder,  r.Y);
        canvas.Children.Add(editBorder);
        ann.Element   = editBorder;
        ann.IsEditing = true;

        bool committed = false;

        _editingTextBox = tb;
        _pendingCommit  = () => CommitEdit();

        void CommitEdit()
        {
            if (committed) return;
            committed       = true;
            ann.IsEditing   = false;
            _pendingCommit  = null;
            _editingTextBox = null;

            if (!string.IsNullOrWhiteSpace(tb.Text))
                ann.Text = tb.Text;

            ApplyTo(ann);
            double w = Math.Max(editBorder.ActualWidth,  40);
            double h = Math.Max(editBorder.ActualHeight, 16);
            ann.Start = new Point(Canvas.GetLeft(editBorder), Canvas.GetTop(editBorder));
            ann.End   = new Point(ann.Start.X + w, ann.Start.Y + h);

            canvas.Children.Remove(editBorder);
            var rendered = RenderBlock(ann.Text, ann.FontFamilyName, ann.FontSize, ann.TextColor,
                ann.StrokeThickness, ann.StrokeColor, ann.Shadow,
                ShapeHelper.NormRect(ann.Start, ann.End),
                ann.HAlign, ann.VAlign);
            ann.Element = rendered;
            canvas.Children.Add(rendered);
            onCommit();
        }

        tb.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CommitEdit();
        }), handledEventsToo: true);

        tb.Focus();
        tb.SelectAll();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static UIElement RenderBlock(
        string text, string fontFamily, double fontSize, Color textColor,
        double strokeThick, Color strokeColor, double shadow,
        Rect r, TextAlignment hAlign, VerticalAlignment vAlign)
    {
        var outlinedText = new OutlinedText
        {
            Text                = text,
            FontFamilyName      = fontFamily,
            FontSizeValue       = fontSize,
            Fill                = new SolidColorBrush(textColor),
            Stroke              = strokeThick > 0 ? new SolidColorBrush(strokeColor) : null,
            StrokeWidth         = strokeThick,
            TextAlign           = hAlign,
            VerticalAlignment   = vAlign,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var border = new Border
        {
            Width        = Math.Max(r.Width,  1),
            Height       = Math.Max(r.Height, 1),
            Background   = Brushes.Transparent,
            ClipToBounds = true,
            Effect       = ShapeHelper.MakeShadow(shadow),
            Child        = outlinedText,
        };

        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border,  r.Y);
        return border;
    }

    // ── Panel ─────────────────────────────────────────────────────────────────

    private StackPanel BuildPanel()
    {
        var panel = UiHelper.MakePanel();

        // Font family
        panel.Children.Add(UiHelper.MakeLabel("Font:"));
        _fontCombo = new ComboBox
        {
            Width             = 130,
            VerticalAlignment = VerticalAlignment.Center,
            Style             = (Style)Application.Current.Resources["DarkCombo"],
        };
        foreach (var f in FontList) _fontCombo.Items.Add(f);
        _fontCombo.SelectedItem = FontList.Contains(_fontFamily) ? _fontFamily : FontList[0];
        _fontCombo.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_fontCombo.SelectedItem is string f)
            { _fontFamily = f; SaveSettings(); PropertiesChanged?.Invoke(); }
        };
        panel.Children.Add(_fontCombo);

        // Text color
        panel.Children.Add(UiHelper.MakeLabel("Color:", 10));
        _textColorPicker = UiHelper.MakePicker();
        ShapeHelper.BuildColorPickerItems(_textColorPicker, ShapeHelper.ColorPresets, () => _textColor, c =>
        {
            _textColor = c;
            ShapeHelper.UpdatePickerSelection(_textColorPicker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_textColorPicker);

        // Font size
        panel.Children.Add(UiHelper.MakeLabel("Size:", 10));
        foreach (var v in new[] { 12, 16, 20, 28, 36, 48 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "TextFontSize", val == (int)_fontSize, () =>
            {
                _fontSize = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        // Text stroke thickness
        panel.Children.Add(UiHelper.MakeLabel("Stroke:", 20));
        foreach (var v in new[] { 0, 1, 2, 3 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "TextStrokeThick", val == (int)_strokeThickness, () =>
            {
                _strokeThickness = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        // Stroke color
        _strokeColorPicker = UiHelper.MakePicker();
        ShapeHelper.BuildColorPickerItems(_strokeColorPicker, ShapeHelper.ColorPresets, () => _strokeColor, c =>
        {
            _strokeColor = c;
            ShapeHelper.UpdatePickerSelection(_strokeColorPicker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_strokeColorPicker);

        // Shadow
        panel.Children.Add(UiHelper.MakeLabel("Shadow:", 20));
        foreach (var v in new[] { 0, 1, 3, 5, 7, 10, 15 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "TextShadow", val == (int)_shadow, () =>
            {
                _shadow = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        // Horizontal alignment
        panel.Children.Add(UiHelper.MakeLabel("H:", 20));
        foreach (var (label, align) in new[] { ("←", TextAlignment.Left), ("↔", TextAlignment.Center), ("→", TextAlignment.Right) })
        {
            var a = align;
            panel.Children.Add(UiHelper.MakeRadio(label, "TextHAlign", a == _hAlign, () =>
            {
                _hAlign = a; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        // Vertical alignment
        panel.Children.Add(UiHelper.MakeLabel("V:", 10));
        foreach (var (label, align) in new[] { ("↑", VerticalAlignment.Top), ("↕", VerticalAlignment.Center), ("↓", VerticalAlignment.Bottom) })
        {
            var a = align;
            panel.Children.Add(UiHelper.MakeRadio(label, "TextVAlign", a == _vAlign, () =>
            {
                _vAlign = a; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        return panel;
    }

    private void SelectRadio(string group, int value)
    {
        string tag = value.ToString();
        foreach (UIElement child in _panel.Children)
            if (child is RadioButton rb && rb.GroupName == group)
                rb.IsChecked = rb.Tag?.ToString() == tag;
    }

    private void SelectRadioStr(string group, string tag)
    {
        foreach (UIElement child in _panel.Children)
            if (child is RadioButton rb && rb.GroupName == group)
                rb.IsChecked = rb.Tag?.ToString() == tag;
    }

    private static string HAlignTag(TextAlignment a) => a switch
    {
        TextAlignment.Center => "↔",
        TextAlignment.Right  => "→",
        _                    => "←",
    };

    private static string VAlignTag(VerticalAlignment a) => a switch
    {
        VerticalAlignment.Center => "↕",
        VerticalAlignment.Bottom => "↓",
        _                        => "↑",
    };
}
