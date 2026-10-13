using System.Diagnostics;

namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    private long _rootTokenId;

    /// <summary>
    /// 在已结束的本次运行中再正常执行一个节点（例如单独运行循环块、子流程的子画布）：
    /// 沿用本次运行的上下文和已提交输出作为输入，结果照常提交并取代该节点上一次的输出；
    /// 运行标识不变，节点状态、执行次数、轨迹和记录序号都接着本次运行继续。只执行这一个节点，不沿连线继续。
    /// </summary>
    /// <param name="plan">
    /// 执行所用的计划，通常由当前画布（含尚未确认的节点修改）重新编译；之后的继续运行和输出查询也使用它。
    /// </param>
    /// <param name="nodeId">要执行的节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该节点执行的结果；节点失败或取消时为对应终态。</returns>
    /// <exception cref="InvalidOperationException">引擎正在运行，或还没有运行过。</exception>
    public async Task<WorkflowRunResult> RunNodeAsync(
        WorkflowBoundExecutionPlan plan,
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        var node = plan.Plan.GetNodeOrThrow(nodeId);
        if (Interlocked.Exchange(ref _isRunning, 1) != 0)
            throw new InvalidOperationException("当前 WorkflowEngine 已经处于运行中。");
        try
        {
            BeginContinuation(plan, node);
        }
        catch
        {
            Volatile.Write(ref _isRunning, 0);
            throw;
        }

        var title = string.IsNullOrWhiteSpace(node.Title) ? node.Id : node.Title;
        try
        {
            await WaitForAdmissionAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // 以本次运行根 Token 的身份执行：根路径上已提交的上游输出对它可见，与在主流程中执行该节点时一致。
            var token = new ExecutionToken(_rootTokenId, Array.Empty<long>(), Array.Empty<long>());
            await ExecuteNodeAsync(node, token, cancellationToken).ConfigureAwait(false);
            var message = $"节点 {title} 单独运行完成。";
            SetTerminalState(E_WorkflowExecutionState.Completed, message);
            return new WorkflowRunResult(true, State, Elapsed, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetTerminalState(E_WorkflowExecutionState.Canceled, "单独运行已取消。");
            return new WorkflowRunResult(false, State, Elapsed, "单独运行已取消。", node.Id);
        }
        catch (WorkflowPathException exception)
        {
            SetTerminalState(E_WorkflowExecutionState.Faulted, exception.Message);
            return new WorkflowRunResult(false, State, Elapsed, exception.Message, exception.NodeId);
        }
        catch (Exception exception)
        {
            SetTerminalState(E_WorkflowExecutionState.Faulted, exception.Message);
            return new WorkflowRunResult(false, State, Elapsed, exception.Message, node.Id);
        }
        finally
        {
            await DisposePendingOperationsAsync().ConfigureAwait(false);
            E_WorkflowExecutionState terminalState;
            string? terminalMessage;
            lock (_stateSync)
            {
                _runStopwatch?.Stop();
                _currentNodeId = null;
                _activeNodeIds.Clear();
                _activeTokens.Clear();
                terminalState = _state;
                terminalMessage = _terminalMessage;
            }
            await CompleteRecordingAsync(terminalState, terminalMessage).ConfigureAwait(false);
            PublishSnapshot(terminalMessage);
            Volatile.Write(ref _isRunning, 0);
        }
    }

    /// <summary>重新打开已结束的运行：保留输出、节点状态和子运行记录，只重置本次执行的终态、计时和记录器。</summary>
    private void BeginContinuation(WorkflowBoundExecutionPlan plan, IWorkflowNodeModel node)
    {
        E_WorkflowExecutionState changedState;
        lock (_stateSync)
        {
            if (_recorder is null || _runId == Guid.Empty)
                throw new InvalidOperationException("还没有运行过流程：先运行一次主流程，单独运行才能使用上游节点的结果。");
            _boundPlan = plan;
            _plan = plan.Plan;
            _currentNodeId = node.Id;
            _terminalMessage = null;
            _currentFault = null;
            _currentRecovery = null;
            _activeNodeIds.Clear();
            _activeTokens.Clear();
            // 该节点上一次子运行的各圈轨迹被这次运行取代，圈序号重新从 1 开始。
            _childTraceHistory.Remove(node.Id);
            _runStopwatch = Stopwatch.StartNew();
            if (_parentRunId is null)
                _manualPauseRequested = false;
            _state = E_WorkflowExecutionState.Running;
            changedState = ApplyPauseStateLocked() ?? _state;
        }
        ContinueRecorder(node);
        PublishStateChange(changedState);
    }

    private void ContinueRecorder(IWorkflowNodeModel node)
    {
        var previous = _recorder;
        _recorder = previous is WorkflowRunRecorder recorder
            ? recorder.CreateContinuation()
            : new WorkflowRunRecorder(_runId, _plan.Name, _options.MaxTraceEntries, _options.Recording, _parentRunId, _parentExecution);
        _recorder.HealthChanged += OnRecordingHealthChanged;
        TryRecordRunEvent(
            WorkflowRunEventDraft.Lifecycle("RunContinued", $"单独运行节点 {node.Id}。",
                new Dictionary<string, object?> { ["NodeId"] = node.Id }),
            WorkflowEventWriteMode.FlushRequested);
    }
}
