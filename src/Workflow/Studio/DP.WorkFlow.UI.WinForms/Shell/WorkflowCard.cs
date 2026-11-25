using ModernUI.WinForms;
using System.Drawing.Drawing2D;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 停靠在一边的块：宽（左右停靠）或高（上下停靠）按 <see cref="LogicalExtent"/> 和当前 DPI 换算。
/// 宿主按字体自动缩放时不缩它的尺寸，否则会和 DPI 换算叠在一起，越放越大。
/// </summary>
public class WorkflowBlock : Panel
{
    private int _extent;
    private Padding? _padding;

    /// <summary>停靠方向上的逻辑尺寸（96 DPI 下的像素）；0 表示不管。</summary>
    public int LogicalExtent
    {
        get => _extent;
        set { _extent = value; ApplyMetrics(); }
    }

    /// <summary>逻辑内边距（96 DPI 下的像素）；为空时不管。</summary>
    public Padding? LogicalPadding
    {
        get => _padding;
        set { _padding = value; ApplyMetrics(); }
    }

    /// <summary>按当前 DPI 重新设定尺寸和留边。</summary>
    protected internal virtual void ApplyMetrics()
    {
        if (_padding is { } logical)
        {
            var padding = new Padding(LogicalToDeviceUnits(logical.Left), LogicalToDeviceUnits(logical.Top), LogicalToDeviceUnits(logical.Right), LogicalToDeviceUnits(logical.Bottom));
            if (Padding != padding) Padding = padding;
        }
        if (_extent <= 0) return;
        var size = LogicalToDeviceUnits(_extent);
        if (Dock is DockStyle.Left or DockStyle.Right) Width = size;
        else if (Dock is DockStyle.Top or DockStyle.Bottom) Height = size;
    }

    /// <inheritdoc />
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);
        ApplyMetrics();
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics();
    }

    /// <inheritdoc />
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
    }

    /// <inheritdoc />
    protected override void OnDockChanged(EventArgs e)
    {
        base.OnDockChanged(e);
        ApplyMetrics();
    }
}

/// <summary>
/// 设计稿里的卡片：表面色底、细线描边、主题圆角。内容区四周让出圆角占用的距离；
/// 卡片里的宿主控件按表面色着色（见 <see cref="WorkflowWinFormsStyle"/>）。
/// </summary>
public class WorkflowCard : WorkflowBlock
{
    /// <summary>创建卡片。</summary>
    public WorkflowCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        ApplyInset();
    }

    /// <inheritdoc />
    protected internal override void ApplyMetrics()
    {
        base.ApplyMetrics();
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
public sealed class WorkflowCardHeader : WorkflowBlock
{
    private readonly FlowLayoutPanel _actions = new()
    {
        Dock = DockStyle.Right,
        AutoSize = true,
        WrapContents = false,
        FlowDirection = FlowDirection.RightToLeft,
        BackColor = Color.Transparent
    };
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
        LogicalExtent = 42;
        Controls.Add(_actions);
    }

    /// <summary>在标题行右边加一个操作（先加的在最右），按 <see cref="WorkflowSizing"/> 定尺寸。</summary>
    /// <param name="control">控件。</param>
    public T AddAction<T>(T control) where T : Control
    {
        _actions.Controls.Add(WorkflowSizing.Fit(control));
        return control;
    }

    /// <inheritdoc />
    protected internal override void ApplyMetrics()
    {
        base.ApplyMetrics();
        _actions.Padding = new Padding(0, Math.Max(0, (Height - 1 - LogicalToDeviceUnits(38)) / 2), LogicalToDeviceUnits(6), 0);
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
        var right = (_actions.Controls.Count > 0 ? _actions.Left : Width) - LogicalToDeviceUnits(8);
        var titleWidth = Math.Min(Math.Max(0, right - x), TextRenderer.MeasureText(e.Graphics, Text, bold, new Size(int.MaxValue, Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width);
        TextRenderer.DrawText(e.Graphics, Text, bold, new Rectangle(x, 0, titleWidth, Height - 1), theme.Text, flags);
        if (!string.IsNullOrEmpty(_subtitle))
        {
            var left = x + titleWidth + LogicalToDeviceUnits(8);
            TextRenderer.DrawText(e.Graphics, _subtitle, Font, new Rectangle(left, 0, Math.Max(0, right - left), Height - 1), theme.TextSecondary, flags);
        }
        using var pen = new Pen(theme.BorderSecondary);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
