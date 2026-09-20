using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowBindingTreeModelTests
{
    [Fact]
    public void Candidates_AreGroupedBySourceAndMemberPathAndSearchable()
    {
        var model = new WorkflowBindingTreeModel();
        model.SetCandidates(new[]
        {
            new WorkflowBindingCandidate("Read", "Result.Code", typeof(int), "读取/Result.Code"),
            new WorkflowBindingCandidate("Read", "Result.Text", typeof(string), "读取/Result.Text"),
            new WorkflowBindingCandidate("Measure", "$", typeof(double), "测量/$")
        });

        Assert.Equal(3, model.MatchCount);
        Assert.Equal(2, model.Roots.Count);
        var read = Assert.Single(model.Roots, item => item.Label.Contains("Read", StringComparison.Ordinal));
        var result = Assert.Single(read.Children);
        Assert.Equal(2, result.Children.Count);

        model.Search("string");
        Assert.Equal(1, model.MatchCount);
        var leaf = Assert.Single(Assert.Single(Assert.Single(model.Roots).Children).Children);
        Assert.Equal("Result.Text", leaf.Candidate!.MemberPath);

        model.Search("MEASURE");
        Assert.Equal("$", Assert.Single(Assert.Single(model.Roots).Children).Candidate!.MemberPath);
    }

    [Fact]
    public void PublicDataCandidates_AreGroupedUnderCollapsiblePublicDataRoot()
    {
        var model = new WorkflowBindingTreeModel();
        model.SetCandidates(new[]
        {
            new WorkflowBindingCandidate("Product", "Id", typeof(string), "生产数据/当前产品/Id",
                WorkflowBindingCandidateSourceKind.PublicData, "当前产品", "生产数据"),
            new WorkflowBindingCandidate("Product", "Quantity", typeof(int), "生产数据/当前产品/Quantity",
                WorkflowBindingCandidateSourceKind.PublicData, "当前产品", "生产数据")
        });

        var root = Assert.Single(model.Roots);
        Assert.Equal(WorkflowBindingTreeNodeKind.PublicDataGroup, root.Kind);
        var category = Assert.Single(root.Children);
        var variable = Assert.Single(category.Children);
        Assert.Equal(WorkflowBindingTreeNodeKind.PublicData, variable.Kind);
        Assert.Equal(2, variable.Children.Count);
        Assert.All(variable.Children, leaf => Assert.True(leaf.Candidate!.ToBindingKey().IsPublicData));
    }
}
