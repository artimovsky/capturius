using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Capturius.Services;
using Capturius.Windows;

namespace Capturius;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    private const int WM_ACTIVATE   = 0x0006;
    private const int WA_INACTIVE   = 0;
    private const int WM_HOTKEY     = 0x0312;
    private const int WM_KEYDOWN    = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WH_KEYBOARD_LL = 13;
    private const int HOTKEY_REGION       = 1;
    private const int HOTKEY_COLOR_PICKER = 2;
    private const uint MOD_CONTROL  = 0x0002;
    private const uint MOD_SHIFT    = 0x0004;
    private const uint VK_C         = 0x43;
    private const uint VK_SNAPSHOT  = 0x2C;
    private const int VK_CONTROL    = 0x11;
    private const int VK_SHIFT      = 0x10;
    private const int VK_MENU       = 0x12; // Alt
    private const int VK_RMENU      = 0xA5; // Right Alt / AltGr

    // Stored as field to prevent GC collection
    private LowLevelKeyboardProc _kbProc = null!;
    private IntPtr _kbHook;

    private System.Windows.Forms.NotifyIcon _trayIcon = null!;
    private bool _isExiting;

    private double _savedLeft, _savedTop;
    // HWND of the window that was active just before our window took focus
    private IntPtr _previousForegroundHwnd;

    public MainWindow()
    {
        InitializeComponent();
        InitTrayIcon();
        if (Environment.GetCommandLineArgs().Contains("--minimized"))
            Hide();
    }

    private void InitTrayIcon()
    {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                   ?? System.Drawing.SystemIcons.Application;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Capturius",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Show();
                Activate();
            }
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e) => Hide();

    private void BuyMeCoffee_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://paypal.me/DenisArtimovskii") { UseShellExecute = true });

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new Windows.SettingsWindow { Owner = this };
        settings.ShowDialog();
    }

    private void ExitApp()
    {
        _isExiting = true;
        _trayIcon.Dispose();
        foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w != this).ToList())
            w.Close();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        RegisterHotKey(hwnd, HOTKEY_REGION,       MOD_CONTROL,              VK_SNAPSHOT);
        RegisterHotKey(hwnd, HOTKEY_COLOR_PICKER, MOD_CONTROL | MOD_SHIFT,  VK_C);

        // Plain PrintScreen is intercepted by Windows before RegisterHotKey fires,
        // so we use a low-level keyboard hook instead.
        _kbProc = KeyboardHook;
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, IntPtr.Zero, 0);
    }

    protected override void OnClosed(EventArgs e)
    {
        UnhookWindowsHookEx(_kbHook);
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_REGION);
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_COLOR_PICKER);
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_ACTIVATE: wParam != WA_INACTIVE means our window is becoming active.
        // lParam is the HWND of the window being deactivated — that's the one the user was on.
        // Skip HWNDs from our own process (e.g. EditorWindow closing triggers this too).
        if (msg == WM_ACTIVATE && wParam.ToInt32() != WA_INACTIVE && lParam != IntPtr.Zero)
        {
            GetWindowThreadProcessId(lParam, out int pid);
            if (pid != Environment.ProcessId)
                _previousForegroundHwnd = lParam;
        }

        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_REGION)
        {
            CaptureRegion_Click(this, null!);
            handled = true;
        }

        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_COLOR_PICKER)
        {
            ColorPicker_Click(this, null!);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == VK_SNAPSHOT)
            {
                bool alt   = (info.flags & 0x20) != 0; // LLKHF_ALTDOWN — reliable for both Left and Right Alt
                // Right Alt (AltGr) injects a synthetic LControl — exclude that from the Ctrl check
                bool altGr = alt && (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0;
                bool ctrl  = !altGr && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                if (!ctrl && !shift && !alt)
                {
                    Dispatcher.InvokeAsync(() => CaptureFullScreen_Click(this, null!));
                    return (IntPtr)1;
                }
                if (!ctrl && shift && !alt)
                {
                    Dispatcher.InvokeAsync(() => CaptureDesktop_Click(this, null!));
                    return (IntPtr)1;
                }
                if (!ctrl && !shift && alt)
                {
                    Dispatcher.InvokeAsync(() => CaptureActiveWindow_Click(this, null!));
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // Window moves are never animated in Windows — unlike Hide/Close.
    // DwmFlush on a background thread waits for the DWM frame with our window off-screen.
    private async Task MoveOffScreenAsync()
    {
        _savedLeft = Left;
        _savedTop = Top;
        Left = -Width - 10;
        Top = -Height - 10;
        await Task.Run(() => DwmFlush());
    }

    private void MoveBackOnScreen()
    {
        Left = _savedLeft;
        Top = _savedTop;
    }

    private void OpenEditor(BitmapSource source)
    {
        MoveBackOnScreen();
        Topmost = false;
        var editor = new EditorWindow(source);
        editor.Closed += (_, _) => Topmost = true;
        editor.Show();
    }

    private async void CaptureFullScreen_Click(object sender, RoutedEventArgs e)
    {
        await MoveOffScreenAsync();

        var bitmap = ScreenCaptureService.CaptureFullScreen();
        var source = ScreenCaptureService.ToBitmapSource(bitmap);
        bitmap.Dispose();

        OpenEditor(source);
    }

    private async void CaptureActiveWindow_Click(object sender, RoutedEventArgs e)
    {
        var ourHwnd = new WindowInteropHelper(this).Handle;

        // WM_ACTIVATE gives us the HWND deactivated when we gained focus (best case).
        // If nothing was tracked yet (app just started), fall back to Z-order: since
        // we are Topmost, the first suitable window below us is the user's target.
        var targetHwnd = _previousForegroundHwnd != IntPtr.Zero
            ? _previousForegroundHwnd
            : ScreenCaptureService.FindWindowBelowHwnd(ourHwnd);

        await MoveOffScreenAsync();

        var bitmap = ScreenCaptureService.CaptureWindowByHandle(targetHwnd);
        if (bitmap == null) { MoveBackOnScreen(); return; }

        var source = ScreenCaptureService.ToBitmapSource(bitmap);
        bitmap.Dispose();

        OpenEditor(source);
    }

    private async void CaptureDesktop_Click(object sender, RoutedEventArgs e)
    {
        await MoveOffScreenAsync();

        var bitmap = ScreenCaptureService.CaptureWorkArea();
        var source = ScreenCaptureService.ToBitmapSource(bitmap);
        bitmap.Dispose();

        OpenEditor(source);
    }

    private async void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        await MoveOffScreenAsync();

        var picker = new ColorPickerWindow();
        picker.ShowDialog();

        MoveBackOnScreen();
    }

    private async void CaptureScroll_Click(object sender, RoutedEventArgs e)
    {
        await MoveOffScreenAsync();

        // Step 1: user picks a window to scroll-capture
        var overlay = new OverlayWindow(snapMode: true);
        overlay.ShowDialog();

        if (overlay.CapturedRegion.IsEmpty)
        {
            MoveBackOnScreen();
            return;
        }

        // Step 2: scroll-capture that region
        var scroll = new ScrollCaptureWindow(overlay.CapturedRegion);
        scroll.ShowDialog();

        if (scroll.CapturedBitmap is { } bitmap)
        {
            var source = ScreenCaptureService.ToBitmapSource(bitmap);
            bitmap.Dispose();
            OpenEditor(source);
        }
        else
        {
            MoveBackOnScreen();
        }
    }

    private async void CaptureRegion_Click(object sender, RoutedEventArgs e)
    {
        await MoveOffScreenAsync();

        var overlay = new OverlayWindow();
        overlay.ShowDialog();

        if (overlay.CapturedBitmap is { } bitmap)
        {
            var source = ScreenCaptureService.ToBitmapSource(bitmap);
            bitmap.Dispose();

            OpenEditor(source);
        }
        else
        {
            MoveBackOnScreen();
        }
    }
}
