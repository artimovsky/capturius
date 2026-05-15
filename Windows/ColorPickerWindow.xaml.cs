using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Capturius.Services;

namespace Capturius.Windows;

public partial class ColorPickerWindow : Window
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public string? PickedHex { get; private set; }

    private double _dpiScale;
    private double _logicalW, _logicalH;
    private double _maxLogX, _maxLogY;
    private System.Drawing.Bitmap _screenshotBitmap = null!;
    private BitmapSource _screenshotSource = null!;

    private Point _cursor;
    private Point _lastPhysCursor;

    private Line _crossH = null!, _crossV = null!;
    private Line _crossHShadow = null!, _crossVShadow = null!;

    public ColorPickerWindow()
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

        Loaded  += OnLoaded;
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
        _screenshotBitmap.Dispose();
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

        MoveCrosshair(_cursor);
        UpdateMagnifier(_cursor);
    }

    private void InitCrosshair()
    {
        var black = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        var white = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        _crossHShadow = new Line { X1 = 0, X2 = _logicalW, Stroke = black, StrokeThickness = 3, IsHitTestVisible = false };
        _crossVShadow = new Line { Y1 = 0, Y2 = _logicalH, Stroke = black, StrokeThickness = 3, IsHitTestVisible = false };
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

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key == Key.System ? e.SystemKey : e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter:  PickColor(); break;
            case Key.Left:   MoveByKey(-1,  0, e); break;
            case Key.Right:  MoveByKey( 1,  0, e); break;
            case Key.Up:     MoveByKey( 0, -1, e); break;
            case Key.Down:   MoveByKey( 0,  1, e); break;
        }
    }

    private void MoveByKey(int dx, int dy, KeyEventArgs e)
    {
        int step = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        int px = (int)Math.Round(_cursor.X * _dpiScale) + dx * step;
        int py = (int)Math.Round(_cursor.Y * _dpiScale) + dy * step;
        px = Math.Clamp(px, 0, _screenshotBitmap.Width  - 1);
        py = Math.Clamp(py, 0, _screenshotBitmap.Height - 1);
        _cursor = new Point(px / _dpiScale, py / _dpiScale);
        MoveCrosshair(_cursor);
        UpdateMagnifier(_cursor);
        e.Handled = true;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _cursor = e.GetPosition(OverlayCanvas);
        PickColor();
    }

    private void PickColor()
    {
        int physX = Math.Clamp((int)(_cursor.X * _dpiScale), 0, _screenshotBitmap.Width  - 1);
        int physY = Math.Clamp((int)(_cursor.Y * _dpiScale), 0, _screenshotBitmap.Height - 1);
        var c = _screenshotBitmap.GetPixel(physX, physY);
        PickedHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        System.Windows.Clipboard.SetText(PickedHex);
        Close();
    }

    private void UpdateMagnifier(Point logPos)
    {
        int physX = (int)(logPos.X * _dpiScale);
        int physY = (int)(logPos.Y * _dpiScale);
        int pw = _screenshotSource.PixelWidth;
        int ph = _screenshotSource.PixelHeight;

        const int cropSize    = 20;
        const double dispSize = 200.0;
        double scale = dispSize / cropSize;

        int cropX = Math.Max(0, physX - cropSize / 2);
        int cropY = Math.Max(0, physY - cropSize / 2);
        int cropW = Math.Min(cropSize, pw - cropX);
        int cropH = Math.Min(cropSize, ph - cropY);
        if (cropW <= 0 || cropH <= 0) return;

        MagnifierImage.Width  = cropW * scale;
        MagnifierImage.Height = cropH * scale;
        Canvas.SetLeft(MagnifierImage, 100 - (physX - cropX) * scale - scale / 2.0);
        Canvas.SetTop(MagnifierImage,  100 - (physY - cropY) * scale - scale / 2.0);
        MagnifierImage.Source = new CroppedBitmap(_screenshotSource,
            new Int32Rect(cropX, cropY, cropW, cropH));

        int cx = Math.Clamp(physX, 0, _screenshotBitmap.Width  - 1);
        int cy = Math.Clamp(physY, 0, _screenshotBitmap.Height - 1);
        var c = _screenshotBitmap.GetPixel(cx, cy);
        HexText.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        MagnifierCoords.Text = $"{physX}, {physY}";

        double panelH = MagnifierPanel.ActualHeight > 0 ? MagnifierPanel.ActualHeight : 260;
        double margin = 20;
        bool right  = logPos.X < _logicalW / 2;
        bool bottom = logPos.Y < _logicalH / 2;
        Canvas.SetLeft(MagnifierPanel, right  ? _logicalW - 200 - margin : margin);
        Canvas.SetTop(MagnifierPanel,  bottom ? _logicalH - panelH - margin : margin);
        MagnifierPanel.Visibility = Visibility.Visible;
    }
}
