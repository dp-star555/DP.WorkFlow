using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>
/// 环形流程中采集节点再次产出时，上一帧的仓内租约随即释放。
/// 帧缓冲池只有两个槽位：同时最多持有“上一帧 + 当前帧”，若旧帧一直留到运行结束，第三次采集必然借不到槽位。
/// </summary>
public sealed class WorkflowVisionFrameScopeLoopReleaseTests
{
    [Fact]
    public async Task 环形采图时被取代的帧立即归还缓冲池()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dp-loop-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, new byte[] { 0 });
        try
        {
            var pool = new FrameBufferPool(new ImageInfo(1, 1, EPixelLayout.Gray8), capacity: 2, byteBudget: 2);
            using var scope = new WorkflowVisionFrameScope();
            var file = new AcquireVisionImageNodeModel { Id = "file", FilePath = path };
            var counter = new CounterNode { Id = "counter" };
            var document = new WorkflowDocument { Name = "环形采图", EntryNodeId = file.Id };
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = file });
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = counter });
            document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = file.Id, FromPort = WorkflowPorts.Success, ToNodeId = counter.Id });
            document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = counter.Id, FromPort = WorkflowPorts.Success, ToNodeId = file.Id });
            var counterHandler = new CounterNodeHandler(iterations: 6);
            var handlers = new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers().Register(counterHandler);
            var services = new WorkflowServiceProvider()
                .Add<IImageFileReader>(new PooledReader(pool))
                .Add<IWorkflowVisionFrameScope>(scope)
                .Add<IWorkflowRunPreparationService>(scope)
                .Add<IWorkflowRunResourceOwner>(scope);
            var engine = new WorkflowEngine(
                new WorkflowCompiler(new WorkflowNodeCatalog().RegisterImageNodes()).Compile(document), handlers, new WorkflowContext(services));

            var result = await engine.RunAsync();

            Assert.True(result.Success, result.Message);
            Assert.Equal(6, counterHandler.Executions);
            var output = Assert.Single(engine.RunState.NodeOutputs, item => item.NodeId == file.Id);
            // 最新一帧仍可读取，供结果查看和下游绑定。
            var pixel = new byte[1];
            Assert.IsType<ImageFrame>(output.Value).Image.CopyTo(0, pixel, 0, 1);
            Assert.Equal(7, pixel[0]);
        }
        finally { File.Delete(path); }
    }

    private sealed class CounterNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Counter";
    }

    /// <summary>前若干次回到采集节点，之后选择未连线的端口结束路径。</summary>
    private sealed class CounterNodeHandler(int iterations) : WorkflowNodeHandler<CounterNode>
    {
        public int Executions { get; private set; }

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(CounterNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            Executions++;
            return ValueTask.FromResult(NodeExecutionResult.Continue(Executions < iterations ? WorkflowPorts.Success : "Done"));
        }
    }

    /// <summary>从池借槽并发布；池耗尽说明旧帧租约没有释放。</summary>
    private sealed class PooledReader(FrameBufferPool pool) : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!pool.TryRent(out var writer))
                throw new InvalidOperationException("池已耗尽，旧帧租约没有释放。");
            writer!.Write(0, new byte[] { 7 }, 0, 1);
            return Task.FromResult(writer.Publish());
        }
    }
}
