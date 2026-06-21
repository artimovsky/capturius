using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Capturius.Helpers;

public static class ShapeHelper
{
    public static readonly (Color Color, string Name)[] ColorPresets =
    [
        (Color.FromRgb(0xE0, 0x31, 0x31), "Red"),
        (Color.FromRgb(0x19, 0x71, 0xC2), "Blue"),
        (Color.FromRgb(0x2F, 0x9D, 0x43), "Green"),
        (Color.FromRgb(0xFF, 0xEC, 0x99), "Yellow"),
        (Colors.White, "White"),
        (Colors.Black, "Black"),
    ];

    public static DropShadowEffect? MakeShadow(double shadow)
    {
        if (shadow <= 0) return null;
        var fx = new DropShadowEffect
        {
            BlurRadius  = shadow,
            ShadowDepth = 0,
            Direction   = 315,
            Color       = Colors.Black,
            Opacity     = 0.9,
        };
        fx.Freeze();
        return fx;
    }

    public static Rect NormRect(Point a, Point b) => new Rect(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static string ColorToString(Color c) => $"{c.A},{c.R},{c.G},{c.B}";

    public static Color ParseColor(string? s, Color fallback)
    {
        if (s == null) return fallback;
        var p = s.Split(',');
        if (p.Length == 4 &&
            byte.TryParse(p[0], out var a) && byte.TryParse(p[1], out var r) &&
            byte.TryParse(p[2], out var g) && byte.TryParse(p[3], out var b))
            return Color.FromArgb(a, r, g, b);
        return fallback;
    }

    public static void BuildColorPickerItems(
        ItemsControl control,
        (Color Color, string Name)[] presets,
        Func<Color> getColor,
        Action<Color> onSelect)
    {
        foreach (var (color, name) in presets)
        {
            var c = color;
            var btn = new Border
            {
                Width           = 20, Height = 20,
                CornerRadius    = new CornerRadius(10),
                Background      = new SolidColorBrush(color),
                Margin          = new Thickness(2, 0, 2, 0),
                Cursor          = Cursors.Hand,
                ToolTip         = name,
                BorderThickness = new Thickness(2),
                BorderBrush     = color == getColor() ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
                Tag             = color,
            };
            btn.MouseLeftButtonDown += (_, _) => onSelect(c);
            control.Items.Add(btn);
        }
    }

    public static void BuildFillColorPickerItems(
        ItemsControl control,
        (Color Color, string Name)[] presets,
        Func<Color> getColor,
        Action<Color> onSelect)
    {
        var noneInner = new Grid();
        noneInner.Children.Add(new Line
        {
            X1 = 1, Y1 = 13, X2 = 13, Y2 = 1,
            Stroke = Brushes.OrangeRed, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
        var noneBtn = new Border
        {
            Width           = 20, Height = 20,
            CornerRadius    = new CornerRadius(10),
            Background      = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Margin          = new Thickness(2, 0, 2, 0),
            Cursor          = Cursors.Hand,
            ToolTip         = "None",
            BorderThickness = new Thickness(2),
            BorderBrush     = getColor().A == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            Tag             = Colors.Transparent,
            Child           = noneInner,
            ClipToBounds    = true,
        };
        noneBtn.MouseLeftButtonDown += (_, _) => onSelect(Colors.Transparent);
        control.Items.Add(noneBtn);

        BuildColorPickerItems(control, presets, getColor, onSelect);
    }

    public static void UpdatePickerSelection(ItemsControl control, Color selected)
    {
        var dim = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
        foreach (Border b in control.Items)
            b.BorderBrush = (Color)b.Tag == selected ? Brushes.White : dim;
    }
}
