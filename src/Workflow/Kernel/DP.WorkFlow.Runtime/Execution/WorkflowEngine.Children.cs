namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    /// <summary>
    /// 运行一次子画布（子流程块）：在同一运行内开一个画布作用域（R3-a），结束时关闭作用域并按输出映射交出结果。
    /// </summary>
    private Task<WorkflowRunResult> RunChildWorkflowAsync(
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        ExecutionToken parentToken,
        WorkflowContext childContext,
        CancellationToken cancellationToken) =>
        RunCanvasScopeAsync(parentNode, parentExecution, parentToken, childContext, cancellationToken);

    private async Task<WorkflowRunResult> RunCanvasScopeAsync(
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        ExecutionToken parentToken,
        WorkflowContext childContext,
        CancellationToken cancellationToken)
    {
        var key = ChildKey(parentNode, parentExecution);
        var scope = CreateCanvasScope(parentNode, parentToken, childContext, cycle: 0);
        RegisterActiveScope(key, parentNode, parentExecution, scope, static () => 0);
        try
        {
            var result = await scope.RunAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateSync)
            {
                if (_activeChildWorkflows.TryGetValue(key, out var active))
                    active.LastMessage = result.Message;
            }
            // 子画布的映射输出可能引用其节点输出（例如图像帧），资源随父节点本次输出的生命周期释放（R2-e）。
            AdoptChildOutputProjections(parentNode.Id, parentExecution.TokenId,
                scope.DetachOutputProjections(result.Success ? GetReferencedChildOutputNodes(parentNode) : null), result.Success);
            return result;
        }
        finally
        {
            RetireActiveChild(key, parentNode.Id, scope);
        }
    }

    /// <summary>为循环块打开可反复运行同一子画布的会话：作用域对象只创建一次，每圈开一次画布作用域。</summary>
    /// <param name="parentNode">拥有子画布的父节点。</param>
    /// <param name="parentExecution">父节点本次执行身份。</param>
    /// <param name="parentToken">父画布上执行循环块的令牌。</param>
    /// <param name="childContext">整个会话期间复用的子上下文。</param>
    /// <returns>调用方负责释放的会话。</returns>
    private IWorkflowChildWorkflowSession OpenChildWorkflowSession(
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        ExecutionToken parentToken,
        WorkflowContext childContext)
    {
        var key = ChildKey(parentNode, parentExecution);
        var scope = CreateCanvasScope(parentNode, parentToken, childContext, cycle: 0);
        var session = new ChildWorkflowSession(this, parentNode, parentExecution, key, scope);
        RegisterActiveScope(key, parentNode, parentExecution, scope, session.CurrentCycle);
        return session;
    }

    private static string ChildKey(IWorkflowNodeModel parentNode, WorkflowExecutionIdentity parentExecution) =>
        $"{parentNode.Id}:{parentExecution.TokenId}:{parentExecution.NodeExecutionCount}";

    /// <summary>创建子画布作用域：本画布的计划、上下文和节点状态独立，运行身份、记录、暂停与令牌编号用根运行的。</summary>
    private WorkflowEngine CreateCanvasScope(IWorkflowNodeModel parentNode, ExecutionToken parentToken, WorkflowContext childContext, int cycle)
    {
        var scope = new WorkflowEngine(_boundPlan.GetChildPlan(parentNode.Id), childContext, _options)
        {
            PlanPath = WorkflowRunPreparationPlanner.ChildPath(PlanPath, parentNode.Id),
            BindingScopeId = BindingScopeId
        };
        scope._scopeRoot = Root;
        scope._enclosingToken = parentToken;
        scope._scopeParents = _scopeParents.Append(new WorkflowTraceParent(parentNode.Id, cycle)).ToArray();
        return scope;
    }

    private void RegisterActiveScope(
        string key,
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        WorkflowEngine scope,
        Func<int> cycle)
    {
        lock (_stateSync)
            _activeChildWorkflows[key] = new ActiveChildWorkflow(key, parentNode.Id, parentExecution, scope, cycle);
        Volatile.Write(ref scope._changeOwner, this);
        PublishSnapshot();
    }

    /// <summary>
    /// 一个正在运行的子画布（或独立的处理流程）的登记项。父快照需要它时才取快照（R1-04），不逐次推送。
    /// </summary>
    private sealed class ActiveChildWorkflow(
        string key,
        string parentNodeId,
        WorkflowExecutionIdentity parentExecution,
        WorkflowEngine engine,
        Func<int> cycle)
    {
        public string Key { get; } = key;
        public WorkflowEngine Engine { get; } = engine;

        /// <summary>子运行结束后最后一次的说明（成功、失败原因）；运行中为空。</summary>
        public string? LastMessage { get; set; }

        public WorkflowChildRuntimeInfo Capture() =>
            new(parentNodeId, parentExecution, Engine.GetRuntimeSnapshot(LastMessage), cycle());
    }

    /// <summary>
    /// 查找本运行或其子画布（正在运行的，或每个父节点最近一次完成的）中节点的最近有效输出。
    /// 子画布按自身子快照的运行标识查询，因此循环块、子流程内部节点的结果也能在界面上实时查看。
    /// </summary>
    /// <param name="runId">节点所属运行（或子画布作用域）的标识。</param>
    /// <param name="nodeId">节点 ID。</param>
    /// <returns>最近一次未失效的输出；该运行已不在本引擎可见范围内时为空。</returns>
    public WorkflowNodeOutput? FindLatestNodeOutput(Guid runId, string nodeId)
    {
        Guard.NotNullOrWhiteSpace(nodeId);
        if (runId == RunState.RunId)
            return RunState.TryGetLatestNodeOutput(nodeId, out var output) ? output : null;
        WorkflowEngine[] children;
        lock (_stateSync)
            children = _activeChildWorkflows.Values.Select(child => child.Engine)
                .Concat(_latestChildEngineByParentNode.Values).Distinct().ToArray();
        foreach (var child in children)
        {
            if (child.FindLatestNodeOutput(runId, nodeId) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>循环块的子画布会话；每圈开一次画布作用域，释放时结束登记。</summary>
    private sealed class ChildWorkflowSession(
        WorkflowEngine owner,
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        string key,
        WorkflowEngine scope) : IWorkflowChildWorkflowSession
    {
        private bool _keepLastCycleOutputs;
        private bool _disposed;
        private int _cycle;

        internal Func<int> CurrentCycle => () => Volatile.Read(ref _cycle);

        public async Task<WorkflowRunResult> RunCycleAsync(CancellationToken cancellationToken)
        {
            Guard.NotDisposed(_disposed, this);
            // 圈与圈之间不传递数据，上一圈剩余输出不会再被读取，开始新一圈前释放其资源。
            owner.RetireChildOutputProjections(parentNode.Id, scope.DetachOutputProjections());
            _keepLastCycleOutputs = false;
            var cycle = Interlocked.Increment(ref _cycle);
            // 本圈的事件标明圈序号：位置写作"循环块@第 n 圈 / 节点"。
            var parents = scope._scopeParents.ToArray();
            parents[^1] = new WorkflowTraceParent(parentNode.Id, cycle);
            scope._scopeParents = parents;
            lock (owner._stateSync)
            {
                if (owner._activeChildWorkflows.TryGetValue(key, out var running))
                    running.LastMessage = null;
            }
            var result = await scope.RunAsync(cancellationToken).ConfigureAwait(false);
            lock (owner._stateSync)
            {
                if (owner._activeChildWorkflows.TryGetValue(key, out var active))
                    active.LastMessage = result.Message;
            }
            owner.PublishSnapshot();
            return result;
        }

        public void KeepLastCycleOutputs() => _keepLastCycleOutputs = true;

        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return default;
            _disposed = true;
            try
            {
                owner.AdoptChildOutputProjections(parentNode.Id, parentExecution.TokenId,
                    scope.DetachOutputProjections(_keepLastCycleOutputs ? GetReferencedChildOutputNodes(parentNode) : null), _keepLastCycleOutputs);
            }
            finally
            {
                owner.RetireActiveChild(key, parentNode.Id, scope);
            }
            return default;
        }
    }

    /// <summary>
    /// 子运行结束：从活动集合移除，并按父节点覆盖保存最后一份快照（只在这里构造一次）；更早历史只进入事件流。
    /// </summary>
    private void RetireActiveChild(string key, string parentNodeId, WorkflowEngine childEngine)
    {
        ActiveChildWorkflow? completed;
        lock (_stateSync)
        {
            _latestChildEngineByParentNode[parentNodeId] = childEngine;
            _activeChildWorkflows.Remove(key, out completed);
        }
        if (completed is not null)
        {
            // 作用域有自己的锁，在本引擎锁外取快照。
            var info = completed.Capture();
            lock (_stateSync)
                _latestChildWorkflowByParentNode[parentNodeId] = info;
        }
        Volatile.Write(ref childEngine._changeOwner, null);
        PublishSnapshot();
    }
}
