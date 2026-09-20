using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>保留原生日期时间交互，并覆盖系统公共控件中无法主题化的静态表面。</summary>
internal sealed class ModernNativeDateTimePicker : DateTimePicker
{
    private ModernTheme _modernTheme = ModernTheme.Light;
    private bool _suppressNativeDropDown;
    private int _selectedTimePart;
    public bool ReadOnly { get; set; }
    public Action? ManagedDropDownRequested { get; set; }
    public Func<Keys, bool>? ManagedKeyHandler { get; set; }
    public event EventHandler? NativePointerDown;

    public ModernTheme ModernTheme
    {
        get => _modernTheme;
        set
        {
            _modernTheme = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate();
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int wmPaint = 0x000F;
        const int wmLButtonDown = 0x0201;
        const int wmLButtonUp = 0x0202;
        const int wmKeyDown = 0x0100;
        const int wmSysKeyDown = 0x0104;
        const int wmChar = 0x0102;
        const int wmMouseWheel = 0x020A;
        const int wmSetFocus = 0x0007;
        const int wmKillFocus = 0x0008;
        const int wmReflectNotify = 0x204e;
        const int dtmGetMonthCalendar = 0x1008;
        const int dtnDropDown = -754;
        if (ReadOnly && m.Msg is wmChar or wmMouseWheel)
        {
            Focus();
            m.Result = IntPtr.Zero;
            return;
        }
        if (ReadOnly && ShowUpDown && (m.Msg is wmLButtonDown or wmLButtonUp or wmKeyDown or wmSysKeyDown))
        {
            if (m.Msg is wmLButtonDown or wmKeyDown or wmSysKeyDown) Focus();
            m.Result = IntPtr.Zero;
            return;
        }
        if (!ShowUpDown && ManagedDropDownRequested is not null && m.Msg == wmReflectNotify && m.LParam != IntPtr.Zero)
        {
            var notification = Marshal.PtrToStructure<NativeHeader>(m.LParam);
            if (notification.Code == dtnDropDown)
            {
                HideNativeCalendar(dtmGetMonthCalendar);
                ManagedDropDownRequested();
                m.Result = IntPtr.Zero;
                return;
            }
        }
        if (!ShowUpDown && ManagedDropDownRequested is not null)
        {
            const int wmLButtonDblClk = 0x0203;
            const int wmNcLButtonDown = 0x00a1;
            const int wmNcLButtonDblClk = 0x00a3;
            const int wmContextMenu = 0x007b;
            var key = (Keys)(int)m.WParam;
            if (m.Msg == wmLButtonDown)
            {
                _suppressNativeDropDown = true;
                NativePointerDown?.Invoke(this, EventArgs.Empty);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == wmLButtonUp && _suppressNativeDropDown)
            {
                _suppressNativeDropDown = false;
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == wmLButtonDblClk || m.Msg is wmNcLButtonDown or wmNcLButtonDblClk or wmContextMenu)
            {
                NativePointerDown?.Invoke(this, EventArgs.Empty);
                m.Result = IntPtr.Zero;
                return;
            }
            if ((m.Msg is wmKeyDown or wmSysKeyDown) && ManagedKeyHandler?.Invoke(key) == true) return;
            if ((m.Msg is wmKeyDown or wmSysKeyDown) &&
                (key == Keys.F4 || key == Keys.Down && (GetKeyState((int)Keys.Menu) & 0x8000) != 0))
            {
                NativePointerDown?.Invoke(this, EventArgs.Empty);
                m.Result = IntPtr.Zero;
                return;
            }
            if (ReadOnly && m.Msg is wmKeyDown or wmSysKeyDown)
            {
                Focus();
                m.Result = IntPtr.Zero;
                return;
            }
        }
        if (ShowUpDown && m.Msg == wmKeyDown)
        {
            var key = (Keys)(int)m.WParam;
            if (key == Keys.Left) _selectedTimePart = Math.Max(0, _selectedTimePart - 1);
            else if (key == Keys.Right) _selectedTimePart = Math.Min(GetTimePartCount() - 1, _selectedTimePart + 1);
        }
        else if (ShowUpDown && m.Msg == wmLButtonDown)
        {
            var x = unchecked((short)((long)m.LParam & 0xffff));
            _selectedTimePart = HitTestTimePart(x);
        }
        base.WndProc(ref m);
        if (ShowUpDown && m.Msg is wmSetFocus or wmKillFocus or wmKeyDown or wmLButtonDown)
            Invalidate();
        if (!ShowUpDown && ManagedDropDownRequested is not null && m.Msg == dtmGetMonthCalendar && m.Result != IntPtr.Zero)
            _ = ShowWindow(m.Result, 0);
        if (m.Msg == wmPaint && IsHandleCreated) DrawModernSurface();
    }

    internal void StepSelectedPart(bool increase)
    {
        if (!Enabled || ReadOnly || !IsHandleCreated) return;
        Focus();
        const int wmKeyDown = 0x0100;
        const int wmKeyUp = 0x0101;
        var key = (IntPtr)(int)(increase ? Keys.Up : Keys.Down);
        _ = SendMessage(Handle, wmKeyDown, key, IntPtr.Zero);
        _ = SendMessage(Handle, wmKeyUp, key, IntPtr.Zero);
    }

    private void HideNativeCalendar(int getMonthCalendarMessage)
    {
        if (!IsHandleCreated) return;
        var calendar = SendMessage(Handle, getMonthCalendarMessage, IntPtr.Zero, IntPtr.Zero);
        if (calendar != IntPtr.Zero) _ = ShowWindow(calendar, 0);
    }

    private void DrawModernSurface()
    {
        using var graphics = Graphics.FromHwnd(Handle);
        graphics.Clear(ModernTheme.Control);
        var buttonWidth = Math.Min(24, Width);
        var textBounds = new Rectangle(7, 0, Math.Max(0, Width - buttonWidth - 9), Height);
        var text = ShowCheckBox && !Checked ? string.Empty : Value.ToString(CustomFormat ?? (ShowUpDown ? "HH:mm:ss" : "yyyy-MM-dd"));
        if (ShowUpDown && Focused && text.Length > 0)
            DrawFocusedTimeText(graphics, textBounds, text);
        else
            TextRenderer.DrawText(graphics, text, Font, textBounds, Enabled ? ModernTheme.Text : ModernTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using var separator = new Pen(ModernTheme.Border);
        graphics.DrawLine(separator, Width - buttonWidth, 3, Width - buttonWidth, Math.Max(3, Height - 4));
        if (!ShowUpDown)
        {
            var calendar = new Rectangle(Width - buttonWidth + 6, (Height - 12) / 2, 12, 12);
            using var pen = new Pen(ModernTheme.TextSecondary, 1);
            graphics.DrawRectangle(pen, calendar);
            graphics.DrawLine(pen, calendar.Left, calendar.Top + 4, calendar.Right, calendar.Top + 4);
        }
    }

    private void DrawFocusedTimeText(Graphics graphics, Rectangle bounds, string text)
    {
        var parts = text.Split(':');
        if (parts.Length < 2)
        {
            TextRenderer.DrawText(graphics, text, Font, bounds, Enabled ? ModernTheme.Text : ModernTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            return;
        }

        _selectedTimePart = Math.Min(_selectedTimePart, parts.Length - 1);
        var x = bounds.Left;
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        for (var index = 0; index < parts.Length; index++)
        {
            var partWidth = TextRenderer.MeasureText(graphics, parts[index], Font, Size.Empty, flags).Width;
            var partBounds = new Rectangle(x, bounds.Top, partWidth, bounds.Height);
            if (index == _selectedTimePart)
            {
                using var selection = new SolidBrush(ModernTheme.PrimaryBackground);
                graphics.FillRectangle(selection, Rectangle.Inflate(partBounds, 2, -3));
            }
            TextRenderer.DrawText(graphics, parts[index], Font, partBounds,
                Enabled ? index == _selectedTimePart ? ModernTheme.Primary : ModernTheme.Text : ModernTheme.TextDisabled,
                flags);
            x += partWidth;
            if (index >= parts.Length - 1) continue;
            var separatorWidth = TextRenderer.MeasureText(graphics, ":", Font, Size.Empty, flags).Width;
            TextRenderer.DrawText(graphics, ":", Font, new Rectangle(x, bounds.Top, separatorWidth, bounds.Height),
                Enabled ? ModernTheme.Text : ModernTheme.TextDisabled, flags);
            x += separatorWidth;
        }
    }

    private int HitTestTimePart(int x)
    {
        var parts = Value.ToString(CustomFormat ?? "HH:mm:ss").Split(':');
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var current = 7;
        for (var index = 0; index < parts.Length; index++)
        {
            var partWidth = TextRenderer.MeasureText(parts[index], Font, Size.Empty, flags).Width;
            if (x < current + partWidth) return index;
            current += partWidth;
            if (index < parts.Length - 1)
                current += TextRenderer.MeasureText(":", Font, Size.Empty, flags).Width;
        }
        return Math.Max(0, parts.Length - 1);
    }

    private int GetTimePartCount() => Math.Max(1,
        (CustomFormat ?? "HH:mm:ss").Split(':').Length);

    [StructLayout(LayoutKind.Sequential)] private struct NativeHeader { public IntPtr HandleFrom; public IntPtr IdFrom; public int Code; }
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}
