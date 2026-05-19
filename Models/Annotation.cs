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

public sealed class TextAnnotation : Annotation { }
