using DP.Vision;
using DP.Vision.Algorithms;
using System.IO;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Samples;

/// <summary>两套桌面示例共用的可直接运行新视觉流程，无相机或厂商模型依赖。</summary>
public static class WorkflowImageDemo
{
    /// <summary>由独立节点目录建立模板局部点、直线及距离示例。</summary>
    public static void PopulateGeometry(WorkflowDesignerSession session)
        => PopulateGeometry(session, false);

    /// <summary>模板只提供父姿态，业务原点定义在模板中心，ROI保存于业务坐标。</summary>
    public static void PopulateCoordinates(WorkflowDesignerSession session)
        => PopulateGeometry(session, true);

    private static void PopulateGeometry(WorkflowDesignerSession session, bool generalCoordinates)
    {
        var source = (AcquireVisionImageNodeModel)session.AddNode("Vision.AcquireFrame", 280, 80).Node;
        source.FilePath = Path.Combine(AppContext.BaseDirectory, "VisionData", "geometry-scene.pgm");
        var template = (AcquireVisionImageNodeModel)session.AddNode("Vision.AcquireFrame", 480, 80).Node;
        template.FilePath = Path.Combine(AppContext.BaseDirectory, "VisionData", "geometry-template.pgm");
        var location = (LocateVisionTemplateNodeModel)session.AddNode("Vision.LocateTemplate", 680, 80).Node;
        location.Frame = Input<ImageFrame>(source.Id); location.Template = Input<ImageFrame>(template.Id);
        location.CoordinateSystemId = "demo-part"; location.MinimumScore = .9999;
        // 与投放的完整模板一致；更换模板后须重新制作坐标绑定。
        using var patch = VisionImage.CopyFrom(new ImageInfo(4, 3, EPixelLayout.Gray8),
            new byte[] { 0, 64, 220, 40, 180, 30, 255, 80, 100, 230, 50, 140 });
        var signature = LocatedCoordinateSystem.ComputeTemplateSignature(patch);
        var created = new List<IWorkflowNodeModel> { source, template, location };
        var coordinateSource = location.Id;
        var coordinateId = location.CoordinateSystemId;
        VisionCoordinateDefinition? businessDefinition = null;
        if (generalCoordinates)
        {
            var definition = session.AddNode("Vision.DefineCoordinateSystem", 880, 80).Node;
            Set(definition, "CoordinateId", "demo-workpiece"); Set(definition, "CoordinateName", "工件中心坐标");
            Set(definition, "OriginDescription", "模板中心对应的工件基准点");
            businessDefinition = ((IWorkflowVisionCoordinateDefinitionNode)definition).GetCoordinateDefinition();
            created.Add(definition);
            var build = (AnalyzeVisionFrameNodeModel)session.AddNode("Vision.BuildCoordinateSystem", 1080, 80).Node;
            build.Frame = Input<ImageFrame>(source.Id); Set(build, "Definition", Input<VisionCoordinateDefinition>(definition.Id));
            var mode = build.GetType().GetProperty("Mode")!;
            mode.SetValue(build, Enum.Parse(mode.PropertyType, "Parent"));
            Set(build, "OriginX", WorkflowInput<double>.FromLiteral(2)); Set(build, "OriginY", WorkflowInput<double>.FromLiteral(1.5));
            build.Coordinates = new WorkflowVisionCoordinateBinding { System = Input<VisionCoordinateSystem>(location.Id, "CoordinateSystem"), CoordinateSystemId = location.CoordinateSystemId, TemplateSignature = signature };
            created.Add(build); coordinateSource = build.Id; coordinateId = businessDefinition.Id;
        }
        AnalyzeVisionFrameNodeModel Add(string type)
        {
            var node = (AnalyzeVisionFrameNodeModel)session.AddNode(type, 280 + 200 * created.Count, 80).Node;
            node.Frame = Input<ImageFrame>(source.Id); created.Add(node); return node;
        }
        AnalyzeVisionFrameNodeModel Point(double x, double y)
        {
            var node = Add("Vision.CreatePoint");
            Set(node, "PointX", WorkflowInput<double>.FromLiteral(x)); Set(node, "PointY", WorkflowInput<double>.FromLiteral(y));
            Set(node, "Space", EVisionCoordinateSpace.TemplateLocal);
            node.Coordinates = new WorkflowVisionCoordinateBinding
            {
                System = Input<VisionCoordinateSystem>(coordinateSource, "CoordinateSystem"),
                CoordinateSystemId = coordinateId, TemplateSignature = generalCoordinates ? "" : signature,
                DefinitionVersion = businessDefinition?.Version ?? 1, DefinitionSignature = businessDefinition?.Signature ?? ""
            };
            return node;
        }
        AnalyzeVisionFrameNodeModel Line(IWorkflowNodeModel a, IWorkflowNodeModel b)
        {
            var node = Add("Vision.GenerateLine"); Set(node, "A", Input<VisionPoint>(a.Id)); Set(node, "B", Input<VisionPoint>(b.Id)); return node;
        }
        var a0 = Point(0, 0); var a1 = Point(3, 0); var b0 = Point(0, 2); var b1 = Point(3, 2);
        var lineA = Line(a0, a1); var lineB = Line(b0, b1);
        var pointLine = Add("Vision.MeasurePointLineDistance");
        Set(pointLine, "Point", Input<VisionPoint>(b0.Id)); Set(pointLine, "Line", Input<VisionLine>(lineA.Id));
        Set(pointLine, "Space", EVisionCoordinateSpace.TemplateLocal);
        var lineLine = Add("Vision.MeasureLineDistance");
        Set(lineLine, "A", Input<VisionLine>(lineA.Id)); Set(lineLine, "B", Input<VisionLine>(lineB.Id));
        Set(lineLine, "Space", EVisionCoordinateSpace.TemplateLocal);
        if (businessDefinition is not null)
        {
            var blobs = (AnalyzeVisionBlobsNodeModel)Add("Vision.AnalyzeBlobs");
            blobs.MaximumGray = 255;
            blobs.Coordinates = new WorkflowVisionCoordinateBinding { System = Input<VisionCoordinateSystem>(coordinateSource, "CoordinateSystem"), CoordinateSystemId = businessDefinition.Id,
                DefinitionVersion = businessDefinition.Version, DefinitionSignature = businessDefinition.Signature };
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
