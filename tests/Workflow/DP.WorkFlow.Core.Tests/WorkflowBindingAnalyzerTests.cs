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
    public void Compile_LoopFromTheEndBackBeforeParallelKeepsMergeAndBindings()
    {
        // 起点 → 取图 → 并行（两个分支）→ 汇聚 → 消费 → 回到取图：首尾回环后流程没有终点，汇聚点仍要识别出来。
        var start = new DelayNodeModel { Id = "Start", Title = "起点" };
        var acquire = new DelayNodeModel { Id = "Acquire", Title = "取图" };
        var parallel = new ParallelAllNodeModel { Id = "Parallel", Title = "并行" };
        var source = new DecisionNodeModel { Id = "Source", Title = "来源" };
        var other = new DelayNodeModel { Id = "Other", Title = "另一分支" };
        var merge = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" };
        var consumer = BoundDecision("Consumer", new WorkflowBindingKey(source.Id, "Value"));
        var document = CreateDocument(
            start, acquire, parallel, source, other, merge, consumer,
            Connect(start.Id, WorkflowPorts.Success, acquire.Id),
            Connect(acquire.Id, WorkflowPorts.Success, parallel.Id),
            Connect(parallel.Id, WorkflowPorts.Branch, source.Id),
            Connect(parallel.Id, WorkflowPorts.Branch, other.Id),
            Connect(source.Id, WorkflowPorts.True, merge.Id),
            Connect(source.Id, WorkflowPorts.False, merge.Id),
            Connect(other.Id, WorkflowPorts.Success, merge.Id),
            Connect(merge.Id, WorkflowPorts.Success, consumer.Id),
            Connect(consumer.Id, WorkflowPorts.True, acquire.Id),
            Connect(consumer.Id, WorkflowPorts.False, acquire.Id));
        document.EntryNodeId = start.Id;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        var analysis = new WorkflowBindingAnalyzer(catalog).Analyze(document);
        Assert.DoesNotContain(analysis.Diagnostics, item => item.Code is "WF021" or "WFB002");
        _ = new WorkflowCompiler(catalog).Compile(document);
    }

    [Fact]
    public void Compile_DisabledBranchIsNotPartOfTheScopeAndItsOutputsAreExplained()
    {
        var parallel = new ParallelAllNodeModel { Id = "Parallel", Title = "并行" };
        var source = new DecisionNodeModel { Id = "Source", Title = "来源" };
        var off = new DecisionNodeModel { Id = "Off", Title = "停用分支" };
        var merge = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" };
        var fromEnabled = BoundDecision("FromEnabled", new WorkflowBindingKey(source.Id, "Value"));
        var fromDisabled = BoundDecision("FromDisabled", new WorkflowBindingKey(off.Id, "Value"));
        var disabledBranch = Connect(parallel.Id, WorkflowPorts.Branch, off.Id);
        disabledBranch.Enabled = false;
        var document = CreateDocument(
            parallel, source, off, merge, fromEnabled, fromDisabled,
            Connect(parallel.Id, WorkflowPorts.Branch, source.Id),
            disabledBranch,
            Connect(source.Id, WorkflowPorts.True, merge.Id),
            Connect(source.Id, WorkflowPorts.False, merge.Id),
            Connect(off.Id, WorkflowPorts.True, merge.Id),
            Connect(off.Id, WorkflowPorts.False, merge.Id),
            Connect(merge.Id, WorkflowPorts.Success, fromEnabled.Id),
            Connect(fromEnabled.Id, WorkflowPorts.True, fromDisabled.Id));
        document.EntryNodeId = parallel.Id;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        var analysis = new WorkflowBindingAnalyzer(catalog).Analyze(document);

        Assert.DoesNotContain(analysis.Diagnostics, d => d.NodeId == fromEnabled.Id);
        var error = Assert.Single(analysis.Diagnostics, d => d.NodeId == fromDisabled.Id);
        Assert.Equal("WFB006", error.Code);
        Assert.Contains("已停用的分支", error.Message);
        Assert.DoesNotContain(analysis.GetCandidates(fromDisabled.Id, typeof(bool)), c => c.SourceNodeId == off.Id);

        // 全部停用：WF023；只剩一个启用分支（另一个停用）时并行仍然有效。
        var enabled = document.CanvasProjection.Connections.Single(c => c.ToNodeId == source.Id && c.FromPort == WorkflowPorts.Branch);
        enabled.Enabled = false;
        var exception = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(catalog, validateBindings: false).Compile(document));
        Assert.Contains(exception.Errors, e => e.Code == "WF023");
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

    [Fact]
    public void Analyze_BindingsInsideSubDocumentAreNotValidatedAgainstParentCanvas()
    {
        var source = new DecisionNodeModel { Id = "ChildSource", Title = "子流程来源" };
        var consumer = BoundDecision("ChildConsumer", new WorkflowBindingKey(source.Id, "Value"));
        var child = CreateDocument(source, consumer, Connect(source.Id, WorkflowPorts.True, consumer.Id));
        child.EntryNodeId = source.Id;
        var composite = new CompositeNode { Id = "Composite", Title = "子流程", SubDocument = child };
        var parent = CreateDocument(composite);
        parent.EntryNodeId = composite.Id;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        var parentAnalysis = new WorkflowBindingAnalyzer(catalog).Analyze(parent);
        var childAnalysis = new WorkflowBindingAnalyzer(catalog).Analyze(child);

        // 子画布的绑定只在子文档内分析：父画布没有 ChildSource 也不报 WFB001。
        Assert.DoesNotContain(parentAnalysis.Diagnostics, item => item.Code == "WFB001");
        Assert.DoesNotContain(childAnalysis.Diagnostics, item => item.Severity == WorkflowValidationSeverity.Error);
    }

    [Fact]
    public void Compile_ReportsParentAndSubDocumentErrorsTogether_WithSubDocumentPath()
    {
        var childConsumer = BoundDecision("ChildConsumer", new WorkflowBindingKey("Nope", "Value"));
        var child = CreateDocument(childConsumer);
        child.EntryNodeId = childConsumer.Id;
        var composite = new CompositeNode { Id = "Composite", Title = "子流程", SubDocument = child };
        var parentConsumer = BoundDecision("ParentConsumer", new WorkflowBindingKey("Missing", "Value"));
        var parent = CreateDocument(composite, parentConsumer, Connect(composite.Id, WorkflowPorts.Success, parentConsumer.Id));
        parent.EntryNodeId = composite.Id;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        var exception = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(catalog).Compile(parent));

        // 父画布有错时子画布照常检查，两边的错误一次报全；子画布的错误带上所在路径。
        Assert.Contains(exception.Errors, error => error.NodeId == parentConsumer.Id && error.PlanPath == "$");
        Assert.Contains(exception.Errors, error => error.NodeId == childConsumer.Id && error.PlanPath == "$/Composite");
    }

    [Fact]
    public void Compile_SubDocumentWarningsAreReportedWithSubDocumentPath()
    {
        var entry = new DecisionNodeModel { Id = "ChildEntry", Title = "入口" };
        var island = new DecisionNodeModel { Id = "Island", Title = "孤立节点" };
        var child = CreateDocument(entry, island);
        child.EntryNodeId = entry.Id;
        var composite = new CompositeNode { Id = "Composite", Title = "子流程", SubDocument = child };
        var parent = CreateDocument(composite);
        parent.EntryNodeId = composite.Id;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

        var plan = new WorkflowCompiler(catalog).Compile(parent);

        var warning = Assert.Single(plan.Diagnostics, item => item.Code == "WF101");
        Assert.Equal(island.Id, warning.NodeId);
        Assert.Equal("$/Composite", warning.PlanPath);
    }

    private sealed class CompositeNode : WorkflowNodeModel, IWorkflowSubDocumentNode
    {
        public override string NodeType => "AnalyzerTest.Composite";

        public WorkflowDocument SubDocument { get; set; } = new();
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
