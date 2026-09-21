namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    /// <summary>沿节点选择的输出端口推进一个 Token，必要时派发并等待结构化并行。</summary>
    /// <param name="startNodeId">该路径的入口节点 ID。</param>
    /// <param name="stopBeforeNodeId">并行分支边界；到达该汇聚节点前返回且不执行它。</param>
    /// <param name="token">携带祖先 Token 和并行 Scope 路径的执行身份。</param>
    /// <param name="cancellationToken">路径执行和暂停门等待的取消令牌。</param>
    private async Task ExecutePathAsync(
        string startNodeId,
        string? stopBeforeNodeId,
        ExecutionToken token,
        CancellationToken cancellationToken)
    {
        string? currentNodeId = startNodeId;
        while (!string.IsNullOrWhiteSpace(currentNodeId))
        {
            if (string.Equals(currentNodeId, stopBeforeNodeId, StringComparison.Ordinal))
                return;

            await WaitForAdmissionAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var node = _plan.GetNodeOrThrow(currentNodeId);
            var interruption = Interlocked.Exchange(ref _jointInterruption, null);
            if (interruption is not null)
                throw new WorkflowPathException(node.Id, interruption, executionIdentity:
                    new WorkflowExecutionIdentity(_runId, token.TokenId, Array.Empty<long>(), Array.Empty<long>(), 0),
                    faultDisposition: WorkflowFaultDisposition.RequestRecovery) { IsBoundaryInterruption = true };
            var result = await ExecuteNodeAsync(node, token, cancellationToken).ConfigureAwait(false);
            if (result.CompleteCurrentPath)
            {
                if (stopBeforeNodeId is not null)
                    throw new WorkflowPathException(node.Id, $"并行分支在到达汇聚节点 {stopBeforeNodeId} 前提前结束。");
                return;
            }

            var selectedPort = result.SelectedPortKey ?? WorkflowPorts.Success;
            var nextNodeIds = _plan.GetNextNodeIds(node.Id, selectedPort);
            if (nextNodeIds.Count == 0)
            {
                if (stopBeforeNodeId is not null)
                    throw new WorkflowPathException(node.Id, $"并行分支在到达汇聚节点 {stopBeforeNodeId} 前没有后继节点。");
                return;
            }

            if (nextNodeIds.Count > 1)
            {
                if (node is not IWorkflowParallelForkNode
                    || !string.Equals(selectedPort, WorkflowPorts.Branch, StringComparison.Ordinal))
                {
                    throw new WorkflowPathException(node.Id, $"节点端口 {selectedPort} 连接了多个目标，但节点不是 ParallelAll。");
                }

                var scope = _plan.GetParallelScopeOrThrow(node.Id);
                await ExecuteParallelScopeAsync(node, scope, token, cancellationToken).ConfigureAwait(false);
                currentNodeId = scope.MergeNodeId;
                continue;
            }

            currentNodeId = nextNodeIds[0];
        }

        if (stopBeforeNodeId is not null)
            throw new WorkflowPathException(startNodeId, $"并行分支未到达汇聚节点 {stopBeforeNodeId}。");
    }

    /// <summary>解析处理器并执行一次节点，同时维护状态、Trace、输出和错误包装。</summary>
    /// <param name="node">本次执行的节点配置。</param>
    /// <param name="token">节点所属的执行路径 Token。</param>
    /// <param name="cancellationToken">传递给节点处理器的取消令牌。</param>
    /// <returns>处理器选择的后继端口、输出及路径控制信息。</returns>
    private async Task<NodeExecutionResult> ExecuteNodeAsync(
        IWorkflowNodeModel node,
        ExecutionToken token,
        CancellationToken cancellationToken)
    {
        var nodeExecutionCount = GetNextNodeExecutionCount(node.Id);
        int? loopIteration = node is IWorkflowLoopNode { Iterations: >= 0 }
            ? token.PrepareLoopIteration(node.Id)
            : null;
        var identity = new WorkflowExecutionIdentity(
            _runId,
            token.TokenId,
            token.AncestorTokenIds,
            token.ScopeIds,
            nodeExecutionCount,
            loopIteration);
        MarkNodeStarted(node, identity);
        SafeInvoke(NodeStarted, node);
        WriteTrace(node, identity, "NodeStarted", "节点开始执行。", null);
        BeginNodeTiming(node.Id);
        var commitStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handler = _boundPlan.GetHandler(node.Id);
            var executionContext = new WorkflowNodeExecutionContext(
                Context,
                node,
                identity,
                _plan.ChildPlans.TryGetValue(node.Id, out var childDefinition) ? childDefinition : null,
                (traceNode, step, message, data) => WriteTrace(traceNode, identity, step, message, data),
                (plan, childContext, childCancellation) =>
                    RunChildWorkflowAsync(node, identity, plan, childContext, childCancellation));
            var operationKey = (token.TokenId, node.Id);
            NodeExecutionResult result;
            if (handler is IWorkflowNodeOperationFactory factory)
            {
                if (!_operations.TryGetValue(operationKey, out var operation))
                {
                    var operationId = Guid.NewGuid();
                    var instance = await factory.CreateOperationAsync(operationId, node, executionContext, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("操作工厂返回空实例。");
                    operation = new PendingOperation(operationId, instance);
                    _operations[operationKey] = operation;
                }
                cancellationToken.ThrowIfCancellationRequested();
                result = await operation.Instance.ExecuteAsync(executionContext, cancellationToken).ConfigureAwait(false);
            }
            else
                result = await handler.ExecuteAsync(node, executionContext, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is null)
                throw new InvalidOperationException($"节点 {node.Id} 返回了空执行结果。");

            if (!result.Success)
                throw new WorkflowPathException(
                    node.Id,
                    result.Message ?? "节点执行失败。",
                    executionIdentity: identity,
                    faultDisposition: result.FaultDisposition,
                    interruptAlarmCode: result.InterruptAlarmCode);

            commitStarted = true;
            // 成功输出须拥有独立生命周期；先清理操作，避免清理失败后仍公开本次输出。
            if (_operations.TryRemove(operationKey, out var completedOperation))
                await completedOperation.Instance.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            executionContext.CommitDataChanges();
            var committedOutput = Context.SetNodeOutput(node.Id, identity, result.Output);
            // 输出已正式写入运行状态，此时才发布派生投影（例如Vision预览）；
            // 取消、失败或提交失败的执行不会走到这里，界面因此看不到未被调度承认的结果。
            result.Projection?.Commit(committedOutput.ExecutionSequence);
            if (executionContext.ChangedVariableKeys.Count > 0)
                _committedVariableKeys[committedOutput.ExecutionSequence] = executionContext.ChangedVariableKeys;
            if (node is IWorkflowRecoveryEntryNode entry && token.ScopeIds.Count == 0)
                _recoveryEntries[entry.RecoveryEntryKey] = committedOutput;
            if (loopIteration.HasValue)
            {
                if (string.Equals(result.SelectedPortKey, WorkflowPorts.Loop, StringComparison.Ordinal))
                    token.CommitLoopIteration(node.Id, loopIteration.Value);
                else if (string.Equals(result.SelectedPortKey, WorkflowPorts.Completed, StringComparison.Ordinal)
                         || result.CompleteCurrentPath)
                    token.CompleteLoop(node.Id);
            }
            MarkNodeFinished(node.Id, token.TokenId, E_NodeState.Completed, null);
            SafeInvoke(NodeCompleted, node);
            WriteTrace(node, identity, "NodeCompleted", "节点执行完成。", null);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkNodeFinished(node.Id, token.TokenId, E_NodeState.Canceled, "节点执行已取消。");
            throw;
        }
        catch (Exception exception)
        {
            var pathException = exception as WorkflowPathException
                ?? new WorkflowPathException(node.Id, exception.Message, exception, identity,
                    WorkflowFaultDisposition.HandleAtScope);
            pathException.IsNodeFault = true;
            pathException.CommitStarted = commitStarted;
            var faultIdentity = pathException.ExecutionIdentity ?? identity;
            RunState.RecordFault(new WorkflowNodeFault(
                node.Id,
                faultIdentity,
                pathException.Message,
                pathException.InterruptAlarmCode,
                (exception.InnerException ?? exception).GetType().FullName,
                DateTimeOffset.UtcNow));
            MarkNodeFinished(node.Id, token.TokenId, E_NodeState.Failed, pathException.Message);
            WriteTrace(node, faultIdentity, "NodeFailed", pathException.Message, null);
            throw pathException;
        }
    }

    private int GetNextNodeExecutionCount(string nodeId)
    {
        lock (_stateSync)
        {
            _totalNodeExecutions++;
            if (_totalNodeExecutions > _options.MaxTotalNodeExecutions)
                throw new WorkflowPathException(nodeId, $"节点总执行次数超过安全上限 {_options.MaxTotalNodeExecutions}，流程可能存在无限循环。");
            var count = _nodeExecutionCounts.TryGetValue(nodeId, out var previous) ? previous + 1 : 1;
            _nodeExecutionCounts[nodeId] = count;
            if (count > _options.MaxNodeExecutions)
                throw new WorkflowPathException(nodeId, $"节点 {nodeId} 的执行次数超过安全上限 {_options.MaxNodeExecutions}，流程可能存在无限循环。");
            return count;
        }
    }

    private sealed class WorkflowPathException : Exception
    {
        public WorkflowPathException(
            string nodeId,
            string message,
            Exception? innerException = null,
            WorkflowExecutionIdentity? executionIdentity = null,
            WorkflowFaultDisposition faultDisposition = WorkflowFaultDisposition.StopRun,
            int interruptAlarmCode = 0)
            : base(message, innerException)
        {
            NodeId = nodeId;
            ExecutionIdentity = executionIdentity;
            FaultDisposition = faultDisposition;
            InterruptAlarmCode = interruptAlarmCode > 0 ? interruptAlarmCode : 0;
        }

        public bool IsBoundaryInterruption { get; set; }
        public bool IsNodeFault { get; set; }
        public bool CommitStarted { get; set; }
        public string NodeId { get; }
        public WorkflowExecutionIdentity? ExecutionIdentity { get; }
        public WorkflowFaultDisposition FaultDisposition { get; }
        public int InterruptAlarmCode { get; }
    }
}
