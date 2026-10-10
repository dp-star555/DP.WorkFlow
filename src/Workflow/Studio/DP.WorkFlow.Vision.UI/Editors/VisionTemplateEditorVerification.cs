using System.Globalization;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

public sealed partial class VisionTemplateEditorModel
{
    private TemplatePoseResult? _buildVerification;
    private string? _buildVerificationFailure;
    private bool _verifyingBuild;
    private string _buildSearchSummary = "";
    private string _trialSearchSummary = "";

    /// <summary>当前模型在制作样图上的独立自检结果；后续测试不会覆盖它。</summary>
    public TemplatePoseResult? BuildVerificationResult => IsBuilt ? _buildVerification : null;
    /// <summary>区别模型生成与实际检出的状态。</summary>
    public string BuildVerificationState => _verifyingBuild ? "正在制作自检" : !IsBuilt ? "等待模型生成"
        : _buildVerificationFailure != null ? "制作自检失败" : _buildVerification == null ? "尚未进行制作自检"
        : _buildVerification.Found ? "制作自检通过 · 已找到目标" : "制作自检未找到目标";
    /// <summary>制作自检的真实搜索条件和匹配结果，不代表新图像的检测效果。</summary>
    public string BuildVerificationSummary => !IsBuilt ? "生成模型后自动在制作样图的制作区域内，以0°/1倍查找。"
        : _buildSearchSummary + (_buildVerificationFailure != null ? "\n" + _buildVerificationFailure
        : _buildVerification == null ? "\n已读取模型可直接自检或测试；无需重新生成。"
        : "\n" + DetectionSummary(_buildVerification));
    /// <summary>最近一次显式测试的实际图像及角度/尺度搜索范围。</summary>
    public string TrialSearchSummary => _trialSearchSummary;

    private void ResetBuildVerification()
    { _buildVerification = null; _buildVerificationFailure = null; _buildSearchSummary = ""; }

    private async Task VerifyBuiltAsync(ImageFrame source, VisionTemplateBuild build, string key, long generation)
    {
        _verifyingBuild = true;
        Status = "模型已生成，正在制作样图上查找…";
        try
        {
            var bounds = new PixelBounds(build.Definition.X, build.Definition.Y, build.Definition.Width, build.Definition.Height);
            var pose = (LocateVisionTemplatePoseNodeModel)_node;
            var options = new TemplatePoseOptions(0d, 0d, 1d, 1d, pose.MinimumScore, pose.MaximumWork, maximumCandidates: pose.MaximumCandidates);
            _buildSearchSummary = DescribeSearch(source, bounds, options);
            var result = await MatchModelAsync(build, source, bounds, options);
            if (_disposed || generation != _generation || key != Key()) return;
            _buildVerification = result;
            Status = result.Found ? "模型已生成，制作自检已找到目标；请继续用新图像测试。"
                : "模型已生成，但制作样图自检未找到目标；请检查制作ROI和参数。";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            if (_disposed || generation != _generation || key != Key()) return;
            _buildVerificationFailure = error.Message;
            ReportFailure(error);
        }
        finally { _verifyingBuild = false; }
    }

    private async Task<TemplatePoseResult> MatchModelAsync(VisionTemplateBuild build, ImageFrame input, PixelBounds bounds,
        TemplatePoseOptions options, RegionGeometry? region = null)
    {
        var issue = SearchIssue(bounds, options);
        if (issue.Length != 0) throw new InvalidOperationException(issue);
        using var resource = await _runtime!.PreviewAsync(build, _token);
        var matcher = resource.Instance as IPreparedVisionTemplateMatcher ?? throw new InvalidOperationException("预览模型类型无效。");
        return await Task.Run(() => matcher.Match(input, bounds, options, region, _token), _token);
    }

    private static string DescribeSearch(ImageFrame frame, PixelBounds bounds, TemplatePoseOptions options)
    {
        return $"图像 {frame.Image.Info.Width}×{frame.Image.Info.Height}；搜索 ({bounds.X}, {bounds.Y}) {bounds.Width}×{bounds.Height}\n"
            + $"角度范围 [{options.MinimumAngleRadians * 180 / Math.PI:G6}°, {options.MaximumAngleRadians * 180 / Math.PI:G6}°]；尺度范围 [{options.MinimumScale:G6}, {options.MaximumScale:G6}]；阈值 {options.MinimumScore:G6}";
    }

    private static string DetectionSummary(TemplatePoseResult result) => result.Found
        ? $"分数 {result.Score:F5} · 中心 ({result.CenterX:F2}, {result.CenterY:F2})\n角度 {result.AngleDegrees:F2}° · 尺度 {result.Scale:F4}"
        : "未返回达标候选；请检查制作区域、对比度/极性、阈值及搜索配置。";
}
