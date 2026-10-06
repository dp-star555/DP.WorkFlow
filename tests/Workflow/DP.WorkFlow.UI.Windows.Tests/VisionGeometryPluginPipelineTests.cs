using System.Runtime.Loader;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionGeometryPluginPipelineTests
{
    [Fact]
    public async Task TemplateBuiltCoordinates_FeedRoiAndPoints_FollowNextFrame_WithoutRewritingRecipe()
    {
        using var rig = new Rig(); var document = rig.TemplateDocument();
        var store = new WorkflowDocumentJsonStore(rig.Nodes); document = store.Deserialize(store.Serialize(document)).Document;
        var json = store.Serialize(document);
        using var host = rig.Host(document); var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var location = Output<TemplatePoseResult>(host, "location");
        Assert.Equal(12, location.ReferenceX, 9); Assert.Equal(21.5, location.ReferenceY, 9); Assert.Equal(0, location.ReferenceAngleDegrees, 9);
        var first = Output<GeometricDistanceResult>(host, "distance");
        Assert.Equal("reference-px", first.Unit); Assert.Equal(2, first.Distance, 9);
        Assert.Equal(12, first.A.ImagePosition.X, 9); Assert.Equal(23.5, first.A.ImagePosition.Y, 9);
        Assert.Equal(0, first.A.LocalPosition!.Value.X, 9); Assert.Equal(2, first.A.LocalPosition.Value.Y, 9);
        Assert.Equal("business", first.CoordinateSystem!.Definition.Id);
        Assert.Equal(12, Output<BlobAnalysisResult>(host, "business-roi").Blobs.Sum(b => b.Area));
        rig.WriteScene(16, 4); run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var second = Output<GeometricDistanceResult>(host, "distance");
        Assert.Equal(18, second.A.ImagePosition.X, 9); Assert.Equal(7.5, second.A.ImagePosition.Y, 9);
        Assert.NotEqual(first.FrameId, second.FrameId); Assert.Equal(2, second.Distance, 9);
        Assert.Equal(12, Output<BlobAnalysisResult>(host, "business-roi").Blobs.Sum(b => b.Area));
        Assert.Equal(json, store.Serialize(document));
        rig.WriteScene(absent: true); Assert.False((await host.RunAsync()).Success);
        Assert.False(Output<TemplatePoseResult>(host, "location").Found);
        Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId is "business-source" or "distance" or "business-roi");
        Assert.Null(rig.Frames.Capture("business-source"));
    }

    [Fact]
    public async Task ChangedTemplate_RejectsRoiConfirmedWithOldReference()
    {
        using var rig = new Rig(); using var host = rig.Host(rig.TemplateDocument());
        Assert.True((await host.RunAsync()).Success);
        rig.ChangeTemplate(); rig.WriteScene();
        Assert.False((await host.RunAsync()).Success);
        Assert.True(Output<TemplatePoseResult>(host, "location").Found); // 匹配仍成功，是参考签名变化让下游拒绝。
        Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId is "distance" or "business-roi" or "p0");
    }

    [Fact]
    public async Task PoseAndTwoPointSourcesWorkWithoutAnyTemplateNode()
    {
        using var rig = new Rig(); var pose = rig.Build("pose", "Pose");
        Set(pose, "OriginX", WorkflowInput<double>.FromLiteral(10)); Set(pose, "OriginY", WorkflowInput<double>.FromLiteral(20));
        Set(pose, "Scale", WorkflowInput<double>.FromLiteral(2));
        var points = rig.Build("points", "TwoPoints"); Set(points, "OriginPoint", Input<VisionPoint>("origin")); Set(points, "DirectionPoint", Input<VisionPoint>("direction"));
        Set(points, "ReferenceLength", WorkflowInput<double>.FromLiteral(5));
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath }, pose,
            rig.Point("origin", 10, 20), rig.Point("direction", 20, 20), points);
        using var host = rig.Host(document); var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var a = Output<VisionCoordinateSystemResult>(host, "pose"); var b = Output<VisionCoordinateSystemResult>(host, "points");
        Assert.Equal(a.CoordinateSystem.Definition.Signature, b.CoordinateSystem.Definition.Signature);
        Assert.Equal(a.CoordinateSystem.LocalToImage.Tx, b.CoordinateSystem.LocalToImage.Tx);
        Assert.Equal(2, b.CoordinateSystem.SimilarityScale, 9);
        Assert.Equal("source", document.Graph.Nodes.First().Id);
        Assert.DoesNotContain(document.Graph.Nodes, n => n.NodeType.Contains("Template", StringComparison.Ordinal));
        using var page = new VisionFrameEditorPageModel(points, rig.Frames); using var canvas = page.Capture(1);
        Assert.NotNull(canvas); Assert.Contains("坐标系", page.Status);
    }

    [Theory]
    [InlineData("inconsistent")]
    [InlineData("version")]
    [InlineData("unit")]
    [InlineData("name")]
    public void CoordinateDefinitionErrorsFailCompilationBeforeReadingImages(string fault)
    {
        using var rig = new Rig(); var document = rig.PoseDocument();
        var source = document.Graph.Nodes.Single(n => n.Id == "business-source");
        if (fault == "inconsistent") { var other = rig.Build("other", "Pose"); Set(other, "DefinitionVersion", 2); document = rig.Document([.. document.Graph.Nodes, other]); }
        if (fault == "version") Set(source, "DefinitionVersion", 2);
        if (fault == "unit") Set(source, "Unit", EVisionCoordinateUnit.Millimeter);
        // 名称不进入签名，只是共用同一坐标系的节点之间必须一致。
        if (fault == "name") { var other = rig.Build("other", "Pose"); Set(other, "CoordinateName", "夹具"); document = rig.Document([.. document.Graph.Nodes, other]); }
        var error = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(rig.Nodes).Compile(document));
        Assert.Contains(error.Errors, e => e.Code == "WF030");
        Assert.Null(rig.Frames.Capture("source"));
    }

    [Fact]
    public void CoordinateSystemDropdown_ListsDocumentCoordinates_AdoptsExistingOrCreatesNew()
    {
        using var rig = new Rig();
        var first = rig.Build("first", "Pose"); Set(first, "CoordinateName", "工件"); Set(first, "DefinitionVersion", 2);
        var second = rig.Build("second", "Pose"); Set(second, "CoordinateId", "fixture"); Set(second, "CoordinateName", "夹具");
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath }, first, second);
        var session = new WorkflowDesignerSession(document, rig.Nodes) { SelectedNodeId = "second" };
        using var inspector = new WorkflowPropertyInspectorModel(session, "source");
        WorkflowPropertyEntry Entry() => inspector.Entries.Single(e => e.Name == "CoordinateSystem");
        Assert.Equal(WorkflowPropertyEditorKind.Choice, Entry().EditorKind);
        Assert.Equal(new[] { "夹具（v1，reference-px）", "工件（v2，reference-px）", "新建坐标系" }, Entry().Choices.Select(c => c.Label));
        Assert.Equal(Entry().Choices[0].Value, Entry().Value);
        // 选择已有坐标系：采用其身份、名称、版本和单位，成为它的又一个来源。
        inspector.SetValue(Entry(), Entry().Choices[1].Value);
        Assert.Equal("business", Get(second, "CoordinateId")); Assert.Equal("工件", Get(second, "CoordinateName")); Assert.Equal(2, Get(second, "DefinitionVersion"));
        Assert.Empty(((IWorkflowNodeDocumentConfigurationValidator)second).ValidateDocumentConfiguration(document.Graph.Nodes.ToArray()));
        Assert.Equal(new[] { "工件（v2，reference-px）", "新建坐标系" }, Entry().Choices.Select(c => c.Label));
        // 新建：新的身份，默认名称按已有坐标系数量编号。
        inspector.SetValue(Entry(), Entry().Choices[^1].Value);
        Assert.NotEqual("business", Get(second, "CoordinateId")); Assert.Equal("坐标系2", Get(second, "CoordinateName")); Assert.Equal(1, Get(second, "DefinitionVersion"));
    }

    [Fact]
    public async Task CoordinateEditorCanRebindSameDefinitionWithoutConvertingStoredRoiTwice()
    {
        using var rig = new Rig(); var document = rig.PoseDocument(); var alternate = rig.Build("alternate", "Pose");
        Set(alternate, "OriginX", WorkflowInput<double>.FromLiteral(8)); Set(alternate, "OriginY", WorkflowInput<double>.FromLiteral(9));
        document = rig.Document([.. document.Graph.Nodes, alternate]);
        using var host = rig.Host(document); Assert.True((await host.RunAsync()).Success);
        var blob = new AnalyzeVisionBlobsNodeModel { Id = "editing", Frame = Input<ImageFrame>("source"),
            Regions = [new() { Id = "roi", CenterX = 12, CenterY = 21.5, Width = 4, Height = 3 }] };
        using var page = new VisionFrameEditorPageModel(blob, rig.Frames); using var preview = page.Capture(0);
        VisionCoordinateRebinding.Bind(blob, "business-source", rig.Frames); Assert.Equal(0, blob.Regions[0].CenterX, 9); Assert.Equal(0, blob.Regions[0].CenterY, 9);
        VisionCoordinateRebinding.Bind(blob, "alternate", rig.Frames); Assert.Equal(0, blob.Regions[0].CenterX, 9); Assert.Equal(0, blob.Regions[0].CenterY, 9);
        Assert.Equal("alternate", blob.Coordinates!.System.Binding!.Value.NodeId);
        using (page.Capture(0)) { }
        var displayed = Assert.IsType<RectangleGeometry>(page.Editor.Document.Rois[0].Shape);
        Assert.Equal(8, displayed.Center.X, 9); Assert.Equal(9, displayed.Center.Y, 9);
        VisionCoordinateRebinding.Unbind(blob, rig.Frames); Assert.Equal(8, blob.Regions[0].CenterX, 9); Assert.Equal(9, blob.Regions[0].CenterY, 9);
    }

    [Fact]
    public async Task AffineCoordinateBindingRejectsCaliperWithoutPartiallyChangingConfiguration()
    {
        using var rig = new Rig(); var build = rig.Build("affine", "Matrix"); Set(build, "M12", WorkflowInput<double>.FromLiteral(1));
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath }, build);
        using var host = rig.Host(document); Assert.True((await host.RunAsync()).Success);
        var caliper = new MeasureVisionCaliperNodeModel { Id = "caliper", Frame = Input<ImageFrame>("source"), StartX = 4, StartY = 10, EndX = 24, EndY = 10 };
        Assert.Throws<NotSupportedException>(() => VisionCoordinateRebinding.Bind(caliper, "affine", rig.Frames));
        Assert.Null(caliper.Coordinates); Assert.Equal(4, caliper.StartX); Assert.Equal(24, caliper.EndX);
    }

    [Fact]
    public async Task SharedCoordinateDemoRunsThroughDiscoveredPluginAndBusinessRoi()
    {
        using var rig = new Rig(configure: (nodes, handlers) => { nodes.RegisterStandardNodes(); handlers.RegisterStandardNodeHandlers(); });
        using var workspace = new WorkflowDocumentWorkspace(rig.Nodes); workspace.New("业务坐标示例");
        DP.WorkFlow.Samples.WorkflowImageDemo.PopulateCoordinates(workspace.Navigator!.RootSession);
        var store = new WorkflowDocumentJsonStore(rig.Nodes); var document = store.Deserialize(store.Serialize(workspace.Navigator.RootSession.Document)).Document;
        using var host = rig.Host(document); var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        Assert.All(host.Engine!.RunState.NodeOutputs.Select(o => o.Value).OfType<GeometricDistanceResult>(), d => { Assert.Equal(2, d.Distance, 9); Assert.Equal("reference-px", d.Unit); });
        var blob = Assert.Single(host.Engine.RunState.NodeOutputs.Select(o => o.Value).OfType<BlobAnalysisResult>());
        Assert.Equal(12, blob.Blobs.Sum(b => b.Area)); Assert.Equal("demo-workpiece", blob.CoordinateSystem!.Definition.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalibrationTableEditsPersistAndSizeMismatchBlocksCoordinateOutput(bool wrongSize)
    {
        using var rig = new Rig(); var build = rig.Build("calibrated", "Correspondences");
        Set(build, "CalibrationImageWidth", wrongSize ? 64 : 32); Set(build, "CalibrationImageHeight", 32);
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath }, build);
        var session = new WorkflowDesignerSession(document, rig.Nodes) { SelectedNodeId = build.Id };
        using (var inspector = new WorkflowPropertyInspectorModel(session, build.Id))
        {
            var entry = inspector.Entries.Single(e => e.Name == "Samples"); Assert.True(WorkflowCollectionTableModel.TryCreate(entry, out var table));
            Assert.Equal(new[] { "LocalX", "LocalY", "ImageX", "ImageY" }, table!.Columns);
            inspector.ApplyCollectionTable(table, new IReadOnlyList<string>[] { new[] { "0", "0", "10", "20" }, new[] { "5", "0", "20", "20" }, new[] { "0", "5", "10", "30" } });
        }
        var store = new WorkflowDocumentJsonStore(rig.Nodes); document = store.Deserialize(store.Serialize(document)).Document;
        using var host = rig.Host(document); var run = await host.RunAsync(); Assert.Equal(!wrongSize, run.Success);
        if (wrongSize) Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == "calibrated");
        else { var result = Output<VisionCoordinateSystemResult>(host, "calibrated"); Assert.Equal(0, result.CalibrationRms!.Value, 9); Assert.Equal(2, result.CoordinateSystem.SimilarityScale, 9); }
    }

    [Fact]
    public void SingularMatrixFailsCompilationRatherThanFailingDuringImageExecution()
    {
        using var rig = new Rig(); var build = rig.Build("singular", "Matrix"); Set(build, "M22", WorkflowInput<double>.FromLiteral(0));
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath }, build);
        var error = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(rig.Nodes).Compile(document));
        Assert.Contains(error.Errors, e => e.Code == "WF030" && e.NodeId == build.Id);
    }

    [Fact]
    public async Task LineIntersectionBuildUsesMeasuredFactsAndRejectsParallelAxes()
    {
        using var rig = new Rig(); var build = rig.Build("intersect", "LineIntersection");
        Set(build, "AxisLine", Input<VisionLine>("x-axis")); Set(build, "CrossLine", Input<VisionLine>("cross"));
        var crossB = rig.Point("q1", 10, 30);
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath },
            rig.Point("p0", 0, 20), rig.Point("p1", 20, 20), rig.Point("q0", 10, 0), crossB, rig.Line("x-axis", "p0", "p1"), rig.Line("cross", "q0", "q1"), build);
        using var host = rig.Host(document); Assert.True((await host.RunAsync()).Success);
        var system = Output<VisionCoordinateSystemResult>(host, "intersect").CoordinateSystem;
        Assert.Equal(10, system.LocalToImage.Tx, 9); Assert.Equal(20, system.LocalToImage.Ty, 9);
        Set(crossB, "PointX", WorkflowInput<double>.FromLiteral(20)); Set(crossB, "PointY", WorkflowInput<double>.FromLiteral(0));
        host.Configure(document, new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(rig.Frames).Add<IWorkflowVisionAlgorithmBindings>(rig.Bindings)
            .Add<IWorkflowNodeCapabilityProvider>(rig.Bindings).Add<IWorkflowRunPreparationService>(rig.Bindings).Add<IWorkflowRunResourceOwner>(rig.Frames)));
        Assert.False((await host.RunAsync()).Success); Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == "intersect");
    }
    [Fact]
    public void BothNativeRenderers_KeepGeometryReadOnly_WhenCoordinateBindingIsReady()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var rig = new Rig();
                var document = rig.BasicDocument(); using var host = rig.Host(document);
                Assert.True(host.RunAsync().GetAwaiter().GetResult().Success);
                var node = document.Graph.Nodes.Single(n => n.Id == "point-line");
                var session = new WorkflowDesignerSession(document, rig.Nodes);
                var descriptor = Assert.Single(new VisionFrameEditorPageProvider(rig.Frames).CreatePages(new WorkflowNodeEditorContext(session, node.Id, node)));
                using var page = Assert.IsType<VisionFrameEditorPageModel>(descriptor.Model);
                using var forms = new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer().CreateControl(descriptor);
                Refresh(forms);
                Assert.True(page.CanBindCoordinates); Assert.True(page.CoordinateEditingReady); Assert.False(page.CanEdit);
                Assert.Null(Assert.Single(forms.Controls.OfType<DP.Vision.Winform.VisionCanvasControl>()).Editor);
                var wpf = Assert.IsAssignableFrom<System.Windows.Controls.DockPanel>(new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer().CreateElement(descriptor));
                Refresh(wpf);
                Assert.Null(Assert.Single(wpf.Children.OfType<DP.Vision.WPF.VisionCanvasControl>()).Editor);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20))); Assert.Null(failure);
        static void Refresh(object control) => control.GetType().GetMethod("RefreshPreview",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, null);
    }

    [Fact]
    public async Task ActualSharedGeometrySample_RunsDiscoveredNodes_InTemplateBuiltCoordinates()
    {
        using var rig = new Rig(configure: (nodes, handlers) =>
        { nodes.RegisterStandardNodes(); handlers.RegisterStandardNodeHandlers(); });
        using var workspace = new WorkflowDocumentWorkspace(rig.Nodes); workspace.New("定位测量示例");
        DP.WorkFlow.Samples.WorkflowImageDemo.PopulateGeometry(workspace.Navigator!.RootSession);
        var store = new WorkflowDocumentJsonStore(rig.Nodes);
        var document = store.Deserialize(store.Serialize(workspace.Navigator.RootSession.Document)).Document;
        using var host = rig.Host(document); var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var distances = host.Engine!.RunState.NodeOutputs.Select(o => o.Value).OfType<GeometricDistanceResult>().ToArray();
        Assert.Equal(2, distances.Length);
        Assert.All(distances, d => { Assert.Equal(2, d.Distance, 9); Assert.Equal("reference-px", d.Unit); Assert.Equal("demo-workpiece", d.CoordinateSystem!.CoordinateSystemId); });
        Assert.Equal(5, distances[0].A.ImagePosition.X, 9); Assert.Equal(8, distances[0].A.ImagePosition.Y, 9);
    }

    [Fact]
    public async Task IndependentPackage_ExposesTenTypedNodes_AndRunsGeometryAndPreview()
    {
        using var rig = new Rig();
        Assert.DoesNotContain(GetType().Assembly.GetReferencedAssemblies(), a => a.Name == "DP.WorkFlow.Nodes.Vision.Geometry");
        Assert.Equal(30, rig.Nodes.Snapshot().Count);
        var document = rig.BasicDocument();
        var store = new WorkflowDocumentJsonStore(rig.Nodes); document = store.Deserialize(store.Serialize(document)).Document;
        var json = store.Serialize(document);
        using var host = rig.Host(document); var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var distance = Output<GeometricDistanceResult>(host, "point-line");
        Assert.Equal(5, distance.Distance, 10); Assert.Equal("image-px", distance.Unit);
        Assert.Equal(5, Output<GeometricDistanceResult>(host, "line-line").Distance, 10);
        Assert.Equal(5, Output<GeometricDistanceResult>(host, "point-point").Distance, 10);
        var line = Output<VisionLine>(host, "line-a"); Assert.Equal(10, line.ImageLength, 10);
        Assert.Equal(Output<ImageFrame>(host, "source").FrameId, line.FrameId);
        Assert.Equal(Output<BlobAnalysisResult>(host, "blobs").MeasuredCentroids[0].ImagePosition, Output<VisionPoint>(host, "select").ImagePosition);
        using var page = new VisionFrameEditorPageModel(document.Graph.Nodes.Single(n => n.Id == "point-line"), rig.Frames);
        using var canvas = page.Capture(1); Assert.NotNull(canvas); Assert.Contains("image-px", page.Status);
        Assert.Contains("image-px", page.Pick(new PointD(0, 2), .2));
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(document.Graph.Nodes.Single(n => n.Id == "line-a").GetType().Assembly));
        Assert.Equal(json, store.Serialize(document));
        Assert.True((await host.RunAsync()).Success);
    }

    [Fact]
    public async Task MissingGeometryImplementation_BlocksBeforeAnyImageIsRead()
    {
        using var rig = new Rig(includeManaged: false);
        var document = rig.BasicDocument();
        using var diagnostics = new WorkflowVisionAlgorithmDiagnostics(rig.Algorithms, rig.Runtime, rig.Bindings, () => new());
        Assert.Contains(diagnostics.Analyze(document), i => i.Code == "ALG_IMPLEMENTATION_MISSING" && i.NodeId == "line-a");
        using var host = rig.Host(document);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());
        Assert.Empty(host.Engine!.RunState.NodeOutputs);
    }

    [Fact]
    public async Task ForeignFramePoint_IsRejected_WithoutLineOrDistanceOutput()
    {
        using var rig = new Rig(configure: (nodes, handlers) =>
        { nodes.Register(WorkflowNodeDescriptor.Create<ForeignPointNode, VisionPoint>(ports: Rig.Ports)); handlers.Register(new ForeignPointHandler()); });
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath },
            new ForeignPointNode { Id = "foreign" }, rig.Point("p", 1, 0), rig.Line("line", "foreign", "p"));
        using var host = rig.Host(document);
        Assert.False((await host.RunAsync()).Success);
        Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == "line");
    }

    [Fact]
    public async Task ExternalCoordinateResult_CannotPublishOnAnotherFrame()
    {
        using var rig = new Rig(configure: (nodes, handlers) =>
        { nodes.Register(WorkflowNodeDescriptor.Create<ForeignLocationNode, ForeignLocationResult>(ports: Rig.Ports)); handlers.Register(new ForeignLocationHandler()); });
        var document = rig.Document(new LoadVisionFileNodeModel { Id = "source", FilePath = rig.ScenePath }, new ForeignLocationNode { Id = "foreign-location" });
        using var host = rig.Host(document);
        Assert.False((await host.RunAsync()).Success);
        Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == "foreign-location");
        Assert.Null(rig.Frames.Capture("foreign-location"));
    }

    [Fact]
    public void MissingGeometryNodePackage_PreservesRecipeAndRestoresAfterDeployment()
    {
        using var rig = new Rig();
        var store = new WorkflowDocumentJsonStore(rig.Nodes);
        var original = store.Serialize(rig.BasicDocument());
        var missing = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterImageNodes());
        var unavailable = missing.Deserialize(original).Document;
        Assert.IsType<UnknownWorkflowNodeModel>(unavailable.Graph.Nodes.Single(n => n.Id == "line-a"));
        var restored = store.Deserialize(missing.Serialize(unavailable)).Document;
        Assert.Equal("Vision.GenerateLine", restored.Graph.Nodes.Single(n => n.Id == "line-a").NodeType);
        Assert.Equal("managed.geometry", Assert.Single(((IWorkflowVisionAlgorithmNode)restored.Graph.Nodes.Single(n => n.Id == "line-a")).GetAlgorithmSlots()).Selection.ImplementationId);
    }

    [Fact]
    public async Task CoordinatePage_ListsTranslation_AndCaliperBindingRoundTripPreservesBand()
    {
        using var rig = new Rig(configure: (nodes, handlers) =>
        { nodes.Register(WorkflowNodeDescriptor.Create<ScaledPoseNode, TemplatePoseResult>(ports: Rig.Ports)); handlers.Register(new ScaledPoseHandler()); });
        var document = rig.TemplateDocument();
        document = rig.Document([.. document.Graph.Nodes, new ScaledPoseNode { Id = "scaled-location" },
            GeometryPluginTestCatalog.BuildFromTemplate(rig.Nodes, "scaled-part", "source", "business", "scaled-location")]);
        using var host = rig.Host(document); Assert.True((await host.RunAsync()).Success);
        // 制作界面读取中立定位契约；无需维护旋转/平移节点白名单。
        var caliper = new MeasureVisionCaliperNodeModel { Id = "caliper", Frame = Input<ImageFrame>("source"),
            StartX = 4, StartY = 10, EndX = 24, EndY = 10, MinimumSeparation = 3, BandSampleStep = 1 };
        var session = new WorkflowDesignerSession(rig.Document(document.Graph.Nodes.ToArray().Append(caliper).ToArray()), rig.Nodes) { SelectedNodeId = "caliper" };
        using var inspector = new WorkflowPropertyInspectorModel(session, "source", null,
            WorkflowVisionCoordinateProperties.CreateProvider(rig.Frames, () => session.Canvas.Nodes.Select(n => n.Node).ToArray()));
        WorkflowPropertyEntry Coordinate() => inspector.Entries.Single(e => e.Name == WorkflowVisionCoordinateProperties.EntryName);
        // 只列出构建坐标系的节点，模板匹配本身不是坐标来源。
        Assert.Equal(new object?[] { "", "business-source", "scaled-part" }, Coordinate().Choices.Select(c => c.Value));
        Assert.StartsWith("工件坐标（v1，reference-px）— ", Coordinate().Choices[1].Label);
        inspector.SetValue(Coordinate(), "scaled-part");
        Assert.Equal(.5, caliper.BandSampleStep, 10); Assert.Equal(1.5, caliper.MinimumSeparation, 10);
        Assert.Empty(caliper.ValidateConfiguration());
        inspector.SetValue(Coordinate(), "");
        Assert.Null(caliper.Coordinates);
        Assert.Equal(4, caliper.StartX, 9); Assert.Equal(24, caliper.EndX, 9);
        Assert.Equal(10, caliper.StartY, 9); Assert.Equal(10, caliper.EndY, 9);
        Assert.Equal(1, caliper.BandSampleStep, 9); Assert.Equal(3, caliper.MinimumSeparation, 9);
    }

    private static T Output<T>(WorkflowRuntimeHost host, string id) => Assert.IsType<T>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == id).Value);
    private static WorkflowInput<T> Input<T>(string id, string member = "$") => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(id, member));
    private static void Set(IWorkflowNodeModel node, string property, object? value) => node.GetType().GetProperty(property)!.SetValue(node, value);
    private static object? Get(IWorkflowNodeModel node, string property) => node.GetType().GetProperty(property)!.GetValue(node);
    private sealed class ForeignPointNode : WorkflowNodeModel { public override string NodeType => "Test.ForeignPoint"; }
    private sealed class ForeignLocationResult : IVisionCoordinateResult
    {
        public string FrameId => "foreign-frame";
        public VisionCoordinateSystem? CoordinateSystem => null;
    }
    private sealed class ForeignLocationNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.ForeignLocation";
        public WorkflowInput<ImageFrame> Frame { get; set; } = Input<ImageFrame>("source");
    }
    private sealed class ForeignLocationHandler : WorkflowNodeHandler<ForeignLocationNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ForeignLocationNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var frame = context.ResolveInput(node.Frame)!; var result = new ForeignLocationResult();
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
        }
    }
    private sealed class ScaledPoseNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.ScaledPose";
        public WorkflowInput<ImageFrame> Frame { get; set; } = Input<ImageFrame>("source");
        public WorkflowInput<ImageFrame> Template { get; set; } = Input<ImageFrame>("template");
    }
    private sealed class ScaledPoseHandler : WorkflowNodeHandler<ScaledPoseNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ScaledPoseNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var frame = context.ResolveInput(node.Frame)!; var template = context.ResolveInput(node.Template)!;
            var pose = new TemplatePoseResult(frame.FrameId, template.FrameId, 1, new TemplatePoseTransform(4, 3, new PointD(12, 21.5), .4, 2),
                TemplateReference.FromImage(template.Image, new PixelBounds(0, 0, 4, 3)));
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: pose, projection: WorkflowVisionFrameScope.Stage(context, frame, pose)));
        }
    }
    private sealed class ForeignPointHandler : WorkflowNodeHandler<ForeignPointNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ForeignPointNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(output: new VisionPoint("foreign-frame", new PointD(0, 0))));
    }
    private sealed class Rig : IDisposable
    {
        public static WorkflowPortDescriptor[] Ports => [WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Success)];
        public WorkflowNodeCatalog Nodes { get; }
        public WorkflowNodeHandlerCatalog Handlers { get; }
        public VisionAlgorithmCatalog Algorithms { get; }
        public VisionAlgorithmRuntime Runtime { get; }
        public WorkflowVisionAlgorithmBindings Bindings { get; }
        public WorkflowVisionFrameScope Frames { get; } = new();
        public string ScenePath { get; }
        public string TemplatePath { get; }
        private readonly byte[] _patch = [0, 64, 220, 40, 180, 30, 255, 80, 100, 230, 50, 140];
        private readonly string _root;
        public Rig(bool includeManaged = true, Action<WorkflowNodeCatalog, WorkflowNodeHandlerCatalog>? configure = null)
        {
            _root = Path.Combine(AppContext.BaseDirectory, "GeometryPluginRuns", Guid.NewGuid().ToString("N"));
            (Nodes, Handlers) = GeometryPluginTestCatalog.Create(_root, configure);
            Algorithms = VisionAlgorithmCatalog.Compose(includeManaged
                ? new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule(), new ManagedVisionAlgorithmModule() }
                : new IVisionAlgorithmModule[] { new OpenCvVisionAlgorithmModule() });
            Runtime = new(Algorithms); Bindings = new(Runtime, Frames);
            ScenePath = Path.Combine(_root, "scene.pgm"); TemplatePath = Path.Combine(_root, "template.pgm");
            WriteTemplate(); WriteScene();
        }
        private void WriteTemplate() => File.WriteAllText(TemplatePath, "P2\n4 3\n255\n" + string.Join(" ", _patch) + "\n");
        public void ChangeTemplate() { _patch[1]++; WriteTemplate(); }
        public void WriteScene(int x = 10, int y = 20, bool absent = false)
        {
            var image = Enumerable.Repeat((byte)200, 32 * 32).ToArray();
            if (!absent) for (var row = 0; row < 3; row++) Array.Copy(_patch, row * 4, image, (y + row) * 32 + x, 4);
            File.WriteAllText(ScenePath, "P2\n32 32\n255\n" + string.Join(" ", image) + "\n");
        }
        public AnalyzeVisionFrameNodeModel Node(string type, string id)
        {
            var node = (AnalyzeVisionFrameNodeModel)Nodes.GetOrThrow(type).Factory(); node.Id = id; node.Frame = Input<ImageFrame>("source"); return node;
        }
        public AnalyzeVisionFrameNodeModel Build(string id, string mode)
        {
            var node = Node("Vision.BuildCoordinateSystem", id); Set(node, "CoordinateId", "business");
            var property = node.GetType().GetProperty("Mode")!; property.SetValue(node, Enum.Parse(property.PropertyType, mode)); return node;
        }
        /// <summary>平移模板→模板方式构建，点和ROI按模板中心的业务坐标随动。</summary>
        public WorkflowDocument TemplateDocument()
        {
            var build = GeometryPluginTestCatalog.BuildFromTemplate(Nodes, "business-source", "source", "business", "location");
            using var image = VisionImage.CopyFrom(new ImageInfo(4, 3, EPixelLayout.Gray8), _patch);
            var coordinates = GeometryPluginTestCatalog.Follow(build.Id, TemplateReference.FromImage(image, new PixelBounds(0, 0, 4, 3))
                .Bind(GeometryPluginTestCatalog.Definition(build)));
            var distance = Node("Vision.MeasurePointLineDistance", "distance"); Set(distance, "Point", Input<VisionPoint>("q0")); Set(distance, "Line", Input<VisionLine>("line"));
            Set(distance, "Space", EVisionCoordinateSpace.Local);
            var roi = new AnalyzeVisionBlobsNodeModel { Id = "business-roi", Frame = Input<ImageFrame>("source"), MaximumGray = 255, Coordinates = coordinates,
                Regions = [new() { Id = "roi", CenterX = 0, CenterY = 0, Width = 4, Height = 3 }] };
            return Document(new LoadVisionFileNodeModel { Id = "source", FilePath = ScenePath }, new LoadVisionFileNodeModel { Id = "template", FilePath = TemplatePath },
                new LocateVisionTemplateNodeModel { Id = "location", Frame = Input<ImageFrame>("source"), Template = Input<ImageFrame>("template"), MinimumScore = .9999 },
                build, Point("p0", 0, 0, coordinates), Point("p1", 3, 0, coordinates), Point("q0", 0, 2, coordinates), Line("line", "p0", "p1"), distance, roi);
        }
        /// <summary>参数姿态构建的业务坐标及随动ROI，定义可静态解析。</summary>
        public WorkflowDocument PoseDocument()
        {
            var build = Build("business-source", "Pose");
            Set(build, "OriginX", WorkflowInput<double>.FromLiteral(12)); Set(build, "OriginY", WorkflowInput<double>.FromLiteral(21.5));
            var roi = new AnalyzeVisionBlobsNodeModel { Id = "business-roi", Frame = Input<ImageFrame>("source"), MaximumGray = 255,
                Coordinates = GeometryPluginTestCatalog.Follow(build.Id, GeometryPluginTestCatalog.Definition(build)),
                Regions = [new() { Id = "roi", CenterX = 0, CenterY = 0, Width = 4, Height = 3 }] };
            return Document(new LoadVisionFileNodeModel { Id = "source", FilePath = ScenePath }, build, roi);
        }
        public AnalyzeVisionFrameNodeModel Point(string id, double x, double y, WorkflowVisionCoordinateBinding? coordinates = null)
        {
            var node = Node("Vision.CreatePoint", id); Set(node, "PointX", WorkflowInput<double>.FromLiteral(x)); Set(node, "PointY", WorkflowInput<double>.FromLiteral(y));
            if (coordinates is not null) { node.Coordinates = coordinates; Set(node, "Space", EVisionCoordinateSpace.Local); }
            return node;
        }
        public AnalyzeVisionFrameNodeModel Line(string id, string a, string b)
        {
            var node = Node("Vision.GenerateLine", id); Set(node, "A", Input<VisionPoint>(a)); Set(node, "B", Input<VisionPoint>(b)); return node;
        }
        public WorkflowDocument BasicDocument()
        {
            var pointLine = Node("Vision.MeasurePointLineDistance", "point-line"); Set(pointLine, "Point", Input<VisionPoint>("q0")); Set(pointLine, "Line", Input<VisionLine>("line-a"));
            var lineLine = Node("Vision.MeasureLineDistance", "line-line"); Set(lineLine, "A", Input<VisionLine>("line-a")); Set(lineLine, "B", Input<VisionLine>("line-b"));
            var pointPoint = Node("Vision.MeasurePointDistance", "point-point"); Set(pointPoint, "A", Input<VisionPoint>("p0")); Set(pointPoint, "B", Input<VisionPoint>("q0"));
            var mapPoint = Node("Vision.TransformPoint", "map-point"); Set(mapPoint, "Point", Input<VisionPoint>("q0"));
            var mapLine = Node("Vision.TransformLine", "map-line"); Set(mapLine, "Line", Input<VisionLine>("line-a"));
            var select = Node("Vision.SelectPoint", "select"); Set(select, "Points", Input<IReadOnlyList<VisionPoint>>("blobs", "MeasuredCentroids"));
            return Document(new LoadVisionFileNodeModel { Id = "source", FilePath = ScenePath },
                Point("p0", 0, 0), Point("p1", 10, 0), Point("q0", 0, 5), Point("q1", 10, 5),
                Line("line-a", "p0", "p1"), Line("line-b", "q0", "q1"), pointLine, lineLine, pointPoint, mapPoint, mapLine,
                new AnalyzeVisionBlobsNodeModel { Id = "blobs", Frame = Input<ImageFrame>("source"), MaximumGray = 255 }, select);
        }
        public WorkflowDocument Document(params IWorkflowNodeModel[] nodes)
        {
            var document = new WorkflowDocument { EntryNodeId = nodes[0].Id };
            foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new() { Node = node });
            for (var index = 1; index < nodes.Length; index++) document.CanvasProjection.Connections.Add(new()
                { FromNodeId = nodes[index - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[index].Id, ToPort = WorkflowPorts.Input });
            return document;
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
