namespace DP.WorkFlow;

/// <summary>为节点参数提供跨 WinForms/WPF 共享的中文显示元数据。</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class WorkflowPropertyAttribute : Attribute
{
    /// <summary>创建节点参数元数据。</summary>
    /// <param name="displayName">参数中文名称。</param>
    /// <param name="description">参数中文说明。</param>
    public WorkflowPropertyAttribute(string displayName, string description)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("参数显示名称不能为空。", nameof(displayName));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("参数说明不能为空。", nameof(description));
        DisplayName = displayName.Trim();
        Description = description.Trim();
    }

    /// <summary>参数中文名称。</summary>
    public string DisplayName { get; }

    /// <summary>参数中文说明。</summary>
    public string Description { get; }

    /// <summary>参数中文分类；为空时使用约定分类。</summary>
    public string? Category { get; set; }

    /// <summary>可选的单位文本，例如 ms、mm 或 ℃。</summary>
    public string? Unit { get; set; }
}
