using DP.Vision;
using DP.Vision.Algorithms;
using System.IO;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Samples;

/// <summary>两套桌面示例共用的可直接运行新视觉流程，无相机或厂商模型依赖。</summary>
public static class WorkflowImageDemo
{
    /// <summary>模板匹配→构建本帧坐标系，在坐标系中建立局部点、直线及距离示例。</summary>
    public static void PopulateGeometry(WorkflowDesignerSession session)
        => PopulateGeometry(session, false);

    /// <summary>在几何示例基础上，Blob的ROI保存于模板构建的坐标系并随动。</summary>
    public static void PopulateCoordinates(WorkflowDesignerSession session)
        => PopulateGeometry(session, true);

    private static void PopulateGeometry(WorkflowDesignerSession session, bool followRoi)
    {
        var source = (AcquireVisionImageNodeModel)session.AddNode("Vision.AcquireFrame", 280, 80).Node;
        source.FilePath = Path.Combine(AppContext.BaseDirectory, "VisionData", "geometry-scene.pgm");
        var template = (AcquireVisionImageNodeModel)session.AddNode("Vision.AcquireFrame", 480, 80).Node;
        template.FilePath = Path.Combine(AppContext.BaseDirectory, "VisionData", "geometry-template.pgm");
        var location = (LocateVisionTemplatePoseNodeModel)session.AddNode("Vision.LocateTemplatePose", 680, 80).Node;
        location.Frame = Input<ImageFrame>(source.Id); location.Template = Input<ImageFrame>(template.Id); location.MinimumScore = .9999;
        var build = (AnalyzeVisionFrameNodeModel)session.AddNode("Vision.BuildCoordinateSystem", 880, 80).Node;
        build.Frame = Input<ImageFrame>(source.Id);
        Set(build, "CoordinateId", "demo-workpiece"); Set(build, "CoordinateName", "工件中心坐标");
        var mode = build.GetType().GetProperty("Mode")!;
        mode.SetValue(build, Enum.Parse(mode.PropertyType, "Template"));
        Set(build, "Template", Input<TemplatePoseResult>(location.Id));
        // 动态模板图像的参考签名来自模板像素：与投放的完整模板一致，更换模板后须重新确认坐标绑定。
        using var patch = VisionImage.CopyFrom(new ImageInfo(4, 3, EPixelLayout.Gray8),
            new byte[] { 0, 64, 220, 40, 180, 30, 255, 80, 100, 230, 50, 140 });
        var coordinates = TemplateReference.FromImage(patch, new PixelBounds(0, 0, 4, 3))
            .Bind(((IWorkflowVisionCoordinateProducerNode)build).GetCoordinateDefinition());
        var created = new List<IWorkflowNodeModel> { source, template, location, build };
        WorkflowVisionCoordinateBinding Follow() => new()
        {
            System = Input<VisionCoordinateSystem>(build.Id, "CoordinateSystem"), CoordinateSystemId = coordinates.Id,
            DefinitionVersion = coordinates.Version, DefinitionSignature = coordinates.Signature
        };
        AnalyzeVisionFrameNodeModel Add(string type)
        {
            var node = (AnalyzeVisionFrameNodeModel)session.AddNode(type, 280 + 200 * created.Count, 80).Node;
            node.Frame = Input<ImageFrame>(source.Id); created.Add(node); return node;
        }
        AnalyzeVisionFrameNodeModel Point(double x, double y)
        {
            var node = Add("Vision.CreatePoint");
            Set(node, "PointX", WorkflowInput<double>.FromLiteral(x)); Set(node, "PointY", WorkflowInput<double>.FromLiteral(y));
            Set(node, "Space", EVisionCoordinateSpace.Local);
            node.Coordinates = Follow();
            return node;
        }
        AnalyzeVisionFrameNodeModel Line(IWorkflowNodeModel a, IWorkflowNodeModel b)
        {
            var node = Add("Vision.GenerateLine"); Set(node, "A", Input<VisionPoint>(a.Id)); Set(node, "B", Input<VisionPoint>(b.Id)); return node;
        }
        var a0 = Point(-2, -1.5); var a1 = Point(1, -1.5); var b0 = Point(-2, .5); var b1 = Point(1, .5);
        var lineA = Line(a0, a1); var lineB = Line(b0, b1);
        var pointLine = Add("Vision.MeasurePointLineDistance");
        Set(pointLine, "Point", Input<VisionPoint>(b0.Id)); Set(pointLine, "Line", Input<VisionLine>(lineA.Id));
        Set(pointLine, "Space", EVisionCoordinateSpace.Local);
        var lineLine = Add("Vision.MeasureLineDistance");
        Set(lineLine, "A", Input<VisionLine>(lineA.Id)); Set(lineLine, "B", Input<VisionLine>(lineB.Id));
        Set(lineLine, "Space", EVisionCoordinateSpace.Local);
        if (followRoi)
        {
            var blobs = (AnalyzeVisionBlobsNodeModel)Add("Vision.AnalyzeBlobs");
            blobs.MaximumGray = 255;
            blobs.Coordinates = Follow();
            blobs.Regions = new() { new() { Id = "part-roi", CenterX = 0, CenterY = 0, Width = 4, Height = 3 } };
        }
        string previous = "Start";
        foreach (var node in created) { session.Connect(previous, WorkflowPorts.Success, node.Id); previous = node.Id; }
    }

    private static WorkflowInput<T> Input<T>(string nodeId, string member = "$") =>
        WorkflowInput<T>.FromBinding(new WorkflowBindingKey(nodeId, member));

    // 示例按目录获取扩展模型，只使用共享契约，宿主不编译引用独立节点包。
    private static void Set<T>(IWorkflowNodeModel node, string name, T value)
    {
        var property = node.GetType().GetProperty(name);
        if (property is null || !property.CanWrite || property.PropertyType != typeof(T))
            throw new InvalidOperationException($"节点 {node.NodeType} 的示例属性 {name} 不兼容。");
        property.SetValue(node, value);
    }

    /// <summary>按目录中的独立条码节点创建演示，不引用插件的具体模型类型。</summary>
    public static void PopulateBarcode(WorkflowDesignerSession session)
    {
        var file = (AcquireVisionImageNodeModel)session.AddNode("Vision.AcquireFrame", 280, 80).Node;
        file.FilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "VisionData", "barcode.pgm");
        var read = (AnalyzeVisionFrameNodeModel)session.AddNode("Vision.ReadBarcode", 540, 80).Node;
        read.Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey(file.Id, "$"));
        session.Connect("Start", WorkflowPorts.Success, file.Id); session.Connect(file.Id, WorkflowPorts.Success, read.Id);
    }
    /// <summary>创建包含预处理、分割、形态学、特征筛选和掩码颜色统计的算子示例。</summary>
    /// <param name="session">仅含Start的新工作区。</param>
    public static void PopulateProcessing(WorkflowDesignerSession session)
    {
        var file = (AcquireVisionImageNodeModel)session.AddNode("Vision.AcquireFrame", 280, 80).Node;
        file.FilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "VisionData", "demo.pgm");
        var process = (PreprocessVisionImageNodeModel)session.AddNode("Vision.PreprocessImage", 480, 80).Node;
        process.Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey(file.Id, "$"));
        var input = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey(process.Id, "$"));
        var threshold = (ThresholdVisionRegionNodeModel)session.AddNode("Vision.ThresholdRegion", 680, 80).Node;
        threshold.Frame = input; threshold.MinimumGray = 100; threshold.MaximumGray = 255;
        var morph = (MorphVisionRegionNodeModel)session.AddNode("Vision.MorphRegion", 880, 80).Node;
        morph.Frame = input; morph.InputRegion = WorkflowInput<DP.Vision.Algorithms.RegionAnalysisResult>.FromBinding(new WorkflowBindingKey(threshold.Id, "$"));
        morph.Operation = DP.Vision.Algorithms.ERegionMorphology.FillHoles;
        var mask = WorkflowInput<DP.Vision.Algorithms.RegionAnalysisResult>.FromBinding(new WorkflowBindingKey(morph.Id, "$"));
        var blobs = (AnalyzeVisionBlobsNodeModel)session.AddNode("Vision.AnalyzeBlobs", 1080, 80).Node;
        blobs.Frame = input; blobs.Mask = mask; blobs.MaximumGray = 255;
        var select = (SelectVisionBlobsNodeModel)session.AddNode("Vision.SelectBlobs", 1280, 80).Node;
        select.Frame = input; select.Blobs = WorkflowInput<DP.Vision.Algorithms.BlobAnalysisResult>.FromBinding(new WorkflowBindingKey(blobs.Id, "$"));
        select.MinimumArea = 2;
        var color = (AnalyzeVisionColorNodeModel)session.AddNode("Vision.AnalyzeColor", 1480, 80).Node;
        color.Frame = input; color.Mask = mask;
        string previous = "Start";
        foreach (var node in new IWorkflowNodeModel[] { file, process, threshold, morph, blobs, select, color })
        { session.Connect(previous, WorkflowPorts.Success, node.Id); previous = node.Id; }
    }

    /// <summary>在仅含Start的新工作区创建文件→Blob→颜色基础流程。</summary>
    /// <param name="session">新工作区根会话。</param>
    public static void Populate(WorkflowDesignerSession session)
    {
        var file = (AcquireVisionImageNodeModel)session.AddNode("Vision.AcquireFrame", 280, 80).Node;
        file.FilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "VisionData", "demo.pgm");
        var blobs = (AnalyzeVisionBlobsNodeModel)session.AddNode("Vision.AnalyzeBlobs", 480, 80).Node;
        blobs.Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey(file.Id, "$"));
        blobs.MinimumGray = 100; blobs.MaximumGray = 255;
        var color = (AnalyzeVisionColorNodeModel)session.AddNode("Vision.AnalyzeColor", 680, 80).Node;
        color.Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey(file.Id, "$"));
        session.Connect("Start", WorkflowPorts.Success, file.Id);
        session.Connect(file.Id, WorkflowPorts.Success, blobs.Id);
        session.Connect(blobs.Id, WorkflowPorts.Success, color.Id);
    }
}
