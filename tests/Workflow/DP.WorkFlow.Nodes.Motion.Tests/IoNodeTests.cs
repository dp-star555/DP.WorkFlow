namespace DP.WorkFlow.Tests;

public sealed class IoNodeTests
{
    [Fact]
    public async Task ReadWriteAndWait_UseInjectedIoServiceAndExplicitPorts()
    {
        var read = new IoReadNodeModel { Id = "Read", Title = "读取", DriveId = "IO1", Index = "0" };
        var write = new IoWriteNodeModel
        {
            Id = "Write",
            Title = "写入",
            DriveId = "IO1",
            Index = "1",
            Command = WorkflowIoWriteCommand.SetByValue,
            Value = WorkflowInput<bool>.FromLiteral(true)
        };
        var wait = new IoWaitNodeModel
        {
            Id = "Wait",
            Title = "等待",
            DriveId = "IO1",
            Index = "2",
            ExpectedValue = true,
            TimeoutMs = 1000
        };
        var canvasDocument = new WorkflowDocument { Name = "IO" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { read, write, wait })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = read.Id,
            FromPort = WorkflowPorts.Success,
            ToNodeId = write.Id
        });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = write.Id,
            FromPort = WorkflowPorts.Success,
            ToNodeId = wait.Id
        });
        var catalog = new WorkflowNodeCatalog().RegisterMotionNodes();
        canvasDocument.EntryNodeId = read.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var io = new FakeIoService();
        var services = new WorkflowServiceProvider().Add<IWorkflowIoService>(io);
        var handlers = new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers();

        var result = await new WorkflowEngine(definition, handlers, new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success);
        Assert.True(io.Values["IO1:1"]);
        Assert.Equal("IO1:2", io.LastWaitPoint);
    }

    private sealed class FakeIoService : IWorkflowIoService
    {
        public Dictionary<string, bool> Values { get; } = new(StringComparer.Ordinal)
        {
            ["IO1:0"] = true,
            ["IO1:2"] = true
        };

        public string? LastWaitPoint { get; private set; }

        public ValueTask<WorkflowIoNodeResult> ReadAsync(WorkflowIoAddress address, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WorkflowIoNodeResult(address.DriveId, address.Index, Values.GetValueOrDefault($"{address.DriveId}:{address.Index}"), true));

        public ValueTask<WorkflowIoWriteResult> WriteAsync(WorkflowIoWriteRequest request, CancellationToken cancellationToken)
        {
            var target = request.Command switch
            {
                WorkflowIoWriteCommand.Off => false,
                WorkflowIoWriteCommand.On => true,
                WorkflowIoWriteCommand.Toggle => !Values.GetValueOrDefault($"{request.Address.DriveId}:{request.Address.Index}"),
                _ => request.Value
            };
            Values[$"{request.Address.DriveId}:{request.Address.Index}"] = target;
            return ValueTask.FromResult(new WorkflowIoWriteResult(request.Address, request.Command, target, true));
        }

        public ValueTask<bool> ReadAsync(string pointKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Values.GetValueOrDefault(pointKey));

        public ValueTask WriteAsync(string pointKey, bool value, CancellationToken cancellationToken)
        {
            Values[pointKey] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> WaitAsync(
            string pointKey,
            bool expectedValue,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastWaitPoint = pointKey;
            return ValueTask.FromResult(Values.GetValueOrDefault(pointKey) == expectedValue);
        }

        public ValueTask<WorkflowIoNodeResult> WaitAsync(WorkflowIoExpectation expectation, WorkflowIoPassMode passMode, int holdMs, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken)
        {
            var address = expectation.ToAddress();
            LastWaitPoint = $"{address.DriveId}:{address.Index}";
            var value = Values.GetValueOrDefault(LastWaitPoint);
            return ValueTask.FromResult(new WorkflowIoNodeResult(address.DriveId, address.Index, value, value == expectation.ExpectedValue));
        }

        public ValueTask<IoMultiNodeResult> CheckManyAsync(IReadOnlyList<WorkflowIoExpectation> expectations, WorkflowIoMatchMode matchMode, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Evaluate(expectations, matchMode));

        public ValueTask<IoMultiNodeResult> WaitManyAsync(IReadOnlyList<WorkflowIoExpectation> expectations, WorkflowIoMatchMode matchMode, WorkflowIoPassMode passMode, int holdMs, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Evaluate(expectations, matchMode));

        private IoMultiNodeResult Evaluate(IReadOnlyList<WorkflowIoExpectation> expectations, WorkflowIoMatchMode mode)
        {
            var items = expectations.Select(item =>
            {
                var address = item.ToAddress();
                var value = Values.GetValueOrDefault($"{address.DriveId}:{address.Index}");
                return new WorkflowIoNodeResult(address.DriveId, address.Index, value, value == item.ExpectedValue);
            }).ToArray();
            return new IoMultiNodeResult(mode == WorkflowIoMatchMode.All ? items.All(item => item.Success) : items.Any(item => item.Success), mode, items);
        }
    }
}
