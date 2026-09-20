using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>命令栏展示文字和图标的策略。</summary>
public enum ModernCommandBarDisplayMode
{
    IconAndText,
    IconOnly,
    Adaptive
}

/// <summary>将共享命令呈现为自适应工具栏，并把放不下的操作收进溢出菜单。</summary>
[DefaultProperty(nameof(Commands))]
[Description("ModernCommandBar 现代命令栏")]
[DisplayName("现代命令栏")]
[ToolboxBitmap(typeof(ModernCommandBar), "Toolbox.Icons.Commands.bmp")]
[ToolboxItem(true)]
public sealed class ModernCommandBar : ModernControl
{
    private readonly BindingList<ModernCommand> _commands = [];
    private readonly ModernButton _overflow = new() { Text = "···", Width = 38, ButtonType = ModernButtonType.Text };
    private readonly ModernContextMenu _overflowMenu = new();
    private readonly ToolTip _toolTip = new();
    private readonly List<Control> _presenters = [];
    private ModernCommandBarDisplayMode _displayMode;
    private IReadOnlyList<ModernCommand> _overflowCommands = Array.Empty<ModernCommand>();
    private float _layoutScale = 1f;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, LayoutCommands);

    public ModernCommandBar()
    {
        AccessibleRole = AccessibleRole.ToolBar;
        Height = 42;
        MinimumSize = new Size(80, 38);
        Padding = new Padding(4);
        _commands.ListChanged += (_, _) => Rebuild();
        _overflow.AccessibleName = FrameworkText(ModernUiTextKeys.MoreActions);
        _overflow.Click += (_, _) => _overflowMenu.Show(_overflow, new Point(0, _overflow.Height));
        Controls.Add(_overflow);
    }

    [Category("Data")]
    [Description("命令栏按顺序展示的共享命令集合。")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public BindingList<ModernCommand> Commands => _commands;

    [Category("Appearance")]
    [DefaultValue(ModernCommandBarDisplayMode.IconAndText)]
    [Description("控制命令栏使用图标加文字、仅图标或按可用宽度自适应显示。")]
    public ModernCommandBarDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            LayoutTransaction.Request();
        }
    }

    [Browsable(false)]
    public bool OverflowVisible => _overflow.Visible;

    [Browsable(false)]
    public IReadOnlyList<ModernCommand> OverflowCommands => _overflowCommands;

    /// <summary>在存在溢出命令时展开更多操作菜单。</summary>
    public void OpenOverflow() { if (_overflow.Visible) _overflow.PerformClick(); }

    protected override bool ScaleChildren => false;

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        _layoutScale *= factor.Height;
        base.ScaleControl(factor, specified);
        LayoutTransaction.Request();
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds) => canvas.Fill(Theme.Container, bounds, 0);

    protected override AccessibleObject CreateAccessibilityInstance() => new CommandBarAccessibleObject(this);

    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        _overflow.AccessibleName = FrameworkText(ModernUiTextKeys.MoreActions);
    }

    protected override void OnThemeChanged()
    {
        if (_overflowMenu is null) return;
        _overflowMenu.Theme = Theme;
        foreach (var presenter in _presenters.OfType<ModernControl>()) presenter.Theme = Theme;
        _overflow.Theme = Theme;
        Invalidate();
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        LayoutTransaction.Request();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        LayoutTransaction.Request();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutTransaction.Request(); }
    protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); LayoutTransaction.Request(); }

    private void Rebuild()
    {
        SuspendLayout();
        foreach (var presenter in _presenters) { Controls.Remove(presenter); presenter.Dispose(); }
        _presenters.Clear();
        foreach (var command in Commands)
        {
            Control presenter = command.Kind == ModernCommandKind.Separator
                ? new Label { Width = Metric(1), Height = Metric(24), BackColor = Theme.BorderSecondary, Margin = new Padding(Metric(5)) }
                : new ModernButton
                {
                    Command = command,
                    AutoSize = false,
                    Height = Metric(32),
                    Width = MeasureCommandWidth(command, iconOnly: false),
                    ButtonType = ModernButtonType.Text,
                    Theme = Theme,
                    AccessibleName = command.Text
                };
            _toolTip.SetToolTip(presenter, CommandToolTip(command));
            _presenters.Add(presenter);
            Controls.Add(presenter);
        }
        Controls.SetChildIndex(_overflow, 0);
        ResumeLayout();
        LayoutTransaction.Request();
    }

    private int MeasureCommandWidth(ModernCommand command, bool iconOnly)
    {
        if (iconOnly) return Metric(38);
        var textWidth = TextRenderer.MeasureText(command.Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        return Math.Max(Metric(42), textWidth + Metric(command.Icon == ModernIconKind.None && command.Image is null ? 34 : 66));
    }

    private void LayoutCommands()
    {
        if (_overflow is null) return;
        var buttonHeight = Metric(32);
        var separatorHeight = Metric(24);
        var gap = Metric(4);
        var separatorSlotWidth = Metric(11);
        var overflowWidth = Metric(38);
        var left = Padding.Left;
        var top = Math.Max(Padding.Top, (Height - buttonHeight) / 2);
        var availableRight = Math.Max(left, Width - Padding.Right);
        var hidden = new List<ModernCommand>();
        var visibleCommands = Commands.Where(command => command.Visible).ToArray();
        var canUseIconsOnly = visibleCommands
            .Where(command => command.Kind == ModernCommandKind.Action)
            .All(command => command.Icon != ModernIconKind.None || command.Image is not null);
        var fullWidth = visibleCommands.Sum(command => command.Kind == ModernCommandKind.Separator
            ? separatorSlotWidth
            : MeasureCommandWidth(command, iconOnly: false) + gap);
        var compactWidth = visibleCommands.Sum(command => command.Kind == ModernCommandKind.Separator
            ? separatorSlotWidth
            : MeasureCommandWidth(command, iconOnly: true) + gap);
        var iconOnly = DisplayMode == ModernCommandBarDisplayMode.IconOnly
                       || (DisplayMode == ModernCommandBarDisplayMode.Adaptive
                           && canUseIconsOnly
                           && fullWidth > availableRight - left
                           && compactWidth <= availableRight - left);
        var requiredWidth = iconOnly ? compactWidth : fullWidth;
        var requiresOverflow = requiredWidth > availableRight - left;
        var commandIndex = 0;
        foreach (var presenter in _presenters)
        {
            var command = Commands[commandIndex++];
            if (!command.Visible) { presenter.Visible = false; continue; }
            var width = command.Kind == ModernCommandKind.Separator
                ? separatorSlotWidth
                : MeasureCommandWidth(command, iconOnly);
            if (presenter is ModernButton button)
            {
                button.Text = iconOnly ? string.Empty : command.Text;
                button.AccessibleName = command.Text;
            }
            var limit = availableRight - (requiresOverflow ? overflowWidth + gap : 0);
            if (left + width > limit)
            {
                presenter.Visible = false;
                if (command.Kind == ModernCommandKind.Action) hidden.Add(command);
                continue;
            }
            presenter.Bounds = command.Kind == ModernCommandKind.Separator
                ? new Rectangle(left + Metric(5), top + Math.Max(0, (buttonHeight - separatorHeight) / 2), Metric(1), separatorHeight)
                : new Rectangle(left, top, width, buttonHeight);
            presenter.Visible = true;
            left += width + gap;
        }
        _overflowCommands = hidden.ToArray();
        _overflow.Visible = hidden.Count > 0;
        if (_overflow.Visible) _overflow.Bounds = new Rectangle(availableRight - overflowWidth, top, overflowWidth, buttonHeight);
        _overflowMenu.SetCommands(hidden);
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
    }

    private static string CommandToolTip(ModernCommand command)
    {
        var shortcut = command.ShortcutKeys == Keys.None
            ? string.Empty
            : $" ({new KeysConverter().ConvertToInvariantString(command.ShortcutKeys)})";
        return command.Text + shortcut;
    }

    private int Metric(int logicalValue) =>
        Math.Max(1, (int)Math.Round(logicalValue * Math.Max(_layoutScale, DeviceDpi / 96f)));

    private IReadOnlyList<AccessibleObject> GetAccessibleChildren()
    {
        var children = new List<AccessibleObject>();
        for (var index = 0; index < Math.Min(_presenters.Count, Commands.Count); index++)
            if (Commands[index].Kind == ModernCommandKind.Action && _presenters[index].Visible)
                children.Add(_presenters[index].AccessibilityObject);
        if (_overflow.Visible) children.Add(_overflow.AccessibilityObject);
        return children;
    }

    private sealed class CommandBarAccessibleObject(ModernCommandBar owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.ToolBar;
        public override int GetChildCount() => owner.GetAccessibleChildren().Count;
        public override AccessibleObject? GetChild(int index)
        {
            var children = owner.GetAccessibleChildren();
            return index >= 0 && index < children.Count ? children[index] : null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _overflowMenu.Dispose();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }
}
