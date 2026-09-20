namespace ModernUI.WinForms;

/// <summary>保留原生 TextBox provider，并为组合控件提供可靠的 WM_MOUSEWHEEL 截获点。</summary>
internal sealed class ModernNativeTextBox : TextBox
{
    public Func<int, bool>? WheelHandler { get; set; }

#if NET48
    private string _placeholderText = string.Empty;
    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value ?? string.Empty;
            if (IsHandleCreated) SendMessage(Handle, 0x1501, new IntPtr(1), _placeholderText);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SendMessage(Handle, 0x1501, new IntPtr(1), _placeholderText);
    }
#endif

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x020A)
        {
            var delta = unchecked((short)((long)message.WParam >> 16));
            if (WheelHandler?.Invoke(delta) == true)
            {
                message.Result = IntPtr.Zero;
                return;
            }
        }
        base.WndProc(ref message);
    }

#if NET48
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);
#endif
}
