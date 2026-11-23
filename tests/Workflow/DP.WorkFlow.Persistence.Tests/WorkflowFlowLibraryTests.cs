using System.Text.Json.Nodes;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowFlowLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dp-workflow-tests", Guid.NewGuid().ToString("N"), "flows");
    private DateTime _now = new(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc);

    public WorkflowFlowLibraryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);

    private WorkflowFlowLibrary Library() => new(_root, () => _now);

    private static string Flow(string name, params (string Id, string Title, double X)[] nodes)
    {
        var json = new JsonObject
        {
            ["SchemaVersion"] = 5,
            ["Name"] = name,
            ["EntryNodeId"] = nodes.Length == 0 ? "" : nodes[0].Id,
            ["Nodes"] = new JsonArray(nodes.Select(node => (JsonNode)new JsonObject
            {
                ["Id"] = node.Id, ["Title"] = node.Title, ["NodeType"] = "Standard.Delay", ["X"] = node.X, ["Y"] = 0,
                ["Config"] = new JsonObject { ["Milliseconds"] = 10 }
            }).ToArray()),
            ["Connections"] = new JsonArray()
        };
        return json.ToJsonString();
    }

    [Fact]
    public void RecordVersion_SkipsUnchangedContent_AndListShowsLatestVersion()
    {
        var library = Library();
        File.WriteAllText(library.PathOf("主流程"), Flow("主流程", ("a", "开始", 0)));
        var first = library.RecordVersion("主流程", "张工");
        _now = _now.AddMinutes(1);
        var same = library.RecordVersion("主流程", "李工");
        File.WriteAllText(library.PathOf("主流程"), Flow("主流程", ("a", "开始", 0), ("b", "延时", 100)));
        var second = library.RecordVersion("主流程", "李工", "加了延时");

        Assert.Equal(1, first.Version);
        Assert.Equal(1, same.Version);
        Assert.Equal(2, second.Version);
        var entry = Assert.Single(library.List());
        Assert.Equal("主流程", entry.Name);
        Assert.Equal(2, entry.Version);
        Assert.Equal("李工", entry.ModifiedBy);
        Assert.Equal(2, entry.NodeCount);
        Assert.Equal(new[] { null, "加了延时" }, library.GetVersions("主流程").Select(version => version.Note));
    }

    [Fact]
    public void DuplicateRenameImport_KeepFlowNameEqualToFileName_AndLandAsDrafts()
    {
        var library = Library();
        File.WriteAllText(library.PathOf("上料"), Flow("上料", ("a", "开始", 0)));
        library.RecordVersion("上料", "张工");

        var copy = library.Duplicate("上料");
        Assert.Equal("上料-副本", copy);
        var copied = library.List().Single(entry => entry.Name == copy);
        Assert.Equal("上料-副本", copied.FlowName);
        Assert.False(copied.IsPublished);
        Assert.True(copied.HasDraft);
        Assert.False(File.Exists(library.PathOf(copy)));

        library.EnsureDraft("上料");
        library.Rename("上料", "上料搬运");
        Assert.False(File.Exists(library.PathOf("上料")));
        Assert.False(File.Exists(library.DraftPathOf("上料")));
        Assert.True(File.Exists(library.DraftPathOf("上料搬运")));
        Assert.Equal("上料搬运", library.List().Single(entry => entry.Name == "上料搬运").FlowName);
        Assert.Single(library.GetVersions("上料搬运"));
        Assert.Throws<InvalidOperationException>(() => library.Rename("上料搬运", "上料-副本"));

        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "上料搬运.json");
        File.WriteAllText(outside, Flow("别的名字", ("x", "开始", 0)));
        var imported = library.Import(outside);
        Assert.Equal("上料搬运2", imported);
        Assert.Equal("上料搬运2", library.List().Single(entry => entry.Name == imported).FlowName);
        Assert.False(File.Exists(library.PathOf(imported)));

        File.WriteAllText(outside, "{\"Name\":\"x\"}");
        Assert.Throws<InvalidDataException>(() => library.Import(outside, "坏文件"));
        Assert.Throws<ArgumentException>(() => library.PathOf("..\\外面"));
    }

    [Fact]
    public void Drafts_DoNotTouchPublishedFile_UntilPublished()
    {
        var library = Library();
        File.WriteAllText(library.PathOf("主流程"), Flow("主流程", ("a", "开始", 0)));
        library.RecordVersion("主流程", "张工");

        var draft = library.EnsureDraft("主流程");
        Assert.Equal(library.DraftPathOf("主流程"), draft);
        Assert.Equal("主流程", library.NameOf(draft));
        Assert.False(library.HasDraftChanges("主流程"));
        Assert.False(library.List().Single().HasDraft);

        File.WriteAllText(draft, Flow("主流程", ("a", "开始", 0), ("b", "延时", 100)));
        Assert.True(library.HasDraftChanges("主流程"));
        Assert.Equal(1, library.List().Single().NodeCount);
        Assert.True(library.List().Single().HasDraft);
        Assert.Equal(new[] { "延时" }, library.CompareDraft("主流程").AddedNodes);

        _now = _now.AddMinutes(5);
        var published = library.Publish("主流程", "李工", "加延时");
        Assert.Equal(2, published.Version);
        Assert.Equal("加延时", published.Note);
        Assert.False(File.Exists(draft));
        var entry = library.List().Single();
        Assert.Equal(2, entry.NodeCount);
        Assert.False(entry.HasDraft);
        Assert.Equal("李工", entry.ModifiedBy);

        // 放弃草稿回到已发布的。
        File.WriteAllText(library.EnsureDraft("主流程"), Flow("主流程"));
        library.DiscardDraft("主流程");
        Assert.Equal(2, library.List().Single().NodeCount);
        Assert.False(File.Exists(draft));

        // 新建的流程只有草稿，发布后才有正式文件和第一版。
        library.CreateDraft("新流程", Flow("随便", ("a", "开始", 0)));
        var fresh = library.List().Single(item => item.Name == "新流程");
        Assert.False(fresh.IsPublished);
        Assert.Equal("新流程", fresh.FlowName);
        Assert.Equal(1, library.Publish("新流程").Version);
        Assert.True(library.List().Single(item => item.Name == "新流程").IsPublished);
        Assert.Equal("发布", library.GetVersions("新流程").Single().Note);
    }

    [Fact]
    public void DeleteAndRestore_KeepHistory_RestoreGoesToDraft()
    {
        var library = Library();
        File.WriteAllText(library.PathOf("剔除"), Flow("剔除", ("a", "开始", 0)));
        library.RecordVersion("剔除", "张工");
        File.WriteAllText(library.PathOf("剔除"), Flow("剔除", ("a", "开始", 0), ("b", "吹气", 50)));
        library.EnsureDraft("剔除");

        library.Delete("剔除", "王工");
        Assert.Empty(library.List());
        Assert.False(File.Exists(library.DraftPathOf("剔除")));
        var deleted = Assert.Single(library.List(includeDeleted: true));
        Assert.True(deleted.IsDeleted);
        Assert.Equal(2, deleted.Version);
        Assert.Equal("删除前", library.GetVersions("剔除")[1].Note);
        Assert.Equal("剔除2", library.UniqueName("剔除"));

        library.Restore("剔除", 1);
        var restored = Assert.Single(library.List());
        Assert.False(restored.IsPublished);
        Assert.Equal(1, restored.NodeCount);
        library.Publish("剔除", "王工", "从 v1 恢复");
        Assert.True(library.List().Single().IsPublished);
        // 内容和 v1 相同，但和最新一版（删除前）不同，所以记成 v3。
        Assert.Equal(new[] { null, "删除前", "从 v1 恢复" }, library.GetVersions("剔除").Select(version => version.Note));

        library.Restore("剔除", 2);
        Assert.Equal(1, library.List().Single().NodeCount);
        Assert.True(library.HasDraftChanges("剔除"));
    }

    [Fact]
    public void Compare_SeparatesChangedMovedAddedRemoved()
    {
        var older = Flow("流程", ("a", "开始", 0), ("b", "延时", 100), ("c", "判定", 200));
        var newer = JsonNode.Parse(Flow("流程2", ("a", "开始", 40), ("b", "延时", 100), ("d", "剔除", 300)))!.AsObject();
        newer["Nodes"]![1]!["Config"]!["Milliseconds"] = 20;
        newer["Connections"]!.AsArray().Add(new JsonObject { ["FromNodeId"] = "a", ["FromPort"] = "Success", ["ToNodeId"] = "b", ["ToPort"] = "Input" });

        var diff = WorkflowFlowLibrary.Compare(older, newer.ToJsonString());

        Assert.Equal(new[] { "剔除" }, diff.AddedNodes);
        Assert.Equal(new[] { "判定" }, diff.RemovedNodes);
        Assert.Equal(new[] { "延时" }, diff.ChangedNodes);
        Assert.Equal(new[] { "开始" }, diff.MovedNodes);
        Assert.Equal(new[] { "开始.Success → 延时" }, diff.AddedConnections);
        Assert.Empty(diff.RemovedConnections);
        Assert.Contains(diff.OtherChanges, text => text.StartsWith("流程名", StringComparison.Ordinal));
        Assert.True(WorkflowFlowLibrary.Compare(older, older).IsSame);
    }
}
