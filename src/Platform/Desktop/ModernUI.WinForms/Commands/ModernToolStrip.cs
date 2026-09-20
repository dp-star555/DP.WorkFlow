using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>Preserves native ToolStrip item, overflow, keyboard, Designer and accessibility semantics with modern theme rendering.</summary>
[DefaultProperty(nameof(Items))]
[Description("ModernToolStrip 现代工具栏")]
[DisplayName("现代工具栏")]
[ToolboxBitmap(typeof(ModernToolStrip), "Toolbox.Icons.Commands.bmp")]
[ToolboxItem(true)]
public sealed class ModernToolStrip : ToolStrip
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;

    public ModernToolStrip()
    {
        GripStyle = ToolStripGripStyle.Hidden;
        Padding = new Padding(4, 2, 4, 2);
        ImageScalingSize = new Size(16, 16);
        ApplyTheme();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); }
    }

    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item is not null) ModernToolStripTheme.ApplyItem(e.Item, Theme);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ModernToolStripTheme.Apply(this, Theme);
    }

    private void ApplyTheme() => ModernToolStripTheme.Apply(this, Theme);
}
