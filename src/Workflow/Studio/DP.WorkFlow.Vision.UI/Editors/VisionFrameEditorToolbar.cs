using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

/// <summary>“加载”下拉的图像来源。</summary>
public enum EVisionEditorImageSource
{
    /// <summary>上游节点最近一次运行的图像。</summary>
    Upstream,
    /// <summary>手动读取的图像文件或文件夹中的一张。</summary>
    File
}

/// <summary>“卡尺”下拉的卡尺布局。</summary>
public enum EVisionCaliperLayout
{
    /// <summary>沿直线排布（直线卡尺、找线）。</summary>
    Line,
    /// <summary>沿扇环/圆环径向排布（圆弧卡尺、找圆）。</summary>
    Annulus,
    /// <summary>沿矩形四边排布（找矩形）。</summary>
    Rectangle
}

/// <summary>“卡尺”下拉的一项。</summary>
/// <param name="Layout">卡尺布局。</param><param name="Text">显示文字。</param>
public sealed record VisionCaliperLayoutChoice(EVisionCaliperLayout Layout, string Text)
{
    /// <summary>直线。</summary>
    public static VisionCaliperLayoutChoice Line { get; } = new(EVisionCaliperLayout.Line, "线卡尺");
    /// <summary>扇环。</summary>
    public static VisionCaliperLayoutChoice Annulus { get; } = new(EVisionCaliperLayout.Annulus, "扇环卡尺");
    /// <summary>矩形。</summary>
    public static VisionCaliperLayoutChoice Rectangle { get; } = new(EVisionCaliperLayout.Rectangle, "矩形卡尺");
    /// <summary>全部布局，图标与宽度按此列表准备。</summary>
    public static IReadOnlyList<VisionCaliperLayoutChoice> All { get; } = Array.AsReadOnly(new[] { Line, Annulus, Rectangle });
    /// <summary>下拉框图标键。</summary>
    public string IconKey => "caliper-" + Layout;
    /// <inheritdoc/>
    public override string ToString() => Text;
}

// 工具栏“加载”和“运行”：输入图像可换成本地文件/文件夹图像，运行只执行本节点一次，结果只显示在本窗口。
public sealed partial class VisionFrameEditorPageModel
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    private readonly WorkflowNodeTrialRunner? _runner;
    private IReadOnlyList<string> _folder = Array.Empty<string>();
    private int _folderIndex = -1;
    private string? _manualPath;
    private TrialResult? _trial;
    private long _trialVersion;
    private int _running;

    private sealed record TrialResult(ImageFrame Frame, object? Facts, long? BaseSequence, string Text);

    /// <summary>输入图像来源；选“文件”且已读图时，输入图像和运行都使用手动图像。</summary>
    public EVisionEditorImageSource ImageSource { get; private set; }

    /// <summary>当前手动图像在文件夹中的位置说明，如“3/20 a.png”；没有手动图像时为空。</summary>
    public string ImageSourceText => _manualPath is null ? "" : _folder.Count > 0
        ? $"{_folderIndex + 1}/{_folder.Count} {Path.GetFileName(_manualPath)}" : Path.GetFileName(_manualPath);

    /// <summary>是否可以读取本地图像（宿主配置了读图器）。</summary>
    public bool CanLoadFiles => _reader is not null && !IsTemplateEditor;

    /// <summary>已加载文件夹时可前后切换。</summary>
    public bool CanStepFolder => _folder.Count > 1;

    /// <summary>是否支持“运行本节点”：宿主提供了试运行器且为分析类节点（采集节点不在编辑器里触发设备）。</summary>
    public bool CanRun => _runner is not null && !IsTemplateEditor && _node is AnalyzeVisionFrameNodeModel;

    /// <summary>是否正在运行本节点。</summary>
    public bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// “卡尺”下拉的可选布局：卡尺测量可在线/扇环之间切换；找线、找圆、找矩形各自固定一种布局；没有卡尺的节点为空（隐藏下拉）。
    /// </summary>
    public IReadOnlyList<VisionCaliperLayoutChoice> CaliperLayouts => Caliper switch
    {
        VisionCaliperGizmo => new[] { VisionCaliperLayoutChoice.Line, VisionCaliperLayoutChoice.Annulus },
        VisionFindShapeGizmo when _node is FindVisionCircleNodeModel => new[] { VisionCaliperLayoutChoice.Annulus },
        VisionFindShapeGizmo => new[] { VisionCaliperLayoutChoice.Line },
        _ => Array.Empty<VisionCaliperLayoutChoice>()
    };

    /// <summary>当前卡尺布局；没有卡尺时为空。</summary>
    public VisionCaliperLayoutChoice? CaliperLayout => Caliper is VisionCaliperGizmo caliper
        ? caliper.Shape == EVisionCaliperShape.Arc ? VisionCaliperLayoutChoice.Annulus : VisionCaliperLayoutChoice.Line
        : CaliperLayouts.FirstOrDefault();

    /// <summary>切换卡尺布局（只有卡尺测量节点可切换），保持在图上的位置；变化时返回 true 并通知参数页。</summary>
    /// <param name="layout">目标布局。</param>
    public bool SetCaliperLayout(EVisionCaliperLayout layout)
    {
        if (Caliper is not VisionCaliperGizmo caliper || layout == EVisionCaliperLayout.Rectangle) return false;
        if (!caliper.SetShape(layout == EVisionCaliperLayout.Annulus ? EVisionCaliperShape.Arc : EVisionCaliperShape.Line)) return false;
        NotifyConfigurationChanged(); InvalidatePreview();
        return true;
    }

    /// <summary>“ROI”下拉是否显示：能编辑面积/矩形范围或在制作模板区域时显示；卡尺类节点由卡尺下拉编辑。</summary>
    /// <param name="templateMaking">当前是否在编辑模板制作区域。</param>
    public bool ShowsRegionTools(bool templateMaking) => templateMaking || (Caliper is null && CanEdit);

    /// <summary>输入图像改回上游运行结果。</summary>
    public void UseUpstreamImage()
    {
        lock (_gate) { ImageSource = EVisionEditorImageSource.Upstream; ClearTrialLocked(); _lastKey = null; }
        Status = "输入图像：上游最近一次运行结果。";
    }

    /// <summary>读取一张图像作为输入。</summary>
    /// <param name="path">图像文件。</param>
    public async Task LoadImageFileAsync(string path)
    {
        await ReadPreviewAsync(path).ConfigureAwait(false);
        lock (_gate) { _folder = Array.Empty<string>(); _folderIndex = -1; SetManualLocked(path); }
    }

    /// <summary>读取文件夹中的图像（按文件名排序），从第一张开始。</summary>
    /// <param name="directory">文件夹。</param>
    public async Task LoadFolderAsync(string directory)
    {
        var files = Directory.EnumerateFiles(directory)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("文件夹中没有 png/jpg/bmp/tif 图像。");
        await ReadPreviewAsync(files[0]).ConfigureAwait(false);
        lock (_gate) { _folder = files; _folderIndex = 0; SetManualLocked(files[0]); }
    }

    /// <summary>切换到文件夹中的上一张/下一张，首尾循环。</summary>
    /// <param name="delta">-1 上一张，1 下一张。</param>
    public async Task StepFolderAsync(int delta)
    {
        string path; int index;
        lock (_gate)
        {
            if (_folder.Count == 0) throw new InvalidOperationException("请先加载文件夹图像。");
            index = ((_folderIndex + delta) % _folder.Count + _folder.Count) % _folder.Count;
            path = _folder[index];
        }
        await ReadPreviewAsync(path).ConfigureAwait(false);
        lock (_gate) { _folderIndex = index; SetManualLocked(path); }
    }

    private void SetManualLocked(string path)
    {
        _manualPath = path; ImageSource = EVisionEditorImageSource.File; ClearTrialLocked(); _lastKey = null;
        Status = $"输入图像：{ImageSourceText}。点“运行”用这张图执行本节点。";
    }

    private void ClearTrialLocked() { _trial?.Frame.Dispose(); _trial = null; _trialVersion++; }

    // 输入图像视图实际显示的手动帧；没有选文件来源时为空。
    private ImageFrame? ManualInputLocked => ImageSource == EVisionEditorImageSource.File ? _manual : null;

    /// <summary>
    /// 用编辑中的参数执行本节点一次：输入帧取手动图像或上游最近一次运行的图像，其他绑定取最近一次运行的输出；
    /// 结果只在本窗口“结果图像”中显示，不写入流程，下一次流程运行后让位给运行结果。
    /// </summary>
    public async Task RunOnceAsync()
    {
        var runner = _runner ?? throw new InvalidOperationException("宿主没有提供单节点运行。");
        if (_node is not AnalyzeVisionFrameNodeModel node) throw new InvalidOperationException("此节点不支持在编辑器中运行。");
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        ImageFrame? input = null;
        try
        {
            long? baseline;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var own = _frames?.Capture(_node.Id);
                baseline = own?.Sequence;
                if (ManualInputLocked is { } manual) input = manual.Retain();
                else if (node.Frame.Binding is { IsPublicData: false } binding)
                {
                    using var upstream = _frames?.Capture(binding.NodeId);
                    input = upstream?.Frame.Retain() ?? throw new InvalidOperationException("上游还没有图像：先运行一次流程，或从“加载”读取图像。");
                }
            }
            var overrides = input is null ? null : new Dictionary<object, object?>(ReferenceEqualityComparer.Instance) { [node.Frame] = input };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            NodeExecutionResult result;
            try { result = await Task.Run(() => runner.RunAsync(node, overrides, _lifetime.Token)).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                var hint = ImageSource == EVisionEditorImageSource.File && (node.Coordinates is not null || node.Mask.Binding is not null)
                    ? "（手动图像不能使用上游其他帧的坐标系/掩码，请先运行流程或改回上游图像）" : "";
                throw new InvalidOperationException("运行失败：" + error.Message + hint, error);
            }
            if (!result.Success) throw new InvalidOperationException("运行失败：" + (result.Message ?? "节点返回失败。"));
            var facts = result.Output is ImageFrame ? null : result.Output;
            var shown = result.Output is ImageFrame output ? output.Retain() : input?.Retain();
            if (shown is null) { Status = $"已运行本节点（{watch.ElapsedMilliseconds} ms），输入图像不是节点输出，无法叠加显示。"; return; }
            lock (_gate)
            {
                if (_disposed) { shown.Dispose(); return; }
                ClearTrialLocked();
                _trial = new TrialResult(shown, facts, baseline, $"已运行本节点（{watch.ElapsedMilliseconds} ms，未写入流程）。");
                _lastKey = null;
            }
        }
        finally { input?.Dispose(); Volatile.Write(ref _running, 0); }
    }
}
