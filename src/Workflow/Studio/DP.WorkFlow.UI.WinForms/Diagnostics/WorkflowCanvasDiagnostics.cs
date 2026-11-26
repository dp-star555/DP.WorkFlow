using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 画布诊断：编辑时持续检查当前画布（结构、绑定、领域检查），结果直接画在画布上（出错节点标红并带角标，悬停看原因），
/// 不再有单独的诊断列表。运行前由画布工作区调用 <see cref="RefreshDiagnostics"/> 并按 <see cref="CanRun"/> 拦截。
/// </summary>
public sealed class WorkflowCanvasDiagnostics : IDisposable
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private WorkflowDesignerSession? _session;
    private WorkflowDiagnosticsModel? _model;
    private string? _entryNodeId;
    private bool _followsDocumentEntry;
    private IWorkflowDiagnosticProvider? _provider;
    private WorkflowDesignerNavigator? _navigator;

    /// <summary>被检查的画布会话。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set { _session = value; RecreateModel(); }
    }

    /// <summary>检查用的入口节点（工作台主画布）。</summary>
    public string? EntryNodeId
    {
        get => _entryNodeId;
        set { _entryNodeId = value; RecreateModel(); }
    }

    /// <summary>
    /// 是否按会话文档的当前入口检查，不需要设置 <see cref="EntryNodeId"/>；
    /// 用于节点窗口中正在编辑的子画布，子画布可能稍后才放入启动节点。
    /// </summary>
    public bool FollowsDocumentEntry
    {
        get => _followsDocumentEntry;
        set { _followsDocumentEntry = value; RecreateModel(); }
    }

    /// <summary>领域检查及后台准备报告。</summary>
    public IWorkflowDiagnosticProvider? Provider
    {
        get => _provider;
        set
        {
            if (_provider != null) _provider.Changed -= OnProviderChanged;
            _provider = value;
            if (_provider != null) _provider.Changed += OnProviderChanged;
            RecreateModel();
        }
    }

    /// <summary>诊断定位所用导航器。</summary>
    public WorkflowDesignerNavigator? Navigator
    {
        get => _navigator;
        set { _navigator = value; RecreateModel(); }
    }

    /// <summary>当前诊断项。</summary>
    public IReadOnlyList<WorkflowDiagnosticItem> Items => _model?.Items ?? Array.Empty<WorkflowDiagnosticItem>();

    /// <summary>没有阻止运行的错误。</summary>
    public bool CanRun => _model?.CanRun == true;

    /// <summary>错误和警告条数（工具栏“校验”上的数字）。</summary>
    public int IssueCount => Items.Count;

    /// <summary>诊断结果变化后发生（在界面线程）。</summary>
    public event EventHandler? Changed;

    /// <summary>重新检查（运行前、点“校验”时）。</summary>
    public void RefreshDiagnostics() => _model?.Refresh();

    private void RecreateModel()
    {
        DisposeModel();
        if (_session is not null && _followsDocumentEntry)
            _model = new WorkflowDiagnosticsModel(_session);
        else if (_session is not null && !string.IsNullOrWhiteSpace(_entryNodeId))
            _model = new WorkflowDiagnosticsModel(_session, _entryNodeId!, _provider, _navigator);
        if (_model is not null) _model.Changed += OnModelChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeModel()
    {
        if (_model is null) return;
        _model.Changed -= OnModelChanged;
        _model.Dispose();
        _model = null;
    }

    private void OnModelChanged(object? sender, EventArgs e) => OnUiThread(() => Changed?.Invoke(this, EventArgs.Empty));

    private void OnProviderChanged(object? sender, EventArgs e) => OnUiThread(RefreshDiagnostics);

    private void OnUiThread(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context) action();
        else _context.Post(_ => action(), null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_provider != null) _provider.Changed -= OnProviderChanged;
        DisposeModel();
    }
}
