using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>为空数据、无搜索结果等场景提供统一占位内容和操作命令。</summary>
[Description("ModernEmptyState 空状态占位")]
[DisplayName("现代空状态")]
[ToolboxBitmap(typeof(ModernEmptyState), "Toolbox.Icons.Feedback.bmp")]
[ToolboxItem(true)]
public sealed class ModernEmptyState : ModernControl
{
    private string? _description;
    private ModernIconKind _icon = ModernIconKind.Info;

    public ModernEmptyState() { AccessibleRole = AccessibleRole.StaticText; Size = new Size(280, 140); AccessibleName = EffectiveDescription; }
    [Category("Appearance"), DefaultValue(null), Description("空状态的显式说明文本；null 使用当前语言的框架默认文本。")]
    public string? Description { get => _description; set { if (_description == value) return; _description = value; AccessibleName = EffectiveDescription; Invalidate(); } }
    [Browsable(false)] public string EffectiveDescription => _description ?? FrameworkText(ModernUiTextKeys.NoData);
    [Category("Appearance"), DefaultValue(ModernIconKind.Info), Description("空状态使用的矢量图标。")]
    public ModernIconKind Icon { get => _icon; set { if (_icon == value) return; _icon = value; Invalidate(); } }
    [Category("Behavior"), DefaultValue(null), Description("单击空状态时执行的可选恢复操作。")]
    public ModernCommand? ActionCommand { get; set; }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var iconSize = Math.Min(ScaleLogical(36), Math.Max(0, Height / 3));
        canvas.DrawIcon(Icon, Theme.TextDisabled, new RectangleF((Width - iconSize) / 2f, Math.Max(8, Height / 2f - iconSize), iconSize, iconSize), ScaleLogical(2));
        canvas.DrawText(EffectiveDescription, Font, Theme.TextSecondary, new Rectangle(12, Height / 2 + 8, Width - 24, Math.Max(0, Height / 2 - 16)), ContentAlignment.TopCenter);
    }
    protected override void OnLocalizationChanged() { base.OnLocalizationChanged(); AccessibleName = EffectiveDescription; }
    protected override void OnClick(EventArgs e) { base.OnClick(e); ActionCommand?.TryExecute(); }
}
