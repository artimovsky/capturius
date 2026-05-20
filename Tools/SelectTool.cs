using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Capturius.Helpers;
using Capturius.Models;

namespace Capturius.Tools;

public sealed class SelectTool : ITool
{
    private readonly StackPanel _panel;

    public event Action?         PropertiesChanged;
    public event Action?         CropRequested;
    public event Action<double>? BlurRequested;

    public string    Name            => "Select";
    public UIElement PropertiesPanel => _panel;

    public SelectTool()
    {
        _panel = UiHelper.MakePanel();
        BuildPanel();
    }

    private void BuildPanel()
    {
        var btnCrop = MakeButton("Crop");
        btnCrop.Click += (_, _) => CropRequested?.Invoke();
        _panel.Children.Add(btnCrop);

        _panel.Children.Add(new Rectangle
        {
            Width             = 1,
            Height            = 20,
            Fill              = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible  = false,
        });

        _panel.Children.Add(UiHelper.MakeLabel("Blur:", 8));

        foreach (var v in new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 })
        {
            double amount = v;
            var btn = MakeButton($"{v}%");
            btn.Click += (_, _) => BlurRequested?.Invoke(amount);
            _panel.Children.Add(btn);
        }
    }

    private static Button MakeButton(string content) => new Button
    {
        Content         = content,
        Foreground      = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
        Background      = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
        BorderBrush     = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
        BorderThickness = new Thickness(1),
        Padding         = new Thickness(8, 4, 8, 4),
        FontSize        = 12,
        FontFamily      = new FontFamily("Segoe UI"),
        Cursor          = Cursors.Hand,
        Margin          = new Thickness(2, 0, 0, 0),
    };

    public UIElement? Preview(Point start, Point current) => MakeSelectRect(start, current);

    public Annotation? Commit(Point start, Point end)
    {
        var r = ShapeHelper.NormRect(start, end);
        if (r.Width < 4 || r.Height < 4) return null;
        return new SelectAnnotation
        {
            Tool    = this,
            Element = MakeSelectRect(start, end),
            Start   = new Point(r.Left, r.Top),
            End     = new Point(r.Right, r.Bottom),
        };
    }

    public UIElement Render(Annotation ann) => MakeSelectRect(ann.Start, ann.End);

    public void SyncFrom(Annotation ann) { }
    public void ApplyTo(Annotation ann)  { }
    public void LoadSettings()           { }
    public void SaveSettings()           { }

    private static Rectangle MakeSelectRect(Point start, Point end)
    {
        var r    = ShapeHelper.NormRect(start, end);
        var rect = new Rectangle
        {
            Stroke          = new SolidColorBrush(Color.FromArgb(200, 137, 180, 250)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection([6, 3]),
            Fill            = new SolidColorBrush(Color.FromArgb(30, 137, 180, 250)),
            Width           = Math.Max(r.Width,  0),
            Height          = Math.Max(r.Height, 0),
        };
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect,  r.Y);
        return rect;
    }
}
