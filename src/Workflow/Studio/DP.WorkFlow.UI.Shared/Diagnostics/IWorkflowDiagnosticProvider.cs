namespace DP.WorkFlow.UI;

/// <summary>领域诊断扩展；共享工作台不引用视觉或厂商类型。</summary>
public interface IWorkflowDiagnosticProvider
{
    /// <summary>返回当前文档及其子文档的轻量检查。</summary>
    IReadOnlyList<WorkflowDiagnosticItem> Analyze(WorkflowDocument document);
    /// <summary>文档编辑后使上一次资源检查及运行问题失效。</summary>
    void Invalidate();
    /// <summary>后台准备报告更新，界面须切回 UI 线程刷新。</summary>
    event EventHandler? Changed;
}
