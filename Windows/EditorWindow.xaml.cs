using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    private System.Windows.Rect _cropRect;
    private Rectangle?          _cropBorder;

    private readonly AnnotationManager _manager;
    private readonly ArrowTool         _arrowTool;
    private readonly RectTool          _rectTool;
    private readonly TextTool          _textTool;
    private readonly CropTool          _cropTool;

    public EditorWindow(BitmapSource bitmapSource)
    {
        InitializeComponent();

        _bitmapSource = bitmapSource;
        _dpiScale     = ScreenCaptureService.GetDpiScale();

        var settings     = new RegistrySettingsStore();
        _arrowTool       = new ArrowTool(settings);
        _rectTool        = new RectTool(settings);
        _textTool        = new TextTool(settings);
        _cropTool        = new CropTool();

        foreach (var t in new ITool[] { _arrowTool, _rectTool, _textTool })
            t.PropertiesChanged += RedrawSelected;

        _currentTool = _arrowTool;
        _manager     = new AnnotationManager(AnnotationCanvas);

        MainImage.Source        = bitmapSource;
        AnnotationCanvas.Width  = bitmapSource.PixelWidth;
        AnnotationCanvas.Height = bitmapSource.PixelHeight;

        ToolPropertiesHost.Content = _currentTool.PropertiesPanel;
        SetZoom(1.0);

        PreviewKeyDown += OnKeyDown;
    }

    // ── Tool switching ────────────────────────────────────────────────────────

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (BtnApplyCrop == null || _switchingTool) return;

        var tag = ((ToggleButton)sender).Tag!.ToString()!;
        UncheckOtherTools((ToggleButton)sender);

        _currentTool = tag switch
        {
            "Arrow" => _arrowTool,
            "Rect"  => _rectTool,
            "Text"  => _textTool,
            "Crop"  => _cropTool,
            _       => _arrowTool,
        };

        ToolPropertiesHost.Content     = _currentTool.PropertiesPanel;
        BtnApplyCrop.Visibility        = tag == "Crop" ? Visibility.Visible : Visibility.Collapsed;
        AnnotationCanvas.Cursor        = tag == "Text" ? Cursors.IBeam : Cursors.Cross;

        if (tag != "Crop") RemoveCropPreview();
        _manager.Deselect();
    }

    private void SwitchToAnnotationTool(ITool tool)
    {
        _currentTool               = tool;
        ToolPropertiesHost.Content = tool.PropertiesPanel;

        _switchingTool = true;
        foreach (var btn in new[] { BtnArrow, BtnRect, BtnText, BtnCrop })
            if (btn != null) btn.IsChecked = btn.Tag?.ToString() == tool.Name;
        _switchingTool = false;
    }

    private void UncheckOtherTools(ToggleButton active)
    {
        foreach (var btn in new[] { BtnArrow, BtnRect, BtnText, BtnCrop })
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

        if (_currentTool is not CropTool)
        {
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
        }

        _manager.Deselect();
        _hitOnDown = _currentTool is CropTool ? null : _manager.HitTest(pos);
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

        if (!_isDrawing && _currentTool is not CropTool)
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

        if (_currentTool is CropTool)
        {
            var r = ShapeHelper.NormRect(_drawStart, cur);
            if (r.Width > 4 && r.Height > 4)
            {
                _cropRect   = r;
                _cropBorder = _cropTool.MakeCropRect(_drawStart, cur);
                AnnotationCanvas.Children.Add(_cropBorder);
            }
            return;
        }

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

        if (_currentTool is TextTool textTool)
        {
            TextAnnotation? ann = null;
            ann = textTool.PlaceOn(AnnotationCanvas, _drawStart,
                onCancel: () => _manager.Delete(ann!),
                onCommit: committed =>
                {
                    _manager.Select(committed);
                    SwitchToAnnotationTool(_textTool);
                });
            _manager.Track(ann);
            Dispatcher.BeginInvoke(() => AttachEditingHandles(ann));
            return;
        }

        var committed = _currentTool.Commit(_drawStart, cur);
        if (committed != null) _manager.Add(committed);
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

    // ── Crop ──────────────────────────────────────────────────────────────────

    private void RemoveCropPreview()
    {
        if (_cropBorder == null) return;
        AnnotationCanvas.Children.Remove(_cropBorder);
        _cropBorder = null;
    }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_cropBorder == null || _cropRect.Width < 4 || _cropRect.Height < 4) return;

        int x = Math.Max(0, (int)_cropRect.X);
        int y = Math.Max(0, (int)_cropRect.Y);
        int w = Math.Min((int)_cropRect.Width,  _bitmapSource.PixelWidth  - x);
        int h = Math.Min((int)_cropRect.Height, _bitmapSource.PixelHeight - y);
        if (w <= 0 || h <= 0) return;

        _bitmapSource           = new CroppedBitmap(_bitmapSource, new Int32Rect(x, y, w, h));
        MainImage.Source        = _bitmapSource;
        AnnotationCanvas.Width  = w;
        AnnotationCanvas.Height = h;

        _manager.Clear();
        _cropBorder             = null;
        BtnApplyCrop.Visibility = Visibility.Collapsed;
        BtnArrow.IsChecked      = true;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void Undo_Click(object sender, RoutedEventArgs e) => _manager.Undo();
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
            { _manager.Undo(); e.Handled = true; }
        else if (e.Key == Key.Delete && _manager.Selected != null)
            { _manager.Delete(_manager.Selected); e.Handled = true; }
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
