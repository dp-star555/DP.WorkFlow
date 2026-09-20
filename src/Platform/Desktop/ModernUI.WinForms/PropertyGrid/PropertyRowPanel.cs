using System.Drawing.Drawing2D;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

/// <summary>使用圆角背景呈现属性行的悬停和选中状态。</summary>
internal sealed class PropertyRowPanel : BufferedTableLayoutPanel
{
    private bool _hovered;
    private bool _selected;

    public Color OutsideColor { get; set; } = SystemColors.Control;
    public Color NormalColor { get; set; }
    public Color HoverColor { get; set; }
    public Color SelectedColor { get; set; }
    public int CornerRadius { get; set; } = 7;

    public bool Hovered
    {
        get => _hovered;
        set
        {
            if (_hovered == value) return;
            _hovered = value;
            Invalidate();
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    public PropertyRowPanel()
    {
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(OutsideColor);
        // 填充完整客户区。旧实现无边框却主动缩小一个 DPI 像素，导致右侧和底部残留黑线。
        var bounds = new RectangleF(0, 0, Math.Max(0, ClientSize.Width), Math.Max(0, ClientSize.Height));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var path = CreateRoundedRectangle(bounds, CornerRadius * DeviceDpi / 96f);
        using var brush = new SolidBrush(Selected ? SelectedColor : Hovered ? HoverColor : NormalColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(Math.Min(bounds.Width, bounds.Height), Math.Max(0, radius * 2));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
