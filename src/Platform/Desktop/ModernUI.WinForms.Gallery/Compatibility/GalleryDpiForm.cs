using System.Runtime.InteropServices;

namespace ModernUI.WinForms.Gallery;

/// <summary>Keeps the last composed frame visible while WinForms scales a native child HWND tree.</summary>
internal class GalleryDpiForm : Form
{
    private const int WindowDpiChanged = 0x02E0;
    private const int WindowSetRedraw = 0x000B;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawErase = 0x0004;
    private const uint RedrawAllChildren = 0x0080;
    private const uint RedrawFrame = 0x0400;
    private bool _dpiRedrawSuspended;
    private int _dpiTransactionVersion;

    protected GalleryDpiForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override bool ShowWithoutActivation =>
        GalleryApplication.ShowWithoutActivationForProbe || base.ShowWithoutActivation;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg != WindowDpiChanged)
        {
            base.WndProc(ref message);
            return;
        }

        var version = ++_dpiTransactionVersion;
        BeginDpiTransaction();
        try
        {
            base.WndProc(ref message);
        }
        catch
        {
            CompleteDpiTransaction();
            throw;
        }

        if (!IsDisposed && IsHandleCreated)
            BeginInvoke((Action)(() => CompleteDpiTransaction(version)));
        else
            CompleteDpiTransaction();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _dpiTransactionVersion++;
        if (_dpiRedrawSuspended)
        {
            _dpiRedrawSuspended = false;
            ResumeLayout(false);
        }
        base.OnHandleDestroyed(e);
    }

    private void BeginDpiTransaction()
    {
        if (_dpiRedrawSuspended || !IsHandleCreated) return;
        _dpiRedrawSuspended = true;
        SuspendLayout();
        SendMessage(Handle, WindowSetRedraw, IntPtr.Zero, IntPtr.Zero);
    }

    private void CompleteDpiTransaction(int version)
    {
        if (version != _dpiTransactionVersion) return;
        CompleteDpiTransaction();
    }

    private void CompleteDpiTransaction()
    {
        if (!_dpiRedrawSuspended) return;
        _dpiRedrawSuspended = false;
        ResumeLayout(true);
        if (!IsHandleCreated || IsDisposed) return;
        SendMessage(Handle, WindowSetRedraw, new IntPtr(1), IntPtr.Zero);
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
            RedrawInvalidate | RedrawErase | RedrawAllChildren | RedrawFrame);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);
}
