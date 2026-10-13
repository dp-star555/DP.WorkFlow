namespace DP.WorkFlow.UI;

/// <summary>UI 框架无关的颜色（sRGB，含不透明度）。</summary>
/// <param name="R">红色分量。</param>
/// <param name="G">绿色分量。</param>
/// <param name="B">蓝色分量。</param>
/// <param name="A">不透明度，255 为完全不透明。</param>
public readonly record struct WorkflowColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>由 0xRRGGBB 创建不透明颜色。</summary>
    /// <param name="rgb">24 位颜色值。</param>
    public static WorkflowColor FromRgb(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>返回相同颜色、指定不透明度的副本。</summary>
    /// <param name="alpha">不透明度。</param>
    public WorkflowColor WithAlpha(byte alpha) => this with { A = alpha };
}

/// <summary>
/// 画布上节点的显示状态。由节点最近运行状态和整个流程是否暂停共同决定，见 <see cref="WorkflowDesignerSession.GetNodeVisualState"/>。
/// </summary>
public enum WorkflowNodeVisualState
{
    /// <summary>尚未执行：中性边框、灰色状态点。</summary>
    Idle,
    /// <summary>正在执行：绿色边框和淡绿外发光。</summary>
    Running,
    /// <summary>流程暂停时停在该节点，或该节点被取消停止：黄色边框。</summary>
    Paused,
    /// <summary>已经完成：中性边框、绿色状态点。</summary>
    Completed,
    /// <summary>执行异常：红色边框，序号徽标也着红色底。</summary>
    Failed
}

/// <summary>节点在某一显示状态下的边框、状态点、外发光与序号徽标配色。</summary>
/// <param name="Border">边框颜色。</param>
/// <param name="BorderWidth">边框设计宽度（缩放为 1 时的像素）。</param>
/// <param name="Indicator">标题行状态点颜色。</param>
/// <param name="Glow">外发光颜色；不透明度为 0 表示没有外发光。</param>
/// <param name="BadgeBackground">执行序号徽标底色。</param>
/// <param name="BadgeText">执行序号文字颜色。</param>
public readonly record struct WorkflowNodeVisualStyle(
    WorkflowColor Border,
    double BorderWidth,
    WorkflowColor Indicator,
    WorkflowColor Glow,
    WorkflowColor BadgeBackground,
    WorkflowColor BadgeText);

/// <summary>
/// 设计画布配色（方案 A）。边框平时为中性色，只有运行（绿）、暂停/停止（黄）、异常（红）、选中（蓝）时着色；
/// 选中只覆盖边框，状态点仍表示运行状态；耗时文字始终为中性色。WinForms 与 WPF 共用这一份颜色，两边画面一致。
/// </summary>
public static class WorkflowDesignerPalette
{
    /// <summary>画布背景。</summary>
    public static readonly WorkflowColor Canvas = WorkflowColor.FromRgb(0x151517);
    /// <summary>细栅格线。</summary>
    public static readonly WorkflowColor GridMinor = WorkflowColor.FromRgb(0x1e1e22);
    /// <summary>每 5 格一条的主栅格线。</summary>
    public static readonly WorkflowColor GridMajor = WorkflowColor.FromRgb(0x2b2b31);

    /// <summary>节点标题行底色。</summary>
    public static readonly WorkflowColor NodeHeader = WorkflowColor.FromRgb(0x26262b);
    /// <summary>节点正文底色。</summary>
    public static readonly WorkflowColor NodeBody = WorkflowColor.FromRgb(0x222226);
    /// <summary>节点中性边框。</summary>
    public static readonly WorkflowColor NodeBorder = WorkflowColor.FromRgb(0x3a3a42);
    /// <summary>标题行与正文、正文各行之间的分隔线。</summary>
    public static readonly WorkflowColor RowSeparator = WorkflowColor.FromRgb(0x303036);
    /// <summary>节点标题文字。</summary>
    public static readonly WorkflowColor Title = WorkflowColor.FromRgb(0xe4e4e7);
    /// <summary>标题行图标描边。</summary>
    public static readonly WorkflowColor Icon = WorkflowColor.FromRgb(0xa1a1aa);
    /// <summary>耗时文字（中性色，不随状态变化）。</summary>
    public static readonly WorkflowColor Duration = WorkflowColor.FromRgb(0x8e8e98);
    /// <summary>端口名称文字。</summary>
    public static readonly WorkflowColor PortLabel = WorkflowColor.FromRgb(0xa1a1aa);
    /// <summary>失败端口名称文字。</summary>
    public static readonly WorkflowColor FailedPortLabel = WorkflowColor.FromRgb(0xfca5a5);
    /// <summary>外置数据成员名称文字。</summary>
    public static readonly WorkflowColor DataLabel = WorkflowColor.FromRgb(0xc4b5fd);

    /// <summary>控制流端口圆点。</summary>
    public static readonly WorkflowColor Port = WorkflowColor.FromRgb(0x9a9aa6);
    /// <summary>失败端口圆点。</summary>
    public static readonly WorkflowColor FailedPort = WorkflowColor.FromRgb(0xf87171);
    /// <summary>数据端口空心菱形与数据连线。</summary>
    public static readonly WorkflowColor DataPort = WorkflowColor.FromRgb(0xc4b5fd);
    /// <summary>拖线时可接入的输入端点、悬停端口的高亮色。</summary>
    public static readonly WorkflowColor PortHighlight = WorkflowColor.FromRgb(0xe4e4e7);

    /// <summary>连线。</summary>
    public static readonly WorkflowColor Connection = WorkflowColor.FromRgb(0x73737d);
    /// <summary>停用的并行分支连线（虚线）。</summary>
    public static readonly WorkflowColor DisabledConnection = WorkflowColor.FromRgb(0x4a4a52);
    /// <summary>连线标签胶囊底色。</summary>
    public static readonly WorkflowColor LabelBackground = WorkflowColor.FromRgb(0x17171a);
    /// <summary>连线标签胶囊描边。</summary>
    public static readonly WorkflowColor LabelBorder = WorkflowColor.FromRgb(0x3a3a42);
    /// <summary>连线标签文字。</summary>
    public static readonly WorkflowColor LabelText = WorkflowColor.FromRgb(0xa5b4fc);
    /// <summary>停用分支的标签文字。</summary>
    public static readonly WorkflowColor DisabledLabelText = WorkflowColor.FromRgb(0x71717a);

    /// <summary>选中（边框、选中连线、框选）。</summary>
    public static readonly WorkflowColor Selected = WorkflowColor.FromRgb(0x38bdf8);
    /// <summary>运行中。</summary>
    public static readonly WorkflowColor Running = WorkflowColor.FromRgb(0x34d399);
    /// <summary>暂停或停止。</summary>
    public static readonly WorkflowColor Paused = WorkflowColor.FromRgb(0xfbbf24);
    /// <summary>异常。</summary>
    public static readonly WorkflowColor Failed = WorkflowColor.FromRgb(0xf87171);
    /// <summary>未执行的状态点。</summary>
    public static readonly WorkflowColor Idle = WorkflowColor.FromRgb(0x5b5b66);

    private static readonly WorkflowColor BadgeBackground = WorkflowColor.FromRgb(0x34343b);
    private static readonly WorkflowColor BadgeText = WorkflowColor.FromRgb(0xd4d4d8);
    private static readonly WorkflowColor FailedBadgeBackground = WorkflowColor.FromRgb(0x4a2a2d);
    private static readonly WorkflowColor FailedBadgeText = WorkflowColor.FromRgb(0xfecaca);

    /// <summary>运行中外发光的设计宽度（缩放为 1 时的像素）。</summary>
    public const double GlowWidth = 3;

    /// <summary>取得节点在指定显示状态下的配色；选中只改边框颜色和宽度。</summary>
    /// <param name="state">节点显示状态。</param>
    /// <param name="selected">节点是否选中。</param>
    public static WorkflowNodeVisualStyle GetNodeStyle(WorkflowNodeVisualState state, bool selected)
    {
        var (border, width) = state switch
        {
            WorkflowNodeVisualState.Running => (Running, 1.5),
            WorkflowNodeVisualState.Paused => (Paused, 1.5),
            WorkflowNodeVisualState.Failed => (Failed, 1.5),
            _ => (NodeBorder, 1d)
        };
        if (selected) (border, width) = (Selected, 2);
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
            state == WorkflowNodeVisualState.Running ? Running.WithAlpha(36) : default,
            failed ? FailedBadgeBackground : BadgeBackground,
            failed ? FailedBadgeText : BadgeText);
    }
}
