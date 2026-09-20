using System.Drawing.Drawing2D;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 可交互图像视口。初始自适应显示，滚轮以光标为中心缩放，拖拽平移，双击恢复自适应。
/// </summary>
public sealed class WorkflowImageViewport : Control
{
    private Image? _image;
    private double _zoomFactor = 1;
    private PointF _panOffset;
    private Point _lastPanPoint;
    private bool _panning;

    public WorkflowImageViewport()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint, true);
        BackColor = Color.Black;
        TabStop = true;
    }

    /// <summary>当前图像。视口不负责释放图像；替换后由调用方释放旧实例。</summary>
    public Image? Image
    {
        get => _image;
        set
        {
            var sizeChanged = _image is null || value is null || _image.Size != value.Size;
            _image = value;
            if (sizeChanged) ResetView();
            else Invalidate();
        }
    }

    /// <summary>相对于自适应比例的缩放倍数。</summary>
    public double ZoomFactor => _zoomFactor;

    /// <summary>允许使用左键平移。ROI 编辑器应保持关闭，仅使用中键或右键平移。</summary>
    public bool AllowLeftButtonPan { get; set; } = true;

    /// <summary>是否允许左键双击恢复自适应视图。</summary>
    public bool ResetOnDoubleClick { get; set; } = true;

    /// <summary>图像在视口客户区中的实际绘制矩形。</summary>
    public RectangleF ImageBounds
    {
        get
        {
            if (_image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return RectangleF.Empty;
            var scale = FitScale * _zoomFactor;
            var width = (float)(_image.Width * scale);
            var height = (float)(_image.Height * scale);
            return new RectangleF(
                (ClientSize.Width - width) / 2f + _panOffset.X,
                (ClientSize.Height - height) / 2f + _panOffset.Y,
                width,
                height);
        }
    }

    public event EventHandler? ViewChanged;

    /// <summary>恢复完整图像自适应显示。</summary>
    public void ResetView()
    {
        _zoomFactor = 1;
        _panOffset = PointF.Empty;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>以客户区中的指定点为中心按倍数缩放。</summary>
    public void ZoomAt(double multiplier, PointF clientAnchor)
    {
        if (_image is null || multiplier <= 0 || !double.IsFinite(multiplier)) return;
        var oldBounds = ImageBounds;
        if (oldBounds.Width <= 0 || oldBounds.Height <= 0) return;
        var imageX = (clientAnchor.X - oldBounds.X) / oldBounds.Width * _image.Width;
        var imageY = (clientAnchor.Y - oldBounds.Y) / oldBounds.Height * _image.Height;
        var next = Math.Clamp(_zoomFactor * multiplier, 0.05, 64);
        if (Math.Abs(next - _zoomFactor) < 0.000001) return;
        _zoomFactor = next;
        var scale = FitScale * _zoomFactor;
        var centeredX = (ClientSize.Width - _image.Width * scale) / 2;
        var centeredY = (ClientSize.Height - _image.Height * scale) / 2;
        _panOffset = new PointF(
            (float)(clientAnchor.X - imageX * scale - centeredX),
            (float)(clientAnchor.Y - imageY * scale - centeredY));
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按客户区像素移动当前视图。</summary>
    public void PanBy(float deltaX, float deltaY)
    {
        if (_image is null) return;
        _panOffset = new PointF(_panOffset.X + deltaX, _panOffset.Y + deltaY);
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把客户区坐标转换为图像坐标。</summary>
    public PointF ClientToImage(PointF point, bool clampToImage = false)
    {
        if (_image is null) return PointF.Empty;
        var bounds = ImageBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return PointF.Empty;
        var x = (point.X - bounds.X) / bounds.Width * _image.Width;
        var y = (point.Y - bounds.Y) / bounds.Height * _image.Height;
        if (clampToImage)
        {
            x = Math.Clamp(x, 0, _image.Width - 1);
            y = Math.Clamp(y, 0, _image.Height - 1);
        }
        return new PointF(x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is null) return;
        var bounds = ImageBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        e.Graphics.InterpolationMode = _zoomFactor >= 4
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_image, bounds);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta == 0) return;
        var steps = e.Delta / (double)SystemInformation.MouseWheelScrollDelta;
        ZoomAt(Math.Pow(1.2, steps), e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (!CanPan(e.Button)) return;
        _panning = true;
        _lastPanPoint = e.Location;
        Capture = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_panning) return;
        PanBy(e.X - _lastPanPoint.X, e.Y - _lastPanPoint.Y);
        _lastPanPoint = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_panning || !CanPan(e.Button)) return;
        _panning = false;
        Capture = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (ResetOnDoubleClick && e.Button == MouseButtons.Left) ResetView();
    }

    private double FitScale => _image is null
        ? 1
        : Math.Min(ClientSize.Width / (double)_image.Width, ClientSize.Height / (double)_image.Height);

    private bool CanPan(MouseButtons button) =>
        button is MouseButtons.Middle or MouseButtons.Right
        || AllowLeftButtonPan && button == MouseButtons.Left;
}
