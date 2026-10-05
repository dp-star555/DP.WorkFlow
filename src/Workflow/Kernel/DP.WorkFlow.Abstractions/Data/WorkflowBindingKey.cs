namespace DP.WorkFlow;

/// <summary>
/// 唯一标识一个节点标准输出中的成员路径。
/// </summary>
public readonly record struct WorkflowBindingKey
{
    private const string GlobalVariablePrefix = "$global:";
    /// <summary>初始化一个节点输出或公共数据的绑定键。</summary>
    /// <param name="nodeId">来源节点 ID；公共数据绑定继续使用兼容前缀 <c>$global:</c>。</param>
    /// <param name="memberPath">来源对象的公开属性路径；<c>$</c> 表示使用整个根值。</param>
    /// <exception cref="ArgumentException">任一参数为空，或参数包含序列化分隔符 <c>|</c>。</exception>
    public WorkflowBindingKey(string nodeId, string memberPath)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("绑定来源节点 ID 不能为空。", nameof(nodeId));
        if (string.IsNullOrWhiteSpace(memberPath))
            throw new ArgumentException("绑定成员路径不能为空。", nameof(memberPath));
        if (nodeId.Contains('|') || memberPath.Contains('|'))
            throw new ArgumentException("绑定节点 ID 和成员路径不能包含分隔符 |。");

        NodeId = nodeId.Trim();
        MemberPath = memberPath.Trim();
    }

    /// <summary>获取来源节点 ID。</summary>
    public string NodeId { get; }

    /// <summary>获取输出对象成员路径；$ 表示整个输出对象。</summary>
    public string MemberPath { get; }

    /// <summary>当前绑定是否来源于独立公共数据仓。</summary>
    public bool IsPublicData => NodeId.StartsWith(GlobalVariablePrefix, StringComparison.Ordinal);

    /// <summary>公共数据键；非公共数据绑定时为空。</summary>
    public string? PublicDataKey => IsPublicData ? NodeId[GlobalVariablePrefix.Length..] : null;

    /// <summary>创建指向独立公共数据仓的绑定；保留 <c>$global:</c> 前缀以兼容已有文档键。</summary>
    public static WorkflowBindingKey FromPublicData(string key, string memberPath = "$")
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("公共数据键不能为空。", nameof(key));
        if (key.Contains('|')) throw new ArgumentException("公共数据键不能包含分隔符 |。", nameof(key));
        return new WorkflowBindingKey(GlobalVariablePrefix + key.Trim(), memberPath);
    }

    /// <inheritdoc />
    public override string ToString() => $"{NodeId}|{MemberPath}";

    /// <summary>尝试解析持久化使用的 <c>nodeId|memberPath</c> 兼容格式。</summary>
    /// <param name="value">要解析的文本；必须恰好包含一个非首尾的 <c>|</c> 分隔符。</param>
    /// <param name="bindingKey">解析成功时返回绑定键；失败时返回默认值。</param>
    /// <returns>文本满足绑定键格式和构造约束时返回 <see langword="true"/>。</returns>
    public static bool TryParse(string? value, out WorkflowBindingKey bindingKey)
    {
        bindingKey = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var separatorIndex = value.IndexOf('|');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1 || value.IndexOf('|', separatorIndex + 1) >= 0)
            return false;

        try
        {
            bindingKey = new WorkflowBindingKey(value[..separatorIndex], value[(separatorIndex + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
