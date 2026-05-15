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
    private BitmapSource _bitmapSource;
    private string _currentTool = "Arrow";
    private Color _currentColor = Colors.Red;
    private double _currentThickness = 1;
    private double _zoomLevel = 1.0;
    private readonly double _dpiScale = ScreenCaptureService.GetDpiScale();

    private Point _drawStart;
    private bool _isDrawing;

    // Temporary shape shown while dragging
    private UIElement? _previewElement;
    // All committed annotation elements (for undo)
    private readonly List<UIElement> _annotations = new();

    // Crop selection state
    private System.Windows.Rect _cropRect;
    private Rectangle? _cropBorder;

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
        AnnotationCanvas.Width = bitmapSource.PixelWidth;
        AnnotationCanvas.Height = bitmapSource.PixelHeight;

        BuildColorPicker();
        SetZoom(1.0);
    }

    // ── Color picker ──────────────────────────────────────────────────────────

    private void BuildColorPicker()
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
                BorderBrush = color == _currentColor
                    ? new SolidColorBrush(Colors.White)
                    : Brushes.Transparent,
                Tag = color,
            };
            btn.MouseLeftButtonDown += ColorBtn_Click;
            ColorPicker.Items.Add(btn);
        }
    }

    private void ColorBtn_Click(object sender, MouseButtonEventArgs e)
    {
        _currentColor = (Color)((Border)sender).Tag;

        foreach (Border b in ColorPicker.Items)
        {
            b.BorderBrush = (Color)b.Tag == _currentColor
                ? Brushes.White
                : Brushes.Transparent;
        }
    }

    // ── Toolbar handlers ──────────────────────────────────────────────────────

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (BtnApplyCrop == null) return; // not yet initialized

        _currentTool = ((ToggleButton)sender).Tag!.ToString()!;
        UncheckOtherTools((ToggleButton)sender);

        bool isCrop = _currentTool == "Crop";
        BtnApplyCrop.Visibility = isCrop ? Visibility.Visible : Visibility.Collapsed;

        if (!isCrop) RemoveCropPreview();
        AnnotationCanvas.Cursor = _currentTool == "Text" ? Cursors.IBeam : Cursors.Cross;
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

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomLevel * 1.25);
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
        // Divide by dpiScale: 100% = 1 physical pixel = 1 screen pixel
        ZoomTransform.ScaleX = _zoomLevel / _dpiScale;
        ZoomTransform.ScaleY = _zoomLevel / _dpiScale;
        ZoomLabel.Text = $"{(int)Math.Round(_zoomLevel * 100)}%";
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _drawStart = e.GetPosition(AnnotationCanvas);
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
            "Arrow" => MakeArrow(_drawStart, cur, _currentColor, _currentThickness),
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

        var el = _currentTool switch
        {
            "Arrow" => MakeArrow(_drawStart, cur, _currentColor, _currentThickness),
            "Rect"  => MakeRect(_drawStart, cur, _currentColor, _currentThickness),
            _       => null
        };

        if (el != null)
        {
            AnnotationCanvas.Children.Add(el);
            _annotations.Add(el);
        }
    }

    // ── Shape factories ───────────────────────────────────────────────────────

    private static UIElement MakeArrow(Point start, Point end, Color color, double thickness)
    {
        var dir = end - start;
        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 2) return new Path();

        dir /= len;
        var perp = new Vector(-dir.Y, dir.X);
        double aLen = Math.Min(16 + thickness * 2, len * 0.4);
        double aWid = aLen * 0.45;

        var p1 = end - dir * aLen + perp * aWid;
        var p2 = end - dir * aLen - perp * aWid;

        var fig = new PathFigure { StartPoint = end, IsClosed = true };
        fig.Segments.Add(new LineSegment(p1, true));
        fig.Segments.Add(new LineSegment(p2, true));
        var arrowHead = new PathGeometry();
        arrowHead.Figures.Add(fig);

        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(start, end));
        group.Children.Add(arrowHead);

        return new Path
        {
            Data = group,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            Fill = new SolidColorBrush(color),
        };
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
        _annotations.Add(tb);
        tb.Focus();

        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    AnnotationCanvas.Children.Remove(tb);
                    _annotations.Remove(tb);
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
        int w = Math.Min((int)_cropRect.Width, _bitmapSource.PixelWidth - x);
        int h = Math.Min((int)_cropRect.Height, _bitmapSource.PixelHeight - y);

        if (w <= 0 || h <= 0) return;

        _bitmapSource = new CroppedBitmap(_bitmapSource, new Int32Rect(x, y, w, h));
        MainImage.Source = _bitmapSource;
        AnnotationCanvas.Width = w;
        AnnotationCanvas.Height = h;

        // Clear all annotations after crop
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
        _annotations.RemoveAt(_annotations.Count - 1);
        AnnotationCanvas.Children.Remove(last);
    }

    // ── Save / Copy ───────────────────────────────────────────────────────────

    private RenderTargetBitmap RenderToRtb()
    {
        int w = _bitmapSource.PixelWidth;
        int h = _bitmapSource.PixelHeight;

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

        // Render source image directly — bypasses Margin/ScaleTransform/ScrollViewer
        var imgVisual = new DrawingVisual();
        using (var dc = imgVisual.RenderOpen())
            dc.DrawImage(_bitmapSource, new Rect(0, 0, w, h));
        rtb.Render(imgVisual);

        // Render annotation canvas on top (Width/Height = PixelWidth/PixelHeight)
        rtb.Render(AnnotationCanvas);

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
