using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPublicDataPersistenceTests
{
    [Fact]
    public void RoundTripsPublicDataManifest()
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
        Assert.Empty(loaded.Warnings);
        Assert.Equal(document.PublicData, loaded.Document.PublicData);
        Assert.Equal(json, store.Serialize(loaded.Document));
    }

    private static WorkflowDocument BuildDocument()
    {
        var document = new WorkflowDocument { Name = "公共数据", EntryNodeId = "Init" };
        var canvas = document;
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
