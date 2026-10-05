namespace DP.WorkFlow;

/// <summary>
/// Represents the editable and persistable intent of one workflow.
/// The semantic graph and designer layout are exposed as separate projections.
/// </summary>
public sealed class WorkflowDocument
{
    private readonly WorkflowDocumentState _state = new();

    /// <summary>Creates an empty workflow document as the sole owner of graph and layout state.</summary>
    public WorkflowDocument()
    {
        Graph = new WorkflowGraph(_state.Nodes, _state.Connections);
        Layout = new WorkflowLayout(_state.Nodes, _state.Connections);
        CanvasProjection = new WorkflowCanvasModel(_state);
    }

    /// <summary>Gets or sets the workflow display name.</summary>
    public string Name
    {
        get => _state.Name;
        set => _state.Name = value ?? string.Empty;
    }

    /// <summary>Gets or sets the stable ID of the root control-flow entry node.</summary>
    public string EntryNodeId { get; set; } = string.Empty;

    /// <summary>Gets the semantic control graph. Layout changes never alter this projection.</summary>
    public WorkflowGraph Graph { get; }

    /// <summary>Gets the designer-only node and connection layout projection.</summary>
    public WorkflowLayout Layout { get; }

    /// <summary>
    /// Gets the transitional canvas projection used by the current designer controls.
    /// New compilation, persistence, and runtime-host interfaces consume the document itself.
    /// </summary>
    public WorkflowCanvasModel CanvasProjection { get; }
}

/// <summary>Provides the semantic nodes and control connections of a workflow document.</summary>
public sealed class WorkflowGraph
{
    private readonly IReadOnlyList<WorkflowCanvasNode> _nodes;
    private readonly IReadOnlyList<WorkflowConnectionModel> _connections;

    internal WorkflowGraph(
        IReadOnlyList<WorkflowCanvasNode> nodes,
        IReadOnlyList<WorkflowConnectionModel> connections)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    /// <summary>Gets node configurations in document order.</summary>
    public IReadOnlyList<IWorkflowNodeModel> Nodes =>
        Array.AsReadOnly(_nodes.Select(item => item.Node).ToArray());

    /// <summary>Gets control connections in document order without any routing geometry.</summary>
    public IReadOnlyList<WorkflowControlConnection> ControlConnections =>
        Array.AsReadOnly(_connections.Select(connection => new WorkflowControlConnection(
            connection.FromNodeId,
            connection.FromPort,
            connection.ToNodeId,
            connection.ToPort,
            connection.State,
            connection.Diagnostic)).ToArray());
}

/// <summary>Describes one semantic control transition between node ports.</summary>
/// <param name="FromNodeId">Source node ID.</param>
/// <param name="FromPort">Source control outcome key.</param>
/// <param name="ToNodeId">Target node ID.</param>
/// <param name="ToPort">Target control input key.</param>
/// <param name="State">Whether the connection currently satisfies both port contracts.</param>
/// <param name="Diagnostic">Reason retained for a detached connection.</param>
public sealed record WorkflowControlConnection(
    string FromNodeId,
    string FromPort,
    string ToNodeId,
    string ToPort,
    WorkflowConnectionState State = WorkflowConnectionState.Active,
    string? Diagnostic = null);

/// <summary>Provides designer layout without exposing node configuration as layout state.</summary>
public sealed class WorkflowLayout
{
    private readonly IReadOnlyList<WorkflowCanvasNode> _nodes;
    private readonly IReadOnlyList<WorkflowConnectionModel> _connections;

    internal WorkflowLayout(
        IReadOnlyList<WorkflowCanvasNode> nodes,
        IReadOnlyList<WorkflowConnectionModel> connections)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    /// <summary>Gets node bounds and per-instance port presentation in document order.</summary>
    public IReadOnlyList<WorkflowNodeLayout> Nodes =>
        Array.AsReadOnly(_nodes.Select(item => new WorkflowNodeLayout(
            item.Node.Id,
            item.X,
            item.Y,
            item.Width,
            item.Height,
            new Dictionary<string, WorkflowPortSide>(item.PortSides, StringComparer.Ordinal),
            item.HiddenOutputPorts.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            item.ExposedOutputMembers.OrderBy(key => key, StringComparer.Ordinal).ToArray())).ToArray());

    /// <summary>Gets connection routing geometry in the same order as semantic control connections.</summary>
    public IReadOnlyList<WorkflowConnectionLayout> Connections =>
        Array.AsReadOnly(_connections.Select((connection, index) => new WorkflowConnectionLayout(
            index,
            connection.FromSide,
            connection.ToSide,
            connection.LabelPosition,
            connection.Waypoints.ToArray())).ToArray());
}

/// <summary>Describes designer presentation for one node.</summary>
/// <param name="NodeId">Stable node ID.</param>
/// <param name="X">Left coordinate.</param>
/// <param name="Y">Top coordinate.</param>
/// <param name="Width">Displayed width.</param>
/// <param name="Height">Displayed height.</param>
/// <param name="PortSides">Per-instance port-side overrides.</param>
/// <param name="HiddenOutputPorts">Output ports hidden by the designer.</param>
/// <param name="ExposedOutputMembers">Standard-output members shown as data ports.</param>
public sealed record WorkflowNodeLayout(
    string NodeId,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyDictionary<string, WorkflowPortSide> PortSides,
    IReadOnlyList<string> HiddenOutputPorts,
    IReadOnlyList<string>? ExposedOutputMembers = null);

/// <summary>Describes designer routing geometry for one control connection.</summary>
/// <param name="ConnectionIndex">Connection order used to associate this layout with the semantic graph.</param>
/// <param name="FromSide">Optional source-side override.</param>
/// <param name="ToSide">Optional target-side override.</param>
/// <param name="LabelPosition">Normalized label position.</param>
/// <param name="Waypoints">Manual orthogonal route points.</param>
public sealed record WorkflowConnectionLayout(
    int ConnectionIndex,
    WorkflowPortSide? FromSide,
    WorkflowPortSide? ToSide,
    double LabelPosition,
    IReadOnlyList<WorkflowPoint> Waypoints);

/// <summary>Document-owned mutable state shared with transitional read/write projections.</summary>
internal sealed class WorkflowDocumentState
{
    internal string Name { get; set; } = string.Empty;

    internal List<WorkflowCanvasNode> Nodes { get; } = new();

    internal List<WorkflowConnectionModel> Connections { get; } = new();
}
