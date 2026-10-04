namespace DP.WorkFlow.Vision.UI;

public sealed partial class VisionTemplateEditorModel
{
    private string? _failure;
    private bool _testing;
    private long _validatedGeneration = -1;
    private string _geometryIssue = "";
    /// <summary>参数/样图/区域的版本，用于刷新属性显示。</summary>
    public long EditRevision => _generation;
    /// <summary>获取制作样图的独立帧租约；调用者负责释放。</summary>
    public DP.Vision.ImageFrame RetainSample() => _source?.Retain() ?? throw new InvalidOperationException("请先读取样图。");
    /// <summary>已加载的原始样图概要。</summary>
    public string SourceSummary => _source == null ? "未读取样图" : $"{_source.Image.Info.Width} × {_source.Image.Info.Height} · {_source.Image.Info.Layout}";
    /// <summary>是否已经读取制作样图。</summary>
    public bool HasSample => _source != null;
    /// <summary>制作状态，不混用测试结果或画布状态。</summary>
    public string BuildState => _busy && !_testing ? "正在生成模型" : IsBuilt ? "模型已就绪" : _source == null ? "等待样图" : "需要生成模型";
    /// <summary>最后失败原因，持续保留到下次操作或配置修改。</summary>
    public string Failure => _failure ?? "";
    /// <summary>将操作失败显式反馈给两种平台的状态区域。</summary>
    public void ReportFailure(Exception error) { if (_disposed) return; _failure = error.Message; Status = "操作失败：" + error.Message; }
    /// <summary>生成按钮不可用的原因；有效区域检查按草稿版本缓存。</summary>
    public string BuildBlockReason
    {
        get
        {
            if (_disposed) return "编辑器已关闭。";
            if (_busy) return "正在执行操作，请等待完成。";
            if (_runtime == null || !Choices.Any(c => c.ImplementationId == _implementation)) return "当前制作引擎未安装或不兼容。";
            if (_source == null) return "请先读取样图或使用当前输入。";
            if (Editor.IsEditing) return "请先完成当前ROI绘制。";
            if (_validatedGeneration != _generation)
            {
                _validatedGeneration = _generation;
                try { _ = Definition(); _geometryIssue = ""; }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { _geometryIssue = ex.Message; }
            }
            return _geometryIssue;
        }
    }
    /// <summary>测试按钮不可用的共同原因，图像与搜索范围由调用者进一步校验。</summary>
    public string TestBlockReason => _disposed ? "编辑器已关闭。" : _busy ? "正在执行操作，请等待完成。"
        : Editor.IsEditing ? "请先完成当前ROI绘制。" : !IsBuilt ? "请为当前制作配置生成模型。" : "";
    /// <summary>是否可以生成当前模板。</summary>
    public bool CanBuild => BuildBlockReason.Length == 0;
    /// <summary>是否具有可测试的模型。</summary>
    public bool CanTest => TestBlockReason.Length == 0;
    /// <summary>应用按钮不可用的原因。</summary>
    public string ApplyBlockReason => TestBlockReason.Length != 0 ? TestBlockReason
        : _dirty && _runtime?.Resources?.RecipeDirectory == null ? "请先保存配方，再应用模板资源。" : "";
    /// <summary>模型有效且资源发布上下文完整时允许确认。</summary>
    public bool CanApply => ApplyBlockReason.Length == 0;
    /// <summary>测试状态，未检出与执行失败明确区分。</summary>
    public string TestState => _testing ? "正在试匹配" : _trial == null ? "尚未测试" : _trial.Found ? "已找到目标" : "未找到目标";
    /// <summary>测试条件改变后清除旧结果，保留已经生成的模型。</summary>
    public void ResetTrial() { _trial = null; _trialFrame?.Dispose(); _trialFrame = null; _failure = null; }
    /// <summary>测试结果的分数、位置、角度和尺度。</summary>
    public string TestSummary => _trial == null ? "生成模型后选择测试图像，再进行试匹配。"
        : _trial.Transform is { } pose ? $"分数 {_trial.Score:F5} · 位置 ({pose.Center.X:F2}, {pose.Center.Y:F2})\n角度 {pose.AngleRadians * 180 / Math.PI:F2}° · 尺度 {pose.Scale:F4}"
        : $"分数 {_trial.Score:F5}；搜索完成，但没有达到阈值的目标。";
}
