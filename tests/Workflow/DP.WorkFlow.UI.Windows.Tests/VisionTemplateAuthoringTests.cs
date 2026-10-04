using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.Vision.UI;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionTemplateAuthoringTests
{
    [Fact]
    public async Task RoiTemplate_DefaultSelfTest_DoesNotSearchTheWholeSample()
    {
        using var fixture = new Fixture();
        var pixels = Enumerable.Range(0, 64 * 64).Select(i => ((i * 37 + i / 64 * 17) % 256).ToString());
        File.WriteAllText(fixture.SamplePath, "P2\n64 64\n255\n" + string.Join(" ", pixels));
        var node = new LocateVisionTemplatePoseNodeModel { MaximumWork = 2000 };
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        await page.Draft.ReadSourceAsync(fixture.SamplePath);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(24, 24), 16, 16)) }));
        await page.Draft.BuildAsync();
        await page.TestAsync();
        Assert.True(page.Draft.TrialResult!.Found);
        page.TestSource = EVisionTemplateTestSource.Sample;
        Assert.Contains("预算", page.TestBlockReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateWorkspace_KeepsPropertiesBesideCanvas_AndBuildStateVisible(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture();
                using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(),
                    reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
                page.Draft.ReadSourceAsync(fixture.SamplePath).GetAwaiter().GetResult();
                page.Draft.BuildAsync().GetAwaiter().GetResult();
                page.TestAsync().GetAwaiter().GetResult();
                var descriptor = new WorkflowNodeEditorPageDescriptor("Template", "模板制作", WorkflowNodeEditorPageKind.Custom, 0, page);
                if (wpf)
                {
                    var root = new DP.WorkFlow.Vision.UI.Wpf.VisionTemplateAuthoringRenderer().CreateElement(descriptor);
                    var layout = Assert.IsAssignableFrom<System.Windows.Controls.Grid>(root);
                    Assert.Equal(3, layout.ColumnDefinitions.Count);
                    var action = WpfDescendants(root).OfType<System.Windows.Controls.Button>().Single(c => Equals(c.Content, "新建空白模板"));
                    Assert.Contains(WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>(), c => c.Name == "TemplateBuildState");
                    root.Measure(new System.Windows.Size(1040, 680)); root.Arrange(new System.Windows.Rect(0, 0, 1040, 680)); root.UpdateLayout();
                    Assert.Contains(WpfAncestors(action), c => c is System.Windows.Controls.Expander);
                    Assert.Contains(WpfDescendants(root).OfType<System.Windows.Controls.TextBlock>(), c => c.Name == "TemplateTestState" && c.Text.Contains("已找到目标"));
                    var canvas = WpfDescendants(root).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                    var frame = WpfDescendants(root).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                    frame.GetType().GetMethod("RefreshPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(frame, null);
                    CaptureWpf(root, "template-workspace-wpf.png");
                }
                else
                {
                    using var root = new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer().CreateControl(descriptor);
                    var split = Assert.Single(FormsDescendants(root).OfType<System.Windows.Forms.SplitContainer>(), s => s.FixedPanel == System.Windows.Forms.FixedPanel.Panel1);
                    Assert.Equal(System.Windows.Forms.FixedPanel.Panel1, split.FixedPanel);
                    Assert.Contains(FormsDescendants(split.Panel1), c => c.GetType().Name == "ModernPropertyGrid");
                    var action = FormsDescendants(root).Single(c => c.Text == "新建空白模板");
                    Assert.Contains(FormsAncestors(action), c => c.GetType().Name == "PropertyRowPanel");
                    Assert.Contains(FormsDescendants(root).OfType<System.Windows.Forms.Label>(), c => c.Name == "TemplateBuildState");
                    using var host = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(1040, 680), ShowInTaskbar = false,
                        StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
                    root.Dock = System.Windows.Forms.DockStyle.Fill; host.Controls.Add(root); host.Show(); System.Windows.Forms.Application.DoEvents();
                    Assert.InRange(split.SplitterDistance, 300, 420);
                    Assert.True(split.Panel2.Width > split.Panel1.Width);
                    Assert.Contains(FormsDescendants(root).OfType<System.Windows.Forms.Label>(), c => c.Name == "TemplateTestState" && c.Text.Contains("已找到目标"));
                    WorkflowPropertyPanelIntegrationTests.Capture(root, "template-workspace-winforms.png");
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    private static void CaptureWpf(System.Windows.FrameworkElement root, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("DP_WORKFLOW_PROPERTY_CAPTURE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pending = new System.Windows.Threading.DispatcherFrame();
        var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        settle.Tick += (_, _) => { settle.Stop(); pending.Continue = false; }; settle.Start();
        System.Windows.Threading.Dispatcher.PushFrame(pending); root.UpdateLayout(); bitmap.Render(root);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, fileName)); png.Save(stream);
    }

    [Fact]
    public async Task TemplatePropertyActions_CreateBuildTestAndClearTheDraft()
    {
        using var fixture = new Fixture();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(),
            reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        var entries = page.Properties(command => page.ExecuteAsync(command,
            command == EVisionTemplateAuthoringCommand.ReadSample ? fixture.SamplePath : null));
        WorkflowPropertyEntry Action(EVisionTemplateAuthoringCommand command) => entries.Single(e => e.Name == "Command." + command);
        Assert.NotEmpty(Action(EVisionTemplateAuthoringCommand.Build).ActionBlockReason);
        await Action(EVisionTemplateAuthoringCommand.ReadSample).ExecuteActionAsync();
        Assert.Empty(Action(EVisionTemplateAuthoringCommand.Build).ActionBlockReason);
        await Action(EVisionTemplateAuthoringCommand.Build).ExecuteActionAsync();
        Assert.True(page.Draft.IsBuilt);
        await Action(EVisionTemplateAuthoringCommand.Test).ExecuteActionAsync();
        Assert.True(page.Draft.TrialResult!.Found);
        page.IsOperating = true;
        await Assert.ThrowsAsync<InvalidOperationException>(Action(EVisionTemplateAuthoringCommand.New).ExecuteActionAsync);
        Assert.True(page.Draft.IsBuilt); page.IsOperating = false;
        await Action(EVisionTemplateAuthoringCommand.New).ExecuteActionAsync();
        Assert.False(page.Draft.HasSample); Assert.False(page.Draft.IsBuilt); Assert.Null(page.Draft.TrialResult);
    }

    [Fact]
    public async Task TemplateReadiness_TracksEmptyBuiltEditedAndFailedDraft_AndUnmatchedTest()
    {
        using var fixture = new Fixture();
        using var page = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(new LocateVisionTemplateNodeModel(),
            reader: fixture.Reader, templates: fixture.Editing, templateEditorOnly: true));
        Assert.False(page.Draft.CanBuild); Assert.False(page.Draft.CanTest); Assert.False(page.CanCommit);
        await page.Draft.ReadSourceAsync(fixture.SamplePath); Assert.True(page.Draft.CanBuild); Assert.False(page.Draft.CanTest);
        await page.Draft.BuildAsync(); Assert.True(page.Draft.CanTest); Assert.True(page.CanCommit);
        await page.TestAsync(); Assert.True(page.Draft.TrialResult!.Found); Assert.Contains("分数", page.Draft.TestSummary);
        page.TestSource = EVisionTemplateTestSource.Input; Assert.NotEmpty(page.TestBlockReason); Assert.Null(page.Draft.TrialResult);
        page.TestSource = EVisionTemplateTestSource.Sample;
        page.Draft.ReportFailure(new InvalidOperationException("明确的测试失败原因"));
        using var preview = page.Frame.Capture(4); Assert.Equal("明确的测试失败原因", page.Draft.Failure);
        page.Draft.OriginX += 1; Assert.False(page.Draft.CanTest); Assert.False(page.CanCommit); Assert.Empty(page.Draft.Failure);
        await page.Draft.BuildAsync(); Assert.True(page.CanCommit);
        page.IsOperating = true; Assert.False(page.CanCommit); page.IsOperating = false;
        var score = page.Properties().Single(p => p.Name == "Score"); score.SetValue(1d);
        using var zero = VisionImage.CopyFrom(new ImageInfo(4, 4, EPixelLayout.Gray8), new byte[16]); using var frame = new ImageFrame("unmatched", zero);
        await page.Draft.TryMatchAsync(frame, new PixelBounds(0, 0, 4, 4), new TemplatePoseOptions(new[] { 0d }, new[] { 1d }, 1d));
        Assert.False(page.Draft.TrialResult!.Found); Assert.Equal("未找到目标", page.Draft.TestState); Assert.True(page.CanCommit);
        page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("exclude-all", new RectangleGeometry(new PointD(2, 2), 4, 4), ERoiPurpose.Exclude) }));
        Assert.False(page.Draft.CanBuild); Assert.Contains("为空", page.Draft.BuildBlockReason);
    }

    [Fact]
    public async Task LoadedTemplate_CanBeTestedWithoutRebuilding()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel();
        using (var maker = new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing))
        { await maker.Template!.ReadSourceAsync(fixture.SamplePath); await maker.Template.BuildAsync(); maker.PrepareCommit(); }
        using var mounted = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader,
            templates: fixture.Editing, templateEditorOnly: true));
        await mounted.OpenAsync();
        Assert.True(mounted.Draft.IsBuilt);
        using var image = await fixture.Reader.ReadAsync(fixture.SamplePath);
        using var frame = new ImageFrame("trial", image);
        await mounted.Draft.TryMatchAsync(frame, new PixelBounds(0, 0, 4, 4), new TemplatePoseOptions(new[] { 0d }, new[] { 1d }, 0.1));
        Assert.True(mounted.Draft.TrialResult!.Found);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MountedTemplateEditor_IsIsolatedFromParent_AndPreservesSearchRegion(bool pose)
    {
        using var fixture = new Fixture();
        var node = pose ? (AnalyzeVisionFrameNodeModel)new LocateVisionTemplatePoseNodeModel { Id = "locate" }
            : new LocateVisionTemplateNodeModel { Id = "locate" };
        node.FullImage = false; node.X = 1; node.Y = 1; node.Width = 2; node.Height = 2;
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new() { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes());
        var providers = new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) };
        await using var parent = new WorkflowNodeEditorModel(session, node.Id, node.Id, providers);
        Assert.Null(parent.Pages.Select(p => p.Model).OfType<VisionFrameEditorPageModel>().Single().Template);
        Assert.DoesNotContain(parent.Pages, p => p.PropertyEditorKey != null);
        using var inspector = new WorkflowPropertyInspectorModel(parent.EditingSession, node.Id);
        var entry = Assert.Single(inspector.Entries, e => e.EditorKey == WorkflowPropertyEditorKeys.VisionTemplateEditor);
        Assert.Equal(WorkflowPropertyEditorKind.Action, entry.EditorKind);
        Assert.Throws<InvalidOperationException>(() => inspector.SetValue(entry, "cannot assign"));
        var original = ((IWorkflowVisionTemplateNode)node).TemplateResourceId;
        await using (var cancelled = parent.CreatePropertyEditor(entry.EditorKey!))
        {
            var draft = cancelled.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
            await draft.OpenAsync(); Assert.Null(draft.Frame.Capture(4));
            await draft.Draft.ReadSourceAsync(fixture.SamplePath); await draft.Draft.BuildAsync();
        }
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
        Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, ((IWorkflowVisionTemplateNode)parent.EditingNode).TemplateSource);
        await using (var accepted = parent.CreatePropertyEditor(entry.EditorKey!))
        {
            var page = accepted.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
            await page.Draft.ReadSourceAsync(fixture.SamplePath);
            page.Draft.Editor.Load(new RoiDocument(new[] { new RoiDefinition("template", new RectangleGeometry(new PointD(2, 2), 2, 2)) }));
            await page.Draft.BuildAsync(); accepted.ApplyChanges();
        }
        Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, ((IWorkflowVisionTemplateNode)node).TemplateSource);
        Assert.Equal(EWorkflowVisionTemplateSource.Resource, ((IWorkflowVisionTemplateNode)parent.EditingNode).TemplateSource);
        parent.ApplyChanges();
        Assert.Equal(EWorkflowVisionTemplateSource.Resource, ((IWorkflowVisionTemplateNode)node).TemplateSource);
        Assert.Equal(original, ((IWorkflowVisionTemplateNode)node).TemplateResourceId);
        Assert.False(node.FullImage); Assert.Equal(1, node.X); Assert.Equal(2, node.Width);
        Assert.True(session.Undo()); Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, ((IWorkflowVisionTemplateNode)node).TemplateSource);
        Assert.True(session.Redo());
    }

    [Fact]
    public async Task MountedTemplateEditor_LoadsCurrentTemplate_ListsVersions_AndCanStartBlank()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel { Id = "locate" };
        using (var first = new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing))
        {
            await first.Template!.ReadSourceAsync(fixture.SamplePath); await first.Template.BuildAsync(); first.PrepareCommit();
        }
        var reference = node.TemplateResourcePath;
        using var mounted = new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(node, reader: fixture.Reader,
            templates: fixture.Editing, templateEditorOnly: true));
        await mounted.OpenAsync(); using var sample = mounted.Frame.Capture(4);
        Assert.NotNull(sample); Assert.Single(mounted.Draft.Resources);
        Assert.Equal(reference, mounted.Draft.Resources[0].Reference);
        mounted.PrepareCommit(); Assert.Equal(reference, node.TemplateResourcePath);
        var damaged = Path.Combine(fixture.Root, "Resources", "Templates", Guid.NewGuid().ToString("N"), "revisions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(damaged); File.WriteAllText(Path.Combine(damaged, "manifest.json"), "broken");
        await mounted.Draft.RefreshResourcesAsync(); Assert.Equal(2, mounted.Draft.Resources.Count);
        Assert.Contains(mounted.Draft.Resources, r => r.Label.Contains("损坏"));
        await Assert.ThrowsAnyAsync<Exception>(() => mounted.Draft.LoadResourceAsync(Path.Combine(damaged, "manifest.json")));
        Assert.Equal(reference, node.TemplateResourcePath);
        mounted.Draft.StartNewTemplate(); Assert.Null(mounted.Frame.Capture(4)); Assert.Empty(node.ModelAlgorithm.Settings);
        Assert.Throws<InvalidOperationException>(mounted.PrepareCommit);
        await mounted.Draft.LoadResourceAsync(reference); Assert.Equal(reference, node.TemplateResourcePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertyGridActionButton_RequestsTemplateEditor_AndDedicatedWindowBuilds(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture();
                var node = new LocateVisionTemplateNodeModel { Id = "locate" };
                var document = new WorkflowDocument { EntryNodeId = node.Id };
                document.CanvasProjection.Nodes.Add(new() { Node = node });
                var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes()) { SelectedNodeId = node.Id };
                WorkflowPropertyActionRequest? requested = null;
                var editor = new WorkflowNodeEditorModel(session, node.Id, node.Id,
                    new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) },
                    propertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
                try
                {
                    var mounted = editor.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single();
                    mounted.Draft.ReadSourceAsync(fixture.SamplePath).GetAwaiter().GetResult();
                    if (wpf)
                    {
                        var panel = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id };
                        panel.PropertyActionRequested += (_, request) => requested = request;
                        var button = WpfDescendants(panel).OfType<System.Windows.Controls.Button>().Single(b => b.Content is string text && text.Contains("制作/选择"));
                        button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        var dialog = new DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow(editor,
                            new DP.WorkFlow.UI.Wpf.IWorkflowWpfNodeEditorPageRenderer[] { new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer(), new DP.WorkFlow.Vision.UI.Wpf.VisionTemplateAuthoringRenderer() });
                        Assert.Contains("模板制作/选择", dialog.Title);
                        Assert.Contains(WpfDescendants(dialog).OfType<System.Windows.Controls.Expander>(), e => Equals(e.Header, "3. 制作参数") && e.IsExpanded);
                        var canvas = WpfDescendants(dialog).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                        var frameControl = WpfDescendants(dialog).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                        CheckBlank(canvas, frameControl, mounted.Draft);
                        dialog.Close();
                    }
                    else
                    {
                        using var host = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(500, 700), ShowInTaskbar = false,
                            StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
                        using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel { Session = session, EntryNodeId = node.Id, Dock = System.Windows.Forms.DockStyle.Fill };
                        panel.PropertyActionRequested += (_, request) => requested = request;
                        host.Controls.Add(panel); host.Show(); System.Windows.Forms.Application.DoEvents();
                        var button = FormsDescendants(panel).OfType<System.Windows.Forms.Button>().Single(b => b.Text.Contains("制作/选择"));
                        Assert.True(button.Enabled); button.PerformClick();
                        using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(editor,
                            new DP.WorkFlow.UI.WinForms.IWorkflowWinFormsNodeEditorPageRenderer[] { new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer(), new DP.WorkFlow.Vision.UI.WinForms.VisionTemplateAuthoringRenderer() });
                        Assert.Contains("模板制作/选择", dialog.Text);
                        Assert.Single(FormsDescendants(dialog).OfType<System.Windows.Forms.SplitContainer>(), s => s.FixedPanel == System.Windows.Forms.FixedPanel.Panel1);
                        var canvas = FormsDescendants(dialog).OfType<DP.Vision.UI.IVisionCanvas>().Single();
                        var frameControl = FormsDescendants(dialog).Single(c => c.GetType().Name == "VisionFrameEditorControl");
                        CheckBlank(canvas, frameControl, mounted.Draft);
                    }
                    Assert.Equal(new WorkflowPropertyActionRequest(node.Id, WorkflowPropertyEditorKeys.VisionTemplateEditor), requested);
                    Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
                }
                finally { editor.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    private static void CheckBlank(DP.Vision.UI.IVisionCanvas canvas, object control, VisionTemplateEditorModel draft)
    {
        var refresh = control.GetType().GetMethod("RefreshPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        refresh.Invoke(control, null); Assert.NotNull(canvas.DisplayedFrameId);
        draft.StartNewTemplate(); refresh.Invoke(control, null); Assert.Null(canvas.DisplayedFrameId);
    }

    private static IEnumerable<System.Windows.Forms.Control> FormsAncestors(System.Windows.Forms.Control root)
    {
        for (var p = root.Parent; p != null; p = p.Parent) yield return p;
    }
    private static IEnumerable<System.Windows.DependencyObject> WpfAncestors(System.Windows.DependencyObject root)
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(root); p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p)) yield return p;
    }
    private static IEnumerable<System.Windows.Forms.Control> FormsDescendants(System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls) { yield return child; foreach (var nested in FormsDescendants(child)) yield return nested; }
    }
    private static IEnumerable<System.Windows.DependencyObject> WpfDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        { yield return child; foreach (var nested in WpfDescendants(child)) yield return nested; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NodeEditor_AppliesResource_RoundTrips_RunsWithoutTemplateInput_AndUndoRestoresOldMode(bool pose)
    {
        using var fixture = new Fixture();
        var node = pose ? (AnalyzeVisionFrameNodeModel)new LocateVisionTemplatePoseNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") }
            : new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
        var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = fixture.SamplePath };
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var document = new WorkflowDocument { EntryNodeId = "source" };
        foreach (var n in new IWorkflowNodeModel[] { source, node }) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = n });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "source", FromPort = WorkflowPorts.Success, ToNodeId = "locate", ToPort = WorkflowPorts.Input });
        if (pose)
        {
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new MapVisionPoseCoordinateNodeModel { Id = "map", Pose = Input<TemplatePoseResult>("locate") } });
            document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "locate", FromPort = WorkflowPorts.Success, ToNodeId = "map", ToPort = WorkflowPorts.Input });
        }
        var session = new WorkflowDesignerSession(document, nodes);
        var editor = new WorkflowNodeEditorModel(session, "source", node.Id, new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) }, propertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
        try
        {
            var page = editor.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single().Frame;
            await page.Template!.ReadSourceAsync(fixture.SamplePath);
            page.Template.Editor.Load(new RoiDocument(new[] { new RoiDefinition("part", new RectangleGeometry(new PointD(2, 2), 2, 2)) }));
            page.Template.OriginX = pose ? 2 : 1; page.Template.OriginY = 1;
            await page.Template.BuildAsync();
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
            editor.ApplyChanges();
            var templateNode = (IWorkflowVisionTemplateNode)node;
            Assert.Equal(EWorkflowVisionTemplateSource.Resource, templateNode.TemplateSource);
            var relative = templateNode.ModelAlgorithm.Settings["templatePath"];
            Assert.False(Path.IsPathRooted(relative)); Assert.True(File.Exists(Path.Combine(fixture.Root, relative)));
            Assert.True(session.Undo()); Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, templateNode.TemplateSource);
            Assert.True(session.Redo()); Assert.Equal(relative, templateNode.ModelAlgorithm.Settings["templatePath"]);
            var json = new WorkflowDocumentJsonStore(nodes); document = json.Deserialize(json.Serialize(document)).Document;
            using var frames = new WorkflowVisionFrameScope(); using var bindings = new WorkflowVisionAlgorithmBindings(fixture.Runtime, frames, () => new VisionAlgorithmResourceContext(fixture.Root));
            var services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings)
                .Add<IWorkflowNodeCapabilityProvider>(bindings).Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers()); host.Configure(document, new WorkflowContext(services));
            var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
            var result = Assert.IsAssignableFrom<IVisionCoordinateResult>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "locate").Value);
            Assert.NotNull(result.CoordinateSystem); Assert.Equal(pose ? 2d : 1d, result.CoordinateSystem!.LocalToImage.Tx, 5); Assert.Equal(1d, result.CoordinateSystem.LocalToImage.Ty, 5);
            if (pose)
            {
                var point = Assert.IsType<Coordinate2D>(host.Engine.RunState.NodeOutputs.Single(o => o.NodeId == "map").Value);
                Assert.Equal(2d, point.X, 5); Assert.Equal(1d, point.Y, 5);
            }
            Assert.Equal(EVisionCoordinateUnit.ReferencePixel, result.CoordinateSystem.Definition.Unit);
        }
        finally { await editor.DisposeAsync(); }
    }

    [Fact]
    public async Task CancelOrStaleDraft_DoesNotPublish_AndDoesNotMutateFormalNode()
    {
        using var fixture = new Fixture();
        var node = new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node }); var session = new WorkflowDesignerSession(document, nodes);
        var editor = new WorkflowNodeEditorModel(session, node.Id, node.Id, new[] { new VisionFrameEditorPageProvider(reader: fixture.Reader, templates: fixture.Editing) }, propertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
        var page = editor.Pages.Select(p => p.Model).OfType<VisionTemplateAuthoringPageModel>().Single().Frame;
        await page.Template!.ReadSourceAsync(fixture.SamplePath); await page.Template.BuildAsync(); page.Template.OriginX += 1;
        Assert.Throws<InvalidOperationException>(editor.ApplyChanges);
        Assert.Equal(EWorkflowVisionTemplateSource.ImageBinding, node.TemplateSource); Assert.Empty(node.ModelAlgorithm.Settings);
        await editor.DisposeAsync(); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
    }

    [Fact]
    public async Task Rebuild_KeepsReferenceDefinition_ChangingOriginInvalidatesDownstreamBinding()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit();
        var first = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        await editor.LoadResourceAsync(node.ModelAlgorithm.Settings["templatePath"]);
        var parameter = editor.Parameters.Single(p => p.Id == "blurKernel"); editor.SetParameter(parameter, "3"); await editor.BuildAsync(); editor.PrepareCommit();
        var second = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        Assert.NotEqual(first.Manifest.RevisionId, second.Manifest.RevisionId);
        Assert.Equal(first.Manifest.Definition.CoordinateDefinition(node.CoordinateSystemId).Signature, second.Manifest.Definition.CoordinateDefinition(node.CoordinateSystemId).Signature);
        using var image = VisionTemplateSource.Decode(first.Read("source/image.bin")); using var frame = new ImageFrame("frame", image);
        var pose = new TemplatePoseTransform(4, 4, new PointD(2, 2), 0, 1);
        var oldCoordinates = first.Manifest.Definition.Locate(node.CoordinateSystemId, frame, pose, first.Identity);
        var binding = WorkflowVisionCoordinateBinding.Capture("locate", oldCoordinates);
        node.Id = "locate";
        var downstream = new AnalyzeVisionColorNodeModel { Coordinates = binding };
        binding.Validate(second.Manifest.Definition.Locate(node.CoordinateSystemId, frame, pose, second.Identity), frame);
        Assert.Empty(downstream.ValidateDocumentConfiguration(new IWorkflowNodeModel[] { node, downstream }));
        editor.OriginX += .5; await editor.BuildAsync(); editor.PrepareCommit();
        var third = VisionTemplateStore.Capture(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        Assert.Equal(2, third.Manifest.Definition.ReferenceVersion);
        Assert.Throws<InvalidOperationException>(() => binding.Validate(third.Manifest.Definition.Locate(node.CoordinateSystemId, frame, pose, third.Identity), frame));
        Assert.Single(downstream.ValidateDocumentConfiguration(new IWorkflowNodeModel[] { node, downstream }));
    }

    [Fact]
    public async Task ResourceImport_RestoresRoiHoles_AndUnchangedApplyDoesNotCreateRevision()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using (var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader))
        {
            await editor.ReadSourceAsync(fixture.SamplePath);
            editor.Editor.Load(new RoiDocument(new[] { new RoiDefinition("include", new RectangleGeometry(new PointD(2, 2), 4, 4)),
                new RoiDefinition("hole", new RectangleGeometry(new PointD(2.5, 2.5), 1, 1), ERoiPurpose.Exclude) }));
            await editor.BuildAsync(); editor.PrepareCommit();
        }
        var reference = node.ModelAlgorithm.Settings["templatePath"];
        using var imported = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await imported.LoadResourceAsync(reference);
        Assert.Equal(2, imported.Editor.Document.Rois.Count); Assert.Contains(imported.Editor.Document.Rois, r => r.Purpose == ERoiPurpose.Exclude);
        imported.PrepareCommit(); Assert.Equal(reference, node.ModelAlgorithm.Settings["templatePath"]);
        Assert.Single(Directory.GetFiles(fixture.Root, "manifest.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UnsavedRecipe_AllowsBuildButRejectsPublish()
    {
        using var fixture = new Fixture();
        var editing = new VisionTemplateEditingRuntime(fixture.Catalog, fixture.Runtime, () => new VisionAlgorithmResourceContext());
        var node = new LocateVisionTemplateNodeModel(); using var editor = new VisionTemplateEditorModel(node, editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync();
        Assert.Throws<InvalidOperationException>(editor.PrepareCommit); Assert.Empty(node.ModelAlgorithm.Settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothDesktopRenderers_ExpandTemplateSection_WithoutLoadingOrPublishing(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var fixture = new Fixture();
                var node = new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
                using var page = new VisionFrameEditorPageModel(node, reader: fixture.Reader, templates: fixture.Editing);
                var descriptor = new WorkflowNodeEditorPageDescriptor("Image", "图像", WorkflowNodeEditorPageKind.Custom, 0, page);
                if (wpf)
                {
                    var control = new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer().CreateElement(descriptor);
                    var section = System.Windows.LogicalTreeHelper.GetChildren(control).OfType<System.Windows.Controls.Expander>().Single();
                    Assert.False(section.IsExpanded); section.IsExpanded = true; Assert.True(section.IsExpanded);
                    Assert.NotNull(section.Content);
                }
                else
                {
                    using var control = new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer().CreateControl(descriptor);
                    control.CreateControl();
                    var section = control.Controls.Cast<System.Windows.Forms.Control>().Single(c => c.GetType().Name == "VisionTemplateEditorControl");
                    var toggle = section.Controls.OfType<System.Windows.Forms.Button>().Single();
                    Assert.Equal(38, section.Height); toggle.PerformClick(); Assert.Equal(320, section.Height);
                    Assert.True(section.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single().Visible);
                }
                Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources"))); Assert.Empty(node.ModelAlgorithm.Settings);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    [Fact]
    public async Task ReplacingSample_IncrementsReferenceVersion_EvenWhenSizeAndOriginMatch()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit();
        await editor.LoadResourceAsync(node.ModelAlgorithm.Settings["templatePath"]);
        File.WriteAllText(fixture.SamplePath, "P2\n4 4\n255\n10 25 91 15\n20 180 75 55\n220 40 135 45\n15 60 20 80\n");
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit();
        var manifest = VisionTemplateStore.Inspect(Path.Combine(fixture.Root, node.ModelAlgorithm.Settings["templatePath"]));
        Assert.Equal(2, manifest.Definition.ReferenceVersion);
    }

    [Fact]
    public async Task TrialMatch_UsesDraftModel_AndDoesNotPublishOrChangeRuntimeConfiguration()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel();
        using var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync();
        using var image = await fixture.Reader.ReadAsync(fixture.SamplePath); using var frame = new ImageFrame("trial", image);
        await editor.TryMatchAsync(frame, new PixelBounds(0, 0, 4, 4), new TemplatePoseOptions(new[] { 0d }, new[] { 1d }, .99));
        Assert.True(editor.TrialResult!.Found); Assert.NotNull(editor.TrialResult.CoordinateSystem);
        Assert.Empty(node.ModelAlgorithm.Settings); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
        using var canvas = editor.Capture(true); Assert.Equal("trial", canvas!.FrameId);
    }

    [Fact]
    public async Task LateBuild_DoesNotOverwriteChangedDraft_OrAllowApply()
    {
        using var fixture = new Fixture(); var gate = new DelayedModule();
        var catalog = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule(), gate });
        using var runtime = new VisionAlgorithmRuntime(catalog);
        var editing = new VisionTemplateEditingRuntime(catalog, runtime, () => new VisionAlgorithmResourceContext(fixture.Root));
        var node = new LocateVisionTemplateNodeModel { ModelAlgorithm = new() { ImplementationId = "test.delayed-model" } };
        using var editor = new VisionTemplateEditorModel(node, editing, () => throw new InvalidOperationException(), fixture.Reader);
        await editor.ReadSourceAsync(fixture.SamplePath);
        var task = editor.BuildAsync(); await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Throws<InvalidOperationException>(editor.PrepareCommit);
        editor.OriginX += .5; gate.Continue.SetResult(); await task;
        Assert.False(editor.IsBuilt); Assert.Throws<InvalidOperationException>(editor.PrepareCommit);
        Assert.Empty(node.ModelAlgorithm.Settings); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Resources")));
    }

    [Fact]
    public async Task ResourceFolderCanMove_WithRecipeRelativeReference_AndReferenceMismatchStopsBeforeAcquisition()
    {
        using var fixture = new Fixture(); var node = new LocateVisionTemplateNodeModel { Id = "locate", Frame = Input<ImageFrame>("source") };
        using (var editor = new VisionTemplateEditorModel(node, fixture.Editing, () => throw new InvalidOperationException(), fixture.Reader))
        { await editor.ReadSourceAsync(fixture.SamplePath); await editor.BuildAsync(); editor.PrepareCommit(); }
        var reference = node.ModelAlgorithm.Settings["templatePath"];
        var moved = Path.Combine(fixture.Root, "moved"); Directory.CreateDirectory(moved);
        foreach (var path in Directory.GetFiles(Path.Combine(fixture.Root, "Resources"), "*", SearchOption.AllDirectories))
        { var target = Path.Combine(moved, Path.GetRelativePath(fixture.Root, path)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target); }
        using (var plan = await fixture.Runtime.PrepareAsync(new[] { new VisionAlgorithmRequest("moved", typeof(IPreparedVisionTemplateMatcher), node.ModelAlgorithm) }, new VisionAlgorithmResourceContext(moved), CancellationToken.None))
            Assert.NotNull(plan.Invoke<IPreparedVisionTemplateMatcher, object>("moved", m => m));
        node.TemplateReferenceDefinition!.OriginX += .5;
        var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = fixture.SamplePath };
        var document = new WorkflowDocument { EntryNodeId = "source" };
        foreach (var n in new IWorkflowNodeModel[] { source, node }) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = n });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "source", FromPort = WorkflowPorts.Success, ToNodeId = node.Id, ToPort = WorkflowPorts.Input });
        using var frames = new WorkflowVisionFrameScope(); using var bindings = new WorkflowVisionAlgorithmBindings(fixture.Runtime, frames, () => new VisionAlgorithmResourceContext(moved));
        WorkflowVisionAlgorithmPreparationReport? report = null; bindings.PreparationChanged += value => report = value;
        var services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings)
            .Add<IWorkflowNodeCapabilityProvider>(bindings).Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(services)); await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());
        Assert.Empty(host.Engine!.RunState.NodeOutputs); Assert.Contains(report!.Issues, issue => issue.Code == "ALG_TEMPLATE_REFERENCE_MISMATCH" && issue.BindingKey.Contains("locate"));
    }

    private sealed class DelayedModule : IVisionAlgorithmModule
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ExtensionId => "test.delayed";
        public void Register(IVisionAlgorithmRegistration registrations)
        {
            registrations.Add(new VisionAlgorithmDescriptor("test.delayed-model", "Test", "1", new DelayedFactory(), new[] { "translation" }));
            registrations.Add(new VisionAlgorithmDescriptor("test.delayed-builder", "Test", "1", VisionAlgorithmFactory<IVisionTemplateBuilder>.Stateless(() => new DelayedBuilder(this))));
        }
        private sealed class DelayedFactory : IVisionAlgorithmFactory, IVisionTemplateFactoryDescription
        {
            public string MethodDisplayName => "Delayed";
            public string BuilderImplementationId => "test.delayed-builder";
            public IReadOnlyList<VisionAlgorithmParameter> BuildParameters => [];
            public Type ContractType => typeof(IPreparedVisionTemplateMatcher);
            public IReadOnlyList<VisionAlgorithmDependency> GetDependencies(VisionAlgorithmConfiguration configuration) => [];
            public Task<VisionAlgorithmActivation> PrepareAsync(VisionAlgorithmConfiguration configuration, IReadOnlyDictionary<string, object> dependencies, CancellationToken cancellationToken) => throw new NotSupportedException();
        }
        private sealed class DelayedBuilder(DelayedModule owner) : IVisionTemplateBuilder
        {
            public async Task<VisionTemplateBuild> BuildAsync(VisionTemplateBuildRequest request, CancellationToken token = default)
            {
                owner.Started.TrySetResult(); await owner.Continue.Task.WaitAsync(token);
                var result = await new OpenCvTemplateModelBuilder("opencv.template-model").BuildAsync(request, token);
                return new VisionTemplateBuild("test.delayed-model", result.Format, result.Definition, result.Settings, result.Files);
            }
        }
    }

    private static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(id, "$"));
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "template-authoring-" + Guid.NewGuid().ToString("N"));
        internal string SamplePath { get; }
        internal VisionAlgorithmCatalog Catalog { get; } = VisionAlgorithmCatalog.Compose(new[] { new OpenCvVisionAlgorithmModule() });
        internal VisionAlgorithmRuntime Runtime { get; }
        internal IImageFileReader Reader { get; } = new OpenCvImageFileReader();
        internal VisionTemplateEditingRuntime Editing { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(Root); SamplePath = Path.Combine(Root, "sample.pgm");
            File.WriteAllText(SamplePath, "P2\n4 4\n255\n10 25 90 15\n20 180 75 55\n220 40 135 45\n15 60 20 80\n");
            Runtime = new VisionAlgorithmRuntime(Catalog); Editing = new VisionTemplateEditingRuntime(Catalog, Runtime, () => new VisionAlgorithmResourceContext(Root));
        }
        public void Dispose() { Runtime.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}

