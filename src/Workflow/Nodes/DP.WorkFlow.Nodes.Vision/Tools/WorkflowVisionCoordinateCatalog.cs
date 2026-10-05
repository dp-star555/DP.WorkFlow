using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>坐标定义提供者；跨节点校验只读取定义，不创建本帧矩阵。</summary>
public interface IWorkflowVisionCoordinateDefinitionNode
{
    /// <summary>读取不可变的业务坐标定义。</summary><returns>坐标定义。</returns>
    VisionCoordinateDefinition GetCoordinateDefinition();
}

/// <summary>构建节点显式引用定义，供目录及编译检查使用。</summary>
public interface IWorkflowVisionCoordinateProducerNode
{
    /// <summary>文档内定义绑定。</summary>
    WorkflowInput<VisionCoordinateDefinition> Definition { get; }
}

/// <summary>文档作用域的定义目录；运行矩阵由数据绑定传递，禁止缓存跨帧状态。</summary>
public static class WorkflowVisionCoordinateCatalog
{
    /// <summary>解析稳定定义引用；限定为本作用域的定义提供者输出。</summary>
    /// <param name="nodes">同文档节点。</param><param name="input">定义引用。</param><returns>定义。</returns>
    public static VisionCoordinateDefinition ResolveDefinition(IReadOnlyList<IWorkflowNodeModel> nodes, WorkflowInput<VisionCoordinateDefinition> input)
    {
        if (input is not { Source: WorkflowValueSource.Binding, LiteralValue: null, Binding: { IsPublicData: false } binding }
            || binding.MemberPath is not ("" or "$")) throw new InvalidOperationException("坐标定义必须直接绑定本文档的定义节点输出。");
        var node = nodes.SingleOrDefault(n => n.Id == binding.NodeId);
        return node is IWorkflowVisionCoordinateDefinitionNode provider ? provider.GetCoordinateDefinition()
            : throw new InvalidOperationException("坐标定义节点不存在或未提供稳定定义。");
    }

    /// <summary>拒绝同作用域中重复的业务身份，防止两个定义产生歧义。</summary>
    /// <param name="nodes">节点。</param><param name="id">定义ID。</param><returns>是否重复。</returns>
    public static bool IsDuplicate(IReadOnlyList<IWorkflowNodeModel> nodes, string id) => nodes.OfType<IWorkflowVisionCoordinateDefinitionNode>()
        .Select(TryGet).Count(d => d?.Id == id) > 1;

    private static VisionCoordinateDefinition? TryGet(IWorkflowVisionCoordinateDefinitionNode node)
    {
        try { return node.GetCoordinateDefinition(); } catch (ArgumentException) { return null; }
    }
}
