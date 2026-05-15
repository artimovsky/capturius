using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Capturius.Services;

namespace Capturius.Windows;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public System.Drawing.Bitmap? CapturedBitmap { get; private set; }

    private double _dpiScale;
    private double _logicalW, _logicalH;
    private double _maxLogX, _maxLogY; // last physical pixel expressed in logical units
    private System.Drawing.Bitmap _screenshotBitmap = null!;
    private BitmapSource _screenshotSource = null!;

    private enum State { WaitingFirst, WaitingSecond }
    private State _state = State.WaitingFirst;
    private Point _firstPoint;
    private Point _cursor;
    private Point _lastPhysCursor;

    // Custom crosshair (system cursor hidden) — double: black shadow + white line
    private Line _crossH = null!, _crossV = null!;
    private Line _crossHShadow = null!, _crossVShadow = null!;

    // First-point marker
    private UIElement? _marker1H, _marker1V, _marker1Dot;

    public OverlayWindow()
    {
        InitializeComponent();

        _dpiScale = ScreenCaptureService.GetDpiScale();
        var (physW, physH) = ScreenCaptureService.GetPhysicalScreenSize();
        _logicalW = physW / _dpiScale;
        _logicalH = physH / _dpiScale;

        Left = 0;
        Top = 0;
        Width = _logicalW;
        Height = _logicalH;

        _screenshotBitmap = ScreenCaptureService.CaptureFullScreen();
        _screenshotSource = ScreenCaptureService.ToBitmapSource(_screenshotBitmap);

        _maxLogX = (_screenshotBitmap.Width  - 1) / _dpiScale;
        _maxLogY = (_screenshotBitmap.Height - 1) / _dpiScale;

        ScreenshotImage.Source = _screenshotSource;
        SetDarkOverlay(new System.Windows.Rect(0, 0, _logicalW, _logicalH));

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitCrosshair();
        Focus();
        CompositionTarget.Rendering += OnRendering;
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        base.OnClosed(e);
    }

    // Poll real cursor position every frame — catches cursor stuck at screen edge
    // where MouseMove stops firing (cursor physically not moving).
    private void OnRendering(object? sender, EventArgs e)
    {
        GetCursorPos(out var pt);

        // Only update _cursor when the physical mouse actually moved.
        // Otherwise arrow-key moves get overwritten back to the mouse position.
        if (Math.Abs(pt.X - _lastPhysCursor.X) < 0.5 && Math.Abs(pt.Y - _lastPhysCursor.Y) < 0.5)
            return;

        _lastPhysCursor = new Point(pt.X, pt.Y);
        _cursor = new Point(
            Math.Clamp(pt.X / _dpiScale, 0, _maxLogX),
            Math.Clamp(pt.Y / _dpiScale, 0, _maxLogY));

        MoveCrosshair(_cursor);
        UpdateMagnifier(_cursor);
        if (_state == State.WaitingSecond)
            UpdateSelectionPreview(_firstPoint, _cursor);
    }

    // ── Crosshair ─────────────────────────────────────────────────────────────

    private void InitCrosshair()
    {
        var black = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        var white = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        // Dark shadow (thicker) — visible on light backgrounds
        _crossHShadow = new Line { X1 = 0, X2 = _logicalW, Stroke = black, StrokeThickness = 3, IsHitTestVisible = false };
        _crossVShadow = new Line { Y1 = 0, Y2 = _logicalH, Stroke = black, StrokeThickness = 3, IsHitTestVisible = false };
        // White line on top — visible on dark backgrounds
        _crossH = new Line { X1 = 0, X2 = _logicalW, Stroke = white, StrokeThickness = 1, IsHitTestVisible = false };
        _crossV = new Line { Y1 = 0, Y2 = _logicalH, Stroke = white, StrokeThickness = 1, IsHitTestVisible = false };

        OverlayCanvas.Children.Add(_crossHShadow);
        OverlayCanvas.Children.Add(_crossVShadow);
        OverlayCanvas.Children.Add(_crossH);
        OverlayCanvas.Children.Add(_crossV);
    }

    private void MoveCrosshair(Point pos)
    {
        _crossH.Y1       = _crossH.Y2       = pos.Y;
        _crossV.X1       = _crossV.X2       = pos.X;
        _crossHShadow.Y1 = _crossHShadow.Y2 = pos.Y;
        _crossVShadow.X1 = _crossVShadow.X2 = pos.X;
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.Enter:
                if (_state == State.WaitingFirst)
                    CommitFirstPoint(_cursor);
                else
                    TryConfirm(_cursor);
                break;

            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                int step = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
                int px = (int)Math.Round(_cursor.X * _dpiScale);
                int py = (int)Math.Round(_cursor.Y * _dpiScale);
                if (key == Key.Left)  px -= step;
                if (key == Key.Right) px += step;
                if (key == Key.Up)    py -= step;
                if (key == Key.Down)  py += step;
                px = Math.Clamp(px, 0, _screenshotBitmap.Width  - 1);
                py = Math.Clamp(py, 0, _screenshotBitmap.Height - 1);
                _cursor = new Point(px / _dpiScale, py / _dpiScale);

                MoveCrosshair(_cursor);
                UpdateMagnifier(_cursor);
                if (_state == State.WaitingSecond)
                    UpdateSelectionPreview(_firstPoint, _cursor);

                e.Handled = true;
                break;
        }
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(OverlayCanvas);
        int px = Math.Clamp((int)Math.Round(pos.X * _dpiScale), 0, _screenshotBitmap.Width  - 1);
        int py = Math.Clamp((int)Math.Round(pos.Y * _dpiScale), 0, _screenshotBitmap.Height - 1);
        _cursor = new Point(px / _dpiScale, py / _dpiScale);

        if (_state == State.WaitingFirst)
            CommitFirstPoint(_cursor);
        else
            TryConfirm(_cursor);
    }

    private void CommitFirstPoint(Point p)
    {
        _firstPoint = p;
        _state = State.WaitingSecond;
        ShowFirstPointMarker(p);
        HintText.Text = "Click / Enter — second point  •  Arrows — fine-tune  •  ESC — cancel";
        SizeText.Visibility = Visibility.Visible;
    }

    private void TryConfirm(Point second)
    {
        var (_, phys) = GetRects(_firstPoint, second);
        if (phys.Width > 4 && phys.Height > 4)
        {
            int x = Math.Max(0, (int)phys.X);
            int y = Math.Max(0, (int)phys.Y);
            int w = Math.Min((int)phys.Width,  _screenshotBitmap.Width  - x);
            int h = Math.Min((int)phys.Height, _screenshotBitmap.Height - y);

            CapturedBitmap = _screenshotBitmap.Clone(
                new System.Drawing.Rectangle(x, y, w, h),
                _screenshotBitmap.PixelFormat);

            _screenshotBitmap.Dispose();
            Close();
        }
    }

    // ── Visual updates ────────────────────────────────────────────────────────

    private void ShowFirstPointMarker(Point p)
    {
        if (_marker1H   != null) OverlayCanvas.Children.Remove(_marker1H);
        if (_marker1V   != null) OverlayCanvas.Children.Remove(_marker1V);
        if (_marker1Dot != null) OverlayCanvas.Children.Remove(_marker1Dot);

        var brush = new SolidColorBrush(Color.FromRgb(137, 180, 250));

        var h   = new Line { X1 = p.X - 10, Y1 = p.Y, X2 = p.X + 10, Y2 = p.Y, Stroke = brush, StrokeThickness = 1.5 };
        var v   = new Line { X1 = p.X, Y1 = p.Y - 10, X2 = p.X, Y2 = p.Y + 10, Stroke = brush, StrokeThickness = 1.5 };
        var dot = new Ellipse { Width = 5, Height = 5, Fill = brush };
        Canvas.SetLeft(dot, p.X - 2.5);
        Canvas.SetTop(dot,  p.Y - 2.5);

        OverlayCanvas.Children.Add(h);
        OverlayCanvas.Children.Add(v);
        OverlayCanvas.Children.Add(dot);
        _marker1H = h; _marker1V = v; _marker1Dot = dot;
    }

    private void UpdateSelectionPreview(Point a, Point b)
    {
        var (rect, physRect) = GetRects(a, b);

        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width  = rect.Width;
        SelectionBorder.Height = rect.Height;

        SetDarkOverlay(rect);
        SizeText.Text = $"{(int)physRect.Width} × {(int)physRect.Height} px";
    }

    private void UpdateMagnifier(Point logPos)
    {
        int physX = (int)(logPos.X * _dpiScale);
        int physY = (int)(logPos.Y * _dpiScale);
        int pw = _screenshotSource.PixelWidth;
        int ph = _screenshotSource.PixelHeight;

        const int cropSize = 80;
        const double displaySize = 200.0;
        double scale = displaySize / cropSize;

        // Crop without clamping right/bottom — take as many pixels as available
        int cropX = Math.Max(0, physX - cropSize / 2);
        int cropY = Math.Max(0, physY - cropSize / 2);
        int cropW = Math.Min(cropSize, pw - cropX);
        int cropH = Math.Min(cropSize, ph - cropY);

        if (cropW <= 0 || cropH <= 0) return;

        // Position image so the cursor point is always at center (100,100)
        double imgLeft = 100 - (physX - cropX) * scale;
        double imgTop  = 100 - (physY - cropY) * scale;

        MagnifierImage.Width  = cropW * scale;
        MagnifierImage.Height = cropH * scale;
        Canvas.SetLeft(MagnifierImage, imgLeft);
        Canvas.SetTop(MagnifierImage,  imgTop);
        MagnifierImage.Source = new CroppedBitmap(_screenshotSource,
            new Int32Rect(cropX, cropY, cropW, cropH));

        MagnifierCoords.Text = $"{physX}, {physY}";

        double magW = displaySize, magH = displaySize, margin = 20;
        bool cursorLeft = logPos.X < _logicalW / 2;
        bool cursorTop  = logPos.Y < _logicalH / 2;

        Canvas.SetLeft(MagnifierPanel, cursorLeft ? _logicalW - magW - margin : margin);
        Canvas.SetTop(MagnifierPanel,  cursorTop  ? _logicalH - magH - margin : margin);
        MagnifierPanel.Visibility = Visibility.Visible;
    }

    private void SetDarkOverlay(System.Windows.Rect selection)
    {
        var outer = new RectangleGeometry(new System.Windows.Rect(0, 0, _logicalW, _logicalH));

        if (selection.Width > 2 && selection.Height > 2)
        {
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(outer);
            group.Children.Add(new RectangleGeometry(selection));
            DarkOverlay.Data = group;
        }
        else
        {
            DarkOverlay.Data = outer;
        }
    }

    private (System.Windows.Rect logical, System.Windows.Rect physical) GetRects(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(b.X - a.X),  h = Math.Abs(b.Y - a.Y);
        return (new System.Windows.Rect(x, y, w, h),
                new System.Windows.Rect(
                    Math.Round(x * _dpiScale), Math.Round(y * _dpiScale),
                    Math.Round(w * _dpiScale), Math.Round(h * _dpiScale)));
    }
}
