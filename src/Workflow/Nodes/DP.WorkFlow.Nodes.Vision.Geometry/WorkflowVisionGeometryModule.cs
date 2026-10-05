using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>独立几何节点包；无需宿主引用节点类或具体引擎。</summary>
public sealed class WorkflowVisionGeometryModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "workflow.vision.geometry";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        WorkflowVisionOutputNames.EnsureRegistered();
        var ports = new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Success) };
        extensions.Nodes
            .Register(WorkflowNodeDescriptor.Create<DefineVisionCoordinateSystemNodeModel, VisionCoordinateDefinition>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<BuildVisionCoordinateSystemNodeModel, VisionCoordinateSystemResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<CreateVisionPointNodeModel, VisionPoint>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<SelectVisionPointNodeModel, VisionPoint>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<TransformVisionPointNodeModel, VisionPoint>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<TransformVisionLineNodeModel, VisionLine>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<GenerateVisionLineNodeModel, VisionLine>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MeasureVisionPointDistanceNodeModel, GeometricDistanceResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MeasureVisionPointLineDistanceNodeModel, GeometricDistanceResult>(ports: ports))
            .Register(WorkflowNodeDescriptor.Create<MeasureVisionLineDistanceNodeModel, GeometricDistanceResult>(ports: ports));
        var frames = WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionFrameScope>();
        var bindings = WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionAlgorithmBindings>();
        var geometry = WorkflowRuntimeCapabilityRequirement.Require<IGeometryMeasurer>();
        extensions.Handlers.Register(new CreateVisionPointNodeHandler(), frames).Register(new SelectVisionPointNodeHandler(), frames)
            .Register(new DefineVisionCoordinateSystemNodeHandler()).Register(new BuildVisionCoordinateSystemNodeHandler(), frames)
            .Register(new TransformVisionPointNodeHandler(), frames).Register(new TransformVisionLineNodeHandler(), frames)
            .Register(new GenerateVisionLineNodeHandler(), frames, bindings, geometry)
            .Register(new MeasureVisionPointDistanceNodeHandler(), frames, bindings, geometry)
            .Register(new MeasureVisionPointLineDistanceNodeHandler(), frames, bindings, geometry)
            .Register(new MeasureVisionLineDistanceNodeHandler(), frames, bindings, geometry);
    }
}
