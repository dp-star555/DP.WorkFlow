using System.Diagnostics;

namespace DP.WorkFlow;

/// <summary>
/// 运行事实使用的高精度时钟（R1-01）。
/// </summary>
/// <remarks>
/// .NET Framework 4.8 上 <see cref="DateTimeOffset.UtcNow"/> 的分辨率通常只有 1～15.6ms，快速循环时前后事件时间相同，
/// 无法靠时间还原先后。这里以进程内一次系统时间为锚点，加上 <see cref="Stopwatch"/> 的高分辨率经过时间：
/// 分辨率为亚微秒级，且在同一进程内单调不减。系统时间被调整（例如对时）超过 1 秒时重新锚定，
/// 但返回值仍不会早于上一次返回值，保证同一运行内的时间先后可靠。
/// </remarks>
public static class WorkflowClock
{
    private static readonly TimeSpan ResyncThreshold = TimeSpan.FromSeconds(1);
    private static readonly object AnchorSync = new();
    private static long _anchorUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
    private static long _anchorTimestamp = Stopwatch.GetTimestamp();
    private static long _lastUtcTicks;

    /// <summary>获取当前 UTC 时间（高分辨率、进程内单调不减）。</summary>
    public static DateTimeOffset UtcNow
    {
        get
        {
            var ticks = Volatile.Read(ref _anchorUtcTicks) + ElapsedTicks(Stopwatch.GetTimestamp() - Volatile.Read(ref _anchorTimestamp));
            var wall = DateTimeOffset.UtcNow.UtcTicks;
            if (Math.Abs(wall - ticks) > ResyncThreshold.Ticks)
            {
                lock (AnchorSync)
                {
                    _anchorTimestamp = Stopwatch.GetTimestamp();
                    Volatile.Write(ref _anchorUtcTicks, wall);
                    ticks = wall;
                }
            }
            // 单调：并发调用或重新锚定后都不早于上一次返回值。
            while (true)
            {
                var last = Volatile.Read(ref _lastUtcTicks);
                var next = ticks > last ? ticks : last;
                if (Interlocked.CompareExchange(ref _lastUtcTicks, next, last) == last)
                    return new DateTimeOffset(next, TimeSpan.Zero);
            }
        }
    }

    private static long ElapsedTicks(long stopwatchTicks)
    {
        var frequency = Stopwatch.Frequency;
        return stopwatchTicks / frequency * TimeSpan.TicksPerSecond
               + stopwatchTicks % frequency * TimeSpan.TicksPerSecond / frequency;
    }
}
