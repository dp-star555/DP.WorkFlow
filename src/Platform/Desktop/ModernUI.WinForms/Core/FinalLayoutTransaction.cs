namespace ModernUI.WinForms;

/// <summary>
/// Coalesces repeated control layout requests into one final UI-thread action. Requests made before
/// handle creation remain synchronous so constructors and the designer keep deterministic bounds.
/// </summary>
internal sealed class FinalLayoutTransaction
{
    private readonly Control _owner;
    private readonly Action _applyFinalLayout;
    private bool _queued;
    private bool _applying;
    private int _generation;

    public FinalLayoutTransaction(Control owner, Action applyFinalLayout)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _applyFinalLayout = applyFinalLayout ?? throw new ArgumentNullException(nameof(applyFinalLayout));
        _owner.HandleDestroyed += (_, _) => CancelPending();
    }

    public bool IsPending => _queued;

    public void Request()
    {
        if (_owner.IsDisposed || _owner.Disposing || _applying) return;
        if (!_owner.IsHandleCreated)
        {
            ApplyCore();
            return;
        }
        if (_queued) return;

        _queued = true;
        var generation = ++_generation;
        _owner.BeginInvoke((Action)(() => Apply(generation)));
    }

    private void Apply(int generation)
    {
        if (generation != _generation) return;
        if (_owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated)
        {
            _queued = false;
            return;
        }

        // Keep the transaction pending while applying bounds. WinForms raises parent Layout from
        // child SetBounds calls; accepting those requests would create a perpetual BeginInvoke loop.
        try { ApplyCore(); }
        finally { _queued = false; }
    }

    private void ApplyCore()
    {
        _applying = true;
        try { _applyFinalLayout(); }
        finally { _applying = false; }
    }

    private void CancelPending()
    {
        _generation++;
        _queued = false;
    }
}
