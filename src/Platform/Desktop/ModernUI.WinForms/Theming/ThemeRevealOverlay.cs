using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ModernUI.WinForms;

/// <summary>Displays an old-theme client snapshot as a per-pixel-alpha layered window.</summary>
internal sealed class ThemeRevealOverlay : Form
{
    private readonly Control _root;
    private readonly Bitmap _oldFrame;
    private readonly Bitmap _compositedFrame;
    private readonly Point _origin;
    private readonly float _maximumRadius;
    private IDisposable? _animation;

    public ThemeRevealOverlay(Control root, Bitmap oldFrame, Point origin)
    {
        _root = root;
        _oldFrame = (Bitmap)oldFrame.Clone();
        _compositedFrame = new Bitmap(oldFrame.Width, oldFrame.Height, PixelFormat.Format32bppPArgb);
        _origin = origin;
        _maximumRadius = CalculateMaximumRadius(origin, oldFrame.Size);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= LayeredWindowNative.OverlayExtendedStyles;
            return parameters;
        }
    }

    public void PresentInitialFrame()
    {
        RenderFrame(0);
        LayeredWindowNative.FlushDesktopComposition();
    }

    public void Start(int duration)
    {
        _animation = ModernAnimation.Start(this, 0, 1, duration, RenderFrame, Complete);
    }

    public void Cancel()
    {
        if (IsDisposed) return;
        _animation?.Dispose();
        _animation = null;
        Close();
    }

    private void RenderFrame(float progress)
    {
        if (!IsHandleCreated || IsDisposed) return;

        using (var graphics = Graphics.FromImage(_compositedFrame))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(_oldFrame, Point.Empty);
            if (progress > 0)
            {
                var radius = _maximumRadius * progress;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using var transparentBrush = new SolidBrush(Color.Transparent);
                graphics.FillEllipse(transparentBrush,
                    _origin.X - radius, _origin.Y - radius, radius * 2, radius * 2);
            }
        }

        LayeredWindowNative.Present(Handle, Location, _compositedFrame);
    }

    private void Complete()
    {
        _animation = null;
        Cancel();
        if (!_root.IsDisposed) _root.Invalidate(true);
    }

    private static float CalculateMaximumRadius(Point origin, Size size)
    {
        static float Distance(Point originPoint, int x, int y)
        {
            var deltaX = originPoint.X - x;
            var deltaY = originPoint.Y - y;
            return ModernCompatibility.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        return Math.Max(
            Math.Max(Distance(origin, 0, 0), Distance(origin, size.Width, 0)),
            Math.Max(Distance(origin, 0, size.Height), Distance(origin, size.Width, size.Height)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation?.Dispose();
            _oldFrame.Dispose();
            _compositedFrame.Dispose();
        }
        base.Dispose(disposing);
    }
}
