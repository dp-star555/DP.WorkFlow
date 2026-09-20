namespace DP.WorkFlow;

/// <summary>Implemented by node models that provide deterministic compile-time configuration validation.</summary>
public interface IWorkflowNodeConfigurationValidator
{
    /// <summary>Returns configuration errors that must prevent creation of an execution plan.</summary>
    /// <returns>Empty when the node configuration is valid.</returns>
    IReadOnlyList<string> ValidateConfiguration();
}
