using System.Drawing.Drawing2D;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>状态色调：标签、状态点、提示条用同一套颜色。</summary>
public enum WorkflowTone
{
    /// <summary>灰色（停止、无）。</summary>
    Neutral,

    /// <summary>主题色（运行中、开始、选中）。</summary>
    Accent,

    /// <summary>绿色（成功、已发布）。</summary>
    Success,

    /// <summary>黄色（提示、草稿、暂停）。</summary>
    Warning,

    /// <summary>红色（失败、故障）。</summary>
    Error
}

/// <summary>色调对应的主题颜色和小标签画法。</summary>
public static class WorkflowTones
{
    /// <summary>前景色。</summary>
    public static Color Fore(WorkflowTone tone)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        return tone switch
        {
            WorkflowTone.Accent => theme.Primary,
            WorkflowTone.Success => theme.Success,
            WorkflowTone.Warning => theme.Warning,
            WorkflowTone.Error => theme.Error,
            _ => theme.TextSecondary
        };
    }

    /// <summary>浅底色。</summary>
    public static Color Back(WorkflowTone tone)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        return tone switch
        {
            WorkflowTone.Accent => theme.PrimaryBackground,
            WorkflowTone.Success => theme.SuccessSurface,
            WorkflowTone.Warning => theme.WarningSurface,
            WorkflowTone.Error => theme.ErrorSurface,
            _ => theme.Background
        };
    }

    /// <summary>量出小标签的宽度。</summary>
    public static int BadgeWidth(Graphics graphics, string text, Font font, Control owner) =>
        TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width
        + owner.LogicalToDeviceUnits(16);

    /// <summary>在右边对齐 <paramref name="right"/>、垂直居中于 <paramref name="row"/> 画一个圆角小标签，返回它的左边。</summary>
    public static int DrawBadge(Graphics graphics, string text, Font font, WorkflowTone tone, int right, Rectangle row, Control owner)
    {
        var width = BadgeWidth(graphics, text, font, owner);
        var height = owner.LogicalToDeviceUnits(20);
        var bounds = new Rectangle(right - width, row.Top + (row.Height - height) / 2, width, height);
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = WorkflowCard.Rounded(bounds, height / 2f))
        using (var fill = new SolidBrush(Back(tone)))
            graphics.FillPath(fill, path);
        graphics.SmoothingMode = smoothing;
        TextRenderer.DrawText(graphics, text, font, bounds, Fore(tone),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return bounds.Left;
    }
}

/// <summary>量文字宽度。</summary>
/// <remarks>
/// 量的时候不能带 EndEllipsis 之类的绘制标志：带上后在没给尺寸时会按省略后的样子量，量出来只有一个字加省略号宽。
/// </remarks>
public static class WorkflowText
{
    /// <summary>单行文字在 <paramref name="font"/> 下的宽度（多留 1 px，避免取整后被省略）。</summary>
    public static int Width(IDeviceContext graphics, string? text, Font font) => string.IsNullOrEmpty(text) ? 0
        : TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + 1;
}

/// <summary>
/// 控件统一尺寸：高 30、外边距 4（相邻控件间隔 8），宽度按文字量或按给定的逻辑宽度。
/// 宿主按字体自动缩放或换 DPI 时会改控件尺寸，改完再按这里的规则量回来。
/// </summary>
public static class WorkflowSizing
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Widths = new();

    /// <summary>让控件按规则定尺寸并立即量一次。</summary>
    /// <param name="control">控件。</param>
    /// <param name="logicalWidth">逻辑宽度；0 表示按文字量（按钮、分段、勾选、文字）。</param>
    public static T Fit<T>(T control, int logicalWidth = 0) where T : Control
    {
        Guard.NotNull(control);
        var first = !Widths.TryGetValue(control, out _);
        Widths.Remove(control);
        Widths.Add(control, logicalWidth);
        if (control is ModernButton button) button.AutoSize = false;
        if (control is Label label)
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.UseMnemonic = false;
        }
        if (first)
        {
            control.TextChanged += OnChanged;
            control.FontChanged += OnChanged;
            control.SizeChanged += OnChanged;
            control.DpiChangedAfterParent += OnChanged;
            if (control is ModernSegmentedControl segmented)
                segmented.Items.ListChanged += (_, _) => Measure(segmented);
        }
        Measure(control);
        return control;
    }

    /// <summary>按当前文字、字体和 DPI 量尺寸。</summary>
    public static void Measure(Control control)
    {
        int S(int value) => control.LogicalToDeviceUnits(value);
        int Text(string? text) => TextRenderer.MeasureText(text ?? string.Empty, control.Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        var logical = Widths.TryGetValue(control, out var value) ? (int)value : 0;
        var width = logical > 0 ? S(logical) : control switch
        {
            ModernButton button => Text(button.Text) + (button.Icon == ModernIconKind.None ? 0 : S(button.IconSize + 6))
                                   + S(button.ButtonType == ModernButtonType.Text ? 22 : 30),
            ModernSegmentedControl segmented => segmented.Items.Count == 0 ? S(60)
                : segmented.Items.Count * (segmented.Items.Max(item => Text(item.Text)) + S(26)) + S(6),
            ModernCheckbox check => Text(check.Text) + S(34),
            ModernSwitch => S(46),
            _ => Text(control.Text) + S(4)
        };
        var size = new Size(width, S(30));
        if (control.Size != size) control.Size = size;
        var margin = new Padding(S(4));
        if (control.Margin != margin) control.Margin = margin;
    }

    private static void OnChanged(object? sender, EventArgs e)
    {
        if (sender is Control control) Measure(control);
    }
}

/// <summary>
/// 工具条卡（设计稿各页顶部 52 高的那张卡）：控件从左往右排，右边一组放主要操作；控件统一 30 高、垂直居中、间隔 8。
/// </summary>
public sealed class WorkflowToolbar : WorkflowCard
{
    private readonly FlowLayoutPanel _left = Row(FlowDirection.LeftToRight);
    private readonly FlowLayoutPanel _right = Row(FlowDirection.RightToLeft);

    /// <summary>创建工具条卡。</summary>
    public WorkflowToolbar()
    {
        Dock = DockStyle.Top;
        LogicalExtent = 52;
        _right.Dock = DockStyle.Right;
        _right.AutoSize = true;
        Controls.Add(_left);
        Controls.Add(_right);
    }

    /// <summary>在左边加一个控件。</summary>
    /// <param name="control">控件。</param>
    /// <param name="logicalWidth">逻辑宽度；0 表示按文字量。</param>
    public T Add<T>(T control, int logicalWidth = 0) where T : Control
    {
        _left.Controls.Add(WorkflowSizing.Fit(control, logicalWidth));
        return control;
    }

    /// <summary>在右边加一个控件（先加的在最右）。</summary>
    /// <param name="control">控件。</param>
    /// <param name="logicalWidth">逻辑宽度；0 表示按文字量。</param>
    public T AddRight<T>(T control, int logicalWidth = 0) where T : Control
    {
        _right.Controls.Add(WorkflowSizing.Fit(control, logicalWidth));
        return control;
    }

    /// <summary>左边加一条竖分隔线。</summary>
    public void AddSeparator()
    {
        var line = new WorkflowSeparator();
        _left.Controls.Add(line);
    }

    /// <inheritdoc />
    protected internal override void ApplyMetrics()
    {
        base.ApplyMetrics();
        if (_left is null) return;
        var top = Math.Max(0, (DisplayRectangle.Height - LogicalToDeviceUnits(38)) / 2);
        _left.Padding = new Padding(LogicalToDeviceUnits(6), top, 0, 0);
        _right.Padding = new Padding(0, top, LogicalToDeviceUnits(6), 0);
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyMetrics();
    }

    private static FlowLayoutPanel Row(FlowDirection direction) => new()
    {
        Dock = DockStyle.Fill,
        WrapContents = false,
        FlowDirection = direction,
        BackColor = Color.Transparent,
        Margin = Padding.Empty
    };
}

/// <summary>工具条里的竖分隔线。</summary>
internal sealed class WorkflowSeparator : Control
{
    public WorkflowSeparator()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        Size = new Size(LogicalToDeviceUnits(1), LogicalToDeviceUnits(20));
        Margin = new Padding(LogicalToDeviceUnits(6), LogicalToDeviceUnits(9), LogicalToDeviceUnits(6), 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new SolidBrush(WorkflowWinFormsTheme.Modern.BorderSecondary);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

/// <summary>列表里的一行。</summary>
/// <param name="Key">稳定的标识（用来恢复选中）。</param>
/// <param name="Title">粗体标题。</param>
/// <param name="Subtitle">下面一行灰字；为空时单行。</param>
/// <param name="Dot">左边状态点的色调；为空时不画点。</param>
/// <param name="Badge">右边小标签的文字；为空时不画。</param>
/// <param name="BadgeTone">小标签的色调。</param>
/// <param name="Action">右边图标按钮；None 时没有。</param>
/// <param name="ActionText">图标按钮的说明（悬停提示）。</param>
/// <param name="Enabled">是否可选；不可选的行灰字。</param>
/// <param name="SubtitleTone">副标题色调；Neutral 用灰字。</param>
public sealed record WorkflowListItem(
    object Key,
    string Title,
    string? Subtitle = null,
    WorkflowTone? Dot = null,
    string? Badge = null,
    WorkflowTone BadgeTone = WorkflowTone.Neutral,
    ModernIconKind Action = ModernIconKind.None,
    string? ActionText = null,
    bool Enabled = true,
    WorkflowTone SubtitleTone = WorkflowTone.Neutral);

/// <summary>
/// 卡片里的列表（设计稿“运行”的流程列表、“节点提示”的按节点、故障的停点和去向）：
/// 每行粗体标题 + 灰色副标题，左边可选状态点，右边可选小标签或图标按钮；选中行浅主题色，滚轮滚动。
/// </summary>
public sealed class WorkflowItemList : Control
{
    private readonly ToolTip _tip = new();
    private IReadOnlyList<WorkflowListItem> _items = Array.Empty<WorkflowListItem>();
    private object? _selected;
    private int _offset;
    private int _hover = -1;
    private bool _hoverAction;
    private string _emptyText = string.Empty;

    /// <summary>创建列表。</summary>
    public WorkflowItemList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        Dock = DockStyle.Fill;
        TabStop = true;
    }

    /// <summary>当前的行。</summary>
    public IReadOnlyList<WorkflowListItem> Items => _items;

    /// <summary>选中行的标识；没选时为空。</summary>
    public object? SelectedKey
    {
        get => _selected;
        set
        {
            if (Equals(_selected, value)) return;
            _selected = value;
            EnsureVisible();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>选中的行；没选时为空。</summary>
    public WorkflowListItem? SelectedItem => _selected is null ? null : _items.FirstOrDefault(item => Equals(item.Key, _selected));

    /// <summary>没有行时居中显示的说明。</summary>
    public string EmptyText
    {
        get => _emptyText;
        set { _emptyText = value ?? string.Empty; Invalidate(); }
    }

    /// <summary>行高（逻辑像素）；为 0 时有副标题 52、没有 36。</summary>
    public int RowHeight { get; set; }

    /// <summary>选中变化时发生。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>点了行右边的图标按钮时发生。</summary>
    public event EventHandler<WorkflowListItem>? ActionClicked;

    /// <summary>双击一行时发生。</summary>
    public event EventHandler<WorkflowListItem>? ItemActivated;

    /// <summary>换一组行；内容没变时不重绘。选中的标识不在新行里时取消选中。</summary>
    /// <param name="items">行。</param>
    public void Show(IEnumerable<WorkflowListItem> items)
    {
        var next = items.ToArray();
        if (next.SequenceEqual(_items)) return;
        _items = next;
        _offset = Math.Min(_offset, MaxOffset());
        if (_selected is not null && !next.Any(item => Equals(item.Key, _selected)))
        {
            _selected = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        Invalidate();
    }

    private int Row => LogicalToDeviceUnits(RowHeight > 0 ? RowHeight : _items.Any(item => item.Subtitle is not null) ? 52 : 36);

    private int MaxOffset() => Math.Max(0, _items.Count * Row - ClientSize.Height);

    private void EnsureVisible()
    {
        var index = IndexOf(_selected);
        if (index < 0) return;
        if (index * Row < _offset) _offset = index * Row;
        else if ((index + 1) * Row > _offset + ClientSize.Height) _offset = Math.Min(MaxOffset(), (index + 1) * Row - ClientSize.Height);
    }

    private int IndexOf(object? key)
    {
        for (var index = 0; index < _items.Count; index++)
            if (Equals(_items[index].Key, key)) return index;
        return -1;
    }

    private Rectangle ActionBounds(Rectangle row) =>
        new(row.Right - LogicalToDeviceUnits(40), row.Top + (row.Height - LogicalToDeviceUnits(28)) / 2, LogicalToDeviceUnits(28), LogicalToDeviceUnits(28));

    private int HitRow(Point point)
    {
        if (point.Y < 0) return -1;
        var index = (point.Y + _offset) / Math.Max(1, Row);
        return index >= 0 && index < _items.Count ? index : -1;
    }

    private Rectangle RowBounds(int index) => new(0, index * Row - _offset, ClientSize.Width, Row);

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        var g = e.Graphics;
        g.Clear(BackColor);
        int S(int value) => LogicalToDeviceUnits(value);
        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, _emptyText, Font, new Rectangle(S(16), 0, Math.Max(0, Width - S(32)), Math.Min(Height, S(120))), theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }
        using var bold = new Font(Font, FontStyle.Bold);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        using var line = new Pen(theme.BorderSecondary);
        var first = Math.Max(0, _offset / Row);
        for (var index = first; index < _items.Count; index++)
        {
            var row = RowBounds(index);
            if (row.Top > Height) break;
            var item = _items[index];
            var selected = Equals(item.Key, _selected);
            if (selected || index == _hover)
            {
                using var fill = new SolidBrush(selected ? theme.PrimaryBackground : theme.ControlHover);
                g.FillRectangle(fill, row);
            }
            g.DrawLine(line, row.Left, row.Bottom - 1, row.Right, row.Bottom - 1);
            var left = row.Left + S(14);
            if (item.Dot is { } dot)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(WorkflowTones.Fore(dot));
                g.FillEllipse(brush, left, row.Top + (row.Height - S(8)) / 2f, S(8), S(8));
                g.SmoothingMode = SmoothingMode.Default;
                left += S(18);
            }
            var right = row.Right - S(12);
            if (item.Action != ModernIconKind.None)
            {
                var action = ActionBounds(row);
                if (index == _hover && _hoverAction)
                {
                    using var hover = new SolidBrush(theme.Border);
                    using var path = WorkflowCard.Rounded(action, S(WorkflowWinFormsTheme.Palette.Radius));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(hover, path);
                    g.SmoothingMode = SmoothingMode.Default;
                }
                using var icon = ModernIcons.CreateBitmap(item.Action, item.Enabled ? theme.Text : theme.TextDisabled, S(16));
                g.DrawImage(icon, action.Left + (action.Width - icon.Width) / 2, action.Top + (action.Height - icon.Height) / 2);
                right = action.Left - S(6);
            }
            if (!string.IsNullOrEmpty(item.Badge))
                right = WorkflowTones.DrawBadge(g, item.Badge!, Font, item.BadgeTone, right, row, this) - S(8);
            var width = Math.Max(0, right - left);
            var titleColor = !item.Enabled ? theme.TextDisabled : theme.Text;
            if (item.Subtitle is null)
            {
                TextRenderer.DrawText(g, item.Title, bold, new Rectangle(left, row.Top, width, row.Height), titleColor, flags);
                continue;
            }
            var titleHeight = bold.Height;
            var subHeight = Font.Height;
            var top = row.Top + (row.Height - titleHeight - subHeight - S(2)) / 2;
            TextRenderer.DrawText(g, item.Title, bold, new Rectangle(left, top, width, titleHeight), titleColor, flags);
            var subColor = !item.Enabled ? theme.TextDisabled : item.SubtitleTone == WorkflowTone.Neutral ? theme.TextSecondary : WorkflowTones.Fore(item.SubtitleTone);
            TextRenderer.DrawText(g, item.Subtitle, Font, new Rectangle(left, top + titleHeight + S(2), width, subHeight), subColor, flags);
        }
        if (MaxOffset() > 0)
        {
            var track = Height - S(8);
            var thumb = Math.Max(S(24), track * Height / (_items.Count * Row));
            var y = S(4) + (track - thumb) * _offset / Math.Max(1, MaxOffset());
            using var brush = new SolidBrush(theme.ScrollThumb);
            g.FillRectangle(brush, Width - S(5), y, S(3), thumb);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var next = Math.Max(0, Math.Min(MaxOffset(), _offset - e.Delta / 120 * Row));
        if (next == _offset) return;
        _offset = next;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _offset = Math.Min(_offset, MaxOffset());
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = HitRow(e.Location);
        var onAction = index >= 0 && _items[index].Action != ModernIconKind.None && ActionBounds(RowBounds(index)).Contains(e.Location);
        if (index == _hover && onAction == _hoverAction) return;
        _hover = index;
        _hoverAction = onAction;
        Cursor = onAction ? Cursors.Hand : Cursors.Default;
        _tip.SetToolTip(this, onAction ? _items[index].ActionText : null);
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _hoverAction = false;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var index = HitRow(e.Location);
        if (index < 0 || e.Button != MouseButtons.Left) return;
        var item = _items[index];
        if (item.Action != ModernIconKind.None && ActionBounds(RowBounds(index)).Contains(e.Location))
        {
            if (item.Enabled) ActionClicked?.Invoke(this, item);
            return;
        }
        SelectedKey = item.Key;
    }

    /// <inheritdoc />
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var index = HitRow(e.Location);
        if (index >= 0 && !(_items[index].Action != ModernIconKind.None && ActionBounds(RowBounds(index)).Contains(e.Location)))
            ItemActivated?.Invoke(this, _items[index]);
    }

    /// <inheritdoc />
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0 || e.KeyCode is not (Keys.Up or Keys.Down)) return;
        var index = IndexOf(_selected);
        index = Math.Max(0, Math.Min(_items.Count - 1, index + (e.KeyCode == Keys.Down ? 1 : -1)));
        SelectedKey = _items[index].Key;
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
