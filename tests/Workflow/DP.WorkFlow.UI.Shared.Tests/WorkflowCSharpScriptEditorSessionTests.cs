using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowCSharpScriptEditorSessionTests
{
    [Fact]
    public async Task EditingAfterFailure_PreservesLastDiagnosticsAsStale()
    {
        var canvasDocument = new WorkflowDocument { Name = "脚本状态" };
        var canvas = canvasDocument.CanvasProjection;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var designer = new WorkflowDesignerSession(canvasDocument, catalog);
        var node = new CSharpScriptNodeModel { Id = "Script", Script = "not valid C#" };
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var page = new WorkflowScriptEditorPageModel(designer, node);
        var session = new WorkflowCSharpScriptEditorSession(page);
        using var service = new ScriptEngine.RoslynScriptService();

        var result = await session.CompileAsync(service);
        var previous = session.Diagnostics;
        session.SetSource(session.Source + " ");

        Assert.False(result.Success);
        Assert.NotEmpty(previous);
        Assert.Same(previous, session.Diagnostics);
        Assert.Equal(WorkflowScriptCompilationState.ModifiedAfterCompilation, session.State);
        Assert.True(session.DiagnosticsAreStale);
        Assert.Contains("上次编译", session.GetStatusText(), StringComparison.Ordinal);
    }
}
