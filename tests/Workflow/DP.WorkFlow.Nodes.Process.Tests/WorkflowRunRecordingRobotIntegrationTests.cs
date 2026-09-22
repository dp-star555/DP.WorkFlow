namespace DP.WorkFlow.Tests;

/// <summary>
/// 实施细节 §12.4：真实工艺流程节点集成。<c>WaferRobotMoveNodeModel</c> 有三个绑定输入槽，
/// Handler 只按属性引用调用 <c>ResolveInput</c>，三个键必须各自自动识别且互不混淆。
/// </summary>
public sealed class WorkflowRunRecordingRobotIntegrationTests
{
    [Fact]
    public async Task WaferRobotMove_RealNode_RecordsAutomaticKeyForEachBindingSlot()
    {
        var sink = new CollectingSink();
        var node = new WaferRobotMoveNodeModel
        {
            Id = "Move",
            Title = "移动",
            RobotKey = "Robot1",
            StationIdSource = E_WaferRobotValueSource.Binding,
            StationIdBinding = WorkflowInput<string>.FromLiteral("LoadPort"),
            SlotSource = E_WaferRobotValueSource.Binding,
            SlotBinding = WorkflowInput<int>.FromLiteral(3),
            ArmSource = E_WaferRobotValueSource.Binding,
            ArmBinding = WorkflowInput<E_EndEffectorType>.FromLiteral(E_EndEffectorType.Arm2)
        };
        var document = new WorkflowDocument { Name = "机器人血缘", EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });

        var service = new RecordingRobotService();
        var services = new WorkflowServiceProvider().Add<IWorkflowWaferRobotService>(service);
        var engine = new WorkflowEngine(
            new WorkflowCompiler(new WorkflowNodeCatalog().RegisterProcessNodes()).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers(),
            new WorkflowContext(services),
            new WorkflowExecutionOptions { Recording = new WorkflowRunRecordingOptions { Sink = sink } });

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        // 三个绑定输入都真实到达了机器人服务，说明记录未改变业务结果。
        Assert.Equal(new WaferRobotTarget("LoadPort", 3, E_EndEffectorType.Arm2, false), service.LastTarget);
        var resolved = sink.Events.Where(item => item.EventType == "InputResolved").ToArray();
        Assert.Equal(
            new[] { "StationIdBinding", "SlotBinding", "ArmBinding" },
            resolved.Select(item => item.Data!["InputKey"].Text));
        Assert.All(resolved, item =>
        {
            Assert.Equal("Automatic", item.Data!["InputMetadataStatus"].Text);
            Assert.Equal("Literal", item.Data["SourceKind"].Text);
        });
        // 每个槽记录的是自己那份值，不能串到别的槽。
        Assert.Equal("LoadPort", resolved[0].Data!["ResolvedValueSummary"].Text);
        Assert.Equal(3L, Convert.ToInt64(resolved[1].Data!["ResolvedValueSummary"].Scalar));
        Assert.Equal((long)E_EndEffectorType.Arm2, Convert.ToInt64(resolved[2].Data!["ResolvedValueSummary"].Scalar));
    }

    private sealed class RecordingRobotService : IWorkflowWaferRobotService
    {
        private static WaferRobotDeviceCommandResult Ok => new(true, "0", "OK", "REQ", "RESP");

        public WaferRobotTarget? LastTarget { get; private set; }

        public ValueTask<WaferRobotDeviceCommandResult> InitializeAsync(string robotKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Ok);

        public ValueTask<WaferRobotDeviceCommandResult> HomeAsync(string robotKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Ok);

        public ValueTask<WaferRobotDeviceCommandResult> StopAsync(string robotKey, E_StopMode stopMode, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Ok);

        public ValueTask<WaferRobotDeviceCommandResult> MoveAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken)
        {
            LastTarget = target;
            return ValueTask.FromResult(Ok);
        }

        public ValueTask<WaferRobotDeviceCommandResult> PickAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Ok);

        public ValueTask<WaferRobotDeviceCommandResult> PlaceAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Ok);

        public ValueTask<WaferRobotSnapshotResult> ReadSnapshotAsync(string robotKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<WaferRobotStateResult> RefreshStateAsync(string robotKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WaferRobotStateResult(true, true, false, false, false, true, null, null, null, null, null, "IDLE", "Idle"));
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
