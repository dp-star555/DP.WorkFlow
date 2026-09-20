using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DP.WorkFlow;

/// <summary>
/// 执行已经编译的工作流定义，并发布 UI 无关的运行快照与 Trace。
/// 支持显式端口、循环、结构化并行、子流程、暂停和外部 Hold。
/// </summary>
public sealed partial class WorkflowEngine
{
    private readonly WorkflowExecutionPlan _plan;
    private readonly WorkflowBoundExecutionPlan _boundPlan;
    private readonly WorkflowExecutionOptions _options;
    private readonly AsyncManualResetEvent _resumeGate = new();
    private readonly object _stateSync = new();
    private readonly HashSet<string> _externalHoldReasons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeNodeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<long, WorkflowActiveTokenInfo> _activeTokens = new();
    private readonly Dictionary<string, MutableNodeRuntimeInfo> _nodeRuntime = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _nodeExecutionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<long, MutableParallelScopeInfo> _parallelScopes = new();
    private readonly Dictionary<string, WorkflowEngine> _activeChildEngines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowChildRuntimeInfo> _childWorkflows = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<WorkflowTraceEntry> _traceEntries = new();
    private Stopwatch? _runStopwatch;
    private Guid _runId;
    private string? _currentNodeId;
    private bool _manualPauseRequested;
    private int _isRunning;
    private int _totalNodeExecutions;
    private long _parallelScopeSequence;
    private long _nodeExecutionSequence;
    private long _tokenSequence;
    private long _snapshotSequence;
    private long _traceSequence;
    private E_WorkflowExecutionState _state = E_WorkflowExecutionState.Idle;

    /// <summary>获取当前或最近一次运行的独立状态模型。</summary>
    public WorkflowRunState RunState { get; private set; } = new();

    /// <summary>初始化一个可串行复用、但同一时刻只允许运行一次的工作流引擎。</summary>
    /// <param name="plan">已经通过结构和绑定校验的只读运行定义。</param>
    /// <param name="handlers">用于为每个节点解析唯一执行处理器的目录。</param>
    /// <param name="context">跨节点共享的变量、输出和服务上下文；为空时创建默认上下文。</param>
    /// <param name="options">执行次数、恢复次数和 Trace 容量安全上限。</param>
    public WorkflowEngine(
        WorkflowExecutionPlan plan,
        WorkflowNodeHandlerCatalog handlers,
        WorkflowContext? context = null,
        WorkflowExecutionOptions? options = null)
        : this(
            new WorkflowRuntimeBinder(handlers ?? throw new ArgumentNullException(nameof(handlers))).Bind(
                plan ?? throw new ArgumentNullException(nameof(plan))),
            context,
            options)
    {
    }

    /// <summary>Creates an engine from a plan whose handlers were already resolved during host configuration.</summary>
    /// <param name="boundPlan">Recursively handler-bound execution plan.</param>
    /// <param name="context">Run data and host capability context.</param>
    /// <param name="options">Execution safety limits.</param>
    public WorkflowEngine(
        WorkflowBoundExecutionPlan boundPlan,
        WorkflowContext? context = null,
        WorkflowExecutionOptions? options = null)
    {
        _boundPlan = boundPlan ?? throw new ArgumentNullException(nameof(boundPlan));
        _plan = boundPlan.Plan;
        Context = context ?? new WorkflowContext();
        _options = options ?? new WorkflowExecutionOptions();
        _options.Validate();
    }

    /// <summary>获取运行上下文。</summary>
    public WorkflowContext Context { get; }

    /// <summary>获取当前执行状态。</summary>
    public E_WorkflowExecutionState State
    {
        get
        {
            lock (_stateSync)
                return _state;
        }
    }

    /// <summary>获取当前外部 Hold 原因的快照。</summary>
    public IReadOnlyCollection<string> ExternalHoldReasons
    {
        get
        {
            lock (_stateSync)
                return _externalHoldReasons.ToArray();
        }
    }

    /// <summary>运行状态变化时发生。</summary>
    public event Action<E_WorkflowExecutionState>? StateChanged;

    /// <summary>节点开始执行时发生。</summary>
    public event Action<IWorkflowNodeModel>? NodeStarted;

    /// <summary>节点成功完成时发生。</summary>
    public event Action<IWorkflowNodeModel>? NodeCompleted;

    /// <summary>产生节点跟踪时发生。</summary>
    public event Action<WorkflowTraceEntry>? NodeTrace;

    /// <summary>运行快照变化时发生。</summary>
    public event Action<WorkflowRuntimeSnapshot>? SnapshotChanged;

    /// <summary>请求人工暂停；当前节点结束后不会继续调度下一节点。</summary>
    public void Pause()
    {
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            _manualPauseRequested = true;
            changedState = ApplyPauseStateLocked();
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.Pause();
        PublishStateChange(changedState);
    }

    /// <summary>清除人工暂停；若不存在外部 Hold，允许继续调度。</summary>
    public void Resume()
    {
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            _manualPauseRequested = false;
            changedState = ApplyPauseStateLocked();
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.Resume();
        PublishStateChange(changedState);
    }

    /// <summary>添加外部 Hold 原因，相同原因会自动去重并传播到活动子引擎。</summary>
    /// <param name="reason">设备、恢复协调器或宿主提供的非空原因；存储前裁剪首尾空白。</param>
    /// <exception cref="ArgumentException"><paramref name="reason"/> 为空或仅包含空白。</exception>
    public void AddExternalHold(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("外部 Hold 原因不能为空。", nameof(reason));

        var normalizedReason = reason.Trim();
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            _externalHoldReasons.Add(normalizedReason);
            changedState = ApplyPauseStateLocked();
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.AddExternalHold(normalizedReason);
        PublishStateChange(changedState);
    }

    /// <summary>移除外部 Hold；所有 Hold 和人工暂停均清除后才会继续运行。</summary>
    /// <param name="reason">要清除并传播到子引擎的原因；空白值被忽略。</param>
    public void RemoveExternalHold(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        var normalizedReason = reason.Trim();
        E_WorkflowExecutionState? changedState;
        WorkflowEngine[] children;
        lock (_stateSync)
        {
            _externalHoldReasons.Remove(normalizedReason);
            changedState = ApplyPauseStateLocked();
            children = _activeChildEngines.Values.ToArray();
        }
        foreach (var child in children)
            child.RemoveExternalHold(normalizedReason);
        PublishStateChange(changedState);
    }

    /// <summary>获取当前运行状态的深层集合快照。</summary>
    /// <param name="message">可选的快照触发原因或终态说明。</param>
    /// <returns>包含活动 Token、节点最近状态、并行进度和子流程状态的不可变快照。</returns>
    public WorkflowRuntimeSnapshot GetRuntimeSnapshot(string? message = null)
    {
        WorkflowRuntimeSnapshot snapshot;
        lock (_stateSync)
        {
            var now = DateTimeOffset.UtcNow;
            var nodes = new ReadOnlyDictionary<string, WorkflowNodeRuntimeInfo>(
                _nodeRuntime.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToSnapshot(now),
                    StringComparer.Ordinal));
            var scopes = new ReadOnlyDictionary<long, WorkflowParallelScopeInfo>(
                _parallelScopes.ToDictionary(pair => pair.Key, pair => pair.Value.ToSnapshot()));
            var tokens = new ReadOnlyDictionary<long, WorkflowActiveTokenInfo>(
                _activeTokens.ToDictionary(pair => pair.Key, pair => pair.Value));
            var children = new ReadOnlyDictionary<string, WorkflowChildRuntimeInfo>(
                _childWorkflows.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            snapshot = new WorkflowRuntimeSnapshot(
                _runId,
                Interlocked.Increment(ref _snapshotSequence),
                now,
                _plan.Name,
                _state,
                _currentNodeId,
                Array.AsReadOnly(_activeNodeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()),
                tokens,
                _runStopwatch?.Elapsed ?? TimeSpan.Zero,
                nodes,
                scopes,
                children,
                Array.AsReadOnly(_externalHoldReasons.ToArray()),
                message,
                RunState.Faults,
                RunState.NodeOutputs);
        }
        RunState.Publish(snapshot);
        return snapshot;
    }

    /// <summary>获取内存中当前保留的不可变 Trace 批次。</summary>
    /// <returns>受 <see cref="WorkflowExecutionOptions.MaxTraceEntries"/> 限制的最近条目及最后序号。</returns>
    public WorkflowTraceBatch GetTraceBatch()
    {
        var entries = _traceEntries.ToArray();
        return new WorkflowTraceBatch(
            _runId,
            entries.LastOrDefault()?.Sequence ?? 0,
            Array.AsReadOnly(entries));
    }

    /// <summary>从定义入口创建根 Token 并执行工作流直至完成、故障或取消。</summary>
    /// <param name="cancellationToken">控制节点执行、暂停等待、并行分支和故障恢复的取消令牌。</param>
    /// <returns>终态、耗时、消息和可定位的失败节点；执行故障通常被转换为结果而不是向外抛出。</returns>
    /// <exception cref="InvalidOperationException">同一引擎实例已有尚未结束的运行。</exception>
    public async Task<WorkflowRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) != 0)
            throw new InvalidOperationException("当前 WorkflowEngine 已经处于运行中。");

        BeginRun(_plan.EntryNodeId);
        try
        {
            var rootToken = new ExecutionToken(
                Interlocked.Increment(ref _tokenSequence),
                Array.Empty<long>(),
                Array.Empty<long>());
            var nextEntryNodeId = _plan.EntryNodeId;
            _recoveryAttempt = 0;
            while (true)
            {
                try
                {
                    await ExecutePathAsync(nextEntryNodeId, null, rootToken, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (WorkflowPathException exception) when (
                    (exception.FaultDisposition == WorkflowFaultDisposition.RequestRecovery
                     || (exception.IsNodeFault && exception.FaultDisposition == WorkflowFaultDisposition.HandleAtScope))
                    && _options.MaxRecoveryAttempts > 0
                    && (_options.RecoveryCoordinator ?? Context.Services.GetService(typeof(IWorkflowFaultRecoveryCoordinator))) is IWorkflowFaultRecoveryCoordinator)
                {
                    nextEntryNodeId = await RecoverPathAsync(exception, rootToken, cancellationToken).ConfigureAwait(false);
                }
            }
            SetTerminalState(E_WorkflowExecutionState.Completed, "流程执行完成。");
            return new WorkflowRunResult(true, State, Elapsed, "流程执行完成。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetTerminalState(E_WorkflowExecutionState.Canceled, "流程已取消。");
            return new WorkflowRunResult(false, State, Elapsed, "流程已取消。", GetCurrentNodeId());
        }
        catch (WorkflowPathException exception)
        {
            SetTerminalState(E_WorkflowExecutionState.Faulted, exception.Message);
            return new WorkflowRunResult(false, State, Elapsed, exception.Message, exception.NodeId);
        }
        catch (Exception exception)
        {
            SetTerminalState(E_WorkflowExecutionState.Faulted, exception.Message);
            return new WorkflowRunResult(false, State, Elapsed, exception.Message, GetCurrentNodeId());
        }
        finally
        {
            await DisposePendingOperationsAsync().ConfigureAwait(false);
            _recoveryEntries.Clear();
            _committedVariableKeys.Clear();
            lock (_stateSync)
            {
                _runStopwatch?.Stop();
                _currentNodeId = null;
                _activeNodeIds.Clear();
                _activeTokens.Clear();
            }
            PublishSnapshot();
            Volatile.Write(ref _isRunning, 0);
        }
    }

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

    private Task<WorkflowRunResult> RunChildWorkflowAsync(
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        WorkflowExecutionPlan childDefinition,
        WorkflowContext childContext,
        CancellationToken cancellationToken)
    {
        return RunTrackedChildAsync(parentNode, parentExecution, _boundPlan.GetChildPlan(parentNode.Id),
            childContext, _options, null, false, cancellationToken);
    }

    private async Task<WorkflowRunResult> RunTrackedChildAsync(
        IWorkflowNodeModel parentNode, WorkflowExecutionIdentity parentExecution,
        WorkflowBoundExecutionPlan boundPlan, WorkflowContext childContext,
        WorkflowExecutionOptions options, string? excludedHold, bool prepare,
        CancellationToken cancellationToken)
    {
        var key = $"{parentNode.Id}:{parentExecution.TokenId}:{parentExecution.NodeExecutionCount}"
            + (excludedHold is null ? string.Empty : ":Recovery");
        var childEngine = new WorkflowEngine(boundPlan, childContext, options);
        void UpdateChildSnapshot(WorkflowRuntimeSnapshot snapshot)
        {
            lock (_stateSync)
                _childWorkflows[key] = new WorkflowChildRuntimeInfo(parentNode.Id, parentExecution, snapshot);
            PublishSnapshot();
        }

        bool isPaused;
        string[] holdReasons;
        lock (_stateSync)
        {
            _activeChildEngines.Add(key, childEngine);
            isPaused = _manualPauseRequested;
            holdReasons = _externalHoldReasons.Where(reason => !string.Equals(reason, excludedHold, StringComparison.Ordinal)).ToArray();
        }
        childEngine.SnapshotChanged += UpdateChildSnapshot;
        if (isPaused)
            childEngine.Pause();
        foreach (var holdReason in holdReasons)
            childEngine.AddExternalHold(holdReason);

        try
        {
            if (prepare)
            {
                WorkflowRuntimeCapabilityValidator.Validate(boundPlan, childContext.Services);
                if (childContext.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
                    await preparation.PrepareAsync(
                        new WorkflowRunPreparationContext(
                            EnumerateRecoveryNodes(boundPlan.Plan).ToArray(),
                            WorkflowRunScopeKind.Nested,
                            parentNode.Id),
                        cancellationToken).ConfigureAwait(false);
            }
            var result = await childEngine.RunAsync(cancellationToken).ConfigureAwait(false);
            UpdateChildSnapshot(childEngine.GetRuntimeSnapshot(result.Message));
            return result;
        }
        finally
        {
            childEngine.SnapshotChanged -= UpdateChildSnapshot;
            lock (_stateSync)
                _activeChildEngines.Remove(key);
        }
    }

    /// <summary>为静态并行定义创建运行时 Scope 和分支 Token，并等待全部分支到达汇聚点。</summary>
    /// <param name="scopeNode">触发派发的 ParallelAll 节点。</param>
    /// <param name="plan">编译期识别的分支入口和共同汇聚边界。</param>
    /// <param name="parentToken">拥有该并行作用域的父执行路径。</param>
    /// <param name="cancellationToken">父路径取消令牌；任一分支失败时也会取消其他分支。</param>
    private async Task ExecuteParallelScopeAsync(
        IWorkflowNodeModel scopeNode,
        WorkflowParallelScopePlan plan,
        ExecutionToken parentToken,
        CancellationToken cancellationToken)
    {
        var runtimeScopeId = Interlocked.Increment(ref _parallelScopeSequence);
        lock (_stateSync)
        {
            _parallelScopes[runtimeScopeId] = new MutableParallelScopeInfo
            {
                RuntimeScopeId = runtimeScopeId,
                ScopeNodeId = scopeNode.Id,
                MergeNodeId = plan.MergeNodeId,
                TotalBranches = plan.BranchEntryNodeIds.Count
            };
        }
        Context.RegisterParallelScope(runtimeScopeId, parentToken.TokenId);
        PublishSnapshot();

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? firstException = null;
        var exceptionSync = new object();
        var tasks = plan.BranchEntryNodeIds.Select(async branchEntry =>
        {
            var ancestorTokenIds = new[] { parentToken.TokenId }
                .Concat(parentToken.AncestorTokenIds)
                .ToArray();
            var childToken = new ExecutionToken(
                Interlocked.Increment(ref _tokenSequence),
                Array.AsReadOnly(ancestorTokenIds),
                Array.AsReadOnly(parentToken.ScopeIds.Concat(new[] { runtimeScopeId }).ToArray()),
                parentToken.CopyLoopFrames());
            try
            {
                await ExecutePathAsync(
                    branchEntry,
                    plan.MergeNodeId,
                    childToken,
                    linkedCancellation.Token).ConfigureAwait(false);
                lock (_stateSync)
                    _parallelScopes[runtimeScopeId].CompletedBranches++;
                PublishSnapshot();
            }
            catch (Exception exception)
            {
                lock (exceptionSync)
                    firstException ??= exception;
                linkedCancellation.Cancel();
                throw;
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            if (firstException is not null)
                throw firstException;
            throw;
        }

        lock (_stateSync)
            _parallelScopes[runtimeScopeId].IsCompleted = true;
        Context.CompleteParallelScope(runtimeScopeId);
        var parentIdentity = new WorkflowExecutionIdentity(
            _runId,
            parentToken.TokenId,
            parentToken.AncestorTokenIds,
            parentToken.ScopeIds,
            0);
        WriteTrace(scopeNode, parentIdentity, "ParallelMerged", $"{plan.BranchEntryNodeIds.Count} 个分支全部到达汇聚节点 {plan.MergeNodeId}。", null);
        PublishSnapshot();
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

    private string? GetCurrentNodeId()
    {
        lock (_stateSync)
            return _currentNodeId;
    }

    private TimeSpan Elapsed
    {
        get
        {
            lock (_stateSync)
                return _runStopwatch?.Elapsed ?? TimeSpan.Zero;
        }
    }

    private void BeginRun(string? startNodeId)
    {
        E_WorkflowExecutionState? changedState;
        lock (_stateSync)
        {
            _runId = Guid.NewGuid();
            RunState = new WorkflowRunState();
            RunState.Begin(_runId);
            _currentNodeId = startNodeId;
            _activeNodeIds.Clear();
            _activeTokens.Clear();
            _nodeRuntime.Clear();
            _nodeExecutionCounts.Clear();
            _nodeExecutionSequence = 0;
            _parallelScopes.Clear();
            _activeChildEngines.Clear();
            _childWorkflows.Clear();
            _totalNodeExecutions = 0;
            _tokenSequence = 0;
            while (_traceEntries.TryDequeue(out _))
            {
            }
            _runStopwatch = Stopwatch.StartNew();
            _manualPauseRequested = false;
            _state = E_WorkflowExecutionState.Running;
            Context.BeginRun(RunState);
            changedState = ApplyPauseStateLocked() ?? _state;
        }
        PublishStateChange(changedState);
    }

    private E_WorkflowExecutionState? ApplyPauseStateLocked()
    {
        var shouldPause = _manualPauseRequested || _externalHoldReasons.Count > 0;
        if (shouldPause)
            _resumeGate.Reset();
        else
            _resumeGate.Set();

        if (_state is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused))
            return null;
        var desiredState = shouldPause ? E_WorkflowExecutionState.Paused : E_WorkflowExecutionState.Running;
        if (_state == desiredState)
            return null;
        _state = desiredState;
        return desiredState;
    }

    private void MarkNodeStarted(IWorkflowNodeModel node, WorkflowExecutionIdentity identity)
    {
        lock (_stateSync)
        {
            _currentNodeId = node.Id;
            _activeNodeIds.Add(node.Id);
            _activeTokens[identity.TokenId] = new WorkflowActiveTokenInfo(
                identity.TokenId,
                identity.AncestorTokenIds,
                identity.ScopeIds,
                node.Id);
            _nodeRuntime[node.Id] = new MutableNodeRuntimeInfo
            {
                NodeId = node.Id,
                State = E_NodeState.Running,
                ExecutionCount = identity.NodeExecutionCount,
                ExecutionSequence = ++_nodeExecutionSequence,
                StartedAt = DateTimeOffset.UtcNow
            };
        }
        PublishSnapshot();
    }

    private void BeginNodeTiming(string nodeId)
    {
        lock (_stateSync)
            if (_nodeRuntime.TryGetValue(nodeId, out var info))
                info.ExecutionStartedTimestamp = Stopwatch.GetTimestamp();
    }

    private void MarkNodeFinished(string nodeId, long tokenId, E_NodeState state, string? message)
    {
        lock (_stateSync)
        {
            if (_nodeRuntime.TryGetValue(nodeId, out var info))
            {
                info.State = state;
                info.CompletedAt = DateTimeOffset.UtcNow;
                info.Elapsed = info.ExecutionStartedTimestamp > 0
                    ? Stopwatch.GetElapsedTime(info.ExecutionStartedTimestamp)
                    : info.CompletedAt.Value - info.StartedAt.GetValueOrDefault(info.CompletedAt.Value);
                info.Message = message;
            }
            _activeTokens.Remove(tokenId);
            _activeNodeIds.Clear();
            foreach (var activeNodeId in _activeTokens.Values
                         .Select(token => token.CurrentNodeId)
                         .Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                _activeNodeIds.Add(activeNodeId!);
            }
            _currentNodeId = _activeNodeIds.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        }
        PublishSnapshot(message);
    }

    private void SetTerminalState(E_WorkflowExecutionState state, string? message)
    {
        lock (_stateSync)
            _state = state;
        SafeInvoke(StateChanged, state);
        PublishSnapshot(message);
    }

    private void PublishStateChange(E_WorkflowExecutionState? changedState)
    {
        if (!changedState.HasValue)
            return;
        SafeInvoke(StateChanged, changedState.Value);
        PublishSnapshot();
    }

    private void PublishSnapshot(string? message = null) =>
        SafeInvoke(SnapshotChanged, GetRuntimeSnapshot(message));

    private void WriteTrace(
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        string step,
        string? message,
        IReadOnlyDictionary<string, object?>? data)
    {
        var entry = new WorkflowTraceEntry(
            Interlocked.Increment(ref _traceSequence),
            DateTimeOffset.UtcNow,
            node.Id,
            node.NodeType,
            step,
            message,
            data is null
                ? null
                : new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(data, StringComparer.Ordinal)),
            identity.TokenId,
            Array.AsReadOnly(identity.ScopeIds.ToArray()));
        _traceEntries.Enqueue(entry);
        while (_traceEntries.Count > _options.MaxTraceEntries)
            _traceEntries.TryDequeue(out _);
        SafeInvoke(NodeTrace, entry);
    }

    private static void SafeInvoke<T>(Action<T>? handlers, T argument)
    {
        if (handlers is null)
            return;
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(argument);
            }
            catch
            {
                // 观察者异常不得改变流程执行结果；宿主可在自己的事件处理器中记录异常。
            }
        }
    }

    private sealed class ExecutionToken
    {
        private readonly Dictionary<string, int> _loopFrames;

        public ExecutionToken(
            long tokenId,
            IReadOnlyList<long> ancestorTokenIds,
            IReadOnlyList<long> scopeIds,
            IReadOnlyDictionary<string, int>? loopFrames = null)
        {
            TokenId = tokenId;
            AncestorTokenIds = ancestorTokenIds;
            ScopeIds = scopeIds;
            _loopFrames = loopFrames is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(loopFrames, StringComparer.Ordinal);
        }

        public long TokenId { get; }

        public IReadOnlyList<long> AncestorTokenIds { get; }

        public IReadOnlyList<long> ScopeIds { get; }

        public int PrepareLoopIteration(string nodeId) =>
            _loopFrames.TryGetValue(nodeId, out var completedIterations)
                ? completedIterations + 1
                : 1;

        public void CommitLoopIteration(string nodeId, int iteration) =>
            _loopFrames[nodeId] = iteration;

        public void CompleteLoop(string nodeId) => _loopFrames.Remove(nodeId);

        public IReadOnlyDictionary<string, int> CopyLoopFrames() =>
            new Dictionary<string, int>(_loopFrames, StringComparer.Ordinal);
    }

    private sealed class MutableParallelScopeInfo
    {
        public long RuntimeScopeId { get; init; }

        public required string ScopeNodeId { get; init; }

        public required string MergeNodeId { get; init; }

        public int TotalBranches { get; init; }

        public int CompletedBranches { get; set; }

        public bool IsCompleted { get; set; }

        public WorkflowParallelScopeInfo ToSnapshot() => new(
            RuntimeScopeId,
            ScopeNodeId,
            MergeNodeId,
            TotalBranches,
            CompletedBranches,
            IsCompleted);
    }

    private sealed class MutableNodeRuntimeInfo
    {
        public required string NodeId { get; init; }

        public E_NodeState State { get; set; }

        public int ExecutionCount { get; init; }

        public long ExecutionSequence { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; set; }

        public long ExecutionStartedTimestamp { get; set; }

        public TimeSpan Elapsed { get; set; }

        public string? Message { get; set; }

        public WorkflowNodeRuntimeInfo ToSnapshot(DateTimeOffset now)
        {
            var elapsed = State == E_NodeState.Running && StartedAt.HasValue
                ? now - StartedAt.Value
                : Elapsed;
            return new WorkflowNodeRuntimeInfo(
                NodeId,
                State,
                ExecutionCount,
                ExecutionSequence,
                StartedAt,
                CompletedAt,
                elapsed,
                Message);
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
