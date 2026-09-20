using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

public sealed partial class ModernListView
{
    private sealed class HeaderWindow : NativeWindow
    {
        private readonly ModernListView _owner;

        public HeaderWindow(ModernListView owner, nint handle)
        {
            _owner = owner;
            AssignHandle(handle);
        }

        public void Release()
        {
            if (Handle != IntPtr.Zero) ReleaseHandle();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WindowPaint && Handle != IntPtr.Zero) _owner.PaintHeaderRemainder(Handle);
        }
    }

    private void LayoutScrollBars()
    {
        if (_verticalScrollBar is null || _horizontalScrollBar is null) return;
        var vertical = _verticalScrollBar.Visible;
        var horizontal = _horizontalScrollBar.Visible;
        var verticalWidth = SystemInformation.VerticalScrollBarWidth;
        var horizontalHeight = SystemInformation.HorizontalScrollBarHeight;
        var verticalLeft = RightToLeft == RightToLeft.Yes ? 0 : Math.Max(0, ClientSize.Width - verticalWidth);
        _verticalScrollBar.Bounds = new Rectangle(verticalLeft, 0,
            verticalWidth, Math.Max(0, ClientSize.Height - (horizontal ? horizontalHeight : 0)));
        var horizontalLeft = vertical && RightToLeft == RightToLeft.Yes ? verticalWidth : 0;
        _horizontalScrollBar.Bounds = new Rectangle(horizontalLeft,
            Math.Max(0, ClientSize.Height - horizontalHeight),
            Math.Max(0, ClientSize.Width - (vertical ? verticalWidth : 0)), horizontalHeight);
        _scrollCorner.Visible = vertical && horizontal;
        if (_scrollCorner.Visible)
            _scrollCorner.Bounds = new Rectangle(verticalLeft,
                ClientSize.Height - horizontalHeight, verticalWidth, horizontalHeight);
        _verticalScrollBar.BringToFront();
        _horizontalScrollBar.BringToFront();
        _scrollCorner.BringToFront();
    }

    private void ScrollHorizontallyTo(int position)
    {
        if (!IsHandleCreated) return;
        const int listViewScroll = 0x1014;
        var current = GetScrollPos(Handle, 0);
        var delta = position - current;
        if (delta != 0) SendMessage(Handle, listViewScroll, (nint)delta, IntPtr.Zero);
    }

    private int ScaleLogical(int logicalPixels) => ModernDpi.ScaleToInt(logicalPixels, DeviceDpi);

    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr window, int bar);

    private void InvalidateItem(int index)
    {
        if (index < 0 || index >= Items.Count || !IsHandleCreated) return;
        Invalidate(GetItemRect(index));
    }
}
