using DP.Vision.Algorithms;
using DP.WorkFlow;
using External.Intensity;

namespace External.IntensityNodes;

/// <summary>无宿主编译期引用的独立节点模块。</summary>
public sealed class IntensityNodeModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "fixture.intensity.node";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        extensions.Nodes.Register(WorkflowNodeDescriptor.Create<IntensityNode, int>(ports: new[] { WorkflowPortDescriptor.Output() }));
        extensions.Handlers.Register(new Handler(), WorkflowRuntimeCapabilityRequirement.Require<IIntensityOffset>());
    }
    private sealed class Handler : WorkflowNodeHandler<IntensityNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(IntensityNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var result = context.GetRequiredCapability<IWorkflowVisionAlgorithmBindings>()
                .Invoke<IIntensityOffset, int>(context, "transform", operation => operation.Apply(node.Value), cancellationToken);
            return ValueTask.FromResult(NodeExecutionResult.Complete(output: result));
        }
    }
}

/// <summary>节点只依赖算法契约，保存明确选择和初始化参数。</summary>
[WorkflowNode("Fixture.Intensity", DisplayName = "插件灰度变换", Category = "插件验收")]
public sealed class IntensityNode : WorkflowNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <inheritdoc/>
    public override string NodeType => "Fixture.Intensity";
    /// <summary>输入值。</summary>
    public int Value { get; set; } = 10;
    /// <summary>节点选择，独立保存初始化设置。</summary>
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "fixture.add" };
    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("transform", typeof(IIntensityOffset), Algorithm) };
}
