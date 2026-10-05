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
    private readonly WorkflowOutputValueExtractor _outputValueExtractor;
    private readonly AsyncManualResetEvent _resumeGate = new();
    private readonly object _stateSync = new();
    private readonly HashSet<string> _externalHoldReasons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeNodeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<long, WorkflowActiveTokenInfo> _activeTokens = new();
    private readonly Dictionary<string, MutableNodeRuntimeInfo> _nodeRuntime = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _nodeExecutionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<long, MutableParallelScopeInfo> _parallelScopes = new();
    private readonly Dictionary<string, WorkflowEngine> _activeChildEngines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowChildRuntimeInfo> _activeChildWorkflows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowChildRuntimeInfo> _latestChildWorkflowByParentNode = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _startedRecoveryCases = new();
    private volatile IWorkflowRunRecorder? _recorder;
    private Stopwatch? _runStopwatch;
    private Guid _runId;
    internal string PlanPath { get; set; } = "$";
    internal Guid BindingScopeId { get; set; }
    private Guid? _parentRunId;
    private WorkflowExecutionIdentity? _parentExecution;
    private string? _currentNodeId;
    private string? _terminalMessage;
    private WorkflowNodeFault? _currentFault;
    private WorkflowRecoveryEvent? _currentRecovery;
    private bool _manualPauseRequested;
    private int _isRunning;
    private int _totalNodeExecutions;
    private long _parallelScopeSequence;
    private long _nodeExecutionSequence;
    private long _tokenSequence;
    private long _snapshotSequence;
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

    /// <summary>记录链路健康状态变化时发生；记录失败不影响工作流运行结果。</summary>
    public event Action<WorkflowRecordingHealth>? RecordingHealthChanged;

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
