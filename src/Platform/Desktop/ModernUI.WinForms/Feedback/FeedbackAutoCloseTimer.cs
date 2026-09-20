namespace ModernUI.WinForms;

[Flags]
internal enum FeedbackPauseReason
{
    None = 0,
    Pointer = 1,
    OwnerInactive = 2
}

/// <summary>为反馈浮层保留精确的剩余显示时间，并组合 Hover 与 Owner 状态暂停原因。</summary>
internal sealed class FeedbackAutoCloseTimer : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Action _elapsed;
    private FeedbackPauseReason _pauseReasons;
    private long _startedAt;
    private int _remaining;
    private bool _running;
    private bool _disposed;

    public FeedbackAutoCloseTimer(Action elapsed)
    {
        _elapsed = elapsed ?? throw new ArgumentNullException(nameof(elapsed));
        _timer.Tick += TimerTick;
    }

    public void Restart(int duration)
    {
        ThrowIfDisposed();
        StopTimer();
        _remaining = Math.Max(0, duration);
        if (_remaining > 0 && _pauseReasons == FeedbackPauseReason.None) StartTimer();
    }

    public void SetPaused(FeedbackPauseReason reason, bool paused)
    {
        ThrowIfDisposed();
        var previous = _pauseReasons;
        if (paused) _pauseReasons |= reason;
        else _pauseReasons &= ~reason;
        if (previous == _pauseReasons) return;
        if (previous == FeedbackPauseReason.None) PauseTimer();
        else if (_pauseReasons == FeedbackPauseReason.None && _remaining > 0) StartTimer();
    }

    private void StartTimer()
    {
        _timer.Interval = Math.Max(1, _remaining);
        _startedAt = ModernCompatibility.TickCount64;
        _running = true;
        _timer.Start();
    }

    private void PauseTimer()
    {
        if (!_running) return;
        var elapsed = (int)ModernCompatibility.Clamp(ModernCompatibility.TickCount64 - _startedAt, 0, int.MaxValue);
        _remaining = Math.Max(1, _remaining - elapsed);
        StopTimer();
    }

    private void StopTimer()
    {
        _timer.Stop();
        _running = false;
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        StopTimer();
        _remaining = 0;
        _elapsed();
    }

    private void ThrowIfDisposed() => ModernCompatibility.ThrowIfDisposed(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Tick -= TimerTick;
        _timer.Dispose();
    }
}
