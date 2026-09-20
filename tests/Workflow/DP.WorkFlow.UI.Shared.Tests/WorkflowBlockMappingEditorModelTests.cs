using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowBlockMappingEditorModelTests
{
    [Fact]
    public void MappingEdits_AreUndoableAndExposeChildBindingCandidates()
    {
        var childSource = new DecisionNodeModel { Id = "ChildDecision", Title = "子判断" };
        var childDocument = new WorkflowDocument { Name = "Child" };
        var child = childDocument.CanvasProjection;
        child.Nodes.Add(new WorkflowCanvasNode { Node = childSource });
        childDocument.EntryNodeId = childSource.Id;
        var block = new BlockNodeModel
        {
            Id = "Block",
            Title = "子流程",
            SubDocument = childDocument
        };
        var rootDocument = new WorkflowDocument { Name = "Root" };
        var root = rootDocument.CanvasProjection;
        root.Nodes.Add(new WorkflowCanvasNode { Node = block });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();
        var session = new WorkflowDesignerSession(rootDocument, catalog);
        using var editor = new WorkflowBlockMappingEditorModel(session, block.Id);

        editor.AddInput();
        var input = Assert.Single(editor.Inputs);
        editor.ReplaceInput(input.Index, input with
        {
            TargetVariableName = "StationId",
            Source = E_BlockInputSource.ParentVariable,
            ParentVariableName = "SelectedStation"
        });
        editor.AddOutput();
        var output = Assert.Single(editor.Outputs);
        var candidate = Assert.Single(
            editor.GetChildCandidates(),
            item => item.SourceNodeId == childSource.Id && item.MemberPath == "Value");
        editor.ReplaceOutput(output.Index, output with
        {
            TargetVariableName = "Passed",
            Source = E_BlockOutputSource.ChildNodeBinding,
            ChildBinding = candidate.ToBindingKey()
        });

        Assert.Equal("StationId", Assert.Single(block.InputMappings).TargetVariableName);
        Assert.Equal(new WorkflowBindingKey(childSource.Id, "Value"), Assert.Single(block.OutputMappings).ChildBinding);
        Assert.True(session.Undo());
        Assert.Equal(E_BlockOutputSource.ChildVariable, Assert.Single(block.OutputMappings).Source);
    }
}
