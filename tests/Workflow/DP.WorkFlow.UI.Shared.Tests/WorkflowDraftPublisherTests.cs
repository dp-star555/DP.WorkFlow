using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDraftPublisherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dp-workflow-tests", Guid.NewGuid().ToString("N"), "flows");

    public WorkflowDraftPublisherTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);

    private sealed class RecordingGate : IWorkflowOperationGate
    {
        public List<WorkflowOperation> Operations { get; } = new();

        public Task RunAsync(WorkflowOperation operation, Func<Task> action)
        {
            Operations.Add(operation);
            return action();
        }
    }

    [Fact]
    public async Task Publish_ChecksDraft_GoesThroughGate_AndRecordsVersion()
    {
        var library = new WorkflowFlowLibrary(_root);
        library.CreateDraft("上料", "{\"Name\":\"x\",\"Nodes\":[{\"Id\":\"a\",\"Title\":\"开始\"}],\"Connections\":[]}");
        var gate = new RecordingGate();
        var issues = new List<string> { "找不到轴 card0[1]。" };
        var blocked = (string?)"有流程在运行";
        var publisher = new WorkflowDraftPublisher(library)
        {
            Gate = gate,
            CurrentUser = () => "张工",
            BlockedReason = () => blocked,
            CheckDraft = (name, json) => { Assert.Equal("上料", name); Assert.Contains("开始", json); return issues; }
        };

        Assert.Equal(new[] { "有流程在运行" }, publisher.Check("上料"));
        blocked = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync("上料", "第一版"));
        Assert.Empty(gate.Operations);
        Assert.Contains("第一次发布", publisher.DescribeChanges("上料"));

        issues.Clear();
        var version = await publisher.PublishAsync("上料", "第一版");
        Assert.Equal(1, version.Version);
        Assert.Equal("张工", version.SavedBy);
        var operation = Assert.Single(gate.Operations);
        Assert.Equal("workflow.design.publish", operation.Key);
        Assert.Equal("上料 第一版", operation.Describe());
        Assert.True(library.List().Single().IsPublished);

        File.WriteAllText(library.EnsureDraft("上料"), "{\"Name\":\"上料\",\"Nodes\":[],\"Connections\":[]}");
        await publisher.DiscardAsync("上料");
        Assert.Equal("workflow.design.discard", gate.Operations[^1].Key);
        Assert.False(library.HasDraftChanges("上料"));
        Assert.Equal(1, library.List().Single().NodeCount);
    }
}
