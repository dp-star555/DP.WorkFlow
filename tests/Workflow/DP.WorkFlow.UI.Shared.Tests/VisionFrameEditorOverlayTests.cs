using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionFrameEditorOverlayTests
{
    [Fact]
    public void RegionNodes_StillEditRois_FindNodesUseTheirOwnEditor()
    {
        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var regionNodes = catalog.Snapshot().Values.Select(d => d.Factory()).OfType<AnalyzeVisionFrameNodeModel>()
            .Where(n => n.RangeCapability == EWorkflowVisionRange.Region).ToArray();
        Assert.NotEmpty(regionNodes);
        foreach (var node in regionNodes)
        {
            using var page = new VisionFrameEditorPageModel(node);
            if (node is FindVisionShapeNodeModel)
            {
                // 找线/找圆：搜索范围由专用图上编辑器编辑，不用通用ROI工具和ROI列表。
                Assert.False(page.CanEdit, node.NodeType);
                Assert.IsType<VisionFindShapeGizmo>(page.Caliper);
            }
            else
            {
                Assert.True(page.CanEdit, node.NodeType);
                Assert.True(new VisionRoiListModel(page).IsAvailable, node.NodeType);
            }
            Assert.Equal(0, page.DefaultView);
        }
    }

    [Fact]
    public void EditableOverlays_OnlyOnInputAndTestImages()
    {
        Assert.True(VisionFrameEditorPageModel.ShowsEditableOverlays(0));
        Assert.True(VisionFrameEditorPageModel.ShowsEditableOverlays(3));
        Assert.False(VisionFrameEditorPageModel.ShowsEditableOverlays(1));
        Assert.False(VisionFrameEditorPageModel.ShowsEditableOverlays(2));
        using var caliper = new VisionFrameEditorPageModel(new MeasureVisionCaliperNodeModel());
        Assert.Equal(0, caliper.DefaultView);
    }
}
