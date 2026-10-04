using DP.Vision;
using DP.Vision.Acquisition;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>只贡献独立DP.Vision新版节点的宿主入口，不将已取消的OCR/切字/打印质量放回工具箱。</summary>
public sealed class WorkflowImageRuntimePluginModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "Workflow.Nodes.Vision.ImageSource";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.Nodes.RegisterImageNodes(); extensions.Handlers.RegisterImageNodeHandlers();
    }
}

/// <summary>新版节点及能力要求的统一注册入口。</summary>
public static class WorkflowImageNodes
{
    /// <summary>注册视觉节点及强类型标准输出。</summary>
    /// <param name="catalog">宿主目录。</param>
    /// <returns>目录。</returns>
    public static WorkflowNodeCatalog RegisterImageNodes(this WorkflowNodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var ports = new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Success) };
        return catalog
            .Register(WorkflowNodeDescriptor.Create<AcquireVisionImageNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<LoadVisionFileNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<LoadVisionFolderNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<CaptureAreaFrameNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<CaptureLineScanFrameNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<AnalyzeVisionBlobsNodeModel, BlobAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<AnalyzeVisionColorNodeModel, ColorAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MeasureVisionEdgesNodeModel, EdgeMeasurementResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<LocateVisionTemplateNodeModel, TemplateLocationResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<SolveVisionCalibrationNodeModel, AffineCalibration>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MapVisionCoordinateNodeModel, Coordinate2D>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MeasureVisionDistanceNodeModel, VisionDistanceResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<PreprocessVisionImageNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<ThresholdVisionRegionNodeModel, RegionAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MorphVisionRegionNodeModel, RegionAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<SelectVisionBlobsNodeModel, BlobAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MeasureVisionCaliperNodeModel, CaliperResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<FitVisionRobustLineNodeModel, RobustLineResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<LocateVisionTemplatePoseNodeModel, TemplatePoseResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MapVisionPoseCoordinateNodeModel, Coordinate2D>(ports: ports));
    }

    /// <summary>成组注册处理器及启动前能力要求。</summary>
    /// <param name="handlers">宿主处理器目录。</param>
    /// <returns>目录。</returns>
    public static WorkflowNodeHandlerCatalog RegisterImageNodeHandlers(this WorkflowNodeHandlerCatalog handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var frames = WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionFrameScope>();
        return handlers
            .Register(new AcquireVisionImageNodeHandler(), node =>
            {
                var input = (AcquireVisionImageNodeModel)node;
                var source = input.SourceMode switch
                {
                    EWorkflowVisionImageSource.File => WorkflowRuntimeCapabilityRequirement.Require<IImageFileReader>(),
                    EWorkflowVisionImageSource.Folder => WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionFolderSource>(),
                    EWorkflowVisionImageSource.AreaCamera or EWorkflowVisionImageSource.LineCamera => WorkflowRuntimeCapabilityRequirement.Require<IVisionAcquisition>(),
                    _ => throw new InvalidOperationException("图像来源类型未定义。")
                };
                return new[] { source, frames };
            })
            .Register(new LoadVisionFileNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IImageFileReader>(), frames)
            .Register(new LoadVisionFolderNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionFolderSource>(), frames)
            // 采集节点只声明中立采集入口：Provider选择、连接复用、互斥和来源元数据都由采集运行时隐藏。
            // 面阵与线扫只在参数绑定和候选过滤上分开，能力要求与输出完全一致。
            .Register(new CaptureAreaFrameNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IVisionAcquisition>(), frames)
            .Register(new CaptureLineScanFrameNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IVisionAcquisition>(), frames)
            .Register(new AnalyzeVisionBlobsNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IBlobAnalyzer>())
            .Register(new AnalyzeVisionColorNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IColorAnalyzer>())
            .Register(new MeasureVisionEdgesNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IEdgeMeasurer>())
            .Register(new LocateVisionTemplateNodeHandler(), node => new[] { ((LocateVisionTemplateNodeModel)node).TemplateSource == EWorkflowVisionTemplateSource.Resource
                ? WorkflowRuntimeCapabilityRequirement.Require<IPreparedVisionTemplateMatcher>() : WorkflowRuntimeCapabilityRequirement.Require<ITemplateLocator>() })
            .Register(new SolveVisionCalibrationNodeHandler()).Register(new MapVisionCoordinateNodeHandler()).Register(new MeasureVisionDistanceNodeHandler())
            .Register(new PreprocessVisionImageNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IImagePreprocessor>(), frames)
            .Register(new ThresholdVisionRegionNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IRegionProcessor>())
            .Register(new MorphVisionRegionNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IRegionProcessor>())
            .Register(new SelectVisionBlobsNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IBlobSelector>())
            .Register(new MeasureVisionCaliperNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<ICaliperMeasurer>())
            .Register(new FitVisionRobustLineNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IRobustLineFitter>())
            .Register(new LocateVisionTemplatePoseNodeHandler(), node => new[] { ((LocateVisionTemplatePoseNodeModel)node).TemplateSource == EWorkflowVisionTemplateSource.Resource
                ? WorkflowRuntimeCapabilityRequirement.Require<IPreparedVisionTemplateMatcher>() : WorkflowRuntimeCapabilityRequirement.Require<ITemplatePoseLocator>() })
            .Register(new MapVisionPoseCoordinateNodeHandler());
    }
}
