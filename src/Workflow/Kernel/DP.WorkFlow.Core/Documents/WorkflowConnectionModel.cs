using System.Drawing;

namespace DP.WorkFlow;

/// <summary>指定控制连接当前是否仍满足节点端口契约。</summary>
public enum WorkflowConnectionState
{
    /// <summary>来源和目标端口均有效，连接参与编译。</summary>
    Active,

    /// <summary>节点配置变化导致端口暂时失效；连接被保留以便修复或撤销。</summary>
    Detached
}

/// <summary>
/// 表示两个节点端口之间的有向连接。
/// </summary>
public sealed class WorkflowConnectionModel
{
    /// <summary>获取或设置来源节点 ID。</summary>
    public string FromNodeId { get; set; } = string.Empty;

    /// <summary>获取或设置来源出口端口。</summary>
    public string FromPort { get; set; } = WorkflowPorts.Success;

    /// <summary>获取或设置目标节点 ID。</summary>
    public string ToNodeId { get; set; } = string.Empty;

    /// <summary>获取或设置目标输入端口。</summary>
    public string ToPort { get; set; } = WorkflowPorts.Input;

    /// <summary>获取或设置该连接在来源端口使用的边；为空时使用节点端口布局。</summary>
    public WorkflowPortSide? FromSide { get; set; }

    /// <summary>获取或设置该连接在目标端口使用的边；为空时使用节点端口布局。</summary>
    public WorkflowPortSide? ToSide { get; set; }

    /// <summary>获取或设置连线标签沿路径的归一化位置，0 为起点，1 为终点。</summary>
    public double LabelPosition { get; set; } = 0.5;

    /// <summary>
    /// 获取或设置连线备注：只用于显示，不影响执行和绑定。端口的固定语义（成立、循环、失败等）始终显示，
    /// 备注附在其后（如“成立 · 有料”）；并行分支显示为“分支 · 备注”，无备注时按顺序显示“分支1、分支2…”。
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// 获取或设置连线是否启用。目前只有并行节点的分支连线可以停用：停用的分支不派发，汇聚节点只等启用的分支。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>获取或设置连接契约状态。</summary>
    public WorkflowConnectionState State { get; set; }

    /// <summary>获取或设置连接失效时的诊断说明。</summary>
    public string? Diagnostic { get; set; }

    /// <summary>获取手工折线路径点。</summary>
    public IList<WorkflowPoint> Waypoints { get; } = new List<WorkflowPoint>();
}

/// <summary>表示与具体 UI 框架无关的画布二维坐标。</summary>
/// <param name="X">画布坐标系中的水平位置。</param>
/// <param name="Y">画布坐标系中的垂直位置。</param>
public readonly record struct WorkflowPoint(double X, double Y);
