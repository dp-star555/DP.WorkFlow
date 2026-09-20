using System.Reflection;

namespace DP.WorkFlow;

/// <summary>Validates node-owned configuration values before an execution plan is created.</summary>
internal static class WorkflowNodeConfigurationValidator
{
    /// <summary>Validates custom node rules and every nested <see cref="WorkflowInput{T}"/> configuration.</summary>
    internal static IReadOnlyList<WorkflowValidationError> Validate(IEnumerable<IWorkflowNodeModel> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var diagnostics = new List<WorkflowValidationError>();
        foreach (var node in nodes)
        {
            if (node is IWorkflowNodeConfigurationValidator custom)
            {
                foreach (var message in custom.ValidateConfiguration().Where(message => !string.IsNullOrWhiteSpace(message)))
                    diagnostics.Add(Error(node, node.GetType().Name, message));
            }
            ValidateObjectGraph(
                node,
                node,
                node.GetType().Name,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                depth: 0,
                diagnostics);
        }
        return diagnostics;
    }

    private static void ValidateObjectGraph(
        IWorkflowNodeModel node,
        object? instance,
        string path,
        ISet<object> visited,
        int depth,
        ICollection<WorkflowValidationError> diagnostics)
    {
        if (instance is WorkflowCanvasModel)
        {
            diagnostics.Add(Error(node, path, "节点配置不得持有 WorkflowCanvasModel；复合节点必须持有 WorkflowDocument。"));
            return;
        }
        if (instance is null || depth > 5 || instance is string or WorkflowDocument || instance.GetType().IsValueType)
            return;
        if (!visited.Add(instance))
            return;

        foreach (var property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
                continue;
            object? value;
            try
            {
                value = property.GetValue(instance);
            }
            catch (TargetInvocationException)
            {
                continue;
            }
            var propertyPath = $"{path}.{property.Name}";
            var propertyType = property.PropertyType;
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(WorkflowInput<>))
            {
                if (value is null)
                {
                    diagnostics.Add(Error(node, propertyPath, "WorkflowInput 不能为空。"));
                    continue;
                }
                var validation = (string?)propertyType.GetMethod(nameof(WorkflowInput<object>.Validate))!.Invoke(value, null);
                if (!string.IsNullOrWhiteSpace(validation))
                    diagnostics.Add(Error(node, propertyPath, validation));
                continue;
            }
            if (value is System.Collections.IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                    ValidateObjectGraph(node, item, $"{propertyPath}[{index++}]", visited, depth + 1, diagnostics);
            }
            else if (propertyType.Namespace?.StartsWith("DP.WorkFlow", StringComparison.Ordinal) == true)
            {
                ValidateObjectGraph(node, value, propertyPath, visited, depth + 1, diagnostics);
            }
        }
    }

    private static WorkflowValidationError Error(IWorkflowNodeModel node, string path, string message) => new(
        "WF030",
        $"节点 {node.Id} 的配置 {path} 无效：{message}",
        node.Id);
}
