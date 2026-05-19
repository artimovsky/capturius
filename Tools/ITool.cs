using System.Windows;
using Capturius.Models;

namespace Capturius.Tools;

public interface ITool
{
    string    Name            { get; }
    UIElement PropertiesPanel { get; }

    event Action? PropertiesChanged;

    UIElement?  Preview(Point start, Point current);
    Annotation? Commit(Point start, Point end);
    UIElement   Render(Annotation annotation);
    void        SyncFrom(Annotation annotation);
    void        ApplyTo(Annotation annotation);

    void LoadSettings();
    void SaveSettings();
}
