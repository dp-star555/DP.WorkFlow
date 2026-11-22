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

        var result = await RunCoreAsync(cancellationToken).ConfigureAwait(false);
        Notify(RunEnded, result);
        return result;
    }

    private async Task<WorkflowRunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        BeginRun(_plan.EntryNodeId);
        try
        {
            // 流程的公共数据清单登记到公共数据中心；与已有登记不一致时整批拒绝，运行以故障结束。
            // 子画布用到的公共数据已登记在根流程上（Schema 5），作用域不再重复登记。
            if (!IsScope && _plan.PublicData.Count > 0)
                Context.PublicDataCenter.DeclareAll(_plan.PublicData);
            var rootToken = new ExecutionToken(
                NextTokenId(),
                Array.Empty<long>(),
                Array.Empty<long>(),
                this,
                _enclosingToken,
                isCanvasRoot: true,
                CurrentStopEpoch);
            _rootTokenId = rootToken.TokenId;
            try
            {
                await ExecutePathAsync(_plan.EntryNodeId, null, rootToken, cancellationToken).ConfigureAwait(false);
            }
            catch (ResumeRedirectException redirect) when (redirect.Target.LapScope == this)
            {
                // 循环块这一圈被"结束本圈"放弃：本圈到此为止，循环块接着进入下一圈。
                SetTerminalState(E_WorkflowExecutionState.Completed, "本圈已按恢复去向结束。");
                return new WorkflowRunResult(true, State, Elapsed, "本圈已按恢复去向结束。");
            }
            SetTerminalState(E_WorkflowExecutionState.Completed, "流程执行完成。");
            return new WorkflowRunResult(true, State, Elapsed, "流程执行完成。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetTerminalState(E_WorkflowExecutionState.Canceled, "流程已取消。");
            return new WorkflowRunResult(false, State, Elapsed, "流程已取消。", GetCurrentNodeId());
        }
        catch (ResumeRedirectException) when (IsScope)
        {
            // 去向在外层：关闭本子画布，交给外层接住。
            SetTerminalState(E_WorkflowExecutionState.Canceled, "恢复去向转到外层，子画布关闭。");
            throw;
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
            if (IsScope)
                RecordCanvasExited(terminalState, terminalMessage);
            else
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
                WorkflowEventWriteMode.FlushRequested);
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
        if (IsScope)
        {
            BeginScope(startNodeId);
            return;
        }
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
            _activeChildWorkflows.Clear();
            _latestChildWorkflowByParentNode.Clear();
            _latestChildEngineByParentNode.Clear();
            // 上一轮剩余输出的资源由所有者在根运行开始时退役；子运行结束后由父引擎接管（见 DetachOutputProjections）。
            _outputProjections.Clear();
            _childOutputProjections.Clear();
            _tokenSequence = 0;
            _runStopwatch = Stopwatch.StartNew();
            // 子引擎的人工暂停由父引擎在运行前传入（包括循环块两圈之间），不能被新一轮运行清除。
            if (_parentRunId is null)
                _manualPauseRequested = false;
            _state = E_WorkflowExecutionState.Running;
            Context.BeginRun(RunState);
            changedState = ApplyPauseStateLocked() ?? _state;
            // RunStarted 进入 Recorder 后立即开始调度；后续 Sink 推送失败不能阻止或终止运行。
            // 在状态锁内创建记录器：状态一变为运行，暂停、挂起等控制调用就能写入同一份记录，不会先于 RunStarted。
            StartRecorder(runId);
            // 带着暂停或挂起开始的运行：RunStarted 之后紧接一条 Paused，说明为什么没有开始调度。
            if (changedState == E_WorkflowExecutionState.Paused)
                RecordControl(CaptureControlRecordLocked(changedState, null, null));
        }
        PublishStateChange(changedState);
    }

    /// <summary>
    /// 开始一次子画布作用域（R3-a）：本画布的数据和节点状态重新开始，运行身份、记录和暂停门沿用根运行。
    /// 作用域有自己的数据标识（<see cref="WorkflowRunState.RunId"/>），子画布节点的输出按它查询。
    /// </summary>
    private void BeginScope(string? startNodeId)
    {
        lock (_stateSync)
        {
            _runId = Root._runId;
            RunState = new WorkflowRunState();
            RunState.Begin(Guid.NewGuid());
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
            _activeChildWorkflows.Clear();
            _latestChildWorkflowByParentNode.Clear();
            _latestChildEngineByParentNode.Clear();
            _outputProjections.Clear();
            _childOutputProjections.Clear();
            _runStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _state = E_WorkflowExecutionState.Running;
            Context.BeginRun(RunState);
        }
        var cycle = _scopeParents.Count == 0 ? 0 : _scopeParents[^1].Cycle;
        TryRecordRunEvent(
            WorkflowRunEventDraft.Lifecycle("CanvasEntered", "进入子画布。", new Dictionary<string, object?>
            {
                ["WorkflowName"] = _plan.Name,
                ["PlanPath"] = PlanPath,
                ["EntryNodeId"] = _plan.EntryNodeId,
                ["NodeCount"] = _plan.NodeIds.Count,
                ["Cycle"] = cycle
            }));
        PublishSnapshot();
    }

    /// <summary>子画布作用域结束：记一条 CanvasExited（完成 / 取消 / 失败），记录本身由根运行收尾。</summary>
    private void RecordCanvasExited(E_WorkflowExecutionState state, string? message) =>
        TryRecordRunEvent(
            WorkflowRunEventDraft.Lifecycle("CanvasExited", message, new Dictionary<string, object?>
            {
                ["State"] = state.ToString()
            }));

    private void StartRecorder(Guid runId)
    {
        _recorder = new WorkflowRunRecorder(
            runId,
            _plan.Name,
            _options.MaxTraceEntries,
            _options.Recording,
            _parentRunId,
            _parentExecution);
        Interlocked.Exchange(ref _reportedRecordingLosses, 0);
        _recorder.HealthChanged += OnRecordingHealthChanged;
        // 与后续事件一样走 fail-open 入口：RunStarted 的记录失败不得让 Run 在启动阶段就 Fault。
        // 流程身份（R4-a）：名称、在根流程中的路径、入口和节点数。流程版本随流程管理（阶段 D）加入。
        TryRecordRunEvent(
            WorkflowRunEventDraft.Lifecycle("RunStarted", "流程开始执行。", new Dictionary<string, object?>
            {
                ["WorkflowName"] = _plan.Name,
                ["PlanPath"] = PlanPath,
                ["EntryNodeId"] = _plan.EntryNodeId,
                ["NodeCount"] = _plan.NodeIds.Count
            }),
            WorkflowEventWriteMode.FlushRequested);
        Notify(RunStarted, runId);
    }

    private void OnRecordingHealthChanged(WorkflowRecordingHealth health)
    {
        Notify(RecordingHealthChanged, health);
        // 记录丢失让人看得到（R1-01）：作为画布级提示，只报告新增的条数。
        var losses = health.DroppedCount + health.FailedWriteCount;
        var previous = Interlocked.Exchange(ref _reportedRecordingLosses, losses);
        if (losses > previous)
            RaiseNotice(null, "记录丢失",
                $"运行记录丢失或写入失败 {losses - previous} 条（累计丢失 {health.DroppedCount}、写入失败 {health.FailedWriteCount}）"
                + (health.LastError is { } error ? $"：{error}" : "。"),
                recordEvent: false);
    }

    /// <summary>
    /// 报告一次不改变节点结果的降级（记录元数据、资源归还、回调失败等）：计入记录健康状态，
    /// 并作为提示显示在对应节点上（无法归属到节点时为画布级提示）。
    /// </summary>
    /// <param name="message">面向诊断的说明。</param>
    /// <param name="nodeId">相关节点；无法归属时为空。</param>
    private void ReportRecordingDegraded(string message, string? nodeId = null)
    {
        try
        {
            Root._recorder?.ReportDegraded(message);
        }
        catch
        {
            // 降级报告属于可观测性，绝不能改变节点执行或 Run 终态。
        }
        // 降级本身可能来自记录失败，这里不再写事件，避免循环。
        RaiseNotice(nodeId, "记录", message, recordEvent: false);
    }

    /// <summary>
    /// 添加一条提示（R1-01）：写入提示板、记一条 NodeNoticeRaised 事件，并让界面在下一帧刷新。绝不抛异常。
    /// </summary>
    /// <param name="nodeId">所属节点；画布级提示为空。</param>
    /// <param name="category">类别。</param>
    /// <param name="message">说明。</param>
    /// <param name="recordEvent">是否写入运行记录；来自记录链路自身的提示不写，避免循环。</param>
    /// <param name="nodeType">所属节点的类型，写入事件。</param>
    internal void RaiseNotice(string? nodeId, string category, string message, bool recordEvent = true, string? nodeType = null)
    {
        try
        {
            var notice = _options.Notices.Raise(_runId, PlanPath, nodeId, category, message);
            if (recordEvent)
                RecordRunEvent(new WorkflowRunEventDraft(
                    WorkflowRunEventCategory.Trace,
                    "NodeNoticeRaised",
                    message,
                    NodeId: nodeId,
                    NodeType: nodeType,
                    Data: new Dictionary<string, object?>
                    {
                        ["Category"] = category,
                        ["Count"] = notice.Count
                    }));
            MarkChanged();
        }
        catch
        {
            // 提示只供观察，任何失败都不能影响运行。
        }
    }

    private void SetTerminalState(E_WorkflowExecutionState state, string? message)
    {
        lock (_stateSync)
        {
            _state = state;
            _terminalMessage = message;
        }
        Notify(StateChanged, state);
        PublishSnapshot(message);
    }
}
