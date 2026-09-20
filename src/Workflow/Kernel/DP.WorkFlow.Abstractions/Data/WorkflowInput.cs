namespace DP.WorkFlow;

/// <summary>
/// 指定节点输入值的来源。
/// </summary>
public enum WorkflowValueSource
{
    /// <summary>直接使用节点配置中的固定值。</summary>
    Literal = 0,

    /// <summary>从可见的上游节点输出解析值。</summary>
    Binding = 1
}

/// <summary>
/// 表示可使用固定值或上游绑定值的强类型节点输入。
/// </summary>
public sealed record WorkflowInput<T>
{
    /// <summary>获取或初始化值来源。</summary>
    public WorkflowValueSource Source { get; init; }

    /// <summary>获取或初始化固定值。</summary>
    public T? LiteralValue { get; init; }

    /// <summary>获取或初始化上游输出绑定。</summary>
    public WorkflowBindingKey? Binding { get; init; }

    /// <summary>创建直接使用节点配置值的输入。</summary>
    /// <param name="value">要保存到节点配置中的固定值；引用类型和可空值类型允许为空。</param>
    /// <returns>来源为 <see cref="WorkflowValueSource.Literal"/> 的强类型输入。</returns>
    public static WorkflowInput<T> FromLiteral(T? value) => new()
    {
        Source = WorkflowValueSource.Literal,
        LiteralValue = value
    };

    /// <summary>创建运行时从节点输出或公共数据解析的输入。</summary>
    /// <param name="binding">描述来源及成员路径的绑定键。</param>
    /// <returns>来源为 <see cref="WorkflowValueSource.Binding"/> 的强类型输入。</returns>
    public static WorkflowInput<T> FromBinding(WorkflowBindingKey binding) => new()
    {
        Source = WorkflowValueSource.Binding,
        Binding = binding
    };

    /// <summary>验证值来源与配置内容是否一致。</summary>
    /// <returns>配置有效时返回 <see langword="null"/>；否则返回可直接用于诊断的错误消息。</returns>
    public string? Validate() => Source switch
    {
        WorkflowValueSource.Literal => null,
        WorkflowValueSource.Binding when Binding.HasValue => null,
        WorkflowValueSource.Binding => "绑定模式下必须配置 Binding。",
        _ => $"不支持的输入来源：{Source}。"
    };
}
