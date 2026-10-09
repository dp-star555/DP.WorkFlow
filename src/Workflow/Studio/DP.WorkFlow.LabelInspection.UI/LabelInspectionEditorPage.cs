using DP.LabelInspection.Contracts;
using DP.LabelInspection.Storage;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.LabelInspection.UI;

/// <summary>参数页上的一个标签配置操作按钮（由平台工作台控件执行）。</summary>
/// <param name="Id">稳定操作标识。</param><param name="Label">属性名。</param><param name="Category">参数页分组。</param>
/// <param name="Caption">按钮文字。</param><param name="Description">操作说明。</param>
public sealed record LabelInspectionEditorCommand(string Id, string Label, string Category, string Caption, string Description);

/// <summary>UI无关配方编辑桥，只修改隔离节点；平台控件提供捕获与异步释放回调。</summary>
public sealed class LabelInspectionEditorPageModel : IWorkflowNodeEditorCommitParticipant, IWorkflowNodeEditorCommitReadiness,
    IWorkflowNodeEditorPropertyContributor, IAsyncDisposable
{
    private readonly Action _changed;
    private IReadOnlyList<LabelInspectionEditorCommand> _commands = Array.Empty<LabelInspectionEditorCommand>();
    private Func<string, Task>? _execute;
    private Func<string, string>? _commandBlockReason;
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
    /// <summary>本节点最近一次正式运行的结果来源；配置页据此显示运行图像与报告。没有运行会话时为空。</summary>
    public LabelInspectionResultPageModel? RunResults { get; init; }

    /// <summary>平台控件连接后，把这些操作作为按钮追加到同一窗口的参数页。</summary>
    /// <param name="commands">操作列表。</param><param name="execute">执行操作（异常由参数页显示）。</param>
    /// <param name="blockReason">操作当前不可执行的原因；空文本表示可执行。</param>
    public void AttachCommands(IReadOnlyList<LabelInspectionEditorCommand> commands, Func<string, Task> execute, Func<string, string> blockReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _commands = commands; _execute = execute; _commandBlockReason = blockReason;
    }

    /// <inheritdoc/>
    public IEnumerable<WorkflowPropertyEntry> CreateProperties(IWorkflowNodeModel editingNode)
    {
        if (_disposed || _execute is not { } execute || editingNode.Id != Node.Id) yield break;
        foreach (var command in _commands)
        {
            var id = command.Id;
            yield return WorkflowPropertyEntry.CreateAction("LabelInspection.Command." + id, command.Label, command.Category, command.Description,
                () => command.Caption, () => execute(id), () => _disposed ? "配置页已关闭。" : _commandBlockReason?.Invoke(id) ?? "");
        }
    }

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
    /// <summary>载入外部版本到隔离编辑草稿，不改变生产的目录选择输入；确认也不自动发布文件。</summary>
    /// <param name="profile">已校验的配方版本。</param>
    public void LoadProfile(WorkflowLabelRecipeProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var recipe = Serializer.Deserialize(profile.RecipeJson);
        profile.ApplyTo(Node); Node.RecipeJson = Serializer.Serialize(recipe); _changed();
    }
    /// <summary>记录目录索引路径；未绑定且选择为空时可初始化为新发布的ID。</summary>
    /// <param name="relativePath">根内索引路径。</param><param name="initialKey">可选初始选择。</param>
    public void SetRecipeCatalog(string relativePath, string? initialKey = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Node.RecipeCatalogPath = relativePath;
        if (initialKey is not null && Node.RecipeKey.Source == WorkflowValueSource.Literal && string.IsNullOrWhiteSpace(Node.RecipeKey.LiteralValue))
            Node.RecipeKey = WorkflowInput<string>.FromLiteral(initialKey);
        _changed();
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

    /// <summary>记录参考位姿（配置帧坐标系的局部→原图），之后在这一帧原图上画的ROI随定位移动；并通知属性页刷新。</summary>
    /// <param name="pose">配置帧坐标系的局部→原图矩阵。</param>
    public void SetReferencePose(DP.Vision.Algorithms.CoordinateMatrix2D pose)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Node.GetReferencePose() is { } current && current.M11 == pose.M11 && current.M12 == pose.M12 && current.Tx == pose.Tx
            && current.M21 == pose.M21 && current.M22 == pose.M22 && current.Ty == pose.Ty) return;
        Node.SetReferencePose(pose); _changed();
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
        // 还没有配方（新建节点只改了参数）时允许提交，由节点校验提示“请创建/导入配方”。
        if (string.IsNullOrWhiteSpace(Node.RecipeJson)) return;
        // 配方无效转为参数校验错误，由节点窗口提示，而不是未处理异常。
        try { _ = Serializer.Deserialize(Node.RecipeJson); }
        catch (Exception error) when (error is not OperationCanceledException and not InvalidOperationException)
        { throw new InvalidOperationException("标签配方无效：" + error.Message, error); }
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
/// <param name="codec">配方中参考图像编码。</param><param name="previews">已提交的图像预览来源。</param>
/// <param name="includeReportPage">是否提供独立的只读报告页；配置页能显示运行结果的平台传false。</param>
public sealed class LabelInspectionEditorPageProvider(IImageCodec codec, IWorkflowVisionPreviewSource? previews = null, bool includeReportPage = true) : IWorkflowNodeEditorPageProvider
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
            new LabelInspectionEditorPageModel((InspectLabelNodeModel)context.Node, codec, context.Session.NotifyNodeConfigurationChanged)
            { RunResults = new LabelInspectionResultPageModel(context.Node.Id, previews, context.RuntimeSession) },
            IconKey: "Image", RendererKey: RendererKey);
        // WinForms在配置页图像下方显示运行结果；没有原生工作台的平台（WPF）使用独立只读报告页。
        if (includeReportPage)
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
