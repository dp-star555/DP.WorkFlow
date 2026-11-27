namespace DP.WorkFlow;

/// <summary>文档里的一个节点：节点配置加上它在画布上的布局。</summary>
public sealed class WorkflowCanvasNode
{
    /// <summary>获取或设置节点配置。</summary>
    public required IWorkflowNodeModel Node { get; init; }

    /// <summary>获取或设置节点左上角 X 坐标。</summary>
    public double X { get; set; }

    /// <summary>获取或设置节点左上角 Y 坐标。</summary>
    public double Y { get; set; }

    /// <summary>获取或设置节点宽度；0 表示未设定，由设计器打开时按节点内容决定。</summary>
    public double Width { get; set; }

    /// <summary>获取或设置节点高度；0 表示未设定，由设计器打开时按节点内容决定。</summary>
    public double Height { get; set; }

    /// <summary>获取按“Direction:PortKey”保存的端口边覆盖配置。</summary>
    public IDictionary<string, WorkflowPortSide> PortSides { get; } =  new Dictionary<string, WorkflowPortSide>(StringComparer.Ordinal);

    /// <summary>
    /// 获取在设计器中隐藏且禁止连线的输出端口键。默认不显示的端口（<see cref="WorkflowPortDescriptor.HiddenByDefault"/>）
    /// 被用户启用时，以 <see cref="ShownPortMarker"/> 加端口键的形式记在这里；请通过
    /// <see cref="IsOutputPortHidden"/> / <see cref="SetOutputPortHidden"/> 读写，不要直接判断键。
    /// </summary>
    public ISet<string> HiddenOutputPorts { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>默认不显示的端口被用户启用时的记录前缀。</summary>
    public const string ShownPortMarker = "+";

    /// <summary>输出端口在设计器中是否隐藏（不含“已有连线时仍显示”的规则）。</summary>
    /// <param name="port">输出端口。</param>
    public bool IsOutputPortHidden(WorkflowPortDescriptor port)
    {
        Guard.NotNull(port);
        return HiddenOutputPorts.Contains(port.Key) || port.HiddenByDefault && !HiddenOutputPorts.Contains(ShownPortMarker + port.Key);
    }

    /// <summary>显示或隐藏输出端口。</summary>
    /// <param name="port">输出端口。</param><param name="hidden">是否隐藏。</param>
    public void SetOutputPortHidden(WorkflowPortDescriptor port, bool hidden)
    {
        Guard.NotNull(port);
        HiddenOutputPorts.Remove(port.Key);
        HiddenOutputPorts.Remove(ShownPortMarker + port.Key);
        if (port.HiddenByDefault) { if (!hidden) HiddenOutputPorts.Add(ShownPortMarker + port.Key); }
        else if (hidden) HiddenOutputPorts.Add(port.Key);
    }

    /// <summary>获取端口在该节点实例上的实际显示边。</summary>
    /// <param name="port">节点类型声明的静态或动态端口描述。</param>
    /// <returns>实例覆盖配置中的边；未覆盖时返回端口描述的默认边。</returns>
    public WorkflowPortSide GetPortSide(WorkflowPortDescriptor port) =>
        PortSides.TryGetValue(GetPortLayoutKey(port.Direction, port.Key), out var side)
            ? side
            : port.Side;

    /// <summary>设置端口在该节点实例上的显示边覆盖。</summary>
    /// <param name="direction">端口方向，与端口键共同构成布局索引。</param>
    /// <param name="portKey">端口稳定键。</param>
    /// <param name="side">设计器绘制端口和连接时应使用的边。</param>
    public void SetPortSide(WorkflowPortDirection direction, string portKey, WorkflowPortSide side) =>
        PortSides[GetPortLayoutKey(direction, portKey)] = side;

    private static string GetPortLayoutKey(WorkflowPortDirection direction, string portKey) =>
        $"{direction}:{portKey}";
}
