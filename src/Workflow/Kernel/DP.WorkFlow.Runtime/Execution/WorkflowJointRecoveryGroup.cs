using System.Collections.ObjectModel;

namespace DP.WorkFlow;

/// <summary>协作消息身份；恢复开始即废弃旧轮次，恢复授权前不接受业务消息。</summary>
public sealed record WorkflowCollaborationStamp(Guid CollaborationId, long Epoch);

/// <summary>联合恢复时间线中的不可变事实。</summary>
public sealed record WorkflowJointRecoveryEvent(Guid CaseId, long Epoch, string Stage, string? Role, string Message, DateTimeOffset Time);

/// <summary>工位负责的物理退出与联合恢复核实，不以流程暂停冒充设备安全停止。</summary>
public interface IWorkflowJointRecoverySafety
{
    /// <summary>在本地节点已退出后核实设备静止及操作权；失败必须抛出异常。</summary>
    /// <param name="role">工程师声明的参与角色。</param>
    /// <param name="request">本地故障或边界中断。</param>
    /// <param name="cancellationToken">联合运行取消令牌。</param>
    ValueTask EnsureQuiescentAsync(string role, WorkflowFaultRecoveryRequest request, CancellationToken cancellationToken);
    /// <summary>处置成功后核实物料归属、互锁、消息重建等共同前提；失败必须抛出异常。</summary>
    /// <param name="stamp">当前协作恢复轮次。</param>
    /// <param name="decisions">按角色冻结的本地恢复请求。</param>
    /// <param name="cancellationToken">联合运行取消令牌。</param>
    ValueTask ValidateRestoreAsync(WorkflowCollaborationStamp stamp,
        IReadOnlyDictionary<string, WorkflowFaultRecoveryDecision> decisions, CancellationToken cancellationToken);
}

/// <summary>
/// 同进程无环串行流程的联合恢复运行域。流程不互相控制，处置只执行一次；本地准备全部完成才共同放行。
/// 任一成员失败会取消域内运行并保留失败阻断；取消不代表物理设备安全停止。实例只运行一次。
/// </summary>
public sealed class WorkflowJointRecoveryGroup : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Participant> _participants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string?>> _plans = new(StringComparer.Ordinal);
    private readonly List<WorkflowJointRecoveryEvent> _events = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly IWorkflowFaultRecoveryCoordinator _treatment;
    private readonly IWorkflowJointRecoverySafety _safety;
    private Round? _round;
    private long _epoch;
    private bool _started, _failed, _disposed, _finished;
    private Task? _running;

    /// <summary>创建一个协作运行域；必须显式提供工艺处理和现场安全核实能力。</summary>
    /// <param name="treatment">工程师预设的工艺处理协调器，不是每个参与者各执行一次。</param>
    /// <param name="safety">设备退出与联合恢复核实能力。</param>
    public WorkflowJointRecoveryGroup(IWorkflowFaultRecoveryCoordinator treatment, IWorkflowJointRecoverySafety safety)
    {
        _treatment = treatment ?? throw new ArgumentNullException(nameof(treatment));
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
    }

    /// <summary>本次工艺协作身份，不等同于参与流程的RunId。</summary>
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>本次协作预留的联合故障会话身份；多个处置轮次保留在同一个会话。</summary>
    public Guid CaseId { get; } = Guid.NewGuid();
    /// <summary>当前轮次；旧轮次消息不得推进新操作。</summary>
    public WorkflowCollaborationStamp Stamp { get { lock (_sync) return new(Id, _epoch); } }
    /// <summary>联合时间线快照，不含可编辑的会话内部状态。</summary>
    public IReadOnlyList<WorkflowJointRecoveryEvent> Events { get { lock (_sync) return Array.AsReadOnly(_events.ToArray()); } }

    /// <summary>仅在共同放行后接受当前协作轮次消息；消息Adapter必须在提交业务状态时调用此检查。</summary>
    /// <param name="stamp">收到的协作消息身份。</param>
    /// <returns>是否属于当前已授权的协作轮次；不是消息去重或持久化保证。</returns>
    public bool Accepts(WorkflowCollaborationStamp stamp)
    {
        lock (_sync) return !_disposed && !_failed && !_finished && _started && _round is null && stamp.CollaborationId == Id && stamp.Epoch == _epoch;
    }

    /// <summary>在恢复轮次锁内检查身份并提交短小的内存状态更新，避免先检查后写入的竞态。</summary>
    /// <param name="stamp">消息的原协作身份；不得接收时改写为当前轮次。</param>
    /// <param name="commit">只允许短小内存更新，不得执行I/O、启动流程或重入本模块；异常不回滚已写状态。</param>
    /// <returns>消息属于当前已授权轮次并已执行提交时为true；过期消息不调用委托。</returns>
    public bool TryApply(WorkflowCollaborationStamp stamp, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (_sync)
        {
            if (!Accepts(stamp)) return false;
            commit();
            return true;
        }
    }

    /// <summary>注册一个角色和独立运行上下文；不支持循环、并行或Block恢复。</summary>
    /// <param name="role">稳定角色键。</param>
    /// <param name="plan">已冻结绑定的本地流程计划。</param>
    /// <param name="context">本地运行上下文，不得与其他角色共用。</param>
    /// <returns>角色监控和独立Hold句柄，不提供单独启动或重配置能力。</returns>
    public WorkflowJointRecoveryParticipant AddParticipant(string role, WorkflowBoundExecutionPlan plan, WorkflowContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role); ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(context);
        lock (_sync)
        {
            EnsureConfigurable();
            if (_participants.ContainsKey(role)) throw new ArgumentException("角色重复。", nameof(role));
            if (_participants.Values.Any(p => ReferenceEquals(p.Context, context))) throw new ArgumentException("参与者不能共享运行上下文。", nameof(context));
            if (!plan.Plan.IsAcyclic || plan.Plan.ParallelScopes.Count != 0 || plan.Plan.ChildPlans.Count != 0)
                throw new ArgumentException("联合恢复当前仅支持无环串行且不含Block的本地计划。", nameof(plan));
            var participant = new Participant(this, role, context, plan);
            participant.Engine = new WorkflowEngine(plan, context, new WorkflowExecutionOptions
                { RecoveryCoordinator = participant, RecoveryCaseId = CaseId, RecoveryBarrier = participant });
            _participants.Add(role, participant);
            return new WorkflowJointRecoveryParticipant(role, participant.Engine);
        }
    }

    /// <summary>声明命名的联合恢复方案；每个角色映射一个本地入口，null表示保留原路径/原操作。</summary>
    /// <param name="key">工艺处置Restart裁决选择的方案键，不是任意节点ID。</param>
    /// <param name="entries">角色到命名入口的完整映射，注册时复制。</param>
    public void AddRestartPlan(string key, IReadOnlyDictionary<string, string?> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key); ArgumentNullException.ThrowIfNull(entries);
        lock (_sync)
        {
            EnsureConfigurable();
            if (entries.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is not null && string.IsNullOrWhiteSpace(p.Value)))
                throw new ArgumentException("角色和恢复入口不能为空白。", nameof(entries));
            _plans.Add(key, new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(entries, StringComparer.Ordinal)));
        }
    }

    /// <summary>启动所有已声明角色并监管其终态。任一成员提前结束而另一个正在恢复时，联合恢复失败。</summary>
    /// <param name="cancellationToken">整个协作任务的取消令牌。</param>
    /// <returns>按角色保存的最终运行结果；必须结合时间线判断失败原因。</returns>
    public Task<IReadOnlyDictionary<string, WorkflowRunResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureConfigurable();
            if (_participants.Count < 2) throw new InvalidOperationException("联合恢复至少需要两个角色。");
            if (_plans.Values.Any(p => p.Count != _participants.Count || p.Keys.Any(k => !_participants.ContainsKey(k))))
                throw new InvalidOperationException("联合恢复方案必须完整声明所有参与角色。");
            _started = true;
            var task = Task.Run(() => RunCoreAsync(cancellationToken), CancellationToken.None); _running = task; return task;
        }
    }

    private async Task<IReadOnlyDictionary<string, WorkflowRunResult>> RunCoreAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        using var registration = linked.Token.Register(() => Fail("联合运行已取消。"));
        try
        {
            foreach (var participant in _participants.Values)
                WorkflowRuntimeCapabilityValidator.Validate(participant.Plan, participant.Context.Services);
            foreach (var participant in _participants.Values)
                if (participant.Context.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
                    await preparation.PrepareAsync(new WorkflowRunPreparationContext(participant.Plan.Plan.Nodes.Values.ToArray()), linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            var tasks = _participants.Values.Select(p => Task.Run(async () =>
            {
                var result = await p.Engine.RunAsync(linked.Token).ConfigureAwait(false);
                bool failed;
                lock (_sync) { p.Completed = true; failed = !result.Success || _round is not null; }
                if (failed) Fail($"角色 {p.Role} 已退出：{result.Message}");
                return new KeyValuePair<string, WorkflowRunResult>(p.Role, result);
            }, CancellationToken.None)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return new ReadOnlyDictionary<string, WorkflowRunResult>(results.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        }
        catch (Exception failure) { Fail(failure.Message); throw; }
        finally
        {
            lock (_sync)
            {
                _finished = true;
                if (!_failed) Record("Completed", null, "全部协作运行已完成。");
                if (_disposed) _stop.Dispose();
            }
        }
    }

    private async ValueTask<WorkflowFaultRecoveryDecision> ArriveAsync(Participant participant,
        WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
    {
        Round round; bool leader;
        lock (_sync)
        {
            if (_failed || _participants.Values.Any(p => p.Completed)) throw new InvalidOperationException("参与者已退出，禁止联合恢复。");
            leader = _round is null;
            round = _round ??= new Round(++_epoch, participant.Role);
            participant.Round = round;
            if (leader)
            {
                Record("Blocking", participant.Role, request.Message);
                foreach (var peer in _participants.Values.Where(p => p != participant))
                    peer.Engine.RequestJointInterruption("协作任务受阻；在当前节点退出后参与联合恢复。");
            }
        }
        try
        {
            await _safety.EnsureQuiescentAsync(participant.Role, request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_failed) throw new InvalidOperationException("联合恢复已失败。");
                round.Arrived.Add(participant.Role);
                Record("Quiescent", participant.Role, "本地节点已退出，工位已确认允许处置。");
                if (round.Arrived.Count == _participants.Count) round.Ready.TrySetResult();
            }
            if (leader)
            {
                await round.Ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                lock (_sync) Record("Treating", participant.Role, "开始统一工艺处置。");
                var decision = await _treatment.RecoverAsync(request, context, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var decisions = Resolve(decision);
                await _safety.ValidateRestoreAsync(new(Id, round.Epoch), decisions, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (_failed) throw new InvalidOperationException("联合恢复已失败。");
                    Record("Preparing", null, "工艺处置及共同条件已核实，等待各Engine本地恢复准备。");
                    round.Decisions.TrySetResult(decisions);
                }
            }
            var resolved = await round.Decisions.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return resolved[participant.Role];
        }
        catch (Exception failure) { Fail(failure.Message); throw; }
    }

    private IReadOnlyDictionary<string, WorkflowFaultRecoveryDecision> Resolve(WorkflowFaultRecoveryDecision decision)
    {
        if (decision.Action == WorkflowFaultRecoveryAction.ContinueOperation)
            return new ReadOnlyDictionary<string, WorkflowFaultRecoveryDecision>(_participants.Keys.ToDictionary(k => k, _ => WorkflowFaultRecoveryDecision.Continue()));
        if (decision.Action != WorkflowFaultRecoveryAction.RestartFromEntry || decision.RecoveryEntryKey is null || !_plans.TryGetValue(decision.RecoveryEntryKey, out var plan))
            throw new InvalidOperationException(decision.Message ?? "处置未选择有效的联合恢复方案。");
        return new ReadOnlyDictionary<string, WorkflowFaultRecoveryDecision>(plan.ToDictionary(p => p.Key,
            p => p.Value is null ? WorkflowFaultRecoveryDecision.Continue() : WorkflowFaultRecoveryDecision.Restart(p.Value), StringComparer.Ordinal));
    }

    private async ValueTask PreparedAsync(Participant participant, CancellationToken cancellationToken)
    {
        Round round; bool authorize;
        lock (_sync)
        {
            round = participant.Round ?? throw new InvalidOperationException("角色没有参加当前恢复轮次。");
            if (_failed || !ReferenceEquals(round, _round)) throw new InvalidOperationException("恢复轮次已失效。");
            if (!round.Prepared.Add(participant.Role)) throw new InvalidOperationException("角色不能重复提交恢复准备。");
            Record("Prepared", participant.Role, "本地验证、操作清理和数据准备完成。");
            authorize = round.Prepared.Count == _participants.Count;
        }
        if (authorize)
        {
            var decisions = await round.Decisions.Task.ConfigureAwait(false);
            await _safety.ValidateRestoreAsync(new(Id, round.Epoch), decisions, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_failed || !ReferenceEquals(round, _round)) throw new InvalidOperationException("联合恢复授权已撤销。");
                _round = null;
                Record("Authorized", null, "所有参与者准备就绪且共同条件再次核实；独立Hold仍由各自所有者解除。");
                round.Release.TrySetResult();
            }
        }
        await round.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Fail(string message)
    {
        lock (_sync)
        {
            if (_failed) return;
            _failed = true;
            Record("Failed", null, message);
            // 取消而非故障TCS，避免无人等待的重复异常；原因保存在时间线。
            _round?.Ready.TrySetCanceled(); _round?.Decisions.TrySetCanceled(); _round?.Release.TrySetCanceled();
        }
        foreach (var participant in _participants.Values) participant.Engine.AddExternalHold("JointRecoveryFailed:" + CaseId);
        try { _stop.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception failure) { lock (_sync) Record("CancellationCallbackFailed", null, failure.Message); }
    }

    private void Record(string stage, string? role, string message)
    {
        if (_events.Count == 2000) _events.RemoveAt(0);
        _events.Add(new(CaseId, _epoch, stage, role, message, DateTimeOffset.UtcNow));
    }
    private void EnsureConfigurable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) throw new InvalidOperationException("联合运行开始后不能修改参与关系或再次运行。");
    }
    /// <summary>取消整个域；活动运行结束前保留取消源，避免释放竞态。应先等待RunAsync结束再Dispose。</summary>
    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        if (_running is not null && !_running.IsCompleted) Fail("联合运行域已释放。");
        else _stop.Dispose();
    }

    private sealed class Round(long epoch, string source)
    {
        public long Epoch { get; } = epoch;
        public string Source { get; } = source;
        public HashSet<string> Arrived { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Prepared { get; } = new(StringComparer.Ordinal);
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyDictionary<string, WorkflowFaultRecoveryDecision>> Decisions { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Participant(WorkflowJointRecoveryGroup group, string role, WorkflowContext context, WorkflowBoundExecutionPlan plan)
        : IWorkflowFaultRecoveryCoordinator, IWorkflowRecoveryBarrier
    {
        public string Role { get; } = role;
        public WorkflowContext Context { get; } = context;
        public WorkflowBoundExecutionPlan Plan { get; } = plan;
        public WorkflowEngine Engine { get; set; } = null!;
        public bool Completed { get; set; }
        public Round? Round { get; set; }
        public ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request,
            IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken) => group.ArriveAsync(this, request, context, cancellationToken);
        public ValueTask PreparedAsync(WorkflowFaultRecoveryRequest request, CancellationToken cancellationToken) => group.PreparedAsync(this, cancellationToken);
        public void Reject(WorkflowFaultRecoveryRequest request, string message) => group.Fail(Role + ": " + message);
    }
}
