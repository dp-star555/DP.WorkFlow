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
    public void WinFormsNodeEditor_OpensScriptNodesOnTheScriptTab()
    {
        Run(() =>
        {
            var (session, startId, _, scriptId) = CreateSession();
            var model = new WorkflowNodeEditorModel(session, startId, scriptId);
            using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(model);
            dialog.CreateControl();

            var tabs = Descendants(dialog).OfType<ModernUI.WinForms.ModernTabControl>().Single();
            Assert.Equal("Script", tabs.SelectedTab?.Name);
            Assert.Equal("Results", tabs.TabPages.Cast<TabPage>().Last().Name);
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
