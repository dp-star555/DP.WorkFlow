using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.Vision;
using DP.WorkFlow.LabelInspection.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.LabelInspection.UI.WinForms;

/// <summary>标签配置工作台与只读报告视图的WinForms组合入口。</summary>
public sealed class LabelInspectionWinFormsExtension : IWorkflowWinFormsStudioExtension
{
    /// <summary>相对资源根的流程文件目录来源。</summary>
    public Func<string> BaseDirectory { get; init; } = () => AppContext.BaseDirectory;
    /// <summary>已经提交的图像预览来源。</summary>
    public IWorkflowVisionPreviewSource? FrameSource { get; init; }
    /// <inheritdoc/>
    public string ExtensionId => "workflow.label-inspection.winforms";
    /// <inheritdoc/>
    public void Register(WorkflowWinFormsStudioExtensionCatalog extensions)
    {
        extensions.RegisterPageProvider(new LabelInspectionEditorPageProvider(new OpenCvImageCodec(), FrameSource));
        extensions.RegisterRenderer(new LabelInspectionWorkbenchRenderer(BaseDirectory, FrameSource));
        extensions.RegisterRenderer(new LabelInspectionReportRenderer());
    }
}

/// <summary>复用原生标签工作台的页面Renderer；不通过控件执行生产节点。</summary>
public sealed class LabelInspectionWorkbenchRenderer(Func<string> baseDirectory, IWorkflowVisionPreviewSource? previews = null) : IWorkflowWinFormsNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => LabelInspectionEditorPageProvider.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(LabelInspectionEditorPageModel);
    /// <inheritdoc/>
    public Control CreateControl(WorkflowNodeEditorPageDescriptor page) => new LabelWorkbenchControl((LabelInspectionEditorPageModel)page.Model, baseDirectory, previews);
}

internal sealed class LabelWorkbenchControl : UserControl
{
    private readonly LabelInspectionEditorPageModel _model;
    private readonly Func<string> _baseDirectory;
    private readonly IWorkflowVisionPreviewSource? _previews;
    private readonly DP.LabelInspection.LabelInspectionControl _workbench = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 42, AutoEllipsis = true };
    private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Top, Height = 36, AutoSize = true };
    private readonly CancellationTokenSource _lifetime = new();
    private WorkflowLabelInspectionResources? _resources;
    private ImageFrame? _actual;
    private bool _loading;
    private Task? _loadingTask;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    internal LabelWorkbenchControl(LabelInspectionEditorPageModel model, Func<string> baseDirectory, IWorkflowVisionPreviewSource? previews)
    {
        _model = model; _baseDirectory = baseDirectory; _previews = previews;
        Controls.Add(_workbench); Controls.Add(_status); Controls.Add(_toolbar);
        Add("载入上游预览", async () =>
        {
            var binding = model.Node.Frame.Binding;
            if (binding is not { IsPublicData: false } key) throw new InvalidOperationException("请先绑定上游图像；公开数据输入请手工加载配置样张。");
            using var preview = _previews?.Capture(key.NodeId) ?? throw new InvalidOperationException("没有本轮上游图像预览，请先运行采图节点或手工加载配置样张。");
            await LoadAsync(preview.Frame);
        });
        Add("加载配置样张", async () =>
        {
            using var dialog = new OpenFileDialog { Filter = "图像|*.png;*.jpg;*.jpeg;*.bmp;*.pgm|所有文件|*.*", InitialDirectory = Root };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            using var frame = await WorkflowLabelInspectionResources.ReadImageAsync(Root, dialog.FileName, _lifetime.Token);
            await LoadAsync(frame);
            model.Node.AuthorImagePath = Path.GetRelativePath(Root, dialog.FileName);
        });
        Add("导入配方", async () =>
        {
            using var dialog = new OpenFileDialog { Filter = "标签配方|*.json|所有文件|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("配方文件超过预算。");
            model.ImportRecipe(await File.ReadAllTextAsync(dialog.FileName, _lifetime.Token));
            if (_actual is not null) await LoadAsync(_actual);
            else _status.Text = "配方已导入；加载同尺寸配置样张后可图上编辑与试检测。";
        });
        Add("导出配方", () =>
        {
            model.PrepareCommit();
            using var dialog = new SaveFileDialog { Filter = "标签配方|*.json", FileName = "recipe.json" };
            if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, model.Node.RecipeJson);
            return Task.CompletedTask;
        });
        Add("重载资源", async () =>
        {
            // 先捕获正在编辑的ROI/项目，不能用旧节点JSON把未确认的工作台改动覆盖掉。
            if (CaptureRecipe() is { } recipe) model.SetRecipe(recipe);
            if (_actual is not null) await LoadAsync(_actual);
            else await LoadAuthorAsync();
        });
        model.AttachWorkbench(CaptureRecipe, () => _loadingTask is not null || _loading || _workbench.IsInspectionRunning, ReleaseAsync);
        _status.Text = "配置与试检测不发布生产输出。显式发布的字库/异常库新修订是外部资源，不随取消回滚。";
        Load += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(model.Node.AuthorImagePath)) return;
            await GuardAsync(LoadAuthorAsync);
        };
    }
    private string Root => Path.GetFullPath(_model.Node.ResourceRoot, Path.GetFullPath(_baseDirectory()));
    private async Task LoadAuthorAsync()
    {
        if (string.IsNullOrWhiteSpace(_model.Node.AuthorImagePath)) throw new InvalidOperationException("请选择配置样张或载入上游预览。");
        using var frame = await WorkflowLabelInspectionResources.ReadImageAsync(Root, _model.Node.AuthorImagePath, _lifetime.Token);
        await LoadAsync(frame);
    }
    private void Add(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30 };
        _toolbar.Controls.Add(button);
        button.Click += async (_, _) => await GuardAsync(action);
    }
    private async Task GuardAsync(Func<Task> action)
    {
        if (_loadingTask is not null || _loading || _workbench.IsInspectionRunning) { _status.Text = "请等待当前装配/试检测结束。"; return; }
        _loadingTask = action();
        try { await _loadingTask; }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "已取消。"; }
        catch (Exception error) { if (!IsDisposed) _status.Text = error.Message; }
        finally { _loadingTask = null; }
    }
    private async Task LoadAsync(ImageFrame frame)
    {
        if (_loading || _workbench.IsInspectionRunning) throw new InvalidOperationException("标签工作台正在运行。");
        // 即使frame是本页的_actual，也必须在替换前保留独立句柄。
        using var input = frame.Retain();
        _loading = true;
        _workbench.Enabled = false;
        WorkflowLabelInspectionResources? candidate = null;
        try
        {
            var node = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(_model.Node);
            var recipe = string.IsNullOrWhiteSpace(node.RecipeJson)
                ? new InspectionRecipe("标签检测", input.Image.Info.Width, input.Image.Info.Height, EInspectionMode.Free,
                    EAlignmentMode.AssumeAligned, Array.Empty<InspectionRegion>())
                : _model.Serializer.Deserialize(node.RecipeJson);
            if (recipe.Width != input.Image.Info.Width || recipe.Height != input.Image.Info.Height) throw new InvalidDataException("配置样张与配方尺寸不一致。");
            node.RecipeJson = _model.Serializer.Serialize(recipe);
            candidate = await WorkflowLabelInspectionResources.CreateForEditingAsync(node, _baseDirectory(), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _workbench.SetActualImage(input.Image);
            _workbench.SetReferenceImage(candidate.Reference?.Image, recipe.Alignment == EAlignmentMode.AssumeAligned);
            _workbench.ApplyRecipe(recipe);
            _workbench.AttachEngine(candidate.Engine);
            _workbench.AttachLibraryManager(candidate.Host.Store);
            _workbench.AttachAnomalyLibraryManager(candidate.Host.Store.AnomalyLibraries, candidate.Host.AnomalyTrainer, candidate.Host.TemplateLocator);
            _resources?.Dispose(); _resources = candidate; candidate = null;
            _actual?.Dispose(); _actual = input.Retain();
            _status.Text = $"配置样张 {input.FrameId}；{recipe.Name}；编辑结果在应用/确定时捕获，生产输入仍来自绑定。";
        }
        finally { candidate?.Dispose(); _loading = false; if (!IsDisposed) _workbench.Enabled = true; }
    }
    private InspectionRecipe? CaptureRecipe()
    {
        if (_actual is null) return null;
        using var request = _workbench.CreateRequest();
        var current = request.Recipe;
        string name = string.IsNullOrWhiteSpace(_model.Node.RecipeJson) ? "标签检测" : _model.Serializer.Deserialize(_model.Node.RecipeJson).Name;
        return new InspectionRecipe(name, current.Width, current.Height, current.Mode, current.Alignment,
            current.Regions, current.Options, current.Bindings);
    }
    private ValueTask ReleaseAsync()
    {
        if (Environment.CurrentManagedThreadId == _uiThread) return new ValueTask(ReleaseOnUiAsync());
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(new Action(async () =>
        {
            try { await ReleaseOnUiAsync(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }));
        return new ValueTask(completion.Task);
    }
    private async Task ReleaseOnUiAsync()
    {
        _lifetime.Cancel();
        try { if (_loadingTask is not null) await _loadingTask; }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
        try { await _workbench.CancelAndWaitAsync(); }
        finally
        {
            _workbench.Dispose(); _resources?.Dispose(); _resources = null;
            _actual?.Dispose(); _actual = null; _lifetime.Dispose();
        }
    }
}
