namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 让工具条不画自己的边框（ToolStrip 默认在底边画一条线、右下角画圆角）：放在卡片里的工具条和右边的按钮组并排时，
/// 那条线只画到工具条为止，看起来像一个被截断的框。其余照原来的渲染器（主题）画。
/// </summary>
internal static class WorkflowBorderlessToolStrip
{
    /// <summary>接到工具条上：以后主题换渲染器时也自动包一层。</summary>
    public static void Attach(ToolStrip strip)
    {
        Wrap(strip);
        strip.RendererChanged += (_, _) => Wrap(strip);
    }

    private static void Wrap(ToolStrip strip)
    {
        if (strip.Renderer is not Renderer) strip.Renderer = new Renderer(strip.Renderer);
    }

    // 只能通过公开的 Draw* 转给原渲染器。
    private sealed class Renderer(ToolStripRenderer inner) : ToolStripRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => inner.DrawToolStripBackground(e);
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => inner.DrawButtonBackground(e);
        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => inner.DrawDropDownButtonBackground(e);
        protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e) => inner.DrawSplitButton(e);
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => inner.DrawMenuItemBackground(e);
        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e) => inner.DrawItemBackground(e);
        protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e) => inner.DrawLabelBackground(e);
        protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e) => inner.DrawOverflowButtonBackground(e);
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) => inner.DrawSeparator(e);
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) => inner.DrawItemText(e);
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) => inner.DrawItemImage(e);
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) => inner.DrawItemCheck(e);
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) => inner.DrawArrow(e);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) => inner.DrawImageMargin(e);
        protected override void OnRenderGrip(ToolStripGripRenderEventArgs e) => inner.DrawGrip(e);
    }
}
