using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>冻结文件列表后的单次运行文件夹游标能力。</summary>
public interface IWorkflowVisionFolderSource
{
    /// <summary>读取下一张，只有解码成功后推进游标。</summary>
    /// <param name="nodeId">已准备节点身份。</param>
    /// <param name="token">取消令牌。</param>
    /// <returns>调用者拥有的图像。</returns>
    Task<IImageSource> NextAsync(string nodeId, CancellationToken token);
}

/// <summary>在运行准备时冻结目录清单；各节点独立游标并串行推进。根运行开始时冻结并归零，根运行内部的嵌套运行只校验、不重置。</summary>
public sealed class WorkflowVisionAcquisitionSession(IImageFileReader reader, IWorkflowRunPreparationService? next = null)
    : IWorkflowVisionFolderSource, IWorkflowRunPreparationService
{
    private readonly IImageFileReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private Dictionary<string, Sequence> _sequences = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
    {
        var prepared = new Dictionary<string, Sequence>(StringComparer.Ordinal);
        foreach (var node in context.Nodes.OfType<LoadVisionFolderNodeModel>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extensions = node.Extensions.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.StartsWith('.') ? e : "." + e).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = Directory.EnumerateFiles(node.FolderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(f => extensions.Contains(Path.GetExtension(f))).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0) throw new InvalidOperationException($"图像文件夹没有匹配文件：{node.FolderPath}");
            // 根/子文档重用同一节点ID会产生歧义，明确拒绝而非共享错误游标。
            if (!prepared.TryAdd(node.Id, new Sequence(files, node.Loop)))
                throw new InvalidOperationException($"文件夹节点ID跨文档重复：{node.Id}");
        }
        if (next is not null) await next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);

        // 冻结清单与游标归零只属于根运行。嵌套运行属于本轮内部：重置游标会让根运行重复消费
        // 已经处理过的图像，重新冻结清单还会让运行中的文件列表中途变化。
        if (context.ScopeKind != WorkflowRunScopeKind.Root)
            return;

        _sequences = prepared;
    }

    /// <inheritdoc/>
    public async Task<IImageSource> NextAsync(string nodeId, CancellationToken token)
    {
        if (!_sequences.TryGetValue(nodeId, out var sequence)) throw new InvalidOperationException("文件夹节点尚未进行运行准备。");
        await sequence.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (sequence.Index >= sequence.Files.Length) throw new InvalidOperationException("图像文件序列已经结束。");
            var image = await _reader.ReadAsync(sequence.Files[sequence.Index], token).ConfigureAwait(false);
            sequence.Index++;
            if (sequence.Loop && sequence.Index == sequence.Files.Length) sequence.Index = 0;
            return image;
        }
        finally { sequence.Gate.Release(); }
    }

    private sealed class Sequence(string[] files, bool loop)
    {
        public readonly string[] Files = files;
        public readonly bool Loop = loop;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Index;
    }
}

/// <summary>新版本文件夹输入；默认到达末尾失败而不是悄悄重复图像。</summary>
[WorkflowNode("Vision.LoadFolder", DisplayName = "顺序读取图像目录", Category = "5.Vision/ImageBuffer")]
public sealed class LoadVisionFolderNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.LoadFolder";
    /// <summary>静态目录。</summary>
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FolderPath)]
    public string FolderPath { get; set; } = string.Empty;
    /// <summary>分号分隔的扩展名。</summary>
    public string Extensions { get; set; } = ".png;.bmp;.jpg;.jpeg;.tif;.tiff";
    /// <summary>明确请求循环。</summary>
    public bool Loop { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration() => !Directory.Exists(FolderPath) || string.IsNullOrWhiteSpace(Extensions)
        ? new[] { "图像目录不存在或扩展名为空。" } : Array.Empty<string>();
}

/// <summary>通过运行级清单读取下一张。</summary>
public sealed class LoadVisionFolderNodeHandler : WorkflowNodeHandler<LoadVisionFolderNodeModel>
{
    /// <inheritdoc/>
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(LoadVisionFolderNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        using var image = await context.GetRequiredCapability<IWorkflowVisionFolderSource>().NextAsync(node.Id, cancellationToken).ConfigureAwait(false);
        return LoadVisionFileNodeHandler.Output(image, context, cancellationToken);
    }
}
