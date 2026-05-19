using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Capturius.Helpers;
using Capturius.Models;

namespace Capturius.Editor;

public sealed class AnnotationManager
{
    private readonly Canvas          _canvas;
    private readonly List<Annotation> _annotations = new();
    private readonly List<UIElement>  _handles     = new();

    // Drag state
    private string _dragMode      = "";
    private int    _dragCornerIdx;
    private Point  _dragMouseStart;
    private Point  _dragAnnStart;
    private Point  _dragAnnEnd;

    public Annotation? Selected   { get; private set; }
    public bool        IsDragging { get; private set; }
    public Cursor      DragCursor { get; private set; } = Cursors.Cross;

    public IReadOnlyList<Annotation> Annotations => _annotations;

    public AnnotationManager(Canvas canvas) => _canvas = canvas;

    // ── List management ───────────────────────────────────────────────────────

    public void Add(Annotation ann)
    {
        _canvas.Children.Add(ann.Element);
        _annotations.Add(ann);
    }

    public void Track(Annotation ann) => _annotations.Add(ann);

    public void Delete(Annotation ann)
    {
        if (ReferenceEquals(ann, Selected)) Deselect();
        _canvas.Children.Remove(ann.Element);
        _annotations.Remove(ann);
    }

    public void Undo()
    {
        if (_annotations.Count == 0) return;
        Delete(_annotations[^1]);
    }

    public void Clear()
    {
        Deselect();
        _canvas.Children.Clear();
        _annotations.Clear();
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    public void Select(Annotation ann)
    {
        Deselect();
        Selected = ann;
        ann.Tool.SyncFrom(ann);
        ShowHandles(ann);
    }

    public void Deselect()
    {
        Selected = null;
        HideHandles();
    }

    public void UpdateHandles()
    {
        if (Selected != null) ShowHandles(Selected);
    }

    // ── Hit test ──────────────────────────────────────────────────────────────

    public Annotation? HitTest(Point pos)
    {
        Annotation? found = null;
        VisualTreeHelper.HitTest(
            _canvas, null,
            result =>
            {
                var ann = FindAnnotationByVisual(result.VisualHit);
                if (ann?.IsSelectable == true) { found = ann; return HitTestResultBehavior.Stop; }
                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(pos));
        return found;
    }

    private Annotation? FindAnnotationByVisual(DependencyObject visual)
    {
        var current = visual;
        while (current != null && !ReferenceEquals(current, _canvas))
        {
            if (current is UIElement el)
            {
                var ann = _annotations.FirstOrDefault(a => ReferenceEquals(a.Element, el));
                if (ann != null) return ann;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ── Cursor ────────────────────────────────────────────────────────────────

    public Cursor HoverCursor(Point pos)
    {
        if (Selected == null) return Cursors.Cross;

        if (!Selected.IsRectLike)
        {
            var d0 = Selected.Start - pos;
            if (Math.Sqrt(d0.X * d0.X + d0.Y * d0.Y) <= 8) return Cursors.SizeNWSE;
            var d1 = Selected.End - pos;
            if (Math.Sqrt(d1.X * d1.X + d1.Y * d1.Y) <= 8) return Cursors.SizeNESW;
        }
        else
        {
            int ci = FindNearestHandle(pos, Selected, 8);
            if (ci >= 0)
                return ci switch
                {
                    0 or 3 => Cursors.SizeNWSE,
                    1 or 2 => Cursors.SizeNESW,
                    4 or 6 => Cursors.SizeNS,
                    _      => Cursors.SizeWE,
                };
        }

        if (HitTest(pos) == Selected) return Cursors.SizeAll;
        return Cursors.Cross;
    }

    // ── Drag ──────────────────────────────────────────────────────────────────

    public bool TryBeginDrag(Point pos)
    {
        if (Selected == null) return false;

        if (!Selected.IsRectLike)
        {
            Point[] eps = { Selected.Start, Selected.End };
            for (int i = 0; i < 2; i++)
            {
                var d = eps[i] - pos;
                if (Math.Sqrt(d.X * d.X + d.Y * d.Y) <= 8)
                {
                    IsDragging      = true;
                    _dragMode       = "resize";
                    _dragCornerIdx  = i;
                    _dragMouseStart = pos;
                    _dragAnnStart   = Selected.Start;
                    _dragAnnEnd     = Selected.End;
                    DragCursor      = i == 0 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                    return true;
                }
            }
        }
        else
        {
            int ci = FindNearestHandle(pos, Selected, 8);
            if (ci >= 0)
            {
                IsDragging      = true;
                _dragMode       = "resize";
                _dragCornerIdx  = ci;
                _dragMouseStart = pos;
                var r           = ShapeHelper.NormRect(Selected.Start, Selected.End);
                _dragAnnStart   = new Point(r.Left,  r.Top);
                _dragAnnEnd     = new Point(r.Right,  r.Bottom);
                DragCursor      = ci switch
                {
                    0 or 3 => Cursors.SizeNWSE,
                    1 or 2 => Cursors.SizeNESW,
                    4 or 6 => Cursors.SizeNS,
                    _      => Cursors.SizeWE,
                };
                return true;
            }
        }

        return false;
    }

    public void BeginMove(Point pos)
    {
        if (Selected == null) return;
        IsDragging      = true;
        _dragMode       = "move";
        _dragMouseStart = pos;
        DragCursor      = Cursors.SizeAll;

        if (Selected.IsRectLike)
        {
            var r       = ShapeHelper.NormRect(Selected.Start, Selected.End);
            _dragAnnStart = new Point(r.Left,  r.Top);
            _dragAnnEnd   = new Point(r.Right, r.Bottom);
        }
        else
        {
            _dragAnnStart = Selected.Start;
            _dragAnnEnd   = Selected.End;
        }
    }

    public void UpdateDrag(Point cur)
    {
        if (!IsDragging || Selected == null) return;
        var ann = Selected;

        if (_dragMode == "move")
        {
            var delta = cur - _dragMouseStart;
            ann.Start = _dragAnnStart + delta;
            ann.End   = _dragAnnEnd   + delta;
        }
        else
        {
            if (!ann.IsRectLike)
            {
                if (_dragCornerIdx == 0) ann.Start = cur;
                else                     ann.End   = cur;
            }
            else
            {
                switch (_dragCornerIdx)
                {
                    case 0: ann.Start = cur;                                    ann.End = _dragAnnEnd;                           break;
                    case 1: ann.Start = new Point(_dragAnnStart.X, cur.Y);     ann.End = new Point(cur.X, _dragAnnEnd.Y);       break;
                    case 2: ann.Start = new Point(cur.X, _dragAnnStart.Y);     ann.End = new Point(_dragAnnEnd.X, cur.Y);       break;
                    case 3: ann.Start = _dragAnnStart;                          ann.End = cur;                                   break;
                    case 4: ann.Start = new Point(_dragAnnStart.X, cur.Y);     ann.End = _dragAnnEnd;                           break;
                    case 5: ann.Start = _dragAnnStart;                          ann.End = new Point(cur.X, _dragAnnEnd.Y);       break;
                    case 6: ann.Start = _dragAnnStart;                          ann.End = new Point(_dragAnnEnd.X, cur.Y);       break;
                    case 7: ann.Start = new Point(cur.X, _dragAnnStart.Y);     ann.End = _dragAnnEnd;                           break;
                }
            }
        }

        int idx = _canvas.Children.IndexOf(ann.Element);
        _canvas.Children.Remove(ann.Element);
        ann.Element = ann.Tool.Render(ann);
        if (idx >= 0) _canvas.Children.Insert(idx, ann.Element);
        else          _canvas.Children.Add(ann.Element);
        ShowHandles(ann);
    }

    public void EndDrag() => IsDragging = false;

    // ── Handles ───────────────────────────────────────────────────────────────

    private void ShowHandles(Annotation ann)
    {
        HideHandles();
        IEnumerable<Point> pts;

        if (ann.IsRectLike)
        {
            var r  = ShapeHelper.NormRect(ann.Start, ann.End);
            double cx = (r.Left + r.Right)  / 2;
            double cy = (r.Top  + r.Bottom) / 2;
            pts = new[]
            {
                new Point(r.Left,  r.Top),    new Point(r.Right, r.Top),
                new Point(r.Left,  r.Bottom), new Point(r.Right, r.Bottom),
                new Point(cx,      r.Top),    new Point(r.Right, cy),
                new Point(cx,      r.Bottom), new Point(r.Left,  cy),
            };
        }
        else
        {
            pts = new[] { ann.Start, ann.End };
        }

        foreach (var pt in pts)
        {
            var h = new Ellipse
            {
                Width            = 8, Height = 8,
                Fill             = Brushes.White,
                Stroke           = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
                StrokeThickness  = 1.5,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(h, pt.X - 4);
            Canvas.SetTop(h, pt.Y - 4);
            _canvas.Children.Add(h);
            _handles.Add(h);
        }
    }

    private void HideHandles()
    {
        foreach (var h in _handles)
            _canvas.Children.Remove(h);
        _handles.Clear();
    }

    public void WithoutHandles(Action render)
    {
        var snapshot = _handles.ToList();
        foreach (var h in snapshot) _canvas.Children.Remove(h);
        render();
        foreach (var h in snapshot) _canvas.Children.Add(h);
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static int FindNearestHandle(Point pos, Annotation ann, double threshold)
    {
        var r  = ShapeHelper.NormRect(ann.Start, ann.End);
        double cx = (r.Left + r.Right)  / 2;
        double cy = (r.Top  + r.Bottom) / 2;
        Point[] pts =
        {
            new Point(r.Left,  r.Top),    new Point(r.Right, r.Top),
            new Point(r.Left,  r.Bottom), new Point(r.Right, r.Bottom),
            new Point(cx,      r.Top),    new Point(r.Right, cy),
            new Point(cx,      r.Bottom), new Point(r.Left,  cy),
        };
        for (int i = 0; i < pts.Length; i++)
        {
            var d = pts[i] - pos;
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) <= threshold) return i;
        }
        return -1;
    }
}
