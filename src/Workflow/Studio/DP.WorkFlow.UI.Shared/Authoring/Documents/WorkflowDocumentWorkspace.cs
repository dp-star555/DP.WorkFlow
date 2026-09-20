using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.UI;

/// <summary>管理新建、打开、保存、脏状态和最近文件，并创建对应设计导航器。</summary>
public sealed class WorkflowDocumentWorkspace : IDisposable
{
    private readonly WorkflowNodeCatalog _catalog;
    private readonly WorkflowDocumentJsonStore _store;
    private readonly List<string> _recentFiles = new();
    private WorkflowDesignerSession? _subscribedSession;
    private WorkflowDesignerNavigator? _navigator;
    private bool _disposed;

    /// <summary>初始化文档工作区并使用指定节点目录和持久化存储。</summary>
    /// <param name="catalog">用于创建和查询节点的节点目录。</param>
    /// <param name="store">可选的画布 JSON 存储服务。</param>
    public WorkflowDocumentWorkspace(WorkflowNodeCatalog catalog, WorkflowDocumentJsonStore? store = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? new WorkflowDocumentJsonStore(catalog);
    }

    public WorkflowDesignerNavigator? Navigator => _navigator;

    /// <summary>获取当前文档的绝对文件路径；尚未保存时为空。</summary>
    public string? CurrentFilePath { get; private set; }

    /// <summary>获取当前文档是否包含尚未保存的修改。</summary>
    public bool IsDirty { get; private set; }

    /// <summary>获取最近一次打开旧格式文档时生成的迁移报告。</summary>
    public WorkflowMigrationReport? LastMigrationReport { get; private set; }

    /// <summary>获取按最近使用时间倒序排列的文件路径快照。</summary>
    public IReadOnlyList<string> RecentFiles => _recentFiles.ToArray();

    /// <summary>在当前文档或导航内容发生变化时发生。</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>在文档未保存状态变化时发生。</summary>
    public event EventHandler? DirtyStateChanged;

    /// <summary>创建空白文档，并添加一个 Start 节点。</summary>
    /// <param name="name">名称。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public WorkflowDesignerNavigator New(string name = "未命名流程")
    {
        ThrowIfDisposed();
        var descriptor = _catalog.GetOrThrow("Start");
        var start = descriptor.Factory();
        start.Id = "Start";
        start.Title = descriptor.DisplayName ?? "Start";
        var document = new WorkflowDocument { Name = name, EntryNodeId = start.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = start, X = 80, Y = 80 });
        SetDocument(new WorkflowDesignerNavigator(document, _catalog), null, null, true);
        return _navigator!;
    }

    /// <summary>打开当前或旧版 JSON 文档。</summary>
    /// <param name="filePath">工作流文档文件路径。</param>
    /// <param name="startNodeId">工作流开始节点标识。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public WorkflowDesignerNavigator Open(string filePath, string? startNodeId = null)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空。", nameof(filePath));
        var fullPath = Path.GetFullPath(filePath);
        var loaded = _store.LoadFromFile(fullPath);
        if (!string.IsNullOrWhiteSpace(startNodeId))
            loaded.Document.EntryNodeId = startNodeId;
        SetDocument(
            new WorkflowDesignerNavigator(loaded.Document, _catalog),
            fullPath,
            loaded.Migration,
            false);
        AddRecentFile(fullPath);
        return _navigator!;
    }

    /// <summary>保存到当前路径。</summary>
    public void Save()
    {
        ThrowIfDisposed();
        if (_navigator is null)
            throw new InvalidOperationException("当前没有可保存的文档。");
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
            throw new InvalidOperationException("当前文档尚未指定保存路径，请调用 SaveAs。");
        _store.SaveToFile(_navigator.RootDocument, CurrentFilePath);
        SetDirty(false);
        AddRecentFile(CurrentFilePath);
    }

    /// <summary>保存到新路径。</summary>
    /// <param name="filePath">工作流文档文件路径。</param>
    public void SaveAs(string filePath)
    {
        ThrowIfDisposed();
        if (_navigator is null)
            throw new InvalidOperationException("当前没有可保存的文档。");
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空。", nameof(filePath));
        CurrentFilePath = Path.GetFullPath(filePath);
        Save();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>解除事件订阅并释放当前模型持有的资源。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DetachNavigator();
    }

    /// <summary>替换当前文档、导航器及文件元数据。</summary>
    /// <param name="navigator">要绑定的设计器导航器。</param>
    /// <param name="filePath">工作流文档文件路径。</param>
    /// <param name="migrationReport">文档反序列化时生成的迁移报告。</param>
    /// <param name="dirty">新的文档未保存状态。</param>
    private void SetDocument(
        WorkflowDesignerNavigator navigator,
        string? filePath,
        WorkflowMigrationReport? migrationReport,
        bool dirty)
    {
        DetachNavigator();
        _navigator = navigator;
        CurrentFilePath = filePath;
        LastMigrationReport = migrationReport;
        _navigator.CurrentChanged += OnNavigatorChanged;
        AttachSession(_navigator.CurrentSession);
        SetDirty(dirty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>处理导航层级变化并转发工作区变更事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnNavigatorChanged(object? sender, EventArgs e)
    {
        if (_navigator is not null)
            AttachSession(_navigator.CurrentSession);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>订阅设计会话变更以跟踪文档脏状态。</summary>
    /// <param name="session">设计器会话。</param>
    private void AttachSession(WorkflowDesignerSession session)
    {
        if (_subscribedSession is not null)
            _subscribedSession.Changed -= OnSessionChanged;
        _subscribedSession = session;
        _subscribedSession.Changed += OnSessionChanged;
    }

    /// <summary>解除当前导航器和会话的事件订阅。</summary>
    private void DetachNavigator()
    {
        if (_subscribedSession is not null)
            _subscribedSession.Changed -= OnSessionChanged;
        _subscribedSession = null;
        if (_navigator is not null)
            _navigator.CurrentChanged -= OnNavigatorChanged;
        _navigator = null;
    }

    /// <summary>处理设计会话变更，并同步刷新派生模型。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind == WorkflowDesignerChangeKind.Document)
            SetDirty(true);
    }

    /// <summary>更新当前文档的未保存状态。</summary>
    /// <param name="dirty">新的文档未保存状态。</param>
    private void SetDirty(bool dirty)
    {
        if (IsDirty == dirty)
            return;
        IsDirty = dirty;
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>将文件路径移到最近文件列表首位并限制列表长度。</summary>
    /// <param name="filePath">工作流文档文件路径。</param>
    private void AddRecentFile(string filePath)
    {
        _recentFiles.RemoveAll(path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
        _recentFiles.Insert(0, filePath);
        if (_recentFiles.Count > 10)
            _recentFiles.RemoveRange(10, _recentFiles.Count - 10);
    }

    /// <summary>对象已经释放时抛出异常。</summary>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
