using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>Preserves native GroupBox grouping, mnemonic, container and accessibility semantics with modern chrome.</summary>
[DefaultProperty(nameof(Text))]
[Description("ModernGroupBox 现代分组框")]
[DisplayName("现代分组框")]
[ToolboxBitmap(typeof(ModernGroupBox), "Toolbox.Icons.Layout.bmp")]
[ToolboxItem(true)]
public sealed class ModernGroupBox : GroupBox
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private int _radius = 8;

    public ModernGroupBox()
    {
        AccessibleRole = AccessibleRole.Grouping;
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = "Group";
        Size = new Size(240, 120);
        Padding = new Padding(12, 24, 12, 12);
        DoubleBuffered = true;
        ApplyTheme();
    }

    [Category("Appearance"), DefaultValue("Group"), AllowNull]
    public override string Text { get => base.Text; set { base.Text = value ?? string.Empty; Invalidate(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); }
    }

    [Category("Appearance"), DefaultValue(8)]
    public int Radius
    {
        get => _radius;
        set { var next = Math.Max(0, value); if (_radius == next) return; _radius = next; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ResolveOpaqueParentBackground());
        var titleSize = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var titleLeft = RightToLeft == RightToLeft.Yes
            ? Math.Max(ScaleLogical(12), Width - ScaleLogical(12) - titleSize.Width)
            : ScaleLogical(12);
        var lineTop = Math.Max(Font.Height / 2, ScaleLogical(8));
        var bounds = new RectangleF(ScaleLogical(1), lineTop,
            Math.Max(0, Width - ScaleLogical(2)), Math.Max(0, Height - lineTop - ScaleLogical(1)));
        using (var canvas = new GdiCanvas(e.Graphics))
        {
            canvas.Fill(Theme.Container, bounds, ScaleLogical(Radius));
            canvas.Draw(Theme.BorderSecondary, ScaleLogical(1), bounds, ScaleLogical(Radius));
        }
        var titleBackground = new Rectangle(titleLeft - ScaleLogical(4), 0,
            titleSize.Width + ScaleLogical(8), Math.Max(titleSize.Height, lineTop * 2));
        using (var brush = new SolidBrush(Theme.Container)) e.Graphics.FillRectangle(brush, titleBackground);
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(titleLeft, 0, titleSize.Width, titleBackground.Height), Theme.Text,
            (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private Color ResolveOpaqueParentBackground()
    {
        for (Control? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.BackColor.A == byte.MaxValue) return ancestor.BackColor;
        return Theme.Background;
    }

    private void ApplyTheme()
    {
        ForeColor = Theme.Text;
        BackColor = Theme.Container;
        Invalidate(true);
    }

    private int ScaleLogical(int logicalPixels) => ModernDpi.ScaleToInt(logicalPixels, DeviceDpi > 0 ? DeviceDpi : 96);
}
