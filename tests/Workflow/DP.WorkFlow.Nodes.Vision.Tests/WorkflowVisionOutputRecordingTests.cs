using DP.Vision;

namespace DP.WorkFlow.Tests;

/// <summary>
/// 实施细节 §12.3/§8.6：图像根输出至少记录 FrameId，绝不记录像素内容。
/// Runtime 不引用 DP.Vision，因此这里验证的是"普通 DTO 第一层属性"这条通用路径对真实图像类型仍然成立。
/// </summary>
public sealed class WorkflowVisionOutputRecordingTests
{
    /// <summary>本次执行的图像输出；放在测试字段上，避免把可释放资源放进节点配置。</summary>
    private static ImageFrame? _frame;

    [Fact]
    public async Task OutputCommitted_RecordsFrameIdWithoutPixelContent()
    {
        var sink = new CollectingSink();
        using var source = VisionImage.CopyFrom(new ImageInfo(4, 4, EPixelLayout.Gray8), new byte[16]);
        var frame = new ImageFrame("frame-20260922-001", source);
        _frame = frame;
        try
        {
            var engine = CreateEngine(new FrameProducerNodeModel { Id = "Capture" }, sink);

            var result = await engine.RunAsync();

            Assert.True(result.Success, result.Message);
            var committed = Assert.Single(sink.Events, item => item.EventType == "OutputCommitted");
            var keys = committed.Data!["OutputKeys"].Text!.Split(", ");
            Assert.Contains("FrameId", keys);
            Assert.Contains("Image", keys);

            // 帧身份必须被记录，供后续绑定和分析确认证据来源。
            Assert.Equal("frame-20260922-001", committed.Data["OutputValue.FrameId"].Text);
            // 图像资源只保留身份和摘要，不得写入像素。
            var image = committed.Data["OutputValue.Image"];
            Assert.NotEqual(WorkflowTraceValueKind.Scalar, image.Kind);
            Assert.Null(image.Scalar);
            Assert.DoesNotContain("OutputValue.Image.Pixels", committed.Data.Keys);
        }
        finally
        {
            _frame = null;
            frame.Dispose();
        }
    }

    private static WorkflowEngine CreateEngine(FrameProducerNodeModel node, IWorkflowRunEventSink sink)
    {
        var document = new WorkflowDocument { Name = "图像输出记录", EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var definition = new WorkflowCompiler(validateBindings: false).Compile(document);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new FrameProducerHandler());
        var options = new WorkflowExecutionOptions
        {
            Recording = new WorkflowRunRecordingOptions { Sink = sink }
        };
        return new WorkflowEngine(definition, handlers, new WorkflowContext(), options);
    }

    [WorkflowNode("Test.VisionFrameProducer")]
    private sealed class FrameProducerNodeModel : WorkflowNodeModel
    {
        public override string NodeType => "Test.VisionFrameProducer";
    }

    private sealed class FrameProducerHandler : WorkflowNodeHandler<FrameProducerNodeModel>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            FrameProducerNodeModel node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Success, _frame));
    }

    private sealed class CollectingSink : IWorkflowRunEventSink
    {
        private readonly object _sync = new();
        private readonly List<WorkflowRunEvent> _events = new();

        public IReadOnlyList<WorkflowRunEvent> Events
        {
            get
            {
                lock (_sync)
                    return _events.OrderBy(item => item.Sequence).ToArray();
            }
        }

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            lock (_sync)
                _events.AddRange(events);
            return ValueTask.FromResult(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
