using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Capturius.Helpers;

public static class UiHelper
{
    public static StackPanel MakePanel() => new StackPanel
    {
        Orientation       = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock MakeLabel(string text, double leftMargin = 0) => new TextBlock
    {
        Text              = text,
        Foreground        = new SolidColorBrush(Color.FromRgb(0x58, 0x5B, 0x70)),
        FontSize          = 12,
        FontFamily        = new FontFamily("Segoe UI"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin            = new Thickness(leftMargin, 0, 6, 0),
    };

    public static ItemsControl MakePicker()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        return new ItemsControl { ItemsPanel = new ItemsPanelTemplate { VisualTree = factory } };
    }

    public static RadioButton MakeRadio(
        string content, string group, bool isChecked, Action onChecked, double leftMargin = 2)
    {
        var rb = new RadioButton
        {
            Content   = content,
            GroupName = group,
            IsChecked = isChecked,
            Margin    = new Thickness(leftMargin, 0, 0, 0),
            Tag       = content,
            Style     = (Style)Application.Current.Resources["ThickBtn"],
        };
        rb.Checked += (_, _) => onChecked();
        return rb;
    }
}
