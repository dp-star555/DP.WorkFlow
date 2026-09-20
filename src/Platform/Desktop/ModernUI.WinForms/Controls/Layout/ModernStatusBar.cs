using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>保留原生 StatusStrip 项模型和 UIA 语义的现代状态栏。</summary>
[Description("ModernStatusBar 现代状态栏")]
[DisplayName("现代状态栏")]
[ToolboxBitmap(typeof(ModernStatusBar), "Toolbox.Icons.Layout.bmp")]
[ToolboxItem(true)]
public sealed class ModernStatusBar : StatusStrip
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;

    public ModernStatusBar()
    {
        SizingGrip = false;
        Padding = new Padding(8, 2, 8, 2);
        ApplyTheme();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); }
    }

    /// <summary>添加占用剩余空间的弹性状态项。</summary>
    public ToolStripStatusLabel AddSpring()
    {
        var item = new ToolStripStatusLabel { Spring = true };
        Items.Add(item);
        return item;
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Container;
        ForeColor = Theme.TextSecondary;
        Renderer = new ModernStatusRenderer(Theme);
        foreach (ToolStripItem item in Items) { item.BackColor = Theme.Container; item.ForeColor = Theme.TextSecondary; }
        Invalidate();
    }

    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item is null) return;
        e.Item.BackColor = Theme.Container;
        e.Item.ForeColor = Theme.TextSecondary;
    }

    private sealed class ModernStatusRenderer(ModernTheme theme)
        : ToolStripProfessionalRenderer(new ModernStatusColorTable(theme))
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(theme.BorderSecondary);
            e.Graphics.DrawLine(pen, e.AffectedBounds.Left, e.AffectedBounds.Top,
                e.AffectedBounds.Right, e.AffectedBounds.Top);
        }
    }

    private sealed class ModernStatusColorTable(ModernTheme theme) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => theme.Container;
        public override Color ToolStripGradientMiddle => theme.Container;
        public override Color ToolStripGradientEnd => theme.Container;
        public override Color ToolStripBorder => theme.BorderSecondary;
        public override Color ButtonSelectedHighlight => theme.ControlHover;
        public override Color ButtonSelectedBorder => theme.Border;
    }
}
