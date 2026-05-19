using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Capturius.Helpers;
using Capturius.Models;
using Capturius.Services;

namespace Capturius.Tools;

public sealed class TextTool : ITool
{
    private readonly ISettingsStore _settings;

    private Color  _color = Color.FromRgb(0xCD, 0xD6, 0xF4);
    private double _size  = 1;

    private ItemsControl? _picker;
    private readonly StackPanel _panel;

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
    public UIElement   Render(Annotation annotation)       => annotation.Element;
    public void        SyncFrom(Annotation annotation)     { }
    public void        ApplyTo(Annotation annotation)      { }

    public void LoadSettings()
    {
        _color = ShapeHelper.ParseColor(_settings.GetString("TextColor"), _color);
        _size  = _settings.GetDouble("TextSize") ?? _size;
    }

    public void SaveSettings()
    {
        _settings.Set("TextColor", ShapeHelper.ColorToString(_color));
        _settings.Set("TextSize",  _size);
    }

    // ── Text placement ────────────────────────────────────────────────────────

    public TextAnnotation PlaceOn(Canvas canvas, Point pos, Action onCancel)
    {
        var tb = new TextBox
        {
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(120, 137, 180, 250)),
            Foreground      = new SolidColorBrush(_color),
            FontSize        = 16 + _size * 2,
            FontFamily      = new FontFamily("Segoe UI"),
            MinWidth        = 80,
            CaretBrush      = new SolidColorBrush(_color),
        };
        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb, pos.Y);
        canvas.Children.Add(tb);

        var ann = new TextAnnotation { Tool = this, Element = tb };

        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    canvas.Children.Remove(tb);
                    onCancel();
                }
                else
                {
                    tb.IsReadOnly      = true;
                    tb.BorderThickness = new Thickness(0);
                    tb.Focusable       = false;
                }
            }
        };

        tb.Focus();
        return ann;
    }

    // ── Panel ─────────────────────────────────────────────────────────────────

    private StackPanel BuildPanel()
    {
        var panel = UiHelper.MakePanel();

        panel.Children.Add(UiHelper.MakeLabel("Color:"));
        _picker = UiHelper.MakePicker();
        ShapeHelper.BuildColorPickerItems(_picker, ShapeHelper.ColorPresets, () => _color, c =>
        {
            _color = c;
            ShapeHelper.UpdatePickerSelection(_picker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_picker);

        panel.Children.Add(UiHelper.MakeLabel("Size:", 10));
        foreach (var v in new[] { 1, 2, 3, 5 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "TextSize", val == (int)_size, () =>
            {
                _size = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        return panel;
    }
}
