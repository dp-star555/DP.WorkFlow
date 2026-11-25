using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 页面顶部的指标条（设计稿各页第一张卡）：左边粗体标题和灰色副标题，接一排“大数字 + 小说明”，最右边一行灰字。
/// 右边可以再放宿主控件（下拉框、按钮），指标只画在它们左边。
/// </summary>
public sealed class WorkflowMetricStrip : WorkflowCard
{
    private readonly FlowLayoutPanel _tools = new()
    {
        Dock = DockStyle.Right,
        AutoSize = true,
        WrapContents = false,
        FlowDirection = FlowDirection.RightToLeft,
        BackColor = Color.Transparent
    };
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _note = string.Empty;
    private IReadOnlyList<WorkflowMetric> _metrics = Array.Empty<WorkflowMetric>();

    /// <summary>创建指标条（设计稿 64 px 高）。</summary>
    public WorkflowMetricStrip()
    {
        Dock = DockStyle.Top;
        LogicalExtent = 64;
        Controls.Add(_tools);
    }

    /// <summary>在最右边加宿主控件（先加的在最右）。</summary>
    /// <param name="control">控件。</param>
    public void AddTool(Control control)
    {
        Guard.NotNull(control);
        _tools.Controls.Add(WorkflowSizing.Fit(control));
    }

    /// <inheritdoc />
    protected internal override void ApplyMetrics()
    {
        base.ApplyMetrics();
        _tools.Padding = new Padding(0, Math.Max(0, (DisplayRectangle.Height - LogicalToDeviceUnits(38)) / 2), LogicalToDeviceUnits(8), 0);
    }

    /// <summary>更新内容；没变时不重绘。</summary>
    /// <param name="title">标题。</param>
    /// <param name="subtitle">副标题。</param>
    /// <param name="metrics">指标。</param>
    /// <param name="note">右边的灰字。</param>
    public void Show(string title, string subtitle, IReadOnlyList<WorkflowMetric> metrics, string note = "")
    {
        if (_title == title && _subtitle == subtitle && _note == note && _metrics.SequenceEqual(metrics)) return;
        _title = title;
        _subtitle = subtitle;
        _note = note;
        _metrics = metrics.ToArray();
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var theme = WorkflowWinFormsTheme.Modern;
        int S(int value) => LogicalToDeviceUnits(value);
        var right = (_tools.Visible && _tools.Controls.Count > 0 ? _tools.Left : Width) - S(16);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Left;
        using var bold = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold);
        using var big = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold);
        using var small = new Font(Font.FontFamily, Math.Max(6f, Font.Size - .5f));
        var x = S(20);
        // 两行文字的高度按字体量，整体在卡片里垂直居中，换字体、换 DPI 都不会被截。
        var inner = DisplayRectangle;
        var headTop = inner.Top + Math.Max(0, (inner.Height - bold.Height - small.Height - S(2)) / 2);
        var valueTop = inner.Top + Math.Max(0, (inner.Height - big.Height - small.Height) / 2);
        var headWidth = Math.Min(S(260), Math.Max(
            WorkflowText.Width(e.Graphics, _title, bold),
            WorkflowText.Width(e.Graphics, _subtitle, small)));
        TextRenderer.DrawText(e.Graphics, _title, bold, new Rectangle(x, headTop, headWidth, bold.Height), theme.Text, flags);
        TextRenderer.DrawText(e.Graphics, _subtitle, small, new Rectangle(x, headTop + bold.Height + S(2), headWidth, small.Height), theme.TextSecondary, flags);
        x += headWidth + S(32);

        var noteWidth = string.IsNullOrEmpty(_note) ? 0 : WorkflowText.Width(e.Graphics, _note, small);
        var limit = right - (noteWidth == 0 ? 0 : noteWidth + S(24));
        foreach (var metric in _metrics)
        {
            var width = Math.Max(
                WorkflowText.Width(e.Graphics, metric.Value, big),
                WorkflowText.Width(e.Graphics, metric.Caption, small));
            if (x + width > limit) break;
            TextRenderer.DrawText(e.Graphics, metric.Value, big, new Rectangle(x, valueTop, width, big.Height), metric.Color ?? theme.Text, flags);
            TextRenderer.DrawText(e.Graphics, metric.Caption, small, new Rectangle(x, valueTop + big.Height, width, small.Height), theme.TextSecondary, flags);
            x += width + S(28);
        }
        if (noteWidth > 0)
            TextRenderer.DrawText(e.Graphics, _note, small, new Rectangle(right - noteWidth, 0, noteWidth, Height), theme.TextSecondary,
                flags | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>指标条里的一项。</summary>
/// <param name="Value">大数字。</param>
/// <param name="Caption">下面的小说明。</param>
/// <param name="Color">数字颜色；为空时用正文色。</param>
public sealed record WorkflowMetric(string Value, string Caption, Color? Color = null);

/// <summary>侧栏里的一行：分组标题（<see cref="Heading"/>）或“名称 …… 值”。</summary>
/// <param name="Label">名称或分组标题。</param>
/// <param name="Value">值；分组标题时是右边的灰字。</param>
/// <param name="Heading">是否分组标题。</param>
/// <param name="Strong">名称和值是否加粗（例如本次最慢的节点）。</param>
/// <param name="ValueColor">值的颜色；为空时用正文色。</param>
public sealed record WorkflowFact(string Label, string Value = "", bool Heading = false, bool Strong = false, Color? ValueColor = null)
{
    /// <summary>分组标题。</summary>
    /// <param name="title">标题。</param>
    /// <param name="note">右边的灰字。</param>
    public static WorkflowFact Group(string title, string note = "") => new(title, note, Heading: true);
}

/// <summary>
/// 侧栏的“名称 …… 值”列表（设计稿的节点耗时、界面同步、流程详情、提示详情）：分组标题加粗，
/// 名称左对齐、值右对齐，行高 28；放不下的行不画。
/// </summary>
public sealed class WorkflowFactList : Control
{
    private IReadOnlyList<WorkflowFact> _facts = Array.Empty<WorkflowFact>();

    /// <summary>创建列表。</summary>
    public WorkflowFactList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Dock = DockStyle.Fill;
    }

    /// <summary>当前的行。</summary>
    public IReadOnlyList<WorkflowFact> Facts => _facts;

    /// <summary>换一组行；没变时不重绘。</summary>
    /// <param name="facts">行。</param>
    public void Show(IEnumerable<WorkflowFact> facts)
    {
        var next = facts.ToArray();
        if (next.SequenceEqual(_facts)) return;
        _facts = next;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        e.Graphics.Clear(BackColor);
        int S(int value) => LogicalToDeviceUnits(value);
        using var bold = new Font(Font, FontStyle.Bold);
        var left = S(16);
        var width = Math.Max(0, Width - S(32));
        var y = S(8);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        for (var index = 0; index < _facts.Count; index++)
        {
            var fact = _facts[index];
            var height = S(fact.Heading ? 34 : 28);
            if (fact.Heading && index > 0) y += S(10);
            if (y + height > Height) break;
            var row = new Rectangle(left, y, width, height);
            var valueWidth = string.IsNullOrEmpty(fact.Value) ? 0
                : Math.Min(width * 2 / 3, WorkflowText.Width(e.Graphics, fact.Value, fact.Strong ? bold : Font));
            var labelFont = fact.Heading || fact.Strong ? bold : Font;
            TextRenderer.DrawText(e.Graphics, fact.Label, labelFont, new Rectangle(row.X, row.Y, Math.Max(0, row.Width - valueWidth - S(8)), row.Height),
                theme.Text, flags);
            if (valueWidth > 0)
                TextRenderer.DrawText(e.Graphics, fact.Value, fact.Strong ? bold : Font, new Rectangle(row.Right - valueWidth, row.Y, valueWidth, row.Height),
                    fact.Heading ? theme.TextSecondary : fact.ValueColor ?? theme.Text, flags | TextFormatFlags.Right);
            y += height;
        }
    }
}

/// <summary>页面最下面一行状态：左边概况，右边最近一条操作提示。</summary>
public sealed class WorkflowStatusLine : WorkflowBlock
{
    private readonly Label _summary = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
    private readonly Label _message = new() { Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false };

    /// <summary>创建状态行（30 px 高）。</summary>
    public WorkflowStatusLine()
    {
        Dock = DockStyle.Bottom;
        LogicalExtent = 30;
        LogicalPadding = new Padding(4, 4, 4, 0);
        Controls.Add(_summary);
        Controls.Add(_message);
    }

    /// <summary>左边的概况。</summary>
    public string Summary
    {
        get => _summary.Text;
        set => _summary.Text = value ?? string.Empty;
    }

    /// <summary>右边的操作提示。</summary>
    public string Message
    {
        get => _message.Text;
        set => _message.Text = value ?? string.Empty;
    }
}
