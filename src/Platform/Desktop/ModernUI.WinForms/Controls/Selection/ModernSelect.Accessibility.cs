using System.Collections;
using System.ComponentModel;

namespace ModernUI.WinForms;

public partial class ModernSelect
{
    private sealed class SelectAccessibleObject(ModernSelect owner) : ControlAccessibleObject(owner)
    {
        public override string? Value => owner.GetItemText(owner.SelectedItem);
        public override string? DefaultAction => owner.FrameworkText(owner.DroppedDown ? ModernUiTextKeys.Collapse : ModernUiTextKeys.Expand);
        public override AccessibleStates State => base.State |
            (owner.DroppedDown ? AccessibleStates.Expanded : AccessibleStates.Collapsed);

        public override void DoDefaultAction()
        {
            if (owner.Enabled) owner.DroppedDown = !owner.DroppedDown;
        }
    }

}
