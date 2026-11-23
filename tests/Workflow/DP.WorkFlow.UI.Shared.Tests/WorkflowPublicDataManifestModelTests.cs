using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPublicDataManifestModelTests
{
    [Fact]
    public void Rows_ListDeclaredFirstThenUndeclaredUses()
    {
        var (session, _) = CreateSession(("Add1", "Parts", E_SignalValueType.String));
        session.PublicData.Add(WorkflowPublicDataDeclaration.Signal("Ready"));
        using var model = new WorkflowPublicDataManifestModel(session);

        Assert.Equal(new[] { "Ready", "Parts" }, model.Rows.Select(row => row.Key));
        var ready = model.Rows[0];
        Assert.True(ready.Declared);
        Assert.True(ready.Unused);
        Assert.Equal("未使用", ready.StatusText);
        var parts = model.Rows[1];
        Assert.False(parts.Declared);
        Assert.Equal("未登记", parts.StatusText);
        Assert.Equal("Add1", Assert.Single(parts.Uses).NodeId);
        Assert.Equal(1, model.UndeclaredCount);
    }

    [Fact]
    public void DeclareMissing_AddsInferredItemsAndCanBeUndone()
    {
        var (session, _) = CreateSession(("Add1", "Parts", E_SignalValueType.String));
        using var model = new WorkflowPublicDataManifestModel(session);

        Assert.Equal(1, model.DeclareMissing());
        Assert.True(model.Rows.Single().Declared);
        Assert.Equal(0, model.UndeclaredCount);

        Assert.True(session.Undo());
        Assert.Empty(session.PublicData);
        Assert.False(model.Rows.Single().Declared);
    }

    [Fact]
    public void Save_MismatchedTypeShowsErrorOnRow()
    {
        var (session, _) = CreateSession(("Add1", "Parts", E_SignalValueType.String));
        using var model = new WorkflowPublicDataManifestModel(session);

        model.Save(WorkflowPublicDataDeclaration.Queue("Parts", E_SignalValueType.Int32));

        var row = model.Rows.Single();
        Assert.True(row.HasError);
        Assert.Contains(row.Diagnostics, item => item.Code == WorkflowPublicDataManifest.MismatchCode);
    }

    [Fact]
    public void Save_RenamesInOneUndoStep()
    {
        var (session, _) = CreateSession();
        session.PublicData.Add(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "0"));
        using var model = new WorkflowPublicDataManifestModel(session);

        model.Save(WorkflowPublicDataDeclaration.Value("Total", E_SignalValueType.Int32, "5"), "Count");

        Assert.Equal("Total", Assert.Single(session.PublicData).Key);
        Assert.True(session.Undo());
        Assert.Equal("Count", Assert.Single(session.PublicData).Key);
    }

    [Theory]
    [InlineData("", WorkflowPublicDataKind.Value, E_SignalValueType.Int32, null, "键不能为空。")]
    [InlineData("Count", WorkflowPublicDataKind.Value, E_SignalValueType.Int32, null, "键 Count 已经登记过。")]
    [InlineData("New", WorkflowPublicDataKind.Value, E_SignalValueType.Int32, "abc", "默认值 \"abc\" 不是 Int32。")]
    [InlineData("New", WorkflowPublicDataKind.Signal, E_SignalValueType.Int32, null, "bool 信号的值类型必须是 Boolean。")]
    public void Check_ReportsInvalidDeclarations(string key, WorkflowPublicDataKind kind, E_SignalValueType type, string? defaultValue, string expected)
    {
        var (session, _) = CreateSession();
        session.PublicData.Add(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32));
        using var model = new WorkflowPublicDataManifestModel(session);

        Assert.Equal(expected, model.Check(new WorkflowPublicDataDeclaration(key, kind, type, defaultValue)));
        Assert.Throws<ArgumentException>(() => model.Save(new WorkflowPublicDataDeclaration(key, kind, type, defaultValue)));
    }

    [Fact]
    public void Check_AllowsKeepingOwnKeyWhenEditing()
    {
        var (session, _) = CreateSession();
        session.PublicData.Add(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32));
        using var model = new WorkflowPublicDataManifestModel(session);

        Assert.Null(model.Check(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int64, "12"), "Count"));
    }

    private static (WorkflowDesignerSession Session, WorkflowDocument Document) CreateSession(
        params (string Id, string Key, E_SignalValueType Type)[] queueAdds)
    {
        var document = new WorkflowDocument();
        var x = 0;
        foreach (var (id, key, type) in queueAdds)
        {
            document.Nodes.Add(new WorkflowCanvasNode { Node = new QueueAddNodeModel { Id = id, QueueKey = key, ValueType = type }, X = x });
            x += 240;
        }
        return (new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes()), document);
    }
}
