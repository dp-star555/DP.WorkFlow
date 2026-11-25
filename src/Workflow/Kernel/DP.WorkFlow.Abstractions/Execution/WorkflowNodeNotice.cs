namespace DP.WorkFlow;

/// <summary>
/// 接收节点提示的执行上下文（R1-01 节点提示）。提示是不影响运行结果的非致命信息，
/// 例如数据被截断、设备返回了可容忍的告警；界面在节点右上角显示黄色角标，确认后清除。
/// </summary>
public interface IWorkflowNodeNoticeSink
{
    /// <summary>为当前节点添加一条提示。</summary>
    /// <param name="category">类别，例如"记录"、"资源"、"设备"。</param>
    /// <param name="message">面向使用者的说明。</param>
    void RaiseNotice(string category, string message);
}

/// <summary>节点提示的入口。</summary>
public static class WorkflowNodeNoticeExtensions
{
    /// <summary>为当前节点添加一条提示；上下文不支持提示时忽略。提示不改变节点结果。</summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="category">类别。</param>
    /// <param name="message">说明。</param>
    public static void RaiseNotice(this IWorkflowNodeExecutionContext context, string category, string message)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(message))
            return;
        (context as IWorkflowNodeNoticeSink)?.RaiseNotice(
            string.IsNullOrWhiteSpace(category) ? "提示" : category.Trim(), message.Trim());
    }
}
