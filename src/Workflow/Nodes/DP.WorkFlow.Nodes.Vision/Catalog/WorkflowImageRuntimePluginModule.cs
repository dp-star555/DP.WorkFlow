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
        WorkflowVisionOutputNames.EnsureRegistered();
        var ports = new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Success), WorkflowPortDescriptor.Failure() };
        return catalog
            .Register(WorkflowNodeDescriptor.Create<AcquireVisionImageNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<AnalyzeVisionBlobsNodeModel, BlobAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<AnalyzeVisionColorNodeModel, ColorAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<SolveVisionCalibrationNodeModel, AffineCalibration>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MapVisionCoordinateNodeModel, Coordinate2D>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<PreprocessVisionImageNodeModel, ImageFrame>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<ThresholdVisionRegionNodeModel, RegionAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<CreateVisionRegionNodeModel, RegionAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MorphVisionRegionNodeModel, RegionAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<SelectVisionBlobsNodeModel, BlobAnalysisResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MeasureVisionCaliperNodeModel, VisionCaliperMeasurement>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<FitVisionRobustLineNodeModel, RobustLineResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<FindVisionLineNodeModel, VisionFindLineResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<FindVisionCircleNodeModel, VisionFindCircleResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<LocateVisionTemplatePoseNodeModel, TemplatePoseResult>(ports: ports));
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
            .Register(new AnalyzeVisionBlobsNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IBlobAnalyzer>())
            .Register(new AnalyzeVisionColorNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IColorAnalyzer>())
            .Register(new SolveVisionCalibrationNodeHandler()).Register(new MapVisionCoordinateNodeHandler())
            .Register(new PreprocessVisionImageNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IImagePreprocessor>(), frames)
            .Register(new ThresholdVisionRegionNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IRegionProcessor>())
            .Register(new CreateVisionRegionNodeHandler(), frames)
            .Register(new MorphVisionRegionNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IRegionProcessor>())
            .Register(new SelectVisionBlobsNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IBlobSelector>())
            .Register(new MeasureVisionCaliperNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<ICaliperMeasurer>())
            .Register(new FitVisionRobustLineNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IRobustLineFitter>())
            .Register(new FindVisionLineNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<ICaliperMeasurer>(), WorkflowRuntimeCapabilityRequirement.Require<IRobustLineFitter>())
            .Register(new FindVisionCircleNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<ICaliperMeasurer>(), WorkflowRuntimeCapabilityRequirement.Require<IRobustCircleFitter>())
            .Register(new LocateVisionTemplatePoseNodeHandler(), node => new[] { ((LocateVisionTemplatePoseNodeModel)node).TemplateSource == EWorkflowVisionTemplateSource.Resource
                ? WorkflowRuntimeCapabilityRequirement.Require<IPreparedVisionTemplateMatcher>() : WorkflowRuntimeCapabilityRequirement.Require<ITemplatePoseLocator>() });
    }
}
