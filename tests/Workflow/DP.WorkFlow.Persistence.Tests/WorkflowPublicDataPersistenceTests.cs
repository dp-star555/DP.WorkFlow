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

        var json = store.Serialize(document);
        var loaded = store.Deserialize(json);

        Assert.Contains("\"Kind\": \"Queue\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ValueType\": \"Int32\"", json, StringComparison.Ordinal);
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
            Node = new SignalValueSetNodeModel { Id = "Count", Title = "计数", SignalKey = "Count", ValueType = E_SignalValueType.Int32, LiteralValue = "1" }
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
