namespace DP.WorkFlow;

/// <summary>
/// 表示工作流输入绑定无法解析或转换。
/// </summary>
public sealed class WorkflowBindingException : Exception
{
    /// <summary>初始化包含持久化绑定键上下文的解析异常。</summary>
    /// <param name="binding">无法读取成员或转换值的绑定键。</param>
    /// <param name="message">具体失败原因，不需要重复绑定键。</param>
    /// <param name="innerException">属性 getter 或值转换抛出的原始异常。</param>
    public WorkflowBindingException(WorkflowBindingKey binding, string message, Exception? innerException = null)
        : base($"绑定 {binding} 解析失败：{message}", innerException)
    {
        Binding = binding;
    }

    /// <summary>获取失败的绑定键。</summary>
    public WorkflowBindingKey Binding { get; }
}
