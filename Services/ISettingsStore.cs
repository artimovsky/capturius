namespace Capturius.Services;

public interface ISettingsStore
{
    void    Set(string key, string value);
    void    Set(string key, double value);
    string? GetString(string key);
    double? GetDouble(string key);
}
