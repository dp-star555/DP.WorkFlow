using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPublicDataDesignerTests
{
    [Fact]
    public void SignalKey_OffersDeclaredSignalsOnly_AndKeepsUndeclaredCurrentValue()
    {
        var (session, node) = Create("Other");
        session.DeclarePublicData(WorkflowPublicDataDeclaration.Signal("Ready"));
        session.DeclarePublicData(WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.Int32));
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);

        var entry = inspector.Entries.Single(item => item.Name == nameof(SignalSetNodeModel.SignalKey));

        Assert.Equal(WorkflowPropertyEditorKind.Choice, entry.EditorKind);
        Assert.Equal(new object?[] { "Other", "Ready" }, entry.Choices.Select(choice => choice.Value).ToArray());
        Assert.Contains("未登记", entry.Choices[0].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void SignalKey_FallsBackToTextWithoutDeclarations()
    {
        var (session, node) = Create("Ready");
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);

        Assert.Equal(WorkflowPropertyEditorKind.Text,
            inspector.Entries.Single(item => item.Name == nameof(SignalSetNodeModel.SignalKey)).EditorKind);
    }

    [Fact]
    public void DeclareMissing_AddsUsedKeys_AndUndoRestores()
    {
        var (session, _) = Create("Ready");

        var added = session.DeclareMissingPublicData();

        Assert.Equal(WorkflowPublicDataDeclaration.Signal("Ready"), Assert.Single(added));
        Assert.Same(session.Document.PublicData, session.PublicData);
        Assert.Single(session.Document.PublicData);
        Assert.True(session.Undo());
        Assert.Empty(session.Document.PublicData);
        Assert.True(session.Redo());
        Assert.True(session.RemovePublicData("Ready"));
        Assert.Empty(session.PublicData);
    }

    private static (WorkflowDesignerSession Session, SignalSetNodeModel Node) Create(string key)
    {
        var node = new SignalSetNodeModel { Id = "Set", Title = "设置", SignalKey = key };
        var document = new WorkflowDocument { Name = "公共数据", EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes()) { SelectedNodeId = node.Id };
        return (session, node);
    }
}
