namespace DP.WorkFlow;

/// <summary>Validates node configuration against peers in the same document before compilation.</summary>
public interface IWorkflowNodeDocumentConfigurationValidator
{
    /// <summary>Returns configuration errors without constructing execution resources.</summary>
    /// <param name="nodes">Nodes in the current document scope.</param><returns>Validation messages.</returns>
    IReadOnlyList<string> ValidateDocumentConfiguration(IReadOnlyList<IWorkflowNodeModel> nodes);
}
