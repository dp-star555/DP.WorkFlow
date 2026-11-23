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
    public void DuplicateRenameImport_KeepFlowNameEqualToFileName()
    {
        var library = Library();
        File.WriteAllText(library.PathOf("上料"), Flow("上料", ("a", "开始", 0)));
        library.RecordVersion("上料", "张工");

        var copy = library.Duplicate("上料");
        Assert.Equal("上料-副本", copy);
        Assert.Equal("上料-副本", library.List().Single(entry => entry.Name == copy).FlowName);

        library.Rename("上料", "上料搬运");
        Assert.False(File.Exists(library.PathOf("上料")));
        Assert.Equal("上料搬运", library.List().Single(entry => entry.Name == "上料搬运").FlowName);
        Assert.Single(library.GetVersions("上料搬运"));
        Assert.Throws<InvalidOperationException>(() => library.Rename("上料搬运", "上料-副本"));

        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "上料搬运.json");
        File.WriteAllText(outside, Flow("别的名字", ("x", "开始", 0)));
        var imported = library.Import(outside);
        Assert.Equal("上料搬运2", imported);
        Assert.Equal("上料搬运2", library.List().Single(entry => entry.Name == imported).FlowName);

        File.WriteAllText(outside, "{\"Name\":\"x\"}");
        Assert.Throws<InvalidDataException>(() => library.Import(outside, "坏文件"));
        Assert.Throws<ArgumentException>(() => library.PathOf("..\\外面"));
    }

    [Fact]
    public void DeleteAndRestore_KeepHistory()
    {
        var library = Library();
        File.WriteAllText(library.PathOf("剔除"), Flow("剔除", ("a", "开始", 0)));
        library.RecordVersion("剔除", "张工");
        File.WriteAllText(library.PathOf("剔除"), Flow("剔除", ("a", "开始", 0), ("b", "吹气", 50)));

        library.Delete("剔除", "王工");
        Assert.Empty(library.List());
        var deleted = Assert.Single(library.List(includeDeleted: true));
        Assert.True(deleted.IsDeleted);
        Assert.Equal(2, deleted.Version);
        Assert.Equal("删除前", library.GetVersions("剔除")[1].Note);
        Assert.Equal("剔除2", library.UniqueName("剔除"));

        library.Restore("剔除", 1, "王工");
        var restored = Assert.Single(library.List());
        Assert.Equal(1, restored.NodeCount);
        Assert.Equal("从 v1 恢复", library.GetVersions("剔除").Last().Note);

        library.Restore("剔除", 2, "王工");
        Assert.Equal(2, library.List().Single().NodeCount);
        // 当前内容就是上一版（从 v1 恢复的那版），“恢复到 v2 前”不重复记。
        Assert.Equal(new[] { null, "删除前", "从 v1 恢复", "从 v2 恢复" }, library.GetVersions("剔除").Select(version => version.Note));

        File.WriteAllText(library.PathOf("剔除"), Flow("剔除"));
        library.Restore("剔除", 1, "王工");
        Assert.Equal("恢复到 v1 前", library.GetVersions("剔除")[^2].Note);
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
