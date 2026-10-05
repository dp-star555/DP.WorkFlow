using System.Windows.Forms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class NodeEditorTabsTests
{
    [Fact]
    public void WinFormsNodeEditor_ShowsParametersAndResultsAsTabs_WithoutHeaderFields()
    {
        Run(() =>
        {
            var (session, startId, actionId, _) = CreateSession();
            var model = new WorkflowNodeEditorModel(session, startId, actionId);
            using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(model);
            dialog.CreateControl();

            var tabs = Descendants(dialog).OfType<ModernUI.WinForms.ModernTabControl>().Single();
            Assert.Equal(new[] { "参数", "运行结果" }, tabs.TabPages.Cast<TabPage>().Select(page => page.Text));
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.DoesNotContain(Descendants(dialog), control => control.Name is "nodeIdTextBox" or "titleTextBox" or "headerLayout");
            var results = tabs.TabPages[1];
            Assert.Contains(Descendants(results), control => control is ModernPropertyGrid.WinForms.ModernPropertyGrid);
        });
    }

    [Fact]
    public void WinFormsNodeEditor_KeepsScriptOutsideTheLeftTabs()
    {
        Run(() =>
        {
            var (session, startId, _, scriptId) = CreateSession();
            var model = new WorkflowNodeEditorModel(session, startId, scriptId);
            using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(model);
            dialog.CreateControl();

            var split = Descendants(dialog).OfType<SplitContainer>().First();
            var tabs = Assert.Single(Descendants(split.Panel1).OfType<ModernUI.WinForms.ModernTabControl>());
            Assert.Equal(new[] { "Properties", "Results" }, tabs.TabPages.Cast<TabPage>().Select(page => page.Name));
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.Contains(Descendants(split.Panel2), control => control is DP.WorkFlow.UI.WinForms.WorkflowCSharpScriptEditorControl);
        });
    }

    [Fact]
    public void RoiListModel_SelectsDeletesAndTogglesRoisSharedWithTheCanvas()
    {
        var node = new AnalyzeVisionColorNodeModel();
        using var frame = new DP.WorkFlow.Vision.UI.VisionFrameEditorPageModel(node);
        frame.Editor.Load(new DP.Vision.UI.RoiDocument(new[]
        {
            new DP.Vision.UI.RoiDefinition("a", new DP.Vision.RectangleGeometry(new DP.Vision.PointD(5, 5), 4, 2)),
            new DP.Vision.UI.RoiDefinition("b", new DP.Vision.EllipseGeometry(new DP.Vision.PointD(8, 8), 3, 2))
        }));
        var roi = new DP.WorkFlow.Vision.UI.VisionRoiListModel(frame);
        var changes = 0;
        roi.Changed += (_, _) => changes++;

        Assert.True(roi.IsAvailable);
        Assert.Equal(new[] { "矩形", "椭圆" }, roi.Items.Select(item => item.Shape));
        roi.Select("b");
        Assert.Equal("b", frame.Editor.SelectedId);
        roi.SetSelectedPurpose(DP.Vision.UI.ERoiPurpose.Exclude);
        roi.SetSelectedEnabled(false);
        var b = roi.Items.Single(item => item.Id == "b");
        Assert.Equal(("排除", false), (b.Purpose, b.Enabled));
        roi.Select("missing");
        Assert.Equal("b", roi.SelectedId);
        roi.DeleteSelected();
        Assert.Equal(new[] { "a" }, roi.Items.Select(item => item.Id));
        Assert.True(changes > 0);
    }

    [Fact]
    public void VisionRenderers_ContributeRoiListPanels()
    {
        Run(() =>
        {
            using var frame = new DP.WorkFlow.Vision.UI.VisionFrameEditorPageModel(new AnalyzeVisionColorNodeModel());
            var page = new WorkflowNodeEditorPageDescriptor("Image", "图像", WorkflowNodeEditorPageKind.Custom, 450, frame,
                RendererKey: DP.WorkFlow.Vision.UI.VisionFrameEditorPageProvider.RendererKey);
            var winForms = Assert.Single(new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer().CreateSidePanels(page));
            Assert.Equal(("Roi", "ROI列表"), (winForms.PanelId, winForms.Title));
            winForms.Content.Dispose();
            var wpf = Assert.Single(new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer().CreateSidePanels(page));
            Assert.Equal(("Roi", "ROI列表"), (wpf.PanelId, wpf.Title));
        });
    }

    [Fact]
    public void WpfNodeEditor_ShowsParametersAndResultsAsTabs()
    {
        Run(() =>
        {
            var (session, startId, actionId, _) = CreateSession();
            var model = new WorkflowNodeEditorModel(session, startId, actionId);
            var window = new DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow(model);
            try
            {
                var tabs = WpfDescendants(window).OfType<System.Windows.Controls.TabControl>().Single();
                Assert.Equal(new object[] { "参数", "运行结果" },
                    tabs.Items.Cast<System.Windows.Controls.TabItem>().Select(item => item.Header).ToArray());
                Assert.Contains(WpfDescendants(window), element => element is System.Windows.Controls.DataGrid);
            }
            finally { window.Close(); }
        });
    }

    private static (WorkflowDesignerSession Session, string StartId, string ActionId, string ScriptId) CreateSession()
    {
        var document = new WorkflowDocument { Name = "Tabs" };
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes());
        var start = session.AddNode("Start", 20, 20);
        var action = session.AddNode("Action", 200, 20);
        var script = session.AddNode("CSharpScript", 380, 20);
        return (session, start.Node.Id, action.Node.Id, script.Node.Id);
    }

    private static void Run(Action action)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds)));
        Assert.Null(failure);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static IEnumerable<System.Windows.DependencyObject> WpfDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var nested in WpfDescendants(child)) yield return nested;
        }
    }
}
