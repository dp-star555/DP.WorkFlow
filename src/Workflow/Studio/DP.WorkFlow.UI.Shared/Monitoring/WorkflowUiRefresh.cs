namespace DP.WorkFlow.UI;

/// <summary>
/// 界面刷新节拍（R4-b）：画布、运行监视、视觉预览等所有按时间刷新的界面共用一个帧率，
/// 运行快照的拉取间隔（<see cref="WorkflowStudioRuntimeBinding"/>）也跟着它走。
/// </summary>
/// <remarks>
/// 默认每秒 10 帧。现场电脑较慢或画面很多时可以调低，调试时可以调高；改动立即生效。
/// 不可见的页面不刷新；暂停、完成、失败、取消等状态变化不等节拍，立即补一帧。
/// </remarks>
public static class WorkflowUiRefresh
{
    /// <summary>默认帧率（每秒帧数）。</summary>
    public const int DefaultFramesPerSecond = 10;

    /// <summary>可设置的最低帧率。</summary>
    public const int MinimumFramesPerSecond = 1;

    /// <summary>可设置的最高帧率。</summary>
    public const int MaximumFramesPerSecond = 60;

    private static int _framesPerSecond = DefaultFramesPerSecond;

    /// <summary>帧率改变后触发（可能在任意线程）。</summary>
    public static event EventHandler? Changed;

    /// <summary>获取或设置每秒刷新帧数，超出 1～60 时取边界值。</summary>
    public static int FramesPerSecond
    {
        get => Volatile.Read(ref _framesPerSecond);
        set
        {
            var clamped = Math.Max(MinimumFramesPerSecond, Math.Min(MaximumFramesPerSecond, value));
            if (Interlocked.Exchange(ref _framesPerSecond, clamped) == clamped) return;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>当前帧率对应的间隔（毫秒）。</summary>
    public static int FrameIntervalMilliseconds => Math.Max(16, (int)Math.Round(1000d / FramesPerSecond));
}
