namespace DP.WorkFlow.LabelInspection;

/// <summary>标签按需资源缓存的宿主策略；数量上限不是进程内存的字节硬限制。</summary>
public sealed record WorkflowLabelInspectionCacheOptions
{
    /// <summary>最后一个使用者归还租约后，最多保留多久；默认一天。</summary>
    public TimeSpan IdleExpiration { get; init; } = TimeSpan.FromDays(1);
    /// <summary>后台空闲清理周期；默认一分钟。</summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>最多保留的配方运行资源份数；满额且全在用时拒绝新的冷加载。</summary>
    public int MaximumCachedRecipes { get; init; } = 8;
    /// <summary>最多保留的不同模型份数；OCR与异常骨干分别计数，相同内容共享。</summary>
    public int MaximumCachedModels { get; init; } = 4;
    /// <summary>同时执行详细配方加载的最大数量。</summary>
    public int MaximumConcurrentLoads { get; init; } = 2;
    /// <summary>单调时钟及后台计时器来源；测试可注入虚拟时间。</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal void Validate()
    {
        if (IdleExpiration <= TimeSpan.Zero || CleanupInterval <= TimeSpan.Zero || CleanupInterval.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(IdleExpiration), "空闲期限与清理周期必须是有效正值。");
        if (MaximumCachedRecipes < 1 || MaximumCachedModels < 1 || MaximumConcurrentLoads < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumCachedRecipes), "缓存数量与加载并发上限必须大于零。");
        ArgumentNullException.ThrowIfNull(TimeProvider);
    }
}

/// <summary>按需缓存的诊断快照；不包含检测报告或资源对象。</summary>
/// <param name="RegisteredRecipes">当前已提交运行作用域内的轻量配方登记数。</param>
/// <param name="CachedRecipes">缓存中的配方运行资源数，包含正在加载的候选。</param>
/// <param name="CachedModels">共享模型缓存数，包含正在加载的候选。</param>
/// <param name="RecipeLoads">成功详细加载配方的累计次数。</param>
/// <param name="ModelLoads">成功创建共享模型的累计次数。</param>
/// <param name="RecipeHits">配方缓存命中次数，包含合并等待同一次加载。</param>
/// <param name="ModelHits">共享模型命中次数。</param>
/// <param name="RecipeEvictions">因超时、数量或版本替换淘汰的配方累计次数。</param>
/// <param name="ModelEvictions">淘汰的共享模型累计次数。</param>
public sealed record WorkflowLabelInspectionCacheStatistics(int RegisteredRecipes, int CachedRecipes, int CachedModels,
    int RecipeLoads, int ModelLoads, int RecipeHits, int ModelHits, int RecipeEvictions, int ModelEvictions);
