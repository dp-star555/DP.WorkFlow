using System.Windows.Forms;
using DP.WorkFlow.UI;
using ModernUI.WinForms;

namespace DP.WorkFlow.Tests;

public sealed class StudioShellStyleTests
{
    [Fact]
    public void Studio_UsesModernSplittersTabsAndThemedLists()
    {
        Run(() =>
        {
            using var studio = new DP.WorkFlow.UI.WinForms.WorkflowStudioControl();
            var controls = Descendants(studio).ToArray();

            Assert.Equal(2, controls.OfType<ModernSplitter>().Count());
            Assert.DoesNotContain(controls, control => control is TabControl or TreeView && control.GetType().Namespace != typeof(ModernTheme).Namespace
                && control.Parent?.GetType().Namespace != typeof(ModernTheme).Namespace);
            Assert.All(controls.OfType<ModernListView>(), list => Assert.Same(ModernTheme.Dark, list.Theme));
            Assert.Same(ModernTheme.Dark, Assert.Single(controls.OfType<ModernTreeView>()).Theme);
        });
    }

    [Fact]
    public void Toolbar_UsesIconButtonsAndIconMenuItems()
    {
        Run(() =>
        {
            using var studio = new DP.WorkFlow.UI.WinForms.WorkflowStudioControl();
            var toolbar = Descendants(studio).OfType<ModernToolStrip>().Single();
            var buttons = toolbar.Items.OfType<ToolStripButton>().ToDictionary(item => item.Name ?? string.Empty);

            foreach (var name in new[] { "_undoButton", "_redoButton", "_upButton", "_runButton", "_pauseButton", "_resumeButton", "_stopButton" })
            {
                Assert.Equal(ToolStripItemDisplayStyle.Image, buttons[name].DisplayStyle);
                Assert.NotNull(buttons[name].Image);
                Assert.False(string.IsNullOrWhiteSpace(buttons[name].ToolTipText));
            }
            Assert.All(toolbar.Items.OfType<ToolStripDropDownButton>().SelectMany(menu => menu.DropDownItems.Cast<ToolStripItem>()),
                item => Assert.NotNull(item.Image));
        });
    }

    [Fact]
    public void Toolbox_FiltersNodesBySearchText()
    {
        Run(() =>
        {
            using var toolbox = new DP.WorkFlow.UI.WinForms.WorkflowToolboxControl
            {
                Session = new WorkflowDesignerSession(new WorkflowDocument { Name = "Toolbox" },
                    new WorkflowNodeCatalog().RegisterStandardNodes())
            };
            var tree = Descendants(toolbox).OfType<ModernTreeView>().Single();
            var search = Descendants(toolbox).OfType<ModernInput>().Single();
            var all = Leaves(tree.Nodes).Count();

            search.Text = "延时";

            var filtered = Leaves(tree.Nodes).ToArray();
            Assert.NotEmpty(filtered);
            Assert.True(filtered.Length < all);
            Assert.All(filtered, node => Assert.Contains("延时", node.Text + node.ToolTipText));
            search.Text = string.Empty;
            Assert.Equal(all, Leaves(tree.Nodes).Count());
        });
    }

    private static IEnumerable<TreeNode> Leaves(TreeNodeCollection nodes) =>
        nodes.Cast<TreeNode>().SelectMany(node => node.Tag is null ? Leaves(node.Nodes) : new[] { node });

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
}
