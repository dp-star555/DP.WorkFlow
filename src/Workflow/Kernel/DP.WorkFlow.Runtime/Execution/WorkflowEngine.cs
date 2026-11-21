using System.Diagnostics;

namespace DP.WorkFlow;

/// <summary>
/// 执行已经编译的工作流定义，并发布 UI 无关的运行快照与 Trace。
/// 支持显式端口、循环、结构化并行、子流程、暂停和外部 Hold。
/// </summary>
public sealed partial class WorkflowEngine
{
    // 继续运行（RunNodeAsync）可换成重新编译的计划，其余时候只在构造时赋值。
    private WorkflowExecutionPlan _plan;
    private WorkflowBoundExecutionPlan _boundPlan;
    private readonly WorkflowExecutionOptions _options;
    private readonly WorkflowOutputValueExtractor _outputValueExtractor;
    private readonly AsyncManualResetEvent _resumeGate = new();
    private readonly WorkflowNotificationDispatcher _notifications = new();
    private readonly object _stateSync = new();
    private readonly HashSet<string> _externalHoldReasons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeNodeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<long, WorkflowActiveTokenInfo> _activeTokens = new();
    private readonly Dictionary<string, MutableNodeRuntimeInfo> _nodeRuntime = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _nodeExecutionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<long, MutableParallelScopeInfo> _parallelScopes = new();
    private readonly Dictionary<string, WorkflowEngine> _activeChildEngines = new(StringComparer.Ordinal);
    // 正在运行的子运行：快照不再由子引擎推送，取父快照时按需向子引擎拉取（R1-04）。
    private readonly Dictionary<string, ActiveChildWorkflow> _activeChildWorkflows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowChildRuntimeInfo> _latestChildWorkflowByParentNode = new(StringComparer.Ordinal);
    // 每个父节点最近一次完成的子引擎：子画布据此在运行结束后仍能查询其节点输出，父运行开始时清空。
    private readonly Dictionary<string, WorkflowEngine> _latestChildEngineByParentNode = new(StringComparer.Ordinal);
    // 正在运行的子引擎轨迹来源，按子运行键索引；读取轨迹时实时合并。
    private readonly Dictionary<string, ChildTraceSource> _childTraceSources = new(StringComparer.Ordinal);
    // 每个父节点已结束的子运行（循环块按圈）轨迹，有界保留，父运行开始时清空。
    private readonly Dictionary<string, Queue<WorkflowTraceEntry>> _childTraceHistory = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _startedRecoveryCases = new();
    private volatile IWorkflowRunRecorder? _recorder;
    private Stopwatch? _runStopwatch;
    private Guid _runId;
    internal string PlanPath { get; set; } = "$";
    internal Guid BindingScopeId { get; set; }
    private Guid? _parentRunId;
    // 循环块子引擎每圈结束时不等待运行记录写出；会话结束时再等待最后一份收尾。
    private bool _deferRecordingCompletion;
    private Task _pendingRecordingCompletion = Task.CompletedTask;
    private WorkflowExecutionIdentity? _parentExecution;
    private string? _currentNodeId;
    private string? _terminalMessage;
    private WorkflowNodeFault? _currentFault;
    private WorkflowRecoveryEvent? _currentRecovery;
    private bool _manualPauseRequested;
    private int _isRunning;
    private long _parallelScopeSequence;
    private long _nodeExecutionSequence;
    private long _tokenSequence;
    private long _snapshotSequence;
    private long _changeVersion;
    private string? _changeMessage;
    // 正在运行本引擎（作为子运行）的父引擎；本引擎的变化同时递增父引擎的变化计数。
    private WorkflowEngine? _changeOwner;
    // 已作为提示报告过的记录丢失 / 写入失败条数；记录健康变化时只报告新增部分。
    private long _reportedRecordingLosses;
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
        _outputValueExtractor = new WorkflowOutputValueExtractor(_options.Recording.MaxOutputProperties);
        // 订阅方异常不影响流程，但要看得见（AR-19）：计入记录健康状态的诊断。
        _notifications.FailureReported = message => ReportRecordingDegraded(message);
    }

    /// <summary>获取节点提示板（R1-01）：不影响运行结果的非致命提示，按节点和画布累计，确认后清除。</summary>
    public WorkflowNoticeBoard Notices => _options.Notices;

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

    // 以下回调都不在执行线程上同步调用：执行线程只把通知放入队列，由后台按发生顺序逐个送达（R1-04）。
    // 订阅方的异常不影响流程，计入记录健康状态的诊断。需要确认此前的通知已送达时调用 FlushNotificationsAsync。

    /// <summary>运行开始时发生，参数为本次运行的身份。</summary>
    public event Action<Guid>? RunStarted;

    /// <summary>运行结束时发生，参数为运行结果（完成 / 取消 / 失败及原因）；在本次运行的其他回调之后送达。</summary>
    public event Action<WorkflowRunResult>? RunEnded;

    /// <summary>运行状态变化时发生。</summary>
    public event Action<E_WorkflowExecutionState>? StateChanged;

    /// <summary>节点开始执行时发生。</summary>
    public event Action<IWorkflowNodeModel>? NodeStarted;

    /// <summary>节点成功完成时发生。</summary>
    public event Action<IWorkflowNodeModel>? NodeCompleted;

    /// <summary>记录链路健康状态变化时发生；记录失败不影响工作流运行结果。</summary>
    public event Action<WorkflowRecordingHealth>? RecordingHealthChanged;

    /// <summary>
    /// 运行状态的变化计数（R1-04）。每次可观察的状态迁移递增一次；执行线程只做这一次递增，不构造快照、不调用界面。
    /// 界面按自己的刷新帧率比较这个值，变了才调用 <see cref="GetRuntimeSnapshot"/> 取一份整体状态；
    /// 停止、暂停、失败等需要立即显示的状态另由 <see cref="StateChanged"/> 通知。
    /// </summary>
    public long ChangeVersion => Interlocked.Read(ref _changeVersion);

    /// <summary>等待此前发出的回调全部送达订阅方。</summary>
    /// <returns>队列中已有通知全部调用完成时结束的任务。</returns>
    public Task FlushNotificationsAsync() => _notifications.FlushAsync();

    /// <summary>获取当前运行的事件记录健康状态；未启动运行时为初始健康状态。</summary>
    public WorkflowRecordingHealth RecordingHealth => _recorder?.Health ?? WorkflowRecordingHealth.Initial;

    /// <summary>由父引擎在启动子运行前注入父 Run 身份，使子事件记录父子血缘。</summary>
    /// <param name="parentRunId">父运行身份。</param>
    /// <param name="parentExecution">触发子运行的父节点执行身份。</param>
    internal void SetParentContext(Guid parentRunId, WorkflowExecutionIdentity parentExecution)
    {
        _parentRunId = parentRunId;
        _parentExecution = parentExecution;
    }
}
