using System.ComponentModel;

namespace ModernUI.WinForms;

public enum ModernShortcutConflictBehavior { FirstWins, Throw }

/// <summary>在一个窗体范围内集中路由 ModernCommand 快捷键。</summary>
[Description("ModernCommandManager 窗体命令快捷键管理器")]
[DisplayName("现代命令管理器")]
[ToolboxBitmap(typeof(ModernCommandManager), "Toolbox.Icons.Commands.bmp")]
[ToolboxItem(true)]
public sealed class ModernCommandManager : Component, IMessageFilter
{
    private readonly BindingList<ModernCommand> _commands = [];
    private Form? _owner;
    private bool _filterInstalled;
    private ModernShortcutConflictBehavior _conflictBehavior = ModernShortcutConflictBehavior.Throw;

    public ModernCommandManager() { }
    public ModernCommandManager(IContainer container) { ModernCompatibility.ThrowIfNull(container, nameof(container)); container.Add(this); }

    [Category("Behavior"), DefaultValue(null)]
    [Description("接收快捷键的宿主窗体。")]
    public Form? Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value)) return;
            DetachOwner();
            _owner = value;
            if (_owner is null) return;
            _owner.HandleCreated += OwnerHandleCreated;
            _owner.HandleDestroyed += OwnerHandleDestroyed;
            _owner.Disposed += OwnerDisposed;
            if (_owner.IsHandleCreated) InstallFilter();
        }
    }

    [Browsable(false)]
    public bool IsActive => _filterInstalled;

    [Category("Behavior"), DefaultValue(ModernShortcutConflictBehavior.Throw)]
    [Description("多个可执行命令使用同一快捷键时的处理策略。")]
    public ModernShortcutConflictBehavior ConflictBehavior { get => _conflictBehavior; set => _conflictBehavior = value; }

    [Category("Data")]
    [Description("由管理器路由快捷键的命令集合。")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public BindingList<ModernCommand> Commands => _commands;

    /// <summary>执行与指定组合键匹配的第一个可见命令。</summary>
    /// <returns>找到并执行命令时返回 true。</returns>
    public bool ProcessShortcut(Keys keyData)
    {
        var normalized = keyData & (Keys.KeyCode | Keys.Modifiers);
        var matches = Commands.Where(item => item.Visible && item.ShortcutKeys != Keys.None &&
            (item.ShortcutKeys & (Keys.KeyCode | Keys.Modifiers)) == normalized).ToArray();
        if (matches.Length > 1 && ConflictBehavior == ModernShortcutConflictBehavior.Throw)
            throw new InvalidOperationException($"Shortcut '{normalized}' is assigned to multiple visible commands.");
        var textInputFocused = IsTextInputFocused();
        return matches.FirstOrDefault(item => !textInputFocused || item.AllowInTextInput)?.TryExecute() == true;
    }

    public bool PreFilterMessage(ref Message m)
    {
        const int wmKeyDown = 0x0100;
        const int wmSysKeyDown = 0x0104;
        if (m.Msg is not (wmKeyDown or wmSysKeyDown) || Owner is null || !Owner.ContainsFocus) return false;
        return ProcessShortcut((Keys)(int)m.WParam | Control.ModifierKeys);
    }

    private bool IsTextInputFocused()
    {
        Control? current = Owner?.ActiveControl;
        while (current is ContainerControl container && container.ActiveControl is not null) current = container.ActiveControl;
        return current is TextBoxBase or MaskedTextBox || current is ComboBox ||
               current?.Parent is ModernInput or ModernComboBox;
    }

    private void OwnerHandleCreated(object? sender, EventArgs e) => InstallFilter();
    private void OwnerHandleDestroyed(object? sender, EventArgs e) => RemoveFilter();
    private void OwnerDisposed(object? sender, EventArgs e) => Owner = null;
    private void InstallFilter()
    {
        if (_filterInstalled || LicenseManager.UsageMode == LicenseUsageMode.Designtime || Owner?.Site?.DesignMode == true) return;
        Application.AddMessageFilter(this);
        _filterInstalled = true;
    }
    private void RemoveFilter() { if (!_filterInstalled) return; Application.RemoveMessageFilter(this); _filterInstalled = false; }
    private void DetachOwner()
    {
        RemoveFilter();
        if (_owner is null) return;
        _owner.HandleCreated -= OwnerHandleCreated;
        _owner.HandleDestroyed -= OwnerHandleDestroyed;
        _owner.Disposed -= OwnerDisposed;
        _owner = null;
    }
    protected override void Dispose(bool disposing) { if (disposing) DetachOwner(); base.Dispose(disposing); }
}
