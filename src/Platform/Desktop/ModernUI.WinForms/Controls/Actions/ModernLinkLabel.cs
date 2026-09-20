using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>Preserves native LinkLabel links, mnemonic, visited and accessibility semantics with modern theme colors.</summary>
[DefaultProperty(nameof(Text))]
[DefaultEvent(nameof(LinkClicked))]
[Description("ModernLinkLabel 现代链接标签")]
[DisplayName("现代链接标签")]
[ToolboxBitmap(typeof(ModernLinkLabel), "Toolbox.Icons.Actions.bmp")]
[ToolboxItem(true)]
public sealed class ModernLinkLabel : LinkLabel
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;

    public ModernLinkLabel()
    {
        AccessibleRole = AccessibleRole.Link;
        AutoSize = true;
        Text = "Link";
        UseMnemonic = true;
        ApplyTheme();
    }

    [Category("Appearance"), DefaultValue("Link"), AllowNull]
    public override string Text { get => base.Text; set => base.Text = value ?? string.Empty; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); }
    }

    private void ApplyTheme()
    {
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        LinkColor = Theme.Primary;
        ActiveLinkColor = Theme.PrimaryActive;
        VisitedLinkColor = Theme.IsDark
            ? Geometry.Blend(Theme.Primary, Theme.TextSecondary, .35f)
            : Geometry.Blend(Theme.Primary, Theme.Text, .32f);
        DisabledLinkColor = Theme.TextDisabled;
        Invalidate();
    }
}
