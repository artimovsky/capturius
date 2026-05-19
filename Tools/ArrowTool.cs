using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Capturius.Helpers;
using Capturius.Models;
using Capturius.Services;

namespace Capturius.Tools;

public sealed class ArrowTool : ITool
{
    private readonly ISettingsStore _settings;

    private Color  _fillColor       = Color.FromRgb(0xE0, 0x31, 0x31);
    private Color  _strokeColor     = Colors.Black;
    private double _thickness       = 2;
    private double _strokeThickness = 0;
    private double _shadow          = 0;

    private ItemsControl? _fillPicker;
    private ItemsControl? _strokePicker;
    private readonly StackPanel _panel;

    public event Action? PropertiesChanged;

    public ArrowTool(ISettingsStore settings)
    {
        _settings = settings;
        LoadSettings();
        _panel = BuildPanel();
    }

    public string    Name            => "Arrow";
    public UIElement PropertiesPanel => _panel;

    public UIElement? Preview(Point start, Point current)
        => Render(start, current, _fillColor, _strokeColor, _thickness, _strokeThickness, _shadow);

    public Annotation? Commit(Point start, Point end) => new ArrowAnnotation
    {
        Tool            = this,
        Element         = Render(start, end, _fillColor, _strokeColor, _thickness, _strokeThickness, _shadow),
        Start           = start,
        End             = end,
        FillColor       = _fillColor,
        StrokeColor     = _strokeColor,
        Thickness       = _thickness,
        StrokeThickness = _strokeThickness,
        Shadow          = _shadow,
    };

    public UIElement Render(Annotation annotation)
    {
        var ann = (ArrowAnnotation)annotation;
        return Render(ann.Start, ann.End, ann.FillColor, ann.StrokeColor, ann.Thickness, ann.StrokeThickness, ann.Shadow);
    }

    public void SyncFrom(Annotation annotation)
    {
        var ann          = (ArrowAnnotation)annotation;
        _fillColor       = ann.FillColor;
        _strokeColor     = ann.StrokeColor;
        _thickness       = ann.Thickness;
        _strokeThickness = ann.StrokeThickness;
        _shadow          = ann.Shadow;

        if (_fillPicker   != null) ShapeHelper.UpdatePickerSelection(_fillPicker,   _fillColor);
        if (_strokePicker != null) ShapeHelper.UpdatePickerSelection(_strokePicker, _strokeColor);
        SelectRadio("ArrowThickness",       (int)_thickness);
        SelectRadio("ArrowStrokeThickness", (int)_strokeThickness);
        SelectRadio("ArrowShadow",          (int)_shadow);
    }

    public void ApplyTo(Annotation annotation)
    {
        var ann          = (ArrowAnnotation)annotation;
        ann.FillColor       = _fillColor;
        ann.StrokeColor     = _strokeColor;
        ann.Thickness       = _thickness;
        ann.StrokeThickness = _strokeThickness;
        ann.Shadow          = _shadow;
    }

    public void LoadSettings()
    {
        _fillColor       = ShapeHelper.ParseColor(_settings.GetString("ArrowFillColor"),   _fillColor);
        _strokeColor     = ShapeHelper.ParseColor(_settings.GetString("ArrowStrokeColor"), _strokeColor);
        _thickness       = _settings.GetDouble("ArrowThickness")       ?? _thickness;
        _strokeThickness = _settings.GetDouble("ArrowStrokeThickness") ?? _strokeThickness;
        _shadow          = _settings.GetDouble("ArrowShadow")          ?? _shadow;
    }

    public void SaveSettings()
    {
        _settings.Set("ArrowFillColor",       ShapeHelper.ColorToString(_fillColor));
        _settings.Set("ArrowStrokeColor",     ShapeHelper.ColorToString(_strokeColor));
        _settings.Set("ArrowThickness",       _thickness);
        _settings.Set("ArrowStrokeThickness", _strokeThickness);
        _settings.Set("ArrowShadow",          _shadow);
    }

    // ── Panel ─────────────────────────────────────────────────────────────────

    private StackPanel BuildPanel()
    {
        var panel = UiHelper.MakePanel();

        panel.Children.Add(UiHelper.MakeLabel("Fill:"));
        _fillPicker = UiHelper.MakePicker();
        ShapeHelper.BuildColorPickerItems(_fillPicker, ShapeHelper.ColorPresets, () => _fillColor, c =>
        {
            _fillColor = c;
            ShapeHelper.UpdatePickerSelection(_fillPicker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_fillPicker);

        panel.Children.Add(UiHelper.MakeLabel("Size:", 10));
        foreach (var v in new[] { 1, 2, 3, 4, 5 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "ArrowThickness", val == (int)_thickness, () =>
            {
                _thickness = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Stroke:", 20));
        _strokePicker = UiHelper.MakePicker();
        ShapeHelper.BuildColorPickerItems(_strokePicker, ShapeHelper.ColorPresets, () => _strokeColor, c =>
        {
            _strokeColor = c;
            ShapeHelper.UpdatePickerSelection(_strokePicker!, c);
            SaveSettings(); PropertiesChanged?.Invoke();
        });
        panel.Children.Add(_strokePicker);

        panel.Children.Add(UiHelper.MakeLabel("Thickness:", 10));
        foreach (var v in new[] { 0, 1, 2, 3, 5 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "ArrowStrokeThickness", val == (int)_strokeThickness, () =>
            {
                _strokeThickness = val; SaveSettings(); PropertiesChanged?.Invoke();
            }));
        }

        panel.Children.Add(UiHelper.MakeLabel("Shadow:", 20));
        foreach (var v in new[] { 0, 1, 3, 5, 7, 10, 15 })
        {
            var val = v;
            panel.Children.Add(UiHelper.MakeRadio(val.ToString(), "ArrowShadow", val == (int)_shadow, () =>
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
        Color fillColor, Color strokeColor, double thickness, double strokeThickness, double shadow)
    {
        var dir = end - start;
        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 4) return new Path();

        dir /= len;
        var perp = new Vector(-dir.Y, dir.X);

        double headWid  = thickness * 4.5 + 6;
        double bodyWid  = thickness * 2.2 + 2;
        double headLen  = Math.Min(len * 0.638, headWid * 2.016);
        var    tip      = end;
        var    hBase    = end - dir * headLen;
        var    bodyBase = end - dir * (headLen * 0.85);

        Point[] poly  = { tip, hBase + perp * headWid, bodyBase + perp * (bodyWid * 1.1), start, bodyBase - perp * (bodyWid * 1.1), hBase - perp * headWid };
        bool[]  round = { true, true, false, true, false, true };

        var geo = new PathGeometry();
        geo.Figures.Add(RoundedPolygon(poly, round, 1.0));
        geo.Freeze();

        var shadowFx    = ShapeHelper.MakeShadow(shadow);
        var fillBrush   = new SolidColorBrush(fillColor);   fillBrush.Freeze();
        var strokeBrush = new SolidColorBrush(strokeColor); strokeBrush.Freeze();

        if (strokeThickness == 0)
            return new Path { Data = geo, Fill = fillBrush, Effect = shadowFx };

        var bgPath = new Path { Data = geo, Fill = strokeBrush, Stroke = strokeBrush, StrokeThickness = strokeThickness * 2, StrokeLineJoin = PenLineJoin.Round };
        var fgPath = new Path { Data = geo, Fill = fillBrush };
        var grid   = new Grid { Effect = shadowFx };
        grid.Children.Add(bgPath);
        grid.Children.Add(fgPath);
        return grid;
    }

    private static PathFigure RoundedPolygon(Point[] pts, bool[] round, double r)
    {
        int n = pts.Length;

        Vector EdgeDir(int from, int to)
        {
            var d = pts[to] - pts[from];
            var l = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            return l > 0 ? d / l : new Vector(0, 0);
        }

        Point ArcStart(int i) => pts[i] - EdgeDir((i - 1 + n) % n, i) * r;
        Point ArcEnd(int i)   => pts[i] + EdgeDir(i, (i + 1) % n) * r;

        var startPt = round[0] ? ArcEnd(0) : pts[0];
        var fig = new PathFigure { StartPoint = startPt, IsClosed = false };

        for (int i = 0; i < n; i++)
        {
            int next   = (i + 1) % n;
            var target = round[next] ? ArcEnd(next) : pts[next];
            if (round[next])
            {
                fig.Segments.Add(new LineSegment(ArcStart(next), true));
                fig.Segments.Add(new QuadraticBezierSegment(pts[next], target, true));
            }
            else
            {
                fig.Segments.Add(new LineSegment(target, true));
            }
        }
        return fig;
    }
}
