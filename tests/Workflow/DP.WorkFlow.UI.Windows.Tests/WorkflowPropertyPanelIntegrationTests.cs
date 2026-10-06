using System.Drawing;
using System.Windows.Forms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPropertyPanelIntegrationTests
{
    [Fact]
    public void DynamicNumericRanges_ConstrainNodePropertyStepping()
    {
        RunSta(() =>
        {
            var document = new WorkflowDocument { EntryNodeId = "node" };
            document.CanvasProjection.Nodes.Add(new() { Node = new InputLayoutNode { Id = "node" } });
            var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<InputLayoutNode, string>())) { SelectedNodeId = "node" };
            var value = 0;
            using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel
            {
                AdditionalProperties = _ => [WorkflowPropertyEntry.Create("Levels", "层数", "制作", "0自动", WorkflowPropertyEditorKind.Number, typeof(int), () => value,
                    v => { value = (int)v!; Assert.InRange(value, 0, 6); }).WithNumberRange(0, 6)],
                Session = session, EntryNodeId = "node"
            };
            var number = Descendants(panel).OfType<ModernUI.WinForms.ModernInputNumber>().Single(c => c.AccessibleName == "层数");
            Assert.Equal(0m, number.Minimum);
            Assert.Equal(6m, number.Maximum);
            var step = typeof(ModernUI.WinForms.ModernInputNumber).GetMethod("StepBy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            step.Invoke(number, new object[] { false }); Assert.Equal(0, value);
            step.Invoke(number, new object[] { true }); Assert.Equal(1, value);
            Assert.Equal(1m, number.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NodePropertyActions_RunFromRows_AndRejectClicksAfterChangingNode(bool wpf)
    {
        RunSta(() =>
        {
            var document = new WorkflowDocument();
            document.CanvasProjection.Nodes.Add(new() { Node = new InputLayoutNode { Id = "first" } });
            document.CanvasProjection.Nodes.Add(new() { Node = new InputLayoutNode { Id = "second" } });
            document.EntryNodeId = "first";
            var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<InputLayoutNode, string>())) { SelectedNodeId = "first" };
            var calls = new List<string>();
            IReadOnlyList<WorkflowPropertyEntry> Actions(IWorkflowNodeModel node) => [WorkflowPropertyEntry.CreateAction("ToolAction", "节点操作", "工具", "直接操作", () => "执行操作",
                () => { calls.Add(node.Id); return Task.CompletedTask; })];
            if (wpf)
            {
                var panel = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel { AdditionalProperties = Actions, Session = session, EntryNodeId = "first" };
                try
                {
                    var button = WpfDescendants(panel).OfType<System.Windows.Controls.Button>().Single(b => Equals(b.Content, "执行操作"));
                    Assert.IsType<System.Windows.Controls.Grid>(button.Parent);
                    button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    session.SelectedNodeId = "second";
                    button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
                finally { panel.Session = null; }
            }
            else
            {
                using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { AdditionalProperties = Actions, Session = session, EntryNodeId = "first" };
                var button = Descendants(panel).OfType<ModernUI.WinForms.ModernButton>().Single(b => b.Text == "执行操作");
                Assert.Equal("PropertyRowPanel", button.Parent!.GetType().Name);
                typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, [EventArgs.Empty]);
                session.SelectedNodeId = "second";
                typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, [EventArgs.Empty]);
            }
            Assert.Equal(new[] { "first" }, calls);
        });
    }
    private static IEnumerable<System.Windows.DependencyObject> WpfDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        { yield return child; foreach (var nested in WpfDescendants(child)) yield return nested; }
    }

    [Theory]
    [InlineData(360)]
    [InlineData(520)]
    public void CompoundEditors_UseRoundedInputSurfaces(int width)
    {
        RunSta(() =>
        {
            using var host = new Form { ClientSize = new Size(width * 2, 640), ShowInTaskbar = false };
            WorkflowNodeModel[] models = [new AcquireVisionImageNodeModel { Id = "file" }, new InputLayoutNode { Id = "input" }];
            foreach (var node in models)
            {
                var document = new WorkflowDocument { EntryNodeId = node.Id };
                document.CanvasProjection.Nodes.Add(new() { Node = node });
                var catalog = new WorkflowNodeCatalog().RegisterImageNodes().Register(WorkflowNodeDescriptor.Create<InputLayoutNode, string>());
                var session = new WorkflowDesignerSession(document, catalog) { SelectedNodeId = node.Id };
                var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id,
                    Bounds = new Rectangle(host.Controls.Count * width, 0, width, 640) };
                host.Controls.Add(panel);
            }
            host.Show(); Application.DoEvents();
            foreach (var panel in host.Controls.OfType<DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel>())
            {
                string property = panel.Session!.SelectedNodeId == "file" ? nameof(AcquireVisionImageNodeModel.FilePath) : nameof(InputLayoutNode.Literal);
                var row = Descendants(panel).Single(c => c.Tag is System.ComponentModel.PropertyDescriptor d && d.Name == property);
                var input = Assert.Single(Descendants(row).OfType<ModernUI.WinForms.ModernInput>());
                Assert.Equal(BorderStyle.None, input.InnerTextBox.BorderStyle);
                Assert.True(input.Width > 0 && input.Height >= 24);
                using var rendered = new Bitmap(input.Width, input.Height);
                input.DrawToBitmap(rendered, input.ClientRectangle);
                Assert.NotEqual(rendered.GetPixel(0, 0).ToArgb(), rendered.GetPixel(input.Width / 2, 0).ToArgb());
            }
        });
    }

    [Theory]
    [InlineData(360)]
    [InlineData(520)]
    public void WorkflowInputs_KeepLiteralAndBindingEditorsCenteredAndContained(int width)
    {
        RunSta(() =>
        {
            var document = new WorkflowDocument();
            var node = new InputLayoutNode { Id = "input" };
            document.CanvasProjection.Nodes.Add(new() { Node = node }); document.EntryNodeId = node.Id;
            var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<InputLayoutNode, string>()))
                { SelectedNodeId = node.Id };
            using var host = new Form { ClientSize = new Size(width, 640), ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
            using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id, Dock = DockStyle.Fill };
            host.Controls.Add(panel); host.Show(); Application.DoEvents();
            foreach (var row in Descendants(panel).Where(control => control.Tag is System.ComponentModel.PropertyDescriptor descriptor
                         && descriptor.Name is nameof(InputLayoutNode.Literal) or nameof(InputLayoutNode.Binding)))
            {
                var source = Descendants(row).OfType<ModernUI.WinForms.ModernSelect>().Single();
                var value = Descendants(row).Single(control => control is TextBox or Button);
                var sourceBounds = row.RectangleToClient(source.RectangleToScreen(source.ClientRectangle));
                var valueBounds = row.RectangleToClient(value.RectangleToScreen(value.ClientRectangle));
                Assert.True(row.ClientRectangle.Contains(sourceBounds));
                Assert.True(row.ClientRectangle.Contains(valueBounds));
                Assert.InRange(Math.Abs((sourceBounds.Top + sourceBounds.Height / 2d) - (valueBounds.Top + valueBounds.Height / 2d)), 0, 4);
            }
        });
    }

    public sealed class InputLayoutNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.InputLayout";
        public WorkflowInput<string> Literal { get; set; } = WorkflowInput<string>.FromLiteral("test");
        public WorkflowInput<string> Binding { get; set; } = WorkflowInput<string>.FromBinding(new("input", "$"));
    }

    [Theory]
    [InlineData(360)]
    [InlineData(520)]
    public void MultiplePropertyPanels_KeepFileAndFolderEditorsAlignedAndInsideRows(int width)
    {
        RunSta(() =>
        {
            using var host = new Form { ClientSize = new Size(width * 2, 640), ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
            for (int index = 0; index < 2; index++)
            {
                var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
                var document = new WorkflowDocument();
                WorkflowNodeModel node = index == 0
                    ? new AcquireVisionImageNodeModel { Id = "file", FilePath = @"C:\Data\PiProgects\WorkFlow\VisionData\demo.pgm" }
                    : new AcquireVisionImageNodeModel { SourceMode = EWorkflowVisionImageSource.Folder, RestartFolderEachRun = true, Id = "folder", FolderPath = @"C:\Data\PiProgects\WorkFlow\VisionData" };
                document.CanvasProjection.Nodes.Add(new() { Node = node }); document.EntryNodeId = node.Id;
                var session = new WorkflowDesignerSession(document, nodes) { SelectedNodeId = node.Id };
                var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id,
                    Bounds = new Rectangle(index * width, 0, width, 640) };
                host.Controls.Add(panel);
            }
            host.Show(); Application.DoEvents(); host.PerformLayout(); Application.DoEvents();
            foreach (var panel in host.Controls.OfType<DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel>())
            {
                var browse = Descendants(panel).OfType<ModernUI.WinForms.ModernButton>().Single(b => b.AccessibleName?.StartsWith("浏览图像", StringComparison.Ordinal) == true);
                var layout = Assert.IsType<TableLayoutPanel>(browse.Parent);
                var text = Assert.Single(layout.Controls.OfType<ModernUI.WinForms.ModernInput>());
                var row = Ancestors(layout).Single(c => c.GetType().Name == "PropertyRowPanel");
                var textBounds = row.RectangleToClient(text.RectangleToScreen(text.ClientRectangle));
                var buttonBounds = row.RectangleToClient(browse.RectangleToScreen(browse.ClientRectangle));
                Assert.True(row.ClientRectangle.Contains(buttonBounds), $"浏览按钮超出参数行: row={row.ClientRectangle}, button={buttonBounds}, text={textBounds}。");
                Assert.True(row.ClientRectangle.Contains(textBounds));
                Assert.True(textBounds.Right <= buttonBounds.Left);
                Assert.InRange(Math.Abs((textBounds.Top + textBounds.Height / 2d) - (buttonBounds.Top + buttonBounds.Height / 2d)), 0, 4);
            }
            if (width == 520) Capture(host, "file-and-folder.png");
        });
    }

    internal static void Capture(Control control, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("DP_WORKFLOW_PROPERTY_CAPTURE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        bitmap.Save(Path.Combine(directory, fileName), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static IEnumerable<Control> Ancestors(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent) yield return parent;
    }
    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() => { try { test(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }
}
