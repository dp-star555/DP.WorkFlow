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
    /// <summary>记录配置样张路径（相对资源根目录），并通知属性页刷新。</summary>
    /// <param name="relativePath">相对资源根目录的路径。</param>
    public void SetAuthorImagePath(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Node.AuthorImagePath == relativePath) return;
        Node.AuthorImagePath = relativePath; _changed();
    }

    /// <summary>记录参考图路径（相对资源根目录），并通知属性页刷新。</summary>
    /// <param name="relativePath">相对资源根目录的路径。</param>
    public void SetReferenceImagePath(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Node.ReferenceImagePath == relativePath) return;
        Node.ReferenceImagePath = relativePath; _changed();
    }

    /// <summary>
    /// 把资源根目录外的配置样张复制到根目录下的 <c>samples</c> 子目录，返回相对根目录的路径；已在根目录内时直接返回相对路径。
    /// 同名文件内容相同则复用，不同则追加序号，不覆盖已有文件。
    /// </summary>
    /// <param name="root">资源根目录（完整路径）。</param><param name="path">选中的图像文件。</param>
    public static string ImportIntoRoot(string root, string path)
    {
        root = Path.GetFullPath(root); path = Path.GetFullPath(path);
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Path.GetRelativePath(root, path);
        var folder = Path.Combine(root, "samples");
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(path); var extension = Path.GetExtension(path);
        for (int index = 0; ; index++)
        {
            var target = Path.Combine(folder, index == 0 ? name + extension : $"{name}-{index}{extension}");
            if (!File.Exists(target)) { File.Copy(path, target); return Path.GetRelativePath(root, target); }
            if (SameContent(target, path)) return Path.GetRelativePath(root, target);
        }
    }

    private static bool SameContent(string a, string b)
    {
        var left = new FileInfo(a); var right = new FileInfo(b);
        if (left.Length != right.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
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
