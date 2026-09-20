namespace DP.WorkFlow;

/// <summary>
/// Declares a fixed-iteration control node whose iteration state is owned by the runtime execution token.
/// </summary>
public interface IWorkflowLoopNode : IWorkflowNodeModel
{
    /// <summary>Gets the number of times the loop body must execute.</summary>
    int Iterations { get; }
}
