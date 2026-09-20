using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodeEditorModelTests
{
    [Fact]
    public async Task ScriptNode_AutomaticallyGetsPropertiesScriptAndDiagnosticsPages()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "脚本" };
        var document = Document(node);
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes());

        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id);

        Assert.Equal(new[] { "Properties", "Script", "ScriptDiagnostics" }, model.Pages.Select(page => page.PageId));
        var script = Assert.IsType<WorkflowScriptEditorPageModel>(model.Pages.Single(page => page.PageId == "Script").Model);
        var sessionRefreshes = 0;
        model.EditingSession.Changed += (_, _) => sessionRefreshes++;
        var changedSource = WorkflowCSharpScriptEditorModel.EnsureProgramSource("return 42;");
        script.SetScript(changedSource);
        Assert.Empty(script.Compile());
        Assert.Equal(0, sessionRefreshes);
        Assert.Equal(WorkflowCSharpProgramSource.DefaultSource, node.Script);
        Assert.Equal(changedSource, ((IWorkflowScriptNode)model.EditingNode).Script);
        Assert.Empty(script.GetDiagnostics());
        model.ApplyChanges();
        Assert.Equal(changedSource, node.Script);
        Assert.True(session.Undo());
        Assert.Equal(WorkflowCSharpProgramSource.DefaultSource, node.Script);
        Assert.True(session.Redo());
        Assert.Equal(changedSource, node.Script);
    }

    [Fact]
    public async Task ScriptChanges_CannotBeAppliedBeforeManualCompilation()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "脚本" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id);
        var script = Assert.IsType<WorkflowScriptEditorPageModel>(model.Pages.Single(page => page.PageId == "Script").Model);
        script.SetScript(WorkflowCSharpScriptEditorModel.EnsureProgramSource("return 1;"));

        var exception = Assert.Throws<InvalidOperationException>(() => model.ApplyChanges());

        Assert.Contains("编译", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposingWithoutApply_DiscardsStagedParameterChanges()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "原始标题", Script = "return 1;" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var model = new WorkflowNodeEditorModel(session, node.Id, node.Id);
        model.EditingNode.Title = "临时标题";
        ((IWorkflowScriptNode)model.EditingNode).Script = "return 2;";

        await model.DisposeAsync();

        Assert.Equal("原始标题", node.Title);
        Assert.Equal("return 1;", node.Script);
    }

    [Fact]
    public async Task EmptyBlock_OpeningEditorDoesNotCreateEntryOrMutateOriginalDocument()
    {
        var block = new BlockNodeModel { Id = "Block", Title = "子流程" };
        var session = new WorkflowDesignerSession(Document(block), new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());

        await using var model = new WorkflowNodeEditorModel(session, block.Id, block.Id);

        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);
        Assert.Empty(block.SubDocument.Graph.Nodes);
        Assert.Empty(block.SubDocument.EntryNodeId);
        Assert.Empty(page.Session.Document.Graph.Nodes);
        Assert.NotSame(block.SubDocument, page.Session.Document);
    }

    [Fact]
    public async Task BlockSubDocument_IsStagedAndAppliedAsOneUndoableConfigurationTransaction()
    {
        var block = new BlockNodeModel { Id = "Block", Title = "子流程" };
        var session = new WorkflowDesignerSession(Document(block), new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());
        await using var model = new WorkflowNodeEditorModel(session, block.Id, block.Id);
        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);

        var start = page.Session.AddNode("Start", 72, 72);
        Assert.Empty(block.SubDocument.Graph.Nodes);
        Assert.Equal(start.Node.Id, page.Session.Document.EntryNodeId);

        model.ApplyChanges();
        Assert.Equal(start.Node.Id, block.SubDocument.EntryNodeId);
        Assert.Single(block.SubDocument.Graph.Nodes);
        Assert.True(session.Undo());
        Assert.Empty(block.SubDocument.Graph.Nodes);
        Assert.True(session.Redo());
        Assert.Single(block.SubDocument.Graph.Nodes);
    }

    [Fact]
    public async Task ImageCapability_UsesResolverAndDisposesFrameSource()
    {
        var node = new ImageNode { Id = "Vision", Title = "视觉" };
        var source = new FakeFrameSource();
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var model = new WorkflowNodeEditorModel(session, node.Id, node.Id, imageSourceResolver: new FakeResolver(source));
        var image = Assert.IsType<WorkflowImageEditorPageModel>(model.Pages.Single(page => page.PageId == "Image").Model);
        WorkflowImageFrame? received = null;
        image.FrameChanged += (_, frame) => received = frame;

        await image.StartAsync();
        source.Publish();
        Assert.NotNull(received);
        await model.DisposeAsync();

        Assert.True(source.Started);
        Assert.True(source.Stopped);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task HigherPriorityPluginPage_ReplacesBuiltInCapabilityPageBySlot()
    {
        var node = new ImageNode { Id = "Vision", Title = "视觉" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var provider = new SlotProvider("Vision.Roi", "Image", priority: 100);

        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id, new[] { provider });

        var image = Assert.Single(model.Pages, page => page.PageId == "Image");
        Assert.Equal("Vision.Roi", image.RendererKey);
        Assert.Equal(100, image.Priority);
    }

    [Fact]
    public void PageCatalog_RejectsDuplicateExtensionIdBeforeOpeningEditor()
    {
        var catalog = new WorkflowNodeEditorPageCatalog()
            .Register(new SlotProvider("Duplicate", "First", priority: 0));

        var error = Assert.Throws<InvalidOperationException>(() =>
            catalog.Register(new SlotProvider("Duplicate", "Second", priority: 0)));

        Assert.Contains("已经注册", error.Message);
    }

    [Fact]
    public void PageCatalog_RejectsAmbiguousPageSlotAtSamePriority()
    {
        var node = new ImageNode { Id = "Image", Title = "图像" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var context = new WorkflowNodeEditorContext(session, node.Id, node, null);
        var catalog = new WorkflowNodeEditorPageCatalog()
            .Register(new SlotProvider("First", "Image", priority: 10))
            .Register(new SlotProvider("Second", "Image", priority: 10));

        var error = Assert.Throws<InvalidOperationException>(() => catalog.CreatePages(context));

        Assert.Contains("多个扩展", error.Message);
    }

    [Fact]
    public void PageCatalog_RejectsCustomPageWithoutExplicitRendererKey()
    {
        var node = new ImageNode { Id = "Custom", Title = "扩展" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var context = new WorkflowNodeEditorContext(session, node.Id, node, null);
        var catalog = new WorkflowNodeEditorPageCatalog().Register(new MissingRendererKeyProvider());

        var error = Assert.Throws<InvalidOperationException>(() => catalog.CreatePages(context));

        Assert.Contains("RendererKey", error.Message);
    }

    [Fact]
    public async Task HostProvider_CanAppendCustomPageWithoutDesktopControlDependency()
    {
        var node = new ImageNode { Id = "Custom", Title = "扩展" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());

        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id, new[] { new CustomProvider() });

        var page = model.Pages.Single(item => item.PageId == "HostCustom");
        var custom = Assert.IsType<DemoPageModel>(page.Model);
        Assert.Equal(node.Id, custom.NodeId);
        Assert.Equal("DemoRenderer", page.RendererKey);
    }

    private static WorkflowDocument Document(IWorkflowNodeModel node)
    {
        var canvasDocument = new WorkflowDocument { Name = "Editor" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        return canvasDocument;
    }

    private sealed class ImageNode : WorkflowNodeModel, IWorkflowImageDisplayNode
    {
        public override string NodeType => "Action";
        public string ImageSourceKey => "Camera1";
    }

    private sealed class FakeResolver(FakeFrameSource source) : IWorkflowImageFrameSourceResolver
    {
        public IWorkflowImageFrameSource? Resolve(IWorkflowImageDisplayNode node) => source;
    }

    private sealed class FakeFrameSource : IWorkflowImageFrameSource
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public event EventHandler<WorkflowImageFrame>? FrameAvailable;
        public ValueTask StartAsync(CancellationToken cancellationToken) { Started = true; return ValueTask.CompletedTask; }
        public ValueTask StopAsync(CancellationToken cancellationToken) { Stopped = true; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public void Publish() => FrameAvailable?.Invoke(this, new WorkflowImageFrame(
            2, 2, 2, WorkflowImagePixelFormat.Gray8, new byte[] { 0, 1, 2, 3 }, DateTimeOffset.UtcNow, 1));
    }

    private sealed class SlotProvider(string extensionId, string slotId, int priority) : IWorkflowNodeEditorPageProvider
    {
        public string ExtensionId => extensionId;
        public bool CanProvide(WorkflowNodeEditorContext context) => true;
        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                slotId, slotId, WorkflowNodeEditorPageKind.Custom, 500, context.Node,
                RendererKey: extensionId,
                Priority: priority);
        }
    }

    private sealed class MissingRendererKeyProvider : IWorkflowNodeEditorPageProvider
    {
        public bool CanProvide(WorkflowNodeEditorContext context) => true;

        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                "MissingRenderer", "缺少 Renderer", WorkflowNodeEditorPageKind.Custom, 500, context.Node);
        }
    }

    private sealed class CustomProvider : IWorkflowNodeEditorPageProvider
    {
        public bool CanProvide(WorkflowNodeEditorContext context) => true;
        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                "HostCustom", "扩展", WorkflowNodeEditorPageKind.Custom, 500,
                new DemoPageModel(context.Node.Id),
                RendererKey: "DemoRenderer");
        }
    }

    private sealed record DemoPageModel(string NodeId);
}
