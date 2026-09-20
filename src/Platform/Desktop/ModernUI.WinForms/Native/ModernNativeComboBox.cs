namespace ModernUI.WinForms;

/// <summary>Preserves native ComboBox viewing and selection while optionally blocking edit messages.</summary>
internal sealed class ModernNativeComboBox : ComboBox
{
    public bool ReadOnly { get; set; }
    public Func<Keys, bool>? ManagedKeyHandler { get; set; }

    protected override void WndProc(ref Message message)
    {
        const int windowChar = 0x0102;
        const int windowClear = 0x0303;
        const int windowCut = 0x0300;
        const int windowPaste = 0x0302;
        if (ReadOnly && message.Msg is windowChar or windowClear or windowCut or windowPaste)
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (ManagedKeyHandler?.Invoke(e.KeyData) == true)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }
        if (ReadOnly && e.KeyCode is Keys.Back or Keys.Delete)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
