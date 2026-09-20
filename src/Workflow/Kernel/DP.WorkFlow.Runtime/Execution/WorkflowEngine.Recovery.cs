using System.Collections.Concurrent;

namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    private readonly ConcurrentDictionary<(long TokenId, string NodeId), PendingOperation> _operations = new();
    private readonly ConcurrentDictionary<string, WorkflowNodeOutput> _recoveryEntries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, IReadOnlyList<string>> _committedVariableKeys = new();
    private int _recoveryAttempt;
    private string? _jointInterruption;
    private readonly AsyncManualResetEvent _jointSignal = new();

    internal void RequestJointInterruption(string message)
    {
        Interlocked.CompareExchange(ref _jointInterruption, message, null);
        _jointSignal.Set();
    }

    private async Task WaitForAdmissionAsync(CancellationToken cancellationToken)
    {
        if (_options.RecoveryBarrier is null)
        {
            await _resumeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        // 协作中断可以唤醒已暂停的边界以报告退出；不解除人工Pause或任何其他Hold。
        while (Volatile.Read(ref _jointInterruption) is null && !_resumeGate.IsSet)
        {
            await Task.WhenAny(_resumeGate.WaitAsync(CancellationToken.None), _jointSignal.WaitAsync(CancellationToken.None))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            _jointSignal.Reset();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed record PendingOperation(Guid Id, IWorkflowNodeOperation Instance);

    private async Task<string> RecoverPathAsync(WorkflowPathException exception, ExecutionToken token, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _jointInterruption, null);
        if (exception.ExecutionIdentity is { ScopeIds.Count: > 0 })
            throw new WorkflowPathException(exception.NodeId,
                $"节点 {exception.NodeId} 位于并行作用域中；在恢复流程能够保留原 Token/Scope 身份前，禁止 Retry 或 Jump。",
                exception, exception.ExecutionIdentity);

        var node = _plan.GetNodeOrThrow(exception.NodeId);
        var coordinator = _options.RecoveryCoordinator ?? (IWorkflowFaultRecoveryCoordinator)Context.Services.GetService(typeof(IWorkflowFaultRecoveryCoordinator))!;
        _operations.TryGetValue((token.TokenId, node.Id), out var operation);
        var request = new WorkflowFaultRecoveryRequest(_plan.Name, node.Id, node.Title,
            exception.Message, exception.InterruptAlarmCode, exception.ExecutionIdentity, 0)
        {
            CaseId = _options.RecoveryCaseId ?? Guid.NewGuid(),
            OperationId = operation?.Id,
            CanContinueOperation = operation?.Instance.CanContinue == true,
            IsBoundaryInterruption = exception.IsBoundaryInterruption,
            ExceptionType = exception.InnerException?.GetType().FullName,
            LoopIterations = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int>(token.CopyLoopFrames())),
            CommitStarted = exception.CommitStarted
        };
        var hold = $"Recovery:{request.CaseId}";
        AddExternalHold(hold);
        try
        {
            while (_recoveryAttempt < _options.MaxRecoveryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                request = request with { Attempt = ++_recoveryAttempt };
                RecordRecovery(request, "Waiting", request.RecoveryFailure ?? request.Message);
                try
                {
                    var recoveryContext = new RecoveryContextAdapter(this, node, exception.ExecutionIdentity!, hold);
                    WorkflowFaultRecoveryDecision decision;
                    try
                    {
                        decision = await coordinator.RecoverAsync(request, recoveryContext, cancellationToken).ConfigureAwait(false)
                            ?? WorkflowFaultRecoveryDecision.Stop("恢复协调器返回空裁决。");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception treatmentFailure)
                    {
                        // 处置中可能已经移动设备；不能从处理入口盲目重放整段处置。
                        throw new RecoveryStoppedException("异常处置执行失败，禁止自动重放：" + treatmentFailure.Message);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (decision.Action == WorkflowFaultRecoveryAction.Stop)
                        throw new RecoveryStoppedException(decision.Message ?? exception.Message);
                    if (request.CommitStarted)
                        throw new InvalidOperationException("故障发生于提交或清理阶段；V1不能确认提交状态，禁止重新执行。");
                    string nextNodeId;
                    switch (decision.Action)
                    {
                        case WorkflowFaultRecoveryAction.ContinueOperation:
                            if (!exception.IsBoundaryInterruption &&
                                (!_operations.TryGetValue((token.TokenId, node.Id), out var retained) || !retained.Instance.CanContinue))
                                throw new InvalidOperationException("当前节点没有保留的操作实例，不能继续原操作；请配置恢复入口或停止。");
                            nextNodeId = node.Id;
                            break;
                        case WorkflowFaultRecoveryAction.RestartFromEntry:
                            nextNodeId = await RestartFromEntryAsync(decision.RecoveryEntryKey, request, token, cancellationToken).ConfigureAwait(false);
                            break;
                        case WorkflowFaultRecoveryAction.RetryFaultedNode:
                            if (node is not IWorkflowRetrySafetyNode { RetrySafety: WorkflowRetrySafety.Idempotent or WorkflowRetrySafety.ResumeAware })
                                throw new RecoveryStoppedException($"节点 {node.Id} 未声明 Idempotent 或 ResumeAware，禁止重试。");
                            nextNodeId = node.Id;
                            break;
                        case WorkflowFaultRecoveryAction.JumpToNode:
                            throw new RecoveryStoppedException("旧Jump恢复已禁用；请使用已声明并验证的命名恢复入口。");
                        default:
                            throw new InvalidOperationException("恢复协调器返回未知裁决。");
                    }
                    if (_options.RecoveryBarrier is { } barrier)
                        await barrier.PreparedAsync(request, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    RecordRecovery(request, "Applied", decision.Message ?? nextNodeId);
                    return nextNodeId;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (RecoveryStoppedException stopped)
                {
                    _options.RecoveryBarrier?.Reject(request, stopped.Message);
                    RecordRecovery(request, "Stopped", stopped.Message);
                    throw new WorkflowPathException(node.Id, stopped.Message, exception, exception.ExecutionIdentity);
                }
                catch (Exception failure)
                {
                    if (_options.RecoveryBarrier is { } rejectedBarrier)
                    {
                        rejectedBarrier.Reject(request, failure.Message);
                        RecordRecovery(request, "Stopped", failure.Message);
                        throw new WorkflowPathException(node.Id, failure.Message, failure, exception.ExecutionIdentity);
                    }
                    request = request with { RecoveryFailure = failure.Message };
                    RecordRecovery(request, "Rejected", failure.Message);
                }
            }
            throw new WorkflowPathException(node.Id,
                $"故障恢复次数超过上限 {_options.MaxRecoveryAttempts}。{request.RecoveryFailure}", exception, exception.ExecutionIdentity);
        }
        finally { RemoveExternalHold(hold); }
    }

    private async Task<string> RestartFromEntryAsync(string? entryKey, WorkflowFaultRecoveryRequest request,
        ExecutionToken token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
            throw new InvalidOperationException("恢复请求缺少命名入口键。");
        entryKey = entryKey.Trim();
        if (!_plan.IsAcyclic || _plan.ParallelScopes.Count != 0 || token.CopyLoopFrames().Count != 0)
            throw new InvalidOperationException("V1命名入口重执行仅支持无环串行计划，禁止循环或并行回退。");
        var entries = _plan.Nodes.Values.Where(node => node is IWorkflowRecoveryEntryNode entry
            && string.Equals(entry.RecoveryEntryKey, entryKey, StringComparison.Ordinal)).ToArray();
        if (entries.Length != 1 || !_recoveryEntries.TryGetValue(entryKey, out var reached)
            || reached.TokenId != token.TokenId)
            throw new InvalidOperationException($"恢复入口 {entryKey} 必须唯一且已由当前Token实际经过。");
        var outputs = RunState.ValidNodeOutputs.Where(output => output.ExecutionSequence >= reached.ExecutionSequence).ToArray();
        var affected = outputs.Select(output => output.NodeId).Append(request.FaultNodeId).Distinct(StringComparer.Ordinal).ToArray();
        if (affected.Any(id => _plan.GetNodeOrThrow(id) is IWorkflowSubDocumentNode))
            throw new InvalidOperationException("V1不支持跨Block重执行；请在所属子流程内处理或停止。");
        var variables = _committedVariableKeys.Where(pair => pair.Key >= reached.ExecutionSequence)
            .SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var plan = new WorkflowRecoveryEntryPlan(entryKey, reached.NodeId, request,
            Array.AsReadOnly(affected), Array.AsReadOnly(variables));
        var guard = Context.Services.GetService(typeof(IWorkflowRecoveryEntryGuard)) as IWorkflowRecoveryEntryGuard
            ?? throw new InvalidOperationException("缺少工位恢复入口验证能力，禁止重执行。");
        var validation = await guard.ValidateAsync(plan, Context, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (validation is not { Allowed: true })
            throw new InvalidOperationException(validation?.Message ?? "恢复入口条件未通过。");

        // 处置与验证完成后才丢弃原操作。释放失败不能推进到恢复入口。
        if (_operations.TryRemove((token.TokenId, request.FaultNodeId), out var abandoned))
        {
            try { await abandoned.Instance.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { throw new RecoveryStoppedException("原操作清理失败，禁止重执行：" + failure.Message); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        RunState.InvalidateOutputsFrom(reached.ExecutionSequence);
        foreach (var key in variables) Context.RemoveVariable(key);
        foreach (var pair in _recoveryEntries.Where(pair => pair.Value.ExecutionSequence >= reached.ExecutionSequence))
            _recoveryEntries.TryRemove(pair.Key, out _);
        foreach (var sequence in _committedVariableKeys.Keys.Where(sequence => sequence >= reached.ExecutionSequence))
            _committedVariableKeys.TryRemove(sequence, out _);
        return reached.NodeId;
    }

    private void RecordRecovery(WorkflowFaultRecoveryRequest request, string stage, string? message)
    {
        RunState.RecordRecovery(new WorkflowRecoveryEvent(request.CaseId, request.FaultNodeId,
            request.Attempt, stage, message, request.OperationId, DateTimeOffset.UtcNow));
        WriteTrace(_plan.GetNodeOrThrow(request.FaultNodeId), request.ExecutionIdentity!,
            "Recovery" + stage, message, new Dictionary<string, object?> { ["CaseId"] = request.CaseId, ["OperationId"] = request.OperationId });
        PublishSnapshot();
    }

    private async Task DisposePendingOperationsAsync()
    {
        foreach (var pair in _operations.ToArray())
        {
            if (!_operations.TryRemove(pair.Key, out var operation)) continue;
            try { await operation.Instance.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure)
            {
                var identity = new WorkflowExecutionIdentity(_runId, pair.Key.TokenId, Array.Empty<long>(), Array.Empty<long>(), 0);
                RunState.RecordFault(new WorkflowNodeFault(pair.Key.NodeId, identity,
                    "操作清理失败：" + failure.Message, 0, failure.GetType().FullName, DateTimeOffset.UtcNow));
            }
        }
    }

    private static IEnumerable<IWorkflowNodeModel> EnumerateRecoveryNodes(WorkflowExecutionPlan plan)
    {
        foreach (var id in plan.NodeIds) yield return plan.GetNodeOrThrow(id);
        foreach (var child in plan.ChildPlans.Values)
        foreach (var node in EnumerateRecoveryNodes(child)) yield return node;
    }

    private sealed class RecoveryStoppedException(string message) : Exception(message) { }

    private sealed class RecoveryContextAdapter(WorkflowEngine engine, IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity, string hold) : IWorkflowFaultRecoveryContext, IWorkflowRecoverySubflowContext
    {
        public IServiceProvider Services => engine.Context.Services;
        public bool TryGetVariable<T>(string key, out T? value) => engine.Context.TryGetVariable(key, out value);
        public void SetVariable(string key, object value) => engine.Context.SetVariable(key, value);
        public Task<WorkflowRunResult> RunRecoverySubflowAsync(WorkflowBoundExecutionPlan plan,
            WorkflowContext context, WorkflowExecutionOptions options, CancellationToken cancellationToken) =>
            engine.RunTrackedChildAsync(node, identity, plan, context, options, hold, true, cancellationToken);
    }
}
