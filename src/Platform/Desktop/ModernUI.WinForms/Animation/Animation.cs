namespace ModernUI.WinForms;

/// <summary>在各 UI 线程内集中调度轻量属性动画。</summary>
public static class ModernAnimation
{
    [ThreadStatic]
    private static AnimationScheduler? _scheduler;

    /// <summary>启动可取消的 UI 线程缓动动画。</summary>
    public static IDisposable Start(Control owner, float from, float to, int duration, Action<float> update, Action? completed = null) =>
        StartCore(owner, from, to, duration, update, ModernAnimationEasing.EaseOutCubic, completed);

    internal static IDisposable Start(Control owner, float from, float to, int duration, Action<float> update,
        Func<float, float> easing, Action? completed = null) =>
        StartCore(owner, from, to, duration, update, easing, completed);

    private static IDisposable StartCore(Control owner, float from, float to, int duration, Action<float> update,
        Func<float, float> easing, Action? completed)
    {
        if (!ModernUiSettings.EffectiveAnimationsEnabled || duration <= 0 || !owner.IsHandleCreated)
        {
            update(to);
            completed?.Invoke();
            return Empty.Instance;
        }

        _scheduler ??= new AnimationScheduler();
        return _scheduler.Start(owner, from, to, duration, update, easing, completed);
    }

    private sealed class AnimationScheduler
    {
        private readonly List<Item> _items = [];
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };

        public AnimationScheduler() => _timer.Tick += (_, _) => Tick();

        public IDisposable Start(Control owner, float from, float to, int duration, Action<float> update,
            Func<float, float> easing, Action? completed)
        {
            var item = new Item(owner, from, to, duration, update, easing, completed);
            _items.RemoveAll(existing => ReferenceEquals(existing.Owner, owner) && existing.Update == update);
            _items.Add(item);
            if (!_timer.Enabled) _timer.Start();
            return item;
        }

        private void Tick()
        {
            for (var index = _items.Count - 1; index >= 0; index--)
            {
                var item = _items[index];
                if (item.Cancelled || item.Owner.IsDisposed)
                {
                    _items.RemoveAt(index);
                    continue;
                }
                var elapsed = ModernCompatibility.TickCount64 - item.Started;
                var progress = ModernCompatibility.Clamp(elapsed / (float)item.Duration, 0, 1);
                var eased = ModernCompatibility.Clamp(item.Easing(progress), 0, 1);
                item.Update(item.From + (item.To - item.From) * eased);
                if (progress >= 1)
                {
                    _items.RemoveAt(index);
                    item.Completed?.Invoke();
                }
            }
            if (_items.Count == 0) _timer.Stop();
        }
    }

    private sealed class Item(Control owner, float from, float to, int duration, Action<float> update,
        Func<float, float> easing, Action? completed) : IDisposable
    {
        public Control Owner { get; } = owner;
        public float From { get; } = from;
        public float To { get; } = to;
        public int Duration { get; } = duration;
        public Action<float> Update { get; } = update;
        public Func<float, float> Easing { get; } = easing;
        public Action? Completed { get; } = completed;
        public long Started { get; } = ModernCompatibility.TickCount64;
        public bool Cancelled { get; private set; }
        public void Dispose() => Cancelled = true;
    }

    private sealed class Empty : IDisposable
    {
        public static Empty Instance { get; } = new();
        public void Dispose() { }
    }
}

internal static class ModernAnimationEasing
{
    public static float EaseOutCubic(float progress) => 1 - ModernCompatibility.Pow(1 - progress, 3);

    public static float EaseInOutCubic(float progress) => progress < .5f
        ? 4 * progress * progress * progress
        : 1 - ModernCompatibility.Pow(-2 * progress + 2, 3) / 2;
}
