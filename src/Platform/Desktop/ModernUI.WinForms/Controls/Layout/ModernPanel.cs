using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>具有主题背景、圆角和边框的容器。</summary>
[Description("ModernPanel 现代圆角容器")]
[DisplayName("现代面板")]
[ToolboxBitmap(typeof(ModernPanel), "Toolbox.Icons.Layout.bmp")]
[ToolboxItem(true)]
public sealed class ModernPanel : ModernControl
{
    private bool _showBorder = true;
    private int _radius = 8;

    public ModernPanel() { TabStop = false; Padding = new Padding(12); }

    [Category("Appearance"), DefaultValue(true)]
    [Description("是否绘制面板边框。")]
    public bool ShowBorder
    {
        get => _showBorder;
        set { if (_showBorder == value) return; _showBorder = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(8)]
    [Description("面板圆角半径，单位为逻辑像素；设置为 0 时使用直角。")]
    public int Radius
    {
        get => _radius;
        set { var next = Math.Max(0, value); if (_radius == next) return; _radius = next; Invalidate(); }
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var inset = ScaleLogical(1f);
        var rect = RectangleF.Inflate(bounds, -inset, -inset);
        canvas.Fill(Theme.Container, rect, ScaleLogical(Radius));
        if (ShowBorder) canvas.Draw(Theme.BorderSecondary, ScaleLogical(1f), rect, ScaleLogical(Radius));
    }
}
