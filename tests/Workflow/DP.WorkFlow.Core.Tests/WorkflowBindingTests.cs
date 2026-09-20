namespace DP.WorkFlow.Tests;

public sealed class WorkflowBindingTests
{
    [Theory]
    [InlineData("Node1|$")]
    [InlineData("Read1|Payload.Code")]
    public void BindingKey_RoundTripsLegacyCompatibleText(string text)
    {
        var parsed = WorkflowBindingKey.TryParse(text, out var bindingKey);

        Assert.True(parsed);
        Assert.Equal(text, bindingKey.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NodeOnly")]
    [InlineData("|Value")]
    [InlineData("Node|")]
    [InlineData("A|B|C")]
    public void BindingKey_RejectsInvalidText(string? text)
    {
        Assert.False(WorkflowBindingKey.TryParse(text, out _));
    }

    [Fact]
    public void WorkflowInput_BindingModeRequiresBindingKey()
    {
        var input = new WorkflowInput<bool> { Source = WorkflowValueSource.Binding };

        Assert.NotNull(input.Validate());
        Assert.Null(WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Read1", "Value")).Validate());
    }
}
