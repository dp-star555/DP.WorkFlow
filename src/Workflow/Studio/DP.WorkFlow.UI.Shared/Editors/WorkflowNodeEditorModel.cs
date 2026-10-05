namespace DP.WorkFlow.UI;

/// <summary>统一节点工作台中的页面类型。</summary>
public enum WorkflowNodeEditorPageKind
{
    /// <summary>通用反射属性编辑页。</summary>
    Properties,
    /// <summary>复合节点的子工作流设计页。</summary>
    SubWorkflow,
    /// <summary>C# 脚本编辑与编译页。</summary>
    Script,
    /// <summary>节点或子工作流诊断页。</summary>
    Diagnostics,
    /// <summary>由宿主页面提供器定义的扩展页。</summary>
    Custom,
    /// <summary>节点最近一次运行状态与输出的只读结果页。</summary>
    Results
}

/// <summary>描述一个与具体桌面 UI 技术无关的节点编辑页面。</summary>
/// <param name="PageId">页面稳定标识。</param>
/// <param name="Title">界面标题。</param>
/// <param name="Kind">类型枚举值。</param>
/// <param name="Order">页面排序值。</param>
/// <param name="Model">页面对应的 UI 无关模型。</param>
/// <param name="IconKey">可选的页面图标资源键。</param>
/// <param name="RendererKey">平台渲染器稳定键；为空时由内置页面类型决定。</param>
/// <param name="Priority">同一页面槽位的替换优先级；它与显示顺序相互独立。</param>
/// <param name="PropertyEditorKey">独立属性编辑器稳定键；该页面不平铺在普通节点窗口中。</param>
public sealed record WorkflowNodeEditorPageDescriptor(
    string PageId,
    string Title,
    WorkflowNodeEditorPageKind Kind,
    int Order,
    object Model,
    string? IconKey = null,
    string? RendererKey = null,
    int Priority = 0,
    string? PropertyEditorKey = null);

/// <summary>节点页面提供器使用的上下文。</summary>
/// <param name="Session">关联的设计器会话。</param>
/// <param name="EntryNodeId">工作流开始节点标识。</param>
/// <param name="Node">关联的工作流节点。</param>
public sealed record WorkflowNodeEditorContext(
    WorkflowDesignerSession Session,
    string EntryNodeId,
    IWorkflowNodeModel Node)
{
    /// <summary>宿主为隔离编辑副本提供的候选值。</summary>
    public WorkflowPropertyChoiceProvider? ChoiceProvider { get; init; }
    /// <summary>宿主提供的领域属性；回调接收编辑副本。</summary>
    public Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? AdditionalProperties { get; init; }

    /// <summary>当前请求的独立属性编辑器；普通节点窗口为空。</summary>
    public string? RequestedPropertyEditor { get; init; }
    /// <summary>接收运行快照的原始设计会话；隔离编辑副本本身没有运行状态。</summary>
    public WorkflowDesignerSession? RuntimeSession { get; init; }
}

/// <summary>扩展节点工作台页面；实现不得返回 WinForms/WPF 控件。</summary>
public interface IWorkflowNodeEditorPageProvider
{
    /// <summary>获取用于插件注册冲突诊断的稳定扩展标识。</summary>
    string ExtensionId => GetType().FullName
        ?? throw new InvalidOperationException("节点页面提供器必须具有稳定类型名称。");

    /// <summary>判断当前提供器能否为指定节点创建页面。</summary>
    /// <param name="context">节点编辑器上下文。</param>
    /// <returns>可以提供页面时返回 <see langword="true"/>。</returns>
    bool CanProvide(WorkflowNodeEditorContext context);

    /// <summary>创建与具体桌面 UI 技术无关的页面描述集合。</summary>
    /// <param name="context">节点编辑器上下文。</param>
    /// <returns>按需要由调用方排序的页面描述。</returns>
    IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context);
}

/// <summary>节点页面的应用准备；只操作隔离编辑副本，失败阻止正式提交。</summary>
public interface IWorkflowNodeEditorCommitParticipant
{
    /// <summary>发布页面资源并更新隔离副本，不能修改正式节点。</summary>
    void PrepareCommit();
}

/// <summary>专用编辑页面的确认条件；统一窗口据此更新应用和确定按钮。</summary>
public interface IWorkflowNodeEditorCommitReadiness
{
    /// <summary>是否可以确认当前草稿。</summary>
    bool CanCommit { get; }
    /// <summary>不能确认的原因。</summary>
    string CommitBlockReason { get; }
}

/// <summary>参数页面上下文。</summary>
/// <param name="Session">关联的设计器会话。</param>
/// <param name="EntryNodeId">工作流开始节点标识。</param>
public sealed record WorkflowPropertyEditorPageModel(WorkflowDesignerSession Session, string EntryNodeId)
{
    /// <summary>属性页候选值来源。</summary>
    public WorkflowPropertyChoiceProvider? ChoiceProvider { get; init; }
    /// <summary>属性页领域扩展。</summary>
    public Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? AdditionalProperties { get; init; }
}

/// <summary>嵌入式子流程页面上下文。</summary>
public sealed class WorkflowSubWorkflowEditorPageModel : IDisposable
{
    /// <summary>初始化子工作流编辑页并创建子画布设计会话。</summary>
    /// <param name="parentSession">父工作流设计会话。</param>
    /// <param name="node">目标画布节点或节点模型。</param>
    public WorkflowSubWorkflowEditorPageModel(WorkflowDesignerSession parentSession, IWorkflowSubDocumentNode node)
    {
        ParentSession = parentSession;
        Node = node;
        Session = new WorkflowDesignerSession(node.SubDocument, parentSession.Catalog, parentSession.PublicDataCatalog);
        Session.Changed += OnChildSessionChanged;
        EntryNodeId = ResolveEntryNodeId();
    }

    /// <summary>获取父工作流设计会话。</summary>
    public WorkflowDesignerSession ParentSession { get; }
    /// <summary>获取正在编辑的复合节点。</summary>
    public IWorkflowSubDocumentNode Node { get; }
    /// <summary>获取子工作流使用的独立设计会话。</summary>
    public WorkflowDesignerSession Session { get; }
    /// <summary>获取子工作流开始节点标识。</summary>
    public string EntryNodeId { get; }

    /// <summary>在子画布修改后通知父会话节点配置已经变化。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">设计会话变更参数。</param>
    private void OnChildSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind == WorkflowDesignerChangeKind.Document)
            ParentSession.NotifyNodeConfigurationChanged();
    }

    /// <summary>解除事件订阅并释放当前模型持有的资源。</summary>
    public void Dispose() => Session.Changed -= OnChildSessionChanged;

    /// <summary>只读解析子文档入口；打开编辑页不得修改节点配置。</summary>
    private string ResolveEntryNodeId()
    {
        if (!string.IsNullOrWhiteSpace(Node.SubDocument.EntryNodeId))
            return Node.SubDocument.EntryNodeId;
        var starts = Node.SubDocument.Graph.Nodes
            .Where(node => node.NodeType == "Start")
            .Select(node => node.Id)
            .ToArray();
        return starts.Length == 1 ? starts[0] : string.Empty;
    }
}

/// <summary>脚本页面模型，负责提交正文并产生 Roslyn 诊断。</summary>
public sealed class WorkflowScriptEditorPageModel
{
    private string? _compiledScript;

    private WorkflowScriptEditorPageModel(IWorkflowScriptNode node) => Node = node;

    /// <summary>初始化脚本编辑页并记录节点当前脚本。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="node">目标画布节点或节点模型。</param>
    public WorkflowScriptEditorPageModel(WorkflowDesignerSession session, IWorkflowScriptNode node)
    {
        ArgumentNullException.ThrowIfNull(session);
        Node = node;
    }

    /// <summary>
    /// 为普通字符串属性创建事务式脚本缓冲区。调用方只在对话框确认后读取 <see cref="Script"/> 并提交，
    /// 因而可以复用与脚本节点完全相同的工具栏、编辑区和诊断区。
    /// </summary>
    public static WorkflowScriptEditorPageModel CreateBuffer(string? script) => new(
        new BufferedScriptNode { Script = WorkflowCSharpScriptEditorModel.EnsureProgramSource(script) });

    /// <summary>获取当前页面或模型关联的节点。</summary>
    public IWorkflowScriptNode Node { get; }
    public string Language => Node.ScriptLanguage;
    public string Script => Node.Script;
    /// <summary>当前模型是否能够持久化外部 DLL 引用。</summary>
    public bool SupportsReferenceManagement => Node is IWorkflowScriptReferenceNode;
    /// <summary>获取当前脚本显式引用的 DLL 路径。</summary>
    public IReadOnlyList<string> ReferencePaths => Node is IWorkflowScriptReferenceNode references
        ? references.ScriptReferencePaths.ToArray()
        : Array.Empty<string>();
    /// <summary>获取当前脚本文本是否已经执行过编译。</summary>
    public bool IsCurrentScriptCompiled => string.Equals(_compiledScript, Node.Script, StringComparison.Ordinal);

    /// <summary>更新节点脚本文本并通知设计会话。</summary>
    /// <param name="script">脚本文本。</param>
    public void SetScript(string script)
    {
        if (Node.Script == script) return;
        Node.Script = script;
    }

    /// <summary>替换脚本 DLL 引用并使当前编译状态失效。</summary>
    public void SetReferencePaths(IEnumerable<string> paths)
    {
        if (Node is not IWorkflowScriptReferenceNode references) return;
        var normalized = paths.Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (references.ScriptReferencePaths.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase)) return;
        references.ScriptReferencePaths = normalized.ToList();
        _compiledScript = null;
    }

    /// <summary>编译指定文本或节点当前脚本，并返回诊断。</summary>
    /// <param name="script">脚本文本。</param>
    public IReadOnlyList<string> Compile(string? script = null)
    {
        var source = script ?? Node.Script;
        SetScript(source);
        if (!Language.Equals("CSharp", StringComparison.OrdinalIgnoreCase))
        {
            RecordCompilation(source, succeeded: true);
            return Array.Empty<string>();
        }
        var diagnosticItems = WorkflowCSharpScriptEditorModel.GetDiagnosticItems(
            source,
            ReferencePaths,
            Node.GetType().Assembly);
        RecordCompilation(
            source,
            !diagnosticItems.Any(item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return diagnosticItems.Select(WorkflowCSharpScriptEditorModel.FormatDiagnostic).ToArray();
    }

    /// <summary>记录一次编译的源代码和成功状态。</summary>
    /// <param name="script">脚本文本。</param>
    /// <param name="succeeded">编译是否成功。</param>
    public void RecordCompilation(string script, bool succeeded)
    {
        SetScript(script);
        _compiledScript = succeeded ? script : null;
    }

    /// <summary>编译脚本并返回适合界面显示的诊断文本。</summary>
    /// <param name="script">脚本文本。</param>
    public IReadOnlyList<string> GetDiagnostics(string? script = null) =>
        Language.Equals("CSharp", StringComparison.OrdinalIgnoreCase)
            ? WorkflowCSharpScriptEditorModel.GetDiagnostics(
                script ?? Node.Script,
                ReferencePaths,
                Node.GetType().Assembly)
            : Array.Empty<string>();

    /// <summary>普通脚本属性使用的内存节点；不暴露无法持久化的 DLL 引用能力。</summary>
    private sealed class BufferedScriptNode : WorkflowNodeModel, IWorkflowScriptNode
    {
        public override string NodeType => "BufferedCSharpScript";
        public string ScriptId { get; set; } = Guid.NewGuid().ToString("N");
        public string Script { get; set; } = string.Empty;
        public string ScriptLanguage => "CSharp";
    }
}

/// <summary>统一构建参数、子流程、脚本、诊断及宿主扩展页面。</summary>
public sealed class WorkflowNodeEditorModel : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _resources = new();
    private readonly List<IDisposable> _disposables = new();
    private readonly IWorkflowNodeEditorPageProvider[] _providers;
    private readonly WorkflowPropertyChoiceProvider? _choiceProvider;
    private readonly Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? _additionalProperties;
    private bool _disposed;

    /// <summary>为指定节点创建隔离编辑副本并聚合可用编辑页面。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="startNodeId">工作流开始节点标识。</param>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="providers">自定义编辑页提供者集合。</param>
    /// <param name="propertyEditorKey">独立属性编辑器稳定键；普通节点窗口为空。</param>
    public WorkflowNodeEditorModel(
        WorkflowDesignerSession session,
        string startNodeId,
        string nodeId,
        IEnumerable<IWorkflowNodeEditorPageProvider>? providers = null, string? propertyEditorKey = null)
        : this(session, startNodeId, nodeId, providers, null, null, propertyEditorKey) { }

    /// <summary>创建带宿主属性扩展的隔离节点编辑器，沿用应用、取消及撤销语义。</summary>
    public WorkflowNodeEditorModel(WorkflowDesignerSession session, string startNodeId, string nodeId,
        IEnumerable<IWorkflowNodeEditorPageProvider>? providers, WorkflowPropertyChoiceProvider? choiceProvider,
        Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? additionalProperties, string? propertyEditorKey = null)
    {
        _providers = providers?.ToArray() ?? [];
        _choiceProvider = choiceProvider; _additionalProperties = additionalProperties;
        PropertyEditorKey = propertyEditorKey;
        Session = session ?? throw new ArgumentNullException(nameof(session));
        EntryNodeId = startNodeId ?? throw new ArgumentNullException(nameof(startNodeId));
        Node = session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == nodeId)?.Node
            ?? throw new InvalidOperationException($"节点 {nodeId} 不存在。");
        session.SelectedNodeId = nodeId;
        (EditingSession, EditingNode) = CreateEditingSession(session, nodeId);
        var context = new WorkflowNodeEditorContext(EditingSession, startNodeId, EditingNode)
        {
            ChoiceProvider = choiceProvider, AdditionalProperties = additionalProperties,
            RequestedPropertyEditor = propertyEditorKey, RuntimeSession = session
        };
        var pageCatalog = WorkflowNodeEditorPageCatalog.CreateDefault();
        foreach (var provider in _providers)
            pageCatalog.Register(provider);
        Pages = pageCatalog.CreatePages(context);
        foreach (var resource in Pages.Select(page => page.Model).OfType<IAsyncDisposable>().Distinct())
            _resources.Add(resource);
        foreach (var disposable in Pages.Select(page => page.Model).OfType<IDisposable>().Distinct()
                     .Where(item => item is not IAsyncDisposable))
            _disposables.Add(disposable);
    }

    /// <summary>将编辑副本中的属性和子模型修改提交到原始节点。</summary>
    public WorkflowDesignerSession Session { get; }
    /// <summary>获取用于暂存节点修改的隔离设计会话。</summary>
    public WorkflowDesignerSession EditingSession { get; }
    /// <summary>获取或设置当前工作流的开始节点标识。</summary>
    public string EntryNodeId { get; }
    /// <summary>获取当前页面或模型关联的节点。</summary>
    public IWorkflowNodeModel Node { get; }
    /// <summary>获取隔离会话中的节点编辑副本。</summary>
    public IWorkflowNodeModel EditingNode { get; }
    /// <summary>获取已经按显示顺序排列的节点编辑页面。</summary>
    public IReadOnlyList<WorkflowNodeEditorPageDescriptor> Pages { get; }

    /// <summary>独立属性编辑窗口的稳定键；空值表示普通节点窗口。</summary>
    public string? PropertyEditorKey { get; }
    /// <summary>扩展页面提供的确认条件。</summary>
    public bool CanApplyChanges => !_disposed && Pages.Select(p => p.Model).OfType<IWorkflowNodeEditorCommitReadiness>().All(p => p.CanCommit);

    /// <summary>从当前编辑副本打开另一层隔离草稿；应用时写回父草稿并直接提交到正式节点。</summary>
    public WorkflowNodeEditorModel CreatePropertyEditor(string key) => new(EditingSession, EntryNodeId, EditingNode.Id,
        _providers, _choiceProvider, _additionalProperties, key) { _parent = this };

    // 从节点窗口打开的独立属性窗口（如模板制作/选择）：应用时先写回节点窗口的编辑副本，再直接提交到正式节点，
    // 不需要再点节点窗口的“应用”；节点窗口的属性面板随编辑会话变化自动刷新。
    private WorkflowNodeEditorModel? _parent;

    /// <summary>将编辑副本中的参数作为一个整体提交到正式节点。</summary>
    public void ApplyChanges()
    {
        var uncompiledScript = Pages.Select(page => page.Model).OfType<WorkflowScriptEditorPageModel>()
            .Distinct().FirstOrDefault(page => !page.IsCurrentScriptCompiled);
        if (uncompiledScript is not null)
            throw new InvalidOperationException("保存前必须点击“编译”，并确保当前 C# 代码编译通过。");

        foreach (var participant in Pages.Select(page => page.Model).OfType<IWorkflowNodeEditorCommitParticipant>().Distinct())
            participant.PrepareCommit();

        var editedSnapshot = WorkflowNodeConfigurationSnapshotter.Capture(EditingNode);
        var sourceCanvasNode = EditingSession.Canvas.Nodes.First(item => item.Node.Id == EditingNode.Id);
        var newHiddenPorts = sourceCanvasNode.HiddenOutputPorts.ToArray();
        Session.ExecuteNodeConfigurationChange(
            Node.Id,
            target => WorkflowNodeConfigurationSnapshotter.Restore(target, editedSnapshot),
            newHiddenPorts);
        _parent?.ApplyChanges();
    }

    /// <summary>创建包含目标节点浅复制的隔离编辑会话，避免确认前修改原始节点。</summary>
    /// <param name="source">原始设计会话。</param>
    /// <param name="nodeId">需要复制到编辑会话的节点标识。</param>
    /// <returns>返回隔离设计会话及其中的节点编辑副本。</returns>
    private static (WorkflowDesignerSession Session, IWorkflowNodeModel Node) CreateEditingSession(
        WorkflowDesignerSession source,
        string nodeId)
    {
        var sourceNode = source.Canvas.Nodes.First(item => item.Node.Id == nodeId);
        var editingNode = WorkflowNodeConfigurationSnapshotter.Capture(sourceNode.Node);
        var document = new WorkflowDocument
        {
            Name = source.Document.Name,
            EntryNodeId = source.Document.EntryNodeId
        };
        var canvas = document.CanvasProjection;
        foreach (var item in source.Canvas.Nodes)
        {
            var copy = new WorkflowCanvasNode
            {
                Node = item.Node.Id == nodeId ? editingNode : item.Node,
                X = item.X, Y = item.Y, Width = item.Width, Height = item.Height
            };
            foreach (var pair in item.PortSides) copy.PortSides[pair.Key] = pair.Value;
            foreach (var key in item.HiddenOutputPorts) copy.HiddenOutputPorts.Add(key);
            canvas.Nodes.Add(copy);
        }
        foreach (var connection in source.Canvas.Connections) canvas.Connections.Add(connection);
        var editingSession = new WorkflowDesignerSession(document, source.Catalog, source.PublicDataCatalog) { SnapToGrid = source.SnapToGrid };
        editingSession.SelectedNodeId = nodeId;
        return (editingSession, editingNode);
    }

    /// <summary>异步停止外部资源并解除事件订阅。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var disposable in _disposables.AsEnumerable().Reverse()) disposable.Dispose();
        foreach (var resource in _resources.AsEnumerable().Reverse())
            await resource.DisposeAsync().ConfigureAwait(false);
    }
}
