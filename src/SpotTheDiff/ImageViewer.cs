using System.Drawing.Drawing2D;

namespace SpotTheDiff;

/// <summary>Image display with mouse-wheel zoom, drag to pan, double-click to fit, and file drop.</summary>
internal sealed class ImageViewer : Control
{
    public readonly record struct ViewState(bool Fit, float Zoom, PointF Offset);

    private Image? _image;
    private bool _fit = true;
    private float _zoom = 1f;
    private PointF _offset;
    private bool _dragging;
    private Point _dragStart;
    private PointF _dragOffset;

    public ImageViewer()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(45, 45, 48);
        ForeColor = Color.Gainsboro;
        AllowDrop = true;
    }

    public string Placeholder { get; set; } = "";

    /// <summary>Raised after the user pans/zooms, so a linked viewer can follow.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Raised with the path of a dropped file.</summary>
    public event EventHandler<string>? FileDropped;

    /// <summary>The viewer does not own the image; the caller disposes it.</summary>
    public Image? Image
    {
        get => _image;
        set
        {
            bool sameSize = _image != null && value != null && _image.Size == value.Size;
            _image = value;
            if (!sameSize) _fit = true;
            Invalidate();
        }
    }

    public ViewState State
    {
        get
        {
            var (z, o) = Current();
            return new ViewState(_fit, z, o);
        }
    }

    public void ApplyState(ViewState s)
    {
        _fit = s.Fit;
        _zoom = s.Zoom;
        _offset = s.Offset;
        Invalidate();
    }

    public void FitToWindow()
    {
        _fit = true;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private (float Zoom, PointF Offset) Current()
    {
        if (_image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return (1f, PointF.Empty);
        if (!_fit) return (_zoom, _offset);

        float z = Math.Min((float)ClientSize.Width / _image.Width, (float)ClientSize.Height / _image.Height);
        return (z, new PointF((ClientSize.Width - _image.Width * z) / 2f, (ClientSize.Height - _image.Height * z) / 2f));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_image == null)
        {
            if (Placeholder.Length > 0)
            {
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var brush = new SolidBrush(Color.FromArgb(150, 150, 150));
                g.DrawString(Placeholder, Font, brush, ClientRectangle, sf);
            }
            return;
        }

        var (z, o) = Current();
        g.InterpolationMode = z < 1f ? InterpolationMode.HighQualityBicubic
            : z >= 2f ? InterpolationMode.NearestNeighbor
            : InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_image, new RectangleF(o.X, o.Y, _image.Width * z, _image.Height * z));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image == null) return;

        var (z, o) = Current();
        float nz = Math.Clamp(z * (e.Delta > 0 ? 1.25f : 0.8f), 0.02f, 32f);
        float ix = (e.X - o.X) / z, iy = (e.Y - o.Y) / z;
        _zoom = nz;
        _offset = new PointF(e.X - ix * nz, e.Y - iy * nz);
        _fit = false;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || _image == null) return;

        var (z, o) = Current();
        _fit = false;
        _zoom = z;
        _offset = o;
        _dragging = true;
        _dragStart = e.Location;
        _dragOffset = o;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _offset = new PointF(_dragOffset.X + e.X - _dragStart.X, _dragOffset.Y + e.Y - _dragStart.Y);
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        FitToWindow();
    }

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = drgevent.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (drgevent.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            FileDropped?.Invoke(this, files[0]);
    }
}
