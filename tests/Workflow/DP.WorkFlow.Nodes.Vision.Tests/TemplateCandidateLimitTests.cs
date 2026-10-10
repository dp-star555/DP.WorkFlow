namespace DP.WorkFlow.Tests;

/// <summary>模板定位的最大候选数：默认32，传给搜索选项，越界在配置校验时报错。</summary>
public sealed class TemplateCandidateLimitTests
{
    [Fact]
    public void MaximumCandidates_FlowsIntoSearchOptions_AndIsValidated()
    {
        var node = new LocateVisionTemplatePoseNodeModel();
        Assert.Equal(32, node.MaximumCandidates);
        node.MaximumCandidates = 5;
        Assert.Equal(5, node.OptionsForPreview().MaximumCandidates);
        node.MaximumCandidates = 0;
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("最大候选数", StringComparison.Ordinal));
        node.MaximumCandidates = 1025;
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("最大候选数", StringComparison.Ordinal));
    }
}
