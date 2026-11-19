namespace DP.WorkFlow;

/// <summary>
/// 标记可由 DP.WorkFlow 插件目录发现的节点模型，并提供稳定的节点类型元数据。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class WorkflowNodeAttribute : Attribute
{
    /// <summary>
    /// 初始化节点元数据。
    /// </summary>
    /// <param name="key">跨程序集和文档版本保持稳定的节点类型键。</param>
    public WorkflowNodeAttribute(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("节点类型键不能为空。", nameof(key));

        Key = key.Trim();
    }

    /// <summary>获取稳定的节点类型键。</summary>
    public string Key { get; }

    /// <summary>获取或设置节点显示名称。</summary>
    public string? DisplayName { get; set; }

    /// <summary>获取或设置工具箱分类路径。</summary>
    public string Category { get; set; } = "General";

    /// <summary>获取或设置节点用途说明。</summary>
    public string? Description { get; set; }

    /// <summary>获取或设置节点图标资源名称。</summary>
    public string? IconResourceName { get; set; }

    /// <summary>
    /// 获取或设置画布和工具箱显示的图标：设计器内置的图标键（如 "camera"），或 24×24 视图框的 SVG 路径数据（线框描边）。
    /// 为空时设计器按节点类型和分类选择图标。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>节点配置格式版本，默认 1。</summary>
    public int Version { get; set; } = 1;

    /// <summary>节点标准输出的静态类型；为空表示设计期无法确定。</summary>
    public Type? OutputType { get; set; }

    /// <summary>是否有输入端口（<see cref="WorkflowPorts.Input"/>，不限连接数，显示在上边）。只有入口类节点为 false。</summary>
    public bool HasInput { get; set; } = true;
}

/// <summary>
/// 节点的输出端口（R7-a：端口写在节点模型上）。未标注时节点只有一个 <see cref="WorkflowPorts.Success"/> 出口；
/// 标注但不给端口键表示没有输出端口（例如结束节点）。可标注多次，按出现顺序排列。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class WorkflowOutputPortsAttribute : Attribute
{
    /// <summary>声明输出端口。</summary>
    /// <param name="keys">端口键，按显示顺序。</param>
    public WorkflowOutputPortsAttribute(params string[] keys)
    {
        Keys = keys ?? Array.Empty<string>();
    }

    /// <summary>端口键。</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>每个端口允许引出的最大连线数，默认 1。</summary>
    public int MaxConnections { get; set; } = 1;

    /// <summary>是否在这些端口之后加默认不显示的失败出口（<see cref="WorkflowPortDescriptor.Failure"/>）。</summary>
    public bool FailurePort { get; set; }
}
