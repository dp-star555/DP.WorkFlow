using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodeEditorRendererValidationTests
{
    [Fact]
    public void WinFormsAndWpfEditors_RejectCustomPageWithoutPlatformRenderer()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var document = new WorkflowDocument();
                var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
                var session = new WorkflowDesignerSession(document, catalog);
                var start = session.AddNode("Start", 20, 20);
                var action = session.AddNode("Action", 180, 20);
                var provider = new MissingRendererPageProvider();

                var winFormsModel = new WorkflowNodeEditorModel(
                    session, start.Node.Id, action.Node.Id, new[] { provider });
                var winFormsError = Assert.Throws<InvalidOperationException>(() =>
                    new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(winFormsModel));
                Assert.Contains("WinForms", winFormsError.Message);
                Assert.Contains("Missing.Renderer", winFormsError.Message);
                winFormsModel.DisposeAsync().AsTask().GetAwaiter().GetResult();

                var wpfModel = new WorkflowNodeEditorModel(
                    session, start.Node.Id, action.Node.Id, new[] { provider });
                var wpfError = Assert.Throws<InvalidOperationException>(() =>
                    new DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow(wpfModel));
                Assert.Contains("WPF", wpfError.Message);
                Assert.Contains("Missing.Renderer", wpfError.Message);
                wpfModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI renderer validation thread timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void PlatformStudios_DoNotReferenceConcreteCompositeNodeAssembly()
    {
        var forbiddenAssembly = "DP.WorkFlow.Nodes.Composite";

        Assert.DoesNotContain(
            typeof(DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, forbiddenAssembly, StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, forbiddenAssembly, StringComparison.Ordinal));
    }

    private sealed class MissingRendererPageProvider : IWorkflowNodeEditorPageProvider
    {
        public string ExtensionId => "Test.MissingRenderer";

        public bool CanProvide(WorkflowNodeEditorContext context) => true;

        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                "MissingPage",
                "Missing",
                WorkflowNodeEditorPageKind.Custom,
                500,
                new object(),
                RendererKey: "Missing.Renderer");
        }
    }
}
