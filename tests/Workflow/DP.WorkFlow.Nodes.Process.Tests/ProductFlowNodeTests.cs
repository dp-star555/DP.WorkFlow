namespace DP.WorkFlow.Tests;

public sealed class ProductFlowNodeTests
{
    [Fact]
    public async Task ProductCreate_UsesInjectedProcessService()
    {
        var node = new ProductCreateNodeModel { Id = "Create", Title = "创建", StationId = "S1", SlotId = "1" };
        var canvasDocument = new WorkflowDocument { Name = "产品" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterProcessNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var service = new FakeProcessService();
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowProcessService>(service));
        var result = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers(), context).RunAsync();
        Assert.True(result.Success);
        Assert.Equal("S1", service.CreateRequest?.StationId);
    }

    [Fact]
    public async Task ProductMoveStart_PreservesVirtualCreateRequest()
    {
        var node = new ProductMoveStartNodeModel
        {
            Id = "Move",
            Title = "流转",
            MoveMode = E_ProductMoveStartMode.VirtualCreate,
            ToStationId = "S2",
            ToSlotId = "2",
            ProductId = "P9",
            Recipe = "R1"
        };
        var service = new FakeProductFlowService();
        var result = await RunAsync(node, new WorkflowServiceProvider().Add<IWorkflowProductFlowService>(service));
        Assert.True(result.Success);
        Assert.Equal(E_ProductMoveStartMode.VirtualCreate, service.StartRequest?.MoveMode);
        Assert.Equal("S2", service.StartRequest?.ToStationId);
        Assert.Equal("P9", service.StartRequest?.ProductId);
    }

    [Fact]
    public async Task StationCanReceive_UsesTrueBranchSemantics()
    {
        var node = new StationCanReceiveNodeModel { Id = "Check", Title = "检查", StationId = "S1", SlotId = "1" };
        var service = new FakeProductFlowService { CanReceive = true };
        var result = await RunAsync(node, new WorkflowServiceProvider().Add<IWorkflowProductFlowService>(service));
        Assert.True(result.Success);
        Assert.Equal(("S1", "1"), service.LastAddress);
    }

    private static async Task<WorkflowRunResult> RunAsync(IWorkflowNodeModel node, WorkflowServiceProvider services)
    {
        var canvasDocument = new WorkflowDocument { Name = "产品流" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterProcessNodes()).Compile(canvasDocument);
        return await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers(), new WorkflowContext(services)).RunAsync();
    }

    private sealed class FakeProductFlowService : IWorkflowProductFlowService
    {
        public ProductMoveStartRequest? StartRequest { get; private set; }
        public bool CanReceive { get; set; }
        public (string, string?) LastAddress { get; private set; }
        public ValueTask<ProductMoveStartNodeResult> StartMoveAsync(ProductMoveStartRequest request, CancellationToken cancellationToken)
        {
            StartRequest = request;
            return ValueTask.FromResult(new ProductMoveStartNodeResult("M1", request.ProductId ?? "AUTO", request.FromStationId, request.FromSlotId, request.ToStationId, request.ToSlotId, E_ProductFlowState.Active));
        }
        public ValueTask<ProductMoveFinishNodeResult> CompleteMoveAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ProductMoveFinishNodeResult> FailMoveAsync(string sessionId, string? failReason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<bool> CanReceiveAsync(string stationId, string? slotId, CancellationToken cancellationToken)
        {
            LastAddress = (stationId, slotId);
            return ValueTask.FromResult(CanReceive);
        }
        public ValueTask<bool> CanSendAsync(string stationId, string? slotId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<bool> SlotHasProductAsync(string stationId, string slotId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask MarkStationFinishedAsync(string stationId, string? slotId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FakeProcessService : IWorkflowProcessService
    {
        public ProductCreateRequest? CreateRequest { get; private set; }
        public ValueTask<ProductCreateNodeResult> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken)
        {
            CreateRequest = request;
            return ValueTask.FromResult(new ProductCreateNodeResult("P1", "L1", request.Recipe, request.StationId, request.SlotId, "Created", 1, 1));
        }
    }
}
