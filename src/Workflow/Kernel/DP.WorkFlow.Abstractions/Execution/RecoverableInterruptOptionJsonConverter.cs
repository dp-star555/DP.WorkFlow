using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>兼容旧版字符串、整数及对象形式的可恢复中断配置。</summary>
public sealed class RecoverableInterruptOptionJsonConverter : JsonConverter<RecoverableInterruptOption>
{
    /// <inheritdoc />
    public override RecoverableInterruptOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new RecoverableInterruptOption();
        if (reader.TokenType == JsonTokenType.Number)
            return new RecoverableInterruptOption { AlarmCode = reader.TryGetInt32(out var number) ? number : 0 };
        if (reader.TokenType == JsonTokenType.String)
            return new RecoverableInterruptOption { AlarmCode = Parse(reader.GetString()) };
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Interrupt 必须是报警编号或包含 AlarmCode 的对象。");

        using var document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty(nameof(RecoverableInterruptOption.AlarmCode), out var alarmCode))
            return new RecoverableInterruptOption();
        var code = alarmCode.ValueKind switch
        {
            JsonValueKind.Number when alarmCode.TryGetInt32(out var value) => value,
            JsonValueKind.String => Parse(alarmCode.GetString()),
            _ => 0
        };
        return new RecoverableInterruptOption { AlarmCode = code };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RecoverableInterruptOption value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(RecoverableInterruptOption.AlarmCode), value?.AlarmCode ?? 0);
        writer.WriteEndObject();
    }

    private static int Parse(string? text) => int.TryParse(text?.Trim(), out var code) ? code : 0;
}
