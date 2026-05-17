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
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    private BitmapSource _bitmapSource;
    private string _currentTool = "Arrow";
    private Color  _currentColor = Colors.Red;
    private double _currentThickness = 1;
    private double _zoomLevel = 1.0;
    private readonly double _dpiScale = ScreenCaptureService.GetDpiScale();

    private Point _drawStart;
    private bool  _isDrawing;

    // Arrow tool state
    private Color  _arrowFillColor       = Colors.Red;
    private Color  _arrowStrokeColor     = Colors.Black;
    private double _arrowThickness       = 2;
    private double _arrowStrokeThickness = 0;
    private double _arrowShadow          = 0;

    // Temporary shape shown while dragging
    private UIElement? _previewElement;
    // All committed annotations
    private readonly List<Annotation> _annotations = new();

    // Selection state
    private Annotation?         _selectedAnnotation;
    private readonly List<UIElement> _selectionHandles = new();

    // Crop selection state
    private System.Windows.Rect _cropRect;
    private Rectangle?          _cropBorder;

    private static readonly (Color Color, string Name)[] ColorPresets =
    [
        (Colors.Red,    "Red"),
        (Colors.Blue,   "Blue"),
        (Colors.Green,  "Green"),
        (Colors.Yellow, "Yellow"),
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
        BuildColorPicker();
        BuildArrowColorPickers();
        SetZoom(1.0);

        Loaded += (_, _) => ApplyArrowRadioSelections();

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

    private void BuildColorPickerItems(ItemsControl control, Func<Color> getColor, Action<Color> onSelect)
    {
        foreach (var (color, name) in ColorPresets)
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

        bool isCrop  = _currentTool == "Crop";
        bool isArrow = _currentTool == "Arrow";

        BtnApplyCrop.Visibility     = isCrop  ? Visibility.Visible   : Visibility.Collapsed;
        GlobalProperties.Visibility = isArrow ? Visibility.Collapsed : Visibility.Visible;
        ArrowProperties.Visibility  = isArrow ? Visibility.Visible   : Visibility.Collapsed;

        if (!isCrop) RemoveCropPreview();
        AnnotationCanvas.Cursor = _currentTool == "Text" ? Cursors.IBeam : Cursors.Cross;

        DeselectAnnotation();
    }

    private void UncheckOtherTools(ToggleButton active)
    {
        foreach (var btn in new[] { BtnArrow, BtnRect, BtnText, BtnCrop })
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

    private void ApplyArrowRadioSelections()
    {
        SelectRadio("ArrowThickness",       (int)_arrowThickness);
        SelectRadio("ArrowStrokeThickness", (int)_arrowStrokeThickness);
        SelectRadio("ArrowShadow",          (int)_arrowShadow);
    }

    private void SelectRadio(string groupName, int value)
    {
        string tag = value.ToString();
        foreach (UIElement child in ArrowProperties.Children)
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

        // In Arrow mode — try to select an existing arrow first
        if (_currentTool == "Arrow")
        {
            var hit = HitTestArrow(pos);
            if (hit != null)
            {
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
        if (!_isDrawing) return;
        var cur = e.GetPosition(AnnotationCanvas);

        if (_previewElement != null)
            AnnotationCanvas.Children.Remove(_previewElement);

        _previewElement = _currentTool switch
        {
            "Arrow" => MakeArrow(_drawStart, cur, _arrowFillColor, _arrowStrokeColor, _arrowThickness, _arrowStrokeThickness, _arrowShadow),
            "Rect"  => MakeRect(_drawStart, cur, _currentColor, _currentThickness),
            "Crop"  => MakeCropPreview(_drawStart, cur),
            _       => null
        };

        if (_previewElement != null)
            AnnotationCanvas.Children.Add(_previewElement);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
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

        var other = _currentTool switch
        {
            "Rect" => MakeRect(_drawStart, cur, _currentColor, _currentThickness),
            _      => null
        };
        if (other != null)
        {
            AnnotationCanvas.Children.Add(other);
            _annotations.Add(new Annotation { Tool = _currentTool, Element = other });
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    private Annotation? HitTestArrow(Point pos)
    {
        Annotation? found = null;
        VisualTreeHelper.HitTest(
            AnnotationCanvas,
            null,
            result =>
            {
                var ann = FindAnnotationByVisual(result.VisualHit);
                if (ann?.Tool == "Arrow") { found = ann; return HitTestResultBehavior.Stop; }
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

        _arrowFillColor       = ann.FillColor;
        _arrowStrokeColor     = ann.StrokeColor;
        _arrowThickness       = ann.Thickness;
        _arrowStrokeThickness = ann.StrokeThickness;
        _arrowShadow          = ann.Shadow;

        UpdatePickerSelection(ArrowFillPicker,   ann.FillColor);
        UpdatePickerSelection(ArrowStrokePicker, ann.StrokeColor);
        ApplyArrowRadioSelections();

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
        foreach (var pt in new[] { ann.Start, ann.End })
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
        if (_selectedAnnotation?.Tool != "Arrow") return;
        var ann = _selectedAnnotation;

        ann.FillColor       = _arrowFillColor;
        ann.StrokeColor     = _arrowStrokeColor;
        ann.Thickness       = _arrowThickness;
        ann.StrokeThickness = _arrowStrokeThickness;
        ann.Shadow          = _arrowShadow;

        int idx = AnnotationCanvas.Children.IndexOf(ann.Element);
        AnnotationCanvas.Children.Remove(ann.Element);

        var newEl = MakeArrow(ann.Start, ann.End, ann.FillColor, ann.StrokeColor, ann.Thickness, ann.StrokeThickness, ann.Shadow);
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

        System.Windows.Media.Effects.DropShadowEffect? shadowEffect = shadow > 0
            ? new System.Windows.Media.Effects.DropShadowEffect
              {
                  BlurRadius  = shadow,
                  ShadowDepth = shadow * 0.4,
                  Direction   = 315,
                  Color       = Colors.Black,
                  Opacity     = 0.5,
              }
            : null;

        if (strokeThickness == 0)
            return new Path { Data = geo, Fill = new SolidColorBrush(fillColor), Effect = shadowEffect };

        var bgPath = new Path
        {
            Data            = geo,
            Fill            = new SolidColorBrush(strokeColor),
            Stroke          = new SolidColorBrush(strokeColor),
            StrokeThickness = strokeThickness * 2,
            StrokeLineJoin  = PenLineJoin.Round,
        };
        var fgPath = new Path { Data = geo, Fill = new SolidColorBrush(fillColor) };
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

    private static UIElement MakeRect(Point start, Point end, Color color, double thickness)
    {
        var r = NormRect(start, end);
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            Fill = Brushes.Transparent,
            Width = r.Width,
            Height = r.Height,
        };
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect, r.Y);
        return rect;
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
