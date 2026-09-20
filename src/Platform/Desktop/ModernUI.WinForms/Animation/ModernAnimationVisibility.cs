namespace ModernUI.WinForms;

/// <summary>避免不可见的连续动画触发被裁剪祖先的合成与重绘。</summary>
internal static class ModernAnimationVisibility
{
    public const int HiddenPollInterval = 250;

    public static bool IsInVisibleViewport(Control control)
    {
        if (!control.Visible || !control.IsHandleCreated || control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0)
            return false;
        if (control.FindForm() is { WindowState: FormWindowState.Minimized }) return false;

        var visibleBounds = control.RectangleToScreen(control.ClientRectangle);
        for (Control? ancestor = control.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!ancestor.Visible || !ancestor.IsHandleCreated) return false;
            visibleBounds.Intersect(ancestor.RectangleToScreen(ancestor.ClientRectangle));
            if (visibleBounds.IsEmpty) return false;
        }
        return true;
    }

    public static void UseInterval(System.Windows.Forms.Timer timer, int interval)
    {
        if (timer.Interval != interval) timer.Interval = interval;
    }
}
