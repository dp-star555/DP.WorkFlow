using DP.Vision;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Samples;

/// <summary>两套桌面示例共用的可直接运行新视觉流程，无相机或厂商模型依赖。</summary>
public static class WorkflowImageDemo
{
    /// <summary>创建包含预处理、分割、形态学、特征筛选和掩码颜色统计的算子示例。</summary>
    /// <param name="session">仅含Start的新工作区。</param>
    public static void PopulateProcessing(WorkflowDesignerSession session)
    {
        var file = (LoadVisionFileNodeModel)session.AddNode("Vision.LoadFile", 280, 80).Node;
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
        var file = (LoadVisionFileNodeModel)session.AddNode("Vision.LoadFile", 280, 80).Node;
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
