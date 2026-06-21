using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Capturius.Services;

namespace Capturius.Windows;

public partial class ScrollCaptureWindow : Window
{
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr lpRect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int  WH_KEYBOARD_LL         = 13;
    private const int  WM_KEYDOWN             = 0x0100;
    private const uint WM_MOUSEWHEEL          = 0x020A;
    private const uint VK_RETURN              = 0x0D;
    private const uint VK_ESCAPE              = 0x1B;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const int  GWL_EXSTYLE            = -20;
    private const int  WS_EX_TRANSPARENT      = 0x00000020;
    private const int  WS_EX_LAYERED          = 0x00080000;
    private const int  StripH                 = 100; // used only for calibration

    public System.Drawing.Bitmap? CapturedBitmap { get; private set; }

    private readonly System.Drawing.Rectangle _region;
    private readonly double _dpiScale;
    private readonly List<System.Drawing.Bitmap> _strips = new();
    private int _totalHeight;

    private System.Drawing.Bitmap? _tailStrip;
    private double _pxPerDelta = 100.0;

    private int _stepPx;
    private int _stepDelta;
    private int _waitMs;

    private System.Drawing.Bitmap? _prevFrame;
    private IntPtr _targetHwnd;
    private bool   _cursorClipped;
    private Window? _regionBorder;

    private readonly DispatcherTimer _startDelay;
    private readonly DispatcherTimer _autoTimer;
    private bool _stopped;

    private LowLevelKeyboardProc _kbProc = null!;
    private IntPtr _kbHook;

    public ScrollCaptureWindow(System.Drawing.Rectangle region)
    {
        InitializeComponent();

        _region   = region;
        _dpiScale = ScreenCaptureService.GetDpiScale();

        _startDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _startDelay.Tick += (_, _) => { _startDelay.Stop(); StartCapture(); };

        _autoTimer = new DispatcherTimer();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var (physW, physH) = ScreenCaptureService.GetPhysicalScreenSize();
        double logW = physW / _dpiScale;
        double logH = physH / _dpiScale;
        Left = (logW - ActualWidth) / 2;
        Top  = logH - ActualHeight - 36;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

        var center = new POINT { X = _region.X + _region.Width / 2, Y = _region.Y + _region.Height / 2 };
        _targetHwnd = WindowFromPoint(center);

        ShowRegionBorder();

        _kbProc = KeyboardHook;
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, IntPtr.Zero, 0);

        // Move cursor onto the Stop panel and confine it there immediately.
        ConfineToPanel();

        _startDelay.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        UnhookWindowsHookEx(_kbHook);
        Cleanup(); // safety net: releases ClipCursor if window was closed without Finish/Cancel
        base.OnClosed(e);
    }

    // Confine cursor to this panel window so it can never enter the capture region.
    private void ConfineToPanel()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (!GetWindowRect(hwnd, out RECT r)) return;

        SetCursorPos((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        ClipCursor(ref r);
        _cursorClipped = true;
    }

    private void ReleaseClip()
    {
        if (!_cursorClipped) return;
        ClipCursor(IntPtr.Zero);
        _cursorClipped = false;
    }

    // 1px red border around the capture region — excluded from screenshots, click-through.
    private void ShowRegionBorder()
    {
        var border = new System.Windows.Controls.Border
        {
            BorderBrush     = System.Windows.Media.Brushes.Red,
            BorderThickness = new Thickness(1),
            Background      = System.Windows.Media.Brushes.Transparent,
        };

        _regionBorder = new Window
        {
            WindowStyle        = WindowStyle.None,
            AllowsTransparency = true,
            Background         = System.Windows.Media.Brushes.Transparent,
            Topmost            = true,
            ShowInTaskbar      = false,
            ResizeMode         = ResizeMode.NoResize,
            Content            = border,
            Left               = _region.X / _dpiScale,
            Top                = _region.Y / _dpiScale,
            Width              = _region.Width  / _dpiScale,
            Height             = _region.Height / _dpiScale,
        };

        _regionBorder.Loaded += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(_regionBorder).Handle;
            SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE);
            int exStyle = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        };

        _regionBorder.Show();
    }

    private System.Drawing.Bitmap CaptureFrame() =>
        ScreenCaptureService.CaptureRegion(_region);

    private void StartCapture()
    {
        var first = CaptureFrame();
        _strips.Add(first);
        _totalHeight = first.Height;
        _tailStrip   = ExtractTail(first);
        _prevFrame   = first.Clone(new System.Drawing.Rectangle(0, 0, first.Width, first.Height), first.PixelFormat);

        HeightText.Text = $"{_totalHeight} px";
        HintText.Text   = "Auto-scrolling...  •  Enter — done  •  ESC — cancel";

        SendScroll(-120);
        _autoTimer.Interval = TimeSpan.FromMilliseconds(600);
        _autoTimer.Tick += OnCalibFrame;
        _autoTimer.Start();
    }

    private void OnCalibFrame(object? sender, EventArgs e)
    {
        _autoTimer.Stop();
        _autoTimer.Tick -= OnCalibFrame;
        if (_stopped) return;

        var frame = CaptureFrame();

        int foundY = FindOverlapCalib(frame);
        if (foundY >= 0)
        {
            int actualPx = frame.Height - foundY - StripH;
            if (actualPx > 0)
            {
                _pxPerDelta = actualPx;
                System.Diagnostics.Debug.WriteLine($"Calibrated: pxPerDelta={_pxPerDelta:F1}");
            }
        }
        else System.Diagnostics.Debug.WriteLine($"Calibration skipped, using default {_pxPerDelta:F1}");

        AppendStrip(frame, (int)Math.Round(_pxPerDelta));

        _tailStrip?.Dispose();
        _tailStrip = null;

        _prevFrame?.Dispose();
        _prevFrame = frame;

        int notches = Math.Max(1, (int)Math.Round(_region.Height / 2.0 / _pxPerDelta));
        _stepDelta = -(notches * 120);
        _stepPx    = (int)Math.Round(notches * _pxPerDelta);
        _waitMs    = Math.Max(500, notches * 100);
        System.Diagnostics.Debug.WriteLine($"AutoScroll: notches={notches} stepPx={_stepPx} waitMs={_waitMs}");

        _autoTimer.Interval = TimeSpan.FromMilliseconds(_waitMs);
        _autoTimer.Tick += OnAutoFrame;
        SendScroll(_stepDelta);
        _autoTimer.Start();
    }

    private void OnAutoFrame(object? sender, EventArgs e)
    {
        _autoTimer.Stop();
        if (_stopped) return;

        var frame = CaptureFrame();

        if (_prevFrame != null && IsBottomReached(frame, _prevFrame))
        {
            System.Diagnostics.Debug.WriteLine("Auto-stop: bottom reached");
            frame.Dispose();
            Finish();
            return;
        }

        int newRows = _prevFrame != null ? FindActualNewRows(frame, _prevFrame) : _stepPx;
        AppendStrip(frame, newRows);

        _prevFrame?.Dispose();
        _prevFrame = frame;

        SendScroll(_stepDelta);
        _autoTimer.Start();
    }

    private int FindActualNewRows(System.Drawing.Bitmap frame, System.Drawing.Bitmap prevFrame)
    {
        int w = Math.Min(prevFrame.Width, frame.Width);
        const int colStep = 16;
        if (w / colStep == 0) return _stepPx;

        int searchMax = frame.Height - StripH;
        int searchMin = Math.Max(0, searchMax - _stepPx);
        if (searchMax <= searchMin) return _stepPx;

        var tData = prevFrame.LockBits(
            new System.Drawing.Rectangle(0, prevFrame.Height - StripH, prevFrame.Width, StripH),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var fData = frame.LockBits(
            new System.Drawing.Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        var tBytes = new byte[Math.Abs(tData.Stride) * StripH];
        var fBytes = new byte[Math.Abs(fData.Stride) * frame.Height];
        Marshal.Copy(tData.Scan0, tBytes, 0, tBytes.Length);
        Marshal.Copy(fData.Scan0, fBytes, 0, fBytes.Length);
        prevFrame.UnlockBits(tData);
        frame.UnlockBits(fData);

        int    bestY = searchMax;
        double bestS = double.MaxValue;
        int    ts = tData.Stride, fs = fData.Stride;
        int    cols = w / colStep;

        for (int y = searchMin; y <= searchMax; y++)
        {
            long sad = 0;
            for (int row = 0; row < StripH; row++)
            {
                int tOff = row * ts;
                int fOff = (y + row) * fs;
                for (int col = 0; col < w; col += colStep)
                {
                    int to = tOff + col * 4;
                    int fo = fOff + col * 4;
                    sad += Math.Abs(tBytes[to]     - fBytes[fo]);
                    sad += Math.Abs(tBytes[to + 1] - fBytes[fo + 1]);
                    sad += Math.Abs(tBytes[to + 2] - fBytes[fo + 2]);
                }
            }
            double s = (double)sad / (StripH * cols * 3);
            if (s < bestS) { bestS = s; bestY = y; }
        }

        int actualRows = frame.Height - bestY - StripH;
        System.Diagnostics.Debug.WriteLine($"FindActualNewRows: bestY={bestY} S={bestS:F2} rows={actualRows}");
        return Math.Max(1, actualRows);
    }

    private void AppendStrip(System.Drawing.Bitmap frame, int rows)
    {
        rows = Math.Clamp(rows, 1, frame.Height - 1);
        var strip = frame.Clone(
            new System.Drawing.Rectangle(0, frame.Height - rows, frame.Width, rows),
            PixelFormat.Format32bppArgb);
        _strips.Add(strip);
        _totalHeight += rows;
        HeightText.Text = $"{_totalHeight} px";
        System.Diagnostics.Debug.WriteLine($"Appended {rows}px  total={_totalHeight}");
    }

    private void SendScroll(int delta)
    {
        if (_targetHwnd == IntPtr.Zero) return;
        var center = new POINT { X = _region.X + _region.Width / 2, Y = _region.Y + _region.Height / 2 };
        var wParam = (IntPtr)(delta << 16);
        var lParam = (IntPtr)((center.Y << 16) | (center.X & 0xFFFF));
        PostMessage(_targetHwnd, WM_MOUSEWHEEL, wParam, lParam);
    }

    private int FindOverlapCalib(System.Drawing.Bitmap frame)
    {
        if (_tailStrip == null || _tailStrip.Height < StripH) return -1;

        int searchLimit = frame.Height - StripH;
        if (searchLimit <= 0) return -1;

        int w = Math.Min(_tailStrip.Width, frame.Width);
        const int step = 16;
        if (w / step == 0) return -1;

        var tData = _tailStrip.LockBits(
            new System.Drawing.Rectangle(0, 0, _tailStrip.Width, _tailStrip.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var fData = frame.LockBits(
            new System.Drawing.Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        var tBytes = new byte[Math.Abs(tData.Stride) * tData.Height];
        var fBytes = new byte[Math.Abs(fData.Stride) * fData.Height];
        Marshal.Copy(tData.Scan0, tBytes, 0, tBytes.Length);
        Marshal.Copy(fData.Scan0, fBytes, 0, fBytes.Length);
        int ts = tData.Stride, fs = fData.Stride;

        _tailStrip.UnlockBits(tData);
        frame.UnlockBits(fData);

        int    bestY = -1;
        double bestS = double.MaxValue;

        for (int y = 0; y <= searchLimit; y++)
        {
            long sad = 0;
            for (int row = 0; row < StripH; row++)
            {
                int tOff = row * ts;
                int fOff = (y + row) * fs;
                for (int col = 0; col < w; col += step)
                {
                    int to = tOff + col * 4;
                    int fo = fOff + col * 4;
                    sad += Math.Abs(tBytes[to]     - fBytes[fo]);
                    sad += Math.Abs(tBytes[to + 1] - fBytes[fo + 1]);
                    sad += Math.Abs(tBytes[to + 2] - fBytes[fo + 2]);
                }
            }
            int cols = w / step;
            double s = (double)sad / (StripH * cols * 3);
            if (s < bestS) { bestS = s; bestY = y; }
        }

        System.Diagnostics.Debug.WriteLine($"FindOverlapCalib: bestY={bestY} score={bestS:F2}");
        return bestY;
    }

    private static System.Drawing.Bitmap ExtractTail(System.Drawing.Bitmap source)
    {
        int h = Math.Min(StripH, source.Height);
        return source.Clone(
            new System.Drawing.Rectangle(0, source.Height - h, source.Width, h),
            PixelFormat.Format32bppArgb);
    }

    private System.Drawing.Bitmap FinalizeStitch()
    {
        int w      = _strips[0].Width;
        var result = new System.Drawing.Bitmap(w, _totalHeight, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(result);
        int y = 0;
        foreach (var strip in _strips)
        {
            g.DrawImage(strip, 0, y);
            y += strip.Height;
        }
        return result;
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => Finish();

    private void Finish()
    {
        if (_stopped) return;
        _stopped = true;
        _startDelay.Stop();
        _autoTimer.Stop();

        if (_strips.Count > 0)
            CapturedBitmap = FinalizeStitch();

        Cleanup();
        Close();
    }

    private void Cancel()
    {
        if (_stopped) return;
        _stopped = true;
        _startDelay.Stop();
        _autoTimer.Stop();
        Cleanup();
        Close();
    }

    private void Cleanup()
    {
        ReleaseClip();
        _regionBorder?.Close();
        _regionBorder = null;
        _tailStrip?.Dispose();
        _tailStrip = null;
        _prevFrame?.Dispose();
        _prevFrame = null;
        foreach (var s in _strips) s.Dispose();
        _strips.Clear();
    }

    private static bool IsBottomReached(System.Drawing.Bitmap a, System.Drawing.Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;

        int h = a.Height, w = a.Width;

        var aData = a.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bData = b.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var aBytes = new byte[Math.Abs(aData.Stride) * h];
        var bBytes = new byte[Math.Abs(bData.Stride) * h];
        Marshal.Copy(aData.Scan0, aBytes, 0, aBytes.Length);
        Marshal.Copy(bData.Scan0, bBytes, 0, bBytes.Length);
        a.UnlockBits(aData);
        b.UnlockBits(bData);

        const int rowCount = 8, colStep = 8;
        int rowStep = Math.Max(1, h / rowCount);
        long sad = 0;
        int samples = 0;

        for (int row = rowStep / 2; row < h; row += rowStep)
        {
            int aOff = row * aData.Stride;
            int bOff = row * bData.Stride;
            for (int col = 0; col < w; col += colStep)
            {
                int ai = aOff + col * 4, bi = bOff + col * 4;
                sad += Math.Abs(aBytes[ai]     - bBytes[bi]);
                sad += Math.Abs(aBytes[ai + 1] - bBytes[bi + 1]);
                sad += Math.Abs(aBytes[ai + 2] - bBytes[bi + 2]);
                samples++;
            }
        }

        double avgDiff = (double)sad / (samples * 3);
        System.Diagnostics.Debug.WriteLine($"IsBottomReached: avgDiff={avgDiff:F2}");
        return avgDiff < 3.0;
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_KEYDOWN)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == VK_RETURN)
            {
                Dispatcher.InvokeAsync(Finish);
                return (IntPtr)1;
            }
            if (info.vkCode == VK_ESCAPE)
            {
                Dispatcher.InvokeAsync(Cancel);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }
}
