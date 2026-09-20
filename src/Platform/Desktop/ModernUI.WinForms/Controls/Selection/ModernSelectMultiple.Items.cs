using System.Collections;
using System.ComponentModel;

namespace ModernUI.WinForms;

public sealed partial class ModernSelectMultiple
{
    private HashSet<object> MatchItems(IEnumerable<object?> requestedValues, Func<object, object?> valueSelector)
    {
        var desired = new HashSet<object>(ModernReferenceEqualityComparer.Instance);
        var items = _list.Items.Cast<object>().ToArray();
        foreach (var requested in requestedValues)
        {
            var match = items.FirstOrDefault(item => !desired.Contains(item) &&
                ReferenceEquals(valueSelector(item), requested));
            match ??= items.FirstOrDefault(item => !desired.Contains(item) &&
                Equals(valueSelector(item), requested));
            if (match is not null) desired.Add(match);
        }
        return desired;
    }

    private void ApplyCheckedItems(HashSet<object> desired)
    {
        // PropertyGrid 提交后可能同步回刷同一个值。相同集合不重复清空和重绘，避免打断正在播放的勾选动画。
        if (_checkedItems.SetEquals(desired)) return;
        CancelCheckAnimations();
        _checkedItems.Clear();
        _checkedItems.UnionWith(desired);
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        _surface.Invalidate();
        Invalidate();
    }

    private void BoundListChanged(object? sender, ListChangedEventArgs e)
    {
        CancelCheckAnimations();
        var currentItems = new HashSet<object>(_list.Items.Cast<object>(), ModernReferenceEqualityComparer.Instance);
        var desired = new HashSet<object>(_checkedItems.Where(currentItems.Contains), ModernReferenceEqualityComparer.Instance);
        var selectionChanged = !_checkedItems.SetEquals(desired);
        if (selectionChanged)
        {
            _checkedItems.Clear();
            _checkedItems.UnionWith(desired);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        _surface.ItemCount = _list.Items.Count;
        _surface.Invalidate();
        Invalidate();
    }

    private void CancelCheckAnimations()
    {
        foreach (var animation in _checkAnimations.Values) animation.Dispose();
        _checkAnimations.Clear();
        _checkProgress.Clear();
    }

    private string GetItemText(object item)
    {
        if (string.IsNullOrWhiteSpace(DisplayMember)) return _list.GetItemText(item) ?? string.Empty;
        return Convert.ToString(DataMemberResolver.Resolve(item, DisplayMember)) ?? string.Empty;
    }

    private object? GetItemValue(object item) => DataMemberResolver.Resolve(item, ValueMember);
}
