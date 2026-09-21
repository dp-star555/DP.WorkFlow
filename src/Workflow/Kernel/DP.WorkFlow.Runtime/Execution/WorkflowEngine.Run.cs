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
            // 终态事件和 Flush 失败只影响 RecordingHealth，不改写已经确定的 Workflow 终态。
            await CompleteRecordingAsync(terminalState, terminalMessage).ConfigureAwait(false);
            PublishSnapshot(terminalMessage);
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private async Task CompleteRecordingAsync(E_WorkflowExecutionState terminalState, string? message)
    {
        // 保留 _recorder 引用：Run 结束后最近事件窗口仍需可读，供 Studio 和诊断查询使用。
        var recorder = _recorder;
        if (recorder is null)
            return;
        try
        {
            recorder.Record(
                WorkflowRunEventDraft.Lifecycle(
                    terminalState switch
                    {
                        E_WorkflowExecutionState.Completed => "RunCompleted",
                        E_WorkflowExecutionState.Canceled => "RunCanceled",
                        _ => "RunFaulted"
                    },
                    message,
                    new Dictionary<string, object?> { ["State"] = terminalState.ToString() }),
                WorkflowEventWriteMode.Durable);
            await recorder.CompleteAsync(new WorkflowRunCompletion(terminalState, message, Elapsed)).ConfigureAwait(false);
        }
        catch
        {
            // 收尾记录异常绝不能改写 Run 结果。
        }
        finally
        {
            recorder.HealthChanged -= OnRecordingHealthChanged;
            try
            {
                await recorder.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // 记录器释放失败同样不影响已经返回的 Run 结果。
            }
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
        Guid runId;
        lock (_stateSync)
        {
            _runId = Guid.NewGuid();
            runId = _runId;
            RunState = new WorkflowRunState();
            RunState.Begin(_runId);
            _currentNodeId = startNodeId;
            _terminalMessage = null;
            _currentFault = null;
            _currentRecovery = null;
            _activeNodeIds.Clear();
            _activeTokens.Clear();
            _nodeRuntime.Clear();
            _nodeExecutionCounts.Clear();
            _nodeExecutionSequence = 0;
            _parallelScopes.Clear();
            _activeChildEngines.Clear();
            _activeChildWorkflows.Clear();
            _latestChildWorkflowByParentNode.Clear();
            _startedRecoveryCases.Clear();
            _totalNodeExecutions = 0;
            _tokenSequence = 0;
            _runStopwatch = Stopwatch.StartNew();
            _manualPauseRequested = false;
            _state = E_WorkflowExecutionState.Running;
            Context.BeginRun(RunState);
            changedState = ApplyPauseStateLocked() ?? _state;
        }
        // RunStarted 进入 Recorder 后立即开始调度；后续 Sink 推送失败不能阻止或终止运行。
        StartRecorder(runId);
        PublishStateChange(changedState);
    }

    private void StartRecorder(Guid runId)
    {
        _recorder = new WorkflowRunRecorder(
            runId,
            _plan.Name,
            _options.MaxTraceEntries,
            _options.Recording,
            _parentRunId,
            _parentExecution);
        _recorder.HealthChanged += OnRecordingHealthChanged;
        _recorder.Record(
            WorkflowRunEventDraft.Lifecycle("RunStarted", "流程开始执行。"),
            WorkflowEventWriteMode.Durable);
    }

    private void OnRecordingHealthChanged(WorkflowRecordingHealth health) =>
        SafeInvoke(RecordingHealthChanged, health);

    private void SetTerminalState(E_WorkflowExecutionState state, string? message)
    {
        lock (_stateSync)
        {
            _state = state;
            _terminalMessage = message;
        }
        SafeInvoke(StateChanged, state);
        PublishSnapshot(message);
    }
}
