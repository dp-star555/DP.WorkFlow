using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>
/// Owns the Win32 contract shared by framework top-level overlays. The first visible frame is
/// committed only after the HWND exists, and every hosted overlay remains owned by its host Form.
/// </summary>
internal class ModernOwnedOverlayForm : Form
{
    private readonly bool _compositedSurface;

    protected ModernOwnedOverlayForm(bool compositedSurface = false)
    {
        _compositedSurface = compositedSurface;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int noActivate = 0x08000000;
            const int toolWindow = 0x00000080;
            const int composited = 0x02000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= noActivate | toolWindow;
            if (_compositedSurface) parameters.ExStyle |= composited;
            // Do not add CS_DROPSHADOW here. Windows may implement it as a second visible,
            // unowned top-level HWND, which can replace the real owner in process/Z-order queries.
            return parameters;
        }
    }

    /// <summary>Creates this overlay and all child HWNDs without making the top-level window visible.</summary>
    internal void PrepareCompleteHandleTree()
    {
        _ = Handle;
        CreateChildHandles(this);
        PerformLayout();
    }

    /// <summary>Creates the complete hidden HWND tree, shows it with an owner, then recommits the final PMv2 frame.</summary>
    internal void ShowFinalFrame(Form? owner, Action prepareHiddenFrame, Action commitFinalFrame)
    {
        ModernCompatibility.ThrowIfNull(prepareHiddenFrame, nameof(prepareHiddenFrame));
        ModernCompatibility.ThrowIfNull(commitFinalFrame, nameof(commitFinalFrame));
        if (IsDisposed) return;
        if (Visible)
        {
            commitFinalFrame();
            return;
        }

        prepareHiddenFrame();
        commitFinalFrame();
        PrimeHiddenBackingSurface();
        if (owner is { IsDisposed: false }) Show(owner);
        else Show();
        commitFinalFrame();
        CommitFirstVisiblePaint();
    }

    private void PrimeHiddenBackingSurface()
    {
        if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
        // RedrawWindow does not establish a DWM backing surface for an unmapped top-level HWND.
        // Render the complete managed overlay tree into one bitmap and copy it to the hidden window
        // DC before WS_VISIBLE. These overlays are intentionally small; this happens once per show.
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        using var graphics = Graphics.FromHwnd(Handle);
        graphics.DrawImageUnscaled(bitmap, Point.Empty);
    }

    private void CommitFirstVisiblePaint()
    {
        if (!IsHandleCreated || IsDisposed) return;
        const uint invalidate = 0x0001;
        const uint erase = 0x0004;
        const uint allChildren = 0x0080;
        const uint updateNow = 0x0100;
        const uint frame = 0x0400;
        // This is a one-time small-overlay commit, not an animation-frame refresh. It completes the
        // final PMv2 recommit after Show(owner); the hidden backing surface above covers the earlier
        // WS_VISIBLE boundary.
        _ = RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
            invalidate | erase | allChildren | updateNow | frame);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle,
        IntPtr updateRegion, uint flags);

    private static void CreateChildHandles(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            _ = child.Handle;
            CreateChildHandles(child);
        }
    }
}

/// <summary>Tracks an overlay anchor and centralizes hierarchy event cleanup.</summary>
internal sealed class ModernOverlayAnchorTracker : IDisposable
{
    private readonly List<Control> _sources = [];
    private readonly Action<Control?, Rectangle> _geometryChanged;
    private Control? _anchor;
    private Rectangle _lastBounds;

    public ModernOverlayAnchorTracker(Action<Control?, Rectangle> geometryChanged) =>
        _geometryChanged = geometryChanged ?? throw new ArgumentNullException(nameof(geometryChanged));

    public Control? Anchor => _anchor;
    public Rectangle LastBounds => _lastBounds;

    public void Track(Control anchor)
    {
        ModernCompatibility.ThrowIfNull(anchor, nameof(anchor));
        Untrack();
        _anchor = anchor;
        _lastBounds = GetScreenBounds(anchor);
        for (Control? current = anchor; current is not null; current = current.Parent)
        {
            _sources.Add(current);
            current.LocationChanged += SourceGeometryChanged;
            current.SizeChanged += SourceGeometryChanged;
            current.VisibleChanged += SourceGeometryChanged;
            current.ParentChanged += SourceGeometryChanged;
            current.HandleDestroyed += SourceGeometryChanged;
            if (current is ScrollableControl scrollable) scrollable.Scroll += SourceGeometryChanged;
        }
    }

    public void RefreshBaseline()
    {
        if (_anchor is { IsDisposed: false, IsHandleCreated: true } anchor)
            _lastBounds = GetScreenBounds(anchor);
    }

    public void Untrack()
    {
        foreach (var source in _sources)
        {
            source.LocationChanged -= SourceGeometryChanged;
            source.SizeChanged -= SourceGeometryChanged;
            source.VisibleChanged -= SourceGeometryChanged;
            source.ParentChanged -= SourceGeometryChanged;
            source.HandleDestroyed -= SourceGeometryChanged;
            if (source is ScrollableControl scrollable) scrollable.Scroll -= SourceGeometryChanged;
        }
        _sources.Clear();
        _anchor = null;
        _lastBounds = Rectangle.Empty;
    }

    private void SourceGeometryChanged(object? sender, EventArgs e)
    {
        if (_anchor is not { IsDisposed: false, IsHandleCreated: true, Visible: true } anchor)
        {
            _geometryChanged(_anchor ?? sender as Control, Rectangle.Empty);
            return;
        }

        var bounds = GetScreenBounds(anchor);
        if (bounds == _lastBounds) return;
        _lastBounds = bounds;
        _geometryChanged(anchor, bounds);
    }

    private static Rectangle GetScreenBounds(Control control) =>
        control.RectangleToScreen(control.ClientRectangle);

    public void Dispose() => Untrack();
}
