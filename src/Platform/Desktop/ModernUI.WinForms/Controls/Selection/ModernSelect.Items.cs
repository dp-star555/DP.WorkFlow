using System.Collections;
using System.ComponentModel;

namespace ModernUI.WinForms;

public partial class ModernSelect
{
    private void BoundListChanged(object? sender, ListChangedEventArgs e)
    {
        _surface.ItemCount = _model.Items.Count;
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        _surface.Invalidate();
        Invalidate();
    }

    public string GetItemText(object? item)
    {
        if (item is null) return string.Empty;
        if (string.IsNullOrWhiteSpace(DisplayMember)) return _model.GetItemText(item) ?? string.Empty;
        return Convert.ToString(DataMemberResolver.Resolve(item, DisplayMember)) ?? string.Empty;
    }
}
