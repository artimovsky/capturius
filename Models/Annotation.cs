using System.Windows;
using System.Windows.Media;
using Capturius.Tools;

namespace Capturius.Models;

public abstract class Annotation
{
    public required ITool     Tool    { get; init; }
    public required UIElement Element { get; set; }
    public          Point     Start   { get; set; }
    public          Point     End     { get; set; }

    public virtual bool IsSelectable => false;
    public virtual bool IsRectLike   => false;
}

public sealed class ArrowAnnotation : Annotation
{
    public Color  FillColor       { get; set; }
    public Color  StrokeColor     { get; set; }
    public double Thickness       { get; set; }
    public double StrokeThickness { get; set; }
    public double Shadow          { get; set; }

    public override bool IsSelectable => true;
}

public sealed class RectAnnotation : Annotation
{
    public Color  FillColor       { get; set; }
    public Color  BorderColor     { get; set; }
    public double BorderThickness { get; set; }
    public double CornerRadius    { get; set; }
    public double Opacity         { get; set; } = 100;
    public double Shadow          { get; set; }

    public override bool IsSelectable => true;
    public override bool IsRectLike   => true;
}

public sealed class TextAnnotation : Annotation
{
    public string            Text                 { get; set; } = "";
    public string            FontFamilyName       { get; set; } = "Segoe UI";
    public double            FontSize             { get; set; } = 18;
    public Color             TextColor            { get; set; } = Color.FromRgb(0xCD, 0xD6, 0xF4);
    public double            StrokeThickness { get; set; } = 0;
    public Color             StrokeColor     { get; set; } = Colors.Black;
    public double            Shadow          { get; set; } = 0;
    public TextAlignment     HAlign          { get; set; } = TextAlignment.Left;
    public VerticalAlignment VAlign               { get; set; } = VerticalAlignment.Top;

    public bool IsEditing { get; set; }

    public override bool IsSelectable => true;
    public override bool IsRectLike   => true;
}
