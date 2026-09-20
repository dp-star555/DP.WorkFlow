namespace DP.WorkFlow;

/// <summary>指定工作流校验结果的严重程度。</summary>
public enum WorkflowValidationSeverity
{
    /// <summary>不阻止编译，但需要设计器提示用户。</summary>
    Warning,

    /// <summary>结构不安全，必须阻止编译和运行。</summary>
    Error
}

/// <summary>
/// 表示工作流图中的一个结构或绑定校验结果。
/// </summary>
/// <param name="Code">稳定诊断代码，例如 <c>WF001</c> 或 <c>WFB004</c>。</param>
/// <param name="Message">面向流程设计者的诊断说明。</param>
/// <param name="NodeId">可定位到具体节点时使用的节点 ID。</param>
/// <param name="Severity">警告允许生成定义，错误会阻止编译和运行。</param>
public sealed record WorkflowValidationError(
    string Code,
    string Message,
    string? NodeId = null,
    WorkflowValidationSeverity Severity = WorkflowValidationSeverity.Error);

/// <summary>
/// 工作流图结构校验器。
/// </summary>
public sealed class WorkflowGraphValidator
{
    private readonly WorkflowNodeCatalog? _catalog;

    /// <summary>初始化图结构校验器。</summary>
    /// <param name="catalog">节点类型目录；为空时只检查节点、连接和布局基础结构，不校验声明端口与连接基数。</param>
    public WorkflowGraphValidator(WorkflowNodeCatalog? catalog = null)
    {
        _catalog = catalog;
    }

    /// <summary>校验正式文档的语义图、入口和独立布局投影。</summary>
    public IReadOnlyList<WorkflowValidationError> Validate(WorkflowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = ValidateOwnedState(
            document.Graph.Nodes,
            document.Graph.ControlConnections,
            document.Layout.Nodes).ToList();
        if (string.IsNullOrWhiteSpace(document.EntryNodeId))
        {
            errors.Add(new WorkflowValidationError("WF013", "工作流文档必须显式设置 EntryNodeId。"));
        }
        else if (!document.Graph.Nodes.Any(node => string.Equals(node.Id, document.EntryNodeId, StringComparison.Ordinal)))
        {
            errors.Add(new WorkflowValidationError(
                "WF014",
                $"工作流入口节点不存在：{document.EntryNodeId}。",
                document.EntryNodeId));
        }
        return errors;
    }

    private IReadOnlyList<WorkflowValidationError> ValidateOwnedState(
        IReadOnlyList<IWorkflowNodeModel> nodes,
        IReadOnlyList<WorkflowControlConnection> connections,
        IReadOnlyList<WorkflowNodeLayout> layouts)
    {
        var errors = new List<WorkflowValidationError>();
        var validNodes = nodes.Where(node => node is not null).ToArray();

        foreach (var duplicate in validNodes
                     .Where(node => !string.IsNullOrWhiteSpace(node.Id))
                     .GroupBy(node => node.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add(new WorkflowValidationError("WF001", $"节点 ID 重复：{duplicate.Key}。", duplicate.Key));
        }

        foreach (var node in validNodes.Where(node => string.IsNullOrWhiteSpace(node.Id)))
            errors.Add(new WorkflowValidationError("WF002", "节点 ID 不能为空。"));

        foreach (var node in validNodes.Where(node => string.IsNullOrWhiteSpace(node.NodeType)))
            errors.Add(new WorkflowValidationError("WF007", $"节点 {node.Id} 的 NodeType 不能为空。", node.Id));

        foreach (var layout in layouts.Where(layout => layout.Width <= 0 || layout.Height <= 0))
        {
            errors.Add(new WorkflowValidationError(
                "WF008",
                $"节点 {layout.NodeId} 的画布尺寸必须大于 0。",
                layout.NodeId,
                WorkflowValidationSeverity.Warning));
        }

        var nodeIds = validNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var connectionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            if (connection.State == WorkflowConnectionState.Detached)
            {
                errors.Add(new WorkflowValidationError(
                    "WF015",
                    connection.Diagnostic ?? $"连接 {connection.FromNodeId}:{connection.FromPort} -> {connection.ToNodeId}:{connection.ToPort} 已失效。",
                    connection.FromNodeId));
            }
            if (!nodeIds.Contains(connection.FromNodeId))
                errors.Add(new WorkflowValidationError("WF003", $"连接的来源节点不存在：{connection.FromNodeId}。"));
            if (!nodeIds.Contains(connection.ToNodeId))
                errors.Add(new WorkflowValidationError("WF004", $"连接的目标节点不存在：{connection.ToNodeId}。"));
            if (string.IsNullOrWhiteSpace(connection.FromPort))
                errors.Add(new WorkflowValidationError("WF005", $"节点 {connection.FromNodeId} 的出口端口不能为空。", connection.FromNodeId));
            if (string.IsNullOrWhiteSpace(connection.ToPort))
                errors.Add(new WorkflowValidationError("WF009", $"节点 {connection.ToNodeId} 的输入端口不能为空。", connection.ToNodeId));

            var key = $"{connection.FromNodeId}\u001f{connection.FromPort}\u001f{connection.ToNodeId}\u001f{connection.ToPort}";
            if (!connectionKeys.Add(key))
                errors.Add(new WorkflowValidationError("WF006", $"存在重复连接：{connection.FromNodeId}:{connection.FromPort} -> {connection.ToNodeId}:{connection.ToPort}。"));
        }

        if (_catalog is not null)
            ValidateDeclaredPorts(validNodes, connections, errors);

        return errors;
    }

    /// <summary>根据节点目录中的静态或动态端口检查端口存在性和连接基数。</summary>
    /// <param name="nodes">已经完成基础空值筛选的画布节点。</param>
    /// <param name="connections">待校验的画布连接。</param>
    /// <param name="errors">接收端口不存在和连接数超限诊断的集合。</param>
    private void ValidateDeclaredPorts(
        IReadOnlyList<IWorkflowNodeModel> nodes,
        IEnumerable<WorkflowControlConnection> connections,
        ICollection<WorkflowValidationError> errors)
    {
        var nodeMap = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var connectionList = connections
            .Where(connection => connection.State == WorkflowConnectionState.Active)
            .ToArray();
        foreach (var connection in connectionList)
        {
            if (nodeMap.TryGetValue(connection.FromNodeId, out var sourceNode)
                && _catalog!.TryGet(sourceNode.NodeType, out var sourceDescriptor)
                && !sourceDescriptor!.GetPorts(sourceNode).Any(port =>
                    port.Direction == WorkflowPortDirection.Output
                    && string.Equals(port.Key, connection.FromPort, StringComparison.Ordinal)))
            {
                errors.Add(new WorkflowValidationError(
                    "WF010",
                    $"节点 {sourceNode.Id}/{sourceNode.NodeType} 未声明输出端口 {connection.FromPort}。",
                    sourceNode.Id));
            }

            if (nodeMap.TryGetValue(connection.ToNodeId, out var targetNode)
                && _catalog!.TryGet(targetNode.NodeType, out var targetDescriptor)
                && !targetDescriptor!.GetPorts(targetNode).Any(port =>
                    port.Direction == WorkflowPortDirection.Input
                    && string.Equals(port.Key, connection.ToPort, StringComparison.Ordinal)))
            {
                errors.Add(new WorkflowValidationError(
                    "WF011",
                    $"节点 {targetNode.Id}/{targetNode.NodeType} 未声明输入端口 {connection.ToPort}。",
                    targetNode.Id));
            }
        }

        foreach (var node in nodes)
        {
            if (!_catalog!.TryGet(node.NodeType, out var descriptor))
                continue;

            foreach (var port in descriptor!.GetPorts(node))
            {
                var count = port.Direction == WorkflowPortDirection.Output
                    ? connectionList.Count(connection =>
                        string.Equals(connection.FromNodeId, node.Id, StringComparison.Ordinal)
                        && string.Equals(connection.FromPort, port.Key, StringComparison.Ordinal))
                    : connectionList.Count(connection =>
                        string.Equals(connection.ToNodeId, node.Id, StringComparison.Ordinal)
                        && string.Equals(connection.ToPort, port.Key, StringComparison.Ordinal));
                if (count > port.MaxConnections)
                {
                    errors.Add(new WorkflowValidationError(
                        "WF012",
                        $"节点 {node.Id} 的端口 {port.Key} 连接数 {count} 超过上限 {port.MaxConnections}。",
                        node.Id));
                }
            }
        }
    }
}
