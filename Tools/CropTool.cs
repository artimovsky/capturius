using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Capturius.Helpers;
using Capturius.Models;

namespace Capturius.Tools;

public sealed class CropTool : ITool
{
    private readonly StackPanel _panel = UiHelper.MakePanel();

    public event Action? PropertiesChanged;

    public string    Name            => "Crop";
    public UIElement PropertiesPanel => _panel;

    public UIElement? Preview(Point start, Point current) => MakeCropRect(start, current);
    public Annotation? Commit(Point start, Point end)     => null;
    public UIElement   Render(Annotation annotation)      => annotation.Element;
    public void        SyncFrom(Annotation annotation)    { }
    public void        ApplyTo(Annotation annotation)     { }
    public void        LoadSettings()                     { }
    public void        SaveSettings()                     { }

    public Rectangle MakeCropRect(Point start, Point end)
    {
        var r = ShapeHelper.NormRect(start, end);
        var rect = new Rectangle
        {
            Stroke          = new SolidColorBrush(Color.FromArgb(200, 137, 180, 250)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection([6, 3]),
            Fill            = new SolidColorBrush(Color.FromArgb(30, 137, 180, 250)),
            Width           = Math.Max(r.Width, 0),
            Height          = Math.Max(r.Height, 0),
        };
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect, r.Y);
        return rect;
    }
}
