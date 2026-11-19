namespace DP.WorkFlow.Tests;

public sealed class PublicDataManifestTests
{
    private static readonly WorkflowNodeCatalog Catalog = new WorkflowNodeCatalog().RegisterStandardNodes();

    [Fact]
    public void Compile_WarnsForUndeclaredUse()
    {
        var plan = new WorkflowCompiler(Catalog).Compile(Build(new SignalValueSetNodeModel
        {
            Id = "Set", Title = "设置", SignalKey = "Count", ValueType = E_SignalValueType.Int32, LiteralValue = "1"
        }));

        var warning = Assert.Single(plan.Diagnostics, item => item.Code == WorkflowPublicDataManifest.UndeclaredCode);
        Assert.Equal(WorkflowValidationSeverity.Warning, warning.Severity);
        Assert.Equal("Set", warning.NodeId);
    }

    [Fact]
    public void Compile_RejectsUseThatDoesNotMatchManifest()
    {
        var document = Build(new QueueAddNodeModel { Id = "Add", Title = "入队", QueueKey = "Jobs", ValueType = E_SignalValueType.String });
        document.PublicData.Add(WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.Int32));

        var error = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(Catalog).Compile(document));

        Assert.Contains(error.Errors, item => item.Code == WorkflowPublicDataManifest.MismatchCode && item.NodeId == "Add");
    }

    [Fact]
    public void Compile_RejectsDuplicateAndUnparsableDeclarations()
    {
        var document = Build(new SignalSetNodeModel { Id = "Set", Title = "设置", SignalKey = "Ready" });
        document.PublicData.Add(WorkflowPublicDataDeclaration.Signal("Ready"));
        document.PublicData.Add(WorkflowPublicDataDeclaration.Signal("Ready"));
        document.PublicData.Add(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "abc"));

        var error = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(Catalog).Compile(document));

        Assert.Equal(2, error.Errors.Count(item => item.Code == WorkflowPublicDataManifest.InvalidDeclarationCode));
    }

    [Fact]
    public void Compile_RejectsNodesThatUseOneKeyDifferently()
    {
        var document = Build(
            new SignalSetNodeModel { Id = "Set", Title = "设置", SignalKey = "Shared" },
            new QueueAddNodeModel { Id = "Add", Title = "入队", QueueKey = "Shared", ValueType = E_SignalValueType.String });

        var error = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(Catalog).Compile(document));

        Assert.Contains(error.Errors, item => item.Code == WorkflowPublicDataManifest.MismatchCode && item.NodeId == "Add");
    }

    [Fact]
    public async Task Run_DeclaresManifestWithDefaultsBeforeFirstNode()
    {
        var document = Build(new SignalValueSetNodeModel
        {
            Id = "Add", Title = "加一", SignalKey = "Count", ValueType = E_SignalValueType.Int32,
            WriteMode = E_SignalValueWriteMode.Increment, LiteralValue = "1"
        });
        document.PublicData.Add(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "10"));
        var context = new WorkflowContext();
        var engine = new WorkflowEngine(new WorkflowCompiler(Catalog).Compile(document),
            new WorkflowNodeHandlerCatalog().Register(new SignalValueSetNodeHandler()), context);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(11, context.PublicDataCenter.Values.ReadSnapshot("Count")!.Value);
        Assert.True(context.PublicDataCenter.TryGetDeclaration("Count", out _));
    }

    [Fact]
    public async Task Run_FaultsWhenManifestConflictsWithExistingDeclaration()
    {
        var document = Build(new SignalSetNodeModel { Id = "Set", Title = "设置", SignalKey = "Ready" });
        document.PublicData.Add(WorkflowPublicDataDeclaration.Signal("Ready"));
        var center = new WorkflowPublicDataCenter();
        center.Declare(WorkflowPublicDataDeclaration.Value("Ready", E_SignalValueType.String));
        var engine = new WorkflowEngine(new WorkflowCompiler(Catalog).Compile(document),
            new WorkflowNodeHandlerCatalog().Register(new SignalSetNodeHandler()), new WorkflowContext(publicDataCenter: center));

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Contains("Ready", result.Message, StringComparison.Ordinal);
        Assert.Equal(WorkflowPublicDataKind.Value, center.Declarations.Single().Kind);
    }

    [Fact]
    public void Compile_ChecksGlobalBindingsAgainstManifest()
    {
        WorkflowDocument Document(string key, string member)
        {
            var document = Build(new DecisionNodeModel
            {
                Id = "Check", Title = "判断",
                Condition = WorkflowInput<bool>.FromBinding(WorkflowBindingKey.FromPublicData(key, member))
            });
            document.PublicData.Add(WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.Int32));
            document.PublicData.Add(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32));
            return document;
        }
        var compiler = new WorkflowCompiler(Catalog);

        Assert.DoesNotContain(compiler.Compile(Document("Jobs", "HasFirstValue")).Diagnostics,
            item => item.Code.StartsWith("WFPD", StringComparison.Ordinal));
        Assert.Contains(compiler.Compile(Document("Missing", "$")).Diagnostics,
            item => item.Code == WorkflowPublicDataManifest.UndeclaredCode && item.NodeId == "Check");
        Assert.Contains(Assert.Throws<WorkflowCompilationException>(() => compiler.Compile(Document("Jobs", "Nope"))).Errors,
            item => item.Code == WorkflowPublicDataManifest.MismatchCode);
        Assert.Contains(Assert.Throws<WorkflowCompilationException>(() => compiler.Compile(Document("Count", "$"))).Errors,
            item => item.Code == WorkflowPublicDataManifest.MismatchCode);
    }

    [Fact]
    public async Task GlobalBinding_ReadsSignalFromCenter()
    {
        var document = Build(new DecisionNodeModel
        {
            Id = "Check", Title = "判断", ConditionSource = E_DecisionConditionSource.Binding,
            Condition = WorkflowInput<bool>.FromBinding(WorkflowBindingKey.FromPublicData("Ready"))
        });
        document.PublicData.Add(WorkflowPublicDataDeclaration.Signal("Ready", defaultValue: true));
        var engine = new WorkflowEngine(new WorkflowCompiler(Catalog).Compile(document),
            new WorkflowNodeHandlerCatalog().Register(new DecisionNodeHandler()), new WorkflowContext());

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(Assert.IsType<DecisionNodeResult>(engine.FindLatestNodeOutput(engine.RunState.RunId, "Check")!.Value).Value);
    }

    private static WorkflowDocument Build(params IWorkflowNodeModel[] nodes)
    {
        var document = new WorkflowDocument { Name = "公共数据", EntryNodeId = nodes[0].Id };
        foreach (var node in nodes)
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        for (var index = 1; index < nodes.Length; index++)
            document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
            {
                FromNodeId = nodes[index - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[index].Id, ToPort = WorkflowPorts.Input
            });
        return document;
    }
}
