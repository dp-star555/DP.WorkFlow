using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDocumentWorkspaceTests
{
    [Fact]
    public void NewEditSaveAndOpen_TracksDirtyStateAndRecentFiles()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        using var workspace = new WorkflowDocumentWorkspace(catalog);
        var navigator = workspace.New("Document");
        var filePath = Path.Combine(Path.GetTempPath(), $"workflow-ui-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(workspace.IsDirty);
            navigator.CurrentSession.AddNode("End", 300, 80);
            workspace.SaveAs(filePath);
            Assert.False(workspace.IsDirty);
            Assert.True(File.Exists(filePath));
            Assert.Equal(filePath, Assert.Single(workspace.RecentFiles));

            navigator.CurrentSession.AddNode("Action", 180, 80);
            Assert.True(workspace.IsDirty);
            var loadedNavigator = workspace.Open(filePath);

            Assert.False(workspace.IsDirty);
            Assert.Equal("Document", loadedNavigator.RootSession.Canvas.Name);
            Assert.Equal(2, loadedNavigator.RootSession.Canvas.Nodes.Count);
            Assert.Equal(filePath, Assert.Single(workspace.RecentFiles));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
