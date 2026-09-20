using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class StudioDependencyTests
{
    [Fact]
    [Trait("Category", "Critical")]
    public void DesignerSession_AcceptsDocumentButNotCanvasAsConstructionRoot()
    {
        var constructors = typeof(WorkflowDesignerSession).GetConstructors();

        Assert.Contains(constructors, constructor => constructor.GetParameters()[0].ParameterType == typeof(WorkflowDocument));
        Assert.DoesNotContain(constructors, constructor => constructor.GetParameters()[0].ParameterType == typeof(WorkflowCanvasModel));
    }

    [Fact]
    [Trait("Category", "Critical")]
    public void SharedStudio_DoesNotReferenceConcreteNodePackages()
    {
        var references = typeof(WorkflowDesignerSession).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain(references, name =>
            name!.StartsWith("DP.WorkFlow.Nodes.", StringComparison.Ordinal));
    }
}
