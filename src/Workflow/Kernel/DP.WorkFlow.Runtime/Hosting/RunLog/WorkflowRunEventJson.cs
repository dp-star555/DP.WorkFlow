using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>
/// 运行事件的 JSON 写法（运行日志行尾的回放数据，见 <see cref="WorkflowRunLogLineFormat"/>）：读回来与原事件一致（含小值的类型）。
/// </summary>
internal static class WorkflowRunEventJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        // 中文原样写（控制字符仍转义，行里不会有原样的制表符和换行）。
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TraceValueConverter() }
    };

    /// <summary>把一条事件写成一行 JSON（不含换行）。</summary>
    /// <param name="runEvent">事件。</param>
    public static string Write(WorkflowRunEvent runEvent)
    {
        Guard.NotNull(runEvent);
        return JsonSerializer.Serialize(runEvent, Options);
    }

    /// <summary>读回一行 JSON。</summary>
    /// <param name="line">一行 JSON。</param>
    /// <exception cref="JsonException">不是运行事件。</exception>
    public static WorkflowRunEvent Read(string line)
    {
        Guard.NotNullOrWhiteSpace(line);
        return JsonSerializer.Deserialize<WorkflowRunEvent>(line, Options) ?? throw new JsonException("空的运行事件。");
    }

    // 小值按类型写：读回来仍是原来的类型（耗时是 TimeSpan、计数是 long），日志的耗时和数据显示才和实时一样。
    private sealed class TraceValueConverter : JsonConverter<WorkflowTraceValue>
    {
        public override WorkflowTraceValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var kind = root.TryGetProperty("Kind", out var kindValue) ? (WorkflowTraceValueKind)kindValue.GetInt32() : WorkflowTraceValueKind.Summary;
            var scalar = root.TryGetProperty("Scalar", out var scalarValue) ? ReadScalar(Text("ScalarType"), scalarValue) : null;
            long? length = root.TryGetProperty("Length", out var lengthValue) && lengthValue.ValueKind == JsonValueKind.Number ? lengthValue.GetInt64() : null;
            var truncated = root.TryGetProperty("Truncated", out var truncatedValue) && truncatedValue.ValueKind == JsonValueKind.True;
            return new WorkflowTraceValue(kind, Text("TypeName") ?? string.Empty, Text("Text"), scalar, length, truncated);
        }

        public override void Write(Utf8JsonWriter writer, WorkflowTraceValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Kind", (int)value.Kind);
            writer.WriteString("TypeName", value.TypeName);
            if (value.Text is not null) writer.WriteString("Text", value.Text);
            if (value.Scalar is { } scalar && WriteScalar(writer, scalar) is { } type) writer.WriteString("ScalarType", type);
            if (value.Length is { } length) writer.WriteNumber("Length", length);
            if (value.Truncated) writer.WriteBoolean("Truncated", true);
            writer.WriteEndObject();
        }

        private static string? WriteScalar(Utf8JsonWriter writer, object scalar)
        {
            writer.WritePropertyName("Scalar");
            switch (scalar)
            {
                case bool flag: writer.WriteBooleanValue(flag); return "bool";
                case string text: writer.WriteStringValue(text); return "string";
                case int or long or short or byte or sbyte or ushort or uint:
                    writer.WriteNumberValue(Convert.ToInt64(scalar, CultureInfo.InvariantCulture)); return "long";
                case ulong number: writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture)); return "ulong";
                case float or double: writer.WriteNumberValue(Convert.ToDouble(scalar, CultureInfo.InvariantCulture)); return "double";
                case decimal number: writer.WriteNumberValue(number); return "decimal";
                case TimeSpan span: writer.WriteNumberValue(span.Ticks); return "timespan";
                case Guid id: writer.WriteStringValue(id); return "guid";
                case DateTimeOffset time: writer.WriteStringValue(time); return "datetimeoffset";
                case DateTime time: writer.WriteStringValue(time); return "datetime";
                default: writer.WriteStringValue(Convert.ToString(scalar, CultureInfo.InvariantCulture)); return "string";
            }
        }

        private static object? ReadScalar(string? type, JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : type switch
        {
            "bool" => value.GetBoolean(),
            "long" => value.GetInt64(),
            "ulong" => ulong.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            "double" => value.GetDouble(),
            "decimal" => value.GetDecimal(),
            "timespan" => TimeSpan.FromTicks(value.GetInt64()),
            "guid" => value.GetGuid(),
            "datetimeoffset" => value.GetDateTimeOffset(),
            "datetime" => value.GetDateTime(),
            _ => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
        };
    }
}
