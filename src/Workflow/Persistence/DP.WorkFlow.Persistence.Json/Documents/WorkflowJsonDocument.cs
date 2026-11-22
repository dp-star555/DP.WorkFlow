using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow.Persistence.Json;

internal sealed class WorkflowJsonDocument
{
    public int SchemaVersion { get; set; }

    public string Name { get; set; } = string.Empty;

    public string EntryNodeId { get; set; } = string.Empty;

    // Schema 5 起根文档写出公共数据清单（子画布不写）；旧文件没有该字段，打开时按节点推断。
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WorkflowJsonPublicData>? PublicData { get; set; }

    public List<WorkflowJsonNode> Nodes { get; set; } = new();

    public List<WorkflowJsonConnection> Connections { get; set; } = new();
}

internal sealed class WorkflowJsonPublicData
{
    public string Key { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowPublicDataKind Kind { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public E_SignalValueType ValueType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Persistent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ResetOnRecovery { get; set; }
}

internal sealed class WorkflowJsonNode
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string NodeType { get; set; } = string.Empty;

    public int NodeVersion { get; set; } = 1;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 180;

    public double Height { get; set; } = 60;

    public Dictionary<string, WorkflowPortSide> PortSides { get; set; } = new(StringComparer.Ordinal);

    public List<string> HiddenOutputPorts { get; set; } = new();

    public List<string> ExposedOutputMembers { get; set; } = new();

    public JsonElement Config { get; set; }

    public WorkflowJsonDocument? SubDocument { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowJsonDocument? SubCanvas { get; set; }
}

internal sealed class WorkflowJsonConnection
{
    public string FromNodeId { get; set; } = string.Empty;

    public string FromPort { get; set; } = WorkflowPorts.Success;

    public string ToNodeId { get; set; } = string.Empty;

    public string ToPort { get; set; } = WorkflowPorts.Input;

    public WorkflowPortSide? FromSide { get; set; }

    public WorkflowPortSide? ToSide { get; set; }

    public double LabelPosition { get; set; } = 0.5;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    // 旧文件没有该字段，按启用读入；只在停用时写出，保持旧文件格式不变。
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool? Disabled { get; set; }

    public List<WorkflowJsonPoint> Waypoints { get; set; } = new();
}

internal sealed class WorkflowJsonPoint
{
    public double X { get; set; }

    public double Y { get; set; }
}
