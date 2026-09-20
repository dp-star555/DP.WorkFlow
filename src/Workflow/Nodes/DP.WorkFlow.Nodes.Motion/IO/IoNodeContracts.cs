namespace DP.WorkFlow;

/// <summary>IO 条件通过模式。</summary>
public enum WorkflowIoPassMode
{
    /// <summary>瞬时匹配即可通过。</summary>
    Instant = 0,
    /// <summary>连续保持指定时长才通过。</summary>
    Hold = 1
}

/// <summary>多点条件组合方式。</summary>
public enum WorkflowIoMatchMode
{
    /// <summary>全部条件满足。</summary>
    All = 0,
    /// <summary>任一条件满足。</summary>
    Any = 1
}

/// <summary>描述一个 IO 点位期望。</summary>
public sealed class WorkflowIoExpectation
{
    /// <summary>获取或设置驱动 ID。</summary>
    public string DriveId { get; set; } = string.Empty;
    /// <summary>获取或设置字符串索引。</summary>
    public string Index { get; set; } = "0";
    /// <summary>获取或设置输入/输出类型。</summary>
    public WorkflowIoPointType IOType { get; set; } = WorkflowIoPointType.Input;
    /// <summary>获取或设置期望状态。</summary>
    public bool ExpectedValue { get; set; } = true;

    /// <summary>转换为已校验地址。</summary>
    public WorkflowIoAddress ToAddress()
    {
        if (string.IsNullOrWhiteSpace(DriveId)) throw new InvalidOperationException("DriveId 为空。");
        if (!int.TryParse(Index, out var index) || index < 0) throw new InvalidOperationException($"Invalid IO Index: {Index}");
        return new WorkflowIoAddress(DriveId.Trim(), index, IOType);
    }
}

/// <summary>表示多 IO 判断结果。</summary>
public sealed record IoMultiNodeResult(bool Matched, WorkflowIoMatchMode Mode, IReadOnlyList<WorkflowIoNodeResult> Items, long ElapsedMs = 0, string? Message = null);
