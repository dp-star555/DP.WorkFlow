using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>坐标系来源节点：自带坐标系定义并构建本帧坐标系；多个节点可以是同一坐标系的不同来源。</summary>
public interface IWorkflowVisionCoordinateProducerNode
{
    /// <summary>本节点定义的坐标系（ID、名称、版本、单位）。</summary><returns>坐标定义。</returns>
    VisionCoordinateDefinition GetCoordinateDefinition();
    /// <summary>静态解析本节点输出坐标系的完整定义；依赖运行内容（如动态模板图像）而无法静态确定时返回空。</summary>
    /// <param name="nodes">同文档节点。</param><returns>输出定义或空。</returns>
    VisionCoordinateDefinition? ResolveDefinition(IReadOnlyList<IWorkflowNodeModel> nodes);
}
