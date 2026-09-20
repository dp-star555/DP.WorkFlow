namespace DP.WorkFlow;

/// <summary>Marks a node that dispatches every target connected to its Branch control outcome.</summary>
public interface IWorkflowParallelForkNode : IWorkflowNodeModel
{
}

/// <summary>Marks the explicit convergence boundary of a structured parallel scope.</summary>
public interface IWorkflowParallelJoinNode : IWorkflowNodeModel
{
}
