using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowInputLiteralEditingTests
{
    [Theory]
    [InlineData(nameof(InputNode.Number), true)]
    [InlineData(nameof(InputNode.Text), true)]
    [InlineData(nameof(InputNode.Flag), true)]
    [InlineData(nameof(InputNode.Mode), true)]
    [InlineData(nameof(InputNode.Optional), true)]
    [InlineData(nameof(InputNode.Identity), true)]
    [InlineData(nameof(InputNode.Duration), true)]
    [InlineData(nameof(InputNode.Object), false)]
    [InlineData(nameof(InputNode.Array), false)]
    [InlineData(nameof(InputNode.Map), false)]
    public void TextEditingPolicy_DependsOnValueTypeWithoutDomainSpecialCases(string name, bool editable)
    {
        var fixture = Create();
        using var inspector = new WorkflowPropertyInspectorModel(fixture.Session, fixture.Node.Id);
        Assert.Equal(editable, inspector.Entries.Single(entry => entry.Name == name).CanEditInputLiteralAsText);
    }

    [Fact]
    public void SwitchingSource_PreservesLiteral_AndSupportsUndoRedoAndRecipeRoundTrip()
    {
        var fixture = Create();
        using var inspector = new WorkflowPropertyInspectorModel(fixture.Session, fixture.Node.Id);
        WorkflowPropertyEntry Number() => inspector.Entries.Single(entry => entry.Name == nameof(InputNode.Number));
        var binding = WorkflowBindingKey.FromPublicData("Counter");
        inspector.SetWorkflowInput(Number(), WorkflowValueSource.Binding, null, binding);
        Assert.Equal(42, fixture.Node.Number.LiteralValue);
        inspector.SetWorkflowInput(Number(), WorkflowValueSource.Literal, Number().GetInputLiteral(), null);
        Assert.Equal(42, fixture.Node.Number.LiteralValue);
        Assert.Equal(WorkflowValueSource.Literal, fixture.Node.Number.Source);
        Assert.True(fixture.Session.Undo()); Assert.Equal(WorkflowValueSource.Binding, fixture.Node.Number.Source);
        Assert.Equal(binding, fixture.Node.Number.Binding); Assert.Equal(42, fixture.Node.Number.LiteralValue);
        var store = new WorkflowDocumentJsonStore(fixture.Catalog);
        var restored = Assert.IsType<InputNode>(store.Deserialize(store.Serialize(fixture.Session.Document)).Document.Graph.Nodes.Single());
        Assert.Equal(42, restored.Number.LiteralValue); Assert.Equal(binding, restored.Number.Binding);
        Assert.True(fixture.Session.Redo()); Assert.Equal(WorkflowValueSource.Literal, fixture.Node.Number.Source);
    }

    [Fact]
    public void UnsupportedText_IsRejectedBeforeInvalidCast_AndLeavesConfigurationUntouched()
    {
        var fixture = Create();
        using var inspector = new WorkflowPropertyInspectorModel(fixture.Session, fixture.Node.Id);
        var entry = inspector.Entries.Single(property => property.Name == nameof(InputNode.Object));
        var error = Assert.Throws<InvalidOperationException>(() => inspector.SetWorkflowInput(entry, WorkflowValueSource.Literal, "", null));
        Assert.Contains("绑定或专用编辑器", error.Message);
        Assert.Equal(WorkflowValueSource.Literal, fixture.Node.Object.Source);
        Assert.Null(fixture.Node.Object.LiteralValue);
        Assert.False(fixture.Session.Undo());
    }

    [Theory]
    [InlineData(nameof(InputNode.Number), "123")]
    [InlineData(nameof(InputNode.Text), "")]
    [InlineData(nameof(InputNode.Flag), "True")]
    [InlineData(nameof(InputNode.Mode), "Binding")]
    [InlineData(nameof(InputNode.Optional), "")]
    [InlineData(nameof(InputNode.Identity), "00000000-0000-0000-0000-000000000001")]
    [InlineData(nameof(InputNode.Duration), "00:00:02")]
    public void SupportedLiterals_ParseAsTheirDeclaredType(string name, string text)
    {
        var fixture = Create();
        using var inspector = new WorkflowPropertyInspectorModel(fixture.Session, fixture.Node.Id);
        var entry = inspector.Entries.Single(property => property.Name == name);
        inspector.SetWorkflowInput(entry, WorkflowValueSource.Literal, text, null);
        var literal = entry.GetInputLiteral();
        if (name == nameof(InputNode.Optional)) Assert.Null(literal);
        else Assert.IsType(entry.WorkflowInputType!, literal);
    }

    public sealed class InputNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.GenericInputKinds";
        public WorkflowInput<int> Number { get; set; } = WorkflowInput<int>.FromLiteral(42);
        public WorkflowInput<string> Text { get; set; } = WorkflowInput<string>.FromLiteral("initial");
        public WorkflowInput<bool> Flag { get; set; } = WorkflowInput<bool>.FromLiteral(false);
        public WorkflowInput<WorkflowValueSource> Mode { get; set; } = WorkflowInput<WorkflowValueSource>.FromLiteral(WorkflowValueSource.Literal);
        public WorkflowInput<int?> Optional { get; set; } = WorkflowInput<int?>.FromLiteral(null);
        public WorkflowInput<Guid> Identity { get; set; } = WorkflowInput<Guid>.FromLiteral(Guid.Empty);
        public WorkflowInput<TimeSpan> Duration { get; set; } = WorkflowInput<TimeSpan>.FromLiteral(TimeSpan.Zero);
        public WorkflowInput<Payload> Object { get; set; } = WorkflowInput<Payload>.FromLiteral(null);
        public WorkflowInput<int[]> Array { get; set; } = WorkflowInput<int[]>.FromLiteral(null);
        public WorkflowInput<Dictionary<string, int>> Map { get; set; } = WorkflowInput<Dictionary<string, int>>.FromLiteral(null);
    }

    public sealed class Payload { public int Value { get; set; } }

    private static (InputNode Node, WorkflowDesignerSession Session, WorkflowNodeCatalog Catalog) Create()
    {
        var node = new InputNode { Id = "input" };
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new() { Node = node });
        var catalog = new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<InputNode>());
        return (node, new WorkflowDesignerSession(document, catalog) { SelectedNodeId = node.Id }, catalog);
    }
}
