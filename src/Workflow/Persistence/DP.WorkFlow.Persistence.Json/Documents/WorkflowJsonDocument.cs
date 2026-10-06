using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow.Persistence.Json;

internal sealed class WorkflowJsonDocument
{
    public int SchemaVersion { get; set; }

    public string Name { get; set; } = string.Empty;

    public string EntryNodeId { get; set; } = string.Empty;

    public List<WorkflowJsonNode> Nodes { get; set; } = new();

    public List<WorkflowJsonConnection> Connections { get; set; } = new();
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

    public WorkflowConnectionState State { get; set; }

    public string? Diagnostic { get; set; }

    public List<WorkflowJsonPoint> Waypoints { get; set; } = new();
}

internal sealed class WorkflowJsonPoint
{
    public double X { get; set; }

    public double Y { get; set; }
}
