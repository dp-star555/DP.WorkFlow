namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 统一刷新时钟（R4-b）：整个界面只用一个界面线程定时器，按 <see cref="WorkflowUiRefresh.FramesPerSecond"/> 走帧，
/// 每帧只通知看得见的页面。画布动画、视觉预览都挂在这里，不再各开定时器。
/// </summary>
/// <remarks>
/// 只在界面线程使用。没有订阅者时定时器停止。控件释放时自动取消订阅。
/// “看得见”指控件及其上级都可见、句柄已创建、所在窗口没有最小化。
/// </remarks>
public static class WorkflowRefreshClock
{
    private static readonly List<Subscription> Subscriptions = new();
    private static System.Windows.Forms.Timer? _timer;
    private static bool _settingsHooked;

    /// <summary>当前订阅数（诊断用）。</summary>
    public static int SubscriberCount => Subscriptions.Count;

    /// <summary>
    /// 订阅帧：<paramref name="owner"/> 看得见时，每帧调用一次 <paramref name="onFrame"/>。
    /// </summary>
    /// <param name="owner">所属控件；释放时自动取消订阅。</param>
    /// <param name="onFrame">每帧要做的事（界面线程）。</param>
    /// <returns>释放即取消订阅。</returns>
    public static IDisposable Subscribe(Control owner, Action onFrame)
    {
        Guard.NotNull(owner);
        Guard.NotNull(onFrame);
        var subscription = new Subscription(owner, onFrame);
        Subscriptions.Add(subscription);
        owner.Disposed += subscription.OnOwnerDisposed;
        EnsureTimer();
        return subscription;
    }

    /// <summary>控件当前是否看得见（自身与上级可见、句柄已创建、窗口未最小化）。</summary>
    /// <param name="control">控件。</param>
    public static bool IsShowing(Control control)
    {
        Guard.NotNull(control);
        if (control.IsDisposed || !control.IsHandleCreated || !control.Visible) return false;
        var form = control.FindForm();
        return form is null || form.WindowState != FormWindowState.Minimized;
    }

    private static void EnsureTimer()
    {
        if (!_settingsHooked)
        {
            _settingsHooked = true;
            WorkflowUiRefresh.Changed += OnSettingsChanged;
        }
        if (_timer is null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = WorkflowUiRefresh.FrameIntervalMilliseconds };
            _timer.Tick += OnTick;
        }
        if (!_timer.Enabled) _timer.Start();
    }

    private static void OnSettingsChanged(object? sender, EventArgs e)
    {
        // 设置可能在别的线程改；定时器间隔只在下一帧（界面线程）里更新。
        _intervalDirty = true;
    }

    private static volatile bool _intervalDirty;

    private static void OnTick(object? sender, EventArgs e)
    {
        if (_intervalDirty && _timer is not null)
        {
            _intervalDirty = false;
            _timer.Interval = WorkflowUiRefresh.FrameIntervalMilliseconds;
        }
        // 回调里可能增删订阅，先拷一份。
        foreach (var subscription in Subscriptions.ToArray())
        {
            if (subscription.Removed || !IsShowing(subscription.Owner)) continue;
            subscription.OnFrame();
        }
        if (Subscriptions.Count == 0) _timer?.Stop();
    }

    private static void Remove(Subscription subscription)
    {
        Subscriptions.Remove(subscription);
        if (Subscriptions.Count == 0) _timer?.Stop();
    }

    private sealed class Subscription : IDisposable
    {
        public Subscription(Control owner, Action onFrame)
        {
            Owner = owner;
            OnFrame = onFrame;
        }

        public Control Owner { get; }

        public Action OnFrame { get; }

        public bool Removed { get; private set; }

        public void OnOwnerDisposed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (Removed) return;
            Removed = true;
            Owner.Disposed -= OnOwnerDisposed;
            Remove(this);
        }
    }
}
