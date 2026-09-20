using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>Preserves native MenuStrip mnemonic, shortcut, MDI, Designer and accessibility semantics with modern theme rendering.</summary>
[DefaultProperty(nameof(Items))]
[Description("ModernMenuStrip 现代菜单栏")]
[DisplayName("现代菜单栏")]
[ToolboxBitmap(typeof(ModernMenuStrip), "Toolbox.Icons.Commands.bmp")]
[ToolboxItem(true)]
public sealed class ModernMenuStrip : MenuStrip
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;

    public ModernMenuStrip()
    {
        Padding = new Padding(6, 2, 0, 2);
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
