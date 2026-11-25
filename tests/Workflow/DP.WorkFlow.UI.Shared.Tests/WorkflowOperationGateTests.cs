using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowOperationGateTests
{
    private sealed class RecordingGate : IWorkflowOperationGate
    {
        public List<WorkflowOperation> Operations { get; } = new();
        public bool Deny { get; set; }

        public async Task RunAsync(WorkflowOperation operation, Func<Task> action)
        {
            Operations.Add(operation);
            if (Deny) throw new InvalidOperationException("需要权限");
            await Task.Yield();
            await action();
        }
    }

    private static (WorkflowStudioRuntimeBinding Binding, WorkflowRuntimeHost Host) Create()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var start = new StartNodeModel { Id = "Start", Title = "开始" };
        var document = new WorkflowDocument { Name = "Run" };
        document.Nodes.Add(new WorkflowCanvasNode { Node = start });
        document.EntryNodeId = start.Id;
        var navigator = new WorkflowDesignerNavigator(document, catalog);
        var host = new WorkflowRuntimeHost(catalog, new WorkflowNodeHandlerCatalog().Register(new StartNodeHandler()));
        return (new WorkflowStudioRuntimeBinding(host, navigator) { AutoConfigureBeforeRun = true }, host);
    }

    [Fact]
    public async Task TrialRun_GoesThroughGate_AndBlockedReasonStopsBeforeGate()
    {
        var (binding, host) = Create();
        using (host)
        using (binding)
        {
            var gate = new RecordingGate();
            binding.Gate = gate;
            binding.GateTarget = "主流程";

            var result = await binding.RunAsync();
            Assert.True(result.Success);
            var operation = Assert.Single(gate.Operations);
            Assert.Equal(WorkflowOperationKind.TrialRun, operation.Kind);
            Assert.Equal("workflow.design.run", operation.Key);
            Assert.Equal("主流程", operation.Target);

            var commands = new WorkflowStudioRunCommands(binding);
            binding.RunBlockedReason = () => "整线在运行";
            Assert.False(commands.CanRun);
            Assert.Contains("整线在运行", commands.RunToolTip, StringComparison.Ordinal);
            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => binding.RunAsync());
            Assert.Equal("整线在运行", blocked.Message);
            Assert.Single(gate.Operations);

            binding.RunBlockedReason = null;
            gate.Deny = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => binding.RunAsync());
            Assert.Equal(E_WorkflowExecutionState.Completed, host.State);
        }
    }

    [Fact]
    public void Operation_KeyAndDescription()
    {
        var operation = new WorkflowOperation(WorkflowOperationKind.ReleaseFault, "*", "上料：重做");
        Assert.Equal("workflow.fault.release", operation.Key);
        Assert.Equal("* 上料：重做", operation.Describe());
        Assert.Equal("workflow.flow.start", new WorkflowOperation(WorkflowOperationKind.StartFlow, "上料").Key);
    }
}
