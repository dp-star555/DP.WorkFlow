namespace ModernUI.WinForms;

/// <summary>Centralizes list movement rules shared by single- and multi-select drop-down surfaces.</summary>
internal static class DropDownKeyboardNavigation
{
    public static bool TryMove(Keys keyData, int currentIndex, int initialIndex,
        int itemCount, int visibleRows, out int targetIndex)
    {
        targetIndex = currentIndex;
        if (itemCount <= 0 || keyData is not (Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown))
            return false;

        var start = currentIndex >= 0 ? currentIndex : initialIndex;
        if (start < 0) start = keyData == Keys.Up ? itemCount : -1;
        targetIndex = keyData switch
        {
            Keys.Home => 0,
            Keys.End => itemCount - 1,
            Keys.PageUp => start - Math.Max(1, visibleRows),
            Keys.PageDown => start + Math.Max(1, visibleRows),
            Keys.Up => start - 1,
            _ => start + 1
        };
        targetIndex = ModernCompatibility.Clamp(targetIndex, 0, itemCount - 1);
        return true;
    }
}
