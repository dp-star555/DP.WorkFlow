using System.Runtime.CompilerServices;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.LabelInspection.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;
using ModernUI.WinForms;

namespace DP.WorkFlow.LabelInspection.UI.WinForms;

/// <summary>标签配置工作台的WinForms组合入口；运行结果显示在配置页图像下方，不另设报告页。</summary>
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
        extensions.RegisterPageProvider(new LabelInspectionEditorPageProvider(new OpenCvImageCodec(), FrameSource, includeReportPage: false));
        extensions.RegisterRenderer(new LabelInspectionWorkbenchRenderer(BaseDirectory, FrameSource));
    }
}

/// <summary>
/// 复用原生标签工作台的页面Renderer：右侧为图像（上方工具栏、下方检测证据），左侧追加“ROI规则”分页，
/// 文件/字库等操作作为按钮放进参数页。不通过控件执行生产节点。
/// </summary>
public sealed class LabelInspectionWorkbenchRenderer(Func<string> baseDirectory, IWorkflowVisionPreviewSource? previews = null)
    : IWorkflowWinFormsNodeEditorPageRenderer, IWorkflowWinFormsNodeEditorSidePanelRenderer
{
    private readonly ConditionalWeakTable<LabelInspectionEditorPageModel, LabelWorkbenchControl> _controls = new();
    /// <inheritdoc/>
    public string RendererKey => LabelInspectionEditorPageProvider.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(LabelInspectionEditorPageModel);
    /// <inheritdoc/>
    public Control CreateControl(WorkflowNodeEditorPageDescriptor page)
    {
        var model = (LabelInspectionEditorPageModel)page.Model;
        var control = new LabelWorkbenchControl(model, baseDirectory, previews);
        _controls.AddOrUpdate(model, control);
        return control;
    }
    /// <inheritdoc/>
    public IEnumerable<WorkflowWinFormsNodeEditorSidePanel> CreateSidePanels(WorkflowNodeEditorPageDescriptor page)
    {
        if (page.Model is LabelInspectionEditorPageModel model && _controls.TryGetValue(model, out var control))
            yield return new WorkflowWinFormsNodeEditorSidePanel("LabelRoiRules", "ROI规则", control.CreateRulesPanel());
    }
}

internal sealed class LabelWorkbenchControl : UserControl
{
    private static readonly ModernTheme Theme = ModernTheme.Dark;
    private readonly LabelInspectionEditorPageModel _model;
    private readonly Func<string> _baseDirectory;
    private readonly IWorkflowVisionPreviewSource? _previews;
    private readonly DP.LabelInspection.LabelInspectionControl _workbench = new()
    { Dock = DockStyle.Fill, SidebarVisible = false, CanvasToolbarVisible = false };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom, Height = 30, AutoEllipsis = true, Padding = new Padding(8, 2, 8, 2),
        BackColor = Theme.Container, ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ModernToolStrip _toolbar = new() { Dock = DockStyle.Top, Theme = Theme };
    private readonly ModernSelect _drawKind = new() { Size = new Size(150, 30), Theme = Theme, DropDownAnimationDuration = 0 };
    private readonly ToolStripButton _editRois = new("选中/调整") { CheckOnClick = true, ToolTipText = "开启后左键选中并移动/缩放ROI；按住Shift仍可新建" };
    private readonly ToolStripButton _run = new("开始检测") { ToolTipText = "按当前ROI与规则试检测当前图像（不发布生产输出）" };
    private readonly ToolStripButton _cancel = new("取消") { Enabled = false, ToolTipText = "取消正在进行的试检测" };
    private readonly CancellationTokenSource _lifetime = new();
    private DP.LabelInspection.RegionRulesControl? _rules;
    private WorkflowLabelInspectionResources? _resources;
    private ImageFrame? _actual;
    private bool _loading;
    private Task? _loadingTask;
    private bool _syncing;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    // 参数页上的操作按钮：文件与配方、字库与模型、检测设置。
    private static readonly LabelInspectionEditorCommand[] Commands =
    {
        new("LoadSample", "配置样张", "1. 配方与样张", "加载配置样张…", "选一张图作配置底图；资源根目录外的图片复制到根目录下samples\\。绑定标签坐标系时请改用图像上方的“载入上游预览”。"),
        new("SaveReference", "参考图", "1. 配方与样张", "保存为参考图", "把当前配置图保存为资源根目录下label-reference-<节点ID>.png并填入“参考图”，模板模式下用于比对。"),
        new("ImportRecipe", "导入配方", "1. 配方与样张", "导入配方…", "导入SDK原生配方JSON，保留检测项目、约束及库修订。"),
        new("ExportRecipe", "导出配方", "1. 配方与样张", "导出配方…", "把当前配方（含未应用的工作台修改）导出为JSON文件。"),
        new("ReloadResources", "重载资源", "1. 配方与样张", "重载资源", "修改资源路径或外部文件后，重新装配试检测引擎与字库。"),
        new("GlyphLibraries", "字库", "2. 字库与模型", "字库…", "单字库管理与多图制库在同一窗口的两个分页中；ROI绑定字库在“ROI规则”页。发布新修订是外部写入，不随取消回滚。"),
        new("AnomalyLibraries", "异常模型", "2. 字库与模型", "异常模型…", "质量方法B：异常模型库管理与批量训练在同一窗口的两个分页中，关闭时可把新发布的版本绑定到ROI。"),
        new("Thresholds", "检测阈值", "3. 检测设置", "编辑阈值…", "墨迹、原图容差、最小面积、对比度及清晰度阈值，随配方保存。"),
        new("TaskData", "试检测任务数据", "3. 检测设置", "录入任务数据…", "只为配置页试检测提供本周期业务数据，载入新图后清除；生产数据来自“任务期望数据”绑定。"),
    };

    internal LabelWorkbenchControl(LabelInspectionEditorPageModel model, Func<string> baseDirectory, IWorkflowVisionPreviewSource? previews)
    {
        _model = model; _baseDirectory = baseDirectory; _previews = previews;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        BuildToolbar();
        Controls.Add(_workbench); Controls.Add(_status); Controls.Add(_toolbar);
        // SDK 工作台是原生控件，并会在运行中重建列表/面板：统一着色并跟随之后加入的子控件。
        WorkflowWinFormsTheme.ApplyDark(_workbench, followAddedControls: true);
        // SDK 的ROI/绑定/字库等编辑窗口是运行时 new 出来的普通 Form，宿主拿不到创建时机：空闲时（模态循环中同样触发）补着色。
        Application.Idle += ThemeSdkDialogs;
        Disposed += (_, _) => Application.Idle -= ThemeSdkDialogs;
        _workbench.BusyChanged += (_, _) => SyncBusy();
        model.AttachWorkbench(CaptureRecipe, () => _loadingTask is not null || _loading || _workbench.IsInspectionRunning, ReleaseAsync);
        model.AttachCommands(Commands, ExecuteCommandAsync, CommandBlockReason);
        _status.Text = "配置与试检测不发布生产输出。显式发布的字库/异常库新修订是外部资源，不随取消回滚。";
        Load += async (_, _) => await GuardAsync(LoadInitialAsync);
    }

    /// <summary>左侧“ROI规则”分页：ROI列表与所选ROI的类型、检测项目和规则，修改即时写回工作台。</summary>
    internal Control CreateRulesPanel()
    {
        _rules ??= new DP.LabelInspection.RegionRulesControl(_workbench) { Dock = DockStyle.Fill };
        WorkflowWinFormsTheme.ApplyDark(_rules, followAddedControls: true);
        return _rules;
    }

    private void BuildToolbar()
    {
        Button("载入上游预览", "载入本轮上游图像；绑定标签坐标系时同时记录或对齐参考位姿", () => GuardAsync(LoadUpstreamAsync));
        Button("显示运行结果", "显示本节点最近一次正式运行的图像与报告", () => GuardAsync(ShowRunResultAsync));
        _toolbar.Items.Add(new ToolStripSeparator());
        foreach (var kind in _workbench.DrawKinds) _drawKind.Items.Add(kind);
        _drawKind.SelectedItem = _workbench.DrawKind;
        _drawKind.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _drawKind.SelectedItem is not DP.LabelInspection.RegionDrawKind kind) return;
            _workbench.DrawKind = kind;
        };
        _workbench.DrawKindChanged += (_, _) => Sync(() => _drawKind.SelectedItem = _workbench.DrawKind);
        _toolbar.Items.Add(new ToolStripLabel("新建ROI"));
        _toolbar.Items.Add(new ToolStripControlHost(_drawKind)
        { AutoSize = false, Size = _drawKind.Size, Margin = new Padding(2, 1, 2, 1), ToolTipText = "左键拖动新建ROI时的区域类型" });
        _editRois.CheckedChanged += (_, _) => { if (!_syncing) _workbench.EditRegionsMode = _editRois.Checked; };
        _workbench.EditRegionsModeChanged += (_, _) => Sync(() => _editRois.Checked = _workbench.EditRegionsMode);
        _toolbar.Items.Add(_editRois);
        _toolbar.Items.Add(new ToolStripSeparator());
        Button("适应窗口", "画布适应窗口（Home）", () => { _workbench.FitToWindow(); return Task.CompletedTask; });
        Button("1:1", "按原始像素显示", () => { _workbench.ActualSize(); return Task.CompletedTask; });
        _toolbar.Items.Add(new ToolStripSeparator());
        _run.Click += async (_, _) => await GuardAsync(RunTrialAsync);
        _cancel.Click += (_, _) => _workbench.CancelInspection();
        _toolbar.Items.Add(_run); _toolbar.Items.Add(_cancel);

        void Button(string text, string tip, Func<Task> action)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip };
            button.Click += async (_, _) => await action();
            _toolbar.Items.Add(button);
        }
    }

    private void Sync(Action action)
    {
        _syncing = true;
        try { action(); }
        finally { _syncing = false; }
    }

    private void SyncBusy()
    {
        bool running = _workbench.IsInspectionRunning;
        _run.Enabled = !running; _cancel.Enabled = running;
    }

    private async Task LoadInitialAsync()
    {
        // 运行过且有结果时首先显示运行结果；否则显示配置样张，或先接上字库/异常库管理。
        bool hasRun;
        using (var run = _model.RunResults?.Capture()) hasRun = run?.Facts is WorkflowLabelInspectionResult;
        if (hasRun && _model.Node.HasRecipe)
        {
            try { await ShowRunResultAsync(); return; }
            catch (Exception error) when (error is not OperationCanceledException) { _status.Text = "运行结果无法显示：" + error.Message; }
        }
        if (!string.IsNullOrWhiteSpace(_model.Node.AuthorImagePath)) { await LoadAuthorAsync(); return; }
        await AttachLibrariesAsync();
    }

    private async Task LoadUpstreamAsync()
    {
        var model = _model;
        var binding = model.Node.Frame.Binding;
        if (binding is not { IsPublicData: false } key) throw new InvalidOperationException("请先绑定上游图像；公开数据输入请在参数页“加载配置样张”。");
        using var preview = _previews?.Capture(key.NodeId) ?? throw new InvalidOperationException("没有本轮上游图像预览，请先运行采图节点或在参数页加载配置样张。");
        if (model.Node.LabelCoordinates.Binding is not { } coordinates) { await LoadAsync(preview.Frame); return; }
        // 绑定了标签坐标系：ROI直接画在原图上，同时记下这一帧的定位作为参考位姿，运行时ROI随标签相对它的位移/旋转/缩放移动。
        if (coordinates.IsPublicData || coordinates.MemberPath != "CoordinateSystem")
            throw new InvalidOperationException("记录参考位姿需要“标签坐标系”直接绑定定位节点的CoordinateSystem成员。");
        using var located = _previews.Capture(coordinates.NodeId) ?? throw new InvalidOperationException("没有本轮定位结果，请先运行定位节点。");
        var system = (located.Facts as IVisionCoordinateResult)?.CoordinateSystem ?? throw new InvalidOperationException("本帧没有成功定位，不能记录参考位姿。");
        system.ValidateFrame(preview.Frame);
        var node = model.Node;
        if (!node.HasRecipe || node.GetReferencePose() is null)
        {
            // 还没有ROI：本帧就是配置帧，原图原样载入。
            model.SetReferencePose(system.LocalToImage);
            await LoadAsync(preview.Frame);
            _status.Text = "已载入原图并以本帧定位为参考位姿；直接在原图上画ROI，运行时ROI随标签相对本帧的位移/旋转/缩放移动。";
            return;
        }
        // 已有ROI：不改参考位姿，把本帧按定位对齐到配置帧显示，ROI仍落在标签上；配置帧本身即原图（恒等）。
        var placement = node.Placement(system)!;
        var recipe = model.Serializer.Deserialize(node.RecipeJson);
        using var aligned = placement.Rectify(preview.Frame, recipe.Width, recipe.Height);
        await LoadAsync(aligned);
        _status.Text = $"本帧标签相对配置帧旋转 {placement.RotationDegrees:0.#}°、平移 ({placement.Tx:0.#}, {placement.Ty:0.#})，已对齐到配置帧显示，ROI坐标不变；超出原图的部分为黑色。";
    }

    /// <summary>显示本节点最近一次正式运行：同一帧（有放置时对齐到配置帧）＋完整报告，证据显示在图像下方。</summary>
    private async Task ShowRunResultAsync()
    {
        using var preview = _model.RunResults?.Capture() ?? throw new InvalidOperationException("还没有本节点的运行结果，请先运行流程。");
        if (preview.Facts is not WorkflowLabelInspectionResult result) throw new InvalidOperationException("本节点最近一次输出不是标签检测报告。");
        if (!_model.Node.HasRecipe) throw new InvalidOperationException("当前没有配方，不能显示运行结果。");
        var recipe = _model.Serializer.Deserialize(_model.Node.RecipeJson);
        using var aligned = result.Placement?.Rectify(preview.Frame, recipe.Width, recipe.Height);
        await LoadAsync(aligned ?? preview.Frame);
        _workbench.ShowReport(result.Report);
        _status.Text = $"本次运行（{result.FrameId}）：{result.Summary}" + (result.Placement is null ? "" : "；已按定位对齐到配置帧显示")
            + (string.Equals(result.RecipeName, recipe.Name, StringComparison.Ordinal) ? "。" : "；运行后配方已修改，ROI框为当前配置。");
    }

    private async Task RunTrialAsync()
    {
        try { await _workbench.RunInspectionAsync(); }
        catch (ArgumentException error) { MessageBox.Show(this, error.Message, "检测配置未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning); throw; }
    }

    private string CommandBlockReason(string id)
    {
        if (IsDisposed) return "配置页已关闭。";
        if (_loadingTask is not null || _loading || _workbench.IsInspectionRunning) return "请等待当前装配/试检测结束。";
        return id == "SaveReference" && _actual is null ? "请先载入上游预览或配置样张。" : "";
    }

    /// <summary>参数页按钮：忙时拒绝；异常交给参数页提示。</summary>
    private async Task ExecuteCommandAsync(string id)
    {
        var reason = CommandBlockReason(id);
        if (reason.Length != 0) throw new InvalidOperationException(reason);
        var task = RunCommandAsync(id);
        _loadingTask = task;
        try { await task; }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "已取消。"; }
        finally { _loadingTask = null; }
    }

    private async Task RunCommandAsync(string id)
    {
        var model = _model;
        switch (id)
        {
            case "LoadSample":
            {
                using var dialog = new OpenFileDialog { Filter = "图像|*.png;*.jpg;*.jpeg;*.bmp;*.pgm|所有文件|*.*", InitialDirectory = Root };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                // 资源根目录外的样张复制到根目录下 samples\，流程随资源目录一起部署时样张也在。
                var relative = LabelInspectionEditorPageModel.ImportIntoRoot(Root, dialog.FileName);
                using var frame = await WorkflowLabelInspectionResources.ReadImageAsync(Root, relative, _lifetime.Token);
                await LoadAsync(frame);
                model.SetAuthorImagePath(relative);
                if (!string.Equals(Path.GetFullPath(Path.Combine(Root, relative)), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                    _status.Text = $"样张不在资源根目录内，已复制到 {relative}。";
                return;
            }
            case "SaveReference":
            {
                if (_actual is null) throw new InvalidOperationException("请先载入上游预览或配置样张。");
                // 模板模式需要与配方同尺寸的参考图；绑定标签坐标系时就是配置帧原图。
                var relative = $"label-reference-{model.Node.Id}.png";
                var info = _actual.Image.Info;
                var pixels = new byte[info.ByteLength]; _actual.Image.CopyTo(0, pixels, 0, pixels.Length);
                var snapshot = new PixelSnapshot(info.Width, info.Height, info.Layout == EPixelLayout.Gray8 ? EImagePixelFormat.Gray8 : EImagePixelFormat.Bgr24, pixels);
                Directory.CreateDirectory(Root);
                await File.WriteAllBytesAsync(Path.Combine(Root, relative), new OpenCvImageCodec().EncodePng(snapshot), _lifetime.Token);
                model.SetReferenceImagePath(relative);
                await LoadAsync(_actual);
                _status.Text = $"参考图已保存为 {relative}；模板模式下用于比对。";
                return;
            }
            case "ImportRecipe":
            {
                using var dialog = new OpenFileDialog { Filter = "标签配方|*.json|所有文件|*.*" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var info = new FileInfo(dialog.FileName);
                if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("配方文件超过预算。");
                model.ImportRecipe(await File.ReadAllTextAsync(dialog.FileName, _lifetime.Token));
                if (_actual is not null) await LoadAsync(_actual);
                else _status.Text = "配方已导入；加载同尺寸配置图后可图上编辑与试检测。";
                return;
            }
            case "ExportRecipe":
            {
                model.PrepareCommit();
                if (string.IsNullOrWhiteSpace(model.Node.RecipeJson)) throw new InvalidOperationException("当前还没有配方：请先载入预览并创建ROI，或导入配方。");
                using var dialog = new SaveFileDialog { Filter = "标签配方|*.json", FileName = "recipe.json" };
                if (dialog.ShowDialog(this) == DialogResult.OK) await File.WriteAllTextAsync(dialog.FileName, model.Node.RecipeJson, _lifetime.Token);
                return;
            }
            case "ReloadResources":
                // 先捕获正在编辑的ROI/项目，不能用旧节点JSON把未确认的工作台改动覆盖掉。
                if (CaptureRecipe() is { } recipe) model.SetRecipe(recipe);
                if (_actual is not null) await LoadAsync(_actual);
                else if (!string.IsNullOrWhiteSpace(model.Node.AuthorImagePath)) await LoadAuthorAsync();
                else await AttachLibrariesAsync();
                return;
            case "GlyphLibraries": _workbench.OpenGlyphLibraries(); break;
            case "AnomalyLibraries": _workbench.OpenAnomalyLibraries(); break;
            case "Thresholds": _workbench.EditThresholds(); break;
            case "TaskData": _workbench.EditTaskData(); break;
            default: throw new InvalidOperationException("未知操作：" + id);
        }
        // 库窗口里可能发布了新修订：刷新ROI规则页中可绑定的库列表。
        _rules?.Reload();
    }

    private async Task AttachLibrariesAsync()
    {
        var node = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(_model.Node);
        if (!node.HasRecipe)
            node.RecipeJson = _model.Serializer.Serialize(new InspectionRecipe("标签检测", 64, 64, EInspectionMode.Free,
                EAlignmentMode.AssumeAligned, Array.Empty<InspectionRegion>()));
        var candidate = await WorkflowLabelInspectionResources.CreateForEditingAsync(node, _baseDirectory(), _lifetime.Token);
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            _workbench.AttachLibraryManager(candidate.Host.Store);
            _workbench.AttachAnomalyLibraryManager(candidate.Host.Store.AnomalyLibraries, candidate.Host.AnomalyTrainer, candidate.Host.TemplateLocator);
            _resources?.Dispose(); _resources = candidate; candidate = null;
        }
        finally { candidate?.Dispose(); }
        _rules?.Reload();
        _status.Text = _model.Node.Frame.Binding is null
            ? "下一步：在参数页绑定“输入图像”，运行一次流程后点图像上方“载入上游预览”；或在参数页“加载配置样张”。字库/异常库管理已可用。"
            : "下一步：运行一次流程（还没有配方时上游照常执行，本节点提示尚未配置配方），再点图像上方“载入上游预览”。字库/异常库管理已可用。";
    }
    private readonly HashSet<Form> _themedDialogs = new();
    private void ThemeSdkDialogs(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form.GetType() != typeof(Form) || !_themedDialogs.Add(form)) continue;
            WorkflowWinFormsTheme.ApplyDark(form, followAddedControls: true);
            form.FormClosed += (_, _) => _themedDialogs.Remove(form);
        }
    }
    private string Root => Path.GetFullPath(_model.Node.ResourceRoot, Path.GetFullPath(_baseDirectory()));
    private async Task LoadAuthorAsync()
    {
        if (string.IsNullOrWhiteSpace(_model.Node.AuthorImagePath)) throw new InvalidOperationException("请选择配置样张或载入上游预览。");
        using var frame = await WorkflowLabelInspectionResources.ReadImageAsync(Root, _model.Node.AuthorImagePath, _lifetime.Token);
        await LoadAsync(frame);
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
            if (recipe.Width != input.Image.Info.Width || recipe.Height != input.Image.Info.Height) throw new InvalidDataException("配置图与配方尺寸不一致。");
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
            _rules?.Reload();
            _status.Text = $"配置图 {input.FrameId}；{recipe.Name}；编辑结果在应用/确定时捕获，生产输入仍来自绑定。";
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
            _rules?.Dispose(); _rules = null;
            _workbench.Dispose(); _resources?.Dispose(); _resources = null;
            _actual?.Dispose(); _actual = null; _lifetime.Dispose();
        }
    }
}
