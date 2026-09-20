using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>具有键盘、UIA 和内容生命周期语义的现代折叠面板。</summary>
[Description("ModernCollapsiblePanel 现代折叠面板")]
[DefaultEvent(nameof(ExpandedChanged))]
[DisplayName("现代折叠面板")]
[ToolboxBitmap(typeof(ModernCollapsiblePanel), "Toolbox.Icons.Layout.bmp")]
[ToolboxItem(true)]
public sealed class ModernCollapsiblePanel : ModernControl
{
    private Control? _content;
    private bool _expanded = true;
    private int _expandedHeight = 180;
    private ModernIconKind _headerIcon;

    public ModernCollapsiblePanel()
    {
        AccessibleRole = AccessibleRole.Grouping;
        Size = new Size(320, 180);
        Padding = new Padding(12, 42, 12, 12);
        Text = "Section";
        Cursor = Cursors.Hand;
    }

    [Category("Appearance"), DefaultValue("Section")]
    [AllowNull]
    public override string Text { get => base.Text; set { base.Text = value; AccessibleName = value; Invalidate(); } }

    [Category("Layout"), DefaultValue(38)]
    public int HeaderHeight { get; set; } = 38;

    /// <summary>获取或设置标题中显示在折叠箭头与文字之间的矢量业务图标。</summary>
    [Category("Appearance"), DefaultValue(ModernIconKind.None)]
    public ModernIconKind HeaderIcon
    {
        get => _headerIcon;
        set { if (_headerIcon == value) return; _headerIcon = value; Invalidate(); }
    }

    [Category("Behavior"), DefaultValue(true)]
    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            if (_expanded) _expandedHeight = Math.Max(_expandedHeight, Height);
            _expanded = value;
            if (_content is not null) { _content.Visible = value; _content.TabStop = value; }
            Height = value ? Math.Max(HeaderHeight + Padding.Bottom, _expandedHeight) : ScaleLogical(HeaderHeight);
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            PerformLayout();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;
            if (_content is not null) Controls.Remove(_content);
            _content = value;
            if (value is not null)
            {
                value.Visible = Expanded;
                Controls.Add(value);
                value.BringToFront();
            }
            PerformLayout();
        }
    }

    public event EventHandler? ExpandedChanged;

    protected override AccessibleObject CreateAccessibilityInstance() => new CollapsiblePanelAccessibleObject(this);

    private sealed class CollapsiblePanelAccessibleObject(ModernCollapsiblePanel owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Grouping;
        public override string? DefaultAction => owner.FrameworkText(owner.Expanded
            ? ModernUiTextKeys.Collapse : ModernUiTextKeys.Expand);
        public override AccessibleStates State => base.State |
            (owner.Expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
        public override void DoDefaultAction()
        {
            if (owner.Enabled) owner.Expanded = !owner.Expanded;
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_content is null) return;
        var header = ScaleLogical(HeaderHeight);
        _content.Bounds = new Rectangle(Padding.Left, header,
            Math.Max(0, ClientSize.Width - Padding.Horizontal),
            Math.Max(0, ClientSize.Height - header - Padding.Bottom));
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var rect = RectangleF.Inflate(bounds, -ScaleLogical(.5f), -ScaleLogical(.5f));
        canvas.Fill(Theme.Container, rect, ScaleLogical(Theme.Radius));
        canvas.Draw(Theme.BorderSecondary, ScaleLogical(1), rect, ScaleLogical(Theme.Radius));
        var header = new Rectangle(ScaleLogical(12), 0, Math.Max(0, bounds.Width - ScaleLogical(24)), ScaleLogical(HeaderHeight));
        var chevronSize = ScaleLogical(14);
        canvas.DrawIcon(Expanded ? ModernIconKind.ChevronDown : ModernIconKind.ChevronRight,
            Theme.TextSecondary, new RectangleF(header.Left, (header.Height - chevronSize) / 2f, chevronSize, chevronSize), ScaleLogical(1.5f));

        var textLeft = header.Left + ScaleLogical(22);
        if (HeaderIcon != ModernIconKind.None)
        {
            var iconSize = ScaleLogical(16);
            canvas.DrawIcon(HeaderIcon, Theme.Primary,
                new RectangleF(textLeft, (header.Height - iconSize) / 2f, iconSize, iconSize), ScaleLogical(1.5f));
            textLeft += iconSize + ScaleLogical(8);
        }
        canvas.DrawText(Text, Font, Theme.Text,
            new Rectangle(textLeft, 0, Math.Max(0, header.Right - textLeft), header.Height), ContentAlignment.MiddleLeft);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && e.Y <= ScaleLogical(HeaderHeight)) Expanded = !Expanded;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter or Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) Expanded = !Expanded;
        else if (e.KeyCode == Keys.Left) Expanded = false;
        else if (e.KeyCode == Keys.Right) Expanded = true;
        else return;
        e.Handled = true;
    }
}
