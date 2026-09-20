namespace DP.WorkFlow;

/// <summary>产品流转状态。</summary>
public enum E_ProductFlowState { Active = 0, Succeeded = 1, Failed = 2 }
/// <summary>开始流转的模式。</summary>
public enum E_ProductMoveStartMode { Normal = 0, VirtualCreate = 1 }

/// <summary>开始产品流转请求。</summary>
public sealed record ProductMoveStartRequest(E_ProductMoveStartMode MoveMode, string? FromStationId, string? FromSlotId, string ToStationId, string? ToSlotId, string? Reason, string? ProductId, bool AutoGenerateProductId, string Recipe);
/// <summary>开始产品流转结果。</summary>
public sealed record ProductMoveStartNodeResult(string SessionId, string ProductId, string? FromStationId, string? FromSlotId, string ToStationId, string? ToSlotId, E_ProductFlowState State);
/// <summary>完成产品流转结果。</summary>
public sealed record ProductMoveFinishNodeResult(string SessionId, string ProductId, string? FromStationId, string? FromSlotId, string ToStationId, string? ToSlotId, E_ProductFlowState State, bool Success, string? FailReason);
/// <summary>工站布尔判断结果。</summary>
public sealed record StationBooleanNodeResult(string StationId, string? SlotId, bool Value);
/// <summary>工站等待结果。</summary>
public sealed record StationWaitNodeResult(string StationId, string? SlotId, bool Success);
/// <summary>工站完成结果。</summary>
public sealed record StationFinishedNodeResult(string StationId, string? SlotId, bool Success);

/// <summary>提供强类型产品流转和工站状态操作。</summary>
public interface IWorkflowProductFlowService
{
    /// <summary>开始正常或虚拟创建型流转。</summary>
    ValueTask<ProductMoveStartNodeResult> StartMoveAsync(ProductMoveStartRequest request, CancellationToken cancellationToken);
    /// <summary>提交流转成功并完成出入站事务。</summary>
    ValueTask<ProductMoveFinishNodeResult> CompleteMoveAsync(string sessionId, CancellationToken cancellationToken);
    /// <summary>提交流转失败并回滚目标工站预接料。</summary>
    ValueTask<ProductMoveFinishNodeResult> FailMoveAsync(string sessionId, string? failReason, CancellationToken cancellationToken);
    /// <summary>判断工站是否允许进料。</summary>
    ValueTask<bool> CanReceiveAsync(string stationId, string? slotId, CancellationToken cancellationToken);
    /// <summary>判断工站是否允许出料。</summary>
    ValueTask<bool> CanSendAsync(string stationId, string? slotId, CancellationToken cancellationToken);
    /// <summary>判断指定槽位是否存在产品。</summary>
    ValueTask<bool> SlotHasProductAsync(string stationId, string slotId, CancellationToken cancellationToken);
    /// <summary>标记工站槽位处理完成。</summary>
    ValueTask MarkStationFinishedAsync(string stationId, string? slotId, CancellationToken cancellationToken);
}

/// <summary>提供产品流节点共享配置和解析。</summary>
public abstract class ProductFlowStationSlotNodeModel : WorkflowNodeModel
{
    /// <summary>工站来源。</summary>
    public E_ProductFlowValueSource StationIdSource { get; set; }
    /// <summary>固定工站 ID。</summary>
    public string StationId { get; set; } = string.Empty;
    /// <summary>绑定工站 ID。</summary>
    public WorkflowInput<string> StationIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>槽位来源。</summary>
    public E_ProductFlowValueSource SlotIdSource { get; set; }
    /// <summary>固定槽位 ID。</summary>
    public string SlotId { get; set; } = string.Empty;
    /// <summary>绑定槽位 ID。</summary>
    public WorkflowInput<string> SlotIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
}

internal static class ProductFlowNodeRuntime
{
    internal static IWorkflowProductFlowService GetService(IWorkflowNodeExecutionContext context) =>
        context.GetRequiredCapability<IWorkflowProductFlowService>();
    internal static string ResolveRequired(E_ProductFlowValueSource source, string literal, WorkflowInput<string> binding, IWorkflowNodeExecutionContext context, string field)
    {
        var value = ResolveOptional(source, literal, binding, context);
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(field + " 为空。") : value;
    }
    internal static string? ResolveOptional(E_ProductFlowValueSource source, string literal, WorkflowInput<string> binding, IWorkflowNodeExecutionContext context)
    {
        var value = source == E_ProductFlowValueSource.Binding ? context.ResolveInput(binding) : literal;
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
    internal static (string StationId, string? SlotId) ResolveAddress(ProductFlowStationSlotNodeModel node, IWorkflowNodeExecutionContext context, bool slotRequired = false)
    {
        var station = ResolveRequired(node.StationIdSource, node.StationId, node.StationIdBinding, context, "StationId");
        var slot = ResolveOptional(node.SlotIdSource, node.SlotId, node.SlotIdBinding, context);
        if (slotRequired && slot is null) throw new InvalidOperationException("SlotId 为空。");
        return (station, slot);
    }
    internal static async ValueTask<bool> WaitAsync(Func<CancellationToken, ValueTask<bool>> predicate, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (timeoutMs <= 0 || started.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await predicate(cancellationToken).ConfigureAwait(false)) return true;
            await Task.Delay(Math.Max(5, pollIntervalMs), cancellationToken).ConfigureAwait(false);
        }
        return false;
    }
}
