using System.Text.Json;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowPluginLoaderTests
{
    [Fact]
    public void LoadModules_UsesManifestGroupAndCreatesPublicModules()
    {
        var root = CreatePluginRoot("demo.plugin", WorkflowPluginModuleGroups.Studio);
        try
        {
            var modules = new WorkflowPluginLoader()
                .LoadModules<ITestWorkflowPluginModule>(root, WorkflowPluginModuleGroups.Studio);

            var module = Assert.Single(modules);
            Assert.Equal("demo.module", module.ModuleId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RuntimePluginCatalog_LoadsModulesThroughTheSameManifest()
    {
        var root = CreatePluginRoot("runtime.plugin", WorkflowPluginModuleGroups.Runtime);
        try
        {
            var catalog = new WorkflowRuntimePluginCatalog(
                new WorkflowNodeCatalog(),
                new WorkflowNodeHandlerCatalog());

            var count = catalog.LoadPlugins(root);

            Assert.Equal(1, count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RuntimePluginCatalog_FreezeFailureLeavesEverythingUnfrozenAndRepairable()
    {
        var nodes = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<TestRuntimeNode>());
        var handlers = new WorkflowNodeHandlerCatalog();
        var catalog = new WorkflowRuntimePluginCatalog(nodes, handlers);

        var error = Assert.Throws<InvalidOperationException>(() => catalog.Freeze());

        Assert.Contains("没有已注册的处理器", error.Message);
        // 冻结失败不得留下"已冻结"的外观：三个目录都必须保持可变。
        Assert.False(catalog.IsFrozen);
        Assert.False(nodes.IsFrozen);
        Assert.False(handlers.IsFrozen);

        // 目录仍然可用：补上缺失的处理器后必须能真正完成冻结。
        handlers.Register(new TestRuntimeNodeHandler());
        catalog.Freeze();

        Assert.True(catalog.IsFrozen);
        Assert.True(nodes.IsFrozen);
        Assert.True(handlers.IsFrozen);
    }

    [Fact]
    public void RuntimePluginCatalog_FreezeFailureIsReportedAgainOnRetry()
    {
        var nodes = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<TestRuntimeNode>());
        var catalog = new WorkflowRuntimePluginCatalog(nodes, new WorkflowNodeHandlerCatalog());

        Assert.Throws<InvalidOperationException>(() => catalog.Freeze());

        // 重试必须重新执行校验并再次报错，而不是因为"已冻结"被跳过。
        var retry = Assert.Throws<InvalidOperationException>(() => catalog.Freeze());

        Assert.Contains("没有已注册的处理器", retry.Message);
        Assert.False(catalog.IsFrozen);
    }

    [Fact]
    public void RuntimePluginCatalog_FreezeRejectsAmbiguousHandlersWithoutFreezing()
    {
        var nodes = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<TestRuntimeNode>());
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new TestRuntimeNodeHandler())
            .Register(new TestRuntimeNodeHandler());
        var catalog = new WorkflowRuntimePluginCatalog(nodes, handlers);

        var error = Assert.Throws<InvalidOperationException>(() => catalog.Freeze());

        Assert.Contains("匹配到多个处理器", error.Message);
        // 重复注册无法撤销，只能改用全新目录重建；此时必须能从 IsFrozen 看出目录不可用。
        Assert.False(catalog.IsFrozen);
        Assert.False(nodes.IsFrozen);
        Assert.False(handlers.IsFrozen);
    }

    [Fact]
    public void NodeCatalog_FreezeRejectsFactoryModelMismatch()
    {
        var nodes = new WorkflowNodeCatalog().Register(new WorkflowNodeDescriptor(
            "Test.Runtime",
            1,
            typeof(TestRuntimeNode),
            static () => new OtherRuntimeNode()));

        var error = Assert.Throws<InvalidOperationException>(() => nodes.Freeze());

        Assert.Contains(typeof(TestRuntimeNode).FullName!, error.Message);
        Assert.Contains(typeof(OtherRuntimeNode).FullName!, error.Message);
    }

    [Fact]
    public void LoadModules_RejectsManifestGroupWithoutExpectedModuleInterface()
    {
        var root = CreatePluginRoot("invalid.group", WorkflowPluginModuleGroups.Studio);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                new WorkflowPluginLoader().LoadModules<INotImplementedPluginModule>(
                    root,
                    WorkflowPluginModuleGroups.Studio));

            Assert.Contains("没有实现", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadModules_DoesNotLoadAssemblyFromUnrequestedPlatformGroup()
    {
        var root = CreatePluginRoot("wpf.only", WorkflowPluginModuleGroups.Wpf);
        try
        {
            var modules = new WorkflowPluginLoader()
                .LoadModules<ITestWorkflowPluginModule>(root, WorkflowPluginModuleGroups.WinForms);

            Assert.Empty(modules);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadModules_RejectsMissingPluginDependency()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CreatePluginPackage(
                root,
                "dependent",
                "dependent.plugin",
                WorkflowPluginModuleGroups.Studio,
                new[] { "missing.plugin" });

            var error = Assert.Throws<InvalidOperationException>(() =>
                new WorkflowPluginLoader().LoadModules<ITestWorkflowPluginModule>(
                    root,
                    WorkflowPluginModuleGroups.Studio));

            Assert.Contains("缺少依赖", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadModules_RejectsCyclicPluginDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CreatePluginPackage(root, "first", "first.plugin", WorkflowPluginModuleGroups.Studio, new[] { "second.plugin" });
            CreatePluginPackage(root, "second", "second.plugin", WorkflowPluginModuleGroups.Studio, new[] { "first.plugin" });

            var error = Assert.Throws<InvalidOperationException>(() =>
                new WorkflowPluginLoader().LoadModules<ITestWorkflowPluginModule>(
                    root,
                    WorkflowPluginModuleGroups.Studio));

            Assert.Contains("依赖存在循环", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadModules_RejectsUnsupportedManifestVersion()
    {
        var root = CreatePluginRoot("future.plugin", WorkflowPluginModuleGroups.Studio);
        try
        {
            var manifestPath = Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories).Single();
            var manifest = JsonSerializer.Deserialize<WorkflowPluginManifest>(File.ReadAllText(manifestPath))!;
            manifest.ManifestVersion = 2;
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

            var error = Assert.Throws<InvalidOperationException>(() =>
                new WorkflowPluginLoader().LoadModules<ITestWorkflowPluginModule>(
                    root,
                    WorkflowPluginModuleGroups.Studio));

            Assert.Contains("不受支持的契约版本", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadModules_RejectsDuplicatePluginIds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CreatePluginPackage(root, "first", "duplicate", WorkflowPluginModuleGroups.Studio);
            CreatePluginPackage(root, "second", "duplicate", WorkflowPluginModuleGroups.Studio);

            var error = Assert.Throws<InvalidOperationException>(() =>
                new WorkflowPluginLoader().LoadModules<ITestWorkflowPluginModule>(
                    root, WorkflowPluginModuleGroups.Studio));

            Assert.Contains("多个 Manifest", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadModules_RejectsAssemblyPathOutsidePluginPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-plugins-{Guid.NewGuid():N}");
        var package = Path.Combine(root, "escape");
        Directory.CreateDirectory(package);
        try
        {
            WriteManifest(package, "escape", WorkflowPluginModuleGroups.Studio, "../outside.dll");

            var error = Assert.Throws<InvalidOperationException>(() =>
                new WorkflowPluginLoader().LoadModules<ITestWorkflowPluginModule>(
                    root, WorkflowPluginModuleGroups.Studio));

            Assert.Contains("不能离开插件包目录", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreatePluginRoot(string pluginId, string group)
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        CreatePluginPackage(root, "package", pluginId, group);
        return root;
    }

    private static void CreatePluginPackage(
        string root,
        string directoryName,
        string pluginId,
        string group,
        string[]? requires = null)
    {
        var package = Path.Combine(root, directoryName);
        Directory.CreateDirectory(package);
        var sourceAssembly = typeof(WorkflowPluginLoaderTests).Assembly.Location;
        var fileName = Path.GetFileName(sourceAssembly);
        File.Copy(sourceAssembly, Path.Combine(package, fileName));
        WriteManifest(package, pluginId, group, fileName, requires);
    }

    private static void WriteManifest(
        string package,
        string pluginId,
        string group,
        string assemblyFile,
        string[]? requires = null)
    {
        var manifest = new WorkflowPluginManifest
        {
            PluginId = pluginId,
            Version = "1.0.0",
            Requires = requires ?? Array.Empty<string>(),
            Modules = new Dictionary<string, string[]>
            {
                [group] = new[] { assemblyFile }
            }
        };
        File.WriteAllText(
            Path.Combine(package, "plugin.json"),
            JsonSerializer.Serialize(manifest));
    }
}

public interface ITestWorkflowPluginModule
{
    string ModuleId { get; }
}

public interface INotImplementedPluginModule
{
}

public sealed class TestWorkflowPluginModule : ITestWorkflowPluginModule
{
    public string ModuleId => "demo.module";
}

public sealed class TestWorkflowRuntimePluginModule : IWorkflowRuntimePluginModule
{
    public string ExtensionId => "test.runtime";

    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
    }
}

file sealed class TestRuntimeNode : WorkflowNodeModel
{
    public TestRuntimeNode()
    {
    }

    public override string NodeType => "Test.Runtime";
}

file sealed class OtherRuntimeNode : WorkflowNodeModel
{
    public override string NodeType => "Test.Other";
}

file sealed class TestRuntimeNodeHandler : WorkflowNodeHandler<TestRuntimeNode>
{
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        TestRuntimeNode node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(NodeExecutionResult.Complete());
}
