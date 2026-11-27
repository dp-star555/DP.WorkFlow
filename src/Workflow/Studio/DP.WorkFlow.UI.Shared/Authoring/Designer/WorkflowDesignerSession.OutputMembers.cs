using System.Reflection;

namespace DP.WorkFlow.UI;

/// <summary>节点标准输出中的一个成员。</summary>
/// <param name="Name">成员路径；标准输出为简单值时为 <c>$</c>。</param>
/// <param name="DisplayName">中文显示名称。</param>
/// <param name="ValueType">成员值类型。</param>
public sealed record WorkflowOutputMember(string Name, string DisplayName, Type ValueType);

/// <summary>节点标准输出的成员（节点“输出”页列出它们和运行值；下游参数通过“选来源”绑定）。</summary>
public sealed partial class WorkflowDesignerSession
{
    /// <summary>返回节点标准输出的成员；输出为简单值时返回代表整个值的 <c>$</c>，无输出时返回空集合。</summary>
    /// <param name="nodeId">节点标识。</param>
    public IReadOnlyList<WorkflowOutputMember> GetOutputMembers(string nodeId) =>
        GetOutputMembers(GetCanvasNodeOrThrow(nodeId));

    private IReadOnlyList<WorkflowOutputMember> GetOutputMembers(WorkflowCanvasNode node)
    {
        if (!Catalog.TryGet(node.Node.NodeType, out var descriptor) || descriptor!.OutputType is not { } type)
            return Array.Empty<WorkflowOutputMember>();
        // 输出成员随配置变化的节点（如测量组）：顶层成员，以及按键寻址的子结果下一层成员（如“左边 · 拟合直线”）。
        if (node.Node is IWorkflowDynamicOutputNode dynamic)
        {
            // 复合节点的输出类型跟随子画布中的来源，读取前按当前文档刷新。
            if (node.Node is IWorkflowSubflowNode)
                WorkflowSubflowInterfaces.Refresh(Document, Catalog);
            var described = dynamic.DescribeOutputs();
            var paths = described.Select(member => member.Path).ToHashSet(StringComparer.Ordinal);
            return described
                .Where(member => member.Path != "$" && (!member.Path.Contains('.') || IsKeyedChild(member.Path, paths)))
                .Select(member => new WorkflowOutputMember(member.Path, member.DisplayName == member.Path ? Resolve(type, member.Path) : member.DisplayName, member.Type))
                .ToArray();
        }
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsSimpleOutput(type))
            return new[] { new WorkflowOutputMember("$", "值", type) };
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken)
            .Select(property => new WorkflowOutputMember(property.Name, WorkflowOutputDisplayNames.Resolve(property), property.PropertyType))
            .ToArray();
    }

    // 动态节点按键寻址的子结果（如 Items.m1a2b3c4d）下一层的成员：形如“顶层.键.成员”，且“顶层.键”在说明里。
    private static bool IsKeyedChild(string path, ISet<string> described)
    {
        var parts = path.Split('.');
        return parts.Length == 3 && described.Contains(parts[0] + "." + parts[1]);
    }

    private static string Resolve(Type type, string path) =>
        type.GetProperty(path, BindingFlags.Instance | BindingFlags.Public) is { } property ? WorkflowOutputDisplayNames.Resolve(property) : path;

    private static bool IsSimpleOutput(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string)
        || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan) || type == typeof(Guid);
}
