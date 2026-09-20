using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

/// <summary>Ant Design 风格的可拖动分隔容器。</summary>
[DefaultEvent(nameof(SplitterMoved))]
[Description("ModernSplitter 现代可拖动分隔容器")]
[DisplayName("现代分隔条")]
[ToolboxBitmap(typeof(ModernSplitter), "Toolbox.Icons.Layout.bmp")]
[ToolboxItem(true)]
public sealed class ModernSplitter : SplitContainer
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private bool _splitterHovered;
    private bool _splitterMoving;
    private bool _initialDistanceApplied;
    private Rectangle _splitterBeforeMove;
    private Rectangle _lastPaintedSplitter;
    private int _gripLength = 40;
    private int _initialPanel2Size = 82;
    private bool _dpiDistancePending;
    private bool _dpiSettling;
    private bool _applyingLogicalDistance;
    private bool _hasLogicalFixedSize;
    private int _dpiSettleVersion;
    private FixedPanel _fixedPanelBeforeDpi;
    private float _logicalFixedPanelSize;
    private float _splitRatioBeforeDpi;
    private float _logicalSplitRatio;
    private bool _hasLogicalSplitRatio;
    private int _lastDeviceDpi = 96;
    private float _logicalSplitterWidth = 7f;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalLayout);

    public ModernSplitter()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BorderStyle = BorderStyle.None;
        SplitterWidth = 7;
        Panel1MinSize = 80;
        Panel2MinSize = 56;
        FixedPanel = FixedPanel.Panel2;
        SplitterMoving += (_, _) =>
        {
            if (!_splitterMoving) _splitterBeforeMove = SplitterRectangle;
            _splitterMoving = true;
            Invalidate(SplitterRectangle);
        };
        SplitterMoved += (_, _) =>
        {
            var userMovedSplitter = _splitterMoving;
            _splitterMoving = false;
            if (!_applyingLogicalDistance && IsHandleCreated && userMovedSplitter)
            {
                var available = Math.Max(1,
                    (Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width) - SplitterWidth);
                if (FixedPanel is FixedPanel.Panel1 or FixedPanel.Panel2)
                {
                    var dpi = Math.Max(1, DeviceDpi);
                    _logicalFixedPanelSize = (FixedPanel == FixedPanel.Panel1
                        ? SplitterDistance
                        : Math.Max(0, available - SplitterDistance)) * 96f / dpi;
                    _hasLogicalFixedSize = true;
                }
                else
                {
                    _logicalSplitRatio = ModernCompatibility.Clamp(SplitterDistance / (float)available, 0f, 1f);
                    _hasLogicalSplitRatio = true;
                }
            }
            var previous = _splitterBeforeMove.IsEmpty ? _lastPaintedSplitter : _splitterBeforeMove;
            RepaintSplitterTransition(previous, SplitterRectangle);
            _splitterBeforeMove = SplitterRectangle;
        };
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            BackColor = value.Background;
            Invalidate();
        }
    }

    /// <summary>分隔条中央可视握柄的长度。</summary>
    [Category("Appearance"), DefaultValue(40)]
    [Description("分隔条中央拖动握柄的长度，单位为逻辑像素。")]
    public int GripLength
    {
        get => _gripLength;
        set { var next = Math.Max(20, value); if (_gripLength == next) return; _gripLength = next; Invalidate(SplitterRectangle); }
    }

    /// <summary>获取或设置分隔条位置，并同步后续DPI转换使用的逻辑固定面板尺寸。</summary>
    [Category("Layout")]
    public new int SplitterDistance
    {
        get => base.SplitterDistance;
        set
        {
            if (base.SplitterDistance == value) return;
            var wasApplying = _applyingLogicalDistance;
            try
            {
                _applyingLogicalDistance = true;
                base.SplitterDistance = value;
            }
            finally { _applyingLogicalDistance = wasApplying; }
            if (wasApplying || !IsHandleCreated) return;
            var available = Math.Max(1,
                (Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width) - SplitterWidth);
            if (FixedPanel is FixedPanel.Panel1 or FixedPanel.Panel2)
            {
                var dpi = Math.Max(1, DeviceDpi);
                _logicalFixedPanelSize = (FixedPanel == FixedPanel.Panel1
                    ? base.SplitterDistance
                    : Math.Max(0, available - base.SplitterDistance)) * 96f / dpi;
                _hasLogicalFixedSize = true;
            }
            else
            {
                _logicalSplitRatio = ModernCompatibility.Clamp(base.SplitterDistance / (float)available, 0f, 1f);
                _hasLogicalSplitRatio = true;
            }
        }
    }

    /// <summary>首次布局时 Panel2 的尺寸；设为 0 时使用 SplitterDistance。</summary>
    [Category("Layout"), DefaultValue(82)]
    [Description("控件首次布局时 Panel2 的初始宽度或高度；设置为 0 时使用 SplitterDistance。")]
    public int InitialPanel2Size
    {
        get => _initialPanel2Size;
        set
        {
            var next = Math.Max(0, value);
            if (_initialPanel2Size == next) return;
            _initialPanel2Size = next;
            _initialDistanceApplied = false;
            if (IsHandleCreated) LayoutTransaction.Request();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _lastDeviceDpi = Math.Max(1, DeviceDpi);
        _logicalSplitterWidth = SplitterWidth * 96f / _lastDeviceDpi;
        var available = Math.Max(1,
            (Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width) - SplitterWidth);
        if (FixedPanel is FixedPanel.Panel1 or FixedPanel.Panel2)
        {
            _logicalFixedPanelSize = FixedPanel == FixedPanel.Panel2 && InitialPanel2Size > 0
                ? InitialPanel2Size
                : (FixedPanel == FixedPanel.Panel1 ? SplitterDistance : Math.Max(0, available - SplitterDistance)) *
                  96f / _lastDeviceDpi;
            _hasLogicalFixedSize = true;
        }
        else
        {
            _logicalSplitRatio = ModernCompatibility.Clamp(SplitterDistance / (float)available, 0f, 1f);
            _hasLogicalSplitRatio = true;
        }
        _initialDistanceApplied = false;
        LayoutTransaction.Request();
    }

    protected override void OnDpiChangedBeforeParent(EventArgs e)
    {
        _dpiSettling = true;
        CaptureLogicalDistanceBeforeDpi();
        base.OnDpiChangedBeforeParent(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        SplitterWidth = Math.Max(1, (int)Math.Round(_logicalSplitterWidth * DeviceDpi / 96f));
        _dpiDistancePending = true;
        _dpiSettling = true;
        var settleVersion = ++_dpiSettleVersion;
        LayoutTransaction.Request();
        // Child DPI callbacks run before the top-level Form completes its deferred PMv2 layout.
        // Recommit once more on the following message-loop turn so SplitContainer cannot restore
        // stale panel HWND bounds after this control's first correction.
        if (IsHandleCreated)
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    _dpiDistancePending = true;
                    LayoutTransaction.Request();
                    BeginInvoke((Action)(() =>
                    {
                        if (settleVersion == _dpiSettleVersion) _dpiSettling = false;
                    }));
                }));
            }));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        ApplyPersistentLogicalDistance();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_dpiSettling || !IsHandleCreated) return;
        _dpiDistancePending = true;
        LayoutTransaction.Request();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hovered = SplitterRectangle.Contains(e.Location);
        if (_splitterHovered != hovered)
        {
            _splitterHovered = hovered;
            Invalidate(SplitterRectangle);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_splitterMoving && _splitterHovered)
        {
            _splitterHovered = false;
            Invalidate(SplitterRectangle);
        }
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Panel1Collapsed || Panel2Collapsed) return;
        var splitter = SplitterRectangle;
        if (splitter.Width <= 0 || splitter.Height <= 0) return;
        _lastPaintedSplitter = splitter;
        var active = _splitterHovered || _splitterMoving;
        using (var background = new SolidBrush(active ? Theme.PrimaryBackground : Theme.Background))
            e.Graphics.FillRectangle(background, splitter);

        RectangleF grip;
        if (Orientation == Orientation.Horizontal)
        {
            var minimumLength = ScaleLogical(20);
            var length = Math.Min(Math.Max(minimumLength, ScaleLogical(GripLength)),
                Math.Max(minimumLength, splitter.Width - ScaleLogical(16)));
            var thickness = ScaleLogical(active ? 3f : 2f);
            grip = new RectangleF(splitter.X + (splitter.Width - length) / 2f,
                splitter.Y + (splitter.Height - thickness) / 2f, length, thickness);
        }
        else
        {
            var minimumLength = ScaleLogical(20);
            var length = Math.Min(Math.Max(minimumLength, ScaleLogical(GripLength)),
                Math.Max(minimumLength, splitter.Height - ScaleLogical(16)));
            var thickness = ScaleLogical(active ? 3f : 2f);
            grip = new RectangleF(splitter.X + (splitter.Width - thickness) / 2f,
                splitter.Y + (splitter.Height - length) / 2f, thickness, length);
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Geometry.CreateRoundedRectangle(grip, Math.Min(grip.Width, grip.Height) / 2f);
        using var brush = new SolidBrush(active ? Theme.PrimaryHover : Theme.BorderSecondary);
        e.Graphics.FillPath(brush, path);
    }

    private void RepaintSplitterTransition(Rectangle previous, Rectangle current)
    {
        if (previous.IsEmpty || previous == current)
        {
            Invalidate(current);
            return;
        }

        var padding = Math.Max(1, ScaleLogical(1));
        previous.Inflate(padding, padding);
        current.Inflate(padding, padding);
        Invalidate(previous);
        Invalidate(current);
        InvalidatePanelChrome(Panel1, previous);
        InvalidatePanelChrome(Panel2, previous);

        // SplitterPanel uses child HWNDs. Merely invalidating the SplitContainer leaves the old
        // splitter pixels visible beneath the panel until an unrelated paint happens. Commit the
        // narrow old/new bands now so consecutive small moves cannot accumulate chrome trails.
        Update();
        Panel1.Update();
        Panel2.Update();
    }

    private static void InvalidatePanelChrome(SplitterPanel panel, Rectangle splitterBounds)
    {
        splitterBounds.Offset(-panel.Left, -panel.Top);
        splitterBounds.Intersect(panel.ClientRectangle);
        if (!splitterBounds.IsEmpty) panel.Invalidate(splitterBounds, invalidateChildren: true);
    }

    private int ScaleLogical(int value) => ModernDpi.ScaleToInt(value, DeviceDpi);
    private float ScaleLogical(float value) => value * DeviceDpi / 96f;

    private void ApplyPersistentLogicalDistance()
    {
        if (!_hasLogicalFixedSize || _applyingLogicalDistance || !IsHandleCreated ||
            Panel1Collapsed || Panel2Collapsed || FixedPanel is not (FixedPanel.Panel1 or FixedPanel.Panel2)) return;
        var length = Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width;
        var available = length - SplitterWidth;
        if (available <= Panel1MinSize + Panel2MinSize) return;
        var fixedPixels = (int)Math.Round(_logicalFixedPanelSize * DeviceDpi / 96f);
        var target = FixedPanel == FixedPanel.Panel1 ? fixedPixels : available - fixedPixels;
        var maximum = available - Panel2MinSize;
        target = ModernCompatibility.Clamp(target, Panel1MinSize, maximum);
        var actualFixedSize = FixedPanel == FixedPanel.Panel1
            ? (Orientation == Orientation.Horizontal ? Panel1.Height : Panel1.Width)
            : (Orientation == Orientation.Horizontal ? Panel2.Height : Panel2.Width);
        var expectedFixedSize = FixedPanel == FixedPanel.Panel1 ? target : available - target;
        if (SplitterDistance == target && Math.Abs(actualFixedSize - expectedFixedSize) <= 1) return;
        try
        {
            _applyingLogicalDistance = true;
            if (SplitterDistance == target)
            {
                var alternate = target < maximum ? target + 1 : target - 1;
                if (alternate >= Panel1MinSize && alternate <= maximum)
                    SplitterDistance = alternate;
            }
            SplitterDistance = target;
            PerformLayout();
        }
        finally { _applyingLogicalDistance = false; }
    }

    private void ApplyFinalLayout()
    {
        if (_dpiDistancePending)
        {
            _dpiDistancePending = false;
            ApplyCapturedDpiDistance();
        }
        else ApplyInitialDistance();
        Invalidate();
    }

    private void CaptureLogicalDistanceBeforeDpi()
    {
        var oldDpi = Math.Max(1, _lastDeviceDpi);
        var length = Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width;
        var available = Math.Max(1, length - SplitterWidth);
        _fixedPanelBeforeDpi = FixedPanel;
        if (!_hasLogicalFixedSize && _fixedPanelBeforeDpi is FixedPanel.Panel1 or FixedPanel.Panel2)
        {
            _logicalFixedPanelSize = (_fixedPanelBeforeDpi == FixedPanel.Panel1
                ? SplitterDistance
                : Math.Max(0, available - SplitterDistance)) * 96f / oldDpi;
            _hasLogicalFixedSize = true;
        }
        if (_fixedPanelBeforeDpi == FixedPanel.None)
        {
            if (!_hasLogicalSplitRatio)
            {
                _logicalSplitRatio = ModernCompatibility.Clamp(SplitterDistance / (float)available, 0f, 1f);
                _hasLogicalSplitRatio = true;
            }
            _splitRatioBeforeDpi = _logicalSplitRatio;
        }
        else _splitRatioBeforeDpi = ModernCompatibility.Clamp(SplitterDistance / (float)available, 0f, 1f);
    }

    private void ApplyCapturedDpiDistance()
    {
        if (Panel1Collapsed || Panel2Collapsed) return;
        // SplitContainer can retain the old monitor's cross-axis SplitterPanel HWND bounds even
        // after its own Bounds are correct. A reversible one-pixel size transaction makes the base
        // control rebuild both panel rectangles before restoring the logical splitter distance.
        var settledSize = Size;
        Size = new Size(settledSize.Width + 1, settledSize.Height + 1);
        Size = settledSize;
        var length = Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width;
        var available = length - SplitterWidth;
        if (available <= Panel1MinSize + Panel2MinSize) return;
        var distance = _fixedPanelBeforeDpi switch
        {
            FixedPanel.Panel1 => (int)Math.Round(_logicalFixedPanelSize * DeviceDpi / 96f),
            FixedPanel.Panel2 => available - (int)Math.Round(_logicalFixedPanelSize * DeviceDpi / 96f),
            _ => (int)Math.Round(available * _splitRatioBeforeDpi)
        };
        var target = ModernCompatibility.Clamp(distance,
            Panel1MinSize, available - Panel2MinSize);
        try
        {
            _applyingLogicalDistance = true;
            // Assigning the same distance is a no-op in SplitContainer even when its native panel HWNDs
            // still carry pre-transition bounds. A one-pixel legal nudge forces the base layout path.
            if (SplitterDistance == target)
            {
                var alternate = target < available - Panel2MinSize ? target + 1 : target - 1;
                if (alternate >= Panel1MinSize && alternate <= available - Panel2MinSize)
                    SplitterDistance = alternate;
            }
            SplitterDistance = target;
            PerformLayout();
        }
        finally { _applyingLogicalDistance = false; }
        _lastDeviceDpi = Math.Max(1, DeviceDpi);
        _hasLogicalFixedSize = FixedPanel is FixedPanel.Panel1 or FixedPanel.Panel2;
        _initialDistanceApplied = true;
    }

    private void ApplyInitialDistance()
    {
        if (_initialDistanceApplied || InitialPanel2Size <= 0 || !IsHandleCreated) return;
        var settledSize = Size;
        Size = new Size(settledSize.Width + 1, settledSize.Height + 1);
        Size = settledSize;
        var length = Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width;
        if (length <= SplitterWidth + Panel1MinSize + Panel2MinSize)
        {
            if (FixedPanel == FixedPanel.Panel2)
            {
                var constrainedPanel2Size = Orientation == Orientation.Horizontal ? Panel2.Height : Panel2.Width;
                _logicalFixedPanelSize = constrainedPanel2Size * 96f / Math.Max(1, DeviceDpi);
                _hasLogicalFixedSize = true;
            }
            _initialDistanceApplied = true;
            return;
        }
        var panel2Size = ScaleLogical(InitialPanel2Size);
        var maximum = length - SplitterWidth - Panel2MinSize;
        var target = ModernCompatibility.Clamp(length - SplitterWidth - panel2Size,
            Panel1MinSize, maximum);
        try
        {
            _applyingLogicalDistance = true;
            if (SplitterDistance == target)
            {
                var alternate = target < maximum ? target + 1 : target - 1;
                if (alternate >= Panel1MinSize && alternate <= maximum)
                    SplitterDistance = alternate;
            }
            SplitterDistance = target;
            PerformLayout();
        }
        finally { _applyingLogicalDistance = false; }
        if (FixedPanel == FixedPanel.Panel2)
        {
            var actualPanel2Size = Orientation == Orientation.Horizontal ? Panel2.Height : Panel2.Width;
            _logicalFixedPanelSize = actualPanel2Size * 96f / Math.Max(1, DeviceDpi);
            _hasLogicalFixedSize = true;
        }
        _initialDistanceApplied = true;
    }
}
