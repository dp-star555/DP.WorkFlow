namespace DP.WorkFlow.Tests;

/// <summary>
/// 实施细节 §12.2：普通 <c>ResolveInput(input)</c> 自动产生稳定 InputKey 和来源血缘，
/// 动态输入与无法识别的输入分别走显式键和 fail-open 降级，且都不改变 Workflow 业务结果。
/// </summary>
public sealed class WorkflowRunRecordingLineageTests
{
    [Fact]
    public async Task InputResolved_AutomaticKeyComesFromPropertyNameAndPointsToRealOutput()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(
                new ProducerNodeModel { Id = "Producer", Output = new ProducerResult(42, true) },
                new LineageNodeModel
                {
                    Id = "Consumer",
                    Value = WorkflowInput<int>.FromBinding(new WorkflowBindingKey("Producer", "Value"))
                },
                startNodeId: "Producer"),
            sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var producer = Assert.Single(engine.Context.ExportNodeOutputs(), item => item.NodeId == "Producer");
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        // Handler 只调用 ResolveInput(node.Value)，输入键必须自动来自属性名。
        Assert.Equal("Value", resolved.Data!["InputKey"].Text);
        Assert.Equal("Automatic", resolved.Data["InputMetadataStatus"].Text);
        Assert.Equal("NodeOutput", resolved.Data["SourceKind"].Text);
        Assert.Equal("Producer", resolved.Data["SourceNodeId"].Text);
        Assert.Equal("Value", resolved.Data["SourceOutputKey"].Text);
        Assert.Equal(producer.ExecutionSequence, Convert.ToInt64(resolved.Data["SourceOutputSequence"].Scalar));
        Assert.Equal(42L, Convert.ToInt64(resolved.Data["ResolvedValueSummary"].Scalar));
    }

    [Fact]
    public async Task InputResolved_RootBindingUsesDollarSourceOutputKey()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(
                new ProducerNodeModel { Id = "Producer", Output = 42 },
                new LineageNodeModel
                {
                    Id = "Consumer",
                    Value = WorkflowInput<int>.FromBinding(new WorkflowBindingKey("Producer", "$"))
                },
                startNodeId: "Producer"),
            sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Equal("$", resolved.Data!["SourceOutputKey"].Text);
    }

    [Fact]
    public async Task InputResolved_LiteralRecordsValueAndSourceKind()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(
                new LineageNodeModel { Id = "Consumer", Value = WorkflowInput<int>.FromLiteral(9) },
                startNodeId: "Consumer"),
            sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Equal("Value", resolved.Data!["InputKey"].Text);
        Assert.Equal("Literal", resolved.Data["SourceKind"].Text);
        Assert.Null(resolved.Data["SourceOutputKey"].Scalar);
        Assert.Null(resolved.Data["SourceOutputSequence"].Scalar);
        Assert.Equal(9L, Convert.ToInt64(resolved.Data["ResolvedValueSummary"].Scalar));
    }

    [Fact]
    public async Task InputResolved_PublicDataRecordsPublicDataKey()
    {
        var sink = new CollectingSink();
        var publicData = new WorkflowPublicDataStore();
        publicData.Apply(new WorkflowPublicDataChangeSet(
            new Dictionary<string, object> { ["GlobalPayload"] = new PublicPayload(19) },
            Array.Empty<string>()));
        var engine = CreateEngine(
            BuildDocument(
                new LineageNodeModel
                {
                    Id = "Consumer",
                    Value = WorkflowInput<int>.FromBinding(WorkflowBindingKey.FromPublicData("GlobalPayload", "Count"))
                },
                startNodeId: "Consumer"),
            sink,
            new WorkflowContext(publicData: publicData));

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Equal("PublicData", resolved.Data!["SourceKind"].Text);
        Assert.Equal("GlobalPayload", resolved.Data["PublicDataKey"].Text);
        Assert.Equal("Count", resolved.Data["SourceOutputKey"].Text);
        Assert.Equal(19L, Convert.ToInt64(resolved.Data["ResolvedValueSummary"].Scalar));
    }

    [Fact]
    public async Task ResolveDynamicInput_RecordsExplicitDynamicStatusWithoutDegradingHealth()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(
                new LineageNodeModel
                {
                    Id = "Consumer",
                    Mode = "Dynamic",
                    Value = WorkflowInput<int>.FromLiteral(3)
                },
                startNodeId: "Consumer"),
            sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Equal("Samples[0]", resolved.Data!["InputKey"].Text);
        Assert.Equal("ExplicitDynamic", resolved.Data["InputMetadataStatus"].Text);
        // 显式动态键是受支持的正常路径，不应产生降级诊断。
        Assert.Equal(0L, engine.RecordingHealth.DiagnosticCount);
        Assert.Equal(E_WorkflowRecordingHealth.Healthy, engine.RecordingHealth.State);
    }

    [Fact]
    public async Task ResolveInput_UnrecognizedInstance_ResolvesNormallyButDegradesHealth()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(
                new LineageNodeModel { Id = "Consumer", Mode = "Unresolved", Value = WorkflowInput<int>.FromLiteral(3) },
                startNodeId: "Consumer"),
            sink);

        var result = await engine.RunAsync();

        // 记录元数据识别失败必须 fail-open：解析仍成功、Run 不 Fault。
        Assert.True(result.Success, result.Message);
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Null(resolved.Data!["InputKey"].Scalar);
        Assert.Equal("Unresolved", resolved.Data["InputMetadataStatus"].Text);
        var health = engine.RecordingHealth;
        Assert.Equal(E_WorkflowRecordingHealth.Degraded, health.State);
        Assert.True(health.DiagnosticCount > 0);
        Assert.NotNull(health.LastDiagnostic);
        // 降级不得伪装成 Sink 写入失败。
        Assert.Equal(0L, health.FailedWriteCount);
    }

    [Fact]
    public async Task ResolveInput_WhenBindingFails_KeepsOriginalExceptionAndRecordsFailure()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(
                new LineageNodeModel
                {
                    Id = "Consumer",
                    Value = WorkflowInput<int>.FromBinding(new WorkflowBindingKey("Missing", "Value"))
                },
                startNodeId: "Consumer"),
            sink);

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Contains("Missing|Value", result.Message);
        var failed = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Equal("Value", failed.Data!["InputKey"].Text);
        // 失败诊断事件必须存在，但不能覆盖绑定解析的原始异常。
        Assert.NotNull(failed.Data["Message"]);
    }

    [Fact]
    public async Task OutputCommitted_ScalarOutputUsesDollarKey()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(new ProducerNodeModel { Id = "Producer", Output = 42 }, startNodeId: "Producer"),
            sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var committed = Assert.Single(sink.Events, item => item.EventType == "OutputCommitted");
        Assert.Equal(new[] { "$" }, committed.Data!["OutputKeys"].Text!.Split(", "));
        Assert.Equal(42L, Convert.ToInt64(committed.Data["OutputValue.$"].Scalar));
    }

    [Fact]
    public async Task OutputCommitted_DtoExpandsFirstLevelStableKeys()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(
                new ProducerNodeModel { Id = "Producer", Output = new ProducerResult(42, true) },
                startNodeId: "Producer"),
            sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var committed = Assert.Single(sink.Events, item => item.EventType == "OutputCommitted");
        Assert.Equal(new[] { "Success", "Value" }, committed.Data!["OutputKeys"].Text!.Split(", "));
        Assert.Equal(42L, Convert.ToInt64(committed.Data["OutputValue.Value"].Scalar));
        Assert.Equal(true, committed.Data["OutputValue.Success"].Scalar);
        Assert.Equal(typeof(ProducerResult).FullName, committed.Data["OutputType"].Text);
    }

    [Fact]
    public async Task OutputCommitted_ObjectWithoutPublicPropertiesFallsBackToDollarKey()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(
            BuildDocument(new ProducerNodeModel { Id = "Producer", Output = new OpaquePayload() }, startNodeId: "Producer"),
            sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var committed = Assert.Single(sink.Events, item => item.EventType == "OutputCommitted");
        // 不可展开对象也必须给出稳定键：否则 OutputKeys 为空且没有任何 OutputValue.*，
        // "每个已提交输出都有稳定键值"就不成立。
        Assert.Equal(new[] { "$" }, committed.Data!["OutputKeys"].Text!.Split(", "));
        Assert.Equal(typeof(OpaquePayload).FullName, committed.Data["OutputType"].Text);
        var opaque = committed.Data["OutputValue.$"];
        Assert.Equal(WorkflowTraceValueKind.Summary, opaque.Kind);
        Assert.Equal(typeof(OpaquePayload).FullName, opaque.TypeName);
    }

    private static WorkflowEngine CreateEngine(
        WorkflowDocument document,
        IWorkflowRunEventSink sink,
        WorkflowContext? context = null)
    {
        // 关闭编译期绑定检查，让 Runtime 的解析路径成为被测对象。
        var definition = new WorkflowCompiler(validateBindings: false).Compile(document);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new ProducerNodeHandler())
            .Register(new LineageNodeHandler());
        var options = new WorkflowExecutionOptions
        {
            Recording = new WorkflowRunRecordingOptions { Sink = sink }
        };
        return new WorkflowEngine(definition, handlers, context ?? new WorkflowContext(), options);
    }

    private static WorkflowDocument BuildDocument(
        IWorkflowNodeModel node,
        string startNodeId) =>
        BuildDocument(node, null, startNodeId);

    private static WorkflowDocument BuildDocument(
        IWorkflowNodeModel first,
        IWorkflowNodeModel? second,
        string startNodeId)
    {
        var document = new WorkflowDocument { Name = "数据血缘", EntryNodeId = startNodeId };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = first });
        if (second is not null)
        {
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = second });
            canvas.Connections.Add(new WorkflowConnectionModel
            {
                FromNodeId = first.Id,
                FromPort = WorkflowPorts.Success,
                ToNodeId = second.Id
            });
        }

        return document;
    }

    private sealed record ProducerResult(int Value, bool Success);

    /// <summary>没有公开可读属性的不透明输出；用于验证回退到根键。</summary>
    private sealed class OpaquePayload
    {
    }

    private sealed record PublicPayload(int Count);

    [WorkflowNode("Test.LineageProducer")]
    private sealed class ProducerNodeModel : WorkflowNodeModel
    {
        public override string NodeType => "Test.LineageProducer";

        /// <summary>该节点提交的输出；用于验证 OutputCommitted 自动键值。</summary>
        public object? Output { get; set; }
    }

    [WorkflowNode("Test.LineageConsumer")]
    private sealed class LineageNodeModel : WorkflowNodeModel
    {
        public override string NodeType => "Test.LineageConsumer";

        /// <summary>被测普通输入槽；Handler 只按属性引用解析，不手写输入名。</summary>
        public WorkflowInput<int> Value { get; set; } = WorkflowInput<int>.FromLiteral(0);

        /// <summary>解析方式；不是输入槽。</summary>
        public string Mode { get; set; } = "Value";
    }

    private sealed class ProducerNodeHandler : WorkflowNodeHandler<ProducerNodeModel>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ProducerNodeModel node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Success, node.Output));
    }

    private sealed class LineageNodeHandler : WorkflowNodeHandler<LineageNodeModel>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            LineageNodeModel node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node.Mode)
            {
                case "Dynamic":
                    context.ResolveDynamicInput("Samples[0]", node.Value);
                    break;
                case "Unresolved":
                    // 运行时临时创建的输入实例不属于任何静态输入槽。
                    context.ResolveInput(WorkflowInput<int>.FromLiteral(3));
                    break;
                default:
                    context.ResolveInput(node.Value);
                    break;
            }

            return ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Success));
        }
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
