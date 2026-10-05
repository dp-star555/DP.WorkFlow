using System.Globalization;
using System.Text;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.UI;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>节点内模板制作草稿，取消不修改正式节点或发布资源。</summary>
public sealed partial class VisionTemplateEditorModel : IDisposable
{
    private readonly IWorkflowVisionTemplateNode _node;
    private readonly VisionTemplateEditingRuntime? _runtime;
    private readonly Func<ImageFrame> _input;
    private readonly IImageFileReader? _reader;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private ImageFrame? _source, _trialFrame;
    private VisionTemplateBuild? _built;
    private string? _builtKey;
    private VisionTemplateDefinition? _baseline;
    private string? _baselineSourceHash;
    private bool _sourceReplaced;
    private bool _disposed, _dirty, _busy;
    private long _generation, _sequence;
    private string _implementation;
    private string _displayName;
    private double _originX, _originY, _axisAngle;
    private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);
    private TemplatePoseResult? _trial;

    /// <summary>建立编辑草稿；构造时不读取文件或加载模型。</summary>
    public VisionTemplateEditorModel(IWorkflowVisionTemplateNode node, VisionTemplateEditingRuntime? runtime,
        Func<ImageFrame> input, IImageFileReader? reader)
    {
        _token = _lifetime.Token;
        _node = node; _runtime = runtime; _input = input; _reader = reader; _implementation = node.ModelAlgorithm.ImplementationId;
        _displayName = DefaultDisplayName();
        if (node.TemplateReferenceDefinition != null) _baseline = VisionTemplateStore.CopyDefinition(node.TemplateReferenceDefinition);
        _baselineSourceHash = string.IsNullOrEmpty(node.TemplateSourceHash) ? null : node.TemplateSourceHash;
        Editor = new RoiEditor(); Editor.DocumentChanged += OnChanged;
    }
    /// <summary>制作区域编辑器，与节点搜索ROI分开。</summary>
    public RoiEditor Editor { get; }
    /// <summary>只读实现候选。</summary>
    public IReadOnlyList<VisionAlgorithmDescriptor> Choices => _runtime?.Choices(_node.RequiresPoseSearch) ?? [];
    /// <summary>用户可读的模板名称；改名仅发布元数据，不需要重新制作原生模型。</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            ThrowIfDisposed();
            value = value?.Trim() ?? "";
            if (value.Length == 0 || value.Length > 100 || value.Any(char.IsControl)) throw new ArgumentException("模板名称须为1至100个字符，不能包含控制字符。");
            if (_displayName == value) return;
            _displayName = value; _dirty = true;
        }
    }
    private string DefaultDisplayName() => _node is IWorkflowNodeModel node && !string.IsNullOrWhiteSpace(node.Title) ? node.Title.Trim() : "定位模板";
    /// <summary>当前匹配实现。</summary>
    public string ImplementationId
    {
        get => _implementation;
        set
        {
            ThrowIfDisposed(); if (value == _implementation) return;
            if (!Choices.Any(d => d.ImplementationId == value)) throw new InvalidOperationException("此实现未安装或不支持本节点的搜索能力。");
            _implementation = value; _settings.Clear(); Invalidate();
        }
    }
    /// <summary>按当前引擎提供的制作参数描述。</summary>
    public IReadOnlyList<VisionAlgorithmParameter> Parameters => Choices.FirstOrDefault(d => d.ImplementationId == _implementation)?.Factory is IVisionTemplateFactoryDescription d ? d.BuildParameters : [];
    /// <summary>读取参数当前值。</summary>
    public string ParameterValue(VisionAlgorithmParameter p) => _settings.GetValueOrDefault(p.Id, p.DefaultValue ?? "");
    /// <summary>类型化验证后提交制作参数。</summary>
    public void SetParameter(VisionAlgorithmParameter parameter, string text)
    {
        ThrowIfDisposed(); if (!Parameters.Any(p => p.Id == parameter.Id)) throw new InvalidOperationException("制作参数已经变化，请重新编辑。");
        if (parameter.ValueType.IsEnum) { var value = Enum.Parse(parameter.ValueType, text); if (!Enum.IsDefined(parameter.ValueType, value)) throw new ArgumentException("枚举值未定义。"); }
        else if (parameter.ValueType == typeof(bool)) _ = bool.Parse(text);
        else if (parameter.ValueType != typeof(string))
        {
            var value = Convert.ChangeType(text, parameter.ValueType, CultureInfo.InvariantCulture); var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number) || number < parameter.Minimum || number > parameter.Maximum)
                throw new ArgumentOutOfRangeException(parameter.Id, text, $"{parameter.DisplayName}超出允许范围：{parameter.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "无下限"}～{parameter.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "无上限"}；输入值：{text}。");
        }
        if (ParameterValue(parameter) == text) { _failure = null; Status = BuildState; return; }
        _settings[parameter.Id] = text; Invalidate();
    }
    /// <summary>样图中的原点X。</summary>
    public double OriginX { get => _originX; set { if (!double.IsFinite(value)) throw new ArgumentException("原点必须有限。"); if (_originX != value) { _originX = value; Invalidate(); } } }
    /// <summary>样图中的原点Y。</summary>
    public double OriginY { get => _originY; set { if (!double.IsFinite(value)) throw new ArgumentException("原点必须有限。"); if (_originY != value) { _originY = value; Invalidate(); } } }
    /// <summary>参考X轴顺时针弧度。</summary>
    public double AxisAngleRadians { get => _axisAngle; set { if (!double.IsFinite(value)) throw new ArgumentException("方向必须有限。"); if (_axisAngle != value) { _axisAngle = value; Invalidate(); } } }
    /// <summary>状态说明。</summary>
    public string Status { get; private set; } = "空白模板：读取样图、使用当前输入，或选择已制作的模板。";
    /// <summary>显式刷新后的配方模板列表；打开属性面板不进行文件扫描。</summary>
    public IReadOnlyList<VisionTemplateResourceChoice> Resources { get; private set; } = [];
    /// <summary>刷新资源候选，不改变选择及草稿。</summary>
    public async Task RefreshResourcesAsync()
    {
        ThrowIfDisposed();
        var choices = _runtime == null ? [] : await _runtime.ListResourcesAsync(_node.RequiresPoseSearch, _token);
        if (!_disposed) Resources = choices;
    }
    /// <summary>是否具有当前参数的有效模型。</summary>
    public bool IsBuilt => _built != null && _builtKey == Key();
    /// <summary>后台操作进行中。</summary>
    public bool IsBusy => _busy;
    /// <summary>页面已关闭，迟到UI事件应停止更新。</summary>
    public bool IsDisposed => _disposed;
    /// <summary>试匹配结果，不会写入工作流运行输出。</summary>
    public TemplatePoseResult? TrialResult => _trial;
    private void OnChanged(object? sender, RoiDocumentChangedEventArgs e) => Invalidate();
    private void Invalidate() { _dirty = true; _generation++; ResetBuildVerification(); ResetTrial(); Status = "模板制作配置已变化，需要生成模型后应用。"; }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>独立复制输入预览，释放运行帧租约。</summary>
    public void UseInput()
    {
        ThrowIfDisposed(); EnsureBaseline(); using var input = _input(); using var copy = VisionTemplateSource.Decode(VisionTemplateSource.Encode(input.Image)); SetSource(copy);
    }
    /// <summary>读取制作样图，不修改采集或搜索配置。</summary>
    public async Task ReadSourceAsync(string path)
    {
        ThrowIfDisposed(); EnsureBaseline(); if (_reader == null) throw new InvalidOperationException("宿主未提供图像解码能力。");
        long generation = ++_generation;
        using var image = await _reader.ReadAsync(path, _token);
        if (_disposed || generation != _generation) return;
        SetSource(image);
    }
    private void SetSource(IImageSource image)
    {
        var frame = new ImageFrame(Guid.NewGuid().ToString("N"), image);
        _sourceReplaced = _baselineSourceHash != null && VisionTemplateStore.Hash(VisionTemplateSource.Encode(image)) != _baselineSourceHash;
        _source?.Dispose(); _source = frame;
        _originX = image.Info.Width / 2d; _originY = image.Info.Height / 2d; _axisAngle = 0;
        Editor.Cancel(); Editor.Load(new RoiDocument([])); Invalidate();
    }
    /// <summary>读取已发布模型的可重建数据，恢复引擎参数和制作几何。</summary>
    public async Task LoadResourceAsync(string reference)
    {
        ThrowIfDisposed(); if (_runtime?.Resources is not { } resources) throw new InvalidOperationException("宿主未提供资源解析上下文。");
        long generation = ++_generation;
        var path = resources.Resolve(reference);
        var snapshot = await Task.Run(() => VisionTemplateStore.Capture(path, _token), _token);
        if (_disposed || generation != _generation) return;
        var manifest = snapshot.Manifest;
        if (!Choices.Any(c => c.ImplementationId == manifest.ImplementationId)) throw new InvalidOperationException("资源匹配实现未安装或与此节点不兼容。");
        using var source = VisionTemplateSource.Decode(snapshot.Read("source/image.bin"));
        var d = manifest.Definition;
        if (source.Info.Width != d.SourceWidth || source.Info.Height != d.SourceHeight) throw new InvalidDataException("资源样图与参考定义尺寸不一致。");
        RoiDocument rois;
        if (manifest.Files.Any(f => f.Path == "source/regions.xml")) rois = RoiDocumentXml.Deserialize(Encoding.UTF8.GetString(snapshot.Read("source/regions.xml")));
        else if (manifest.Files.Any(f => f.Path == "source/mask.bin"))
        {
            using var mask = VisionTemplateSource.Decode(snapshot.Read("source/mask.bin"));
            if (mask.Info.Width != d.SourceWidth || mask.Info.Height != d.SourceHeight || mask.Info.Layout != EPixelLayout.Gray8) throw new InvalidDataException("重建掩码尺寸或格式不一致。");
            var runs = new List<RegionRun>(); var row = new byte[mask.Info.Width];
            for (int y = 0; y < mask.Info.Height; y++)
            {
                mask.CopyTo(y * row.Length, row, 0, row.Length);
                for (int x = 0; x < row.Length; x++) if (row[x] != 0) { int start = x; while (x < row.Length && row[x] != 0) x++; runs.Add(new RegionRun(y, start, x)); }
            }
            rois = new RoiDocument(new[] { new RoiDefinition("template-mask", new RegionGeometry(runs)) });
        }
        else throw new InvalidDataException("资源缺少区域或有效掩码，无法完整恢复制作依据。");
        _implementation = manifest.ImplementationId; _settings.Clear(); foreach (var pair in manifest.BuildSettings) _settings.Add(pair.Key, pair.Value);
        _displayName = string.IsNullOrWhiteSpace(manifest.DisplayName) ? "未命名模板" : manifest.DisplayName;
        SetSource(source); Editor.Load(rois); _originX = d.OriginX; _originY = d.OriginY; _axisAngle = d.AxisAngleRadians;
        _baseline = VisionTemplateStore.CopyDefinition(d); _baselineSourceHash = manifest.Files.Single(f => f.Path == "source/image.bin").Hash;
        _sourceReplaced = false; _node.TemplateResourceId = manifest.TemplateId;
        _node.TemplateReferenceDefinition = VisionTemplateStore.CopyDefinition(d); _node.TemplateSourceHash = _baselineSourceHash;
        _node.ModelAlgorithm = new VisionAlgorithmSelection { ImplementationId = manifest.ImplementationId, Settings = new() { ["templatePath"] = reference } };
        _node.TemplateSource = EWorkflowVisionTemplateSource.Resource;
        _built = new VisionTemplateBuild(manifest.ImplementationId, manifest.ModelFormat, d, manifest.BuildSettings,
            manifest.Files.Select(f => new VisionTemplateArtifact(f.Path, snapshot.Read(f.Path))));
        _builtKey = Key(); _dirty = false; _failure = null; _trial = null;
        Status = "已读取模板，模型就绪：可直接测试或应用；修改制作参数后需要重新生成。";
    }
    private VisionTemplateDefinition Definition()
    {
        var source = _source ?? throw new InvalidOperationException("请先读取模板样图。");
        var mask = MakingRegion(source);
        if (mask.AreaPixels == 0) throw new InvalidOperationException("模板有效区域为空。");
        var bounds = mask.Bounds;
        int x = (int)Math.Floor(bounds.X), y = (int)Math.Floor(bounds.Y);
        var d = new VisionTemplateDefinition { SourceWidth = source.Image.Info.Width, SourceHeight = source.Image.Info.Height,
            X = x, Y = y, Width = (int)Math.Ceiling(bounds.X + bounds.Width) - x, Height = (int)Math.Ceiling(bounds.Y + bounds.Height) - y,
            OriginX = _originX, OriginY = _originY, AxisAngleRadians = _axisAngle, ReferenceVersion = _baseline?.ReferenceVersion ?? 1,
            ReferenceIdentity = _baseline?.ReferenceIdentity ?? _node.TemplateResourceId };
        if (_baseline != null && (_sourceReplaced || d.GeometrySignature() != _baseline.GeometrySignature())) d.ReferenceVersion = checked(_baseline.ReferenceVersion + 1);
        d.Validate(); return d;
    }
    private void EnsureBaseline()
    {
        if (_baseline != null || !_node.ModelAlgorithm.Settings.TryGetValue("templatePath", out var reference) || string.IsNullOrWhiteSpace(reference)) return;
        var resources = _runtime?.Resources ?? throw new InvalidOperationException("宿主未提供资源解析上下文。");
        var manifest = VisionTemplateStore.Inspect(resources.Resolve(reference));
        _baseline = VisionTemplateStore.CopyDefinition(manifest.Definition);
        _baselineSourceHash = manifest.Files.SingleOrDefault(f => f.Path == "source/image.bin")?.Hash;
        _node.TemplateResourceId = manifest.TemplateId;
    }
    /// <summary>明确新建模板与坐标身份，用于替换无法核对的资源；正式节点应用前保持不变。</summary>
    public void StartNewTemplate()
    {
        ThrowIfDisposed(); _baseline = null; _baselineSourceHash = null; _sourceReplaced = false;
        _source?.Dispose(); _source = null; _built = null; _builtKey = null; _settings.Clear();
        _originX = _originY = _axisAngle = 0;
        Editor.Cancel(); Editor.Load(new RoiDocument([]));
        _node.TemplateResourceId = Guid.NewGuid().ToString("N"); _node.CoordinateSystemId = Guid.NewGuid().ToString("N");
        _node.TemplateReferenceDefinition = null; _node.TemplateSourceHash = "";
        _displayName = DefaultDisplayName();
        _node.ModelAlgorithm = new VisionAlgorithmSelection { ImplementationId = _implementation };
        Invalidate(); Status = "已新建空白模板和坐标身份；请读取样图并生成模型。原下游ROI需重新确认。";
    }
    private string Key() => _source == null ? "no-source" : _source.FrameId + "|" + _implementation + "|" +
        OriginX.ToString("R", CultureInfo.InvariantCulture) + "|" + OriginY.ToString("R", CultureInfo.InvariantCulture) + "|" + AxisAngleRadians.ToString("R", CultureInfo.InvariantCulture)
        + "|" + RoiDocumentXml.Serialize(Editor.Document) + "|" + string.Join(";", _settings.OrderBy(p => p.Key).Select(p => p.Key.Length + ":" + p.Key + p.Value.Length + ":" + p.Value));

    /// <summary>后台生成模型并在制作样图制作区域内以0°/1倍自检；迟到结果不覆盖当前草稿。</summary>
    public async Task BuildAsync()
    {
        ThrowIfDisposed(); if (_busy) throw new InvalidOperationException("请等待当前制作或试匹配结束。");
        if (_runtime == null) throw new InvalidOperationException("宿主未注册模板制作运行时。");
        var definition = Definition(); var key = Key(); long generation = _generation;
        using var source = _source!.Retain();
        var mask = MakingRegion(source);
        var roiXml = Encoding.UTF8.GetBytes(RoiDocumentXml.Serialize(Editor.Document));
        ResetBuildVerification(); ResetTrial(); _failure = null; _testing = false; _busy = true; Status = "正在生成模板模型…";
        try
        {
            var built = await _runtime.BuildAsync(_implementation, new VisionTemplateBuildRequest(source, definition, mask, new Dictionary<string, string>(_settings)), _token);
            if (_disposed || generation != _generation || key != Key()) { if (!_disposed) Status = "制作完成时配置已改变，请重新生成。"; return; }
            _built = new VisionTemplateBuild(built.ImplementationId, built.Format, built.Definition, built.Settings,
                built.Files.Concat(new[] { new VisionTemplateArtifact("source/regions.xml", roiXml) }));
            _builtKey = key;
            await VerifyBuiltAsync(source, _built, key, generation);
        }
        catch (Exception ex) { ReportFailure(ex); throw; }
        finally { _busy = false; }
    }
    /// <summary>在当前输入或测试图像上试匹配，持有资源直到原生调用退出。</summary>
    public async Task TryMatchAsync(ImageFrame frame, PixelBounds bounds, TemplatePoseOptions options, RegionGeometry? region = null, VisionCoordinateSystem? parent = null)
    {
        ThrowIfDisposed(); if (_busy || !IsBuilt) throw new InvalidOperationException("请先为当前配置生成模型。");
        var build = _built!; var key = _builtKey; long generation = _generation;
        using var input = frame.Retain(); _failure = null; _testing = true; _busy = true; _trial = null; _trialMask = null;
        _trialFrame?.Dispose(); _trialFrame = null; _trialSearchSummary = DescribeSearch(input, bounds, options);
        Status = "正在试匹配…";
        try
        {
            var result = await MatchModelAsync(build, input, bounds, options, region, parent);
            if (_disposed || generation != _generation || key != Key()) return;
            var search = new RegionGeometry(Enumerable.Range(bounds.Y, bounds.Height).Select(y => new RegionRun(y, bounds.X, bounds.X + bounds.Width)));
            _trialMask = region is null ? search : search.Intersect(region, _token);
            _trialFrame?.Dispose(); _trialFrame = input.Retain(); _trial = result;
            Status = result.Found ? $"测试完成：已找到目标，分数 {result.Score:F5}。" : $"测试完成：未找到目标，分数 {result.Score:F5}；可检查测试图像、搜索范围和阈值。";
        }
        catch (Exception ex) { ReportFailure(ex); throw; }
        finally { _busy = false; _testing = false; }
    }
    /// <summary>节点应用前发布确定版本，更新隔离节点引用。</summary>
    public void PrepareCommit()
    {
        ThrowIfDisposed();
        if (!_dirty) return;
        if (_busy || Editor.IsEditing || !IsBuilt) throw new InvalidOperationException("模板草稿有未完成修改，请完成绘制并生成模型，或取消节点编辑。");
        var directory = _runtime?.Resources?.RecipeDirectory ?? throw new InvalidOperationException("请先保存配方，再应用本地模板资源。");
        var reference = VisionTemplateStore.Publish(directory, _node.TemplateResourceId, _built!, _displayName, _token);
        var choice = VisionTemplateResourceChoice.FromManifest(reference, new VisionTemplateManifest
        {
            TemplateId = _node.TemplateResourceId, RevisionId = Path.GetFileName(Path.GetDirectoryName(reference))!,
            DisplayName = _displayName, ImplementationId = _implementation, Definition = _built!.Definition
        }, File.GetLastWriteTimeUtc(Path.Combine(directory, reference)), compatible: true);
        Resources = new[] { choice }.Concat(Resources.Where(r => r.Reference != reference)).ToArray();
        _node.ModelAlgorithm = new VisionAlgorithmSelection { ImplementationId = _implementation, Settings = new() { ["templatePath"] = reference } };
        _node.TemplateSource = EWorkflowVisionTemplateSource.Resource; _baseline = VisionTemplateStore.CopyDefinition(_built!.Definition);
        _baselineSourceHash = VisionTemplateStore.Hash(_built.Files.Single(f => f.Name == "source/image.bin").Content); _sourceReplaced = false; _dirty = false;
        _node.TemplateReferenceDefinition = VisionTemplateStore.CopyDefinition(_baseline); _node.TemplateSourceHash = _baselineSourceHash;
        Status = "模板新版本已发布，节点引用将在应用时提交。";
    }
    /// <summary>制作样图或试匹配画布快照。</summary>
    public CanvasFrame? Capture(bool trial) => Capture(trial, checked(++_sequence));

    /// <summary>页面使用自身的统一序号捕获模板来源，避免跨来源切换时被画布作为旧帧拒绝。</summary>
    internal CanvasFrame? Capture(bool trial, long sequence)
    {
        if (_disposed) return null;
        var frame = trial ? _trialFrame : _source; if (frame == null) return null;
        var visuals = new List<Visual>();
        var layers = new List<CanvasLayer>();
        if (ShowMask && (trial ? _trialMask : PreviewMask(frame)) is { } mask)
            layers.Add(new CanvasLayer("effective-mask", ELayerKind.Annotation,
                new[] { new Visual("effective-mask", mask, 0x4022DD88, $"{(trial ? "测试" : "模板")}有效掩膜：{mask.AreaPixels}像素") }, -10, name: "有效掩膜"));
        if (trial && _trial?.Transform is { } p) visuals.Add(new Visual("trial", new RectangleGeometry(p.Center, p.TemplateWidth * p.Scale, p.TemplateHeight * p.Scale, p.AngleRadians), 0xFF33BBFF, "试匹配"));
        if (!trial)
        {
            if (BuildVerificationResult is { Transform: { } verified } result && result.FrameId == frame.FrameId)
            {
                visuals.Add(new Visual("build-check", new RectangleGeometry(verified.Center, verified.TemplateWidth * verified.Scale, verified.TemplateHeight * verified.Scale, verified.AngleRadians), 0xFF55DD77, $"制作自检 · 分数 {result.Score:F5}"));
                visuals.Add(new Visual("build-check-center", new ContourGeometry(new[] { new PointD(verified.Center.X - 5, verified.Center.Y), new PointD(verified.Center.X + 5, verified.Center.Y) }), 0xFF55DD77, "自检匹配中心"));
                visuals.Add(new Visual("build-check-center-y", new ContourGeometry(new[] { new PointD(verified.Center.X, verified.Center.Y - 5), new PointD(verified.Center.X, verified.Center.Y + 5) }), 0xFF55DD77));
            }
            visuals.Add(new Visual("origin", new ContourGeometry(new[] { new PointD(_originX, _originY) }), 0xFFFFCC33, "参考原点"));
            visuals.Add(new Visual("axis", new ContourGeometry(new[] { new PointD(_originX, _originY), new PointD(_originX + 30 * Math.Cos(_axisAngle), _originY + 30 * Math.Sin(_axisAngle)) }), 0xFFFFCC33, "参考X方向"));
        }
        layers.Add(new CanvasLayer("template", ELayerKind.Annotation, visuals));
        return new CanvasFrame(frame.FrameId, sequence, frame.Image, new GeometryOverlay(frame.FrameId, layers));
    }
    /// <inheritdoc/>
    public void Dispose()
    { if (_disposed) return; _disposed = true; _lifetime.Cancel(); Editor.DocumentChanged -= OnChanged; Editor.Cancel(); _source?.Dispose(); _trialFrame?.Dispose(); _lifetime.Dispose(); }
}
