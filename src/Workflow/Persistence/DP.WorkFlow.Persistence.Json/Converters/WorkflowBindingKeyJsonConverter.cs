using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow.Persistence.Json;

/// <summary>
/// 将 <see cref="WorkflowBindingKey"/> 以稳定的 nodeId|memberPath 字符串保存。
/// </summary>
public sealed class WorkflowBindingKeyJsonConverter : JsonConverter<WorkflowBindingKey>
{
    /// <inheritdoc />
    public override WorkflowBindingKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && WorkflowBindingKey.TryParse(reader.GetString(), out var key))
        {
            return key;
        }

        throw new JsonException("WorkflowBindingKey 必须是 nodeId|memberPath 格式的字符串。");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WorkflowBindingKey value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
