namespace DP.WorkFlow.Tests;

public sealed class WorkflowBindingAnalyzerTests
{
    [Fact]
    public void Analyze_KnownOutputPathProducesTypedCandidate()
    {
        var source = new DecisionNodeModel { Id = "Source", Title = "来源" };
        var consumer = BoundDecision("Consumer", new WorkflowBindingKey("Source", "Value"));
        var document = CreateDocument(source, consumer, Connect(source.Id, WorkflowPorts.True, consumer.Id));
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        document.EntryNodeId = source.Id;

        var analysis = new WorkflowBindingAnalyzer(catalog).Analyze(document);

        Assert.DoesNotContain(
            analysis.Diagnostics,
            item => item.Severity == WorkflowValidationSeverity.Error);
        Assert.Contains(
            analysis.GetCandidates(consumer.Id, typeof(bool)),
            candidate => candidate.SourceNodeId == source.Id
                && candidate.MemberPath == "Value"
                && candidate.ValueType == typeof(bool));
    }

    [Fact]
    public void Compile_BindingSourceOnOnlyOneConditionalPathFailsWithWfb002()
    {
        var branch = new DecisionNodeModel { Id = "Branch", Title = "分支" };
        var source = new DecisionNodeModel { Id = "Source", Title = "条件来源" };
        var consumer = BoundDecision("Consumer", new WorkflowBindingKey(source.Id, "Value"));
        var document = CreateDocument(
            branch,
            source,
            consumer,
            Connect(branch.Id, WorkflowPorts.True, source.Id),
            Connect(branch.Id, WorkflowPorts.False, consumer.Id),
            Connect(source.Id, WorkflowPorts.True, consumer.Id));
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        document.EntryNodeId = branch.Id;
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler(catalog).Compile(document));

        Assert.Contains(exception.Errors, error => error.Code == "WFB002" && error.NodeId == consumer.Id);
    }

    [Fact]
    public void Compile_InvalidMemberAndIncompatibleTypeReportSpecificCodes()
    {
        var loop = new LoopNodeModel { Id = "Loop", Title = "循环" };
        var invalidPath = BoundDecision("InvalidPath", new WorkflowBindingKey(loop.Id, "Missing"));
        var wrongType = BoundDecision("WrongType", new WorkflowBindingKey(loop.Id, "CurrentIteration"));
        var document = CreateDocument(
            loop,
            invalidPath,
            wrongType,
            Connect(loop.Id, WorkflowPorts.Completed, invalidPath.Id),
            Connect(invalidPath.Id, WorkflowPorts.True, wrongType.Id));
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        document.EntryNodeId = loop.Id;
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler(catalog).Compile(document));

        Assert.Contains(exception.Errors, error => error.Code == "WFB004" && error.NodeId == invalidPath.Id);
        Assert.Contains(exception.Errors, error => error.Code == "WFB005" && error.NodeId == wrongType.Id);
    }

    [Fact]
    public void Analyze_SourceGuaranteedByParallelMergeIsAvailableAfterMerge()
    {
        var parallel = new ParallelAllNodeModel { Id = "Parallel", Title = "并行" };
        var source = new DecisionNodeModel { Id = "Source", Title = "来源" };
        var other = new DelayNodeModel { Id = "Other", Title = "另一分支" };
        var merge = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" };
        var consumer = BoundDecision("Consumer", new WorkflowBindingKey(source.Id, "Value"));
        var document = CreateDocument(
            parallel,
            source,
            other,
            merge,
            consumer,
            Connect(parallel.Id, WorkflowPorts.Branch, source.Id),
            Connect(parallel.Id, WorkflowPorts.Branch, other.Id),
            Connect(source.Id, WorkflowPorts.True, merge.Id),
            Connect(source.Id, WorkflowPorts.False, merge.Id),
            Connect(other.Id, WorkflowPorts.Success, merge.Id),
            Connect(merge.Id, WorkflowPorts.Success, consumer.Id));
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        document.EntryNodeId = parallel.Id;
        var analysis = new WorkflowBindingAnalyzer(catalog).Analyze(document);

        Assert.DoesNotContain(analysis.Diagnostics, item => item.Code == "WFB002");
        Assert.Contains(
            analysis.GetCandidates(consumer.Id, typeof(bool)),
            candidate => candidate.SourceNodeId == source.Id && candidate.MemberPath == "Value");
    }

    private static DecisionNodeModel BoundDecision(string id, WorkflowBindingKey binding) => new()
    {
        Id = id,
        Title = id,
        ConditionSource = E_DecisionConditionSource.Binding,
        Condition = WorkflowInput<bool>.FromBinding(binding)
    };

    private static WorkflowDocument CreateDocument(
        IWorkflowNodeModel first,
        params object[] items)
    {
        var document = new WorkflowDocument { Name = "BindingAnalysis" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = first });
        foreach (var item in items)
        {
            if (item is IWorkflowNodeModel node)
                canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
            else if (item is WorkflowConnectionModel connection)
                canvas.Connections.Add(connection);
        }
        return document;
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to,
        ToPort = WorkflowPorts.Input
    };
}
