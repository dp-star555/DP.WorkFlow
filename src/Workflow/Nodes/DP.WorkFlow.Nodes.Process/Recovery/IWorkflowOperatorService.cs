namespace DP.WorkFlow;

/// <summary>操作员多选一选项。</summary>
public sealed record OperatorChoiceOption(string Key, string Text);

/// <summary>提供 UI 无关的操作员选择和确认能力。</summary>
public interface IWorkflowOperatorService
{
    /// <summary>显示多选一对话框并返回所选稳定 Key。</summary>
    ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken);
    /// <summary>显示单步确认对话框并等待操作员确认。</summary>
    ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken);
}
