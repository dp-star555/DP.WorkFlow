using DP.LabelInspection.Contracts;
using DP.LabelInspection.Storage;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.LabelInspection.UI;

/// <summary>UI无关配方编辑桥，只修改隔离节点；平台控件提供捕获与异步释放回调。</summary>
public sealed class LabelInspectionEditorPageModel : IWorkflowNodeEditorCommitParticipant, IWorkflowNodeEditorCommitReadiness, IAsyncDisposable
{
    private readonly Action _changed;
    private Func<InspectionRecipe?>? _capture;
    private Func<bool>? _busy;
    private Func<ValueTask>? _release;
    private bool _disposed;
    /// <summary>创建页面草稿；node必须是Workflow提供的EditingNode。</summary>
    public LabelInspectionEditorPageModel(InspectLabelNodeModel node, IImageCodec codec, Action changed)
    { Node = node; Serializer = new InspectionRecipeSerializer(codec); _changed = changed; }
    /// <summary>隔离节点配置。</summary>
    public InspectLabelNodeModel Node { get; }
    /// <summary>完整原生配方桥，保留私有setter的Tasks等配置。</summary>
    public InspectionRecipeSerializer Serializer { get; }
    /// <summary>导入配方，不依赖运行预览；不会改上游输入或资源引用。</summary>
    public void ImportRecipe(string json) => SetRecipe(Serializer.Deserialize(json));
    /// <summary>写入草稿中的配方快照。</summary>
    public void SetRecipe(InspectionRecipe recipe)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var json = Serializer.Serialize(recipe);
        if (Node.RecipeJson == json) return;
        Node.RecipeJson = json; _changed();
    }
    /// <summary>控件注册确认前捕获、忙状态及关闭前停止原生工作的桥。</summary>
    public void AttachWorkbench(Func<InspectionRecipe?> capture, Func<bool> busy, Func<ValueTask> release)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_release is not null) throw new InvalidOperationException("此页面已经绑定工作台。");
        _capture = capture; _busy = busy; _release = release;
    }
    /// <inheritdoc/>
    public bool CanCommit => !_disposed && !(_busy?.Invoke() ?? false);
    /// <inheritdoc/>
    public string CommitBlockReason => CanCommit ? string.Empty : "请等待标签资源装配/试检测结束后确认。";
    /// <inheritdoc/>
    public void PrepareCommit()
    {
        if (!CanCommit) throw new InvalidOperationException(CommitBlockReason);
        if (_capture?.Invoke() is { } recipe) SetRecipe(recipe);
        _ = Serializer.Deserialize(Node.RecipeJson);
    }
    /// <summary>取消/关闭只停止与释放；不会捕获或回写控件草稿。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_release is not null) await _release();
    }
}

/// <summary>标签节点的专用工作台页面，不贡献通用视觉ROI编辑页。</summary>
public sealed class LabelInspectionEditorPageProvider(IImageCodec codec, IWorkflowVisionPreviewSource? previews = null) : IWorkflowNodeEditorPageProvider
{
    /// <summary>平台Renderer的稳定键。</summary>
    public const string RendererKey = "Workflow.LabelInspection.Workbench";
    /// <inheritdoc/>
    public string ExtensionId => RendererKey;
    /// <inheritdoc/>
    public bool CanProvide(WorkflowNodeEditorContext context) => context.Node is InspectLabelNodeModel && context.RequestedPropertyEditor is null;
    /// <inheritdoc/>
    public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        if (!CanProvide(context)) yield break;
        yield return new("LabelInspection", "标签配置与试检测", WorkflowNodeEditorPageKind.Custom, 450,
            new LabelInspectionEditorPageModel((InspectLabelNodeModel)context.Node, codec, context.Session.NotifyNodeConfigurationChanged),
            IconKey: "Image", RendererKey: RendererKey);
        yield return new("LabelReport", "标签检测报告", WorkflowNodeEditorPageKind.Custom, 850,
            new LabelInspectionResultPageModel(context.Node.Id, previews, context.RuntimeSession), IconKey: "Results", RendererKey: LabelInspectionResultPageModel.RendererKey);
    }
}

/// <summary>只读报告页只捕获本节点已提交且同帧的预览，不读取配置页试检测状态。</summary>
public sealed class LabelInspectionResultPageModel(string nodeId, IWorkflowVisionPreviewSource? previews, WorkflowDesignerSession? runtimeSession = null) : IDisposable
{
    /// <summary>报告页Renderer稳定键。</summary>
    public const string RendererKey = "Workflow.LabelInspection.Report";
    private Action? _release;
    /// <summary>捕获独立图像租约；输出失效时返回空，调用方释放。</summary>
    public WorkflowVisionPreview? Capture()
    {
        var preview = previews?.Capture(nodeId);
        if (preview is not null && runtimeSession is not null && !ReferenceEquals(runtimeSession.GetLatestNodeOutput(nodeId)?.Value, preview.Facts))
        { preview.Dispose(); return null; }
        return preview;
    }
    /// <summary>登记UI线程视图清理。</summary>
    public void RegisterViewLifetime(Action release) => _release = release;
    /// <inheritdoc/>
    public void Dispose() { _release?.Invoke(); _release = null; }
}
