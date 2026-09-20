namespace DP.WorkFlow;

/// <summary>描述一个节点在本次配置下执行前必须由宿主提供的强类型运行能力。</summary>
public sealed record WorkflowRuntimeCapabilityRequirement
{
    /// <summary>创建运行能力要求。</summary>
    /// <param name="capabilityType">宿主能力的稳定接口类型。</param>
    /// <param name="description">面向宿主和诊断界面的可选用途说明。</param>
    public WorkflowRuntimeCapabilityRequirement(Type capabilityType, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(capabilityType);
        if (capabilityType.ContainsGenericParameters)
            throw new ArgumentException("运行能力不能是开放泛型类型。", nameof(capabilityType));
        CapabilityType = capabilityType;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>获取宿主必须能够解析的能力类型。</summary>
    public Type CapabilityType { get; }

    /// <summary>获取该能力在节点执行中的可选用途说明。</summary>
    public string? Description { get; }

    /// <summary>创建一个强类型运行能力要求。</summary>
    /// <typeparam name="TCapability">宿主需要提供的能力类型。</typeparam>
    /// <param name="description">面向宿主和诊断界面的可选用途说明。</param>
    /// <returns>对应 <typeparamref name="TCapability"/> 的要求。</returns>
    public static WorkflowRuntimeCapabilityRequirement Require<TCapability>(string? description = null)
        where TCapability : class => new(typeof(TCapability), description);
}

/// <summary>表示一个节点 Handler 及其按当前冻结配置解析出的运行能力要求。</summary>
/// <param name="Handler">唯一匹配的节点 Handler。</param>
/// <param name="RequiredCapabilities">执行该节点前必须存在的宿主能力。</param>
public sealed record WorkflowNodeHandlerResolution(
    IWorkflowNodeHandler Handler,
    IReadOnlyList<WorkflowRuntimeCapabilityRequirement> RequiredCapabilities);
