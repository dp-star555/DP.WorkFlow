namespace ModernUI.WinForms;

/// <summary>Centralizes the native ToolStrip family renderer so menus, toolbars and status bars stay consistent.</summary>
internal static class ModernToolStripTheme
{
    public static ToolStripRenderer CreateRenderer(ModernTheme theme, bool topBorder = false) =>
        new Renderer(theme, topBorder);

    public static void Apply(ToolStrip strip, ModernTheme theme, bool secondaryText = false)
    {
        var foreground = secondaryText ? theme.TextSecondary : theme.Text;
        strip.BackColor = theme.Container;
        strip.ForeColor = foreground;
        strip.Renderer = CreateRenderer(theme, strip is StatusStrip);
        foreach (ToolStripItem item in strip.Items) ApplyItem(item, theme, foreground);
        strip.Invalidate();
    }

    public static void ApplyItem(ToolStripItem item, ModernTheme theme, Color? foreground = null)
    {
        item.BackColor = theme.Container;
        item.ForeColor = foreground ?? theme.Text;
        if (item is not ToolStripDropDownItem dropDownItem) return;
        dropDownItem.DropDownOpening -= DropDownOpening;
        dropDownItem.DropDownOpening += DropDownOpening;
        ApplyDropDown(dropDownItem, theme);
        foreach (ToolStripItem child in dropDownItem.DropDownItems) ApplyItem(child, theme);
    }

    private static void DropDownOpening(object? sender, EventArgs e)
    {
        if (sender is ToolStripDropDownItem item)
            ApplyDropDown(item, ResolveTheme(item));
    }

    private static ModernTheme ResolveTheme(ToolStripItem item) => item.Owner switch
    {
        ModernMenuStrip menu => menu.Theme,
        ModernToolStrip toolStrip => toolStrip.Theme,
        ModernContextMenu contextMenu => contextMenu.Theme,
        ToolStripDropDown owner when owner.OwnerItem is { } parent => ResolveTheme(parent),
        _ => ModernUiSettings.DefaultTheme
    };

    private static void ApplyDropDown(ToolStripDropDownItem item, ModernTheme theme)
    {
        item.DropDown.BackColor = theme.Control;
        item.DropDown.ForeColor = theme.Text;
        item.DropDown.Renderer = CreateRenderer(theme);
        ModernToolStripDropDownRegion.Attach(item.DropDown, theme.Radius);
    }

    private sealed class Renderer(ModernTheme theme, bool topBorder)
        : ToolStripProfessionalRenderer(new ColorTable(theme))
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (!topBorder)
            {
                base.OnRenderToolStripBorder(e);
                return;
            }
            using var pen = new Pen(theme.BorderSecondary);
            e.Graphics.DrawLine(pen, e.AffectedBounds.Left, e.AffectedBounds.Top,
                e.AffectedBounds.Right, e.AffectedBounds.Top);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled != false ? theme.TextSecondary : theme.TextDisabled;
            base.OnRenderArrow(e);
        }
    }

    private sealed class ColorTable(ModernTheme theme) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => theme.Container;
        public override Color ToolStripGradientMiddle => theme.Container;
        public override Color ToolStripGradientEnd => theme.Container;
        public override Color ToolStripDropDownBackground => theme.Control;
        public override Color ToolStripBorder => theme.BorderSecondary;
        public override Color MenuBorder => theme.Border;
        public override Color MenuItemSelected => theme.PrimaryBackground;
        public override Color MenuItemBorder => theme.Primary;
        public override Color MenuItemPressedGradientBegin => theme.PrimaryBackground;
        public override Color MenuItemPressedGradientMiddle => theme.PrimaryBackground;
        public override Color MenuItemPressedGradientEnd => theme.PrimaryBackground;
        public override Color ImageMarginGradientBegin => theme.Control;
        public override Color ImageMarginGradientMiddle => theme.Control;
        public override Color ImageMarginGradientEnd => theme.Control;
        public override Color ButtonSelectedHighlight => theme.ControlHover;
        public override Color ButtonSelectedBorder => theme.Border;
        public override Color ButtonPressedHighlight => theme.PrimaryBackground;
        public override Color ButtonPressedBorder => theme.Primary;
        public override Color CheckBackground => theme.PrimaryBackground;
        public override Color CheckSelectedBackground => theme.PrimaryBackground;
        public override Color SeparatorDark => theme.BorderSecondary;
        public override Color SeparatorLight => theme.BorderSecondary;
        public override Color OverflowButtonGradientBegin => theme.Container;
        public override Color OverflowButtonGradientMiddle => theme.Container;
        public override Color OverflowButtonGradientEnd => theme.Container;
    }
}
