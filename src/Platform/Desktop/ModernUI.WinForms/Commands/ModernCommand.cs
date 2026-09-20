using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModernUI.WinForms;

public enum ModernCommandKind { Action, Separator }

/// <summary>表示可由按钮、菜单和命令栏共享的用户操作。</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class ModernCommand : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _description = string.Empty;
    private ModernIconKind _icon;
    private Image? _image;
    private Keys _shortcutKeys;
    private bool _enabled = true;
    private bool _visible = true;
    private bool _isChecked;
    private bool _checkOnExecute;
    private bool _allowInTextInput;
    private ModernCommandKind _kind;
    private Action? _execute;
    private Func<bool>? _canExecute;

    /// <summary>初始化空命令。</summary>
    public ModernCommand() { }

    /// <summary>使用执行委托初始化命令。</summary>
    /// <param name="execute">命令执行时调用的委托。</param>
    public ModernCommand(Action execute) => ExecuteAction = execute ?? throw new ArgumentNullException(nameof(execute));

    public string Text { get => _text; set => Set(ref _text, value ?? string.Empty); }
    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }
    public ModernIconKind Icon { get => _icon; set => Set(ref _icon, value); }
    /// <summary>获取或设置调用方拥有的自定义命令图像；非空时优先于 Icon，命令不会释放该图像。</summary>
    public Image? Image { get => _image; set => Set(ref _image, value); }
    public Keys ShortcutKeys { get => _shortcutKeys; set => Set(ref _shortcutKeys, value); }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
    public bool Visible { get => _visible; set => Set(ref _visible, value); }
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
    public bool CheckOnExecute { get => _checkOnExecute; set => Set(ref _checkOnExecute, value); }
    public bool AllowInTextInput { get => _allowInTextInput; set => Set(ref _allowInTextInput, value); }
    public ModernCommandKind Kind { get => _kind; set => Set(ref _kind, value); }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? ExecuteAction { get => _execute; set => Set(ref _execute, value); }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<bool>? CanExecutePredicate { get => _canExecute; set { if (Set(ref _canExecute, value)) RaiseCanExecuteChanged(); } }

    /// <summary>获取命令当前是否可执行。</summary>
    [Browsable(false)]
    public bool CanExecute => Enabled && (CanExecutePredicate?.Invoke() ?? true);

    /// <summary>在命令可执行时执行操作。</summary>
    /// <returns>命令实际执行时返回 true。</returns>
    public bool TryExecute()
    {
        if (!CanExecute) return false;
        if (CheckOnExecute) IsChecked = !IsChecked;
        ExecuteAction?.Invoke();
        return true;
    }

    /// <summary>通知所有适配器重新计算 CanExecute。</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => string.IsNullOrWhiteSpace(Text) ? nameof(ModernCommand) : Text;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
