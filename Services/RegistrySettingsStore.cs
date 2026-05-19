using System.Globalization;
using Microsoft.Win32;

namespace Capturius.Services;

public sealed class RegistrySettingsStore : ISettingsStore
{
    private const string RegKey = @"Software\Capturius\Editor";

    public void Set(string key, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RegKey);
        k.SetValue(key, value);
    }

    public void Set(string key, double value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RegKey);
        k.SetValue(key, value.ToString(CultureInfo.InvariantCulture));
    }

    public string? GetString(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RegKey);
        return k?.GetValue(key) as string;
    }

    public double? GetDouble(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RegKey);
        var s = k?.GetValue(key)?.ToString();
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
