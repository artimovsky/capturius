using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Capturius.Editor;
using Capturius.Helpers;
using Capturius.Models;
using Capturius.Services;
using Capturius.Tools;

namespace Capturius.Windows;

public partial class EditorWindow : Window
{
    private BitmapSource _bitmapSource;
    private ITool        _currentTool;
    private double       _zoomLevel = 1.0;
    private readonly double _dpiScale;

    private Point      _drawStart;
    private bool       _isDrawing;
    private UIElement? _previewElement;
    private Annotation? _hitOnDown;
    private bool       _switchingTool;

    private readonly Stack<Action> _undoStack = new();

    private enum CropHandle { TL, TC, TR, ML, MR, BL, BC, BR }
    private readonly Dictionary<CropHandle, Rectangle> _cropHandles = new();
    private Rectangle? _cropPreview;
    private bool       _isCropDragging;
    private CropHandle _activeCropHandle;
    private Point      _cropDragStart;
    private double     _cropOrigW, _cropOrigH;
    private double     _cropNewX, _cropNewY, _cropNewW, _cropNewH;

    private readonly AnnotationManager _manager;
    private readonly ArrowTool         _arrowTool;
    private readonly RectTool          _rectTool;
    private readonly TextTool          _textTool;
    private readonly SelectTool        _selectTool;
    private readonly NumberTool        _numberTool;

    public EditorWindow(BitmapSource bitmapSource)
    {
        InitializeComponent();

        _bitmapSource = bitmapSource;
        _dpiScale     = ScreenCaptureService.GetDpiScale();

        var settings     = new RegistrySettingsStore();
        _arrowTool       = new ArrowTool(settings);
        _rectTool        = new RectTool(settings);
        _textTool        = new TextTool(settings);
        _selectTool      = new SelectTool();
        _selectTool.CropRequested += ApplyCropToSelection;
        _selectTool.BlurRequested += ApplyBlurToSelection;
        _numberTool      = new NumberTool(settings);

        foreach (var t in new ITool[] { _arrowTool, _rectTool, _textTool, _numberTool })
            t.PropertiesChanged += RedrawSelected;

        _currentTool = _selectTool;
        _manager     = new AnnotationManager(AnnotationCanvas);

        MainImage.Source        = bitmapSource;
        AnnotationCanvas.Width  = bitmapSource.PixelWidth;
        AnnotationCanvas.Height = bitmapSource.PixelHeight;

        ToolPropertiesHost.Content = _currentTool.PropertiesPanel;
        SetZoom(1.0);
        InitCropHandles();

        PreviewKeyDown += OnKeyDown;
    }

    // ── Tool switching ────────────────────────────────────────────────────────

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (_manager == null || _switchingTool) return;

        var tag = ((ToggleButton)sender).Tag!.ToString()!;
        UncheckOtherTools((ToggleButton)sender);

        _currentTool = tag switch
        {
            "Arrow"  => _arrowTool,
            "Rect"   => _rectTool,
            "Text"   => _textTool,
            "Select" => _selectTool,
            "Number" => _numberTool,
            _        => _arrowTool,
        };

        ToolPropertiesHost.Content = _currentTool.PropertiesPanel;
        AnnotationCanvas.Cursor    = tag == "Text" ? Cursors.IBeam : Cursors.Cross;

        if (tag != "Select")
        {
            var existing = _manager.Annotations.OfType<SelectAnnotation>().FirstOrDefault();
            if (existing != null) _manager.Delete(existing);
        }
        _manager.Deselect();
    }

    private void SwitchToAnnotationTool(ITool tool)
    {
        _currentTool               = tool;
        ToolPropertiesHost.Content = tool.PropertiesPanel;

        _switchingTool = true;
        foreach (var btn in new[] { BtnArrow, BtnRect, BtnText, BtnSelect, BtnNumber })
            if (btn != null) btn.IsChecked = btn.Tag?.ToString() == tool.Name;
        _switchingTool = false;
    }

    private void UncheckOtherTools(ToggleButton active)
    {
        foreach (var btn in new[] { BtnArrow, BtnRect, BtnText, BtnSelect, BtnNumber })
            if (btn != null && btn != active) btn.IsChecked = false;
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(AnnotationCanvas);

        if (_manager.Selected is TextAnnotation { IsEditing: true })
        {
            if (_manager.TryBeginDrag(pos))
            {
                AnnotationCanvas.Cursor = _manager.DragCursor;
                AnnotationCanvas.CaptureMouse();
            }
            else
            {
                _textTool.CommitPending();
            }
            return;
        }

        // Double-click on a text annotation → enter edit mode
        if (e.ClickCount == 2)
        {
            var hitAnn = _manager.HitTest(pos);
            if (hitAnn is TextAnnotation textAnn)
            {
                _manager.Deselect();
                _textTool.BeginEdit(AnnotationCanvas, textAnn, () => _manager.Select(textAnn));
                Dispatcher.BeginInvoke(() => AttachEditingHandles(textAnn));
                return;
            }
        }

        if (_manager.TryBeginDrag(pos))
        {
            AnnotationCanvas.Cursor = _manager.DragCursor;
            AnnotationCanvas.CaptureMouse();
            return;
        }

        var hit = _manager.HitTest(pos);
        if (hit == _manager.Selected && hit != null)
        {
            _manager.BeginMove(pos);
            AnnotationCanvas.Cursor = _manager.DragCursor;
            AnnotationCanvas.CaptureMouse();
            return;
        }

        _manager.Deselect();
        _hitOnDown = _manager.HitTest(pos);
        _drawStart = pos;
        _isDrawing = true;
        AnnotationCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var cur = e.GetPosition(AnnotationCanvas);

        if (_manager.IsDragging)
        {
            _manager.UpdateDrag(cur);
            return;
        }

        if (!_isDrawing)
        {
            var cur2 = _manager.HoverCursor(cur);
            if (cur2 == Cursors.Cross && _currentTool is TextTool)
                cur2 = Cursors.IBeam;
            AnnotationCanvas.Cursor = cur2;
            return;
        }

        if (!_isDrawing) return;

        if (_previewElement != null)
            AnnotationCanvas.Children.Remove(_previewElement);

        _previewElement = _currentTool.Preview(_drawStart, cur);
        if (_previewElement != null)
            AnnotationCanvas.Children.Add(_previewElement);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_manager.IsDragging)
        {
            _manager.EndDrag();
            AnnotationCanvas.ReleaseMouseCapture();
            AnnotationCanvas.Cursor = _currentTool is TextTool ? Cursors.IBeam : Cursors.Cross;
            return;
        }

        if (!_isDrawing) return;
        _isDrawing = false;
        AnnotationCanvas.ReleaseMouseCapture();

        if (_previewElement != null)
        {
            AnnotationCanvas.Children.Remove(_previewElement);
            _previewElement = null;
        }

        var cur = e.GetPosition(AnnotationCanvas);

        // Short click on an existing annotation → select it instead of drawing
        if (_hitOnDown != null)
        {
            var d = cur - _drawStart;
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) < 5)
            {
                _manager.Select(_hitOnDown);
                SwitchToAnnotationTool(_hitOnDown.Tool);
                _hitOnDown = null;
                return;
            }
            _hitOnDown = null;
        }

        if (_currentTool is NumberTool numTool)
        {
            var numAnn = numTool.CommitAt(cur);
            if (numAnn != null)
            {
                _manager.Add(numAnn);
                _manager.Select(numAnn);
                SwitchToAnnotationTool(_numberTool);
                PushAnnotationUndo(numAnn);
            }
            return;
        }

        if (_currentTool is SelectTool)
        {
            var existing = _manager.Annotations.OfType<SelectAnnotation>().FirstOrDefault();
            var newSel   = _currentTool.Commit(_drawStart, cur);
            if (newSel != null)
            {
                if (existing != null) _manager.Delete(existing);
                _manager.Add(newSel);
                _manager.Select(newSel);
            }
            else if (existing != null)
            {
                _manager.Delete(existing);
            }
            return;
        }

        if (_currentTool is TextTool textTool)
        {
            TextAnnotation? ann = null;
            ann = textTool.PlaceOn(AnnotationCanvas, _drawStart,
                onCancel: () => _manager.Delete(ann!),
                onCommit: committed =>
                {
                    _manager.Select(committed);
                    SwitchToAnnotationTool(_textTool);
                    PushAnnotationUndo(committed);
                });
            _manager.Track(ann);
            Dispatcher.BeginInvoke(() => AttachEditingHandles(ann));
            return;
        }

        var drag = cur - _drawStart;
        if (Math.Sqrt(drag.X * drag.X + drag.Y * drag.Y) < 5) return;

        var committed = _currentTool.Commit(_drawStart, cur);
        if (committed != null)
        {
            _manager.Add(committed);
            _manager.Select(committed);
            SwitchToAnnotationTool(_currentTool);
            PushAnnotationUndo(committed);
        }
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private void ZoomIn_Click(object sender, RoutedEventArgs e)    => SetZoom(_zoomLevel * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e)   => SetZoom(_zoomLevel / 1.25);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        var newZoom = Math.Clamp(e.Delta > 0 ? _zoomLevel * 1.25 : _zoomLevel / 1.25, 0.1, 8.0);

        // Cursor position in viewport and in unscaled content coordinates (before zoom)
        var mouseInViewport = e.GetPosition(Scroller);
        var contentPoint    = e.GetPosition(ZoomedContent);

        SetZoom(newZoom);

        // Scroll so the content point under the cursor stays at the same viewport position
        Scroller.ScrollToHorizontalOffset(contentPoint.X * ZoomTransform.ScaleX - mouseInViewport.X);
        Scroller.ScrollToVerticalOffset  (contentPoint.Y * ZoomTransform.ScaleY - mouseInViewport.Y);

        e.Handled = true;
    }

    private void SetZoom(double zoom)
    {
        _zoomLevel           = Math.Clamp(zoom, 0.1, 8.0);
        ZoomTransform.ScaleX = _zoomLevel / _dpiScale;
        ZoomTransform.ScaleY = _zoomLevel / _dpiScale;
        ZoomLabel.Text       = $"{(int)Math.Round(_zoomLevel * 100)}%";
    }

    // ── Selection image operations ────────────────────────────────────────────

    private void ApplyCropToSelection()
    {
        if (_manager.Selected is not SelectAnnotation ann) return;
        PushBitmapUndo();
        var r = ShapeHelper.NormRect(ann.Start, ann.End);
        int x = Math.Max(0, (int)r.X);
        int y = Math.Max(0, (int)r.Y);
        int w = Math.Min((int)r.Width,  _bitmapSource.PixelWidth  - x);
        int h = Math.Min((int)r.Height, _bitmapSource.PixelHeight - y);
        if (w <= 0 || h <= 0) return;

        _bitmapSource           = new CroppedBitmap(_bitmapSource, new Int32Rect(x, y, w, h));
        MainImage.Source        = _bitmapSource;
        AnnotationCanvas.Width  = w;
        AnnotationCanvas.Height = h;
        _manager.Clear();
    }

    private void ApplyBlurToSelection(double percent)
    {
        if (_manager.Selected is not SelectAnnotation ann) return;
        PushBitmapUndo();
        var r = ShapeHelper.NormRect(ann.Start, ann.End);
        int x = Math.Max(0, (int)r.X);
        int y = Math.Max(0, (int)r.Y);
        int w = Math.Min((int)r.Width,  _bitmapSource.PixelWidth  - x);
        int h = Math.Min((int)r.Height, _bitmapSource.PixelHeight - y);
        if (w <= 0 || h <= 0) return;

        var region = new CroppedBitmap(_bitmapSource, new Int32Rect(x, y, w, h));
        var img = new Image
        {
            Source = region,
            Width  = w,
            Height = h,
            Effect = new BlurEffect { Radius = percent * 0.3 },
        };
        img.Measure(new Size(w, h));
        img.Arrange(new Rect(0, 0, w, h));

        var blurRtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        blurRtb.Render(img);

        var rtb = new RenderTargetBitmap(
            _bitmapSource.PixelWidth, _bitmapSource.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(_bitmapSource,
                new Rect(0, 0, _bitmapSource.PixelWidth, _bitmapSource.PixelHeight));
            dc.DrawImage(blurRtb, new Rect(x, y, w, h));
        }
        rtb.Render(dv);
        _bitmapSource    = rtb;
        MainImage.Source = _bitmapSource;
    }

    private void DeleteSelection()
    {
        if (_manager.Selected is not SelectAnnotation ann) return;
        PushBitmapUndo();
        var r = ShapeHelper.NormRect(ann.Start, ann.End);
        int x = Math.Max(0, (int)r.X);
        int y = Math.Max(0, (int)r.Y);
        int w = Math.Min((int)r.Width,  _bitmapSource.PixelWidth  - x);
        int h = Math.Min((int)r.Height, _bitmapSource.PixelHeight - y);
        if (w <= 0 || h <= 0) return;

        var rtb = new RenderTargetBitmap(
            _bitmapSource.PixelWidth, _bitmapSource.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(_bitmapSource,
                new Rect(0, 0, _bitmapSource.PixelWidth, _bitmapSource.PixelHeight));
            dc.DrawRectangle(Brushes.Black, null, new Rect(x, y, w, h));
        }
        rtb.Render(dv);
        _bitmapSource    = rtb;
        MainImage.Source = _bitmapSource;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoLastAction();

    private void UndoLastAction()
    {
        if (_undoStack.TryPop(out var action))
            action();
    }

    private void PushBitmapUndo()
    {
        var bmp = _bitmapSource;
        var w   = AnnotationCanvas.Width;
        var h   = AnnotationCanvas.Height;
        _undoStack.Push(() =>
        {
            _bitmapSource           = bmp;
            MainImage.Source        = bmp;
            AnnotationCanvas.Width  = w;
            AnnotationCanvas.Height = h;
            UpdateCropHandlePositions();
        });
    }

    private void PushAnnotationUndo(Annotation ann) =>
        _undoStack.Push(() => _manager.Delete(ann));
    private void Copy_Click(object sender, RoutedEventArgs e) => Clipboard.SetImage(RenderToRtb());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title       = "Save Screenshot",
            Filter      = "PNG image|*.png|JPEG image|*.jpg;*.jpeg",
            FilterIndex = 1,
            FileName    = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
        };
        if (dlg.ShowDialog() != true) return;

        var rtb = RenderToRtb();
        BitmapEncoder enc = dlg.FilterIndex == 2
            ? new JpegBitmapEncoder { QualityLevel = 95 }
            : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(dlg.FileName);
        enc.Save(fs);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            { UndoLastAction(); e.Handled = true; }
        else if (e.Key == Key.Delete && _manager.Selected != null)
        {
            if (_manager.Selected is SelectAnnotation)
                { DeleteSelection(); e.Handled = true; }
            else
                { _manager.Delete(_manager.Selected); e.Handled = true; }
        }
        else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            { Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            { Copy_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox)
            { _manager.Deselect(); e.Handled = true; }
    }

    private void AttachEditingHandles(TextAnnotation ann)
    {
        if (ann.Element is not FrameworkElement fe) return;
        ann.End = new Point(Canvas.GetLeft(fe) + fe.ActualWidth, Canvas.GetTop(fe) + fe.ActualHeight);
        _manager.Select(ann);
        fe.SizeChanged += (_, _) =>
        {
            if (!ann.IsEditing) return;
            ann.End = new Point(Canvas.GetLeft(fe) + fe.ActualWidth, Canvas.GetTop(fe) + fe.ActualHeight);
            _manager.UpdateHandles();
        };
    }

    // ── Canvas crop handles ───────────────────────────────────────────────────

    private static Cursor CursorForCropHandle(CropHandle ch) => ch switch
    {
        CropHandle.TL or CropHandle.BR => Cursors.SizeNWSE,
        CropHandle.TR or CropHandle.BL => Cursors.SizeNESW,
        CropHandle.TC or CropHandle.BC => Cursors.SizeNS,
        _                              => Cursors.SizeWE,
    };

    private void InitCropHandles()
    {
        foreach (CropHandle ch in Enum.GetValues<CropHandle>())
        {
            var r = new Rectangle
            {
                Width           = 12,
                Height          = 12,
                Fill            = Brushes.White,
                Stroke          = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                StrokeThickness = 1.5,
                Cursor          = CursorForCropHandle(ch),
                Tag             = ch,
            };
            r.MouseLeftButtonDown += CropHandle_MouseDown;
            r.MouseMove           += CropHandle_MouseMove;
            r.MouseLeftButtonUp   += CropHandle_MouseUp;
            CropHandleCanvas.Children.Add(r);
            _cropHandles[ch] = r;
        }
        UpdateCropHandlePositions();
    }

    private void UpdateCropHandlePositions()
    {
        double w = AnnotationCanvas.Width;
        double h = AnnotationCanvas.Height;
        const double half = 6;

        void Place(CropHandle ch, double x, double y)
        {
            Canvas.SetLeft(_cropHandles[ch], x - half);
            Canvas.SetTop (_cropHandles[ch], y - half);
        }

        Place(CropHandle.TL, 0,   0);
        Place(CropHandle.TC, w/2, 0);
        Place(CropHandle.TR, w,   0);
        Place(CropHandle.ML, 0,   h/2);
        Place(CropHandle.MR, w,   h/2);
        Place(CropHandle.BL, 0,   h);
        Place(CropHandle.BC, w/2, h);
        Place(CropHandle.BR, w,   h);
    }

    private void CropHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _activeCropHandle = (CropHandle)((Rectangle)sender).Tag!;
        _cropDragStart    = e.GetPosition(AnnotationCanvas);
        _cropOrigW        = AnnotationCanvas.Width;
        _cropOrigH        = AnnotationCanvas.Height;
        _cropNewX = _cropNewY = 0;
        _cropNewW = _cropOrigW;
        _cropNewH = _cropOrigH;
        _isCropDragging   = true;
        ((Rectangle)sender).CaptureMouse();
        e.Handled = true;
    }

    private void CropHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isCropDragging) return;
        var cur  = e.GetPosition(AnnotationCanvas);
        double dx = cur.X - _cropDragStart.X;
        double dy = cur.Y - _cropDragStart.Y;

        double x = 0, y = 0, w = _cropOrigW, h = _cropOrigH;
        switch (_activeCropHandle)
        {
            case CropHandle.TL:
                x = Math.Clamp(dx, 0, _cropOrigW - 10); y = Math.Clamp(dy, 0, _cropOrigH - 10);
                w = _cropOrigW - x; h = _cropOrigH - y; break;
            case CropHandle.TC:
                y = Math.Clamp(dy, 0, _cropOrigH - 10); h = _cropOrigH - y; break;
            case CropHandle.TR:
                y = Math.Clamp(dy, 0, _cropOrigH - 10); h = _cropOrigH - y;
                w = Math.Clamp(_cropOrigW + dx, 10, _cropOrigW); break;
            case CropHandle.ML:
                x = Math.Clamp(dx, 0, _cropOrigW - 10); w = _cropOrigW - x; break;
            case CropHandle.MR:
                w = Math.Clamp(_cropOrigW + dx, 10, _cropOrigW); break;
            case CropHandle.BL:
                x = Math.Clamp(dx, 0, _cropOrigW - 10); w = _cropOrigW - x;
                h = Math.Clamp(_cropOrigH + dy, 10, _cropOrigH); break;
            case CropHandle.BC:
                h = Math.Clamp(_cropOrigH + dy, 10, _cropOrigH); break;
            case CropHandle.BR:
                w = Math.Clamp(_cropOrigW + dx, 10, _cropOrigW);
                h = Math.Clamp(_cropOrigH + dy, 10, _cropOrigH); break;
        }
        _cropNewX = x; _cropNewY = y; _cropNewW = w; _cropNewH = h;
        UpdateCropPreview();
        e.Handled = true;
    }

    private void CropHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isCropDragging) return;
        ((Rectangle)sender).ReleaseMouseCapture();
        _isCropDragging = false;
        RemoveCropPreview();

        bool changed = _cropNewX > 0.5 || _cropNewY > 0.5
                    || _cropNewW < _cropOrigW - 0.5
                    || _cropNewH < _cropOrigH - 0.5;
        if (changed)
        {
            PushBitmapUndo();
            ApplyCanvasCrop((int)Math.Round(_cropNewX), (int)Math.Round(_cropNewY),
                            (int)Math.Round(_cropNewW), (int)Math.Round(_cropNewH));
        }
        e.Handled = true;
    }

    private void UpdateCropPreview()
    {
        if (_cropPreview == null)
        {
            _cropPreview = new Rectangle
            {
                Stroke           = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                StrokeThickness  = 1.5,
                StrokeDashArray  = new DoubleCollection { 6, 3 },
                Fill             = new SolidColorBrush(Color.FromArgb(25, 137, 180, 250)),
                IsHitTestVisible = false,
            };
            CropHandleCanvas.Children.Add(_cropPreview);
        }
        Canvas.SetLeft(_cropPreview, _cropNewX);
        Canvas.SetTop (_cropPreview, _cropNewY);
        _cropPreview.Width  = _cropNewW;
        _cropPreview.Height = _cropNewH;
    }

    private void RemoveCropPreview()
    {
        if (_cropPreview == null) return;
        CropHandleCanvas.Children.Remove(_cropPreview);
        _cropPreview = null;
    }

    private void ApplyCanvasCrop(int x, int y, int w, int h)
    {
        w = Math.Min(w, _bitmapSource.PixelWidth  - x);
        h = Math.Min(h, _bitmapSource.PixelHeight - y);
        if (w < 1 || h < 1) return;

        _bitmapSource           = new CroppedBitmap(_bitmapSource, new Int32Rect(x, y, w, h));
        MainImage.Source        = _bitmapSource;
        AnnotationCanvas.Width  = w;
        AnnotationCanvas.Height = h;
        _manager.Clear();
        UpdateCropHandlePositions();
    }

    // ── Redraw / Render ───────────────────────────────────────────────────────

    private void RedrawSelected()
    {
        if (_manager.Selected == null) return;
        var ann = _manager.Selected;
        if (ann is TextAnnotation { IsEditing: true })
        {
            _textTool.UpdateEditingTextBox();
            return;
        }
        ann.Tool.ApplyTo(ann);
        int idx = AnnotationCanvas.Children.IndexOf(ann.Element);
        AnnotationCanvas.Children.Remove(ann.Element);
        ann.Element = ann.Tool.Render(ann);
        if (idx >= 0) AnnotationCanvas.Children.Insert(idx, ann.Element);
        else          AnnotationCanvas.Children.Add(ann.Element);
        _manager.UpdateHandles();
    }

    private RenderTargetBitmap RenderToRtb()
    {
        int w   = _bitmapSource.PixelWidth;
        int h   = _bitmapSource.PixelHeight;
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawImage(_bitmapSource, new Rect(0, 0, w, h));
        rtb.Render(dv);

        _manager.WithoutHandles(() => rtb.Render(AnnotationCanvas));
        return rtb;
    }
}
