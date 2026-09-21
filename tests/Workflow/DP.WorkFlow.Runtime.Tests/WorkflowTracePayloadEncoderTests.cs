namespace DP.WorkFlow.Tests;

/// <summary>
/// §20验收：Payload 编码不递归序列化任意对象图，图像和二进制只记身份，
/// 超长内容按策略截断并标记，敏感字段脱敏，编码失败不掩盖原始故障。
/// </summary>
public sealed class WorkflowTracePayloadEncoderTests
{
    [Fact]
    public void Encode_ComplexObject_ProducesBoundedSummaryInsteadOfRecursing()
    {
        var encoded = CreateEncoder().Encode(new Dictionary<string, object?>
        {
            ["Result"] = new NestedResult("定位", new[] { 1, 2, 3 })
        });

        var value = Assert.IsType<WorkflowTraceValue>(encoded!["Result"]);
        Assert.Equal(WorkflowTraceValueKind.Summary, value.Kind);
        Assert.Equal(typeof(NestedResult).FullName, value.TypeName);
        Assert.Null(value.Scalar);
        Assert.True(value.Text!.Length <= 128);
    }

    [Fact]
    public void Encode_BinaryPayload_BecomesReferenceWithLengthOnly()
    {
        var encoded = CreateEncoder().Encode(new Dictionary<string, object?>
        {
            ["Image"] = new byte[2048],
            ["Mask"] = new int[128]
        });

        var image = encoded!["Image"];
        Assert.Equal(WorkflowTraceValueKind.Reference, image.Kind);
        Assert.Equal(2048L, image.Length!.Value);
        Assert.Null(image.Scalar);
        // 大型基本类型数组同样只记身份和长度，不展开元素。
        var mask = encoded["Mask"];
        Assert.Equal(WorkflowTraceValueKind.Reference, mask.Kind);
        Assert.Equal(128L, mask.Length!.Value);
    }

    [Fact]
    public void Encode_LongString_IsTruncatedAndMarked()
    {
        var encoder = new WorkflowTracePayloadEncoder(new WorkflowRunRecordingOptions { MaxStringLength = 8 });

        var value = encoder.Encode(new Dictionary<string, object?> { ["Text"] = new string('a', 20) })!["Text"];

        Assert.Equal(WorkflowTraceValueKind.Scalar, value.Kind);
        Assert.True(value.Truncated);
        Assert.Equal(8, value.Text!.Length);
        Assert.Equal(20L, value.Length!.Value);
    }

    [Fact]
    public void Encode_LargeCollection_IsTruncatedAndMarked()
    {
        var encoder = new WorkflowTracePayloadEncoder(new WorkflowRunRecordingOptions { MaxCollectionItems = 3 });
        var items = Enumerable.Range(0, 10).Select(index => "item" + index).ToList();

        var value = encoder.Encode(new Dictionary<string, object?> { ["Items"] = items })!["Items"];

        Assert.Equal(WorkflowTraceValueKind.Summary, value.Kind);
        Assert.True(value.Truncated);
        Assert.Equal(10L, value.Length!.Value);
    }

    [Fact]
    public void Encode_SensitiveKey_IsRedacted()
    {
        var encoded = CreateEncoder().Encode(new Dictionary<string, object?>
        {
            ["DbPassword"] = "p@ssw0rd",
            ["ApiToken"] = "abcdef"
        });

        Assert.Equal("[已脱敏]", encoded!["DbPassword"].Text);
        Assert.Equal("[已脱敏]", encoded["ApiToken"].Text);
        Assert.Null(encoded["DbPassword"].Scalar);
    }

    [Fact]
    public void Encode_ThrowingValue_DoesNotPropagateAndMarksFailure()
    {
        var encoded = CreateEncoder().Encode(new Dictionary<string, object?>
        {
            ["Bad"] = new ThrowingSequence()
        });

        var value = Assert.IsType<WorkflowTraceValue>(encoded!["Bad"]);
        Assert.Equal(WorkflowTraceValueKind.Summary, value.Kind);
        Assert.Equal("[编码失败]", value.Text);
    }

    [Fact]
    public void Encode_Message_IsExposedAsMessageKey()
    {
        var encoded = CreateEncoder().Encode(null, "节点开始执行。");

        Assert.Equal("节点开始执行。", encoded!["Message"].Text);
    }

    [Fact]
    public void Encode_WithoutContent_ReturnsNull()
    {
        Assert.Null(CreateEncoder().Encode(null));
        Assert.Null(CreateEncoder().Encode(new Dictionary<string, object?>()));
    }

    private static WorkflowTracePayloadEncoder CreateEncoder() =>
        new(new WorkflowRunRecordingOptions());

    private sealed record NestedResult(string Name, int[] Values);

    /// <summary>枚举时抛出的值；用于验证编码失败被就地吸收，不掩盖原始业务故障。</summary>
    private sealed class ThrowingSequence : IEnumerable<object?>
    {
        public IEnumerator<object?> GetEnumerator() => throw new InvalidOperationException("编码失败。");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
