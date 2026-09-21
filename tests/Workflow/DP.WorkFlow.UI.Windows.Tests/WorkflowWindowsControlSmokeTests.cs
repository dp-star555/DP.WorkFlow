using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

// 控件库用例：Studio 的 WinForms/WPF 控件能否在同一会话上创建并绑定，只在控件库源码改动时才需要重跑。
[Trait(TestCategories.Category, TestCategories.UiControls)]
public sealed class WorkflowWindowsControlSmokeTests
{
    [Fact]
    public void WinFormsAndWpfStudios_CreateAndBindSameSessionOnStaThread()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var canvasDocument = new WorkflowDocument { Name = "Smoke" };
                var canvas = canvasDocument.CanvasProjection;
                var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
                var session = new WorkflowDesignerSession(canvasDocument, catalog);
                var start = session.AddNode("Start", 20, 20);

                using var winForms = new DP.WorkFlow.UI.WinForms.WorkflowStudioControl
                {
                    Session = session,
                    EntryNodeId = start.Node.Id
                };
                var wpf = new DP.WorkFlow.UI.Wpf.WorkflowStudioControl
                {
                    Session = session,
                    EntryNodeId = start.Node.Id
                };

                Assert.Same(session, winForms.Designer.Session);
                Assert.Same(session, wpf.Designer.Session);
                Assert.Same(canvas, wpf.Session!.Canvas);

                using var overviewCanvas = new DP.WorkFlow.UI.WinForms.WorkflowDesignerControl
                {
                    Session = session,
                    Size = new System.Drawing.Size(640, 420)
                };
                overviewCanvas.CreateControl();
                Assert.False(overviewCanvas.IsOverviewMapVisible);
                _ = session.AddNode("Delay", 2400, 1600);
                Assert.True(overviewCanvas.IsOverviewMapVisible);
                using (var bitmap = new System.Drawing.Bitmap(overviewCanvas.Width, overviewCanvas.Height))
                    overviewCanvas.DrawToBitmap(bitmap, overviewCanvas.ClientRectangle);

                var script = session.AddNode("CSharpScript", 160, 20);
                var winModel = new WorkflowNodeEditorModel(session, start.Node.Id, script.Node.Id);
                using var winDialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(winModel);
                winDialog.CreateControl();
                winDialog.PerformLayout();
                var wpfModel = new WorkflowNodeEditorModel(session, start.Node.Id, script.Node.Id);
                var wpfDialog = new DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow(wpfModel);
                wpfDialog.Measure(new System.Windows.Size(1140, 800));
                wpfDialog.Arrange(new System.Windows.Rect(0, 0, 1140, 800));
                wpfDialog.Close();

                var visionNode = new AnalyzeVisionBlobsNodeModel { Id = "Vision1", Title = "Blob" };
                var visionData = new DP.WorkFlow.Vision.UI.VisionFrameEditorPageModel(visionNode);
                var visionPage = new WorkflowNodeEditorPageDescriptor(
                    "Image", "图像与 ROI", WorkflowNodeEditorPageKind.Custom, 450,
                    visionData,
                    RendererKey: DP.WorkFlow.Vision.UI.VisionFrameEditorPageProvider.RendererKey);
                var winVisionRenderer = new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer();
                using var winVision = winVisionRenderer.CreateControl(visionPage);
                var wpfVisionRenderer = new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer();
                var wpfVision = wpfVisionRenderer.CreateElement(visionPage);
                Assert.Equal(DP.WorkFlow.Vision.UI.VisionFrameEditorPageProvider.RendererKey, winVisionRenderer.RendererKey);
                Assert.Equal(DP.WorkFlow.Vision.UI.VisionFrameEditorPageProvider.RendererKey, wpfVisionRenderer.RendererKey);
                Assert.NotNull(wpfVision);
                visionData.Dispose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI smoke test thread timed out.");
        Assert.Null(failure);
    }
}
