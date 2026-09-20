using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

/// <summary>为选择类控件提供统一的圆角、可动画且自动避让屏幕边缘的弹出层。</summary>
internal sealed class ModernPopupController : IDisposable, IMessageFilter
{
    [ThreadStatic]
    private static ModernPopupController? _active;
    private readonly RoundedPopupForm _popup = new();
    private readonly Control _content;
    private Font? _ownedContentFont;
    private bool _hiding;
    private bool _filterInstalled;
    private readonly ModernOverlayAnchorTracker _anchorTracker;
    private Control? _anchor;
    private Form? _ownerForm;

    public ModernPopupController(Control content)
    {
        _content = content;
        _anchorTracker = new ModernOverlayAnchorTracker(AnchorGeometryChanged);
        _popup.Controls.Add(content);
        _popup.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            HidePopup();
        };
    }

    public bool Visible => _popup.Visible;
    public event EventHandler? Closed;

    public void ApplyTheme(ModernTheme theme)
    {
        _content.BackColor = theme.Elevated;
        _content.ForeColor = theme.Text;
        _popup.SurfaceColor = theme.Elevated;
        _popup.BorderColor = theme.Border;
        _popup.CornerRadius = Math.Max(0, theme.Radius);
        _popup.Invalidate();
    }

    public void Show(Control owner, Size contentSize, int gap, int duration)
    {
        _ = duration; // Kept for API compatibility; top-level popup geometry is no longer animated.
        if (_popup.Visible) return;
        var previous = _active;
        _active = this;
        if (previous is not null && !ReferenceEquals(previous, this)) previous.HidePopup();
        _anchor = owner;
        _anchor.FontChanged += AnchorFontChanged;
        _anchorTracker.Track(owner);
        _ownerForm = owner.FindForm();
        if (_ownerForm is not null) _ownerForm.Deactivate += OwnerForm_Deactivate;
        if (!_filterInstalled)
        {
            Application.AddMessageFilter(this);
            _filterInstalled = true;
        }
        _popup.DpiScale = owner.DeviceDpi / 96f;
        SynchronizeContentFont(owner.Font);
        var width = Math.Max(2, contentSize.Width);
        var targetContentHeight = Math.Max(1, contentSize.Height);
        var borderInset = Math.Max(1, (int)Math.Round(_popup.DpiScale));
        var targetPopupHeight = targetContentHeight + borderInset * 2;

        var origin = owner.PointToScreen(Point.Empty);
        var workingArea = Screen.FromControl(owner).WorkingArea;
        var below = origin.Y + owner.Height + gap;
        var opensBelow = below + targetPopupHeight <= workingArea.Bottom;
        var finalY = opensBelow
            ? below
            : Math.Max(workingArea.Top, origin.Y - gap - targetPopupHeight);
        var rightToLeft = owner.RightToLeft == RightToLeft.Yes || owner.FindForm()?.RightToLeft == RightToLeft.Yes;
        var preferredX = rightToLeft ? origin.X + owner.Width - width : origin.X;
        var x = ModernCompatibility.Clamp(preferredX, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width));
        // 内容始终保持最终尺寸，只动画裁剪窗口高度，避免 ListBox/自绘列表每帧重新布局和重绘而闪烁。
        _content.Size = new Size(Math.Max(0, width - borderInset * 2), targetContentHeight);
        // Create and lay out every HWND while the popup is still hidden. Neither an opacity-zero
        // Show nor WM_SETREDRAW around a visible Form is safe here: DWM may compose that staging
        // state as a small native-looking blank rectangle. The first visible window therefore
        // already has its final bounds, font, region, and child handles.
        SetPopupFrame(width, targetContentHeight, targetContentHeight, opensBelow);
        _popup.Location = new Point(x, finalY);
        _popup.ShowFinalFrame(_ownerForm,
            prepareHiddenFrame: () =>
            {
                _popup.PrepareHiddenFrame();
                // net48 can create this hidden PMv2 HWND with its stale system-DPI cache and mutate
                // Form and child bounds. Once every HWND exists, recommit the physical frame.
                CommitFrame();
                _popup.PerformLayout();
            },
            commitFinalFrame: CommitFrame);

        void CommitFrame()
        {
            // Show(owner) is the final net48 PMv2 font boundary. The same commit runs before and
            // after visibility so the first DWM frame already uses final typography and geometry.
            SynchronizeContentFont(owner.Font);
            _content.Size = new Size(Math.Max(0, width - borderInset * 2), targetContentHeight);
            _popup.SetPhysicalSize(new Size(width, targetPopupHeight));
            _popup.Location = new Point(x, finalY);
            SetPopupFrame(width, targetContentHeight, targetContentHeight, opensBelow);
            _popup.UpdateRoundedRegion();
        }
    }

    private void SynchronizeContentFont(Font ownerFont)
    {
        if (_ownedContentFont is not null && ReferenceEquals(_content.Font, _ownedContentFont) &&
            string.Equals(_ownedContentFont.FontFamily.Name, ownerFont.FontFamily.Name, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(_ownedContentFont.SizeInPoints - ownerFont.SizeInPoints) < .01f &&
            _ownedContentFont.Style == ownerFont.Style)
            return;

        var normalizedFont = new Font(ownerFont.FontFamily, ownerFont.SizeInPoints,
            ownerFont.Style, GraphicsUnit.Point);
        var previous = _ownedContentFont;
        _ownedContentFont = normalizedFont;
        _content.Font = normalizedFont;
        previous?.Dispose();
    }

    public void Close() => HidePopup();

    private void SetPopupFrame(int width, int visibleContentHeight, int fullContentHeight, bool opensBelow)
    {
        var borderInset = Math.Max(1, (int)Math.Round(_popup.DpiScale));
        _popup.Size = new Size(width, visibleContentHeight + borderInset * 2);
        _content.Location = opensBelow
            ? new Point(borderInset, borderInset)
            : new Point(borderInset, visibleContentHeight - fullContentHeight + borderInset);
    }

    private void HidePopup()
    {
        if (_hiding || !_popup.Visible) return;
        _hiding = true;
        try
        {
            _popup.Hide();
            if (ReferenceEquals(_active, this)) _active = null;
            RemoveCloseTracking();
            Closed?.Invoke(this, EventArgs.Empty);
        }
        finally { _hiding = false; }
    }

    public bool PreFilterMessage(ref Message m)
    {
        const int WmLButtonDown = 0x0201;
        const int WmRButtonDown = 0x0204;
        const int WmMButtonDown = 0x0207;
        const int WmNcLButtonDown = 0x00A1;
        const int WmMouseWheel = 0x020A;
        const int WmMouseHWheel = 0x020E;
        if (!_popup.Visible) return false;

        var targetsPopup = IsPopupMessageTarget(m.HWnd);
        var point = Cursor.Position;
        if (m.Msg is WmMouseWheel or WmMouseHWheel)
        {
            // 弹出层是独立顶层窗口。父滚动容器滚动时锚点会移动，继续显示会造成弹层悬空。
            if (!targetsPopup && !_popup.Bounds.Contains(point)) HidePopup();
            return false;
        }

        if (m.Msg is not (WmLButtonDown or WmRButtonDown or WmMButtonDown or WmNcLButtonDown))
            return false;
        // 消息目标比 Cursor.Position 更可靠；快速点击时系统光标坐标可能尚未同步到当前消息位置。
        if (targetsPopup) return false;
        var anchorBounds = _anchor is { IsDisposed: false }
            ? _anchor.RectangleToScreen(_anchor.ClientRectangle)
            : Rectangle.Empty;
        if (!_popup.Bounds.Contains(point) && !anchorBounds.Contains(point)) HidePopup();
        return false;
    }

    private bool IsPopupMessageTarget(IntPtr windowHandle)
    {
        var target = Control.FromHandle(windowHandle);
        return target is not null && (ReferenceEquals(target, _popup) || _popup.Contains(target));
    }

    private void OwnerForm_Deactivate(object? sender, EventArgs e)
    {
        // WS_EX_NOACTIVATE 通常不会让宿主失活；部分窗口环境仍会在从锚点打开弹层时短暂触发 Deactivate。
        // 光标仍位于锚点或弹层内时属于同一次打开交互，不能立即关闭刚显示的窗口。
        var anchorBounds = _anchor is { IsDisposed: false }
            ? _anchor.RectangleToScreen(_anchor.ClientRectangle)
            : Rectangle.Empty;
        if (_popup.Visible
            && (_popup.Bounds.Contains(Cursor.Position) || anchorBounds.Contains(Cursor.Position)))
        {
            return;
        }
        HidePopup();
    }

    private void AnchorGeometryChanged(Control? anchor, Rectangle bounds)
    {
        // The tracker filters redundant WinForms Layout/SizeChanged notifications. A managed select
        // popup closes only when its anchor's actual screen geometry or visibility changes.
        if (anchor is null || bounds.IsEmpty || _popup.Visible) HidePopup();
    }

    private void AnchorFontChanged(object? sender, EventArgs e)
    {
        if (!_popup.Visible || sender is not Control owner || !ReferenceEquals(owner, _anchor)) return;
        SynchronizeContentFont(owner.Font);
        _content.Invalidate();
        _popup.Invalidate();
    }

    private void RemoveCloseTracking()
    {
        _anchorTracker.Untrack();

        if (_filterInstalled)
        {
            Application.RemoveMessageFilter(this);
            _filterInstalled = false;
        }
        if (_ownerForm is not null) _ownerForm.Deactivate -= OwnerForm_Deactivate;
        _ownerForm = null;
        if (_anchor is not null) _anchor.FontChanged -= AnchorFontChanged;
        _anchor = null;
    }

    public void Dispose()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        RemoveCloseTracking();
        _anchorTracker.Dispose();
        _popup.Dispose();
        _ownedContentFont?.Dispose();
        _ownedContentFont = null;
    }

    /// <summary>完全自绘的无边框圆角弹窗，避免 ToolStripDropDown 的矩形系统边缘。</summary>
    private sealed class RoundedPopupForm : ModernOwnedOverlayForm
    {
        public RoundedPopupForm()
        {
            KeyPreview = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public Color SurfaceColor { get; set; } = Color.White;
        public Color BorderColor { get; set; } = Color.Gray;
        public int CornerRadius { get; set; } = 6;
        public float DpiScale { get; set; } = 1;

        public void PrepareHiddenFrame()
        {
            _ = Handle;
            CreateChildHandles(this);
            PerformLayout();
            UpdateRoundedRegion();
        }

        public void SetPhysicalSize(Size size)
        {
            if (!IsHandleCreated)
            {
                Size = size;
                return;
            }
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, size.Width, size.Height,
                0x0001 | 0x0002 | 0x0004 | 0x0010);
        }

        private static void CreateChildHandles(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                _ = child.Handle;
                CreateChildHandles(child);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(SurfaceColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var strokeWidth = Math.Max(1f, DpiScale);
            var halfStroke = strokeWidth / 2f;
            var bounds = new RectangleF(halfStroke, halfStroke,
                Math.Max(0, ClientSize.Width - strokeWidth), Math.Max(0, ClientSize.Height - strokeWidth));
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using var path = CreateRoundedPath(bounds, ScaledRadius);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(BorderColor, strokeWidth) { Alignment = PenAlignment.Inset };
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRoundedRegion();
        }

        public void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using var path = CreateRoundedPath(new RectangleF(0, 0, Width, Height), ScaledRadius);
            var oldRegion = Region;
            Region = new Region(path);
            oldRegion?.Dispose();
            Invalidate();
        }

        private float ScaledRadius => Math.Min(Math.Min(Width, Height) / 2f, CornerRadius * DpiScale);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(0, radius * 2);
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
