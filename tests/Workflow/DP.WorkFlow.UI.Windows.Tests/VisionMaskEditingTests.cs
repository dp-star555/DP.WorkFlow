using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.Vision.UI;
using DP.WorkFlow.Vision.UI;
using System.IO;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionMaskEditingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeImagePages_ExposeMaskDisplaySwitchWithoutChangingRois(bool wpf)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var node = new AnalyzeVisionColorNodeModel { Regions = new() { new() { Id = "roi", CenterX = 2, CenterY = 2, Width = 2, Height = 2 } } };
                using var model = new VisionFrameEditorPageModel(node);
                var page = new WorkflowNodeEditorPageDescriptor("Image", "图像", WorkflowNodeEditorPageKind.Custom, 450, model,
                    RendererKey: VisionFrameEditorPageProvider.RendererKey);
                if (wpf)
                {
                    var element = Assert.IsAssignableFrom<System.Windows.Controls.DockPanel>(new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer().CreateElement(page));
                    var toggle = Assert.Single(element.Children.OfType<System.Windows.Controls.ToolBarTray>().SelectMany(t => t.ToolBars).SelectMany(b => b.Items.OfType<System.Windows.Controls.Primitives.ToggleButton>()));
                    Assert.Equal("显示有效掩膜", toggle.ToolTip); toggle.IsChecked = false; Assert.False(model.ShowMask);
                    toggle.IsChecked = true; Assert.True(model.ShowMask);
                }
                else
                {
                    using var control = new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer().CreateControl(page);
                    var toggle = Assert.Single(control.Controls.OfType<ModernUI.WinForms.ModernToolStrip>().SelectMany(t => t.Items.OfType<System.Windows.Forms.ToolStripButton>()), b => b.CheckOnClick);
                    Assert.Equal("显示有效掩膜", toggle.Text); toggle.Checked = false; Assert.False(model.ShowMask);
                    toggle.Checked = true; Assert.True(model.ShowMask);
                }
                Assert.Single(node.Regions);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void TemplateMaking_ShowsExactHoleAndRejectsDisabledRoisWithoutFullImageFallback()
    {
        using var image = VisionImage.CopyFrom(new ImageInfo(4, 4, EPixelLayout.Gray8), new byte[16]);
        using var frame = new ImageFrame("sample", image);
        using var maker = new VisionTemplateEditorModel(new LocateVisionTemplatePoseNodeModel(), null, () => frame.Retain(), null);
        maker.UseInput();
        maker.Editor.Load(new RoiDocument(new[]
        {
            new RoiDefinition("include", new RectangleGeometry(new PointD(2, 2), 4, 4)),
            new RoiDefinition("hole", new RectangleGeometry(new PointD(1.5, 1.5), 1, 1), ERoiPurpose.Exclude)
        }));
        using var first = maker.Capture(false); Assert.NotNull(first);
        var region = Assert.IsType<RegionGeometry>(Assert.Single(first.Overlay!.Layers.Single(l => l.Id == "effective-mask").Visuals).Geometry);
        Assert.Equal(15, region.AreaPixels); Assert.False(region.Contains(new PointD(1.5, 1.5)));
        Assert.Contains("15像素", maker.MaskSummary);
        maker.ShowMask = false;
        using var hidden = maker.Capture(false); Assert.NotNull(hidden);
        Assert.DoesNotContain(hidden.Overlay!.Layers, l => l.Id == "effective-mask");
        Assert.Equal(2, maker.Editor.Document.Rois.Count);
        maker.ShowMask = true;
        maker.Editor.Load(new RoiDocument(new[] { new RoiDefinition("disabled", new RectangleGeometry(new PointD(2, 2), 4, 4), enabled: false) }));
        using var invalid = maker.Capture(false); Assert.NotNull(invalid);
        Assert.DoesNotContain(invalid.Overlay!.Layers, l => l.Id == "effective-mask");
        Assert.Contains("全部ROI已禁用", maker.MaskSummary);
    }

    [Fact]
    public async Task InputPreview_ShowsBoundMaskAndRefreshesAfterRoiExclusionOnSameImage()
    {
        using var file = new TemporaryFile();
        var source = new LoadVisionFileNodeModel { Id = "image", FilePath = file.Path };
        var mask = new ThresholdVisionRegionNodeModel { Id = "mask", Frame = Input<ImageFrame>("image"), MinimumGray = 255, MaximumGray = 255 };
        var consumer = new AnalyzeVisionColorNodeModel { Id = "color", Frame = Input<ImageFrame>("image"), Mask = Input<RegionAnalysisResult>("mask") };
        using var frames = new WorkflowVisionFrameScope();
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(Document(source, mask, consumer), new WorkflowContext(new WorkflowServiceProvider()
            .Add<IImageFileReader>(new Reader()).Add<IWorkflowVisionFrameScope>(frames)
            .Add<IRegionProcessor>(new OpenCvRegionProcessor()).Add<IColorAnalyzer>(new RgbColorAnalyzer())));
        Assert.True((await host.RunAsync()).Success);
        using var page = new VisionFrameEditorPageModel(consumer, frames);
        using var first = page.Capture(0); Assert.NotNull(first);
        var region = Assert.IsType<RegionGeometry>(Assert.Single(first.Overlay!.Layers.Single(l => l.Id == "effective-mask").Visuals).Geometry);
        Assert.Equal(8, region.AreaPixels); Assert.False(region.Contains(new PointD(3.5, .5)));
        Assert.Null(page.Capture(0));
        page.Editor.Load(new RoiDocument(new[] { new RoiDefinition("hole", new RectangleGeometry(new PointD(.5, .5), 1, 1), ERoiPurpose.Exclude) }));
        using var second = page.Capture(0); Assert.NotNull(second); Assert.Equal(first.FrameId, second.FrameId);
        var updated = Assert.IsType<RegionGeometry>(Assert.Single(second.Overlay!.Layers.Single(l => l.Id == "effective-mask").Visuals).Geometry);
        Assert.Equal(7, updated.AreaPixels); Assert.False(updated.Contains(new PointD(.5, .5)));
        page.ShowMask = false;
        using var hidden = page.Capture(0); Assert.NotNull(hidden);
        Assert.DoesNotContain(hidden.Overlay!.Layers, l => l.Id == "effective-mask");
        Assert.NotNull(consumer.Mask.Binding); Assert.Single(consumer.Regions);
        page.ShowMask = true;
        using var shown = page.Capture(0); Assert.NotNull(shown);
        Assert.Contains(shown.Overlay!.Layers, l => l.Id == "effective-mask");
    }

    [Fact]
    public async Task DrawnMaskNode_RoundTripsAndRunsWithoutVendorEngine_AndCanBindThroughPropertyGrid()
    {
        using var file = new TemporaryFile();
        var source = new LoadVisionFileNodeModel { Id = "image", FilePath = file.Path };
        var mask = new CreateVisionRegionNodeModel { Id = "mask", Frame = Input<ImageFrame>("image"), Regions = new()
        {
            new() { Id = "include", CenterX = 1, CenterY = 2, Width = 2, Height = 4 },
            new() { Id = "hole", CenterX = .5, CenterY = .5, Width = 1, Height = 1, Exclude = true }
        } };
        var color = new AnalyzeVisionColorNodeModel { Id = "color", Frame = Input<ImageFrame>("image"), Mask = Input<RegionAnalysisResult>("mask") };
        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var store = new WorkflowDocumentJsonStore(catalog);
        var document = store.Deserialize(store.Serialize(Document(source, mask, color))).Document;
        var edited = Assert.IsType<AnalyzeVisionColorNodeModel>(document.CanvasProjection.Nodes.Last().Node);
        using var frames = new WorkflowVisionFrameScope();
        using var host = new WorkflowRuntimeHost(catalog, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(new WorkflowServiceProvider().Add<IImageFileReader>(new Reader())
            .Add<IWorkflowVisionFrameScope>(frames).Add<IColorAnalyzer>(new RgbColorAnalyzer())));
        var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var output = Assert.IsType<RegionAnalysisResult>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "mask").Value);
        Assert.Equal(7, output.Area); Assert.False(output.Region.Contains(new PointD(.5, .5)));
        var result = Assert.IsType<ColorAnalysisResult>(host.Engine.RunState.NodeOutputs.Single(o => o.NodeId == "color").Value);
        Assert.Equal(7, result.PixelCount); Assert.Equal(255, result.Red);
        var session = new WorkflowDesignerSession(document, catalog) { SelectedNodeId = edited.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, "image");
        var entry = inspector.Entries.Single(e => e.Name == nameof(edited.Mask));
        entry.SetWorkflowInput(WorkflowValueSource.Literal, null, null); Assert.Null(edited.Mask.Binding);
        entry.SetWorkflowInput(WorkflowValueSource.Binding, null, new WorkflowBindingKey("mask", "$"));
        Assert.Equal("mask", edited.Mask.Binding?.NodeId);
    }

    [Fact]
    public async Task UnrelatedPreviewImage_RejectsBoundMaskAndDoesNotKeepOldOverlay()
    {
        using var file = new TemporaryFile();
        var source = new LoadVisionFileNodeModel { Id = "image", FilePath = file.Path };
        var mask = new CreateVisionRegionNodeModel { Id = "mask", Frame = Input<ImageFrame>("image") };
        using var frames = new WorkflowVisionFrameScope();
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(Document(source, mask), new WorkflowContext(new WorkflowServiceProvider().Add<IImageFileReader>(new Reader()).Add<IWorkflowVisionFrameScope>(frames)));
        Assert.True((await host.RunAsync()).Success);
        var consumer = new AnalyzeVisionColorNodeModel { Id = "consumer", Frame = Input<ImageFrame>("image"), Mask = Input<RegionAnalysisResult>("mask") };
        using var page = new VisionFrameEditorPageModel(consumer, frames, new Reader());
        using var first = page.Capture(0); Assert.NotNull(first);
        await page.ReadPreviewAsync(file.Path);
        using var manual = page.Capture(3); Assert.NotNull(manual); Assert.NotEqual(first.FrameId, manual.FrameId);
        Assert.DoesNotContain(manual.Overlay!.Layers, l => l.Id == "effective-mask");
        Assert.Contains("无法预览掩膜", page.Status);
    }

    internal static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new(id, "$"));
    internal static WorkflowDocument Document(params IWorkflowNodeModel[] nodes)
    {
        var document = new WorkflowDocument { EntryNodeId = nodes[0].Id };
        foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new() { Node = node });
        for (int i = 1; i < nodes.Length; i++) document.CanvasProjection.Connections.Add(new()
            { FromNodeId = nodes[i - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[i].Id, ToPort = WorkflowPorts.Input });
        return document;
    }
    internal sealed class Reader : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
            => Task.FromResult<IImageSource>(VisionImage.CopyFrom(new ImageInfo(4, 4, EPixelLayout.Gray8),
                Enumerable.Range(0, 16).Select(i => i % 4 < 2 ? (byte)255 : (byte)0).ToArray()));
    }
    internal sealed class TemporaryFile : IDisposable
    {
        internal string Path { get; } = System.IO.Path.GetTempFileName();
        public void Dispose() => File.Delete(Path);
    }
}
