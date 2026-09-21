using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class BlockNodeTests
{
    [Fact]
    public async Task RunAsync_BlockRunsIsolatedChildAndPublishesChildSnapshot()
    {
        var invoked = 0;
        var actions = new WorkflowActionRegistry().Register("ChildAction", (context, _) =>
        {
            Assert.True(context.TryGetVariable<int>("Input", out var input));
            invoked += input;
            context.SetVariable("ChildOnly", true);
            return ValueTask.FromResult<object?>("child-output");
        });
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);
        var context = new WorkflowContext(services);
        context.SetVariable("Input", 3);
        var (engine, block) = CreateEngine(CreateChildDocument(), context, configuredBlock =>
            configuredBlock.InputMappings.Add(new BlockInputMapping
            {
                TargetVariableName = "Input",
                Source = E_BlockInputSource.ParentVariable,
                ParentVariableName = "Input"
            }));
        var completed = new List<string>();
        engine.NodeCompleted += node => completed.Add(node.Id);

        var result = await engine.RunAsync();
        var snapshot = engine.GetRuntimeSnapshot();

        Assert.True(result.Success, result.Message);
        Assert.Contains("ParentEnd", completed);
        Assert.Equal(new[] { "Block", "ParentEnd" }, completed);
        Assert.Equal(3, invoked);
        Assert.False(context.ContainsVariable("ChildOnly"));
        var child = Assert.Single(snapshot.EnumerateChildWorkflows());
        Assert.Equal(block.Id, child.ParentNodeId);
        Assert.Equal(E_WorkflowExecutionState.Completed, child.Snapshot.ExecutionState);
        Assert.NotEqual(snapshot.RunId, child.Snapshot.RunId);
        Assert.True(context.TryGetNodeOutput(block.Id, out var blockOutput));
        Assert.IsType<BlockNodeOutput>(blockOutput!.Value);
    }

    [Fact]
    public async Task CancelInsideBlock_DoesNotTranslateCancellationIntoParentFault()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new WorkflowActionRegistry().Register("ChildAction", async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (engine, _) = CreateEngine(CreateChildDocument(), context);
        using var cancellation = new CancellationTokenSource();
        var running = engine.RunAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
        Assert.Empty(engine.RunState.Faults);
        Assert.Equal(E_WorkflowExecutionState.Canceled,
            Assert.Single(engine.GetRuntimeSnapshot().EnumerateChildWorkflows()).Snapshot.ExecutionState);
    }

    [Fact]
    public async Task RunAsync_AppliesExplicitInputAndOutputMappings()
    {
        var actions = new WorkflowActionRegistry().Register("Map", (actionContext, _) =>
        {
            Assert.True(actionContext.TryGetVariable<long>("ChildInput", out var input));
            return ValueTask.FromResult<object?>(new NumericOutput(input * 2));
        });
        var context = new WorkflowContext(
            new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        context.SetVariable("ParentInput", 21L);
        var child = CreateChildDocument(
            new ActionNodeModel { Id = "Map", Title = "映射", FunctionKey = "Map" });
        var (engine, block) = CreateEngine(child, context, configuredBlock =>
        {
            configuredBlock.InputMappings.Add(new BlockInputMapping
            {
                TargetVariableName = "ChildInput",
                Source = E_BlockInputSource.ParentVariable,
                ParentVariableName = "ParentInput"
            });
            configuredBlock.OutputMappings.Add(new BlockOutputMapping
            {
                TargetVariableName = "ParentResult",
                Source = E_BlockOutputSource.ChildNodeBinding,
                ChildBinding = new WorkflowBindingKey("Map", "Value")
            });
        });

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(context.TryGetVariable<long>("ParentResult", out var mapped));
        Assert.Equal(42L, mapped);
        Assert.True(context.TryGetNodeOutput(block.Id, out var blockRaw));
        var blockOutput = Assert.IsType<BlockNodeOutput>(blockRaw!.Value);
        Assert.Equal(42L, blockOutput.MappedOutputs["ParentResult"]);
    }

    [Fact]
    public void Compile_BlockOutputBindingToMissingChildNodeFailsEarly()
    {
        var child = CreateChildDocument();
        var exception = Assert.Throws<WorkflowCompilationException>(() => CreateEngine(
            child,
            configure: block => block.OutputMappings.Add(new BlockOutputMapping
            {
                TargetVariableName = "Result",
                Source = E_BlockOutputSource.ChildNodeBinding,
                ChildBinding = new WorkflowBindingKey("MissingChild", "Value")
            })));

        Assert.Contains(exception.Errors, error => error.Code == "WFB001");
    }

    [Fact]
    public async Task PauseAndResume_PropagateToActiveChildEngine()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterCount = 0;
        var actions = new WorkflowActionRegistry()
            .Register("Blocking", async (_, cancellationToken) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return null;
            })
            .Register("After", (_, _) =>
            {
                Interlocked.Increment(ref afterCount);
                return ValueTask.FromResult<object?>(null);
            });
        var context = new WorkflowContext(
            new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var child = CreateChildDocument(
            new ActionNodeModel { Id = "Blocking", Title = "阻塞", FunctionKey = "Blocking" },
            new ActionNodeModel { Id = "After", Title = "之后", FunctionKey = "After" });
        var (engine, _) = CreateEngine(child, context);

        var runTask = engine.RunAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        engine.Pause();
        release.TrySetResult(true);
        await WaitUntilAsync(() =>
            engine.GetRuntimeSnapshot().EnumerateChildWorkflows()
                .Any(info => info.Snapshot.ExecutionState == E_WorkflowExecutionState.Paused));

        Assert.Equal(0, Volatile.Read(ref afterCount));
        engine.Resume();
        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, afterCount);
    }

    [Fact]
    public void JsonStore_RoundTripsNestedBlockCanvas()
    {
        var catalogs = CreateCatalog();
        var block = new BlockNodeModel
        {
            Id = "Block",
            Title = "子流程",
            SubDocument = CreateChildDocument(),
            InputMappings = new List<BlockInputMapping>
            {
                new()
                {
                    TargetVariableName = "ChildInput",
                    Source = E_BlockInputSource.ParentVariable,
                    ParentVariableName = "ParentInput"
                }
            },
            OutputMappings = new List<BlockOutputMapping>
            {
                new()
                {
                    TargetVariableName = "ParentResult",
                    Source = E_BlockOutputSource.ChildNodeBinding,
                    ChildBinding = new WorkflowBindingKey("ChildAction", "Value")
                }
            }
        };
        var canvasDocument = new WorkflowDocument { Name = "Parent" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = block, X = 12, Y = 34 });
        canvasDocument.EntryNodeId = block.Id;
        var definition = new WorkflowCompiler(catalogs).Compile(canvasDocument);
        var store = new WorkflowDocumentJsonStore(catalogs);

        var json = store.Serialize(canvasDocument);
        var loaded = store.Deserialize(json).Canvas;

        Assert.Equal("ChildStart", definition.GetChildPlanOrThrow(block.Id).EntryNodeId);
        var loadedBlock = Assert.IsType<BlockNodeModel>(Assert.Single(loaded.Nodes).Node);
        Assert.Equal("ChildStart", loadedBlock.SubDocument.EntryNodeId);
        Assert.Equal(3, loadedBlock.SubDocument.Graph.Nodes.Count);
        Assert.Equal(2, loadedBlock.SubDocument.Graph.ControlConnections.Count);
        Assert.Equal("ParentInput", Assert.Single(loadedBlock.InputMappings).ParentVariableName);
        Assert.Equal(
            new WorkflowBindingKey("ChildAction", "Value"),
            Assert.Single(loadedBlock.OutputMappings).ChildBinding);
        Assert.Contains("SubDocument", json);
        Assert.DoesNotContain("SubCanvas", json);
    }

    private static (WorkflowEngine Engine, BlockNodeModel Block) CreateEngine(
        WorkflowDocument childDocument,
        WorkflowContext? context = null,
        Action<BlockNodeModel>? configure = null)
    {
        var catalog = CreateCatalog();
        var block = new BlockNodeModel
        {
            Id = "Block",
            Title = "子流程",
            SubDocument = childDocument
        };
        configure?.Invoke(block);
        var end = new EndNodeModel { Id = "ParentEnd", Title = "结束" };
        var parentDocument = new WorkflowDocument { Name = "Parent" };
        var parent = parentDocument.CanvasProjection;
        parent.Nodes.Add(new WorkflowCanvasNode { Node = block });
        parent.Nodes.Add(new WorkflowCanvasNode { Node = end });
        parent.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = block.Id,
            FromPort = WorkflowPorts.Success,
            ToNodeId = end.Id,
            ToPort = WorkflowPorts.Input
        });
        parentDocument.EntryNodeId = block.Id;
        var definition = new WorkflowCompiler(catalog).Compile(parentDocument);
        var handlers = new WorkflowNodeHandlerCatalog()
            .RegisterCompositeNodeHandlers()
            .Register(new StartNodeHandler())
            .Register(new ActionNodeHandler())
            .Register(new EndNodeHandler());
        return (new WorkflowEngine(definition, handlers, context), block);
    }

    private static WorkflowNodeCatalog CreateCatalog() =>
        new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();

    private static WorkflowDocument CreateChildDocument() => CreateChildDocument(
        new ActionNodeModel { Id = "ChildAction", Title = "动作", FunctionKey = "ChildAction" });

    private static WorkflowDocument CreateChildDocument(params ActionNodeModel[] actions)
    {
        var start = new StartNodeModel { Id = "ChildStart", Title = "开始" };
        var end = new EndNodeModel { Id = "ChildEnd", Title = "结束" };
        var canvasDocument = new WorkflowDocument { Name = "Child" };
        var canvas = canvasDocument.CanvasProjection;
        canvasDocument.EntryNodeId = start.Id;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = start });
        foreach (var action in actions)
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = action });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = end });
        var previousId = start.Id;
        foreach (var action in actions)
        {
            canvas.Connections.Add(Connect(previousId, action.Id));
            previousId = action.Id;
        }
        canvas.Connections.Add(Connect(previousId, end.Id));
        return canvasDocument;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed record NumericOutput(long Value);

    private static WorkflowConnectionModel Connect(string from, string to) => new()
    {
        FromNodeId = from,
        FromPort = WorkflowPorts.Success,
        ToNodeId = to,
        ToPort = WorkflowPorts.Input
    };
}
