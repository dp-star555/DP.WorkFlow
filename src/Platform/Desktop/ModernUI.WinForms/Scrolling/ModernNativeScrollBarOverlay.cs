namespace ModernUI.WinForms;

/// <summary>Renders modern scroll chrome while delegating all position semantics to one scroll adapter.</summary>
internal sealed class ModernNativeScrollBarOverlay : Control
{
    private readonly IModernScrollAdapter _adapter;
    private readonly Control _target;
    private readonly bool _vertical;
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private bool _hovered;
    private bool _dragging;
    private int _dragOrigin;
    private int _dragStartPosition;

    public ModernNativeScrollBarOverlay(IModernScrollAdapter adapter, bool vertical)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _target = adapter.Target;
        _vertical = vertical;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Cursor = vertical ? Cursors.SizeNS : Cursors.SizeWE;
    }

    public bool AutoVisibility { get; set; }
    public int Position => TryGetMetrics(out var metrics) ? metrics.State.Position : 0;

    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value; BackColor = _target.BackColor; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_target.BackColor);
        if (!TryGetMetrics(out var metrics)) return;
        var color = _dragging ? Theme.ScrollThumbDragging : _hovered ? Theme.ScrollThumbHover : Theme.ScrollThumb;
        ModernScrollThumbRenderer.Draw(e.Graphics, metrics.Thumb, color);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging) return;
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !TryGetMetrics(out var metrics)) return;
        var coordinate = _vertical ? e.Y : e.X;
        var thumbStart = _vertical ? metrics.Thumb.Top : metrics.Thumb.Left;
        var thumbEnd = _vertical ? metrics.Thumb.Bottom : metrics.Thumb.Right;
        if (coordinate < thumbStart || coordinate > thumbEnd)
        {
            var pageStep = Math.Max(1, metrics.State.PageSize);
            var direction = _adapter.IsDirectionReversed ? -1 : 1;
            SetPosition(ModernCompatibility.Clamp(metrics.State.Position +
                (coordinate < thumbStart ? -pageStep : pageStep) * direction,
                metrics.State.Minimum, metrics.State.MaximumPosition));
            QueueRefreshFromTarget();
            return;
        }
        _dragging = true;
        _dragOrigin = coordinate;
        _dragStartPosition = metrics.State.Position;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || !TryGetMetrics(out var metrics)) return;
        var coordinate = _vertical ? e.Y : e.X;
        var travel = Math.Max(1f, metrics.TrackLength - metrics.ThumbLength);
        var valueTravel = Math.Max(0, metrics.State.MaximumPosition - metrics.State.Minimum);
        var direction = _adapter.IsDirectionReversed ? -1 : 1;
        var next = _dragStartPosition +
                   (int)Math.Round((coordinate - _dragOrigin) * valueTravel / travel) * direction;
        SetPosition(ModernCompatibility.Clamp(next, metrics.State.Minimum, metrics.State.MaximumPosition));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            _adapter.EndScroll();
            _adapter.SynchronizeChrome();
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e) => TryScrollWheel(e.Delta);

    public bool CanScroll(int delta) =>
        delta != 0 && _adapter.TryGetState(out var state) && state.CanScroll(delta);

    public bool TryScrollWheel(int delta)
    {
        if (delta == 0 || !_adapter.TryGetState(out var state) || !state.CanScroll(delta)) return false;
        var wheelLines = SystemInformation.MouseWheelScrollLines;
        var step = wheelLines < 0
            ? Math.Max(1, state.PageSize)
            : Math.Max(1, Math.Min(state.PageSize, wheelLines));
        var notches = Math.Max(1, Math.Abs(delta) / 120);
        var next = ModernCompatibility.Clamp(state.Position - Math.Sign(delta) * step * notches,
            state.Minimum, state.MaximumPosition);
        if (next == state.Position) return false;
        SetPosition(next);
        QueueRefreshFromTarget();
        return true;
    }

    public void RefreshFromTarget()
    {
        if (IsDisposed) return;
        var hasRange = _adapter.TryGetState(out _);
        _adapter.SynchronizeChrome();
        if (AutoVisibility) Visible = hasRange;
        Invalidate();
    }

    private bool TryGetMetrics(out ScrollMetrics metrics)
    {
        metrics = default;
        if (!_adapter.TryGetState(out var state)) return false;
        var inset = ModernDpi.Scale(4, DeviceDpi);
        var thickness = ModernDpi.Scale(ModernScrollThumbRenderer.DefaultThickness, DeviceDpi);
        var trackLength = Math.Max(1, (_vertical ? ClientSize.Height : ClientSize.Width) - inset * 2);
        var thumbLength = ModernCompatibility.Clamp(trackLength * state.PageSize / (float)state.Range,
            Math.Min(trackLength, ModernDpi.Scale(ModernScrollThumbRenderer.MinimumLength, DeviceDpi)), trackLength);
        var travel = Math.Max(0, trackLength - thumbLength);
        var positionOffset = _adapter.IsDirectionReversed
            ? state.MaximumPosition - state.Position
            : state.Position - state.Minimum;
        var offset = travel * positionOffset /
                     Math.Max(1f, state.MaximumPosition - state.Minimum);
        // Align the capsule's cross-axis edge to a physical pixel. Half-pixel placement leaks an
        // antialiased edge one pixel outside the intended thin thumb in composed drag frames.
        var crossAxis = (float)Math.Ceiling(((_vertical ? ClientSize.Width : ClientSize.Height) - thickness) / 2f);
        var thumb = _vertical
            ? new RectangleF(crossAxis, inset + offset, thickness, thumbLength)
            : new RectangleF(inset + offset, crossAxis, thumbLength, thickness);
        metrics = new ScrollMetrics(state, trackLength, thumbLength, thumb);
        return true;
    }

    private void SetPosition(int position)
    {
        // Native list/tree controls already scroll and invalidate the exposed strip atomically.
        // Toggling WM_SETREDRAW around every thumb movement turns each drag frame into a full
        // erase/repaint cycle, which is especially visible during horizontal scrolling.
        _adapter.SetPosition(position);
        _adapter.SynchronizeChrome();
    }

    private void QueueRefreshFromTarget()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(RefreshFromTarget);
    }

    private readonly record struct ScrollMetrics(ModernScrollState State,
        float TrackLength, float ThumbLength, RectangleF Thumb);
}
