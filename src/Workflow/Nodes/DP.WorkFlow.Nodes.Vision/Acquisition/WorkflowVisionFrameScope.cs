using System.Linq;
using DP.Vision;
using DP.Vision.Acquisition;

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
    internal WorkflowVisionPreview(ImageFrame frame, object? facts, long sequence, long executionSequence)
    {
        Frame = frame; Facts = facts; Sequence = sequence; ExecutionSequence = executionSequence;
    }
    /// <summary>此快照拥有的帧。</summary>
    public ImageFrame Frame { get; }
    /// <summary>同帧结果；采集时为空。</summary>
    public object? Facts { get; }
    /// <summary>单调预览序号，跨重跑不归零。</summary>
    public long Sequence { get; }
    /// <summary>产生本预览的运行输出提交序号；该序号起失效时必须同步撤销。</summary>
    public long ExecutionSequence { get; }
    /// <inheritdoc/>
    public void Dispose() => Frame.Dispose();
}

/// <summary>有界运行帧仓和最新预览源。作为 <see cref="IWorkflowRunResourceOwner"/> 在根运行开始时释放上一轮仓内租约，使结果查看窗口结束后自然回收；准备阶段只校验，不清空。UI已Retain的快照不受影响。</summary>
public sealed class WorkflowVisionFrameScope : IWorkflowVisionFrameScope, IWorkflowVisionPreviewSource, IWorkflowNodeOutputProjectionSink, IWorkflowRunPreparationService, IWorkflowRunResourceOwner, IDisposable
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

    /// <summary>
    /// 登记本次执行的预览投影。预览是"已提交输出"的派生投影，只有节点输出正式提交后才会发布；
    /// 校验仍在Handler内立即执行，避免把同帧身份不一致推迟到提交之后。
    /// </summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="frame">本次执行的帧，必须与事实同帧。</param>
    /// <param name="facts">可选的同帧算法事实。</param>
    /// <returns>提交后发布的投影；宿主没有注册帧作用域时为空。</returns>
    internal static IWorkflowNodeOutputProjection? Stage(IWorkflowNodeExecutionContext context, ImageFrame frame, object? facts = null)
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
            return new VisionPreviewProjection(scope, context.Node.Id, frame, facts);
        return null;
    }

    private void Publish(string nodeId, ImageFrame frame, object? facts, long executionSequence)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var next = new WorkflowVisionPreview(frame.Retain(), facts, ++_sequence, executionSequence);
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
            return new WorkflowVisionPreview(current.Frame.Retain(), current.Facts, current.Sequence, current.ExecutionSequence);
        }
    }

    /// <summary>运行输出失效时同步撤销对应预览，不让界面继续显示已不可绑定的结果。</summary>
    /// <param name="executionSequence">失效区间的起始序号，含该序号本身。</param>
    public void InvalidateFrom(long executionSequence)
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var pair in _previews.Where(item => item.Value.ExecutionSequence >= executionSequence).ToArray())
                if (_previews.Remove(pair.Key, out var removed)) removed.Dispose();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 本方法**只校验，不释放任何既有资源**：仓内帧仍被本轮运行的节点输出引用。
    /// 退役上一轮租约由 <see cref="ReleasePreviousRunAsync"/> 承担，只有根运行宿主持有该接口。
    /// </remarks>
    public async ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var duplicate = context.Nodes.Where(n => n is AnalyzeVisionFrameNodeModel or LoadVisionFileNodeModel
                or LoadVisionFolderNodeModel or CaptureVisionFrameNodeModel)
            .GroupBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"新版视觉预览节点ID跨子文档重复：{duplicate.Key}；不能把不同节点的图像合并到同一预览槽。");
        ValidateCaptureNodes(context);
        if (_next is not null) await _next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 退役上一轮运行留下的仓内租约与预览投影。只有根运行宿主会调用本方法：
    /// 嵌套调用点（恢复处置子流程、联合恢复参与者、告警协调器）不解析
    /// <see cref="IWorkflowRunResourceOwner"/>，因此在类型上无法触发这次清理。
    /// </summary>
    /// <param name="cancellationToken">宿主取消本次运行时触发的令牌。</param>
    /// <returns>清理完成时结束的异步操作。</returns>
    public async ValueTask ReleasePreviousRunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 链上的后续所有者（例如文件夹采集会话）先退役，再释放本仓：
        // 本仓持有的是本轮取到的帧租约，会话持有的是清单与游标，两者互不依赖。
        if (_next is IWorkflowRunResourceOwner nextOwner)
            await nextOwner.ReleasePreviousRunAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed) return;
            Clear();
        }
    }

    private void Clear()
    {
        foreach (var frame in _frames) frame.Dispose();
        foreach (var preview in _previews.Values) preview.Dispose();
        _frames.Clear(); _previews.Clear(); _bytes = 0;
    }

    /// <summary>
    /// 运行前校验采集节点的逻辑源绑定。这里不是最终互斥——真正取得设备使用权仍然只能在执行采集时
    /// 由Acquisition Runtime原子完成；此处只保证"源不存在"这类配置错误不会拖到首节点之后才暴露。
    /// </summary>
    private static void ValidateCaptureNodes(WorkflowRunPreparationContext context)
    {
        var captures = context.Nodes.OfType<CaptureVisionFrameNodeModel>().ToArray();
        if (captures.Length == 0)
            return;
        var catalog = context.Services?.GetService(typeof(IWorkflowVisionSourceCatalog)) as IWorkflowVisionSourceCatalog
            ?? throw new InvalidOperationException(
                "流程包含采集节点，但宿主没有发布逻辑源目录（IWorkflowVisionSourceCatalog）；"
                + "无法在运行前校验Source绑定，禁止开始执行。");
        var bufferedSources = new List<string>();
        foreach (var capture in captures)
        {
            var source = capture.Source
                ?? throw new InvalidOperationException($"采集节点 {capture.Id} 未配置逻辑图像源。");
            if (!catalog.TryGet(source.SourceId, out var info) || info is null)
                throw new InvalidOperationException(
                    $"采集节点 {capture.Id} 的逻辑源 {source.SourceId} 未在当前机器配置中发布；"
                    + "Provider失败时不会自动尝试其他源。");
            if (!info.IsAvailable)
                throw new InvalidOperationException(
                    $"采集节点 {capture.Id} 的逻辑源 {source.SourceId} 当前不可用（Provider {info.ProviderId}）："
                    + (info.Diagnostic ?? "未提供原因。"));
            // 参数不合法时同样在运行前拒绝，而不是等到设备已经打开之后。
            var request = capture.CreateRequest();

            if (info.AcquisitionMode == EVisionAcquisitionMode.BufferedExternal)
            {
                // 相机正在长期布防出图；改写曝光/增益会让已在途的帧参数不一致，因此运行前就拒绝，
                // 而不是等节点执行时才失败——那时设备已经打开。
                if (request.ExposureMicroseconds is not null || request.GainDecibels is not null)
                    throw new InvalidOperationException(
                        $"采集节点 {capture.Id} 的逻辑源 {source.SourceId} 是外部回调缓冲源（BufferedExternal），"
                        + "不支持节点级曝光/增益覆盖；这些参数由机器 Source/Profile 固定，"
                        + "请在机器配置里修改并重新发布，而不是逐节点覆盖。");
                bufferedSources.Add(source.SourceId);
                continue;
            }

            if (info.SharingPolicy == EVisionSourceSharingPolicy.ExclusiveRun)
                throw new InvalidOperationException(
                    $"采集节点 {capture.Id} 的逻辑源 {source.SourceId} 是主动采集源却配置为 ExclusiveRun；"
                    + "ExclusiveRun 只用于外部回调缓冲源，主动采集源按操作级互斥协调。");
        }

        if (bufferedSources.Count == 0)
            return;

        // 没有根运行作用域所有者就不会建立采集代次，回调帧永远无法领取。
        // 与其让节点在领取处超时，不如在首节点之前说清楚缺什么。
        if (context.Services?.GetService(typeof(IWorkflowRunScopeOwner)) is null)
            throw new InvalidOperationException(
                $"流程使用了外部回调缓冲源（{string.Join("、", bufferedSources.OrderBy(id => id, StringComparer.Ordinal))}），"
                + "但宿主没有注册根运行作用域所有者（IWorkflowRunScopeOwner）；"
                + "没有它就不会在首节点之前布防并建立采集代次，回调帧永远无法领取。");
    }

    /// <summary>使用者结束借用后释放；并行持有者须保留独立租约。</summary>
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; Clear(); }
    }

    /// <summary>预览投影；借用帧作用域已拥有的帧，不额外持有租约，因此未提交时无需清理。</summary>
    private sealed class VisionPreviewProjection : IWorkflowNodeOutputProjection
    {
        private readonly WorkflowVisionFrameScope _scope;
        private readonly string _nodeId;
        private readonly ImageFrame _frame;
        private readonly object? _facts;

        internal VisionPreviewProjection(WorkflowVisionFrameScope scope, string nodeId, ImageFrame frame, object? facts)
        {
            _scope = scope; _nodeId = nodeId; _frame = frame; _facts = facts;
        }

        public void Commit(long executionSequence) => _scope.Publish(_nodeId, _frame, _facts, executionSequence);
    }
}
