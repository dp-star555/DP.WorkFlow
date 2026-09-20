namespace DP.WorkFlow.Tests;

public sealed class CSharpScriptNodeTests
{
    [Fact]
    public async Task Script_ExecutesAgainstControlledGlobalsAndReturnsValue()
    {
        var node = new CSharpScriptNodeModel
        {
            Id = "Script",
            Title = "脚本",
            Script = "SetVariable(\"FromScript\", 21); Trace(\"done\"); return GetVariable<int>(\"FromScript\") * 2;",
            ResultVarKey = "Result"
        };
        var canvasDocument = new WorkflowDocument { Name = "脚本" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var context = new WorkflowContext();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);

        var run = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(new CSharpScriptNodeHandler()), context).RunAsync();

        Assert.True(run.Success, run.Message);
        Assert.True(context.TryGetVariable<int>("Result", out var result));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ConsoleWriteLine_IsPublishedToRuntimeOutputTrace()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "脚本", Script = "Console.WriteLine(\"运行成功\"); return null;" };
        var canvasDocument = new WorkflowDocument { Name = "脚本输出" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var engine = new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(new CSharpScriptNodeHandler()));

        var run = await engine.RunAsync();

        Assert.True(run.Success, run.Message);
        Assert.Contains(engine.GetTraceBatch().Entries, item => item.Step == "ScriptOutput" && item.Message == "运行成功");
    }

    [Fact]
    public async Task CompilationFailure_CanContinueAlongErrorPort()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "脚本", Script = "not valid C#", ContinueOnError = true };
        var canvasDocument = new WorkflowDocument { Name = "脚本错误" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);

        var run = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(new CSharpScriptNodeHandler())).RunAsync();

        Assert.True(run.Success, run.Message);
    }

    [Fact]
    public void LegacySource_CommentMentioningProgramContract_IsStillWrapped()
    {
        const string legacy = "// ICSharpProgram 只是说明文字\nreturn 42;";

        var normalized = WorkflowCSharpProgramSource.Normalize(legacy);

        Assert.Contains("public sealed class WorkflowProgram : ICSharpProgram", normalized, StringComparison.Ordinal);
        Assert.Contains("return 42;", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CooperativeTimeout_CanContinueAlongErrorPort()
    {
        var node = new CSharpScriptNodeModel
        {
            Id = "Script",
            Title = "超时脚本",
            Script = "await Task.Delay(Timeout.Infinite, CancellationToken); return null;",
            TimeoutMilliseconds = 50,
            ContinueOnError = true
        };
        var canvasDocument = new WorkflowDocument { Name = "脚本超时" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);

        var run = await new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().Register(new CSharpScriptNodeHandler())).RunAsync();

        Assert.True(run.Success, run.Message);
    }

    [Fact]
    public void ErrorPort_IsOnlyVisibleWhenContinueOnErrorIsEnabled()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var descriptor = catalog.GetOrThrow("CSharpScript");
        var node = new CSharpScriptNodeModel();

        Assert.DoesNotContain(descriptor.GetPorts(node), port => port.Key == "Error");
        node.ContinueOnError = true;
        Assert.Contains(descriptor.GetPorts(node), port => port.Key == "Error");
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task UnchangedWorkflowScript_UsesCachedNormalizationAndCompilationEnvironment()
    {
        using var scriptService = new ScriptEngine.RoslynScriptService();
        var node = new CSharpScriptNodeModel
        {
            Id = "AllocationScript",
            ScriptId = "workflow-allocation-script",
            ResultVarKey = string.Empty,
            TimeoutMilliseconds = 0,
            Script = "return 42;"
        };
        var canvasDocument = new WorkflowDocument { Name = "脚本热路径分配" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var engine = new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().Register(new CSharpScriptNodeHandler(scriptService)));

        for (var index = 0; index < 10; index++)
            Assert.True((await engine.RunAsync()).Success);

        var before = GC.GetAllocatedBytesForCurrentThread();
        const int runCount = 100;
        for (var index = 0; index < runCount; index++)
            Assert.True((await engine.RunAsync()).Success);
        var allocatedPerRun = (GC.GetAllocatedBytesForCurrentThread() - before) / runCount;

        Assert.True(
            allocatedPerRun < 48 * 1024,
            $"未修改工作流脚本每次运行分配 {allocatedPerRun:N0} 字节，疑似重复规范化或构建编译环境。");
    }
#endif

    [Fact]
    public async Task ScriptRevision_StaysHotUntilSourceChangesAndThenReplacesOldVersion()
    {
        using var scriptService = new ScriptEngine.RoslynScriptService();
        var handler = new CSharpScriptNodeHandler(scriptService);
        var node = new CSharpScriptNodeModel
        {
            Id = "Script",
            ScriptId = "workflow-hot-script",
            ResultVarKey = "Result",
            Script = CounterProgram("return ++_counter;")
        };
        var context = new WorkflowContext();

        Assert.Equal(1, await RunAndReadResultAsync(node, handler, context));
        Assert.Equal(2, await RunAndReadResultAsync(node, handler, context));

        node.Script = CounterProgram("_counter += 10; return _counter;");

        Assert.Equal(10, await RunAndReadResultAsync(node, handler, context));
        Assert.Equal(20, await RunAndReadResultAsync(node, handler, context));
    }

    private static async Task<int> RunAndReadResultAsync(
        CSharpScriptNodeModel node,
        CSharpScriptNodeHandler handler,
        WorkflowContext context)
    {
        var canvasDocument = new WorkflowDocument { Name = "脚本热替换" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(canvasDocument);
        var run = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(handler), context).RunAsync();
        Assert.True(run.Success, run.Message);
        Assert.True(context.TryGetVariable<int>(node.ResultVarKey, out var result));
        return result;
    }

    private static string CounterProgram(string body) => $$"""
using ScriptEngine;
using DP.WorkFlow;

public sealed class WorkflowCounterProgram : ICSharpProgram
{
    private static int _counter;

    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        {{body}}
    }
}
""";
}
