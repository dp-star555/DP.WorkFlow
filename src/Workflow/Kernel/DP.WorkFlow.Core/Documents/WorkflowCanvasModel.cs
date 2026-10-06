namespace DP.WorkFlow;

/// <summary>
/// 表示当前桌面设计器使用的过渡画布投影；正式持久化和编译入口为 <see cref="WorkflowDocument"/>。
/// </summary>
public sealed class WorkflowCanvasModel
{
    private readonly WorkflowDocumentState _state;

    internal WorkflowCanvasModel(WorkflowDocumentState state) =>
        _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>获取或设置正式文档拥有的流程名称。</summary>
    public string Name
    {
        get => _state.Name;
        set => _state.Name = value ?? string.Empty;
    }

    /// <summary>获取正式文档拥有的节点与布局组合投影。</summary>
    public IList<WorkflowCanvasNode> Nodes => _state.Nodes;

    /// <summary>获取正式文档拥有的语义连接与路由布局组合投影。</summary>
    public IList<WorkflowConnectionModel> Connections => _state.Connections;
}

/// <summary>
/// 将文档节点配置投影到当前设计器布局；后续画布控件迁移后该组合类型将被移除。
/// </summary>
public sealed class WorkflowCanvasNode
{
    /// <summary>获取或设置节点配置。</summary>
    public required IWorkflowNodeModel Node { get; init; }

    /// <summary>获取或设置节点左上角 X 坐标。</summary>
    public double X { get; set; }

    /// <summary>获取或设置节点左上角 Y 坐标。</summary>
    public double Y { get; set; }

    /// <summary>获取或设置节点宽度。</summary>
    public double Width { get; set; } = 180;

    /// <summary>获取或设置节点高度。</summary>
    public double Height { get; set; } = 60;

    /// <summary>获取按“Direction:PortKey”保存的端口边覆盖配置。</summary>
    public IDictionary<string, WorkflowPortSide> PortSides { get; } =  new Dictionary<string, WorkflowPortSide>(StringComparer.Ordinal);

    /// <summary>获取在设计器中隐藏且禁止连线的输出端口键。</summary>
    public ISet<string> HiddenOutputPorts { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>获取在节点上显示为数据端口的标准输出成员名；从数据端口拖线可直接建立下游参数绑定。</summary>
    public ISet<string> ExposedOutputMembers { get; } = new HashSet<string>(StringComparer.Ordinal);

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
