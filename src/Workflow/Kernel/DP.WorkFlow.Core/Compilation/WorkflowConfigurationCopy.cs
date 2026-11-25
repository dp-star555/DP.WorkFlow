using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>
/// 节点配置自己提供按保存格式复制的方法（例如保留原始配置的未知节点）。一般节点不用实现，默认按保存格式序列化再反序列化。
/// </summary>
public interface IWorkflowNodeConfigurationCopyable
{
    /// <summary>复制一份与原配置无关的节点配置。</summary>
    IWorkflowNodeModel CopyConfiguration();
}

/// <summary>
/// 运行开始时复制配置（R7-c）：在内存中按保存格式序列化再反序列化，保证运行中修改配置不生效，
/// 也保证复制干净（不会把对象引用、缓存带进运行）。配置里只允许能保存的内容：
/// 基本类型、枚举、字符串、列表、<see cref="WorkflowInput{T}"/>、普通嵌套对象。
/// </summary>
public static class WorkflowConfigurationCopy
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>复制整份文档（含全部子画布）。</summary>
    /// <param name="document">要复制的文档。</param>
    /// <returns>与原文档没有共享对象的副本。</returns>
    /// <exception cref="InvalidOperationException">子画布循环引用，或节点配置含有不能保存的内容。</exception>
    public static WorkflowDocument CopyDocument(WorkflowDocument document)
    {
        Guard.NotNull(document);
        return CopyDocument(document, new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>复制一个节点配置（复合节点连同子画布）。</summary>
    /// <param name="node">节点配置。</param>
    /// <returns>与原配置没有共享对象的副本。</returns>
    /// <exception cref="InvalidOperationException">节点配置含有不能保存的内容。</exception>
    public static IWorkflowNodeModel CopyNode(IWorkflowNodeModel node)
    {
        Guard.NotNull(node);
        return CopyNode(node, new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance));
    }

    private static WorkflowDocument CopyDocument(WorkflowDocument source, HashSet<WorkflowDocument> ancestors)
    {
        if (!ancestors.Add(source))
            throw new InvalidOperationException("检测到子文档循环引用，无法编译复合流程。");
        try
        {
            var copy = new WorkflowDocument { Name = source.Name, EntryNodeId = source.EntryNodeId };
            copy.PublicData.AddRange(source.PublicData);
            foreach (var item in source.Nodes)
            {
                var node = new WorkflowCanvasNode
                {
                    Node = CopyNode(item.Node, ancestors),
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height
                };
                foreach (var pair in item.PortSides)
                    node.PortSides[pair.Key] = pair.Value;
                foreach (var key in item.HiddenOutputPorts)
                    node.HiddenOutputPorts.Add(key);
                foreach (var member in item.ExposedOutputMembers)
                    node.ExposedOutputMembers.Add(member);
                copy.Nodes.Add(node);
            }
            foreach (var connection in source.Connections)
            {
                var clone = new WorkflowConnectionModel
                {
                    FromNodeId = connection.FromNodeId,
                    FromPort = connection.FromPort,
                    ToNodeId = connection.ToNodeId,
                    ToPort = connection.ToPort,
                    FromSide = connection.FromSide,
                    ToSide = connection.ToSide,
                    LabelPosition = connection.LabelPosition,
                    Note = connection.Note,
                    Enabled = connection.Enabled,
                    State = connection.State,
                    Diagnostic = connection.Diagnostic
                };
                foreach (var point in connection.Waypoints)
                    clone.Waypoints.Add(point);
                copy.Connections.Add(clone);
            }
            return copy;
        }
        finally
        {
            ancestors.Remove(source);
        }
    }

    private static IWorkflowNodeModel CopyNode(IWorkflowNodeModel node, HashSet<WorkflowDocument> ancestors)
    {
        IWorkflowNodeModel copy;
        if (node is IWorkflowNodeConfigurationCopyable copyable)
            copy = copyable.CopyConfiguration();
        else
        {
            var type = node.GetType();
            try
            {
                var element = JsonSerializer.SerializeToElement(node, type, Options);
                copy = (IWorkflowNodeModel?)element.Deserialize(type, Options)
                    ?? throw new InvalidOperationException("反序列化结果为空。");
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"节点 {node.Id}（{node.NodeType}）的配置不能按保存格式复制：{exception.Message} 配置里只允许能保存的内容。",
                    exception);
            }
        }
        copy.Id = node.Id;
        copy.Title = node.Title;
        if (node is IWorkflowSubDocumentNode composite && copy is IWorkflowSubDocumentNode target)
            target.SubDocument = CopyDocument(composite.SubDocument, ancestors);
        return copy;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = null };
        options.Converters.Add(new BindingKeyConverter());
        return options;
    }

    /// <summary>与保存格式一致：绑定键写成 nodeId|memberPath。</summary>
    private sealed class BindingKeyConverter : JsonConverter<WorkflowBindingKey>
    {
        public override WorkflowBindingKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && WorkflowBindingKey.TryParse(reader.GetString(), out var key)
                ? key
                : throw new JsonException("WorkflowBindingKey 必须是 nodeId|memberPath 格式的字符串。");

        public override void Write(Utf8JsonWriter writer, WorkflowBindingKey value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
