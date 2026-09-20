using DP.Vision;

namespace DP.WorkFlow;

/// <summary>一次运行及结果查看期间的帧租约拥有者。</summary>
public interface IWorkflowVisionFrameScope
{
    /// <summary>保留独立帧租约，返回值只供借用。</summary>
    /// <param name="frame">输入帧。</param>
    /// <returns>作用域拥有的新句柄。</returns>
    ImageFrame Retain(ImageFrame frame);
}

/// <summary>迟到订阅者使用的最新节点图像及事实源。</summary>
public interface IWorkflowVisionPreviewSource
{
    /// <summary>读取独立租约快照，无结果时为空；调用者释放。</summary>
    /// <param name="nodeId">节点身份。</param>
    /// <returns>同帧图像与事实。</returns>
    WorkflowVisionPreview? Capture(string nodeId);
}

/// <summary>预览快照；只保留自身图像租约，不拥有不可变算法事实。</summary>
public sealed class WorkflowVisionPreview : IDisposable
{
    internal WorkflowVisionPreview(ImageFrame frame, object? facts, long sequence) { Frame = frame; Facts = facts; Sequence = sequence; }
    /// <summary>此快照拥有的帧。</summary>
    public ImageFrame Frame { get; }
    /// <summary>同帧结果；采集时为空。</summary>
    public object? Facts { get; }
    /// <summary>单调预览序号，跨重跑不归零。</summary>
    public long Sequence { get; }
    /// <inheritdoc/>
    public void Dispose() => Frame.Dispose();
}

/// <summary>有界运行帧仓和最新预览源。根运行开始时释放上一轮仓内租约，使结果查看窗口结束后自然回收；根运行内部的嵌套运行只校验、不清空。UI已Retain的快照不受影响。</summary>
public sealed class WorkflowVisionFrameScope : IWorkflowVisionFrameScope, IWorkflowVisionPreviewSource, IWorkflowRunPreparationService, IDisposable
{
    private readonly object _gate = new();
    private readonly List<ImageFrame> _frames = new();
    private readonly Dictionary<string, WorkflowVisionPreview> _previews = new(StringComparer.Ordinal);
    private readonly IWorkflowRunPreparationService? _next;
    private readonly long _maximumBytes;
    private readonly int _maximumFrames;
    private long _bytes, _sequence;
    private bool _disposed;

    /// <summary>创建结果仓，可组合已有视觉运行准备服务；达到预算明确失败，不释放仍被下游借用的输出。</summary>
    /// <param name="next">可选后续准备服务。</param>
    /// <param name="maximumBytes">单次运行采集像素总预算。</param>
    /// <param name="maximumFrames">单次运行输出帧数上限。</param>
    public WorkflowVisionFrameScope(IWorkflowRunPreparationService? next = null, long maximumBytes = 512L * 1024 * 1024, int maximumFrames = 1024)
    {
        if (maximumBytes < 1 || maximumFrames < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _next = next; _maximumBytes = maximumBytes; _maximumFrames = maximumFrames;
    }

    /// <inheritdoc/>
    public ImageFrame Retain(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_frames.Count >= _maximumFrames || frame.Image.Info.ByteLength > _maximumBytes - _bytes)
                throw new InvalidOperationException("本次运行图像输出超过资源预算；不能静默淘汰仍可被绑定读取的帧。");
            var retained = frame.Retain();
            _frames.Add(retained); _bytes += frame.Image.Info.ByteLength;
            return retained;
        }
    }

    internal static void Publish(IWorkflowNodeExecutionContext context, ImageFrame frame, object? facts = null)
    {
        if (facts is null && context.Node is AnalyzeVisionFrameNodeModel and not PreprocessVisionImageNodeModel)
            throw new InvalidOperationException("视觉分析能力返回了空结果。");
        var identity = facts switch
        {
            ImageFrame result => result.FrameId,
            DP.Vision.Algorithms.RegionAnalysisResult result => result.FrameId,
            DP.Vision.Algorithms.CaliperResult result => result.FrameId,
            DP.Vision.Algorithms.RobustLineResult result => result.FrameId,
            DP.Vision.Algorithms.TemplatePoseResult result => result.FrameId,
            DP.Vision.Algorithms.BlobAnalysisResult result => result.FrameId,
            DP.Vision.Algorithms.ColorAnalysisResult result => result.FrameId,
            DP.Vision.Algorithms.EdgeMeasurementResult result => result.FrameId,
            DP.Vision.Algorithms.TemplateLocationResult result => result.FrameId,
            _ => null
        };
        if (identity is not null && !string.Equals(identity, frame.FrameId, StringComparison.Ordinal))
            throw new InvalidOperationException("视觉算法事实与输入帧身份不一致，禁止提交或叠加到另一张图像。");
        if (context.Services.GetService(typeof(IWorkflowVisionFrameScope)) is WorkflowVisionFrameScope scope)
            scope.Publish(context.Node.Id, frame, facts);
    }

    private void Publish(string nodeId, ImageFrame frame, object? facts)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var next = new WorkflowVisionPreview(frame.Retain(), facts, ++_sequence);
            if (_previews.Remove(nodeId, out var old)) old.Dispose();
            _previews.Add(nodeId, next);
        }
    }

    /// <inheritdoc/>
    public WorkflowVisionPreview? Capture(string nodeId)
    {
        lock (_gate)
        {
            if (_disposed || !_previews.TryGetValue(nodeId, out var current)) return null;
            return new WorkflowVisionPreview(current.Frame.Retain(), current.Facts, current.Sequence);
        }
    }

    /// <inheritdoc/>
    public async ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var duplicate = context.Nodes.Where(n => n is AnalyzeVisionFrameNodeModel or LoadVisionFileNodeModel
                or LoadVisionFolderNodeModel or CaptureVisionFrameNodeModel)
            .GroupBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"新版视觉预览节点ID跨子文档重复：{duplicate.Key}；不能把不同节点的图像合并到同一预览槽。");
        if (_next is not null) await _next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);

        // 释放上一轮资源只有根运行才做。嵌套运行属于本轮内部，仓内帧仍被根运行的节点输出引用，
        // 此时清空会让父输出指向已释放的图像（ObjectDisposedException）。
        if (context.ScopeKind != WorkflowRunScopeKind.Root)
            return;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Clear();
        }
    }

    private void Clear()
    {
        foreach (var frame in _frames) frame.Dispose();
        foreach (var preview in _previews.Values) preview.Dispose();
        _frames.Clear(); _previews.Clear(); _bytes = 0;
    }

    /// <summary>使用者结束借用后释放；并行持有者须保留独立租约。</summary>
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; Clear(); }
    }
}
