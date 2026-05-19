using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Capturius.Services;

namespace Capturius.Windows;

public partial class EditorWindow : Window
{
    // ── Annotation model ──────────────────────────────────────────────────────

    private class Annotation
    {
        public required string    Tool            { get; init; }
        public required UIElement Element         { get; set; }
        // Arrow-specific (used for re-rendering when properties change)
        public Point  Start           { get; set; }
        public Point  End             { get; set; }
        public Color  FillColor       { get; set; }
        public Color  StrokeColor     { get; set; }
        public double Thickness       { get; set; }
        public double StrokeThickness { get; set; }
        public double Shadow          { get; set; }
        public double CornerRadius    { get; set; }
        public double Opacity         { get; set; } = 100;
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    private BitmapSource _bitmapSource;
    private string _currentTool = "Arrow";
    private Color  _currentColor = Color.FromRgb(0xE0, 0x31, 0x31);
    private double _currentThickness = 1;
    private double _zoomLevel = 1.0;
    private readonly double _dpiScale = ScreenCaptureService.GetDpiScale();

    private Point _drawStart;
    private bool  _isDrawing;

    // Arrow tool state
    private Color  _arrowFillColor       = Color.FromRgb(0xE0, 0x31, 0x31);
    private Color  _arrowStrokeColor     = Colors.Black;
    private double _arrowThickness       = 2;
    private double _arrowStrokeThickness = 0;
    private double _arrowShadow          = 0;

    // Rect tool state
    private Color  _rectFillColor       = Colors.Transparent;
    private Color  _rectBorderColor     = Color.FromRgb(0xE0, 0x31, 0x31);
    private double _rectBorderThickness = 2;
    private double _rectCornerRadius    = 0;
    private double _rectOpacity         = 100;
    private double _rectShadow          = 0;

    // Highlighter tool state
    private Color  _highlighterColor  = Color.FromRgb(0xFF, 0xFF, 0x00);
    private double _highlighterShadow = 0;

    // Temporary shape shown while dragging
    private UIElement? _previewElement;
    // All committed annotations
    private readonly List<Annotation> _annotations = new();

    // Selection state
    private Annotation?              _selectedAnnotation;
    private readonly List<UIElement> _selectionHandles = new();

    // Drag/resize state
    private bool   _isDragging;
    private string _dragMode      = "";   // "move" or "resize"
    private int    _dragCornerIdx;        // 0=TL, 1=TR, 2=BL, 3=BR
    private Point  _dragMouseStart;
    private Point  _dragAnnStart;         // TL of annotation at drag start
    private Point  _dragAnnEnd;           // BR of annotation at drag start

    // Crop selection state
    private System.Windows.Rect _cropRect;
    private Rectangle?          _cropBorder;

    private static readonly (Color Color, string Name)[] ColorPresets =
    [
        (Color.FromRgb(0xE0, 0x31, 0x31), "Red"),
        (Color.FromRgb(0x19, 0x71, 0xC2), "Blue"),
        (Color.FromRgb(0x2F, 0x9D, 0x43), "Green"),
        (Color.FromRgb(0xF0, 0x8C, 0x00), "Yellow"),
        (Colors.White,  "White"),
        (Colors.Black,  "Black"),
    ];

    public EditorWindow(BitmapSource bitmapSource)
    {
        InitializeComponent();

        _bitmapSource = bitmapSource;
        MainImage.Source = bitmapSource;
        AnnotationCanvas.Width  = bitmapSource.PixelWidth;
        AnnotationCanvas.Height = bitmapSource.PixelHeight;

        LoadArrowSettings();
        LoadRectSettings();
        LoadHighlighterSettings();
        BuildColorPicker();
        BuildArrowColorPickers();
        BuildRectColorPickers();
        BuildHighlighterColorPicker();
        SetZoom(1.0);

        Loaded += (_, _) => { ApplyArrowRadioSelections(); ApplyRectRadioSelections(); ApplyHighlighterRadioSelections(); };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Undo_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _selectedAnnotation != null)
            {
                DeleteAnnotation(_selectedAnnotation);
                e.Handled = true;
            }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Copy_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DeselectAnnotation();
                e.Handled = true;
            }
        };
    }

    // ── Color picker ──────────────────────────────────────────────────────────

    private void BuildColorPicker()
    {
        BuildColorPickerItems(ColorPicker, () => _currentColor, c =>
        {
            _currentColor = c;
            UpdatePickerSelection(ColorPicker, c);
        });
    }

    private void BuildArrowColorPickers()
    {
        BuildColorPickerItems(ArrowFillPicker, () => _arrowFillColor, c =>
        {
            _arrowFillColor = c;
            UpdatePickerSelection(ArrowFillPicker, c);
            SaveArrowSettings();
            RedrawSelectedAnnotation();
        });
        BuildColorPickerItems(ArrowStrokePicker, () => _arrowStrokeColor, c =>
        {
            _arrowStrokeColor = c;
            UpdatePickerSelection(ArrowStrokePicker, c);
            SaveArrowSettings();
            RedrawSelectedAnnotation();
        });
    }

    private void BuildRectColorPickers()
    {
        BuildFillColorPickerItems(RectFillPicker, () => _rectFillColor, c =>
        {
            _rectFillColor = c;
            UpdateFillPickerSelection(RectFillPicker, c);
            SaveRectSettings();
            RedrawSelectedAnnotation();
        });
        BuildColorPickerItems(RectBorderPicker, () => _rectBorderColor, c =>
        {
            _rectBorderColor = c;
            UpdatePickerSelection(RectBorderPicker, c);
            SaveRectSettings();
            RedrawSelectedAnnotation();
        });
    }

    private static readonly (Color Color, string Name)[] HighlighterColorPresets =
    [
        (Color.FromRgb(0xFF, 0xFF, 0x00), "Bright Yellow"),
        (Color.FromRgb(0xFF, 0x6B, 0x8A), "Pink"),
        ..ColorPresets,
    ];

    private void BuildHighlighterColorPicker()
    {
        BuildColorPickerItems(HighlighterColorPicker, HighlighterColorPresets, () => _highlighterColor, c =>
        {
            _highlighterColor = c;
            UpdatePickerSelection(HighlighterColorPicker, c);
            SaveHighlighterSettings();
            RedrawSelectedAnnotation();
        });
    }

    private void BuildFillColorPickerItems(ItemsControl control, Func<Color> getColor, Action<Color> onSelect)
    {
        // "None" swatch (transparent fill)
        var noneInner = new Grid();
        noneInner.Children.Add(new Line
        {
            X1 = 1, Y1 = 13, X2 = 13, Y2 = 1,
            Stroke = Brushes.OrangeRed, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
        var noneBtn = new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Margin = new Thickness(2, 0, 2, 0),
            Cursor = Cursors.Hand,
            ToolTip = "None",
            BorderThickness = new Thickness(2),
            BorderBrush = getColor().A == 0 ? Brushes.White : Brushes.Transparent,
            Tag = Colors.Transparent,
            Child = noneInner,
            ClipToBounds = true,
        };
        noneBtn.MouseLeftButtonDown += (s, e) => onSelect(Colors.Transparent);
        control.Items.Add(noneBtn);

        // Regular color swatches
        BuildColorPickerItems(control, getColor, onSelect);
    }

    private static void UpdateFillPickerSelection(ItemsControl control, Color selected)
    {
        foreach (Border b in control.Items)
            b.BorderBrush = (Color)b.Tag == selected ? Brushes.White : Brushes.Transparent;
    }

    private void BuildColorPickerItems(ItemsControl control, Func<Color> getColor, Action<Color> onSelect)
        => BuildColorPickerItems(control, ColorPresets, getColor, onSelect);

    private void BuildColorPickerItems(ItemsControl control, (Color Color, string Name)[] presets, Func<Color> getColor, Action<Color> onSelect)
    {
        foreach (var (color, name) in presets)
        {
            var btn = new Border
            {
                Width = 20, Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(color),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = name,
                BorderThickness = new Thickness(2),
                BorderBrush = color == getColor()
                    ? new SolidColorBrush(Colors.White)
                    : Brushes.Transparent,
                Tag = color,
            };
            btn.MouseLeftButtonDown += (s, e) => onSelect((Color)((Border)s).Tag);
            control.Items.Add(btn);
        }
    }

    private static void UpdatePickerSelection(ItemsControl control, Color selected)
    {
        foreach (Border b in control.Items)
            b.BorderBrush = (Color)b.Tag == selected ? Brushes.White : Brushes.Transparent;
    }

    // ── Toolbar handlers ──────────────────────────────────────────────────────

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (BtnApplyCrop == null) return; // not yet initialized

        _currentTool = ((ToggleButton)sender).Tag!.ToString()!;
        UncheckOtherTools((ToggleButton)sender);

        bool isCrop        = _currentTool == "Crop";
        bool isArrow       = _currentTool == "Arrow";
        bool isRect        = _currentTool == "Rect";
        bool isHighlighter = _currentTool == "Highlighter";

        BtnApplyCrop.Visibility          = isCrop                                   ? Visibility.Visible   : Visibility.Collapsed;
        GlobalProperties.Visibility      = (isArrow || isRect || isHighlighter) ? Visibility.Collapsed : Visibility.Visible;
        ArrowProperties.Visibility       = isArrow       ? Visibility.Visible   : Visibility.Collapsed;
        RectProperties.Visibility        = isRect        ? Visibility.Visible   : Visibility.Collapsed;
        HighlighterProperties.Visibility = isHighlighter ? Visibility.Visible   : Visibility.Collapsed;

        if (!isCrop) RemoveCropPreview();
        AnnotationCanvas.Cursor = _currentTool == "Text" ? Cursors.IBeam : Cursors.Cross;

        DeselectAnnotation();
    }

    private void UncheckOtherTools(ToggleButton active)
    {
        foreach (var btn in new[] { BtnArrow, BtnRect, BtnHighlighter, BtnText, BtnCrop })
            if (btn != null && btn != active) btn.IsChecked = false;
    }

    private void Thickness_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
            _currentThickness = t;
    }

    private void ArrowThickness_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _arrowThickness = t; SaveArrowSettings(); RedrawSelectedAnnotation(); }
    }

    private void ArrowStrokeThickness_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _arrowStrokeThickness = t; SaveArrowSettings(); RedrawSelectedAnnotation(); }
    }

    private void ArrowShadow_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _arrowShadow = t; SaveArrowSettings(); RedrawSelectedAnnotation(); }
    }

    private void RectBorderThickness_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _rectBorderThickness = t; SaveRectSettings(); RedrawSelectedAnnotation(); }
    }

    private void RectCornerRadius_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _rectCornerRadius = t; SaveRectSettings(); RedrawSelectedAnnotation(); }
    }

    private void RectOpacity_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _rectOpacity = t; SaveRectSettings(); RedrawSelectedAnnotation(); }
    }

    private void RectShadow_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _rectShadow = t; SaveRectSettings(); RedrawSelectedAnnotation(); }
    }

    // ── Settings persistence ──────────────────────────────────────────────────

    private const string EditorRegKey = @"Software\Capturius\Editor";

    private void LoadArrowSettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(EditorRegKey);
        if (key == null) return;

        _arrowFillColor   = ParseColor(key.GetValue("ArrowFillColor")   as string, _arrowFillColor);
        _arrowStrokeColor = ParseColor(key.GetValue("ArrowStrokeColor") as string, _arrowStrokeColor);
        if (int.TryParse(key.GetValue("ArrowThickness")?.ToString(),       out var t))  _arrowThickness       = t;
        if (int.TryParse(key.GetValue("ArrowStrokeThickness")?.ToString(), out var st)) _arrowStrokeThickness = st;
        if (int.TryParse(key.GetValue("ArrowShadow")?.ToString(),          out var sh)) _arrowShadow          = sh;
    }

    private void SaveArrowSettings()
    {
        using var key = Registry.CurrentUser.CreateSubKey(EditorRegKey);
        key.SetValue("ArrowFillColor",       ColorToString(_arrowFillColor));
        key.SetValue("ArrowStrokeColor",     ColorToString(_arrowStrokeColor));
        key.SetValue("ArrowThickness",       (int)_arrowThickness);
        key.SetValue("ArrowStrokeThickness", (int)_arrowStrokeThickness);
        key.SetValue("ArrowShadow",          (int)_arrowShadow);
    }

    private void LoadRectSettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(EditorRegKey);
        if (key == null) return;

        _rectFillColor   = ParseColor(key.GetValue("RectFillColor")   as string, _rectFillColor);
        _rectBorderColor = ParseColor(key.GetValue("RectBorderColor") as string, _rectBorderColor);
        if (double.TryParse(key.GetValue("RectBorderThickness")?.ToString(), out var bt)) _rectBorderThickness = bt;
        if (double.TryParse(key.GetValue("RectCornerRadius")?.ToString(),    out var cr)) _rectCornerRadius    = cr;
        if (double.TryParse(key.GetValue("RectOpacity")?.ToString(),         out var op)) _rectOpacity         = op;
        if (double.TryParse(key.GetValue("RectShadow")?.ToString(),          out var sh)) _rectShadow          = sh;
    }

    private void SaveRectSettings()
    {
        using var key = Registry.CurrentUser.CreateSubKey(EditorRegKey);
        key.SetValue("RectFillColor",       ColorToString(_rectFillColor));
        key.SetValue("RectBorderColor",     ColorToString(_rectBorderColor));
        key.SetValue("RectBorderThickness", _rectBorderThickness);
        key.SetValue("RectCornerRadius",    _rectCornerRadius);
        key.SetValue("RectOpacity",         _rectOpacity);
        key.SetValue("RectShadow",          _rectShadow);
    }

    private void LoadHighlighterSettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(EditorRegKey);
        if (key == null) return;
        _highlighterColor = ParseColor(key.GetValue("HighlighterColor") as string, _highlighterColor);
        if (double.TryParse(key.GetValue("HighlighterShadow")?.ToString(), out var sh)) _highlighterShadow = sh;
    }

    private void SaveHighlighterSettings()
    {
        using var key = Registry.CurrentUser.CreateSubKey(EditorRegKey);
        key.SetValue("HighlighterColor",  ColorToString(_highlighterColor));
        key.SetValue("HighlighterShadow", _highlighterShadow);
    }

    private void HighlighterShadow_Checked(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(((RadioButton)sender).Tag?.ToString(), out var t))
        { _highlighterShadow = t; SaveHighlighterSettings(); RedrawSelectedAnnotation(); }
    }

    private void ApplyArrowRadioSelections()
    {
        SelectRadio("ArrowThickness",       (int)_arrowThickness);
        SelectRadio("ArrowStrokeThickness", (int)_arrowStrokeThickness);
        SelectRadio("ArrowShadow",          (int)_arrowShadow);
    }

    private void ApplyRectRadioSelections()
    {
        SelectRadio("RectBorderThickness", (int)_rectBorderThickness);
        SelectRadio("RectCornerRadius",    (int)_rectCornerRadius);
        SelectRadio("RectOpacity",         (int)_rectOpacity);
        SelectRadio("RectShadow",          (int)_rectShadow);
    }

    private void ApplyHighlighterRadioSelections()
    {
        SelectRadio("HighlighterShadow", (int)_highlighterShadow);
    }

    private void SelectRadio(string groupName, int value)
    {
        string tag = value.ToString();
        foreach (var panel in new StackPanel[] { ArrowProperties, RectProperties, HighlighterProperties })
            foreach (UIElement child in panel.Children)
                if (child is RadioButton rb && rb.GroupName == groupName)
                    rb.IsChecked = rb.Tag?.ToString() == tag;
    }

    private static string ColorToString(Color c) => $"{c.A},{c.R},{c.G},{c.B}";

    private static Color ParseColor(string? s, Color fallback)
    {
        if (s == null) return fallback;
        var p = s.Split(',');
        if (p.Length == 4 &&
            byte.TryParse(p[0], out var a) && byte.TryParse(p[1], out var r) &&
            byte.TryParse(p[2], out var g) && byte.TryParse(p[3], out var b))
            return Color.FromArgb(a, r, g, b);
        return fallback;
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private void ZoomIn_Click(object sender, RoutedEventArgs e)  => SetZoom(_zoomLevel * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomLevel / 1.25);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Delta > 0) SetZoom(_zoomLevel * 1.25);
        else             SetZoom(_zoomLevel / 1.25);
        e.Handled = true;
    }

    private void SetZoom(double zoom)
    {
        _zoomLevel = Math.Clamp(zoom, 0.1, 8.0);
        ZoomTransform.ScaleX = _zoomLevel / _dpiScale;
        ZoomTransform.ScaleY = _zoomLevel / _dpiScale;
        ZoomLabel.Text = $"{(int)Math.Round(_zoomLevel * 100)}%";
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(AnnotationCanvas);

        if (_currentTool == "Arrow" || _currentTool == "Rect" || _currentTool == "Highlighter")
        {
            // 1a. Check endpoints of selected Arrow → drag endpoint
            if (_selectedAnnotation?.Tool == "Arrow")
            {
                Point[] eps = { _selectedAnnotation.Start, _selectedAnnotation.End };
                for (int i = 0; i < 2; i++)
                {
                    var d = eps[i] - pos;
                    if (Math.Sqrt(d.X * d.X + d.Y * d.Y) <= 8)
                    {
                        _isDragging     = true;
                        _dragMode       = "resize";
                        _dragCornerIdx  = i;
                        _dragMouseStart = pos;
                        _dragAnnStart   = _selectedAnnotation.Start;
                        _dragAnnEnd     = _selectedAnnotation.End;
                        AnnotationCanvas.Cursor = i == 0 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                        AnnotationCanvas.CaptureMouse();
                        return;
                    }
                }
            }

            // 1b. Check corner handles of selected Rect/Highlighter → resize
            if (_selectedAnnotation?.Tool == "Rect" || _selectedAnnotation?.Tool == "Highlighter")
            {
                int ci = FindNearestHandle(pos, _selectedAnnotation, 8);
                if (ci >= 0)
                {
                    _isDragging     = true;
                    _dragMode       = "resize";
                    _dragCornerIdx  = ci;
                    _dragMouseStart = pos;
                    var r = NormRect(_selectedAnnotation.Start, _selectedAnnotation.End);
                    _dragAnnStart = new Point(r.Left,  r.Top);
                    _dragAnnEnd   = new Point(r.Right, r.Bottom);
                    AnnotationCanvas.Cursor = ci switch
                    {
                        0 or 3 => Cursors.SizeNWSE,
                        1 or 2 => Cursors.SizeNESW,
                        4 or 6 => Cursors.SizeNS,
                        5 or 7 => Cursors.SizeWE,
                        _      => Cursors.Cross,
                    };
                    AnnotationCanvas.CaptureMouse();
                    return;
                }
            }

            // 2. Hit-test any annotation
            var hit = HitTestAnnotation(pos);
            if (hit != null)
            {
                if (hit == _selectedAnnotation)
                {
                    // Move already-selected annotation
                    _isDragging     = true;
                    _dragMode       = "move";
                    _dragMouseStart = pos;
                    var r = NormRect(hit.Start, hit.End);
                    bool isRectLike = hit.Tool == "Rect" || hit.Tool == "Highlighter";
                    _dragAnnStart = isRectLike ? new Point(r.Left, r.Top)     : hit.Start;
                    _dragAnnEnd   = isRectLike ? new Point(r.Right, r.Bottom) : hit.End;
                    AnnotationCanvas.Cursor = Cursors.SizeAll;
                    AnnotationCanvas.CaptureMouse();
                    return;
                }
                SelectAnnotation(hit);
                return;
            }
        }

        DeselectAnnotation();
        _drawStart = pos;
        _isDrawing = true;
        AnnotationCanvas.CaptureMouse();

        if (_currentTool == "Text")
        {
            _isDrawing = false;
            AnnotationCanvas.ReleaseMouseCapture();
            PlaceTextBox(_drawStart);
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var cur = e.GetPosition(AnnotationCanvas);

        if (_isDragging && _selectedAnnotation != null)
        {
            var ann = _selectedAnnotation;
            if (_dragMode == "move")
            {
                var delta = cur - _dragMouseStart;
                ann.Start = _dragAnnStart + delta;
                ann.End   = _dragAnnEnd   + delta;
            }
            else // resize
            {
                if (ann.Tool == "Arrow")
                {
                    if (_dragCornerIdx == 0) ann.Start = cur;
                    else                     ann.End   = cur;
                }
                else // Rect / Highlighter
                {
                    switch (_dragCornerIdx)
                    {
                        // corners
                        case 0: ann.Start = cur;                                    ann.End = _dragAnnEnd;                           break;
                        case 1: ann.Start = new Point(_dragAnnStart.X, cur.Y);     ann.End = new Point(cur.X, _dragAnnEnd.Y);       break;
                        case 2: ann.Start = new Point(cur.X, _dragAnnStart.Y);     ann.End = new Point(_dragAnnEnd.X, cur.Y);       break;
                        case 3: ann.Start = _dragAnnStart;                          ann.End = cur;                                   break;
                        // edge midpoints — one axis only
                        case 4: ann.Start = new Point(_dragAnnStart.X, cur.Y);     ann.End = _dragAnnEnd;                           break; // top
                        case 5: ann.Start = _dragAnnStart;                          ann.End = new Point(cur.X, _dragAnnEnd.Y);       break; // right
                        case 6: ann.Start = _dragAnnStart;                          ann.End = new Point(_dragAnnEnd.X, cur.Y);       break; // bottom
                        case 7: ann.Start = new Point(cur.X, _dragAnnStart.Y);     ann.End = _dragAnnEnd;                           break; // left
                    }
                }
            }

            int idx = AnnotationCanvas.Children.IndexOf(ann.Element);
            AnnotationCanvas.Children.Remove(ann.Element);
            ann.Element = ann.Tool switch
            {
                "Rect"        => MakeRect(ann.Start, ann.End, ann.FillColor, ann.StrokeColor, ann.Thickness, ann.CornerRadius, ann.Opacity, ann.Shadow),
                "Highlighter" => MakeHighlighter(ann.Start, ann.End, ann.FillColor, ann.Shadow),
                _             => MakeArrow(ann.Start, ann.End, ann.FillColor, ann.StrokeColor, ann.Thickness, ann.StrokeThickness, ann.Shadow),
            };
            if (idx >= 0) AnnotationCanvas.Children.Insert(idx, ann.Element);
            else          AnnotationCanvas.Children.Add(ann.Element);
            ShowHandles(ann);
            return;
        }

        // Cursor update while hovering (not drawing, not dragging)
        if (!_isDrawing && (_currentTool == "Arrow" || _currentTool == "Rect" || _currentTool == "Highlighter"))
        {
            AnnotationCanvas.Cursor = HoverCursor(cur);
            return;
        }

        if (!_isDrawing) return;

        if (_previewElement != null)
            AnnotationCanvas.Children.Remove(_previewElement);

        _previewElement = _currentTool switch
        {
            "Arrow"       => MakeArrow(_drawStart, cur, _arrowFillColor, _arrowStrokeColor, _arrowThickness, _arrowStrokeThickness, _arrowShadow),
            "Rect"        => MakeRect(_drawStart, cur, _rectFillColor, _rectBorderColor, _rectBorderThickness, _rectCornerRadius, _rectOpacity, _rectShadow),
            "Highlighter" => MakeHighlighter(_drawStart, cur, _highlighterColor, _highlighterShadow),
            "Crop"        => MakeCropPreview(_drawStart, cur),
            _             => null
        };

        if (_previewElement != null)
            AnnotationCanvas.Children.Add(_previewElement);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            AnnotationCanvas.ReleaseMouseCapture();
            AnnotationCanvas.Cursor = Cursors.Cross;
            return;
        }

        if (!_isDrawing) return;
        _isDrawing = false;
        AnnotationCanvas.ReleaseMouseCapture();

        var cur = e.GetPosition(AnnotationCanvas);

        if (_previewElement != null)
            AnnotationCanvas.Children.Remove(_previewElement);
        _previewElement = null;

        if (_currentTool == "Crop")
        {
            var r = NormRect(_drawStart, cur);
            if (r.Width > 4 && r.Height > 4)
            {
                _cropRect = r;
                _cropBorder = MakeCropPreview(_drawStart, cur) as Rectangle;
                if (_cropBorder != null)
                    AnnotationCanvas.Children.Add(_cropBorder);
            }
            return;
        }

        if (_currentTool == "Arrow")
        {
            var el = MakeArrow(_drawStart, cur, _arrowFillColor, _arrowStrokeColor, _arrowThickness, _arrowStrokeThickness, _arrowShadow);
            AnnotationCanvas.Children.Add(el);
            _annotations.Add(new Annotation
            {
                Tool            = "Arrow",
                Element         = el,
                Start           = _drawStart,
                End             = cur,
                FillColor       = _arrowFillColor,
                StrokeColor     = _arrowStrokeColor,
                Thickness       = _arrowThickness,
                StrokeThickness = _arrowStrokeThickness,
                Shadow          = _arrowShadow,
            });
            return;
        }

        if (_currentTool == "Rect")
        {
            var el = MakeRect(_drawStart, cur, _rectFillColor, _rectBorderColor, _rectBorderThickness, _rectCornerRadius, _rectOpacity, _rectShadow);
            AnnotationCanvas.Children.Add(el);
            _annotations.Add(new Annotation
            {
                Tool         = "Rect",
                Element      = el,
                Start        = _drawStart,
                End          = cur,
                FillColor    = _rectFillColor,
                StrokeColor  = _rectBorderColor,
                Thickness    = _rectBorderThickness,
                CornerRadius = _rectCornerRadius,
                Opacity      = _rectOpacity,
                Shadow       = _rectShadow,
            });
        }

        if (_currentTool == "Highlighter")
        {
            var el = MakeHighlighter(_drawStart, cur, _highlighterColor, _highlighterShadow);
            AnnotationCanvas.Children.Add(el);
            _annotations.Add(new Annotation
            {
                Tool      = "Highlighter",
                Element   = el,
                Start     = _drawStart,
                End       = cur,
                FillColor = _highlighterColor,
                Shadow    = _highlighterShadow,
            });
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    private Cursor HoverCursor(Point pos)
    {
        if (_selectedAnnotation?.Tool == "Arrow")
        {
            var d0 = _selectedAnnotation.Start - pos;
            if (Math.Sqrt(d0.X * d0.X + d0.Y * d0.Y) <= 8) return Cursors.SizeNWSE;
            var d1 = _selectedAnnotation.End - pos;
            if (Math.Sqrt(d1.X * d1.X + d1.Y * d1.Y) <= 8) return Cursors.SizeNESW;
        }
        if (_selectedAnnotation?.Tool == "Rect" || _selectedAnnotation?.Tool == "Highlighter")
        {
            int ci = FindNearestHandle(pos, _selectedAnnotation, 8);
            if (ci >= 0)
                return ci switch
                {
                    0 or 3 => Cursors.SizeNWSE,
                    1 or 2 => Cursors.SizeNESW,
                    4 or 6 => Cursors.SizeNS,
                    5 or 7 => Cursors.SizeWE,
                    _      => Cursors.Cross,
                };
        }
        if (_selectedAnnotation != null && HitTestAnnotation(pos) == _selectedAnnotation)
            return Cursors.SizeAll;
        return Cursors.Cross;
    }

    private static int FindNearestHandle(Point pos, Annotation ann, double threshold)
    {
        var r = NormRect(ann.Start, ann.End);
        double cx = (r.Left + r.Right)  / 2;
        double cy = (r.Top  + r.Bottom) / 2;
        Point[] corners = {
            // corners (0-3)
            new Point(r.Left,  r.Top),    new Point(r.Right, r.Top),
            new Point(r.Left,  r.Bottom), new Point(r.Right, r.Bottom),
            // edge midpoints (4-7): top, right, bottom, left
            new Point(cx,      r.Top),    new Point(r.Right, cy),
            new Point(cx,      r.Bottom), new Point(r.Left,  cy),
        };
        for (int i = 0; i < corners.Length; i++)
        {
            var d = corners[i] - pos;
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) <= threshold)
                return i;
        }
        return -1;
    }

    private Annotation? HitTestAnnotation(Point pos)
    {
        Annotation? found = null;
        VisualTreeHelper.HitTest(
            AnnotationCanvas,
            null,
            result =>
            {
                var ann = FindAnnotationByVisual(result.VisualHit);
                if (ann?.Tool == "Arrow" || ann?.Tool == "Rect" || ann?.Tool == "Highlighter") { found = ann; return HitTestResultBehavior.Stop; }
                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(pos));
        return found;
    }

    private Annotation? FindAnnotationByVisual(DependencyObject visual)
    {
        DependencyObject? current = visual;
        while (current != null && !ReferenceEquals(current, AnnotationCanvas))
        {
            if (current is UIElement el)
            {
                var ann = _annotations.FirstOrDefault(a => ReferenceEquals(a.Element, el));
                if (ann != null) return ann;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void SelectAnnotation(Annotation ann)
    {
        DeselectAnnotation();
        _selectedAnnotation = ann;

        if (ann.Tool == "Arrow")
        {
            _arrowFillColor       = ann.FillColor;
            _arrowStrokeColor     = ann.StrokeColor;
            _arrowThickness       = ann.Thickness;
            _arrowStrokeThickness = ann.StrokeThickness;
            _arrowShadow          = ann.Shadow;

            UpdatePickerSelection(ArrowFillPicker,   ann.FillColor);
            UpdatePickerSelection(ArrowStrokePicker, ann.StrokeColor);
            ApplyArrowRadioSelections();
        }
        else if (ann.Tool == "Rect")
        {
            _rectFillColor       = ann.FillColor;
            _rectBorderColor     = ann.StrokeColor;
            _rectBorderThickness = ann.Thickness;
            _rectCornerRadius    = ann.CornerRadius;
            _rectOpacity         = ann.Opacity;
            _rectShadow          = ann.Shadow;

            UpdateFillPickerSelection(RectFillPicker,    ann.FillColor);
            UpdatePickerSelection(RectBorderPicker, ann.StrokeColor);
            ApplyRectRadioSelections();
        }
        else if (ann.Tool == "Highlighter")
        {
            _highlighterColor  = ann.FillColor;
            _highlighterShadow = ann.Shadow;
            UpdatePickerSelection(HighlighterColorPicker, ann.FillColor);
            ApplyHighlighterRadioSelections();
        }

        ShowHandles(ann);
    }

    private void DeselectAnnotation()
    {
        _selectedAnnotation = null;
        HideHandles();
    }

    private void DeleteAnnotation(Annotation ann)
    {
        DeselectAnnotation();
        AnnotationCanvas.Children.Remove(ann.Element);
        _annotations.Remove(ann);
    }

    private void ShowHandles(Annotation ann)
    {
        HideHandles();
        IEnumerable<Point> pts;
        if (ann.Tool == "Rect" || ann.Tool == "Highlighter")
        {
            var r = NormRect(ann.Start, ann.End);
            double cx = (r.Left + r.Right)  / 2;
            double cy = (r.Top  + r.Bottom) / 2;
            pts = new[] {
                // corners
                new Point(r.Left,  r.Top),    new Point(r.Right, r.Top),
                new Point(r.Left,  r.Bottom), new Point(r.Right, r.Bottom),
                // edge midpoints
                new Point(cx,      r.Top),    new Point(r.Right, cy),
                new Point(cx,      r.Bottom), new Point(r.Left,  cy),
            };
        }
        else
        {
            pts = new[] { ann.Start, ann.End };
        }
        foreach (var pt in pts)
        {
            var handle = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(handle, pt.X - 4);
            Canvas.SetTop(handle, pt.Y - 4);
            AnnotationCanvas.Children.Add(handle);
            _selectionHandles.Add(handle);
        }
    }

    private void HideHandles()
    {
        foreach (var h in _selectionHandles)
            AnnotationCanvas.Children.Remove(h);
        _selectionHandles.Clear();
    }

    private void RedrawSelectedAnnotation()
    {
        if (_selectedAnnotation == null) return;
        var ann = _selectedAnnotation;

        UIElement newEl;
        if (ann.Tool == "Arrow")
        {
            ann.FillColor       = _arrowFillColor;
            ann.StrokeColor     = _arrowStrokeColor;
            ann.Thickness       = _arrowThickness;
            ann.StrokeThickness = _arrowStrokeThickness;
            ann.Shadow          = _arrowShadow;
            newEl = MakeArrow(ann.Start, ann.End, ann.FillColor, ann.StrokeColor, ann.Thickness, ann.StrokeThickness, ann.Shadow);
        }
        else if (ann.Tool == "Rect")
        {
            ann.FillColor    = _rectFillColor;
            ann.StrokeColor  = _rectBorderColor;
            ann.Thickness    = _rectBorderThickness;
            ann.CornerRadius = _rectCornerRadius;
            ann.Opacity      = _rectOpacity;
            ann.Shadow       = _rectShadow;
            newEl = MakeRect(ann.Start, ann.End, ann.FillColor, ann.StrokeColor, ann.Thickness, ann.CornerRadius, ann.Opacity, ann.Shadow);
        }
        else if (ann.Tool == "Highlighter")
        {
            ann.FillColor = _highlighterColor;
            ann.Shadow    = _highlighterShadow;
            newEl = MakeHighlighter(ann.Start, ann.End, ann.FillColor, ann.Shadow);
        }
        else return;

        int idx = AnnotationCanvas.Children.IndexOf(ann.Element);
        AnnotationCanvas.Children.Remove(ann.Element);
        ann.Element = newEl;

        if (idx >= 0)
            AnnotationCanvas.Children.Insert(idx, newEl);
        else
            AnnotationCanvas.Children.Add(newEl);

        ShowHandles(ann);
    }

    // ── Shape factories ───────────────────────────────────────────────────────

    private static UIElement MakeArrow(Point start, Point end, Color fillColor, Color strokeColor, double thickness, double strokeThickness, double shadow)
    {
        var dir = end - start;
        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 4) return new Path();

        dir /= len;
        var perp = new Vector(-dir.Y, dir.X);

        double headWid = thickness * 4.5 + 6;
        double bodyWid = thickness * 2.2 + 2;
        double headLen = Math.Min(len * 0.638, headWid * 2.016);
        var tip      = end;
        var hBase    = end - dir * headLen;
        var bodyBase = end - dir * (headLen * 0.85);

        Point[] poly = {
            tip,
            hBase    + perp * headWid,
            bodyBase + perp * (bodyWid * 1.1),
            start,
            bodyBase - perp * (bodyWid * 1.1),
            hBase    - perp * headWid,
        };
        bool[] round = { true, true, false, true, false, true };

        var geo = new PathGeometry();
        geo.Figures.Add(RoundedPolygon(poly, round, 1.0));
        geo.Freeze();

        System.Windows.Media.Effects.DropShadowEffect? shadowEffect = shadow > 0
            ? new System.Windows.Media.Effects.DropShadowEffect
              {
                  BlurRadius  = shadow,
                  ShadowDepth = 0,
                  Direction   = 315,
                  Color       = Colors.Black,
                  Opacity     = 0.5,
              }
            : null;
        shadowEffect?.Freeze();

        var fillBrush   = new SolidColorBrush(fillColor);   fillBrush.Freeze();
        var strokeBrush = new SolidColorBrush(strokeColor); strokeBrush.Freeze();

        if (strokeThickness == 0)
            return new Path { Data = geo, Fill = fillBrush, Effect = shadowEffect };

        var bgPath = new Path
        {
            Data            = geo,
            Fill            = strokeBrush,
            Stroke          = strokeBrush,
            StrokeThickness = strokeThickness * 2,
            StrokeLineJoin  = PenLineJoin.Round,
        };
        var fgPath = new Path { Data = geo, Fill = fillBrush };
        var container = new Grid { Effect = shadowEffect };
        container.Children.Add(bgPath);
        container.Children.Add(fgPath);
        return container;
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

    private static UIElement MakeRect(Point start, Point end, Color fillColor, Color borderColor, double borderThickness, double cornerRadius, double opacity, double shadow)
    {
        var r = NormRect(start, end);

        System.Windows.Media.Effects.DropShadowEffect? shadowEffect = shadow > 0
            ? new System.Windows.Media.Effects.DropShadowEffect
              {
                  BlurRadius  = shadow,
                  ShadowDepth = 0,
                  Direction   = 315,
                  Color       = Colors.Black,
                  Opacity     = 0.5,
              }
            : null;
        shadowEffect?.Freeze();

        var bgBrush     = new SolidColorBrush(fillColor);   bgBrush.Freeze();
        var borderBrush = new SolidColorBrush(borderColor); borderBrush.Freeze();

        var border = new Border
        {
            Width           = r.Width,
            Height          = r.Height,
            Background      = bgBrush,
            BorderBrush     = borderBrush,
            BorderThickness = new Thickness(borderThickness),
            CornerRadius    = new CornerRadius(cornerRadius),
            Opacity         = opacity / 100.0,
            Effect          = shadowEffect,
        };
        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border, r.Y);
        return border;
    }

    private UIElement MakeHighlighter(Point start, Point end, Color fillColor, double shadow)
    {
        var r = NormRect(start, end);
        int x = (int)Math.Max(0, r.X);
        int y = (int)Math.Max(0, r.Y);
        int w = (int)Math.Min(r.Width,  _bitmapSource.PixelWidth  - x);
        int h = (int)Math.Min(r.Height, _bitmapSource.PixelHeight - y);
        if (w <= 0 || h <= 0)
        {
            var empty = new Border { Width = Math.Max(r.Width, 1), Height = Math.Max(r.Height, 1) };
            Canvas.SetLeft(empty, r.X); Canvas.SetTop(empty, r.Y);
            return empty;
        }

        var crop = new CroppedBitmap(_bitmapSource, new Int32Rect(x, y, w, h));
        var bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);

        int stride = w * 4;
        var pixels = new byte[h * stride];
        bgra.CopyPixels(pixels, stride, 0);

        // Multiply blend: result = background * highlight (dark areas stay dark)
        float fr = fillColor.R / 255f;
        float fg = fillColor.G / 255f;
        float fb = fillColor.B / 255f;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i]     = (byte)(pixels[i]     * fb); // B
            pixels[i + 1] = (byte)(pixels[i + 1] * fg); // G
            pixels[i + 2] = (byte)(pixels[i + 2] * fr); // R
        }

        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);

        var img = new System.Windows.Controls.Image
        {
            Source  = wb,
            Width   = w,
            Height  = h,
            Stretch = Stretch.None,
            Effect  = shadow > 0
                ? new System.Windows.Media.Effects.DropShadowEffect
                  {
                      BlurRadius  = shadow,
                      ShadowDepth = 0,
                      Direction   = 315,
                      Color       = Colors.Black,
                      Opacity     = 0.5,
                  }
                : null,
        };
        Canvas.SetLeft(img, x);
        Canvas.SetTop(img, y);
        return img;
    }

    private Rectangle MakeCropPreview(Point start, Point end)
    {
        var r = NormRect(start, end);
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(200, 137, 180, 250)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection([6, 3]),
            Fill = new SolidColorBrush(Color.FromArgb(30, 137, 180, 250)),
            Width = Math.Max(r.Width, 0),
            Height = Math.Max(r.Height, 0),
        };
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect, r.Y);
        return rect;
    }

    private void PlaceTextBox(Point position)
    {
        var tb = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 137, 180, 250)),
            Foreground = new SolidColorBrush(_currentColor),
            FontSize = 16 + _currentThickness * 2,
            FontFamily = new FontFamily("Segoe UI"),
            MinWidth = 80,
            CaretBrush = new SolidColorBrush(_currentColor),
        };

        Canvas.SetLeft(tb, position.X);
        Canvas.SetTop(tb, position.Y);
        AnnotationCanvas.Children.Add(tb);
        var ann = new Annotation { Tool = "Text", Element = tb };
        _annotations.Add(ann);
        tb.Focus();

        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    AnnotationCanvas.Children.Remove(tb);
                    _annotations.Remove(ann);
                }
                else
                {
                    tb.IsReadOnly = true;
                    tb.BorderThickness = new Thickness(0);
                    tb.Focusable = false;
                }
            }
        };
    }

    // ── Crop ──────────────────────────────────────────────────────────────────

    private void RemoveCropPreview()
    {
        if (_cropBorder != null)
        {
            AnnotationCanvas.Children.Remove(_cropBorder);
            _cropBorder = null;
        }
    }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_cropBorder == null || _cropRect.Width < 4 || _cropRect.Height < 4) return;

        int x = Math.Max(0, (int)_cropRect.X);
        int y = Math.Max(0, (int)_cropRect.Y);
        int w = Math.Min((int)_cropRect.Width,  _bitmapSource.PixelWidth  - x);
        int h = Math.Min((int)_cropRect.Height, _bitmapSource.PixelHeight - y);

        if (w <= 0 || h <= 0) return;

        _bitmapSource = new CroppedBitmap(_bitmapSource, new Int32Rect(x, y, w, h));
        MainImage.Source = _bitmapSource;
        AnnotationCanvas.Width  = w;
        AnnotationCanvas.Height = h;

        DeselectAnnotation();
        AnnotationCanvas.Children.Clear();
        _annotations.Clear();
        _cropBorder = null;

        BtnApplyCrop.Visibility = Visibility.Collapsed;
        BtnArrow.IsChecked = true;
    }

    // ── Undo ──────────────────────────────────────────────────────────────────

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_annotations.Count == 0) return;
        var last = _annotations[^1];
        if (ReferenceEquals(last, _selectedAnnotation)) DeselectAnnotation();
        _annotations.RemoveAt(_annotations.Count - 1);
        AnnotationCanvas.Children.Remove(last.Element);
    }

    // ── Save / Copy ───────────────────────────────────────────────────────────

    private RenderTargetBitmap RenderToRtb()
    {
        int w = _bitmapSource.PixelWidth;
        int h = _bitmapSource.PixelHeight;

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

        var imgVisual = new DrawingVisual();
        using (var dc = imgVisual.RenderOpen())
            dc.DrawImage(_bitmapSource, new Rect(0, 0, w, h));
        rtb.Render(imgVisual);

        // Hide handles before rendering, then restore
        var handles = _selectionHandles.ToList();
        foreach (var handle in handles) AnnotationCanvas.Children.Remove(handle);
        rtb.Render(AnnotationCanvas);
        foreach (var handle in handles) AnnotationCanvas.Children.Add(handle);

        return rtb;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Screenshot",
            Filter = "PNG image|*.png|JPEG image|*.jpg;*.jpeg",
            FilterIndex = 1,
            FileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
        };
        if (dlg.ShowDialog() != true) return;

        var rtb = RenderToRtb();
        BitmapEncoder encoder = dlg.FilterIndex == 2
            ? new JpegBitmapEncoder { QualityLevel = 95 }
            : new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(dlg.FileName);
        encoder.Save(fs);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var rtb = RenderToRtb();
        Clipboard.SetImage(rtb);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Rect NormRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
}
