using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>
/// AR-01 同源回归：文件夹采集会话的"新运行归零"同样只能由根运行触发。
/// 嵌套运行重置游标会让根运行重复消费已经处理过的图像。
/// </summary>
public sealed class WorkflowVisionAcquisitionSessionRunScopeTests
{
    private static WorkflowRunPreparationContext Context(WorkflowRunScopeKind kind, params IWorkflowNodeModel[] nodes) =>
        new(nodes, kind);

    /// <summary>创建含 01.png / 02.png / 03.png 的临时目录，像素值即文件名序号。</summary>
    private static string CreateFolder(out LoadVisionFolderNodeModel node)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "01.png", "02.png", "03.png" })
            File.WriteAllText(Path.Combine(directory, name), name);
        node = new LoadVisionFolderNodeModel { Id = "folder", FolderPath = directory };
        return directory;
    }

    private static async Task<int> ReadAsync(WorkflowVisionAcquisitionSession session, string nodeId)
    {
        using var image = await session.NextAsync(nodeId, default);
        var pixel = new byte[1];
        image.CopyTo(0, pixel, 0, 1);
        return pixel[0];
    }

    [Fact]
    public async Task 嵌套准备不得重置根运行的文件夹游标()
    {
        string directory = CreateFolder(out var node);
        try
        {
            var session = new WorkflowVisionAcquisitionSession(new NumberedReader());
            await session.PrepareAsync(Context(WorkflowRunScopeKind.Root, node), default);
            Assert.Equal(1, await ReadAsync(session, node.Id));
            Assert.Equal(2, await ReadAsync(session, node.Id));

            // 本轮内部起嵌套运行（恢复处置子流程），复用同一 Services，因此是同一个会话。
            await session.PrepareAsync(Context(WorkflowRunScopeKind.Nested, node), default);

            // 修复前：游标被重置，这里会再次读到 01.png —— 生产上表现为重复检测同一张图。
            Assert.Equal(3, await ReadAsync(session, node.Id));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task 根运行准备仍然重置文件夹游标()
    {
        string directory = CreateFolder(out var node);
        try
        {
            var session = new WorkflowVisionAcquisitionSession(new NumberedReader());
            await session.PrepareAsync(Context(WorkflowRunScopeKind.Root, node), default);
            Assert.Equal(1, await ReadAsync(session, node.Id));

            // 归零语义不能被削弱：开新一轮仍要从头读。
            await session.PrepareAsync(Context(WorkflowRunScopeKind.Root, node), default);
            Assert.Equal(1, await ReadAsync(session, node.Id));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task 嵌套准备仍然执行跨子文档节点ID重复校验()
    {
        string directory = CreateFolder(out var node);
        try
        {
            var session = new WorkflowVisionAcquisitionSession(new NumberedReader());
            var duplicate = new LoadVisionFolderNodeModel { Id = node.Id, FolderPath = directory };

            // 嵌套作用域只是"不重置游标"，不是"跳过准备"：清单校验必须照常执行。
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                session.PrepareAsync(Context(WorkflowRunScopeKind.Nested, node, duplicate), default).AsTask());
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>确定性读取器：像素值取文件名序号，不依赖 OpenCV 与真实解码。</summary>
    private sealed class NumberedReader : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            byte value = byte.Parse(Path.GetFileNameWithoutExtension(path));
            return Task.FromResult<IImageSource>(
                VisionImage.CopyFrom(new ImageInfo(1, 1, EPixelLayout.Gray8), new[] { value }));
        }
    }
}
