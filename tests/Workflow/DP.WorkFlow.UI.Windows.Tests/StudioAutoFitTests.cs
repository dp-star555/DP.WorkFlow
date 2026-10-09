using DP.WorkFlow.UI;
using System.Windows.Threading;

namespace DP.WorkFlow.Tests;

[Trait(TestCategories.Category, TestCategories.UiControls)]
public sealed class StudioAutoFitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Studio_InitialDisplayAndFileOpen_CenterNodesAfterLayout(bool wpf)
    {
        Run(() =>
        {
            var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
            using var workspace = new WorkflowDocumentWorkspace(catalog);
            workspace.New("Auto fit");
            var original = workspace.Navigator!.CurrentSession;
            original.SnapToGrid = false;
            original.AddNode("Delay", 1100, 80);
            var path = Path.Combine(Path.GetTempPath(), $"workflow-fit-{Guid.NewGuid():N}.json");
            workspace.SaveAs(path);
            try
            {
                if (wpf)
                {
                    var studio = new DP.WorkFlow.UI.Wpf.WorkflowStudioControl { Workspace = workspace };
                    var window = new System.Windows.Window
                    {
                        Content = studio, Width = 1400, Height = 800,
                        Left = -10000, Top = -10000, ShowInTaskbar = false
                    };
                    try
                    {
                        window.Show();
                        Pump();
                        AssertCentered(original, studio.Designer.ActualWidth, studio.Designer.ActualHeight);
                        original.SetViewport(0.8, -350, 270);
                        workspace.Open(path);
                        Pump();
                        var opened = studio.Session!;
                        Assert.NotSame(original, opened);
                        AssertCentered(opened, studio.Designer.ActualWidth, studio.Designer.ActualHeight);
                        Assert.False(workspace.IsDirty);
                        opened.SetViewport(0.9, 123, -45);
                        opened.SelectNode(opened.Canvas.Nodes[0].Node.Id);
                        window.Width += 120;
                        Pump();
                        AssertViewportUnchanged(opened);
                    }
                    finally { window.Close(); }
                }
                else
                {
                    using var studio = new DP.WorkFlow.UI.WinForms.WorkflowStudioControl
                    {
                        Workspace = workspace, Dock = System.Windows.Forms.DockStyle.Fill
                    };
                    using var window = new System.Windows.Forms.Form
                    {
                        ClientSize = new System.Drawing.Size(1400, 800),
                        StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                        Location = new System.Drawing.Point(-10000, -10000), ShowInTaskbar = false
                    };
                    window.Controls.Add(studio);
                    window.Show();
                    Pump();
                    AssertCentered(original, studio.Designer.ClientSize.Width, studio.Designer.ClientSize.Height);
                    original.SetViewport(0.8, -350, 270);
                    workspace.Open(path);
                    Pump();
                    var opened = studio.Session!;
                    Assert.NotSame(original, opened);
                    AssertCentered(opened, studio.Designer.ClientSize.Width, studio.Designer.ClientSize.Height);
                    Assert.False(workspace.IsDirty);
                    opened.SetViewport(0.9, 123, -45);
                    opened.SelectNode(opened.Canvas.Nodes[0].Node.Id);
                    window.Width += 120;
                    Pump();
                    AssertViewportUnchanged(opened);
                }
            }
            finally { File.Delete(path); }
        });
    }

    private static void AssertCentered(WorkflowDesignerSession session, double width, double height)
    {
        var nodes = session.Canvas.Nodes;
        var left = nodes.Min(n => n.X);
        var right = nodes.Max(n => n.X + n.Width);
        var top = nodes.Min(n => n.Y);
        var bottom = nodes.Max(n => n.Y + n.Height);
        Assert.Equal(width / 2, (left + right) / 2 * session.Zoom + session.PanX, 5);
        Assert.Equal(height / 2, (top + bottom) / 2 * session.Zoom + session.PanY, 5);
        Assert.True(left * session.Zoom + session.PanX >= 47);
        Assert.True(right * session.Zoom + session.PanX <= width - 47);
        Assert.Equal(80, nodes[0].X);
        Assert.Equal(80, nodes[0].Y);
        Assert.Equal(1100, nodes[1].X);
        Assert.Equal(80, nodes[1].Y);
    }

    private static void AssertViewportUnchanged(WorkflowDesignerSession session)
    {
        Assert.Equal(0.9, session.Zoom);
        Assert.Equal(123, session.PanX);
        Assert.Equal(-45, session.PanY);
    }

    private static void Pump()
    {
        System.Windows.Forms.Application.DoEvents();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        System.Windows.Forms.Application.DoEvents();
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
}
