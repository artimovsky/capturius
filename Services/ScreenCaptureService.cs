using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Capturius.Services;

public static class ScreenCaptureService
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("gdi32.dll")]  private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);
    [DllImport("user32.dll")] private static extern long GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    private const uint GW_HWNDNEXT      = 2;
    private const int  GWL_EXSTYLE      = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    // Captures window content including GPU-rendered surfaces (Chrome, WPF, etc.)
    private const uint PW_RENDERFULLCONTENT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public static (int Width, int Height) GetPhysicalScreenSize() =>
        (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    public static double GetDpiScale()
    {
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96.0;
    }

    public static Bitmap CaptureFullScreen()
    {
        var (w, h) = GetPhysicalScreenSize();
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(0, 0, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap CaptureRegion(System.Drawing.Rectangle r)
    {
        var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap CaptureWorkArea()
    {
        // WorkArea is in WPF logical units — multiply by DPI to get physical pixels
        var wa = System.Windows.SystemParameters.WorkArea;
        double dpi = GetDpiScale();
        int x = (int)Math.Round(wa.X * dpi);
        int y = (int)Math.Round(wa.Y * dpi);
        int w = (int)Math.Round(wa.Width * dpi);
        int h = (int)Math.Round(wa.Height * dpi);

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap? CaptureWindowByHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        if (!GetWindowRect(hwnd, out var rect)) return null;

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return null;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        // PrintWindow renders the window's own content into the bitmap — no neighboring
        // windows bleed in at the edges, and off-screen areas are captured correctly.
        var hdc = g.GetHdc();
        PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
        g.ReleaseHdc(hdc);
        return bmp;
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    // Walk Z-order starting just below excludeHwnd (our Topmost window).
    // The first visible, non-minimized, non-toolwindow window with a reasonable
    // size is what the user was working in before clicking our toolbar.
    public static IntPtr FindWindowBelowHwnd(IntPtr excludeHwnd)
    {
        var hwnd = GetWindow(excludeHwnd, GW_HWNDNEXT);
        while (hwnd != IntPtr.Zero)
        {
            if (IsWindowVisible(hwnd) && !IsIconic(hwnd))
            {
                GetWindowRect(hwnd, out var rect);
                int w = rect.Right  - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w > 100 && h > 100 && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0)
                    return hwnd;
            }
            hwnd = GetWindow(hwnd, GW_HWNDNEXT);
        }
        return IntPtr.Zero;
    }
}
