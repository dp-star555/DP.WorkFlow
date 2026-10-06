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

    /// <summary>
    /// 节点执行失败时的出口。已连线时故障照常记录（节点标记失败），但本次运行不中止，沿该出口继续；
    /// 未连线时按原有规则中止运行或交给恢复处理。
    /// </summary>
    public const string Failed = "Failed";

    /// <summary>节点的默认输入端口。</summary>
    public const string Input = "Input";

    /// <summary>端口在设计器中显示的中文名称；未知端口返回端口键本身。</summary>
    /// <param name="key">端口稳定键。</param>
    public static string GetDisplayName(string key) => key switch
    {
        Success => "成功",
        Failed => "失败",
        True => "成立",
        False => "不成立",
        Timeout => "超时",
        Branch => "分支",
        Loop => "循环",
        Completed => "完成",
        Input => "输入",
        _ => key
    };
}
