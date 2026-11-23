using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowHostPropertyChoicesTests
{
    private sealed class DeviceNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Device";

        [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.HostChoicePrefix + "Test.Device")]
        public string Device { get; set; } = string.Empty;

        [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.HostChoicePrefix + "Test.Missing")]
        public string Other { get; set; } = string.Empty;
    }

    private sealed class Source(string key, params string[] values) : IWorkflowPropertyChoiceSource
    {
        public IReadOnlyList<KeyValuePair<string, object?>>? GetChoices(string editorKey, string propertyName) =>
            editorKey == key ? values.Select(v => new KeyValuePair<string, object?>(v, v)).ToArray() : null;
    }

    [Fact]
    public void HostKeys_BecomeChoices_FromFirstAnsweringSource_AndFallBackToText()
    {
        var services = new WorkflowServiceProvider()
            .AddPropertyChoices(new Source("Host/Other", "x"))
            .AddPropertyChoices(new Source("Host/Test.Device", "card0", "card1"));
        var provider = WorkflowHostPropertyChoices.CreateProvider(WorkflowHostPropertyChoices.FromServices(services));

        var entries = WorkflowPropertyInspectorModel.CreateEntries(new DeviceNode(), provider);
        var device = entries.Single(entry => entry.Name == nameof(DeviceNode.Device));
        Assert.Equal(WorkflowPropertyEditorKind.Choice, device.EditorKind);
        Assert.Equal(new[] { "card0", "card1" }, device.Choices.Select(choice => choice.Label));
        Assert.Equal(WorkflowPropertyEditorKind.Text, entries.Single(entry => entry.Name == nameof(DeviceNode.Other)).EditorKind);
    }
}
