namespace DP.WorkFlow.Tests;

public sealed class WorkflowPublicDataCenterTests
{
    [Fact]
    public void DeclareAll_AppliesDefaultsAndCreatesQueues()
    {
        var center = new WorkflowPublicDataCenter();
        center.DeclareAll(new[]
        {
            WorkflowPublicDataDeclaration.Signal("Ready", defaultValue: true),
            WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "5"),
            WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.String)
        });

        Assert.True(center.Signals.Read("Ready"));
        Assert.Equal(5, center.Values.ReadSnapshot("Count")!.Value);
        Assert.Equal(E_SignalValueType.String, center.Queues.ReadSnapshot("Jobs")!.ValueType);
        Assert.Equal(3, center.Declarations.Count);
    }

    [Fact]
    public void DeclareAll_RejectsConflictsWithoutApplyingAnything()
    {
        var center = new WorkflowPublicDataCenter();
        center.Declare(WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.Int32));

        var error = Assert.Throws<WorkflowPublicDataConflictException>(() => center.DeclareAll(new[]
        {
            WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32),
            WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.String),
            WorkflowPublicDataDeclaration.Signal("Jobs")
        }));

        Assert.Equal(2, error.Conflicts.Count);
        Assert.False(center.TryGetDeclaration("Count", out _));
        Assert.Single(center.Declarations);
    }

    [Fact]
    public void DeclareAll_AcceptsSameKindAndTypeAgain()
    {
        var center = new WorkflowPublicDataCenter();
        center.Declare(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "1"));
        center.Values.WriteValue("Count", 9);

        center.Declare(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "1"));

        Assert.Equal(9, center.Values.ReadSnapshot("Count")!.Value);
    }

    [Fact]
    public void Declare_RejectsDefaultThatDoesNotParse()
    {
        var center = new WorkflowPublicDataCenter();
        Assert.ThrowsAny<Exception>(() => center.Declare(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "abc")));
        Assert.Empty(center.Declarations);
    }

    [Fact]
    public void Use_AutoDeclaresByDefaultAndThenEnforcesKind()
    {
        var center = new WorkflowPublicDataCenter();
        center.Values.WriteValue("Count", 3);

        Assert.True(center.TryGetDeclaration("Count", out var declaration));
        Assert.Equal(WorkflowPublicDataKind.Value, declaration!.Kind);
        Assert.Equal(E_SignalValueType.Int32, declaration.ValueType);
        Assert.Throws<InvalidOperationException>(() => center.Signals.Write("Count", true));
        Assert.Throws<InvalidOperationException>(() => center.Queues.EnsureExists("Count", E_SignalValueType.Int32));
    }

    [Fact]
    public void Use_RejectsWrongQueueType()
    {
        var center = new WorkflowPublicDataCenter();
        center.Declare(WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.Int32));

        Assert.Throws<InvalidOperationException>(() =>
            center.Queues.EnqueueValue("Jobs", "A", E_SignalValueType.String, false, false));
        Assert.True(center.Queues.EnqueueValue("Jobs", 1, E_SignalValueType.Int32, false, false));
    }

    [Fact]
    public void RequireDeclaration_RejectsUndeclaredKeys()
    {
        var center = new WorkflowPublicDataCenter(requireDeclaration: true);

        Assert.Throws<InvalidOperationException>(() => center.Signals.Write("Ready", true));
        center.Declare(WorkflowPublicDataDeclaration.Signal("Ready"));
        center.Signals.Write("Ready", true);
        Assert.True(center.Signals.Read("Ready"));
    }

    [Fact]
    public void WorkflowContext_HostServicesWinAndChildScopesShareTheCenter()
    {
        var hostQueues = new WorkflowQueueService();
        var context = new WorkflowContext(new SingleServiceProvider(typeof(IWorkflowQueueService), hostQueues));

        Assert.Same(hostQueues, context.Services.GetService(typeof(IWorkflowQueueService)));
        Assert.Same(context.PublicDataCenter.Signals, context.Services.GetService(typeof(IWorkflowSignalService)));
        Assert.Same(context.PublicDataCenter, context.Services.GetService(typeof(WorkflowPublicDataCenter)));

        var child = context.CreateChildScope();
        Assert.Same(context.PublicDataCenter, child.PublicDataCenter);
        Assert.Same(hostQueues, child.Services.GetService(typeof(IWorkflowQueueService)));

        context.PublicDataCenter.Values.WriteValue("Shared", "A");
        var fromChild = (IWorkflowValueSignalService)child.Services.GetService(typeof(IWorkflowValueSignalService))!;
        Assert.Equal("A", fromChild.ReadSnapshot("Shared")!.Value);
    }

    private sealed class SingleServiceProvider(Type type, object instance) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == type ? instance : null;
    }
}
