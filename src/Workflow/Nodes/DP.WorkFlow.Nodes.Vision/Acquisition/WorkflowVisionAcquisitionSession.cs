using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>冻结文件列表后的文件夹游标能力；是否跨根运行继续由节点配置决定。</summary>
public interface IWorkflowVisionFolderSource
{
    /// <summary>读取下一张，只有解码成功后推进游标。</summary>
    /// <param name="nodeId">已准备节点身份。</param>
    /// <param name="token">取消令牌。</param>
    /// <returns>调用者拥有的图像。</returns>
    Task<IImageSource> NextAsync(string nodeId, CancellationToken token);
}

/// <summary>文件序列管理与节点选择的解码器分离；成功解码后才推进游标。</summary>
public interface IWorkflowVisionAlgorithmFolderSource : IWorkflowVisionFolderSource
{
    /// <summary>以调用者提供的节点绑定读取下一张；调用者拥有返回图像。</summary>
    Task<IImageSource> NextAsync(string nodeId, Func<string, CancellationToken, Task<IImageSource>> read, CancellationToken token);
}

/// <summary>在运行准备时冻结目录清单；各节点独立游标并串行推进。准备阶段只校验并产出候选清单，由 <see cref="IWorkflowRunResourceOwner.ReleasePreviousRunAsync"/> 启用。</summary>
public sealed class WorkflowVisionAcquisitionSession(IImageFileReader reader, IWorkflowRunPreparationService? next = null)
    : IWorkflowVisionAlgorithmFolderSource, IWorkflowRunPreparationService, IWorkflowRunResourceOwner
{
    private readonly IImageFileReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private Dictionary<string, Sequence> _sequences = new(StringComparer.Ordinal);
    private Dictionary<string, Sequence>? _pending;

    /// <inheritdoc/>
    /// <remarks>
    /// 本方法只校验并产出候选清单，不启用它；嵌套运行不能重置根运行正在使用的游标（AR-27）。
    /// 根运行激活时按节点配置复用或重置序列，启用由 <see cref="ReleasePreviousRunAsync"/> 完成。
    /// </remarks>
    public async ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var prepared = new Dictionary<string, Sequence>(StringComparer.Ordinal);
        foreach (var node in context.Nodes)
        {
            var folder = node switch
            {
                AcquireVisionImageNodeModel { SourceMode: EWorkflowVisionImageSource.Folder } input =>
                    (input.FolderPath, input.Extensions, input.Loop, Restart: input.RestartFolderEachRun),
                _ => default
            };
            if (folder.FolderPath is null) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var extensions = folder.Extensions.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.StartsWith('.') ? e : "." + e).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var path = Path.GetFullPath(folder.FolderPath);
            var filter = string.Join(";", extensions.OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase));
            var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                .Where(f => extensions.Contains(Path.GetExtension(f))).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0) throw new InvalidOperationException($"图像文件夹没有匹配文件：{folder.FolderPath}");
            var sequence = !folder.Restart && _sequences.TryGetValue(node.Id, out var previous)
                && previous.Loop == folder.Loop && StringComparer.OrdinalIgnoreCase.Equals(previous.FolderPath, path)
                && StringComparer.OrdinalIgnoreCase.Equals(previous.Filter, filter) && previous.Files.SequenceEqual(files, StringComparer.Ordinal)
                ? previous : new Sequence(files, folder.Loop, path, filter);
            // 根/子文档重用同一节点ID会产生歧义，明确拒绝而非共享错误游标。
            if (!prepared.TryAdd(node.Id, sequence))
                throw new InvalidOperationException($"文件夹节点ID跨文档重复：{node.Id}");
        }
        if (next is not null) await next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);

        // 只有根运行会把候选清单变成生效清单，而根运行宿主的调用顺序是"先准备、后释放"，
        // 因此这里暂存即可；嵌套运行同样会走到这里，但它不会触发 Release，候选自然被下一轮覆盖。
        _pending = prepared;
    }

    /// <summary>启用本次准备产出的清单，复用符合继续读取条件的游标，退役其他序列。</summary>
    /// <param name="cancellationToken">宿主取消本次运行时触发的令牌。</param>
    /// <returns>清单切换完成时结束的异步操作。</returns>
    public async ValueTask ReleasePreviousRunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (next is IWorkflowRunResourceOwner nextOwner)
            await nextOwner.ReleasePreviousRunAsync(cancellationToken).ConfigureAwait(false);
        if (_pending is null)
            return;
        _sequences = _pending;
        _pending = null;
    }

    /// <inheritdoc/>
    public Task<IImageSource> NextAsync(string nodeId, CancellationToken token) => NextAsync(nodeId, _reader.ReadAsync, token);

    /// <inheritdoc/>
    public async Task<IImageSource> NextAsync(string nodeId, Func<string, CancellationToken, Task<IImageSource>> read, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!_sequences.TryGetValue(nodeId, out var sequence)) throw new InvalidOperationException("文件夹节点尚未进行运行准备。");
        await sequence.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (sequence.Index >= sequence.Files.Length) throw new InvalidOperationException("图像文件序列已经结束。");
            var image = await read(sequence.Files[sequence.Index], token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                image.Dispose();
                token.ThrowIfCancellationRequested();
            }
            sequence.Index++;
            if (sequence.Loop && sequence.Index == sequence.Files.Length) sequence.Index = 0;
            return image;
        }
        finally { sequence.Gate.Release(); }
    }

    private sealed class Sequence(string[] files, bool loop, string folderPath, string filter)
    {
        public readonly string[] Files = files;
        public readonly bool Loop = loop;
        public readonly string FolderPath = folderPath;
        public readonly string Filter = filter;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Index;
    }
}
