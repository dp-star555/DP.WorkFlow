namespace DP.WorkFlow;

/// <summary>运行日志树里一行的种类。</summary>
public enum WorkflowRunLogRowKind
{
    /// <summary>一次运行（树根）。</summary>
    Run,

    /// <summary>普通节点的一次执行。</summary>
    Node,

    /// <summary>子流程（复合节点）：下面是它子画布里的节点。</summary>
    SubFlow,

    /// <summary>循环块：下面每圈一层。</summary>
    LoopBlock,

    /// <summary>循环块的一圈。</summary>
    Cycle,

    /// <summary>并行：下面每个分支一层。</summary>
    Parallel,

    /// <summary>并行的一个分支。</summary>
    Branch,

    /// <summary>节点故障。</summary>
    Fault,

    /// <summary>故障处理（故障会话、回退、放行）。</summary>
    Recovery,

    /// <summary>节点提示。</summary>
    Notice,

    /// <summary>节点自己写的进度或说明（非控制步骤）。</summary>
    Message,

    /// <summary>运行级的说明（要求停下、继续运行等）。</summary>
    Lifecycle
}

/// <summary>一行的结果。</summary>
public enum WorkflowRunLogState
{
    /// <summary>没有结果（说明类的行）。</summary>
    None,

    /// <summary>进行中。</summary>
    Running,

    /// <summary>成功结束。</summary>
    Succeeded,

    /// <summary>失败。</summary>
    Failed,

    /// <summary>被中断（故障处理接管）。</summary>
    Interrupted,

    /// <summary>取消。</summary>
    Canceled,

    /// <summary>失败后走了故障出口。</summary>
    FaultRouted,

    /// <summary>回退。</summary>
    Undone
}

/// <summary>
/// 运行日志树的一行。只由 <see cref="WorkflowRunLog"/> 在锁内修改；界面在 <see cref="WorkflowRunLogBook.Read{T}"/> 的锁内读它，读出快照再画。
/// </summary>
public sealed class WorkflowRunLogRow
{
    private List<WorkflowRunLogRow>? _children;

    internal WorkflowRunLogRow(long id, WorkflowRunLogRowKind kind, WorkflowRunLogRow? parent, string title, DateTimeOffset started)
    {
        Id = id;
        Kind = kind;
        Parent = parent;
        Title = title;
        Started = started;
    }

    /// <summary>运行内唯一的行号（按出现顺序）。</summary>
    public long Id { get; }

    /// <summary>种类；节点执行到后来发现是子流程 / 循环块 / 并行时会改成对应种类。</summary>
    public WorkflowRunLogRowKind Kind { get; internal set; }

    /// <summary>上一层；树根为空。</summary>
    public WorkflowRunLogRow? Parent { get; }

    /// <summary>标题（节点名、“第 3 圈”、“分支 1 放料”等）。</summary>
    public string Title { get; internal set; }

    /// <summary>说明（节点消息、出口、故障原因等）。</summary>
    public string? Detail { get; internal set; }

    /// <summary>节点 ID；不是节点的行为空。</summary>
    public string? NodeId { get; internal set; }

    /// <summary>节点类型键。</summary>
    public string? NodeType { get; internal set; }

    /// <summary>所在画布路径（<c>$</c>、<c>$/循环块</c>）。</summary>
    public string CanvasPath { get; internal set; } = "$";

    /// <summary>执行令牌；没有时为 0。</summary>
    public long TokenId { get; internal set; }

    /// <summary>父令牌；没有时为 0。</summary>
    public long ParentTokenId { get; internal set; }

    /// <summary>圈序号（圈行、循环块里的行）；没有时为 0。</summary>
    public int Cycle { get; internal set; }

    /// <summary>开始时间。</summary>
    public DateTimeOffset Started { get; internal set; }

    /// <summary>结束时间；进行中或说明类的行为空。</summary>
    public DateTimeOffset? Ended { get; internal set; }

    /// <summary>节点报告的耗时（比开始 / 结束时间差更准）；没有时为空。</summary>
    public TimeSpan? Elapsed { get; internal set; }

    /// <summary>结果。</summary>
    public WorkflowRunLogState State { get; internal set; }

    /// <summary>这一行或下面有失败 / 故障。</summary>
    public bool HasFailure { get; internal set; }

    /// <summary>这一行和下面的提示条数。</summary>
    public int NoticeCount { get; internal set; }

    /// <summary>失败 / 故障条数（这一行和下面）。</summary>
    public int FailureCount { get; internal set; }

    /// <summary>最后一条相关事件的数据（节点写的消息和数据），给详情面板看。</summary>
    public IReadOnlyDictionary<string, WorkflowTraceValue>? Data { get; internal set; }

    /// <summary>故障处理会话（有的话）。</summary>
    public Guid? RecoveryCaseId { get; internal set; }

    /// <summary>下一层。</summary>
    public IReadOnlyList<WorkflowRunLogRow> Children => (IReadOnlyList<WorkflowRunLogRow>?)_children ?? Array.Empty<WorkflowRunLogRow>();

    /// <summary>是否是容器（子流程、循环块、圈、并行、分支、运行）。</summary>
    public bool IsContainer => Kind is WorkflowRunLogRowKind.Run or WorkflowRunLogRowKind.SubFlow or WorkflowRunLogRowKind.LoopBlock
        or WorkflowRunLogRowKind.Cycle or WorkflowRunLogRowKind.Parallel or WorkflowRunLogRowKind.Branch;

    internal void Add(WorkflowRunLogRow child) => (_children ??= new List<WorkflowRunLogRow>()).Add(child);

    internal void MarkFailure()
    {
        for (var row = this; row is not null; row = row.Parent)
        {
            row.HasFailure = true;
            row.FailureCount++;
        }
    }

    internal void MarkNotice()
    {
        for (var row = this; row is not null; row = row.Parent)
            row.NoticeCount++;
    }

    /// <summary>从根到这一行经过的标题（不含树根）。</summary>
    public IReadOnlyList<string> Path()
    {
        var titles = new List<string>();
        for (var row = this; row?.Parent is not null; row = row.Parent)
            titles.Insert(0, row.Title);
        return titles;
    }
}
