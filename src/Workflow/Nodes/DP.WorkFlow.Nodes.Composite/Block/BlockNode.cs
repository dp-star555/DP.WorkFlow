using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>将可复用子流程封装为父流程中的单一节点。</summary>
[WorkflowNode("Block", DisplayName = "子流程块", Category = "Control", Description = "同步运行一个隔离的子流程画布。")]
public sealed class BlockNodeModel : WorkflowNodeModel, IWorkflowBlockMappingNode, IWorkflowBindingDeclarationProvider
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

    /// <summary>获取父作用域到子变量的显式输入映射。</summary>
    public IList<BlockInputMapping> InputMappings { get; set; } = new List<BlockInputMapping>();

    /// <summary>获取子作用域到父变量的显式输出映射。</summary>
    public IList<BlockOutputMapping> OutputMappings { get; set; } = new List<BlockOutputMapping>();

    /// <summary>获取子画布节点数量。</summary>
    [JsonIgnore]
    public int ChildNodeCount => SubDocument.Graph.Nodes.Count;

    /// <inheritdoc />
    public IEnumerable<WorkflowDeclaredBinding> GetDeclaredBindings()
    {
        for (var index = 0; index < InputMappings.Count; index++)
        {
            var mapping = InputMappings[index];
            if (mapping.Source == E_BlockInputSource.ParentNodeBinding && mapping.ParentBinding.HasValue)
            {
                yield return new WorkflowDeclaredBinding(
                    mapping.ParentBinding.Value,
                    typeof(object),
                    $"InputMappings[{index}].ParentBinding");
            }
        }
        for (var index = 0; index < OutputMappings.Count; index++)
        {
            var mapping = OutputMappings[index];
            if (mapping.Source == E_BlockOutputSource.ChildNodeBinding && mapping.ChildBinding.HasValue)
            {
                yield return new WorkflowDeclaredBinding(
                    mapping.ChildBinding.Value,
                    typeof(object),
                    $"OutputMappings[{index}].ChildBinding",
                    WorkflowBindingGraphScope.SubDocument);
            }
        }
    }
}
