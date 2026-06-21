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
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr RealChildWindowFromPoint(IntPtr hwndParent, POINT pt);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT  { public int Left, Top, Right, Bottom; }

    private const uint GW_HWNDNEXT = 2;

    public System.Drawing.Bitmap? CapturedBitmap { get; private set; }
    public System.Drawing.Rectangle CapturedRegion { get; private set; }

    private readonly bool   _snapMode;
    private readonly double _dpiScale;
    private readonly double _logicalW, _logicalH;
    private readonly double _maxLogX, _maxLogY;
    private System.Drawing.Bitmap _screenshotBitmap = null!;
    private BitmapSource _screenshotSource = null!;

    // Region-mode state
    private enum State { WaitingFirst, WaitingSecond }
    private State _state = State.WaitingFirst;
    private Point _firstPoint;
    private Point _cursor;
    private Point _lastPhysCursor;
    private Point _mouseDownPoint;
    private bool  _isDragging;

    // Region-mode crosshair
    private Line _crossH = null!, _crossV = null!;
    private Line _crossHShadow = null!, _crossVShadow = null!;
    private UIElement? _marker1H, _marker1V, _marker1Dot;

    // Snap-mode state
    private System.Drawing.Rectangle? _snapPhysRect;

    public OverlayWindow(bool snapMode = false)
    {
        InitializeComponent();

        _snapMode = snapMode;
        _dpiScale = ScreenCaptureService.GetDpiScale();
        var (physW, physH) = ScreenCaptureService.GetPhysicalScreenSize();
        _logicalW = physW / _dpiScale;
        _logicalH = physH / _dpiScale;

        Left = 0; Top = 0;
        Width = _logicalW; Height = _logicalH;

        _screenshotBitmap = ScreenCaptureService.CaptureFullScreen();
        _screenshotSource = ScreenCaptureService.ToBitmapSource(_screenshotBitmap);

        _maxLogX = (_screenshotBitmap.Width  - 1) / _dpiScale;
        _maxLogY = (_screenshotBitmap.Height - 1) / _dpiScale;

        ScreenshotImage.Source = _screenshotSource;
        DarkOverlay.Data = new RectangleGeometry(new Rect(0, 0, _logicalW, _logicalH));

        HintText.Text = snapMode
            ? "Click on a window  •  ESC — cancel"
            : "Click to set first point  •  ESC or Right Click — cancel";

        Loaded  += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_snapMode)
            InitCrosshair();
        Focus();
        CompositionTarget.Rendering += OnRendering;
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        base.OnClosed(e);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        GetCursorPos(out var pt);

        if (Math.Abs(pt.X - _lastPhysCursor.X) < 0.5 && Math.Abs(pt.Y - _lastPhysCursor.Y) < 0.5)
            return;

        _lastPhysCursor = new Point(pt.X, pt.Y);
        _cursor = new Point(
            Math.Clamp(pt.X / _dpiScale, 0, _maxLogX),
            Math.Clamp(pt.Y / _dpiScale, 0, _maxLogY));

        UpdateMagnifier(_cursor);

        if (_snapMode)
        {
            _snapPhysRect = DetectSnapWindow(pt.X, pt.Y);
            UpdateSnapBorder();
        }
        else
        {
            MoveCrosshair(_cursor);
            if (_state == State.WaitingSecond)
                UpdateSelectionPreview(_firstPoint, _cursor);
        }
    }

    // ── Crosshair (region mode) ───────────────────────────────────────────────

    private void InitCrosshair()
    {
        var black = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        var white = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        _crossHShadow = new Line { X1 = 0, X2 = _logicalW, Stroke = black, StrokeThickness = 3, IsHitTestVisible = false };
        _crossVShadow = new Line { Y1 = 0, Y2 = _logicalH, Stroke = black, StrokeThickness = 3, IsHitTestVisible = false };
        _crossH       = new Line { X1 = 0, X2 = _logicalW, Stroke = white, StrokeThickness = 1, IsHitTestVisible = false };
        _crossV       = new Line { Y1 = 0, Y2 = _logicalH, Stroke = white, StrokeThickness = 1, IsHitTestVisible = false };

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

        if (key == Key.Escape) { Close(); return; }

        if (_snapMode)
        {
            if (key == Key.Enter) SnapConfirm();
            return;
        }

        // Region mode
        switch (key)
        {
            case Key.Enter:
                if (_state == State.WaitingFirst) CommitFirstPoint(_cursor);
                else TryConfirm(_cursor);
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

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => Close();

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_snapMode) return;
        _mouseDownPoint = SnapPos(e.GetPosition(OverlayCanvas));
        _isDragging     = false;
        _cursor         = _mouseDownPoint;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_snapMode) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_isDragging || _state != State.WaitingFirst) return;

        var pos = SnapPos(e.GetPosition(OverlayCanvas));
        var d   = pos - _mouseDownPoint;
        if (Math.Sqrt(d.X * d.X + d.Y * d.Y) > 5)
        {
            _isDragging = true;
            CommitFirstPoint(_mouseDownPoint);
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_snapMode) { SnapConfirm(); return; }

        var pos = SnapPos(e.GetPosition(OverlayCanvas));
        _cursor = pos;

        if (_isDragging)
        {
            _isDragging = false;
            TryConfirm(pos);
            return;
        }

        if (_state == State.WaitingFirst) CommitFirstPoint(pos);
        else TryConfirm(pos);
    }

    // ── Region mode logic ─────────────────────────────────────────────────────

    private Point SnapPos(Point logPos)
    {
        int px = Math.Clamp((int)Math.Round(logPos.X * _dpiScale), 0, _screenshotBitmap.Width  - 1);
        int py = Math.Clamp((int)Math.Round(logPos.Y * _dpiScale), 0, _screenshotBitmap.Height - 1);
        return new Point(px / _dpiScale, py / _dpiScale);
    }

    private void CommitFirstPoint(Point p)
    {
        _firstPoint = p;
        _state = State.WaitingSecond;
        ShowFirstPointMarker(p);
        HintText.Text       = "Click / Enter — second point  •  Arrows — fine-tune  •  ESC or Right Click — cancel";
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
        Canvas.SetTop(SelectionBorder,  rect.Y);
        SelectionBorder.Width  = rect.Width;
        SelectionBorder.Height = rect.Height;

        SetDarkOverlay(rect);
        SizeText.Text = $"{(int)physRect.Width} × {(int)physRect.Height} px";
    }

    private void SetDarkOverlay(Rect selection)
    {
        var outer = new RectangleGeometry(new Rect(0, 0, _logicalW, _logicalH));
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

    private (Rect logical, Rect physical) GetRects(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(b.X - a.X),  h = Math.Abs(b.Y - a.Y);
        return (new Rect(x, y, w, h),
                new Rect(
                    Math.Round(x * _dpiScale), Math.Round(y * _dpiScale),
                    Math.Round(w * _dpiScale), Math.Round(h * _dpiScale)));
    }

    // ── Snap mode logic ───────────────────────────────────────────────────────

    private void SnapConfirm()
    {
        if (!_snapPhysRect.HasValue) return;
        var r = _snapPhysRect.Value;
        CapturedRegion = r;
        CapturedBitmap = _screenshotBitmap.Clone(r, _screenshotBitmap.PixelFormat);
        _screenshotBitmap.Dispose();
        Close();
    }

    private System.Drawing.Rectangle? DetectSnapWindow(int physX, int physY)
    {
        var ourHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var hwnd    = GetWindow(ourHwnd, GW_HWNDNEXT);
        while (hwnd != IntPtr.Zero)
        {
            if (IsWindowVisible(hwnd) && !IsIconic(hwnd) && GetWindowRect(hwnd, out RECT r))
            {
                if (r.Left <= physX && physX < r.Right && r.Top <= physY && physY < r.Bottom)
                {
                    RECT target = r;

                    var centerPt = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
                    ScreenToClient(hwnd, ref centerPt);
                    var child = RealChildWindowFromPoint(hwnd, centerPt);

                    if (child != IntPtr.Zero && child != hwnd && GetWindowRect(child, out RECT cr))
                    {
                        int childW = cr.Right  - cr.Left;
                        int childH = cr.Bottom - cr.Top;
                        bool smallerInAnyDimension = childW < r.Right - r.Left - 10
                                                  || childH < r.Bottom - r.Top - 10;
                        if (smallerInAnyDimension && childW > 200 && childH > 200)
                            target = cr;
                    }

                    int x = Math.Max(0, target.Left);
                    int y = Math.Max(0, target.Top);
                    int w = Math.Min(target.Right,  _screenshotBitmap.Width)  - x;
                    int h = Math.Min(target.Bottom, _screenshotBitmap.Height) - y;
                    if (w > 4 && h > 4)
                        return new System.Drawing.Rectangle(x, y, w, h);
                }
            }
            hwnd = GetWindow(hwnd, GW_HWNDNEXT);
        }
        return null;
    }

    private void UpdateSnapBorder()
    {
        var outer = new RectangleGeometry(new Rect(0, 0, _logicalW, _logicalH));

        if (_snapPhysRect.HasValue)
        {
            var r = _snapPhysRect.Value;
            double lx = r.X / _dpiScale,    ly = r.Y / _dpiScale;
            double lw = r.Width / _dpiScale, lh = r.Height / _dpiScale;

            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(outer);
            group.Children.Add(new RectangleGeometry(new Rect(lx, ly, lw, lh)));
            DarkOverlay.Data = group;

            SnapBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SnapBorder, lx);
            Canvas.SetTop(SnapBorder,  ly);
            SnapBorder.Width  = lw;
            SnapBorder.Height = lh;
        }
        else
        {
            DarkOverlay.Data      = outer;
            SnapBorder.Visibility = Visibility.Collapsed;
        }
    }

    // ── Magnifier ─────────────────────────────────────────────────────────────

    private void UpdateMagnifier(Point logPos)
    {
        int physX = (int)(logPos.X * _dpiScale);
        int physY = (int)(logPos.Y * _dpiScale);
        int pw = _screenshotSource.PixelWidth;
        int ph = _screenshotSource.PixelHeight;

        const int cropSize = 80;
        const double displaySize = 200.0;
        double scale = displaySize / cropSize;

        int cropX = Math.Max(0, physX - cropSize / 2);
        int cropY = Math.Max(0, physY - cropSize / 2);
        int cropW = Math.Min(cropSize, pw - cropX);
        int cropH = Math.Min(cropSize, ph - cropY);

        if (cropW <= 0 || cropH <= 0) return;

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
}
