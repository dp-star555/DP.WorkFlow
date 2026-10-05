using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDiagnosticsModelTests
{
    [Fact]
    public void DomainErrors_BlockRun_ButHistoricalResourceFailureAllowsRetry()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var document = Document(new StartNodeModel { Id = "start" });
        var session = new WorkflowDesignerSession(document, catalog);
        var provider = new Provider { Items = new[] { new WorkflowDiagnosticItem("ALG_MISSING", WorkflowValidationSeverity.Error, "missing", "start") } };
        using var diagnostics = new WorkflowDiagnosticsModel(session, "start", provider, null);
        Assert.False(diagnostics.CanRun);
        provider.Items = new[] { new WorkflowDiagnosticItem("ALG_NATIVE", WorkflowValidationSeverity.Error, "licence", "start") { BlocksRun = false } };
        diagnostics.Refresh(); Assert.True(diagnostics.CanRun); Assert.Single(diagnostics.Items);
        session.AddNode("End", 20, 20); Assert.Equal(1, provider.Invalidations);
    }

    [Fact]
    public void NestedDiagnostic_UsesEncodedPath_AndNeverSelectsSameIdInWrongDocument()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();
        var child = Document(new StartNodeModel { Id = "same" });
        var block = new BlockNodeModel { Id = "parent/a|b", SubDocument = child };
        var root = Document(new StartNodeModel { Id = "same" }, block);
        var navigator = new WorkflowDesignerNavigator(root, catalog);
        using var diagnostics = new WorkflowDiagnosticsModel(navigator.RootSession, "same", null, navigator);
        var issue = new WorkflowDiagnosticItem("ALG_MISSING", WorkflowValidationSeverity.Error, "missing", "same")
            { PlanPath = "$/" + Uri.EscapeDataString(block.Id), FromRoot = true };
        Assert.True(diagnostics.NavigateTo(issue)); Assert.Same(root, navigator.CurrentSession.Document);
        Assert.Equal(block.Id, navigator.CurrentSession.SelectedNodeId);
        Assert.False(diagnostics.NavigateTo(issue with { PlanPath = "$/unknown" }));
        Assert.Equal(block.Id, navigator.CurrentSession.SelectedNodeId);
        Assert.True(diagnostics.NavigateTo(issue with { PlanPath = "$" }));
        Assert.Same(root, navigator.CurrentSession.Document);
        Assert.Equal("same", navigator.CurrentSession.SelectedNodeId);
    }

    private static WorkflowDocument Document(params IWorkflowNodeModel[] nodes)
    {
        var document = new WorkflowDocument { EntryNodeId = nodes[0].Id };
        foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        return document;
    }
    private sealed class Provider : IWorkflowDiagnosticProvider
    {
        public IReadOnlyList<WorkflowDiagnosticItem> Items { get; set; } = Array.Empty<WorkflowDiagnosticItem>();
        public int Invalidations { get; private set; }
        public IReadOnlyList<WorkflowDiagnosticItem> Analyze(WorkflowDocument document) => Items;
        public void Invalidate() { Invalidations++; Changed?.Invoke(this, EventArgs.Empty); }
        public event EventHandler? Changed;
    }

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
