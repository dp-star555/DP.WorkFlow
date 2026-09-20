using System.Text.Json;
using DP.WorkFlow;
using DP.WorkFlow.UI.WinForms;
using DP.WorkFlow.UI.Wpf;
using DP.WorkFlow.Vision.UI.WinForms;
using DP.WorkFlow.Vision.UI.Wpf;

namespace DP.WorkFlow.UI.Windows.Tests;

public sealed class VisionDesktopDependencyTests
{
    [Fact]
    [Trait("Category", "Critical")]
    public void VisionCanvases_DoNotReferenceWorkflowAssemblies()
    {
        AssertNoWorkflowReference(typeof(DP.Vision.Winform.VisionCanvasControl).Assembly);
        AssertNoWorkflowReference(typeof(DP.Vision.WPF.VisionCanvasControl).Assembly);
    }

    [Fact]
    [Trait("Category", "Critical")]
    public void StudioPluginManifest_LoadsActualWinFormsAndWpfVisionExtensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vision-studio-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CreatePluginPackage(
                root,
                "winforms",
                "vision.studio.winforms",
                WorkflowPluginModuleGroups.WinForms,
                typeof(VisionWinFormsStudioExtension).Assembly.Location);
            CreatePluginPackage(
                root,
                "wpf",
                "vision.studio.wpf",
                WorkflowPluginModuleGroups.Wpf,
                typeof(VisionWpfStudioExtension).Assembly.Location);
            var loader = new WorkflowPluginLoader();

            var winFormsModule = Assert.Single(
                loader.LoadModules<IWorkflowWinFormsStudioExtension>(root, WorkflowPluginModuleGroups.WinForms));
            var wpfModule = Assert.Single(
                loader.LoadModules<IWorkflowWpfStudioExtension>(root, WorkflowPluginModuleGroups.Wpf));
            var winFormsCatalog = new WorkflowWinFormsStudioExtensionCatalog().Register(winFormsModule);
            var wpfCatalog = new WorkflowWpfStudioExtensionCatalog().Register(wpfModule);

            Assert.Throws<InvalidOperationException>(() =>
                winFormsCatalog.Register(new VisionWinFormsStudioExtension()));
            Assert.Throws<InvalidOperationException>(() =>
                wpfCatalog.Register(new VisionWpfStudioExtension()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Critical")]
    public void CanvasContracts_AreOwnedByIndependentVisionAssemblies()
    {
        Assert.Equal("DP.Vision.UI", typeof(DP.Vision.UI.IVisionCanvas).Assembly.GetName().Name);
        Assert.Equal("DP.Vision", typeof(DP.Vision.IImageSource).Assembly.GetName().Name);
        Assert.DoesNotContain(typeof(VisionWinFormsStudioExtension).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("MachineVision", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(VisionWpfStudioExtension).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("MachineVision", StringComparison.Ordinal));
    }

    private static void AssertNoWorkflowReference(System.Reflection.Assembly assembly)
    {
        var references = assembly.GetReferencedAssemblies()
            .Select(item => item.Name)
            .Where(item => item is not null)
            .ToArray();
        Assert.DoesNotContain(references, name => name!.StartsWith("DP.WorkFlow", StringComparison.Ordinal));
    }

    private static void CreatePluginPackage(
        string root,
        string directoryName,
        string pluginId,
        string moduleGroup,
        string sourceAssembly)
    {
        var package = Path.Combine(root, directoryName);
        Directory.CreateDirectory(package);
        var assemblyFile = Path.GetFileName(sourceAssembly);
        File.Copy(sourceAssembly, Path.Combine(package, assemblyFile));
        var manifest = new WorkflowPluginManifest
        {
            PluginId = pluginId,
            Version = "1.0.0",
            Modules = new Dictionary<string, string[]>
            {
                [moduleGroup] = new[] { assemblyFile }
            }
        };
        File.WriteAllText(
            Path.Combine(package, "plugin.json"),
            JsonSerializer.Serialize(manifest));
    }
}
