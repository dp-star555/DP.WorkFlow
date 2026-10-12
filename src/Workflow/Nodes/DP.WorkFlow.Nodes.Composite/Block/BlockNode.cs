using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>将可复用子流程封装为父流程中的单一节点。</summary>
[WorkflowNode("Block", DisplayName = "子流程块", Category = "Control", Description = "同步运行一个隔离的子流程画布。")]
public sealed class BlockNodeModel : WorkflowNodeModel, IWorkflowSubflowNode, IWorkflowDynamicOutputNode, IWorkflowBindingDeclarationProvider
{
    /// <inheritdoc />
    public override string NodeType => "Block";

    private WorkflowDocument _subDocument = new();

    /// <inheritdoc />
    [JsonIgnore]
    public WorkflowDocument SubDocument
    {
        get => _subDocument;
        set => _subDocument = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    [Browsable(false)]
    public IList<WorkflowSubflowInput> Inputs { get; set; } = new List<WorkflowSubflowInput>();

    /// <inheritdoc />
    [Browsable(false)]
    public IList<WorkflowSubflowOutput> Outputs { get; set; } = new List<WorkflowSubflowOutput>();

    /// <inheritdoc />
    [JsonIgnore]
    [Browsable(false)]
    public IReadOnlyList<WorkflowBindableMember> SubflowOutputs { get; set; } = Array.Empty<WorkflowBindableMember>();

    /// <summary>获取子画布节点数量。</summary>
    [JsonIgnore]
    public int ChildNodeCount => SubDocument.Graph.Nodes.Count;

    /// <inheritdoc />
    public IReadOnlyList<WorkflowBindableMember> DescribeOutputs() =>
        WorkflowSubflowInterfaces.DescribeCompositeOutputs(this, typeof(BlockNodeOutput));

    /// <inheritdoc />
    public IEnumerable<WorkflowDeclaredBinding> GetDeclaredBindings() => WorkflowSubflowInterfaces.DeclareBindings(this);
}
