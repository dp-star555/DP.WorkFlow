using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDiagnosticsModelTests
{
    [Fact]
    public void Diagnostics_RefreshAfterDocumentEditAndNavigateToNode()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var canvasDocument = new WorkflowDocument { Name = "Diagnostics" };
        var canvas = canvasDocument.CanvasProjection;
        var source = new StartNodeModel { Id = "Start", Title = "开始" };
        var unreachable = new EndNodeModel { Id = "Unused", Title = "未使用" };
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = source });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = unreachable });
        canvasDocument.EntryNodeId = source.Id;
        var session = new WorkflowDesignerSession(canvasDocument, catalog);
        using var diagnostics = new WorkflowDiagnosticsModel(session, source.Id);

        var warning = Assert.Single(diagnostics.Items, item => item.Code == "WF101");
        Assert.True(diagnostics.CanRun);
        Assert.True(diagnostics.NavigateTo(warning));
        Assert.Equal(unreachable.Id, session.SelectedNodeId);

        session.RemoveNode(unreachable.Id);
        Assert.DoesNotContain(diagnostics.Items, item => item.Code == "WF101");
    }
}
