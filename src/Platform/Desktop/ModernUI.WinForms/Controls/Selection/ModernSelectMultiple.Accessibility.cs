using System.Collections;
using System.ComponentModel;

namespace ModernUI.WinForms;

public sealed partial class ModernSelectMultiple
{
    private sealed class MultiSelectAccessibleObject(ModernSelectMultiple owner) : ControlAccessibleObject(owner)
    {
        public override string? Value => string.Join(", ", owner.SelectedItems.Select(owner.GetItemText));
        public override string? DefaultAction => owner.FrameworkText(owner.DroppedDown ? ModernUiTextKeys.Collapse : ModernUiTextKeys.Expand);
        public override AccessibleStates State => base.State |
            (owner.DroppedDown ? AccessibleStates.Expanded : AccessibleStates.Collapsed);

        public override void DoDefaultAction()
        {
            if (owner.Enabled) owner.DroppedDown = !owner.DroppedDown;
        }

        public override int GetChildCount() => owner.DisplayMode == MultipleSelectionDisplayMode.Tags
            ? owner.SelectedItems.Count : 0;

        public override AccessibleObject? GetChild(int index)
        {
            var selected = owner.SelectedItems;
            return owner.DisplayMode == MultipleSelectionDisplayMode.Tags && (uint)index < (uint)selected.Count
                ? new TagAccessibleObject(owner, selected[index], this) : null;
        }
    }

    private sealed class TagAccessibleObject(ModernSelectMultiple owner, object item, AccessibleObject parent) : AccessibleObject
    {
        public override string? Name { get => owner.FrameworkText(ModernUiTextKeys.RemoveItem,
            new Dictionary<string, object?> { ["item"] = owner.GetItemText(item) }); set { } }
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override string? DefaultAction => Name;
        public override AccessibleObject? Parent => parent;
        public override Rectangle Bounds
        {
            get
            {
                var layout = owner.GetTagLayouts(owner.SelectedItems, owner.ScaleLogical(10), owner.Width - owner.ScaleLogical(32))
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, item) || Equals(candidate.Item, item));
                return layout.Item is null ? Rectangle.Empty : owner.RectangleToScreen(layout.Bounds);
            }
        }
        public override void DoDefaultAction()
        {
            var index = owner._list.Items.IndexOf(item);
            if (owner.Enabled && index >= 0) owner.SetItemCheckedCore(index, false, true);
        }
    }

}
