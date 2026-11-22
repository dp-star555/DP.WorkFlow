using System.Text.Json.Nodes;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPublicDataPersistenceTests
{
    [Fact]
    public void Schema5_RoundTripsPublicDataManifest()
    {
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterStandardNodes());
        var document = BuildDocument();
        document.PublicData.Add(WorkflowPublicDataDeclaration.Signal("Ready", defaultValue: true, persistent: true));
        document.PublicData.Add(new WorkflowPublicDataDeclaration("Jobs", WorkflowPublicDataKind.Queue, E_SignalValueType.Int32, DisplayName: "任务"));
        document.PublicData.Add(new WorkflowPublicDataDeclaration("Handshake", WorkflowPublicDataKind.Signal, E_SignalValueType.Boolean, ResetOnRecovery: true));

        var json = store.Serialize(document);
        var loaded = store.Deserialize(json);

        Assert.Contains("\"Kind\": \"Queue\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ValueType\": \"Int32\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ResetOnRecovery\": true", json, StringComparison.Ordinal);
        Assert.Empty(loaded.Migration.Warnings);
        Assert.Equal(document.PublicData, loaded.Document.PublicData);
        Assert.Equal(json, store.Serialize(loaded.Document));
    }

    [Fact]
    public void Schema4_InfersManifestFromNodes()
    {
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterStandardNodes());
        var root = JsonNode.Parse(store.Serialize(BuildDocument()))!.AsObject();
        root["SchemaVersion"] = 4;
        root.Remove("PublicData");

        var loaded = store.Deserialize(root.ToJsonString());

        Assert.Equal(4, loaded.Migration.SourceSchemaVersion);
        Assert.Contains(loaded.Migration.Warnings, warning => warning.Contains("公共数据清单", StringComparison.Ordinal));
        Assert.Equal(new[]
        {
            WorkflowPublicDataDeclaration.Signal("Ready", defaultValue: true),
            WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32),
            WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.String)
        }, loaded.Document.PublicData);
    }

    private static WorkflowDocument BuildDocument()
    {
        var document = new WorkflowDocument { Name = "公共数据", EntryNodeId = "Init" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new SignalStateBatchInitializeNodeModel
            {
                Id = "Init",
                Title = "初始化",
                Items = { new SignalStateInitItem { SignalKey = "Ready", DefaultState = true } }
            }
        });
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new SignalSetNodeModel { Id = "Set", Title = "设置", SignalKey = "Ready" }
        });
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new SignalValueSetNodeModel { Id = "Count", Title = "计数", SignalKey = "Count", ValueType = E_SignalValueType.Int32, Value = WorkflowInput<string>.FromLiteral("1") }
        });
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new QueueAddNodeModel { Id = "Add", Title = "入队", QueueKey = "Jobs", ValueType = E_SignalValueType.String }
        });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Init", FromPort = WorkflowPorts.Success, ToNodeId = "Set", ToPort = WorkflowPorts.Input });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Set", FromPort = WorkflowPorts.Success, ToNodeId = "Count", ToPort = WorkflowPorts.Input });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Count", FromPort = WorkflowPorts.Success, ToNodeId = "Add", ToPort = WorkflowPorts.Input });
        return document;
    }
}

public sealed class WorkflowLegacyValueInputMigrationTests
{
    [Fact]
    public void ThreePieceInputs_AreMergedIntoOneInput()
    {
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterStandardNodes());
        var document = new WorkflowDocument { Name = "迁移", EntryNodeId = "Add" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new QueueAddNodeModel { Id = "Add", Title = "入队", QueueKey = "Jobs" } });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new SignalValueWaitNodeModel { Id = "Wait", Title = "等待", SignalKey = "Count" } });
        var root = System.Text.Json.Nodes.JsonNode.Parse(store.Serialize(document))!.AsObject();
        var add = root["Nodes"]![0]!["Config"]!.AsObject();
        add.Remove("Value");
        add["ValueSource"] = 0;
        add["LiteralValue"] = "42";
        add["BindingValue"] = new System.Text.Json.Nodes.JsonObject { ["Source"] = 0 };
        var wait = root["Nodes"]![1]!["Config"]!.AsObject();
        wait.Remove("CompareValue");
        wait["CompareValueSource"] = 1;
        wait["CompareLiteralValue"] = "";
        wait["CompareBinding"] = new System.Text.Json.Nodes.JsonObject { ["Source"] = 1, ["Binding"] = "Add|Count" };

        var loaded = store.Deserialize(root.ToJsonString());

        var addNode = Assert.IsType<QueueAddNodeModel>(loaded.Document.Graph.Nodes.Single(node => node.Id == "Add"));
        Assert.Equal(WorkflowValueSource.Literal, addNode.Value.Source);
        Assert.Equal("42", addNode.Value.LiteralValue);
        var waitNode = Assert.IsType<SignalValueWaitNodeModel>(loaded.Document.Graph.Nodes.Single(node => node.Id == "Wait"));
        Assert.Equal(WorkflowValueSource.Binding, waitNode.CompareValue.Source);
        Assert.Equal(new WorkflowBindingKey("Add", "Count"), waitNode.CompareValue.Binding);
        Assert.Equal(2, loaded.Migration.Warnings.Count(warning => warning.Contains("已合并为", StringComparison.Ordinal)));
    }
}
