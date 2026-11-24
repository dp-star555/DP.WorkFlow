using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodePropertyChoicesTests
{
    [Fact]
    public void NodeChoice_UsesServicesHandedToInspector_AndFallsBackToTextWithout()
    {
        var node = new AxisProbeNodeModel { Id = "Axis", Title = "轴" };
        var document = new WorkflowDocument { Name = "候选", EntryNodeId = node.Id };
        document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().Register<AxisProbeNodeModel>()) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);

        Assert.Equal(WorkflowPropertyEditorKind.Text, Entry().EditorKind);

        inspector.Services = new WorkflowServiceProvider().Add(new AxisNames("X", "Y"));

        Assert.Equal(WorkflowPropertyEditorKind.Choice, Entry().EditorKind);
        Assert.Equal(new object?[] { "X", "Y" }, Entry().Choices.Select(choice => choice.Value).ToArray());

        WorkflowPropertyEntry Entry() => inspector.Entries.Single(item => item.Name == nameof(AxisProbeNodeModel.AxisName));
    }

    public sealed class AxisNames(params string[] names)
    {
        public IReadOnlyList<string> Names { get; } = names;
    }

    [WorkflowNode("Test.AxisProbe", DisplayName = "轴探测", Category = "测试")]
    public sealed class AxisProbeNodeModel : WorkflowNodeModel, IWorkflowPropertyChoices
    {
        public override string NodeType => "Test.AxisProbe";

        [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.NodeChoice)]
        public string AxisName { get; set; } = string.Empty;

        public IReadOnlyList<KeyValuePair<string, object?>> GetPropertyChoices(string propertyName, WorkflowPropertyChoiceContext context) =>
            propertyName == nameof(AxisName) && context.Services?.GetService(typeof(AxisNames)) is AxisNames axes
                ? axes.Names.Select(name => new KeyValuePair<string, object?>(name, name)).ToArray()
                : [];
    }
}
