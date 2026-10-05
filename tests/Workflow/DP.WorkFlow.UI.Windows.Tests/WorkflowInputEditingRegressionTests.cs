using System.Reflection;
using DP.Vision;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowInputEditingRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComplexLiteralFocusLoss_DoesNotCastDisplayTextIntoImageFrame(bool wpf)
    {
        RunSta(() =>
        {
            var node = new InputNode { Id = "input" };
            var document = new WorkflowDocument { EntryNodeId = node.Id };
            document.CanvasProjection.Nodes.Add(new() { Node = node });
            var session = new WorkflowDesignerSession(document,
                new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<InputNode>())) { SelectedNodeId = node.Id };
            if (wpf)
            {
                var panel = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                try
                {
                    var text = WpfDescendants(panel).OfType<System.Windows.Controls.TextBox>()
                        .Single(control => control.Parent is System.Windows.Controls.Grid grid
                            && grid.Children.OfType<System.Windows.Controls.ComboBox>().Any());
                    text.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.UIElement.LostFocusEvent));
                    Assert.True(text.IsReadOnly);
                }
                finally { panel.Session = null; }
            }
            else
            {
                using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                var text = FormsDescendants(panel).OfType<ModernUI.WinForms.ModernInput>()
                    .Single(control => control.Parent is System.Windows.Forms.TableLayoutPanel table
                        && table.Controls.OfType<ModernUI.WinForms.ModernSelect>().Any()).InnerTextBox;
                typeof(System.Windows.Forms.Control).GetMethod("OnValidated", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(text, [EventArgs.Empty]);
                Assert.True(text.ReadOnly);
            }
            Assert.Equal(WorkflowValueSource.Literal, node.Frame.Source);
            Assert.Null(node.Frame.LiteralValue);
        });
    }

    public sealed class InputNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.InputLiteral";
        public WorkflowInput<ImageFrame> Frame { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateLiteralFocusLoss_DoesNotOverwriteNewBinding(bool wpf)
    {
        RunSta(() =>
        {
            var node = new ScalarNode { Id = "input" };
            var document = new WorkflowDocument { EntryNodeId = node.Id };
            document.CanvasProjection.Nodes.Add(new() { Node = node });
            var session = new WorkflowDesignerSession(document,
                new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<ScalarNode>())) { SelectedNodeId = node.Id };
            using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);
            var binding = WorkflowBindingKey.FromPublicData("Count");
            if (wpf)
            {
                var panel = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                try
                {
                    var text = WpfDescendants(panel).OfType<System.Windows.Controls.TextBox>().Single(control => control.Text == "42");
                    text.Text = "99";
                    inspector.SetWorkflowInput(inspector.Entries.Single(entry => entry.Name == nameof(ScalarNode.Count)), WorkflowValueSource.Binding, null, binding);
                    text.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.UIElement.LostFocusEvent));
                }
                finally { panel.Session = null; }
            }
            else
            {
                using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                var text = FormsDescendants(panel).OfType<System.Windows.Forms.TextBox>().Single(control => control.Text == "42");
                text.Text = "99";
                inspector.SetWorkflowInput(inspector.Entries.Single(entry => entry.Name == nameof(ScalarNode.Count)), WorkflowValueSource.Binding, null, binding);
                typeof(System.Windows.Forms.Control).GetMethod("OnValidated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, [EventArgs.Empty]);
            }
            Assert.Equal(WorkflowValueSource.Binding, node.Count.Source);
            Assert.Equal(binding, node.Count.Binding);
            Assert.Equal(42, node.Count.LiteralValue);
        });
    }

    public sealed class ScalarNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.ScalarLiteral";
        public WorkflowInput<int> Count { get; set; } = WorkflowInput<int>.FromLiteral(42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BindingToLiteral_ActuallyReplacesTheEditor_AndRepeatedSwitchingIsSafe(bool wpf)
    {
        RunSta(() =>
        {
            var binding = WorkflowBindingKey.FromPublicData("Frame");
            var node = new InputNode { Id = "input", Frame = WorkflowInput<ImageFrame>.FromBinding(binding) };
            var document = new WorkflowDocument { EntryNodeId = node.Id };
            document.CanvasProjection.Nodes.Add(new() { Node = node });
            var session = new WorkflowDesignerSession(document,
                new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<InputNode>())) { SelectedNodeId = node.Id };
            using var inspector = new WorkflowPropertyInspectorModel(session, node.Id);
            if (wpf)
            {
                var panel = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                try
                {
                    for (var index = 0; index < 3; index++)
                    {
                        var selector = WpfDescendants(panel).OfType<System.Windows.Controls.ComboBox>()
                            .Single(control => control.Items.Contains(WorkflowValueSource.Binding));
                        Assert.Equal(WorkflowValueSource.Binding, selector.SelectedItem);
                        selector.SelectedItem = WorkflowValueSource.Literal;
                        var text = WpfDescendants(panel).OfType<System.Windows.Controls.TextBox>()
                            .Single(control => control.Parent is System.Windows.Controls.Grid grid
                                && grid.Children.OfType<System.Windows.Controls.ComboBox>().Any());
                        Assert.True(text.IsReadOnly);
                        text.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.UIElement.LostFocusEvent));
                        Assert.Equal(WorkflowValueSource.Literal, node.Frame.Source);
                        inspector.SetWorkflowInput(inspector.Entries.Single(entry => entry.Name == nameof(InputNode.Frame)), WorkflowValueSource.Binding, null, binding);
                    }
                }
                finally { panel.Session = null; }
            }
            else
            {
                using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                for (var index = 0; index < 3; index++)
                {
                    var selector = FormsDescendants(panel).OfType<ModernUI.WinForms.ModernSelect>()
                        .Single(control => control.Items.Contains(WorkflowValueSource.Binding));
                    Assert.Equal(WorkflowValueSource.Binding, selector.SelectedItem);
                    selector.SelectedItem = WorkflowValueSource.Literal;
                    var text = FormsDescendants(panel).OfType<ModernUI.WinForms.ModernInput>()
                        .Single(control => control.Parent is System.Windows.Forms.TableLayoutPanel table
                            && table.Controls.OfType<ModernUI.WinForms.ModernSelect>().Any()).InnerTextBox;
                    Assert.True(text.ReadOnly);
                    typeof(System.Windows.Forms.Control).GetMethod("OnValidated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, [EventArgs.Empty]);
                    Assert.Equal(WorkflowValueSource.Literal, node.Frame.Source);
                    inspector.SetWorkflowInput(inspector.Entries.Single(entry => entry.Name == nameof(InputNode.Frame)), WorkflowValueSource.Binding, null, binding);
                }
            }
            Assert.Equal(WorkflowValueSource.Binding, node.Frame.Source);
            Assert.Equal(binding, node.Frame.Binding);
            Assert.Null(node.Frame.LiteralValue);
        });
    }

    private static IEnumerable<System.Windows.Forms.Control> FormsDescendants(System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls)
        { yield return child; foreach (var nested in FormsDescendants(child)) yield return nested; }
    }

    private static IEnumerable<System.Windows.DependencyObject> WpfDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        { yield return child; foreach (var nested in WpfDescendants(child)) yield return nested; }
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            var threadId = Environment.CurrentManagedThreadId;
            var casts = new List<string>();
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe = (_, args) =>
            {
                if (Environment.CurrentManagedThreadId == threadId && args.Exception is InvalidCastException
                    && args.Exception.Message.Contains("System.String", StringComparison.Ordinal))
                    casts.Add(args.Exception.ToString());
            };
            AppDomain.CurrentDomain.FirstChanceException += observe;
            try { test(); Assert.Empty(casts); }
            catch (Exception error) { failure = error; }
            finally { AppDomain.CurrentDomain.FirstChanceException -= observe; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }
}
