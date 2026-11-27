namespace DP.WorkFlow.UI;

/// <summary>
/// 画布配色（方案 A 的同一组颜色角色），可以由界面主题推导（<see cref="From"/>），也可以用原来的固定深色（<see cref="Classic"/>）。
/// 边框平时为中性色，只有运行（成功色）、暂停 / 停止（警告色）、异常（错误色）、选中（强调色）时着色；
/// 节点右上角的提示角标用警告色，和异常的红色区分。
/// </summary>
public sealed record WorkflowCanvasPalette
{
    /// <summary>画布背景。</summary>
    public WorkflowColor Canvas { get; init; }
    /// <summary>细栅格线。</summary>
    public WorkflowColor GridMinor { get; init; }
    /// <summary>主栅格线。</summary>
    public WorkflowColor GridMajor { get; init; }
    /// <summary>节点标题行底色。</summary>
    public WorkflowColor NodeHeader { get; init; }
    /// <summary>节点正文底色。</summary>
    public WorkflowColor NodeBody { get; init; }
    /// <summary>节点中性边框。</summary>
    public WorkflowColor NodeBorder { get; init; }
    /// <summary>标题行与正文、正文各行之间的分隔线。</summary>
    public WorkflowColor RowSeparator { get; init; }
    /// <summary>节点标题文字。</summary>
    public WorkflowColor Title { get; init; }
    /// <summary>标题行图标描边。</summary>
    public WorkflowColor Icon { get; init; }
    /// <summary>耗时文字。</summary>
    public WorkflowColor Duration { get; init; }
    /// <summary>端口名称文字。</summary>
    public WorkflowColor PortLabel { get; init; }
    /// <summary>失败端口名称文字。</summary>
    public WorkflowColor FailedPortLabel { get; init; }
    /// <summary>控制流端口圆点。</summary>
    public WorkflowColor Port { get; init; }
    /// <summary>失败端口圆点。</summary>
    public WorkflowColor FailedPort { get; init; }
    /// <summary>拖线时可接入的端点、悬停端口的高亮色。</summary>
    public WorkflowColor PortHighlight { get; init; }
    /// <summary>连线。</summary>
    public WorkflowColor Connection { get; init; }
    /// <summary>停用的并行分支连线。</summary>
    public WorkflowColor DisabledConnection { get; init; }
    /// <summary>连线标签胶囊底色。</summary>
    public WorkflowColor LabelBackground { get; init; }
    /// <summary>连线标签胶囊描边。</summary>
    public WorkflowColor LabelBorder { get; init; }
    /// <summary>连线标签文字。</summary>
    public WorkflowColor LabelText { get; init; }
    /// <summary>停用分支的标签文字。</summary>
    public WorkflowColor DisabledLabelText { get; init; }
    /// <summary>选中（边框、选中连线、框选）。</summary>
    public WorkflowColor Selected { get; init; }
    /// <summary>运行中。</summary>
    public WorkflowColor Running { get; init; }
    /// <summary>运行中外发光（半透明）。</summary>
    public WorkflowColor RunningGlow { get; init; }
    /// <summary>暂停或停止。</summary>
    public WorkflowColor Paused { get; init; }
    /// <summary>异常。</summary>
    public WorkflowColor Failed { get; init; }
    /// <summary>未执行的状态点。</summary>
    public WorkflowColor Idle { get; init; }
    /// <summary>执行序号徽标底色。</summary>
    public WorkflowColor BadgeBackground { get; init; }
    /// <summary>执行序号文字。</summary>
    public WorkflowColor BadgeText { get; init; }
    /// <summary>异常节点的序号徽标底色。</summary>
    public WorkflowColor FailedBadgeBackground { get; init; }
    /// <summary>异常节点的序号文字。</summary>
    public WorkflowColor FailedBadgeText { get; init; }
    /// <summary>提示角标底色。</summary>
    public WorkflowColor Notice { get; init; }
    /// <summary>提示角标文字。</summary>
    public WorkflowColor NoticeText { get; init; }
    /// <summary>画布浮层（运行级提示、缩放条）底色。</summary>
    public WorkflowColor Overlay { get; init; }
    /// <summary>画布浮层描边。</summary>
    public WorkflowColor OverlayBorder { get; init; }
    /// <summary>画布浮层文字。</summary>
    public WorkflowColor OverlayText { get; init; }
    /// <summary>是否为深色画布（自绘时用来选择阴影强度）。</summary>
    public bool IsDark { get; init; }

    /// <summary>原来固定的深色画布（WPF 仍在使用）。</summary>
    public static WorkflowCanvasPalette Classic { get; } = new()
    {
        IsDark = true,
        Canvas = WorkflowDesignerPalette.Canvas,
        GridMinor = WorkflowDesignerPalette.GridMinor,
        GridMajor = WorkflowDesignerPalette.GridMajor,
        NodeHeader = WorkflowDesignerPalette.NodeHeader,
        NodeBody = WorkflowDesignerPalette.NodeBody,
        NodeBorder = WorkflowDesignerPalette.NodeBorder,
        RowSeparator = WorkflowDesignerPalette.RowSeparator,
        Title = WorkflowDesignerPalette.Title,
        Icon = WorkflowDesignerPalette.Icon,
        Duration = WorkflowDesignerPalette.Duration,
        PortLabel = WorkflowDesignerPalette.PortLabel,
        FailedPortLabel = WorkflowDesignerPalette.FailedPortLabel,
        Port = WorkflowDesignerPalette.Port,
        FailedPort = WorkflowDesignerPalette.FailedPort,
        PortHighlight = WorkflowDesignerPalette.PortHighlight,
        Connection = WorkflowDesignerPalette.Connection,
        DisabledConnection = WorkflowDesignerPalette.DisabledConnection,
        LabelBackground = WorkflowDesignerPalette.LabelBackground,
        LabelBorder = WorkflowDesignerPalette.LabelBorder,
        LabelText = WorkflowDesignerPalette.LabelText,
        DisabledLabelText = WorkflowDesignerPalette.DisabledLabelText,
        Selected = WorkflowDesignerPalette.Selected,
        Running = WorkflowDesignerPalette.Running,
        RunningGlow = WorkflowDesignerPalette.Running.WithAlpha(36),
        Paused = WorkflowDesignerPalette.Paused,
        Failed = WorkflowDesignerPalette.Failed,
        Idle = WorkflowDesignerPalette.Idle,
        BadgeBackground = WorkflowColor.FromRgb(0x34343b),
        BadgeText = WorkflowColor.FromRgb(0xd4d4d8),
        FailedBadgeBackground = WorkflowColor.FromRgb(0x4a2a2d),
        FailedBadgeText = WorkflowColor.FromRgb(0xfecaca),
        Notice = WorkflowDesignerPalette.Paused,
        NoticeText = WorkflowColor.FromRgb(0x1a1408),
        Overlay = WorkflowColor.FromRgb(0x222226),
        OverlayBorder = WorkflowDesignerPalette.NodeBorder,
        OverlayText = WorkflowDesignerPalette.Title
    };

    /// <summary>按界面主题推导画布配色：画布用次一级表面，节点用卡片色，状态色直接取主题的成功 / 警告 / 错误色。</summary>
    /// <param name="theme">界面主题。</param>
    public static WorkflowCanvasPalette From(WorkflowThemePalette theme)
    {
        Guard.NotNull(theme);
        var c = theme.Colors;
        var dark = theme.IsDark;
        // 浅色主题画布取次一级表面，深色主题比工作区再暗一点，节点卡片浮在上面。
        var canvas = dark ? c.Background.Mix(WorkflowColors.Black, .12) : c.SurfaceAlt;
        var border = theme.Border;
        return new WorkflowCanvasPalette
        {
            IsDark = dark,
            Canvas = canvas,
            GridMinor = c.Line.Mix(canvas, dark ? .45 : .35),
            GridMajor = c.Line.Mix(c.Text, dark ? .06 : .05),
            NodeHeader = dark ? c.SurfaceAlt : c.SurfaceAlt.Mix(c.Line, .35),
            NodeBody = c.Surface,
            NodeBorder = border,
            RowSeparator = c.Line,
            Title = c.Text,
            Icon = c.TextMuted,
            Duration = c.TextMuted,
            PortLabel = c.TextMuted,
            FailedPortLabel = c.Error,
            Port = c.TextMuted.Mix(canvas, .15),
            FailedPort = c.Error,
            PortHighlight = c.Text,
            Connection = c.TextMuted.Mix(canvas, dark ? .2 : .1),
            DisabledConnection = border,
            LabelBackground = c.Surface,
            LabelBorder = c.Line,
            LabelText = c.Accent,
            DisabledLabelText = c.TextMuted,
            Selected = c.Accent,
            Running = c.Success,
            RunningGlow = c.Success.WithAlpha(dark ? (byte)56 : (byte)44),
            Paused = c.Warning,
            Failed = c.Error,
            Idle = c.Line.Mix(c.Text, .22),
            BadgeBackground = c.SurfaceAlt.Mix(c.Text, dark ? .1 : .06),
            BadgeText = c.TextMuted,
            FailedBadgeBackground = c.ErrorSurface,
            FailedBadgeText = c.Error,
            Notice = c.Warning,
            NoticeText = c.Warning.ReadableOn(),
            Overlay = c.Surface,
            OverlayBorder = border,
            OverlayText = c.Text
        };
    }

    /// <summary>取得节点在指定显示状态下的配色；选中只改边框颜色和宽度。</summary>
    /// <param name="state">节点显示状态。</param>
    /// <param name="selected">节点是否选中。</param>
    public WorkflowNodeVisualStyle GetNodeStyle(WorkflowNodeVisualState state, bool selected)
    {
        var (border, width) = state switch
        {
            WorkflowNodeVisualState.Running => (Running, 2d),
            WorkflowNodeVisualState.Paused => (Paused, 2d),
            WorkflowNodeVisualState.Failed => (Failed, 2d),
            _ => (NodeBorder, 1.5)
        };
        if (selected) (border, width) = (Selected, 2.5);
        var indicator = state switch
        {
            WorkflowNodeVisualState.Running or WorkflowNodeVisualState.Completed => Running,
            WorkflowNodeVisualState.Paused => Paused,
            WorkflowNodeVisualState.Failed => Failed,
            _ => Idle
        };
        var failed = state == WorkflowNodeVisualState.Failed;
        return new WorkflowNodeVisualStyle(
            border,
            width,
            indicator,
            state == WorkflowNodeVisualState.Running ? RunningGlow : default,
            failed ? FailedBadgeBackground : BadgeBackground,
            failed ? FailedBadgeText : BadgeText);
    }
}
