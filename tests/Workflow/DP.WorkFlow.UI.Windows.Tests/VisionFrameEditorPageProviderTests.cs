using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionFrameEditorPageProviderTests
{
    [Fact]
    public void CaliperPage_PreservesGizmoAndConfigurationChangeNotification()
    {
        var node = new MeasureVisionCaliperNodeModel { Id = "caliper" };
        var document = new WorkflowDocument();
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes());
        var notifications = 0;
        session.Changed += (_, _) => notifications++;
        var provider = new VisionFrameEditorPageProvider();

        var page = Assert.Single(provider.CreatePages(new WorkflowNodeEditorContext(session, "", node)));
        using var model = Assert.IsType<VisionFrameEditorPageModel>(page.Model);

        Assert.Equal("Image", page.PageId);
        Assert.Equal(VisionFrameEditorPageProvider.RendererKey, page.RendererKey);
        Assert.NotNull(model.Caliper);
        Assert.Null(model.Template);
        model.NotifyConfigurationChanged();
        Assert.Equal(1, notifications);
    }
}
