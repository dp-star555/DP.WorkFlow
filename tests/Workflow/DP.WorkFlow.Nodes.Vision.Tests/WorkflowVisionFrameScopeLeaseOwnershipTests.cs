using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>
/// AR-01 验收第 5 条：结束后租约按所有权恰好释放。
///
/// 帧仓不对外暴露租约计数，所以这里用<b>容量为 1 的帧缓冲池当探针</b>：
/// 采集时从池里借走唯一槽位，只要还有任何一个租约没释放，槽位就回不来，
/// <see cref="FrameBufferPool.TryRent"/> 必然失败。于是"租约是否恰好释放"
/// 变成一个确定性的布尔断言，不依赖 GC、也不依赖计时。
///
/// 两条测试分别锁定设计的两个半边：
/// ① 本轮结束后仓仍持有租约供结果查看（这是设计，不是泄漏），下一轮根运行开始时恰好归零；
/// ② 仓释放后 UI 已 Retain 的快照仍可显示，且最后一个租约释放时才归池。
/// </summary>
public sealed class WorkflowVisionFrameScopeLeaseOwnershipTests
{
    [Fact]
    public async Task 运行结束后仓仍持有租约下一轮根运行开始时恰好归零()
    {
        string path = TempImage();
        try
        {
            var pool = new FrameBufferPool(PixelLayout, capacity: 1, byteBudget: 1);
            using var scope = new WorkflowVisionFrameScope();

            await RunLoadFileAsync(path, pool, scope);

            // 本轮结束后，仓仍持有帧与预览两份租约，供结果查看窗口使用。
            Assert.False(SlotReturned(pool), "运行结束后仓应当仍持有帧租约。");

            // 下一轮根运行开始：运行所有者退役上一轮全部租约，槽位随之归池。
            // AR-01 阶段2 起，"退役"是独立于准备的动作（IWorkflowRunResourceOwner），
            // 准备阶段只校验，不再顺带清理。
            await scope.PrepareAsync(
                new WorkflowRunPreparationContext(Array.Empty<IWorkflowNodeModel>(), WorkflowRunScopeKind.Root),
                CancellationToken.None);
            Assert.False(SlotReturned(pool), "准备阶段本身不得释放既有租约。");

            await scope.ReleasePreviousRunAsync(CancellationToken.None);

            Assert.True(SlotReturned(pool), "根运行退役上一轮后仓内租约必须全部释放。");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task 仓释放后UI快照仍可显示且最后一个租约释放时才归池()
    {
        string path = TempImage();
        try
        {
            var pool = new FrameBufferPool(PixelLayout, capacity: 1, byteBudget: 1);
            var scope = new WorkflowVisionFrameScope();

            await RunLoadFileAsync(path, pool, scope);

            // 结果查看窗口打开时，UI 取一份独立快照（自带独立租约）。
            using var snapshot = scope.Capture("file");
            Assert.NotNull(snapshot);

            // 窗口关闭：作用域释放，仓内帧与预览租约全部归还。
            scope.Dispose();

            // 快照仍能显示：像素读得出来，且就是原图内容。
            var pixel = new byte[1];
            snapshot!.Frame.Image.CopyTo(0, pixel, 0, 1);
            Assert.Equal(7, pixel[0]);

            // 但快照自己还持有一个租约，所以槽位仍未归还——"恰好"由所有权决定，不是提前回收。
            Assert.False(SlotReturned(pool), "UI 快照仍持有租约时不得回收底层存储。");

            snapshot.Dispose();

            // 最后一个租约释放，槽位归位。
            Assert.True(SlotReturned(pool), "全部租约释放后底层存储必须归池。");
        }
        finally { File.Delete(path); }
    }

    private static ImageInfo PixelLayout => new(1, 1, EPixelLayout.Gray8);

    /// <summary>池槽是否已归还。探针用完立即把槽位还回去，可重复调用。</summary>
    private static bool SlotReturned(FrameBufferPool pool)
    {
        if (!pool.TryRent(out var writer))
            return false;
        writer!.Dispose();
        return true;
    }

    /// <summary>跑一次真实采图：节点会 Retain 进帧仓，并 Publish 一份预览。</summary>
    private static async Task RunLoadFileAsync(string path, FrameBufferPool pool, WorkflowVisionFrameScope scope)
    {
        var file = new LoadVisionFileNodeModel { Id = "file", FilePath = path };
        var document = new WorkflowDocument { Name = "采图", EntryNodeId = file.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = file });

        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var handlers = new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers();
        var services = new WorkflowServiceProvider()
            .Add<IImageFileReader>(new PooledReader(pool))
            .Add<IWorkflowVisionFrameScope>(scope)
            .Add<IWorkflowRunPreparationService>(scope)
            .Add<IWorkflowRunResourceOwner>(scope);

        var result = await new WorkflowEngine(
                new WorkflowCompiler(catalog).Compile(document), handlers, new WorkflowContext(services))
            .RunAsync();

        Assert.True(result.Success, result.Message);
    }

    /// <summary>内容无关紧要：读取器是假的，这里只为通过编译期的文件存在性校验。</summary>
    private static string TempImage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dp-lease-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, new byte[] { 0 });
        return path;
    }

    /// <summary>从池借槽并发布，让"租约是否释放"通过槽位是否归还变得可观测。</summary>
    private sealed class PooledReader : IImageFileReader
    {
        private readonly FrameBufferPool _pool;

        public PooledReader(FrameBufferPool pool) => _pool = pool;

        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!_pool.TryRent(out var writer))
                throw new InvalidOperationException("池已耗尽，无法采集。");
            writer!.Write(0, new byte[] { 7 }, 0, 1);
            return Task.FromResult(writer.Publish());
        }
    }
}
