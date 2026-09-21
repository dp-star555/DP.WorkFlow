using System.Diagnostics;

namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
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

    private void SetTerminalState(E_WorkflowExecutionState state, string? message)
    {
        lock (_stateSync)
            _state = state;
        SafeInvoke(StateChanged, state);
        PublishSnapshot(message);
    }
}
