using System.Collections.Concurrent;

namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    // 仍保留在运行历史中的输出所登记的投影；输出退出历史时由投影释放派生资源。
    private readonly ConcurrentDictionary<long, (string NodeId, IWorkflowNodeOutputProjection Projection)> _outputProjections = new();
    // 子流程节点本次执行的子运行剩余投影；节点输出提交后挂到该输出上，随它被取代而释放。
    private readonly ConcurrentDictionary<(long TokenId, string NodeId), IReadOnlyList<IWorkflowNodeOutputProjection>> _childOutputProjections = new();

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
            NodeExecutionResult result;
            try
            {
                result = await ExecuteNodeAsync(node, token, cancellationToken).ConfigureAwait(false);
            }
            catch (WorkflowPathException fault) when (fault.IsNodeFault && !fault.IsBoundaryInterruption
                && _plan.GetNextNodeIds(node.Id, WorkflowPorts.Failed).Count > 0)
            {
                // 失败出口已连线：故障已记录（节点标记失败、写入运行事件），本次运行不中止，沿失败支路继续（例如视觉检测NG）。
                RouteFaultToFailedPort(node, token, fault);
                result = NodeExecutionResult.Continue(WorkflowPorts.Failed);
            }
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
        Notify(NodeStarted, node);
        RecordRunEvent(WorkflowRunEventDraft.Node("NodeStarted", node, identity, "节点开始执行。"));
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
                _boundPlan.GetInputLayout(node.Id),
                (traceNode, step, message, data, writeMode) => WriteTrace(traceNode, identity, step, message, data, writeMode),
                (draft, writeMode) => RecordRunEvent(draft, writeMode),
                message => ReportRecordingDegraded(message, node.Id),
                (plan, childContext, childCancellation) =>
                    RunChildWorkflowAsync(node, identity, plan, childContext, childCancellation), PlanPath, BindingScopeId,
                childContext => OpenChildWorkflowSession(node, identity, childContext),
                (category, message) => RaiseNotice(node.Id, category, message, nodeType: node.NodeType));
            // R2-c：进入执行代码前取齐全部普通输入；任何一个取不到，节点直接失败，不会执行到一半才发现。
            executionContext.ResolveAllInputs();
            var operationKey = (token.TokenId, node.Id);
            // 上一次执行失败时留下的子运行投影不属于本次输出。
            _childOutputProjections.TryRemove(operationKey, out _);
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
            RecordDataChangesCommitted(node, identity, executionContext);
            var committedOutput = Context.SetNodeOutput(node.Id, identity, result.Output, out var superseded);
            RecordOutputCommitted(node, identity, committedOutput);
            // 输出已正式写入运行状态，此时才发布派生投影（例如Vision预览）；
            // 取消、失败或提交失败的执行不会走到这里，界面因此看不到未被调度承认的结果。
            result.Projection?.Commit(committedOutput.ExecutionSequence);
            if (ComposeOutputProjection(result.Projection, operationKey) is { } outputProjection)
                _outputProjections[committedOutput.ExecutionSequence] = (node.Id, outputProjection);
            // 新输出已发布，被它取代的上一次输出不会再被读到，释放其资源（环形流程的内存因此不随循环次数增长）。
            RetireOutputs(superseded);
            // 变量键只供命名入口重执行回退；该能力限于无环串行计划，其他计划不记录，避免循环中无限累计。
            if (executionContext.ChangedVariableKeys.Count > 0 && _plan.IsAcyclic && _plan.ParallelScopes.Count == 0)
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
            Notify(NodeCompleted, node);
            RecordRunEvent(WorkflowRunEventDraft.Node(
                "NodeCompleted",
                node,
                identity,
                "节点执行完成。",
                new Dictionary<string, object?>
                {
                    ["Elapsed"] = GetNodeElapsed(node.Id).TotalMilliseconds,
                    ["SelectedPort"] = result.SelectedPortKey ?? WorkflowPorts.Success
                }));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkNodeFinished(node.Id, token.TokenId, E_NodeState.Canceled, "节点执行已取消。");
            RecordRunEvent(WorkflowRunEventDraft.Node("NodeCanceled", node, identity, "节点执行已取消。"));
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
            var fault = new WorkflowNodeFault(
                node.Id,
                faultIdentity,
                pathException.Message,
                pathException.InterruptAlarmCode,
                (exception.InnerException ?? exception).GetType().FullName,
                WorkflowClock.UtcNow);
            RunState.RecordFault(fault);
            lock (_stateSync)
                _currentFault = fault;
            MarkNodeFinished(node.Id, token.TokenId, E_NodeState.Failed, pathException.Message);
            TryRecordRunEvent(WorkflowRunEventDraft.Fault(
                "NodeFailed",
                node,
                faultIdentity,
                pathException.Message,
                data: new Dictionary<string, object?>
                {
                    ["ExceptionType"] = fault.ExceptionType,
                    ["InterruptAlarmCode"] = fault.InterruptAlarmCode,
                    ["CommitStarted"] = commitStarted
                }));
            throw pathException;
        }
    }

    /// <summary>把已记录的节点故障转为失败出口：清除当前故障，记录一条“故障已由失败支路处理”的运行事件。</summary>
    private void RouteFaultToFailedPort(IWorkflowNodeModel node, ExecutionToken token, WorkflowPathException fault)
    {
        lock (_stateSync)
            _currentFault = null;
        var identity = fault.ExecutionIdentity
            ?? new WorkflowExecutionIdentity(_runId, token.TokenId, token.AncestorTokenIds, token.ScopeIds, 0);
        var message = $"节点失败，沿失败出口继续：{fault.Message}";
        TryRecordRunEvent(WorkflowRunEventDraft.Node(
            "NodeFaultRouted",
            node,
            identity,
            message,
            new Dictionary<string, object?> { ["SelectedPort"] = WorkflowPorts.Failed, ["Message"] = fault.Message }));
    }

    /// <summary>合并节点自身投影与本次执行中子运行剩余的投影；两者都没有时为空。</summary>
    private IWorkflowNodeOutputProjection? ComposeOutputProjection(
        IWorkflowNodeOutputProjection? own,
        (long TokenId, string NodeId) operationKey)
    {
        if (!_childOutputProjections.TryRemove(operationKey, out var children) || children.Count == 0)
            return own;
        return new CompositeOutputProjection(own is null ? children : children.Prepend(own).ToArray());
    }

    /// <summary>
    /// 移交本次运行仍保留的输出投影并清空登记；父引擎据此在子流程节点输出被取代时释放子运行资源。
    /// 给出 <paramref name="keepNodeIds"/> 时只移交这些节点的投影（R2-e：被子流程输出映射引用、要往外交的资源），
    /// 其余在此立即归还。
    /// </summary>
    /// <param name="keepNodeIds">要移交的节点；为空时全部移交。</param>
    internal IReadOnlyList<IWorkflowNodeOutputProjection> DetachOutputProjections(IReadOnlyCollection<string>? keepNodeIds = null)
    {
        var entries = _outputProjections.Values.ToArray();
        _outputProjections.Clear();
        if (keepNodeIds is null)
            return entries.Select(entry => entry.Projection).ToArray();
        var kept = new List<IWorkflowNodeOutputProjection>();
        foreach (var entry in entries)
        {
            if (keepNodeIds.Contains(entry.NodeId))
                kept.Add(entry.Projection);
            else
                RetireProjection(entry.NodeId, entry.Projection);
        }
        return kept;
    }

    /// <summary>子流程输出映射引用的子画布节点；不是带接口的子流程节点时为空（全部移交）。</summary>
    /// <param name="parentNode">拥有子画布的父节点。</param>
    internal static IReadOnlyCollection<string>? GetReferencedChildOutputNodes(IWorkflowNodeModel parentNode) =>
        parentNode is IWorkflowSubflowNode subflow
            ? new HashSet<string>(subflow.Outputs.Where(output => output.Source is not null).Select(output => output.Source!.Value.NodeId), StringComparer.Ordinal)
            : null;

    /// <summary>接管子运行剩余投影：子流程成功时挂到本次节点输出上，失败时没有输出可引用，立即释放。</summary>
    private void AdoptChildOutputProjections(
        string parentNodeId,
        long parentTokenId,
        IReadOnlyList<IWorkflowNodeOutputProjection> projections,
        bool succeeded)
    {
        if (projections.Count == 0)
            return;
        if (succeeded)
        {
            _childOutputProjections.AddOrUpdate((parentTokenId, parentNodeId), projections,
                (_, existing) => existing.Concat(projections).ToArray());
            return;
        }
        RetireChildOutputProjections(parentNodeId, projections);
    }

    /// <summary>立即释放子运行剩余投影；它们的输出不会再被读取。</summary>
    private void RetireChildOutputProjections(string parentNodeId, IReadOnlyList<IWorkflowNodeOutputProjection> projections)
    {
        if (projections.Count > 0)
            RetireProjection(parentNodeId, new CompositeOutputProjection(projections));
    }

    /// <summary>输出退出运行历史后通知其投影释放派生资源；释放失败只降级记录，不影响运行。</summary>
    /// <param name="outputs">已退出历史的输出。</param>
    private void RetireOutputs(IReadOnlyList<WorkflowNodeOutput> outputs)
    {
        foreach (var output in outputs)
        {
            if (_outputProjections.TryRemove(output.ExecutionSequence, out var entry))
                RetireProjection(output.NodeId, entry.Projection);
        }
    }

    private void RetireProjection(string nodeId, IWorkflowNodeOutputProjection projection)
    {
        try
        {
            projection.Retire();
        }
        catch (Exception exception)
        {
            ReportRecordingDegraded($"释放节点 {nodeId} 被取代输出的资源失败：{exception.Message}", nodeId);
        }
    }

    /// <summary>子流程节点输出携带的子运行投影集合；发布已在子运行中完成，这里只负责一起释放。</summary>
    private sealed class CompositeOutputProjection(IReadOnlyList<IWorkflowNodeOutputProjection> parts) : IWorkflowNodeOutputProjection
    {
        public void Commit(long executionSequence)
        {
        }

        public void Retire()
        {
            List<Exception>? failures = null;
            foreach (var part in parts)
            {
                try { part.Retire(); }
                catch (Exception exception) { (failures ??= new List<Exception>()).Add(exception); }
            }
            if (failures is not null)
                throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        }
    }

    private TimeSpan GetNodeElapsed(string nodeId)
    {
        lock (_stateSync)
            return _nodeRuntime.TryGetValue(nodeId, out var info) ? info.Elapsed : TimeSpan.Zero;
    }

    private void RecordDataChangesCommitted(
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        WorkflowNodeExecutionContext executionContext)
    {
        var variableKeys = executionContext.ChangedVariableKeys;
        if (variableKeys.Count > 0)
            RecordRunEvent(new WorkflowRunEventDraft(
                WorkflowRunEventCategory.DataFlow,
                "VariableChangesCommitted",
                NodeId: node.Id,
                NodeType: node.NodeType,
                ExecutionIdentity: identity,
                Data: new Dictionary<string, object?> { ["Keys"] = variableKeys }));
    }

    private void RecordOutputCommitted(
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        WorkflowNodeOutput output)
    {
        // 输出键由提交值自动提取，Handler 不再手写；图像和二进制由编码器转换为引用，不写入完整内容。
        var extracted = _outputValueExtractor.Extract(output.Value);
        foreach (var diagnostic in extracted.Diagnostics)
            ReportRecordingDegraded(diagnostic, node.Id);

        // 输出值展开到 Payload 顶层稳定键；不能作为普通字典值交给编码器，否则会被压缩成"n 项"摘要。
        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["OutputExecutionSequence"] = output.ExecutionSequence,
            ["OutputType"] = extracted.OutputType,
            ["OutputKeys"] = extracted.Values.Keys.ToArray()
        };
        foreach (var pair in extracted.Values)
            data["OutputValue." + pair.Key] = pair.Value;

        RecordRunEvent(new WorkflowRunEventDraft(
            WorkflowRunEventCategory.DataFlow,
            "OutputCommitted",
            NodeId: node.Id,
            NodeType: node.NodeType,
            ExecutionIdentity: identity,
            Data: data));
    }

    /// <summary>
    /// 节点本次运行的执行序号。不限制执行次数（循环可以不限次数长期运行）；
    /// 运行数据不随执行次数增长：每个节点只保留最近输出、记录只保留最近窗口，计数只是每个节点一个整数。
    /// </summary>
    private int GetNextNodeExecutionCount(string nodeId)
    {
        lock (_stateSync)
        {
            var count = _nodeExecutionCounts.TryGetValue(nodeId, out var previous) ? previous + 1 : 1;
            _nodeExecutionCounts[nodeId] = count;
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
