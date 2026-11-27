using System.Text.Json;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDocumentJsonStoreTests
{
    [Fact]
    public void SerializeAndDeserialize_CurrentDocument_PreservesNodeConfigLayoutAndPorts()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var store = new WorkflowDocumentJsonStore(catalog);
        var document = BuildDocument();

        var json = store.Serialize(document);
        var loaded = store.Deserialize(json);

        Assert.Empty(loaded.Warnings);
        Assert.Equal("持久化测试", loaded.Document.Name);
        Assert.Equal(3, loaded.Document.Nodes.Count);
        var actionItem = Assert.Single(loaded.Document.Nodes, item => item.Node.Id == "Action1");
        var action = Assert.IsType<ActionNodeModel>(actionItem.Node);
        Assert.Equal("Run", action.FunctionKey);
        Assert.Equal(240, actionItem.X);
        Assert.Equal(WorkflowPortSide.Bottom, actionItem.PortSides["Output:Success"]);
        Assert.Equal(WorkflowPorts.Success, loaded.Document.Connections[0].FromPort);
        Assert.Equal(WorkflowPortSide.Right, loaded.Document.Connections[1].ToSide);
        Assert.Equal(0.7, loaded.Document.Connections[1].LabelPosition);
        Assert.Contains("LegacyHidden", loaded.Document.Nodes[1].HiddenOutputPorts);
    }

    [Fact]
    public void SerializeAndDeserialize_ConnectionNoteAndDisabledFlag()
    {
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterStandardNodes());
        var document = BuildDocument();
        var plain = store.Serialize(document);
        Assert.DoesNotContain("\"Note\"", plain);     // 未设置时不写出。
        Assert.DoesNotContain("\"Disabled\"", plain);

        document.Connections[0].Note = "左相机";
        document.Connections[1].Enabled = false;
        var loaded = store.Deserialize(store.Serialize(document));

        Assert.Equal("左相机", loaded.Document.Connections[0].Note);
        Assert.True(loaded.Document.Connections[0].Enabled);
        Assert.Null(loaded.Document.Connections[1].Note);
        Assert.False(loaded.Document.Connections[1].Enabled);
        Assert.True(store.Deserialize(plain).Document.Connections.All(c => c.Enabled && c.Note is null));
    }

    [Fact]
    public void ConnectionValidationResults_AreNotSavedAndOldValuesAreIgnored()
    {
        // R6-c：连线的校验状态和诊断说明每次打开重新计算，不进文件；旧文件里的值读入时忽略。
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterStandardNodes());
        var document = BuildDocument();
        document.Connections[0].State = WorkflowConnectionState.Detached;
        document.Connections[0].Diagnostic = "旧诊断";

        var json = store.Serialize(document);
        Assert.DoesNotContain("\"Diagnostic\"", json);
        Assert.DoesNotContain("\"State\"", json);

        var legacy = json.Replace("\"ToPort\"", "\"State\": 1, \"Diagnostic\": \"旧诊断\", \"ToPort\"");
        var loaded = store.Deserialize(legacy).Document.Connections[0];
        Assert.Equal(WorkflowConnectionState.Active, loaded.State);
        Assert.Null(loaded.Diagnostic);
    }

    [Fact]
    public void SerializeAndDeserialize_CSharpScript_PreservesIdentityAndDllReferences()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var store = new WorkflowDocumentJsonStore(catalog);
        var script = new CSharpScriptNodeModel
        {
            Id = "Script1",
            ScriptId = "stable-script-id",
            Script = WorkflowCSharpProgramSource.DefaultSource,
            ScriptReferencePaths = new List<string> { @"lib\\Customer.Algorithm.dll" }
        };
        var canvasDocument = new WorkflowDocument { Name = "脚本持久化" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = script });
        canvasDocument.EntryNodeId = script.Id;

        var loaded = store.Deserialize(store.Serialize(canvasDocument));

        var restored = Assert.IsType<CSharpScriptNodeModel>(loaded.Document.Nodes[0].Node);
        Assert.Equal("stable-script-id", restored.ScriptId);
        Assert.Equal(script.Script, restored.Script);
        Assert.Equal(new[] { @"lib\\Customer.Algorithm.dll" }, restored.ScriptReferencePaths);
    }

    [Fact]
    public void SerializeAndDeserialize_WorkflowInputBinding_PreservesStructuredBindingKey()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var store = new WorkflowDocumentJsonStore(catalog);
        var canvasDocument = new WorkflowDocument { Name = "绑定持久化" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new DecisionNodeModel
            {
                Id = "Decision1",
                Title = "判断",
                ConditionSource = E_DecisionConditionSource.Binding,
                Condition = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Read1", "Payload.Value"))
            }
        });
        canvasDocument.EntryNodeId = "Decision1";

        var loaded = store.Deserialize(store.Serialize(canvasDocument));

        var decision = Assert.IsType<DecisionNodeModel>(loaded.Document.Nodes[0].Node);
        Assert.Equal(WorkflowValueSource.Binding, decision.Condition.Source);
        Assert.Equal("Read1|Payload.Value", decision.Condition.Binding!.Value.ToString());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void Deserialize_OtherSchemaVersion_Throws(int version)
    {
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterStandardNodes());
        var json = store.Serialize(BuildDocument()).Replace("\"SchemaVersion\": 5", $"\"SchemaVersion\": {version}");

        Assert.Throws<NotSupportedException>(() => store.Deserialize(json));
    }

    [Fact]
    public void Deserialize_WhenPluginIsMissing_PreservesUnknownNodeRawConfiguration()
    {
        const string json = """
        {
          "SchemaVersion": 5,
          "Name": "Missing Plugin",
          "EntryNodeId": "Vision1",
          "Nodes": [
            {
              "Id": "Vision1",
              "Title": "视觉",
              "NodeType": "VisionInspect",
              "NodeVersion": 1,
              "X": 10,
              "Y": 20,
              "Width": 180,
              "Height": 60,
              "Config": { "Recipe": "R1", "Threshold": 12 }
            }
          ],
          "Connections": []
        }
        """;
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog());

        var loaded = store.Deserialize(json);
        var unknown = Assert.IsType<UnknownWorkflowNodeModel>(loaded.Document.Nodes[0].Node);
        var savedAgain = store.Serialize(loaded.Document);
        using var parsed = JsonDocument.Parse(savedAgain);

        Assert.Contains(loaded.Warnings, warning => warning.Contains("Unknown", StringComparison.Ordinal));
        Assert.Equal("R1", unknown.RawConfig.GetProperty("Recipe").GetString());
        Assert.Equal("R1", parsed.RootElement.GetProperty("Nodes")[0].GetProperty("Config").GetProperty("Recipe").GetString());
    }

    [Fact]
    public void RoundTripsExplicitDocumentEntry()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var document = new WorkflowDocument { Name = "Entry", EntryNodeId = "Start1" };
        document.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new StartNodeModel { Id = "Start1", Title = "开始" },
            X = 12,
            Y = 24
        });
        var store = new WorkflowDocumentJsonStore(catalog);

        var json = store.Serialize(document);
        var loaded = store.Deserialize(json).Document;

        Assert.Contains("\"SchemaVersion\": 5", json, StringComparison.Ordinal);
        Assert.Contains("\"EntryNodeId\": \"Start1\"", json, StringComparison.Ordinal);
        Assert.Equal("Start1", loaded.EntryNodeId);
        Assert.Equal("Start1", Assert.Single(loaded.Graph.Nodes).Id);
        Assert.Equal(12, Assert.Single(loaded.Layout.Nodes).X);
    }

    private static WorkflowDocument BuildDocument()
    {
        var canvasDocument = new WorkflowDocument { Name = "持久化测试" };
        var canvas = canvasDocument;
        canvasDocument.EntryNodeId = "Start1";
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new StartNodeModel { Id = "Start1", Title = "启动" }
        });
        var actionItem = new WorkflowCanvasNode
        {
            Node = new ActionNodeModel { Id = "Action1", Title = "动作", FunctionKey = "Run" },
            X = 240
        };
        actionItem.SetPortSide(WorkflowPortDirection.Output, WorkflowPorts.Success, WorkflowPortSide.Bottom);
        actionItem.HiddenOutputPorts.Add("LegacyHidden");
        canvas.Nodes.Add(actionItem);
        canvas.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new EndNodeModel { Id = "End1", Title = "结束" },
            X = 480
        });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = "Start1",
            FromPort = WorkflowPorts.Success,
            ToNodeId = "Action1"
        });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = "Action1",
            FromPort = WorkflowPorts.Success,
            ToNodeId = "End1",
            ToSide = WorkflowPortSide.Right,
            LabelPosition = 0.7
        });
        return canvasDocument;
    }
}
