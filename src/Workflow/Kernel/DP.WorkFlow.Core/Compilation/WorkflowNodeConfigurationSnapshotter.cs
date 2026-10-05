using System.Collections;
using System.Reflection;

namespace DP.WorkFlow;

/// <summary>
/// Captures detached node-configuration object graphs so editable canvas models never leak into execution definitions.
/// </summary>
public static class WorkflowNodeConfigurationSnapshotter
{
    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
        "MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("无法解析 Object.MemberwiseClone。 ");

    /// <summary>Creates a detached snapshot while preserving concrete plug-in model types.</summary>
    /// <param name="node">The editable node configuration.</param>
    /// <returns>A deep configuration snapshot.</returns>
    public static IWorkflowNodeModel Capture(IWorkflowNodeModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return (IWorkflowNodeModel)CloneValue(
            node,
            new Dictionary<object, object>(ReferenceEqualityComparer.Instance))!;
    }

    /// <summary>Restores a detached snapshot into an existing node instance while preserving its object identity.</summary>
    /// <param name="target">The node instance observed by the designer and external callers.</param>
    /// <param name="snapshot">A snapshot previously returned by <see cref="Capture"/>.</param>
    public static void Restore(IWorkflowNodeModel target, IWorkflowNodeModel snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (target.GetType() != snapshot.GetType())
            throw new ArgumentException("节点配置快照类型与目标节点类型不一致。", nameof(snapshot));

        var detached = Capture(snapshot);
        foreach (var field in EnumerateInstanceFields(target.GetType()))
        {
            try
            {
                field.SetValue(target, field.GetValue(detached));
            }
            catch (FieldAccessException exception)
            {
                throw new InvalidOperationException(
                    $"节点配置字段 {target.GetType().FullName}.{field.Name} 无法恢复。",
                    exception);
            }
        }
    }

    private static object? CloneValue(object? source, IDictionary<object, object> visited)
    {
        if (source is null)
            return null;

        var type = source.GetType();
        if (IsSharedImmutable(type, source))
            return source;
        if (visited.TryGetValue(source, out var existing))
            return existing;
        if (source is WorkflowDocument document)
            return CloneDocument(document, visited);
        if (source is WorkflowCanvasModel)
            throw new InvalidOperationException("节点配置不得持有 WorkflowCanvasModel；复合节点必须持有 WorkflowDocument。");
        if (type.IsArray)
            return CloneArray((Array)source, visited);
        if (source is IDictionary dictionary)
            return CloneDictionary(dictionary, type, visited);
        if (source is IList list)
            return CloneList(list, type, visited);

        var clone = MemberwiseCloneMethod.Invoke(source, null)
            ?? throw new InvalidOperationException($"无法复制节点配置类型 {type.FullName}。");
        visited[source] = clone;
        foreach (var field in EnumerateInstanceFields(type))
        {
            var fieldValue = field.GetValue(source);
            var clonedFieldValue = CloneValue(fieldValue, visited);
            if (ReferenceEquals(fieldValue, clonedFieldValue))
                continue;
            try
            {
                field.SetValue(clone, clonedFieldValue);
            }
            catch (FieldAccessException exception)
            {
                throw new InvalidOperationException(
                    $"节点配置字段 {type.FullName}.{field.Name} 无法生成独立快照。请将其建模为可复制配置值。",
                    exception);
            }
        }
        return clone;
    }

    private static WorkflowDocument CloneDocument(
        WorkflowDocument source,
        IDictionary<object, object> visited)
    {
        var clone = new WorkflowDocument
        {
            Name = source.Name,
            EntryNodeId = source.EntryNodeId
        };
        visited[source] = clone;
        visited[source.CanvasProjection] = clone.CanvasProjection;
        PopulateCanvas(source.CanvasProjection, clone.CanvasProjection, visited);
        return clone;
    }

    private static void PopulateCanvas(
        WorkflowCanvasModel source,
        WorkflowCanvasModel clone,
        IDictionary<object, object> visited)
    {
        foreach (var item in source.Nodes)
        {
            var clonedNode = new WorkflowCanvasNode
            {
                Node = (IWorkflowNodeModel)CloneValue(item.Node, visited)!,
                X = item.X,
                Y = item.Y,
                Width = item.Width,
                Height = item.Height
            };
            visited[item] = clonedNode;
            foreach (var pair in item.PortSides)
                clonedNode.PortSides[pair.Key] = pair.Value;
            foreach (var portKey in item.HiddenOutputPorts)
                clonedNode.HiddenOutputPorts.Add(portKey);
            foreach (var member in item.ExposedOutputMembers)
                clonedNode.ExposedOutputMembers.Add(member);
            clone.Nodes.Add(clonedNode);
        }
        foreach (var connection in source.Connections)
        {
            var clonedConnection = new WorkflowConnectionModel
            {
                FromNodeId = connection.FromNodeId,
                FromPort = connection.FromPort,
                ToNodeId = connection.ToNodeId,
                ToPort = connection.ToPort,
                FromSide = connection.FromSide,
                ToSide = connection.ToSide,
                LabelPosition = connection.LabelPosition,
                State = connection.State,
                Diagnostic = connection.Diagnostic
            };
            visited[connection] = clonedConnection;
            foreach (var waypoint in connection.Waypoints)
                clonedConnection.Waypoints.Add(waypoint);
            clone.Connections.Add(clonedConnection);
        }
    }

    private static Array CloneArray(Array source, IDictionary<object, object> visited)
    {
        if (source.Rank != 1)
            throw new NotSupportedException($"节点配置数组 {source.GetType().FullName} 必须是一维数组。");
        var clone = Array.CreateInstance(source.GetType().GetElementType()!, source.Length);
        visited[source] = clone;
        for (var index = 0; index < source.Length; index++)
            clone.SetValue(CloneValue(source.GetValue(index), visited), index);
        return clone;
    }

    private static object CloneDictionary(
        IDictionary source,
        Type type,
        IDictionary<object, object> visited)
    {
        var clone = CreateCollection(type) as IDictionary
            ?? throw new InvalidOperationException($"节点配置字典 {type.FullName} 必须提供无参数构造函数。");
        visited[source] = clone;
        foreach (DictionaryEntry entry in source)
        {
            var key = CloneValue(entry.Key, visited)
                ?? throw new InvalidOperationException($"节点配置字典 {type.FullName} 包含空键。");
            clone.Add(key, CloneValue(entry.Value, visited));
        }
        return clone;
    }

    private static object CloneList(
        IList source,
        Type type,
        IDictionary<object, object> visited)
    {
        var clone = CreateCollection(type) as IList
            ?? throw new InvalidOperationException($"节点配置集合 {type.FullName} 必须提供无参数构造函数。");
        visited[source] = clone;
        foreach (var item in source)
            clone.Add(CloneValue(item, visited));
        return clone;
    }

    private static object? CreateCollection(Type type)
    {
        try
        {
            return Activator.CreateInstance(type, nonPublic: true);
        }
        catch (MissingMethodException)
        {
            return null;
        }
    }

    private static IEnumerable<FieldInfo> EnumerateInstanceFields(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var field in current.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return field;
            }
        }
    }

    private static bool IsSharedImmutable(Type type, object value) =>
        type.IsValueType
        || value is string or Type or Uri or Version
        || value is Delegate
        || type == typeof(System.Text.Json.JsonDocument);
}
