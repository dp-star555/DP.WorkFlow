using System.Runtime.Loader;
using DP.Plugins;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class IndependentVisionNodePluginTests
{
    [Fact]
    public void RealOcrPackage_ExposesModelAndPreprocessorEditing_WithoutLoadingModel()
    {
        using var rig = new Rig(includeOnnx: true);
        var node = (AnalyzeVisionFrameNodeModel)rig.Nodes.GetOrThrow("Vision.RecognizeTextLine").Factory();
        node.Id = "ocr-edit";
        var document = rig.Document(node);
        var session = new WorkflowDesignerSession(document, rig.Nodes) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, document.EntryNodeId, null, WorkflowVisionAlgorithmProperties.CreateProvider(rig.Algorithms));
        var model = inspector.Entries.Single(e => e.Name == "Algorithm.recognizer.modelPath");
        Assert.Equal(WorkflowPropertyEditorKeys.FilePath, model.EditorKey);
        inspector.SetValue(model, "resource:Models/customer.onnx");
        var preprocessor = inspector.Entries.Single(e => e.Name == "Algorithm.recognizer.Dependency.preprocessor.ImplementationId");
        Assert.Equal("opencv.text-preprocess", preprocessor.Value);
        Assert.Contains(preprocessor.Choices, c => Equals(c.Value, "opencv.text-preprocess"));
        var selection = Assert.Single(((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots()).Selection;
        Assert.Equal("resource:Models/customer.onnx", selection.Settings["modelPath"]);
        Assert.True(session.Undo());
        Assert.False(Assert.Single(((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots()).Selection.Settings.ContainsKey("modelPath"));
        Assert.True(session.Redo());
        Assert.Equal("resource:Models/customer.onnx", Assert.Single(((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots()).Selection.Settings["modelPath"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BarcodeNodeAndEngine_AreDiscoveredFromPackages_WithoutNodeOrZxingReferences(bool masked)
    {
        using var rig = new Rig();
        Assert.DoesNotContain(GetType().Assembly.GetReferencedAssemblies(), a => a.Name is "DP.WorkFlow.Nodes.Vision.Barcode" or "DP.WorkFlow.Nodes.Vision.Ocr" or "DP.Vision.Zxing");
        var node = (AnalyzeVisionFrameNodeModel)rig.Nodes.GetOrThrow("Vision.ReadBarcode").Factory(); node.Id = "read";
        if (masked) node.Regions.Add(new WorkflowVisionRoi { Id = "all", CenterX = 80, CenterY = 80, Width = 160, Height = 160 });
        var document = rig.Document(node);
        var store = new WorkflowDocumentJsonStore(rig.Nodes); document = store.Deserialize(store.Serialize(document)).Document;
        using var host = rig.Host(document);
        var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var output = host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == node.Id).Value!;
        var fact = Assert.IsAssignableFrom<IWorkflowVisionFrameFact>(output);
        var reading = Assert.IsType<BarcodeReadResult>(output.GetType().GetProperty("Reading")!.GetValue(output));
        Assert.Equal("DP-PLUGIN-001", Assert.Single(reading.Observations).Text);
        Assert.Equal(EAlgorithmStatus.Completed, reading.Status);
        object? Member(string name) => output.GetType().GetProperty(name)!.GetValue(output);
        Assert.Equal("DP-PLUGIN-001", Member("Text")); Assert.Equal("DP-PLUGIN-001", Member("JoinedText"));
        Assert.Equal(1, Member("Count")); Assert.Equal(true, Member("CountMatched"));
        Assert.NotNull(Assert.Single(reading.Observations).Location);
        using var preview = rig.Frames.Capture(node.Id); Assert.Same(output, preview!.Facts); Assert.Equal(preview.Frame.FrameId, fact.FrameId);
        using var page = new VisionFrameEditorPageModel(node, rig.Frames);
        using var canvas = page.Capture(1); Assert.NotNull(canvas); Assert.Contains("DP-PLUGIN-001", page.Status);
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(node.GetType().Assembly));
        using var plan = await rig.Runtime.PrepareAsync(new[] { new VisionAlgorithmRequest("engine", typeof(IBarcodeReader), new() { ImplementationId = "zxing.code" }) });
        Assert.NotSame(AssemblyLoadContext.Default, plan.Invoke<IBarcodeReader, AssemblyLoadContext?>("engine", reader => AssemblyLoadContext.GetLoadContext(reader.GetType().Assembly)));
    }

    [Fact]
    public async Task BarcodeFilters_KeepRawReading_AndReportCountAgainstExpectation()
    {
        using var rig = new Rig();
        var node = (AnalyzeVisionFrameNodeModel)rig.Nodes.GetOrThrow("Vision.ReadBarcode").Factory(); node.Id = "read";
        void Set(string name, object value) => node.GetType().GetProperty(name)!.SetValue(node, value);
        Set("TextPattern", "^SN"); Set("ExpectedCount", 0);
        using var host = rig.Host(rig.Document(node));
        var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var output = host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == node.Id).Value!;
        object? Member(string name) => output.GetType().GetProperty(name)!.GetValue(output);
        // 原始读取仍保留被过滤的码；过滤后为空时“个数合格”为假，首个文本为空字符串。
        Assert.Single(Assert.IsType<BarcodeReadResult>(Member("Reading")).Observations);
        Assert.Equal(0, Member("Count")); Assert.Equal(false, Member("CountMatched")); Assert.Equal("", Member("Text"));
        Assert.Contains("文本规则", ((IWorkflowVisionFrameFact)output).Summary);
        Set("TextPattern", "[");
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("正则", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingBarcodeEngine_LeavesNodeEditable_AndFailsBeforeAnyOutput()
    {
        using var rig = new Rig(includeZxing: false);
        var node = rig.Nodes.GetOrThrow("Vision.ReadBarcode").Factory(); node.Id = "read";
        var document = rig.Document((AnalyzeVisionFrameNodeModel)node);
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(rig.Algorithms, rig.Runtime, rig.Bindings, () => new());
        var issue = Assert.Single(diagnostics.Analyze(document)); Assert.Equal("read", issue.NodeId); Assert.True(issue.BlocksRun);
        using var host = rig.Host(document);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());
        Assert.Contains("zxing.code", error.Message); Assert.Empty(host.Engine!.RunState.NodeOutputs);
    }

    [Fact]
    public async Task EmptyIncludedRegion_YieldsNotDecoded_WithoutFallingBackToFullImage()
    {
        using var rig = new Rig();
        var node = (AnalyzeVisionFrameNodeModel)rig.Nodes.GetOrThrow("Vision.ReadBarcode").Factory(); node.Id = "empty";
        node.Regions.Add(new WorkflowVisionRoi { Id = "include", CenterX = 80, CenterY = 80, Width = 160, Height = 160 });
        node.Regions.Add(new WorkflowVisionRoi { Id = "exclude", CenterX = 80, CenterY = 80, Width = 160, Height = 160, Exclude = true });
        using var host = rig.Host(rig.Document(node)); var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var output = host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == node.Id).Value!;
        var reading = Assert.IsType<BarcodeReadResult>(output.GetType().GetProperty("Reading")!.GetValue(output));
        Assert.Equal("not_decoded", reading.ReasonCode); Assert.Empty(reading.Observations);
    }

    [Theory]
    [InlineData("Vision.ReadBarcode")]
    [InlineData("Vision.RecognizeTextLine")]
    public void MissingNodePackage_RoundTripPreservesSelectedEngineSettingsAndDependencies(string type)
    {
        using var rig = new Rig();
        var node = rig.Nodes.GetOrThrow(type).Factory(); node.Id = "saved";
        var selection = Assert.Single(((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots()).Selection;
        selection.Settings["modelPath"] = "resource:Models/company.onnx";
        var store = new WorkflowDocumentJsonStore(rig.Nodes);
        var original = store.Serialize(rig.Document((AnalyzeVisionFrameNodeModel)node));
        var missing = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterImageNodes());
        var unavailable = missing.Deserialize(original).Document;
        Assert.IsType<UnknownWorkflowNodeModel>(unavailable.Graph.Nodes.Single(n => n.Id == "saved"));
        var restored = store.Deserialize(missing.Serialize(unavailable)).Document;
        var copy = Assert.Single(((IWorkflowVisionAlgorithmNode)restored.Graph.Nodes.Single(n => n.Id == "saved")).GetAlgorithmSlots()).Selection;
        Assert.Equal(selection.ImplementationId, copy.ImplementationId); Assert.Equal(selection.Settings["modelPath"], copy.Settings["modelPath"]);
        Assert.Equal(selection.Dependencies.Keys, copy.Dependencies.Keys);
    }

    [Fact]
    public async Task RealOnnxFactory_InvalidModelPassesFileInspectionButFailsResourcePreparation()
    {
        using var rig = new Rig(includeOnnx: true);
        var node = (AnalyzeVisionFrameNodeModel)rig.Nodes.GetOrThrow("Vision.RecognizeTextLine").Factory(); node.Id = "ocr-model";
        var path = Path.Combine(Path.GetTempPath(), "invalid-ocr-" + Guid.NewGuid().ToString("N") + ".onnx");
        File.WriteAllText(path, "invalid-model-bytes");
        try
        {
            Assert.Single(((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots()).Selection.Settings["modelPath"] = path;
            var document = rig.Document(node);
            using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(rig.Algorithms, rig.Runtime, rig.Bindings, () => new());
            Assert.Empty(diagnostics.Analyze(document));
            await Assert.ThrowsAsync<InvalidOperationException>(() => diagnostics.CheckAsync(document, default));
            var issue = Assert.Single(diagnostics.Analyze(document));
            Assert.Equal("ALG_INITIALIZATION_FAILED", issue.Code); Assert.Equal(node.Id, issue.NodeId);
            Assert.Contains("ppocr.recognize", issue.Message); Assert.NotNull(issue.Detail); Assert.False(issue.BlocksRun);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OcrPlugin_RecognizesRectifiedTextBox_AndRequiresOneRectangle()
    {
        using var rig = new Rig(extra: new OcrModule());
        var node = (AnalyzeVisionFrameNodeModel)rig.Nodes.GetOrThrow("Vision.RecognizeTextLine").Factory(); node.Id = "ocr";
        var selection = Assert.Single(((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots()).Selection;
        selection.ImplementationId = "test.ocr"; selection.Dependencies.Clear();
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("矩形文字框", StringComparison.Ordinal));
        Assert.False(node.SupportsRegionMask);
        using (var page = new VisionFrameEditorPageModel(node))
        {
            Assert.True(page.SupportsRegions);
            // 竖排文字框：宽度方向（文字行方向）旋转90°，校正后仍为 80×32 的水平小图。
            page.Editor.Load(new DP.Vision.UI.RoiDocument(new[] { new DP.Vision.UI.RoiDefinition("line", new RectangleGeometry(new PointD(60, 70), 80, 32, Math.PI / 2)) }));
        }
        var document = rig.Document(node);
        Assert.Single(node.Regions); Assert.Empty(node.ValidateConfiguration());
        using var host = rig.Host(document); var result = await host.RunAsync(); Assert.True(result.Success, result.Message);
        var output = host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "ocr").Value!;
        Assert.Contains("AB", ((IWorkflowVisionFrameFact)output).Summary);
        Assert.Equal((80, 32), Ocr.LastPatch);
        node.Regions.Add(new WorkflowVisionRoi { Id = "second", CenterX = 20, CenterY = 20, Width = 10, Height = 10 });
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("矩形文字框", StringComparison.Ordinal));
        node.Regions.RemoveAt(1); node.Regions[0].Exclude = true;
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("矩形文字框", StringComparison.Ordinal));
    }

    private sealed class Ocr : ITextLineRecognizer
    {
        public static (int Width, int Height) LastPatch;
        public TextLineRecognition Recognize(IImageSource frame, PixelBounds bounds, CancellationToken token)
        {
            LastPatch = (frame.Info.Width, frame.Info.Height);
            return new(bounds, "test-model", 80, 80,
                new[] { new CtcStep(1, .9f), new CtcStep(2, .8f) }, new[] { new CtcToken("A", 0, 1, .9f), new CtcToken("B", 1, 2, .8f) });
        }
        public void Dispose() { }
    }
    private sealed class OcrModule : IVisionAlgorithmModule
    { public string ExtensionId => "test.ocr"; public void Register(IVisionAlgorithmRegistration registrations) => registrations.Add(new("test.ocr", "Test", "1", VisionAlgorithmFactory<ITextLineRecognizer>.Stateless(() => new Ocr()))); }

    private sealed class Rig : IDisposable
    {
        public WorkflowNodeCatalog Nodes { get; } = new();
        public WorkflowNodeHandlerCatalog Handlers { get; } = new();
        public VisionAlgorithmCatalog Algorithms { get; }
        public VisionAlgorithmRuntime Runtime { get; }
        public WorkflowVisionAlgorithmBindings Bindings { get; }
        public WorkflowVisionFrameScope Frames { get; } = new();
        public Rig(bool includeZxing = true, IVisionAlgorithmModule? extra = null, bool includeOnnx = false)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "PluginTestRuns", Guid.NewGuid().ToString("N"));
            foreach (var package in new[] { "workflow.vision.barcode", "workflow.vision.ocr-line" }
                .Concat(includeZxing ? new[] { "dp.vision.zxing" } : Array.Empty<string>()).Concat(includeOnnx ? new[] { "dp.vision.ppocr" } : Array.Empty<string>()))
            {
                var source = Path.Combine(AppContext.BaseDirectory, "plugins", package);
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                { var target = Path.Combine(root, package, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
            }
            var session = new PluginLoadSession(); var algorithmLoader = new VisionAlgorithmModuleLoader(session);
            session.RegisterSharedAssembly(typeof(IWorkflowVisionAlgorithmNode).Assembly);
            var workflowLoader = new WorkflowPluginLoader(session); session.RegisterSharedContracts(root);
            var composition = new WorkflowRuntimePluginCatalog(Nodes, Handlers).Register(new WorkflowImageRuntimePluginModule());
            Assert.Equal(2, composition.LoadPlugins(root, workflowLoader)); Assert.Empty(workflowLoader.DiscoveryFailures); composition.Freeze();
            Algorithms = algorithmLoader.Load(root, extra == null ? new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule() }
                : new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule(), extra }); Assert.Empty(Algorithms.Diagnostics);
            Runtime = new(Algorithms); Bindings = new(Runtime, Frames);
        }
        public WorkflowDocument Document(AnalyzeVisionFrameNodeModel node)
        {
            var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = Path.Combine(AppContext.BaseDirectory, "VisionData", "barcode.pgm") };
            node.Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey(source.Id, "$"));
            var document = new WorkflowDocument { EntryNodeId = source.Id };
            document.CanvasProjection.Nodes.Add(new() { Node = source }); document.CanvasProjection.Nodes.Add(new() { Node = node });
            document.CanvasProjection.Connections.Add(new() { FromNodeId = source.Id, FromPort = WorkflowPorts.Success, ToNodeId = node.Id, ToPort = WorkflowPorts.Input }); return document;
        }
        public WorkflowRuntimeHost Host(WorkflowDocument document)
        {
            var services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(Frames).Add<IWorkflowVisionAlgorithmBindings>(Bindings)
                .Add<IWorkflowNodeCapabilityProvider>(Bindings).Add<IWorkflowRunPreparationService>(Bindings).Add<IWorkflowRunResourceOwner>(Frames);
            var host = new WorkflowRuntimeHost(Nodes, Handlers); host.Configure(document, new WorkflowContext(services)); return host;
        }
        public void Dispose() { Bindings.Dispose(); Runtime.Dispose(); Frames.Dispose(); }
    }
}
