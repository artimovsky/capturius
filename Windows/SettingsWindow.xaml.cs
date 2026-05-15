using Microsoft.Win32;
using System.Windows;

namespace Capturius.Windows;

public partial class SettingsWindow : Window
{
    private const string RunKey      = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string SettingsKey = @"Software\Capturius";
    private const string AppName     = "Capturius";

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (settings is null)
        {
            // First launch — enable startup by default
            SetStartup(true);
            ChkStartup.IsChecked = true;
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            key.SetValue("StartWithWindows", 1);
            return;
        }

        ChkStartup.IsChecked = settings.GetValue("StartWithWindows") is int v && v == 1;
    }

    private void ChkStartup_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkStartup.IsChecked == true;
        SetStartup(enable);
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue("StartWithWindows", enable ? 1 : 0);
    }

    private static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (enable)
            key?.SetValue(AppName, $"\"{Environment.ProcessPath!}\" --minimized");
        else
            key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
