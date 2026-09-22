namespace DP.WorkFlow.Tests;

/// <summary>
/// 实施细节 §12.3：输出稳定键自动提取。标量和资源根值统一 <c>$</c>，普通 DTO 只展开第一层，
/// 单个属性失败或超限不得影响其他键和已提交输出。
/// </summary>
public sealed class WorkflowOutputValueExtractorTests
{
    [Fact]
    public void Extract_ScalarRootUsesDollarKey()
    {
        var result = new WorkflowOutputValueExtractor(32).Extract(10);

        Assert.Equal("$", Assert.Single(result.Values).Key);
        Assert.Equal(10, result.Values["$"]);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Extract_NullRootRecordsDollarNull()
    {
        var result = new WorkflowOutputValueExtractor(32).Extract(null);

        Assert.Equal("$", Assert.Single(result.Values).Key);
        Assert.Null(result.Values["$"]);
        Assert.Null(result.OutputType);
    }

    [Fact]
    public void Extract_DtoExpandsFirstLevelPropertiesInStableOrder()
    {
        var result = new WorkflowOutputValueExtractor(32).Extract(new LineResult(12.5, 0.25, true));

        Assert.Equal(new[] { "AngleRadians", "Distance", "Success" }, result.Values.Keys);
        Assert.Equal(12.5, result.Values["Distance"]);
        Assert.Equal(typeof(LineResult).FullName, result.OutputType);
    }

    [Fact]
    public void Extract_DoesNotRecurseIntoNestedObjects()
    {
        var result = new WorkflowOutputValueExtractor(32).Extract(new NestedResult(new Inner(1)));

        // 嵌套对象整体作为一个值交给统一编码器；其内部属性不得成为顶层输出键。
        Assert.Equal(new[] { "Inner" }, result.Values.Keys);
        Assert.IsType<Inner>(result.Values["Inner"]);
    }

    [Fact]
    public void Extract_NonExpandableRootValuesUseDollarKey()
    {
        var extractor = new WorkflowOutputValueExtractor(32);

        foreach (var root in new object?[]
                 {
                     "text",
                     'x',
                     true,
                     DateTime.UnixEpoch,
                     Guid.Empty,
                     TimeSpan.Zero,
                     new byte[] { 1, 2, 3 },
                     new int[] { 1, 2, 3 },
                     new List<int> { 1 },
                     new Dictionary<string, int> { ["a"] = 1 },
                     Stream.Null,
                     new InvalidOperationException("设备未就绪。")
                 })
        {
            var result = extractor.Extract(root);
            Assert.Equal("$", Assert.Single(result.Values).Key);
        }
    }

    [Fact]
    public void Extract_WhenGetterThrows_KeepsOtherPropertiesAndReportsDiagnostic()
    {
        var result = new WorkflowOutputValueExtractor(32).Extract(new PartiallyBrokenResult());

        // 单个属性读取失败不得阻止其他键被记录。
        Assert.Equal("[读取失败]", result.Values["Broken"]);
        Assert.Equal("ok", result.Values["Healthy"]);
        Assert.Contains(result.Diagnostics, message => message.Contains("Broken"));
    }

    [Fact]
    public void Extract_TruncatesAtMaxOutputPropertiesAndReportsDiagnostic()
    {
        var result = new WorkflowOutputValueExtractor(2).Extract(new WideResult());

        Assert.Equal(new[] { "A", "B" }, result.Values.Keys);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Constructor_RejectsNonPositivePropertyLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowOutputValueExtractor(0));
    }

    private sealed record LineResult(double Distance, double AngleRadians, bool Success);

    private sealed record Inner(int Value);

    private sealed record NestedResult(Inner Inner);

    private sealed class PartiallyBrokenResult
    {
        public string Healthy => "ok";

        public string Broken => throw new InvalidOperationException("属性读取失败。");
    }

    private sealed class WideResult
    {
        public int A => 1;

        public int B => 2;

        public int C => 3;
    }
}
