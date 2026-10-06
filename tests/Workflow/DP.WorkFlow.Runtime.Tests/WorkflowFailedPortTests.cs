namespace DP.WorkFlow.Tests;

/// <summary>失败出口：连线时节点失败不中止运行，沿失败支路继续；未连线时仍中止运行。</summary>
public sealed class WorkflowFailedPortTests
{
    [Fact]
    public async Task 失败出口已连线时沿失败支路继续且运行完成()
    {
        var (document, catalog, handlers, executed) = Create(connectFailed: true);
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document), handlers);
        var traces = new List<WorkflowTraceEntry>();
        engine.NodeTrace += traces.Add;

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(E_WorkflowExecutionState.Completed, result.State);
        Assert.Equal(new[] { "Inspect", "OnNg" }, executed);
        // 故障照常记录：节点标记失败，轨迹里有失败和转入失败支路两条。
        Assert.Equal(E_NodeState.Failed, engine.GetRuntimeSnapshot().Nodes["Inspect"].State);
        Assert.Single(engine.RunState.Faults);
        Assert.Contains(traces, t => t.NodeId == "Inspect" && t.Step == "NodeFailed" && t.Message!.Contains("检测NG"));
        Assert.Contains(traces, t => t.NodeId == "Inspect" && t.Step == "NodeFaultRouted");
    }

    [Fact]
    public async Task 失败出口未连线时仍中止运行()
    {
        var (document, catalog, handlers, executed) = Create(connectFailed: false);
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document), handlers);

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Equal(E_WorkflowExecutionState.Faulted, result.State);
        Assert.Equal(new[] { "Inspect" }, executed);
    }

    [Fact]
    public void 端口显示中文名称()
    {
        Assert.Equal("成功", WorkflowPorts.GetDisplayName(WorkflowPorts.Success));
        Assert.Equal("失败", WorkflowPorts.GetDisplayName(WorkflowPorts.Failed));
        Assert.Equal("Custom", WorkflowPorts.GetDisplayName("Custom"));
    }

    private static (WorkflowDocument, WorkflowNodeCatalog, WorkflowNodeHandlerCatalog, List<string>) Create(bool connectFailed)
    {
        var inspect = new FailedPortTestNode { Id = "Inspect", Title = "检测", Fail = true };
        var ok = new FailedPortTestNode { Id = "OnOk", Title = "OK" };
        var ng = new FailedPortTestNode { Id = "OnNg", Title = "NG" };
        var document = new WorkflowDocument { Name = "失败出口", EntryNodeId = inspect.Id };
        var canvas = document.CanvasProjection;
        foreach (var node in new[] { inspect, ok, ng }) canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = inspect.Id, FromPort = WorkflowPorts.Success, ToNodeId = ok.Id, ToPort = WorkflowPorts.Input });
        if (connectFailed)
            canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = inspect.Id, FromPort = WorkflowPorts.Failed, ToNodeId = ng.Id, ToPort = WorkflowPorts.Input });
        var executed = new List<string>();
        var catalog = new WorkflowNodeCatalog().Register<FailedPortTestNode>(1,
            WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(), WorkflowPortDescriptor.Failure());
        return (document, catalog, new WorkflowNodeHandlerCatalog().Register(new FailedPortTestHandler(executed)), executed);
    }

    [WorkflowNode("FailedPortTest")]
    private sealed class FailedPortTestNode : WorkflowNodeModel
    {
        public override string NodeType => "FailedPortTest";
        public bool Fail { get; set; }
    }

    private sealed class FailedPortTestHandler(List<string> executed) : WorkflowNodeHandler<FailedPortTestNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(FailedPortTestNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            executed.Add(node.Id);
            if (node.Fail) throw new InvalidOperationException("检测NG：未找到边缘。");
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }
}
