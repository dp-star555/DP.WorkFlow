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

public sealed class WorkflowPublicDataPersistenceTests
{
    [Fact]
    public void PersistentItems_AreSavedOnWriteAndRestoredOnDeclare()
    {
        var store = new MemoryPersistence();
        var first = new WorkflowPublicDataCenter(persistence: store);
        first.DeclareAll(new[]
        {
            WorkflowPublicDataDeclaration.Signal("Ready", persistent: true),
            WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "1", persistent: true),
            WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.Int64, persistent: true),
            WorkflowPublicDataDeclaration.Value("Temp", E_SignalValueType.Int32, "1")
        });
        first.Signals.Write("Ready", true);
        first.Values.WriteValue("Count", 7);
        first.Values.WriteValue("Temp", 9);
        first.Queues.EnqueueValue("Jobs", 10L, E_SignalValueType.Int64, false, false);
        first.Queues.EnqueueValue("Jobs", 20L, E_SignalValueType.Int64, false, false);
        Assert.True(first.Queues.TryDequeueValue("Jobs", E_SignalValueType.Int64, false, out var dequeued));
        Assert.Equal(10L, dequeued);

        Assert.DoesNotContain("Temp", store.Items.Keys);
        var second = new WorkflowPublicDataCenter(persistence: store);
        second.DeclareAll(new[]
        {
            WorkflowPublicDataDeclaration.Signal("Ready", persistent: true),
            WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "1", persistent: true),
            WorkflowPublicDataDeclaration.Queue("Jobs", E_SignalValueType.Int64, persistent: true),
            WorkflowPublicDataDeclaration.Value("Temp", E_SignalValueType.Int32, "1")
        });

        Assert.True(second.Signals.Read("Ready"));
        Assert.Equal(7, second.Values.ReadSnapshot("Count")!.Value);
        Assert.Equal(1, second.Values.ReadSnapshot("Temp")!.Value);
        Assert.Equal(new object[] { 20L }, second.Queues.ReadSnapshot("Jobs")!.Items);
    }

    [Fact]
    public void ChangedType_FallsBackToDefault()
    {
        var store = new MemoryPersistence();
        store.Save(new WorkflowPersistedPublicData("Count", WorkflowPublicDataKind.Value, E_SignalValueType.String, new[] { "abc" }));
        var center = new WorkflowPublicDataCenter(persistence: store);

        center.Declare(WorkflowPublicDataDeclaration.Value("Count", E_SignalValueType.Int32, "5", persistent: true));

        Assert.Equal(5, center.Values.ReadSnapshot("Count")!.Value);
    }

    [Fact]
    public void SaveFailure_IsReportedWithoutBreakingWrites()
    {
        var store = new MemoryPersistence { FailSaves = true };
        var center = new WorkflowPublicDataCenter(persistence: store);
        var messages = new List<string>();
        center.PersistenceFailed += messages.Add;
        center.Declare(WorkflowPublicDataDeclaration.Signal("Ready", persistent: true));

        center.Signals.Write("Ready", true);

        Assert.True(center.Signals.Read("Ready"));
        Assert.Equal(1, center.PersistenceFailureCount);
        Assert.Contains("Ready", Assert.Single(messages), StringComparison.Ordinal);
    }

    private sealed class MemoryPersistence : IWorkflowPublicDataPersistence
    {
        public Dictionary<string, WorkflowPersistedPublicData> Items { get; } = new(StringComparer.Ordinal);
        public bool FailSaves { get; set; }
        public IReadOnlyList<WorkflowPersistedPublicData> Load() => Items.Values.ToArray();
        public void Save(WorkflowPersistedPublicData item)
        {
            if (FailSaves) throw new IOException("disk full");
            Items[item.Key] = item;
        }
    }
}
