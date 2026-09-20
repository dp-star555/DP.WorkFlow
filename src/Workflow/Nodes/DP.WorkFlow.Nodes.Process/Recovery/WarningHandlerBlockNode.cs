using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>包含宿主告警切入流程的专用不可连线子画布。</summary>
[WorkflowNode("WarningHandlerBlock", DisplayName = "告警处理块", Category = "8.Process/异常恢复")]
public sealed class WarningHandlerBlockNodeModel : WorkflowNodeModel, IWorkflowSubDocumentNode
{
    private WorkflowDocument _subDocument = new() { Name = "告警处理" };
    /// <summary>初始化默认确认/重试告警模板。</summary>
    public WarningHandlerBlockNodeModel()
    {
        var start = new WarningHandlerStartNodeModel { Id = "WarningHandlerStart", Title = "告警处理起点" };
        var choice = new OperatorChoiceNodeModel { Id = "WarningOperatorChoice", Title = "人工处理选择" };
        var canvas = _subDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = start });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = choice, X = 240 });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = start.Id, FromPort = WorkflowPorts.Success, ToNodeId = choice.Id });
        _subDocument.EntryNodeId = start.Id;
    }
    /// <inheritdoc />
    public override string NodeType => "WarningHandlerBlock";
    /// <inheritdoc />
    [JsonIgnore]
    public WorkflowDocument SubDocument
    {
        get => _subDocument;
        set => _subDocument = value ?? throw new ArgumentNullException(nameof(value));
    }
    /// <summary>子文档节点数。</summary>
    public int ChildNodeCount => SubDocument.Graph.Nodes.Count;
}

/// <summary>阻止告警处理块作为普通主流程节点执行。</summary>
public sealed class WarningHandlerBlockNodeHandler : WorkflowNodeHandler<WarningHandlerBlockNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WarningHandlerBlockNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException($"告警处理块 '{node.Id}' 不能作为主流程普通节点执行。");
    }
}
