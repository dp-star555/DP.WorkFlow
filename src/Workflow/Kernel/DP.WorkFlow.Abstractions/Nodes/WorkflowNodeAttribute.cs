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
}
