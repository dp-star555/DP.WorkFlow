using System.Reflection;

namespace DP.WorkFlow.Tests;

public sealed class ProcessOutputDisplayNameTests
{
    [Fact]
    public void NodeOutputs_HaveChineseNamesForEveryMember()
    {
        var unnamed = new WorkflowNodeCatalog().RegisterProcessNodes().Snapshot().Values
            .Select(descriptor => descriptor.OutputType)
            .Where(type => type is not null && !type.IsPrimitive && !type.IsEnum && type != typeof(string)
                && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            .SelectMany(type => type!.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .Where(property => WorkflowOutputDisplayNames.Resolve(property) == property.Name)
                .Select(property => $"{type.Name}.{property.Name}"))
            .Distinct();

        Assert.Empty(unnamed);
    }
}
