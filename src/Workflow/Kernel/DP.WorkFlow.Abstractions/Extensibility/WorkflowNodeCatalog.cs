using System.Collections.ObjectModel;
using System.Reflection;

namespace DP.WorkFlow;

/// <summary>指定端口位于节点的哪一条边。</summary>
public enum WorkflowPortSide
{
    Left,
    Top,
    Right,
    Bottom
}

/// <summary>指定节点端口的数据流方向。</summary>
public enum WorkflowPortDirection
{
    /// <summary>输入端口。</summary>
    Input,

    /// <summary>输出端口。</summary>
    Output
}

/// <summary>
/// 描述节点公开的一个端口及其连接。
/// </summary>
public sealed record WorkflowPortDescriptor
{
    /// <summary>初始化一个节点端口的稳定声明。</summary>
    /// <param name="key">端口稳定键；它会写入流程连接并供运行时选择，因此发布后不应随显示文本变化。</param>
    /// <param name="direction">端口相对于节点的数据流方向。</param>
    /// <param name="maxConnections">该端口允许建立的最大连接数，必须大于零。</param>
    /// <param name="side">端口的默认显示边；为空时输入端口位于上方、输出端口位于下方。</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> 为空或仅包含空白字符。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConnections"/> 小于 1。</exception>
    public WorkflowPortDescriptor(
        string key,
        WorkflowPortDirection direction,
        int maxConnections = 1,
        WorkflowPortSide? side = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("端口键不能为空。", nameof(key));
        if (maxConnections < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConnections), "最大连接数必须大于 0。");
        Key = key.Trim();
        Direction = direction;
        MaxConnections = maxConnections;
        Side = side ?? (direction == WorkflowPortDirection.Input
            ? WorkflowPortSide.Top
            : WorkflowPortSide.Bottom);
    }

    /// <summary>获取稳定端口键。</summary>
    public string Key { get; }

    /// <summary>获取端口方向。</summary>
    public WorkflowPortDirection Direction { get; }

    /// <summary>获取该端口允许建立的最大连接数。</summary>
    public int MaxConnections { get; }

    /// <summary>获取端口所在节点边；未显式配置时输入在上、输出在下。</summary>
    public WorkflowPortSide Side { get; }

    /// <summary>获取输出端口是否默认不显示（例如失败出口）；在节点属性中启用或已有连线时才显示。</summary>
    public bool HiddenByDefault { get; init; }

    /// <summary>创建一个输入端口描述。</summary>
    /// <param name="key">输入端口稳定键，默认使用 <see cref="WorkflowPorts.Input"/>。</param>
    /// <param name="maxConnections">允许接入该端口的最大上游连接数。</param>
    /// <param name="side">端口在节点上的默认显示边。</param>
    /// <returns>具有输入方向及指定连接约束的端口描述。</returns>
    public static WorkflowPortDescriptor Input(
        string key = WorkflowPorts.Input,
        int maxConnections = 1,
        WorkflowPortSide side = WorkflowPortSide.Top) =>
        new(key, WorkflowPortDirection.Input, maxConnections, side);

    /// <summary>创建一个输出端口描述。</summary>
    /// <param name="key">输出端口稳定键，默认使用 <see cref="WorkflowPorts.Success"/>。</param>
    /// <param name="maxConnections">允许从该端口引出的最大下游连接数。</param>
    /// <param name="side">端口在节点上的默认显示边。</param>
    /// <returns>具有输出方向及指定连接约束的端口描述。</returns>
    public static WorkflowPortDescriptor Output(
        string key = WorkflowPorts.Success,
        int maxConnections = 1,
        WorkflowPortSide side = WorkflowPortSide.Bottom) =>
        new(key, WorkflowPortDirection.Output, maxConnections, side);

    /// <summary>
    /// 创建失败出口（<see cref="WorkflowPorts.Failed"/>）：连线后节点失败不中止运行，沿该出口继续。
    /// 默认不显示，在节点属性中启用“失败”输出后才出现在节点上。
    /// </summary>
    /// <param name="maxConnections">允许从该端口引出的最大下游连接数。</param>
    /// <param name="side">端口在节点上的默认显示边。</param>
    public static WorkflowPortDescriptor Failure(int maxConnections = 1, WorkflowPortSide side = WorkflowPortSide.Bottom) =>
        Output(WorkflowPorts.Failed, maxConnections, side) with { HiddenByDefault = true };
}

/// <summary>
/// 描述一种可发现、可创建和可持久化的节点模型。
/// </summary>
public sealed class WorkflowNodeDescriptor
{
    /// <summary>初始化一种可发现、可创建、可校验和可持久化的节点类型描述。</summary>
    /// <param name="nodeType">跨程序集及文档版本保持稳定的节点类型键。</param>
    /// <param name="nodeVersion">该节点配置格式的版本，必须大于零；它不同于工作流文档 Schema 版本。</param>
    /// <param name="modelType">实现 <see cref="IWorkflowNodeModel"/> 的节点配置 CLR 类型。</param>
    /// <param name="factory">每次调用均创建独立节点模型实例的工厂。</param>
    /// <param name="displayName">设计器工具箱中显示的名称。</param>
    /// <param name="category">设计器工具箱中的分类路径。</param>
    /// <param name="description">面向流程设计者的节点用途说明。</param>
    /// <param name="ports">节点的静态基础端口；动态端口节点可基于此集合计算实例端口。</param>
    /// <param name="outputType">节点标准输出的静态类型；为空表示设计期无法确定。</param>
    /// <exception cref="ArgumentException">节点类型键为空、模型类型不实现节点接口，或端口声明存在重复。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nodeVersion"/> 小于或等于零。</exception>
    public WorkflowNodeDescriptor(
        string nodeType,
        int nodeVersion,
        Type modelType,
        Func<IWorkflowNodeModel> factory,
        string? displayName = null,
        string? category = null,
        string? description = null,
        IEnumerable<WorkflowPortDescriptor>? ports = null,
        Type? outputType = null)
    {
        if (string.IsNullOrWhiteSpace(nodeType))
            throw new ArgumentException("节点类型键不能为空。", nameof(nodeType));
        if (nodeVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(nodeVersion), "节点版本必须大于 0。");
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!typeof(IWorkflowNodeModel).IsAssignableFrom(modelType))
            throw new ArgumentException($"{modelType.FullName} 没有实现 IWorkflowNodeModel。", nameof(modelType));

        NodeType = nodeType.Trim();
        NodeVersion = nodeVersion;
        ModelType = modelType;
        Factory = factory;
        DisplayName = displayName;
        Category = category;
        Description = description;
        Ports = (ports ?? Array.Empty<WorkflowPortDescriptor>()).ToArray();
        OutputType = outputType;
        var duplicatePort = Ports
            .GroupBy(port => (port.Direction, port.Key))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePort is not null)
            throw new ArgumentException($"端口 {duplicatePort.Key.Key} 重复声明。", nameof(ports));
    }

    /// <summary>获取稳定节点类型键。</summary>
    public string NodeType { get; }

    /// <summary>获取节点配置版本。</summary>
    public int NodeVersion { get; }

    /// <summary>获取节点模型 CLR 类型。</summary>
    public Type ModelType { get; }

    /// <summary>获取节点实例工厂。</summary>
    public Func<IWorkflowNodeModel> Factory { get; }

    /// <summary>获取显示名称。</summary>
    public string? DisplayName { get; }

    /// <summary>获取工具箱分类。</summary>
    public string? Category { get; }

    /// <summary>获取节点说明。</summary>
    public string? Description { get; }

    /// <summary>获取节点声明的输入和输出端口。</summary>
    public IReadOnlyList<WorkflowPortDescriptor> Ports { get; }

    /// <summary>获取节点标准输出的静态类型；null 表示由宿主或运行时决定。</summary>
    public Type? OutputType { get; }

    /// <summary>根据节点模型类型及其特性创建不声明静态输出类型的描述器。</summary>
    /// <typeparam name="TNode">节点配置模型类型；必须实现节点接口并提供公开无参数构造函数。</typeparam>
    /// <param name="nodeVersion">节点配置格式版本，默认从 1 开始。</param>
    /// <param name="ports">节点的静态基础端口集合。</param>
    /// <returns>包含模型类型、实例工厂、端口和特性元数据的节点描述器。</returns>
    /// <exception cref="InvalidOperationException">节点特性键与模型的 <see cref="IWorkflowNodeModel.NodeType"/> 不一致。</exception>
    public static WorkflowNodeDescriptor Create<TNode>(
        int nodeVersion = 1,
        params WorkflowPortDescriptor[] ports)
        where TNode : class, IWorkflowNodeModel, new() =>
        CreateCore<TNode>(null, nodeVersion, ports);

    /// <summary>根据节点模型类型、标准输出类型及其特性创建节点描述器。</summary>
    /// <typeparam name="TNode">节点配置模型类型；必须实现节点接口并提供公开无参数构造函数。</typeparam>
    /// <typeparam name="TOutput">节点通过 <see cref="NodeExecutionResult.Output"/> 产生的标准输出类型；仅记录类型，不创建实例。</typeparam>
    /// <param name="nodeVersion">节点配置格式版本，默认从 1 开始。</param>
    /// <param name="ports">节点的静态基础端口集合。</param>
    /// <returns>包含模型类型、输出类型、实例工厂、端口和特性元数据的节点描述器。</returns>
    /// <exception cref="InvalidOperationException">节点特性键与模型的 <see cref="IWorkflowNodeModel.NodeType"/> 不一致。</exception>
    public static WorkflowNodeDescriptor Create<TNode, TOutput>(
        int nodeVersion = 1,
        params WorkflowPortDescriptor[] ports)
        where TNode : class, IWorkflowNodeModel, new() =>
        CreateCore<TNode>(typeof(TOutput), nodeVersion, ports);

    private static WorkflowNodeDescriptor CreateCore<TNode>(
        Type? outputType,
        int nodeVersion,
        WorkflowPortDescriptor[] ports)
        where TNode : class, IWorkflowNodeModel, new()
    {
        var modelType = typeof(TNode);
        var attribute = modelType.GetCustomAttribute<WorkflowNodeAttribute>();
        var sample = new TNode();
        var nodeType = attribute?.Key ?? sample.NodeType;
        if (!string.Equals(nodeType, sample.NodeType, StringComparison.Ordinal))
            throw new InvalidOperationException($"节点 {modelType.FullName} 的 Attribute Key 与 NodeType 不一致。");

        return new WorkflowNodeDescriptor(
            nodeType,
            nodeVersion,
            modelType,
            static () => new TNode(),
            attribute?.DisplayName,
            attribute?.Category,
            attribute?.Description,
            ports,
            outputType);
    }
}

/// <summary>
/// 保存已注册节点描述，并对稳定 Key 冲突进行诊断。
/// </summary>
public sealed class WorkflowNodeCatalog
{
    private readonly Dictionary<string, WorkflowNodeDescriptor> _descriptors = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();
    private bool _frozen;

    /// <summary>注册节点描述；重复节点类型键会抛出异常而不是静默覆盖。</summary>
    /// <param name="descriptor">要加入目录的节点类型描述。</param>
    /// <returns>当前目录实例，便于链式注册。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">目录中已经存在相同 <see cref="WorkflowNodeDescriptor.NodeType"/> 的描述。</exception>
    public WorkflowNodeCatalog Register(WorkflowNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_syncRoot)
        {
            EnsureMutable();
            if (_descriptors.TryGetValue(descriptor.NodeType, out var existing))
            {
                throw new InvalidOperationException(
                    $"节点类型键 {descriptor.NodeType} 已由 {existing.ModelType.FullName} 注册，不能再注册 {descriptor.ModelType.FullName}。");
            }

            _descriptors.Add(descriptor.NodeType, descriptor);
        }
        return this;
    }

    /// <summary>创建并注册一个不声明静态输出类型的强类型节点描述。</summary>
    /// <typeparam name="TNode">节点配置模型类型。</typeparam>
    /// <param name="nodeVersion">节点配置格式版本。</param>
    /// <param name="ports">节点的静态基础端口集合。</param>
    /// <returns>当前目录实例，便于链式注册。</returns>
    public WorkflowNodeCatalog Register<TNode>(
        int nodeVersion = 1,
        params WorkflowPortDescriptor[] ports)
        where TNode : class, IWorkflowNodeModel, new() =>
        Register(WorkflowNodeDescriptor.Create<TNode>(nodeVersion, ports));

    /// <summary>获取指定节点类型的描述。</summary>
    /// <param name="nodeType">要查找的稳定节点类型键。</param>
    /// <returns>已经注册的节点描述。</returns>
    /// <exception cref="ArgumentException"><paramref name="nodeType"/> 为空或仅包含空白字符。</exception>
    /// <exception cref="KeyNotFoundException">目录中不存在指定节点类型。</exception>
    public WorkflowNodeDescriptor GetOrThrow(string nodeType)
    {
        if (string.IsNullOrWhiteSpace(nodeType))
            throw new ArgumentException("节点类型键不能为空。", nameof(nodeType));
        lock (_syncRoot)
            return _descriptors.TryGetValue(nodeType, out var descriptor)
                ? descriptor
                : throw new KeyNotFoundException($"节点类型尚未注册：{nodeType}。");
    }

    /// <summary>尝试获取指定节点类型的描述。</summary>
    /// <param name="nodeType">要查找的稳定节点类型键。</param>
    /// <param name="descriptor">查找成功时返回节点描述，否则返回 <see langword="null"/>。</param>
    /// <returns>找到注册项时返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
    public bool TryGet(string nodeType, out WorkflowNodeDescriptor? descriptor)
    {
        lock (_syncRoot)
            return _descriptors.TryGetValue(nodeType, out descriptor);
    }

    /// <summary>获取目录是否已经冻结。</summary>
    public bool IsFrozen
    {
        get
        {
            lock (_syncRoot)
                return _frozen;
        }
    }

    /// <summary>
    /// 校验全部节点工厂是否可用。不会冻结目录，也不会改变任何状态，可重复调用。
    /// </summary>
    /// <remarks>
    /// 供"先对候选配置完整校验、通过后再发布"的调用方使用；校验失败后目录仍可继续注册。
    /// </remarks>
    /// <exception cref="InvalidOperationException">节点工厂失败、返回 null、返回错误模型类型或返回不一致的节点类型键。</exception>
    public void Validate()
    {
        lock (_syncRoot)
        {
            foreach (var descriptor in _descriptors.Values)
                ValidateFactory(descriptor);
        }
    }

    /// <summary>
    /// 校验所有节点工厂并冻结目录。冻结后仍可查询节点，但不能继续注册。
    /// </summary>
    /// <returns>冻结时刻的只读节点描述快照。</returns>
    /// <exception cref="InvalidOperationException">节点工厂失败、返回错误模型类型或返回不一致的节点类型键。</exception>
    public IReadOnlyDictionary<string, WorkflowNodeDescriptor> Freeze()
    {
        lock (_syncRoot)
        {
            if (!_frozen)
            {
                // 校验在置位之前完成：校验抛错时目录保持可变，不留下"已冻结"的外观。
                Validate();
                _frozen = true;
            }
            return CreateSnapshot();
        }
    }

    /// <summary>创建当前注册信息的只读快照。</summary>
    public IReadOnlyDictionary<string, WorkflowNodeDescriptor> Snapshot()
    {
        lock (_syncRoot)
            return CreateSnapshot();
    }

    private IReadOnlyDictionary<string, WorkflowNodeDescriptor> CreateSnapshot() =>
        new ReadOnlyDictionary<string, WorkflowNodeDescriptor>(
            new Dictionary<string, WorkflowNodeDescriptor>(_descriptors, StringComparer.Ordinal));

    private static void ValidateFactory(WorkflowNodeDescriptor descriptor)
    {
        IWorkflowNodeModel? node;
        try
        {
            node = descriptor.Factory();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"节点 {descriptor.NodeType} 的工厂无法创建模型。", exception);
        }
        if (node is null)
            throw new InvalidOperationException($"节点 {descriptor.NodeType} 的工厂返回了 null。");
        if (!descriptor.ModelType.IsInstanceOfType(node))
        {
            throw new InvalidOperationException(
                $"节点 {descriptor.NodeType} 的工厂应创建 {descriptor.ModelType.FullName}，实际创建了 {node.GetType().FullName}。");
        }
        if (!string.Equals(descriptor.NodeType, node.NodeType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"节点 {descriptor.NodeType} 的工厂创建了 NodeType 为 {node.NodeType} 的模型。");
        }
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException("节点类型目录已经冻结，不能继续注册节点。");
    }
}
