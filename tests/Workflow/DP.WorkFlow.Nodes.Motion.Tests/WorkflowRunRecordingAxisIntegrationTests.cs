namespace DP.WorkFlow.Tests;

/// <summary>
/// 实施细节 §12.4：真实运控节点集成。<c>AxisActionNodeHandler</c> 只写
/// <c>context.ResolveInput(node.StepKeyBinding)</c>，输入键必须自动来自属性名。
/// </summary>
public sealed class WorkflowRunRecordingAxisIntegrationTests
{
    [Fact]
    public async Task AxisAction_RealNode_RecordsAutomaticKeyForBindingSlot()
    {
        var sink = new CollectingSink();
        var node = new AxisActionNodeModel
        {
            Id = "Move",
            Title = "移动",
            StepKeySource = E_AxisStepValueSource.Binding,
            // 绑定到公共数据的成员：真实节点 + 非字面量来源，验证公共数据血缘也能自动关联。
            StepKeyBinding = WorkflowInput<string>.FromBinding(
                WorkflowBindingKey.FromPublicData("GlobalPayload", "StepKey"))
        };
        var document = new WorkflowDocument { Name = "轴动作血缘", EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });

        var publicData = new WorkflowPublicDataStore();
        publicData.Apply(new WorkflowPublicDataChangeSet(
            new Dictionary<string, object> { ["GlobalPayload"] = new AxisPayload("LoadPosition") },
            Array.Empty<string>()));
        var axis = new RecordingAxisService();
        var services = new WorkflowServiceProvider().Add<IWorkflowAxisService>(axis);
        var engine = new WorkflowEngine(
            new WorkflowCompiler(new WorkflowNodeCatalog().RegisterMotionNodes()).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(),
            new WorkflowContext(services, publicData),
            new WorkflowExecutionOptions { Recording = new WorkflowRunRecordingOptions { Sink = sink } });

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        // 输入确实被解析并送进了轴服务，说明记录没有改变业务结果。
        Assert.Equal("LoadPosition", axis.StepKey);
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Equal("StepKeyBinding", resolved.Data!["InputKey"].Text);
        Assert.Equal("Automatic", resolved.Data["InputMetadataStatus"].Text);
        Assert.Equal("PublicData", resolved.Data["SourceKind"].Text);
        Assert.Equal("GlobalPayload", resolved.Data["PublicDataKey"].Text);
        Assert.Equal("StepKey", resolved.Data["SourceOutputKey"].Text);
        Assert.Equal("LoadPosition", resolved.Data["ResolvedValueSummary"].Text);
    }

    private sealed record AxisPayload(string StepKey);

    private sealed class RecordingAxisService : IWorkflowAxisService
    {
        public string? StepKey { get; private set; }

        public ValueTask<WorkflowAxisStepResult> ExecuteStepAsync(
            string stepKey,
            bool waitForCompleted,
            int overrideTimeoutMs,
            CancellationToken cancellationToken)
        {
            StepKey = stepKey;
            return ValueTask.FromResult(new WorkflowAxisStepResult(true, null, TimeSpan.Zero, Array.Empty<string>()));
        }

        public ValueTask<AxisServoNodeResult> SetServoAsync(
            WorkflowAxisAddress axis,
            bool enabled,
            bool waitForCompleted,
            int timeoutMs,
            int pollIntervalMs,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AxisServoNodeResult(axis.DeviceId, axis.AxisId, enabled, enabled, true, waitForCompleted));

        public ValueTask<AxisStopNodeResult> StopAsync(
            WorkflowAxisAddress axis,
            bool waitForCompleted,
            int timeoutMs,
            int pollIntervalMs,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AxisStopNodeResult(axis.DeviceId, axis.AxisId, waitForCompleted, true, 0, true));

        public ValueTask<AxisWaitNodeResult> WaitAsync(
            WorkflowAxisWaitRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AxisWaitNodeResult(
                request.Axis.DeviceId,
                request.Axis.AxisId,
                request.Condition,
                0,
                request.TargetPosition,
                0,
                request.PositionTolerance,
                true));
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
