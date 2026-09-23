using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPropertyInspectorTests
{
    [Fact]
    public void Inspector_EditsScalarAndWorkflowInputUsingTypedCandidates()
    {
        var canvasDocument = new WorkflowDocument { Name = "Properties" };
        var canvas = canvasDocument.CanvasProjection;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var session = new WorkflowDesignerSession(canvasDocument, catalog);
        var source = session.AddNode("Decision", 0, 0);
        var consumer = session.AddNode("Decision", 240, 0);
        session.Document.EntryNodeId = source.Node.Id;
        session.Connect(source.Node.Id, WorkflowPorts.True, consumer.Node.Id);
        session.PublicDataCatalog.Register<bool>("MachineReady", "设备就绪", "设备公共数据");
        session.SelectedNodeId = consumer.Node.Id;
        using var inspector = new WorkflowPropertyInspectorModel(session, source.Node.Id);
        var title = inspector.Entries.Single(entry => entry.Name == nameof(IWorkflowNodeModel.Title));
        var condition = inspector.Entries.Single(entry => entry.Name == nameof(DecisionNodeModel.Condition));
        var originalTitle = consumer.Node.Title;

        inspector.SetValue(title, "新标题");
        var candidates = inspector.GetBindingCandidates(condition);
        var candidate = Assert.Single(
            candidates,
            item => item.SourceNodeId == source.Node.Id && item.MemberPath == "Value");
        var publicData = Assert.Single(candidates,
            item => item.SourceKind == WorkflowBindingCandidateSourceKind.PublicData
                && item.SourceNodeId == "MachineReady" && item.MemberPath == "$");
        Assert.Equal(WorkflowBindingKey.FromPublicData("MachineReady"), publicData.ToBindingKey());
        inspector.SetWorkflowInput(
            condition,
            WorkflowValueSource.Binding,
            null,
            candidate.ToBindingKey());

        var decision = Assert.IsType<DecisionNodeModel>(consumer.Node);
        Assert.Equal("新标题", decision.Title);
        Assert.Equal(WorkflowValueSource.Binding, decision.Condition.Source);
        Assert.Equal(new WorkflowBindingKey(source.Node.Id, "Value"), decision.Condition.Binding);

        Assert.True(session.Undo());
        Assert.Equal(WorkflowValueSource.Literal, decision.Condition.Source);
        Assert.True(session.Undo());
        Assert.Equal(originalTitle, decision.Title);
        Assert.True(session.Redo());
        Assert.Equal("新标题", decision.Title);
    }

    [Fact]
    public void Inspector_HidesAndShowsConditionalTreeProperties()
    {
        var catalog = new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<ConditionalNode>());
        var node = new ConditionalNode { Id = "Conditional", Title = "条件参数" };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(canvasDocument, catalog) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);

        Assert.DoesNotContain(inspector.Entries, entry => entry.Name == nameof(ConditionalNode.BindingKey));
        var mode = inspector.Entries.Single(entry => entry.Name == nameof(ConditionalNode.Mode));
        Assert.Contains("- Literal", mode.Description, StringComparison.Ordinal);
        Assert.Contains("- Binding", mode.Description, StringComparison.Ordinal);
        inspector.SetValue(mode, ConditionalMode.Binding);

        Assert.Contains(inspector.Entries, entry => entry.Name == nameof(ConditionalNode.BindingKey));
        var items = inspector.Entries.Single(entry => entry.Name == nameof(ConditionalNode.Items));
        inspector.SetStructuredJson(items, "[1, 2, 3]");
        Assert.Equal(new[] { 1, 2, 3 }, node.Items);
        Assert.True(WorkflowCollectionTableModel.TryCreate(items, out var table));
        inspector.ApplyCollectionTable(table!, new[]
        {
            (IReadOnlyList<string>)new[] { "4" },
            new[] { "5" }
        });
        Assert.Equal(new[] { 4, 5 }, node.Items);
    }

    [Fact]
    public void Inspector_UsesChineseConventionAndWorkflowPropertyAttributeMetadata()
    {
        var catalog = new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<ConditionalNode>());
        var node = new ConditionalNode { Id = "Metadata", Title = "元数据" };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(canvasDocument, catalog) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);

        var mode = inspector.Entries.Single(entry => entry.Name == nameof(ConditionalNode.Mode));
        Assert.Equal("输入模式", mode.DisplayName);
        Assert.Equal("数据来源", mode.Category);
        Assert.StartsWith("选择参数使用固定值还是绑定值。", mode.Description, StringComparison.Ordinal);
        Assert.Contains("- Literal", mode.Description, StringComparison.Ordinal);
        Assert.Contains("- Binding", mode.Description, StringComparison.Ordinal);
        var items = inspector.Entries.Single(entry => entry.Name == nameof(ConditionalNode.Items));
        Assert.Equal("配置项", items.DisplayName);
        Assert.Contains("批量处理", items.Description, StringComparison.Ordinal);
        var file = inspector.Entries.Single(entry => entry.Name == nameof(ConditionalNode.FilePath));
        Assert.Equal(WorkflowPropertyEditorKeys.FilePath, file.EditorKey);
        Assert.Equal("图像|*.png", file.EditorFilter);
        Assert.True(file.EditorCheckExists);
        Assert.Throws<InvalidOperationException>(() => inspector.SetValue(file, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png")));
    }

    [Fact]
    public void Inspector_MapsTypedVisionThresholdsAndRoundTripsRegions()
    {
        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var node = new AnalyzeVisionBlobsNodeModel { Id = "Blobs", Title = "连通域" };
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(document, catalog) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);
        Assert.DoesNotContain(inspector.Entries, entry => entry.Name == "ParametersJson");
        var minimum = inspector.Entries.Single(entry => entry.Name == nameof(node.MinimumGray));
        Assert.Equal("最小灰度", minimum.DisplayName);
        inspector.SetValue(minimum, 140);
        inspector.SetValue(inspector.Entries.Single(entry => entry.Name == nameof(node.MaximumGray)), 200);
        inspector.SetStructuredJson(inspector.Entries.Single(entry => entry.Name == nameof(node.Regions)),
            "[{\"Id\":\"hole\",\"CenterX\":5,\"CenterY\":5,\"Width\":2,\"Height\":2,\"Exclude\":true}]");
        var store = new WorkflowDocumentJsonStore(catalog);
        var loaded = store.Deserialize(store.Serialize(document)).Document;
        var restored = Assert.IsType<AnalyzeVisionBlobsNodeModel>(loaded.CanvasProjection.Nodes[0].Node);
        Assert.Equal(140, restored.MinimumGray);
        Assert.Equal(200, restored.MaximumGray);
        Assert.True(Assert.Single(restored.Regions).Exclude);
    }

    [Fact]
    public void Inspector_UsesKindSpecificChoiceEditorsForCaptureNodes()
    {
        // 面阵与线扫是两个强类型节点，属性面板必须靠节点自己的编辑器键区分候选来源，
        // 不能共用同一个键让操作员在两个节点上看到同一份（可能不匹配的）源列表。
        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var area = new CaptureAreaFrameNodeModel { Id = "Area", Title = "面阵采集" };
        var line = new CaptureLineScanFrameNodeModel { Id = "Line", Title = "线扫采集" };
        var document = new WorkflowDocument { EntryNodeId = area.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = area });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = line });
        var session = new WorkflowDesignerSession(document, catalog) { SelectedNodeId = area.Id };

        // 宿主按编辑器键注入候选：键必须原样传给提供者，否则面阵与线扫会拿到同一份源列表。
        var requested = new List<string>();
        WorkflowPropertyChoiceProvider provider = (editorKey, _) =>
        {
            requested.Add(editorKey);
            return editorKey is WorkflowPropertyEditorKeys.VisionAreaSource or WorkflowPropertyEditorKeys.VisionLineScanSource
                ? new[] { new WorkflowPropertyChoice("Camera.Top", new DP.Vision.Acquisition.VisionSourceReference("Camera.Top")) }
                : Array.Empty<WorkflowPropertyChoice>();
        };

        using var areaInspector = new WorkflowPropertyInspectorModel(session, area.Id, provider);
        var areaSource = areaInspector.Entries.Single(entry => entry.Name == nameof(CaptureAreaFrameNodeModel.Source));
        Assert.Equal(WorkflowPropertyEditorKeys.VisionAreaSource, areaSource.EditorKey);
        Assert.Equal(WorkflowPropertyEditorKind.Choice, areaSource.EditorKind);
        Assert.Equal("Camera.Top", Assert.Single(areaSource.Choices).Label);
        Assert.Equal("逻辑图像源", areaSource.DisplayName);
        Assert.Contains(areaInspector.Entries, entry => entry.Name == nameof(CaptureAreaFrameNodeModel.TriggerMode));

        // 属性面板跟随会话的当前选中节点解析目标，切换选中后才能拿到线扫节点的属性。
        session.SelectedNodeId = line.Id;
        using var lineInspector = new WorkflowPropertyInspectorModel(session, line.Id, provider);
        var lineSource = lineInspector.Entries.Single(entry => entry.Name == nameof(CaptureLineScanFrameNodeModel.Source));
        Assert.Equal(WorkflowPropertyEditorKeys.VisionLineScanSource, lineSource.EditorKey);
        Assert.Equal(WorkflowPropertyEditorKind.Choice, lineSource.EditorKind);
        // 线扫不预设触发模式：在确定设备需求之前加字段只会被当成可用的工艺参数。
        Assert.DoesNotContain(lineInspector.Entries, entry => entry.Name == nameof(CaptureAreaFrameNodeModel.TriggerMode));
        Assert.Contains(WorkflowPropertyEditorKeys.VisionAreaSource, requested);
        Assert.Contains(WorkflowPropertyEditorKeys.VisionLineScanSource, requested);
    }

    [Fact]
    public void ScriptEditor_ProvidesRoslynDiagnosticsAndApiCompletions()
    {
        var source = WorkflowCSharpScriptEditorModel.EnsureProgramSource("SetVariable(\"X\", 1); return GetVariable<int>(\"X\");");
        Assert.Empty(WorkflowCSharpScriptEditorModel.GetDiagnostics(source));
        Assert.NotEmpty(WorkflowCSharpScriptEditorModel.GetDiagnostics("not valid C#"));
        var caret = source.IndexOf("GetVariable<int>", StringComparison.Ordinal) + 4;
        Assert.Contains(WorkflowCSharpScriptEditorModel.GetCompletions(source, caret), item => item.StartsWith("GetVariable", StringComparison.Ordinal));
    }

    private enum ConditionalMode { Literal, Binding }

    [WorkflowNode("ConditionalPropertyTest")]
    private sealed class ConditionalNode : WorkflowNodeModel
    {
        public override string NodeType => "ConditionalPropertyTest";
        [WorkflowProperty("输入模式", "选择参数使用固定值还是绑定值。", Category = "数据来源")]
        public ConditionalMode Mode { get; set; }
        [WorkflowPropertyVisibleWhen(nameof(Mode), nameof(ConditionalMode.Binding))]
        public string BindingKey { get; set; } = string.Empty;
        [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath, Filter = "图像|*.png", CheckExists = true)]
        public string FilePath { get; set; } = string.Empty;
        public List<int> Items { get; set; } = new();
    }
}
