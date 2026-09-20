namespace DP.WorkFlow.Persistence.Json;

internal sealed class LegacyWorkflowDocument
{
    public int SchemaVersion { get; set; }

    public string? Name { get; set; }

    public List<LegacyWorkflowNode> Nodes { get; set; } = new();

    public List<LegacyWorkflowConnection> Connections { get; set; } = new();
}

internal sealed class LegacyWorkflowNode
{
    public string? Id { get; set; }

    public string? NodeKey { get; set; }

    public string? NodeClrType { get; set; }

    public Dictionary<string, string?> Properties { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class LegacyWorkflowConnection
{
    public string? FromNodeId { get; set; }

    public string? ToNodeId { get; set; }

    public string? Label { get; set; }

    public List<LegacyWorkflowPoint> Waypoints { get; set; } = new();
}

internal sealed class LegacyWorkflowPoint
{
    public double X { get; set; }

    public double Y { get; set; }
}
