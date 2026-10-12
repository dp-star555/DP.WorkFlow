namespace DP.WorkFlow.Tests;

/// <summary>输出成员随配置变化的节点：编译期校验与绑定候选以节点自己的输出说明为准。</summary>
public sealed class WorkflowDynamicOutputTests
{
    [Fact]
    public void Compile_KeyedMemberPathsFollowTheNodesOwnDescription()
    {
        var group = new GroupNode { Id = "Group", Title = "测量组", Keys = { "a" } };
        var ok = Bound("Ok", new WorkflowBindingKey(group.Id, "Items.a.Flag"));
        var deeper = Bound("Deeper", new WorkflowBindingKey(group.Id, "Items.a.Detail.Flag"));
        var missing = Bound("Missing", new WorkflowBindingKey(group.Id, "Items.zz.Flag"));
        var wrongType = Bound("WrongType", new WorkflowBindingKey(group.Id, "Items.a.Name"));
        var document = Chain(group, ok, deeper, missing, wrongType);

        var exception = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(Catalog()).Compile(document));

        Assert.DoesNotContain(exception.Errors, e => e.NodeId is "Ok" or "Deeper");
        Assert.Contains(exception.Errors, e => e.Code == "WFB004" && e.NodeId == "Missing");
        Assert.Contains(exception.Errors, e => e.Code == "WFB005" && e.NodeId == "WrongType");
    }

    [Fact]
    public void Analyze_CandidatesUseDescribedMembersAndDisplayNames()
    {
        var group = new GroupNode { Id = "Group", Title = "测量组", Keys = { "a", "b" } };
        var consumer = Bound("Consumer", new WorkflowBindingKey(group.Id, "Items.b.Flag"));
        var document = Chain(group, consumer);

        var analysis = new WorkflowBindingAnalyzer(Catalog()).Analyze(document);

        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == WorkflowValidationSeverity.Error);
        var candidates = analysis.GetCandidates(consumer.Id, typeof(bool));
        Assert.Contains(candidates, c => c.MemberPath == "Items.a.Flag" && c.DisplayPath == "测量组/a · Flag");
        Assert.Contains(candidates, c => c.MemberPath == "Items.b.Flag");
        Assert.Contains(candidates, c => c.MemberPath == "AllFound");
    }

    [Fact]
    public void OutputMembers_StaticNodesStillReflectTheirOutputType()
    {
        var decision = new DecisionNodeModel();
        Assert.Equal(typeof(bool), WorkflowOutputMembers.Resolve(decision, typeof(DecisionNodeResult), "Value"));
        Assert.Null(WorkflowOutputMembers.Resolve(decision, typeof(DecisionNodeResult), "Items.a"));
        Assert.Contains(WorkflowOutputMembers.Describe(decision, typeof(DecisionNodeResult)), m => m.Path == "Value" && m.DisplayName == "Value");
        Assert.Contains(WorkflowOutputMembers.OfType(typeof(ItemValue), "Items.a", "a · "), m => m.Path == "Items.a.Detail.Flag" && m.DisplayName == "a · Detail.Flag");
    }

    private static WorkflowNodeCatalog Catalog() => new WorkflowNodeCatalog().RegisterStandardNodes()
        .Register(WorkflowNodeDescriptor.Create<GroupNode, GroupOutput>(ports: new[]
            { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output(WorkflowPorts.Success) }));

    private static DecisionNodeModel Bound(string id, WorkflowBindingKey binding) => new()
    {
        Id = id, Title = id, ConditionSource = E_DecisionConditionSource.Binding, Condition = WorkflowInput<bool>.FromBinding(binding)
    };

    // 测量组 → 各消费节点串成一条链（每个消费节点都由前一个节点的 True 出口进入），保证来源必然先执行。
    private static WorkflowDocument Chain(IWorkflowNodeModel first, params DecisionNodeModel[] consumers)
    {
        var document = new WorkflowDocument { Name = "DynamicOutputs" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = first });
        string previous = first.Id, port = WorkflowPorts.Success;
        foreach (var consumer in consumers)
        {
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = consumer });
            canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = previous, FromPort = port, ToNodeId = consumer.Id, ToPort = WorkflowPorts.Input });
            (previous, port) = (consumer.Id, WorkflowPorts.True);
        }
        document.EntryNodeId = first.Id;
        return document;
    }

    [WorkflowNode("Test.Group")]
    private sealed class GroupNode : WorkflowNodeModel, IWorkflowDynamicOutputNode
    {
        public override string NodeType => "Test.Group";
        public List<string> Keys { get; set; } = new();
        public IReadOnlyList<WorkflowOutputMember> DescribeOutputs() =>
            WorkflowOutputMembers.OfType(typeof(GroupOutput), maxDepth: 1).Where(m => !m.Path.StartsWith("Items", StringComparison.Ordinal))
                .Concat(Keys.SelectMany(k => WorkflowOutputMembers.OfType(typeof(ItemValue), "Items." + k, k + " · ", 1))).ToArray();
    }

    private sealed class GroupOutput
    {
        public bool AllFound { get; init; }
        public object? Items { get; init; }
    }

    private sealed class ItemValue
    {
        public bool Flag { get; init; }
        public string Name { get; init; } = "";
        public ItemValue? Detail { get; init; }
    }
}
