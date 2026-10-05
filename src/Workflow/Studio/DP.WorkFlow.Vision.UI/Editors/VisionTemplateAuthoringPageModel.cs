using DP.WorkFlow.UI;
using DP.Vision;
using System.Globalization;

namespace DP.WorkFlow.Vision.UI;

/// <summary>模板属性列表中的操作身份；平台仅负责文件选择和画布切换。</summary>
public enum EVisionTemplateAuthoringCommand
{
    /// <summary>新建空白模板。</summary>
    New,
    /// <summary>导入模板清单。</summary>
    Import,
    /// <summary>刷新模板列表。</summary>
    Refresh,
    /// <summary>读取列表选中的模板。</summary>
    Load,
    /// <summary>读取制作样图。</summary>
    ReadSample,
    /// <summary>使用当前节点输入。</summary>
    UseInput,
    /// <summary>在图上选择参考原点。</summary>
    PickOrigin,
    /// <summary>在图上选择参考方向。</summary>
    PickDirection,
    /// <summary>生成模型。</summary>
    Build,
    /// <summary>读取测试图像。</summary>
    ReadTestImage,
    /// <summary>测试匹配。</summary>
    Test
}

/// <summary>制作测试的明确图像来源。</summary>
public enum EVisionTemplateTestSource
{
    /// <summary>对当前样图整图测试。</summary>
    Sample,
    /// <summary>沿用匹配节点当前输入及搜索配置。</summary>
    Input,
    /// <summary>显式读取测试图像，沿用匹配节点搜索配置。</summary>
    TestImage,
    /// <summary>在制作ROI的外接矩形中，以0°/1倍验证模型，不代替运行图像测试。</summary>
    SampleRegion
}

/// <summary>挂载在匹配节点上的独立模板编辑模型；创建、选择及修改均在隔离草稿中完成。</summary>
public sealed class VisionTemplateAuthoringPageModel(VisionFrameEditorPageModel frame) : IDisposable, IWorkflowNodeEditorCommitParticipant, IWorkflowNodeEditorCommitReadiness
{
    private EVisionTemplateTestSource _testSource = EVisionTemplateTestSource.SampleRegion;
    private string? _selectedResource;
    /// <summary>列表中的候选引用，读取操作成功后才改变节点草稿。</summary>
    public string SelectedResource { get => _selectedResource ?? Frame.TemplateReference; set => _selectedResource = value; }
    /// <summary>默认每个模板显示最新保存修订；可显式查看历史。当前引用始终保留。</summary>
    public bool ShowHistory { get; set; }
    /// <summary>平台显式读取/制作操作期间暂时禁止确认草稿。</summary>
    public bool IsOperating { get; set; }
    /// <summary>制作测试图像来源；切换后旧测试结果作废。</summary>
    public EVisionTemplateTestSource TestSource { get => _testSource; set { if (_testSource != value) { _testSource = value; Draft.ResetTrial(); } } }
    /// <summary>图上拾取参考原点或方向。</summary>
    public string PickMode { get; private set; } = "";
    /// <summary>拾取期间暂停ROI编辑。</summary>
    public bool IsPicking => PickMode.Length != 0;
    /// <summary>请求在样图中拾取业务参考几何。</summary>
    public void BeginPick(bool origin) => PickMode = origin ? "请在样图上点击参考原点" : "请在样图上点击X轴方向点";
    /// <summary>完成原点或方向设置。</summary>
    public void Pick(PointD point)
    {
        if (!IsPicking) return;
        if (PickMode.Contains("原点")) { Draft.OriginX = point.X; Draft.OriginY = point.Y; }
        else
        {
            if (Math.Abs(point.X - Draft.OriginX) + Math.Abs(point.Y - Draft.OriginY) < 1e-10) throw new ArgumentException("方向点不能与原点重合。");
            Draft.AxisAngleRadians = Math.Atan2(point.Y - Draft.OriginY, point.X - Draft.OriginX);
        }
        PickMode = "";
    }
    /// <summary>模型与图像都可用才允许测试。</summary>
    public string TestBlockReason => Draft.TestBlockReason.Length != 0 ? Draft.TestBlockReason
        : TestSource is EVisionTemplateTestSource.Sample or EVisionTemplateTestSource.SampleRegion ? Frame.TemplateSampleTestIssue(TestSource == EVisionTemplateTestSource.SampleRegion) : Frame.TemplateTestInputIssue(TestSource == EVisionTemplateTestSource.TestImage);
    /// <summary>测试按钮旁显示当前启用条件，不用制作自检状态代替测试校验。</summary>
    public string TestReadiness => CommandBlockReason(EVisionTemplateAuthoringCommand.Test) is { Length: > 0 } reason
        ? "暂不能测试：" + reason : "可以测试；已就绪的模型无需重新生成。";
    /// <summary>固定测试状态优先显示当前阻断原因，避免被旧失败或制作自检提示遮蔽。</summary>
    public string TestStatus => CommandBlockReason(EVisionTemplateAuthoringCommand.Test) is { Length: > 0 } reason
        ? "测试：" + (Draft.IsBusy || IsOperating ? Draft.TestState : "暂不可用") + "\n" + reason
        : "测试：" + Draft.TestState + "\n" + (Draft.TrialSearchSummary.Length == 0 ? "" : Draft.TrialSearchSummary + "\n")
            + (Draft.TrialResult == null ? TestReadiness : Draft.TestSummary);
    /// <summary>明确使用所选图像进行测试。</summary>
    public Task TestAsync() => TestSource is EVisionTemplateTestSource.Sample or EVisionTemplateTestSource.SampleRegion ? Frame.TryTemplateSampleAsync(TestSource == EVisionTemplateTestSource.SampleRegion)
        : Frame.TryTemplateAsync(TestSource == EVisionTemplateTestSource.TestImage);
    /// <inheritdoc/>
    public bool CanCommit => !IsOperating && Draft.CanApply;
    /// <inheritdoc/>
    public string CommitBlockReason => IsOperating ? "请等待当前操作完成。" : Draft.ApplyBlockReason;
    /// <summary>共同属性列表，供两种原生属性布局使用。</summary>
    /// <param name="execute">平台操作入口；空值用于只读描述，按钮不可执行。</param>
    /// <returns>包括参数、候选和操作按钮的属性列表。</returns>
    public IReadOnlyList<WorkflowPropertyEntry> Properties(Func<EVisionTemplateAuthoringCommand, Task>? execute = null)
    {
        var properties = new List<WorkflowPropertyEntry>();
        Add("TemplateName", "模板名称", "1. 模板信息", WorkflowPropertyEditorKind.Text, typeof(string), () => Draft.DisplayName,
            v => Draft.DisplayName = (string)v!, "用户可读名称；改名后应用即可保存，不需要重新生成模型。内部资源身份保持不变。");
        Add("ShowHistory", "显示历史版本", "1. 模板信息", WorkflowPropertyEditorKind.Boolean, typeof(bool), () => ShowHistory,
            v => ShowHistory = (bool)v!, "默认每个模板只列最新保存版本；当前使用的旧版本始终显示，不会自动替换节点引用。");
        var visible = ShowHistory ? Draft.Resources : Draft.Resources
            .GroupBy(r => r.TemplateId.Length == 0 ? r.Reference : r.TemplateId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.SavedUtc).ThenBy(r => r.Reference, StringComparer.Ordinal).First())
            .Concat(Draft.Resources.Where(r => r.Reference == Frame.TemplateReference || r.Reference == SelectedResource))
            .DistinctBy(r => r.Reference).ToArray();
        var resources = new List<WorkflowPropertyChoice> { new("未选择模板", "") };
        resources.AddRange(visible.Select(r => new WorkflowPropertyChoice((r.Reference == Frame.TemplateReference ? "[当前] " : "") + r.Label, r.Reference)));
        if (SelectedResource.Length != 0 && !resources.Any(r => Equals(r.Value, SelectedResource)))
            resources.Add(new((SelectedResource == Frame.TemplateReference ? "[当前] " + Draft.DisplayName + " / " : "候选引用：") + SelectedResource, SelectedResource));
        properties.Add(WorkflowPropertyEntry.CreateChoice("TemplateResource", "已有模板", "1. 模板信息", "选择版本后点击读取所选模板；不会覆盖未确认的正式节点。",
            () => SelectedResource, v => SelectedResource = (string)v!, resources, new WorkflowPropertyEditorAttribute("Template.Resource")));
        Action(EVisionTemplateAuthoringCommand.Load, "读取模板", "读取所选模板", "1. 模板信息");
        Action(EVisionTemplateAuthoringCommand.New, "新建模板", "新建空白模板", "1. 模板信息");
        Action(EVisionTemplateAuthoringCommand.Import, "导入模板", "选择模板资源…", "1. 模板信息");
        Action(EVisionTemplateAuthoringCommand.Refresh, "资源列表", "刷新模板列表", "1. 模板信息");
        properties.Add(WorkflowPropertyEntry.CreateChoice("Engine", "制作方式", "1. 模板信息", "制作参数和模型格式由对应引擎提供。",
            () => Draft.ImplementationId, v => Draft.ImplementationId = (string)v!,
            Draft.Choices.Select(d => new WorkflowPropertyChoice(d.Engine + " / " + ((DP.Vision.Algorithms.IVisionTemplateFactoryDescription)d.Factory).MethodDisplayName, d.ImplementationId)).ToArray(), new WorkflowPropertyEditorAttribute("Template.Engine")));
        Add("Source", "样图", "1. 模板信息", WorkflowPropertyEditorKind.ReadOnly, typeof(string), () => Draft.SourceSummary);
        Action(EVisionTemplateAuthoringCommand.ReadSample, "制作样图", "读取样图…", "1. 模板信息");
        Action(EVisionTemplateAuthoringCommand.UseInput, "节点图像", "使用当前输入", "1. 模板信息");
        Add("MakingMask", "制作掩膜", "1. 模板信息", WorkflowPropertyEditorKind.ReadOnly, typeof(string), () => Draft.MaskSummary);
        Add("OriginX", "原点 X", "2. 参考坐标", WorkflowPropertyEditorKind.Number, typeof(double), () => Draft.OriginX, v => Draft.OriginX = (double)v!);
        Add("OriginY", "原点 Y", "2. 参考坐标", WorkflowPropertyEditorKind.Number, typeof(double), () => Draft.OriginY, v => Draft.OriginY = (double)v!);
        Add("Direction", "方向（°）", "2. 参考坐标", WorkflowPropertyEditorKind.Number, typeof(double), () => Draft.AxisAngleRadians * 180 / Math.PI, v => Draft.AxisAngleRadians = (double)v! * Math.PI / 180);
        Action(EVisionTemplateAuthoringCommand.PickOrigin, "选择原点", "图上设原点", "2. 参考坐标");
        Action(EVisionTemplateAuthoringCommand.PickDirection, "选择方向", "图上设方向", "2. 参考坐标");
        foreach (var p in Draft.Parameters)
        {
            Add("Build." + p.Id, p.DisplayName, "3. 制作参数", p.ValueType == typeof(bool) ? WorkflowPropertyEditorKind.Boolean : p.ValueType.IsEnum ? WorkflowPropertyEditorKind.Enum : p.ValueType == typeof(string) ? WorkflowPropertyEditorKind.Text : WorkflowPropertyEditorKind.Number,
                p.ValueType, () => ConvertParameter(p), v => Draft.SetParameter(p, Convert.ToString(v, CultureInfo.InvariantCulture)!), p.Description ?? "", p.Minimum, p.Maximum);
            if (p.DisplayRadiansAsDegrees) properties[^1].WithRadiansAsDegrees();
        }
        Action(EVisionTemplateAuthoringCommand.Build, "模型制作", "生成模型", "3. 制作参数");
        properties.Add(WorkflowPropertyEntry.CreateChoice("TestSource", "测试图像", "4. 测试设置", "制作区域自检使用0°/1倍；样图整图使用运行搜索区间。当前输入/测试文件沿用节点搜索范围。",
            () => TestSource.ToString(), v => TestSource = Enum.Parse<EVisionTemplateTestSource>((string)v!),
            new[] { new WorkflowPropertyChoice("制作区域（0°/1倍自检）", "SampleRegion"), new WorkflowPropertyChoice("模板样图（整图）", "Sample"), new WorkflowPropertyChoice("当前输入（节点搜索范围）", "Input"), new WorkflowPropertyChoice("测试图像（节点搜索范围）", "TestImage") }, new WorkflowPropertyEditorAttribute("Template.TestSource")));
        Add("Score", "最小分数", "4. 测试设置", WorkflowPropertyEditorKind.Number, typeof(double),
            () => Frame.EditingNode is LocateVisionTemplatePoseNodeModel pose ? pose.MinimumScore : ((LocateVisionTemplateNodeModel)Frame.EditingNode).MinimumScore,
            v => { var score = (double)v!; if (!double.IsFinite(score) || score < 0 || score > 1) throw new ArgumentOutOfRangeException(nameof(score), "最小分数必须在0到1之间。");
                if (Frame.EditingNode is LocateVisionTemplatePoseNodeModel pose) pose.MinimumScore = score; else ((LocateVisionTemplateNodeModel)Frame.EditingNode).MinimumScore = score; Draft.ResetTrial(); }, minimum: 0, maximum: 1);
        if (Frame.EditingNode is LocateVisionTemplatePoseNodeModel poseNode)
        {
            Add("WorkBudget", "比较预算", "4. 测试设置", WorkflowPropertyEditorKind.Number, typeof(long), () => poseNode.MaximumWork,
                v => { long budget = (long)v!; if (budget < 1 || budget > 2000000000) throw new ArgumentOutOfRangeException(nameof(budget), "比较预算须为1至20亿。"); poseNode.MaximumWork = budget; Draft.ResetTrial(); }, "搜索位置与候选的工作量上限；超限时建议先缩小搜索ROI。", 1, 2000000000);
            Angle("TestMinimumAngle", "匹配最小角度", () => poseNode.MinimumAngleRadians, v => poseNode.MinimumAngleRadians = v,
                "运行搜索的顺时针角度下限；绑定父坐标时相对父坐标。与模型制作范围独立，修改后随应用提交到节点。");
            Angle("TestMaximumAngle", "匹配最大角度", () => poseNode.MaximumAngleRadians, v => poseNode.MaximumAngleRadians = v,
                "最小85°、最大95°表示90°附近±5°；上下限相同表示固定角度。制作区域自检固定为0°。");
            Number("TestMinimumScale", "匹配最小尺度", () => poseNode.MinimumScale, v => poseNode.MinimumScale = v, .1, 10);
            Number("TestMaximumScale", "匹配最大尺度", () => poseNode.MaximumScale, v => poseNode.MaximumScale = v, .1, 10);
            Angle("TestAngleStep", "采样角度步长", () => poseNode.AngleStepRadians, v => poseNode.AngleStepRadians = v,
                "OpenCV按此步长采样；HALCON原生范围搜索不使用。与模型制作角度步长独立。", .000001, 2 * Math.PI);
            Number("TestScaleStep", "采样尺度步长", () => poseNode.ScaleStep, v => poseNode.ScaleStep = v, .000001, 10);
            void Angle(string id, string label, Func<double> read, Action<double> write, string description, double minimum = -2 * Math.PI, double maximum = 2 * Math.PI)
            {
                Add(id, label, "4. 测试设置", WorkflowPropertyEditorKind.Number, typeof(double), () => read(),
                    v => { write((double)v!); Draft.ResetTrial(); }, description, minimum, maximum);
                properties[^1].WithRadiansAsDegrees();
            }
            void Number(string id, string label, Func<double> read, Action<double> write, double minimum, double maximum)
                => Add(id, label, "4. 测试设置", WorkflowPropertyEditorKind.Number, typeof(double), () => read(),
                    v => { write((double)v!); Draft.ResetTrial(); }, "与节点共用的运行搜索参数；区间须在已保存模型的制作范围内。", minimum, maximum);
        }
        Action(EVisionTemplateAuthoringCommand.ReadTestImage, "测试文件", "读取测试图像…", "4. 测试设置");
        Add("TestReadiness", "测试条件", "4. 测试设置", WorkflowPropertyEditorKind.ReadOnly, typeof(string), () => TestReadiness);
        Action(EVisionTemplateAuthoringCommand.Test, "匹配测试", "测试匹配", "4. 测试设置");
        return properties;
        void Action(EVisionTemplateAuthoringCommand command, string label, string caption, string category)
            => properties.Add(WorkflowPropertyEntry.CreateAction("Command." + command, label, category, caption,
                () => command == EVisionTemplateAuthoringCommand.Build && Draft.IsBuilt ? "重新生成模型" : caption,
                () => execute!(command), () => execute == null ? "宿主未提供操作入口。" : CommandBlockReason(command)));
        void Add(string id, string label, string category, WorkflowPropertyEditorKind kind, Type type, Func<object?> read, Action<object?>? write = null, string description = "", double? minimum = null, double? maximum = null)
        {
            var entry = WorkflowPropertyEntry.Create(id, label, category, description, kind, type, read, write ?? (_ => { }));
            if (kind == WorkflowPropertyEditorKind.Number) entry.WithNumberRange(minimum, maximum);
            properties.Add(entry);
        }
        object ConvertParameter(DP.Vision.Algorithms.VisionAlgorithmParameter p) => p.ValueType.IsEnum ? Enum.Parse(p.ValueType, Draft.ParameterValue(p)) : Convert.ChangeType(Draft.ParameterValue(p), p.ValueType, CultureInfo.InvariantCulture);
    }
    /// <summary>按钮不可执行的原因；执行前再次检查，避免迟到点击覆盖操作。</summary>
    /// <param name="command">操作身份。</param>
    /// <returns>空文本表示可执行。</returns>
    public string CommandBlockReason(EVisionTemplateAuthoringCommand command)
    {
        if (Draft.IsDisposed) return "编辑器已关闭。";
        if (IsOperating || Draft.IsBusy) return "请等待当前操作完成。";
        return command switch
        {
            EVisionTemplateAuthoringCommand.Build => Draft.BuildBlockReason,
            EVisionTemplateAuthoringCommand.Test => TestBlockReason,
            EVisionTemplateAuthoringCommand.Load when SelectedResource.Length == 0 => "请先选择已有模板。",
            EVisionTemplateAuthoringCommand.PickOrigin or EVisionTemplateAuthoringCommand.PickDirection when !Draft.HasSample => "请先读取样图。",
            _ => ""
        };
    }
    /// <summary>执行属性列表请求的操作；平台在调用前提供选中的文件路径。</summary>
    /// <param name="command">操作身份。</param>
    /// <param name="path">文件选择结果；非文件操作不需要。</param>
    /// <returns>操作完成任务。</returns>
    public async Task ExecuteAsync(EVisionTemplateAuthoringCommand command, string? path = null)
    {
        var reason = CommandBlockReason(command);
        if (reason.Length != 0) throw new InvalidOperationException(reason);
        IsOperating = true;
        try
        {
            switch (command)
            {
                case EVisionTemplateAuthoringCommand.New: Draft.StartNewTemplate(); _selectedResource = ""; ResetResourceTest(); break;
                case EVisionTemplateAuthoringCommand.Import: await Draft.LoadResourceAsync(RequirePath()); _selectedResource = Frame.TemplateReference; ResetResourceTest(); break;
                case EVisionTemplateAuthoringCommand.Refresh: await Draft.RefreshResourcesAsync(); break;
                case EVisionTemplateAuthoringCommand.Load: await Draft.LoadResourceAsync(SelectedResource); ResetResourceTest(); break;
                case EVisionTemplateAuthoringCommand.ReadSample: await Draft.ReadSourceAsync(RequirePath()); PickMode = ""; break;
                case EVisionTemplateAuthoringCommand.UseInput: Draft.UseInput(); PickMode = ""; break;
                case EVisionTemplateAuthoringCommand.PickOrigin: BeginPick(true); break;
                case EVisionTemplateAuthoringCommand.PickDirection: BeginPick(false); break;
                case EVisionTemplateAuthoringCommand.Build: PickMode = ""; await Draft.BuildAsync(); break;
                case EVisionTemplateAuthoringCommand.ReadTestImage: await Frame.ReadPreviewAsync(RequirePath()); Draft.ResetTrial(); TestSource = EVisionTemplateTestSource.TestImage; break;
                case EVisionTemplateAuthoringCommand.Test: PickMode = ""; await TestAsync(); break;
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
        }
        finally { IsOperating = false; }
        string RequirePath() => !string.IsNullOrWhiteSpace(path) ? path : throw new ArgumentException("未选择文件。", nameof(path));
    }
    /// <summary>WinForms/WPF 的共同编辑页面键。</summary>
    public const string RendererKey = "DP.Vision.TemplateEditor";
    private void ResetResourceTest() { TestSource = EVisionTemplateTestSource.SampleRegion; PickMode = ""; }
    /// <summary>制作、试匹配画布，拥有草稿和预览资源。</summary>
    public VisionFrameEditorPageModel Frame { get; } = frame;
    /// <summary>独立模板制作草稿。</summary>
    public VisionTemplateEditorModel Draft => Frame.Template ?? throw new InvalidOperationException("缺少模板编辑模型。");
    /// <summary>打开弹窗时加载已选择版本；空白模板保持空白，失败保留原引用。</summary>
    public async Task OpenAsync()
    {
        var errors = new List<Exception>();
        try
        {
            if (!string.IsNullOrEmpty(Frame.TemplateReference)) { await Draft.LoadResourceAsync(Frame.TemplateReference); _selectedResource = Frame.TemplateReference; ResetResourceTest(); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { errors.Add(ex); }
        if (!Draft.IsDisposed)
            try { await Draft.RefreshResourcesAsync(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(e => e.Message)), errors[0]);
    }
    /// <inheritdoc/>
    public void PrepareCommit()
    {
        Draft.PrepareCommit();
        _selectedResource = Frame.TemplateReference;
    }
    /// <inheritdoc/>
    public void Dispose() => Frame.Dispose();
}
