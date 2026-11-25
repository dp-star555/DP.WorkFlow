using System.Drawing.Drawing2D;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 设计稿里的卡片：表面色底、细线描边、主题圆角。内容区四周让出圆角占用的距离；
/// 卡片里的宿主控件按表面色着色（见 <see cref="WorkflowWinFormsStyle"/>）。
/// </summary>
public class WorkflowCard : Panel
{
    /// <summary>创建卡片。</summary>
    public WorkflowCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        ApplyInset();
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyInset();
    }

    /// <inheritdoc />
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyInset();
    }

    /// <summary>主题换了以后圆角可能不同，重新留边。</summary>
    internal void ApplyInset()
    {
        var inset = LogicalToDeviceUnits(Math.Max(2, (int)Math.Ceiling(WorkflowWinFormsTheme.Palette.Radius * .3) + 1));
        if (Padding.All != inset) Padding = new Padding(inset);
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        e.Graphics.Clear(Parent?.BackColor ?? theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(new RectangleF(.5f, .5f, Width - 1f, Height - 1f), LogicalToDeviceUnits(WorkflowWinFormsTheme.Palette.Radius));
        using (var fill = new SolidBrush(theme.Container))
            e.Graphics.FillPath(fill, path);
        using var pen = new Pen(theme.BorderSecondary);
        e.Graphics.DrawPath(pen, path);
    }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (d <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>卡片标题行：粗体标题、灰色副标题，下面一条分隔线（设计稿 42 px 高）。</summary>
public sealed class WorkflowCardHeader : Control
{
    private string? _subtitle;

    /// <summary>创建标题行。</summary>
    /// <param name="title">标题。</param>
    /// <param name="subtitle">副标题；可为空。</param>
    public WorkflowCardHeader(string title, string? subtitle = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Text = title;
        _subtitle = subtitle;
        Dock = DockStyle.Top;
        Height = LogicalToDeviceUnits(42);
    }

    /// <summary>副标题。</summary>
    public string? Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Invalidate(); }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        e.Graphics.Clear(BackColor);
        var x = LogicalToDeviceUnits(14);
        using var bold = new Font(Font, FontStyle.Bold);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var titleWidth = TextRenderer.MeasureText(e.Graphics, Text, bold, Size.Empty, flags).Width;
        TextRenderer.DrawText(e.Graphics, Text, bold, new Rectangle(x, 0, Width - x, Height - 1), theme.Text, flags);
        if (!string.IsNullOrEmpty(_subtitle))
        {
            var left = x + titleWidth + LogicalToDeviceUnits(4);
            TextRenderer.DrawText(e.Graphics, _subtitle, Font, new Rectangle(left, 0, Math.Max(0, Width - left), Height - 1), theme.TextSecondary, flags);
        }
        using var pen = new Pen(theme.BorderSecondary);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
