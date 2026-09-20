namespace DP.WorkFlow;

/// <summary>
/// 定义框架内置的标准端口键。
/// </summary>
public static class WorkflowPorts
{
    /// <summary>普通节点执行成功后的默认出口。</summary>
    public const string Success = "Success";

    /// <summary>布尔判断成立时的出口。</summary>
    public const string True = "True";

    /// <summary>布尔判断不成立时的出口。</summary>
    public const string False = "False";

    /// <summary>等待操作超时时的出口。</summary>
    public const string Timeout = "Timeout";

    /// <summary>并行节点用于派发多个执行分支的出口。</summary>
    public const string Branch = "Branch";

    /// <summary>循环节点继续执行循环体时的出口。</summary>
    public const string Loop = "Loop";

    /// <summary>循环或复合控制节点完成时的出口。</summary>
    public const string Completed = "Completed";

    /// <summary>节点的默认输入端口。</summary>
    public const string Input = "Input";
}
