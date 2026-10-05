using DP.Vision.Acquisition;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class UnifiedVisionAcquisitionEditingTests
{
    [Fact]
    public void ToolboxOffersOneAcquisitionEntry_AndLegacyRecipesStillDeserialize()
    {
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var session = new WorkflowDesignerSession(new WorkflowDocument(), nodes);
        var acquisition = session.GetToolboxItems().Where(item => item.Category == "5.Vision/Acquisition").ToArray();
        Assert.Equal("Vision.AcquireFrame", Assert.Single(acquisition).NodeType);
        Assert.Equal("图像获取", acquisition[0].DisplayName);
        var document = new WorkflowDocument { EntryNodeId = "file" };
        document.CanvasProjection.Nodes.Add(new() { Node = new LoadVisionFileNodeModel { Id = "file", FilePath = "preserve.png" } });
        document.CanvasProjection.Nodes.Add(new() { Node = new LoadVisionFolderNodeModel { Id = "folder", FolderPath = "preserve-folder", Loop = true } });
        document.CanvasProjection.Nodes.Add(new() { Node = new CaptureAreaFrameNodeModel { Id = "area", Source = new("Area") } });
        document.CanvasProjection.Nodes.Add(new() { Node = new CaptureLineScanFrameNodeModel { Id = "line", Source = new("Line") } });
        var store = new WorkflowDocumentJsonStore(nodes);
        var restored = store.Deserialize(store.Serialize(document)).Document.CanvasProjection.Nodes.Select(item => item.Node).ToArray();
        Assert.Equal("preserve.png", Assert.IsType<LoadVisionFileNodeModel>(restored[0]).FilePath);
        Assert.True(Assert.IsType<LoadVisionFolderNodeModel>(restored[1]).Loop);
        Assert.Equal("Area", Assert.IsType<CaptureAreaFrameNodeModel>(restored[2]).Source!.SourceId);
        Assert.Equal("Line", Assert.IsType<CaptureLineScanFrameNodeModel>(restored[3]).Source!.SourceId);
    }

    [Fact]
    public void ModeSwitchUsesTypedChineseChoices_KeepsDormantSettings_AndUndoesCameraAliasEdits()
    {
        var fixture = Create();
        using var inspector = new WorkflowPropertyInspectorModel(fixture.Session, fixture.Node.Id, fixture.Choices, fixture.Parameters);
        WorkflowPropertyEntry Entry(string name) => inspector.Entries.Single(entry => entry.Name == name);
        var mode = Entry(nameof(AcquireVisionImageNodeModel.SourceMode));
        Assert.Equal(WorkflowPropertyEditorKind.Choice, mode.EditorKind);
        Assert.Equal(new[] { "文件", "文件夹", "面阵相机", "线扫相机" }, mode.Choices.Select(choice => choice.Label));
        Assert.Contains(inspector.Entries, entry => entry.Name == nameof(AcquireVisionImageNodeModel.FilePath));
        Assert.Contains(inspector.Entries, entry => entry.Name.StartsWith("Algorithm.", StringComparison.Ordinal));
        inspector.SetValue(mode, EWorkflowVisionImageSource.Folder);
        Assert.DoesNotContain(inspector.Entries, entry => entry.Name == nameof(AcquireVisionImageNodeModel.FilePath));
        Assert.Contains(inspector.Entries, entry => entry.Name == nameof(AcquireVisionImageNodeModel.RestartFolderEachRun));
        inspector.SetValue(Entry(nameof(AcquireVisionImageNodeModel.SourceMode)), EWorkflowVisionImageSource.AreaCamera);
        Assert.DoesNotContain(inspector.Entries, entry => entry.Name.StartsWith("Algorithm.", StringComparison.Ordinal));
        Assert.DoesNotContain(inspector.Entries, entry => entry.Name == nameof(AcquireVisionImageNodeModel.FolderPath));
        var source = Entry(nameof(AcquireVisionImageNodeModel.AreaSource));
        Assert.Equal("Area", Assert.Single(source.Choices).Label);
        inspector.SetValue(source, new VisionSourceReference("Area"));
        Assert.Equal("Area", fixture.Node.Source!.SourceId);
        Assert.True(fixture.Session.Undo()); Assert.Null(fixture.Node.Source);
        Assert.True(fixture.Session.Redo()); Assert.Equal("Area", fixture.Node.Source!.SourceId);
        inspector.SetValue(Entry(nameof(AcquireVisionImageNodeModel.SourceMode)), EWorkflowVisionImageSource.LineCamera);
        Assert.Equal("Line", Assert.Single(Entry(nameof(AcquireVisionImageNodeModel.LineSource)).Choices).Label);
        Assert.DoesNotContain(inspector.Entries, entry => entry.Name == nameof(AcquireVisionImageNodeModel.TriggerMode));
        Assert.Equal("preserve.png", fixture.Node.FilePath);
        Assert.Equal("preserve-folder", fixture.Node.FolderPath);
        Assert.True(fixture.Session.Undo()); Assert.Equal(EWorkflowVisionImageSource.AreaCamera, fixture.Node.SourceMode);
    }

    [Fact]
    public void BothDesktopPanelsRebuildEditorsWhenSwitchingImageSource()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var fixture = Create();
                using var inspector = new WorkflowPropertyInspectorModel(fixture.Session, fixture.Node.Id, fixture.Choices, fixture.Parameters);
                using var form = new System.Windows.Forms.Form { ClientSize = new(520, 700), ShowInTaskbar = false,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new(-3000, -3000) };
                using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel
                    { ChoiceProvider = fixture.Choices, AdditionalProperties = fixture.Parameters, Session = fixture.Session,
                        EntryNodeId = fixture.Node.Id, Dock = System.Windows.Forms.DockStyle.Fill };
                var wpf = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel { ChoiceProvider = fixture.Choices,
                    AdditionalProperties = fixture.Parameters, Session = fixture.Session, EntryNodeId = fixture.Node.Id };
                form.Controls.Add(panel); form.Show(); System.Windows.Forms.Application.DoEvents();
                Switch(EWorkflowVisionImageSource.Folder);
                Assert.Contains(FormsDescendants(panel).OfType<ModernUI.WinForms.ModernButton>(), button => button.AccessibleName == "浏览图像文件夹");
                Assert.DoesNotContain(FormsDescendants(panel).OfType<ModernUI.WinForms.ModernButton>(), button => button.AccessibleName == "浏览图像文件");
                Assert.Contains(WpfDescendants(wpf).OfType<System.Windows.Controls.TextBlock>(), label => label.Text == "图像文件夹");
                WorkflowPropertyPanelIntegrationTests.Capture(panel, "unified-folder.png");
                Switch(EWorkflowVisionImageSource.AreaCamera);
                Assert.DoesNotContain(FormsDescendants(panel).OfType<ModernUI.WinForms.ModernButton>(), button => button.AccessibleName?.StartsWith("浏览图像") == true);
                Assert.Contains(WpfDescendants(wpf).OfType<System.Windows.Controls.TextBlock>(), label => label.Text == "逻辑图像源");
                Assert.DoesNotContain(WpfDescendants(wpf).OfType<System.Windows.Controls.TextBlock>(), label => label.Text == "图像文件夹");
                WorkflowPropertyPanelIntegrationTests.Capture(panel, "unified-camera.png");
                Switch(EWorkflowVisionImageSource.File);
                Assert.Contains(FormsDescendants(panel).OfType<ModernUI.WinForms.ModernButton>(), button => button.AccessibleName == "浏览图像文件");
                Assert.Contains(WpfDescendants(wpf).OfType<System.Windows.Controls.TextBlock>(), label => label.Text == "图像文件");
                Assert.True(new VisionFrameEditorPageProvider().CanProvide(new(fixture.Session, fixture.Node.Id, fixture.Node)));
                wpf.Session = null;
                void Switch(EWorkflowVisionImageSource mode)
                {
                    inspector.SetValue(inspector.Entries.Single(entry => entry.Name == nameof(AcquireVisionImageNodeModel.SourceMode)), mode);
                    System.Windows.Forms.Application.DoEvents();
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    private static (AcquireVisionImageNodeModel Node, WorkflowDesignerSession Session, WorkflowPropertyChoiceProvider Choices,
        Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>> Parameters) Create()
    {
        var node = new AcquireVisionImageNodeModel { Id = "image", FilePath = "preserve.png", FolderPath = "preserve-folder" };
        var document = new WorkflowDocument { EntryNodeId = node.Id }; document.CanvasProjection.Nodes.Add(new() { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterImageNodes()) { SelectedNodeId = node.Id };
        var sourceChoices = WorkflowVisionSourceChoices.CreateProvider(new WorkflowVisionSourceCatalog(
        [
            new("Area", "test", EVisionSourceSharingPolicy.ExclusiveOperation, true, kind: EVisionAcquisitionKind.AreaScan),
            new("Line", "test", EVisionSourceSharingPolicy.ExclusiveOperation, true, kind: EVisionAcquisitionKind.LineScan)
        ]));
        var algorithms = VisionAlgorithmCatalog.Compose([new OpenCvVisionAlgorithmModule()]);
        return (node, session, sourceChoices, WorkflowVisionAlgorithmProperties.CreateProvider(algorithms));
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
}
