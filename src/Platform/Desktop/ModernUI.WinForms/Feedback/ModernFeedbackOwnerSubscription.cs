namespace ModernUI.WinForms;

/// <summary>Owns the repeated geometry, visibility, DPI and close subscription used by feedback hosts.</summary>
internal sealed class ModernFeedbackOwnerSubscription : IDisposable
{
    private readonly Form _owner;
    private readonly EventHandler _changed;
    private readonly FormClosedEventHandler _closed;
    private readonly DpiChangedEventHandler _dpiChanged;
    private bool _disposed;

    public ModernFeedbackOwnerSubscription(Form owner, EventHandler changed, FormClosedEventHandler closed)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _closed = closed ?? throw new ArgumentNullException(nameof(closed));
        _dpiChanged = (sender, _) => changed(sender, EventArgs.Empty);
        owner.LocationChanged += changed;
        owner.SizeChanged += changed;
        owner.DpiChanged += _dpiChanged;
        owner.VisibleChanged += changed;
        owner.FormClosed += closed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.LocationChanged -= _changed;
        _owner.SizeChanged -= _changed;
        _owner.DpiChanged -= _dpiChanged;
        _owner.VisibleChanged -= _changed;
        _owner.FormClosed -= _closed;
    }
}
