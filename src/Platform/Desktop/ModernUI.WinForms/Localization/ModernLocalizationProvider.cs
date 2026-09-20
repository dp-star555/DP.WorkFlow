using System.ComponentModel;
using ModernUI.Localization;

namespace ModernUI.WinForms;

/// <summary>把稳定文本 Key 绑定到普通 WinForms 控件，并响应运行时语言切换。</summary>
[ProvideProperty("TextKey", typeof(Control))]
[ProvideProperty("PlaceholderKey", typeof(Control))]
[ProvideProperty("AccessibleNameKey", typeof(Control))]
[ProvideProperty("AccessibleDescriptionKey", typeof(Control))]
[Description("ModernLocalizationProvider WinForms 多语言绑定组件")]
[DisplayName("现代本地化提供器")]
[ToolboxBitmap(typeof(ModernLocalizationProvider), "Toolbox.Icons.Feedback.bmp")]
[ToolboxItem(true)]
public sealed class ModernLocalizationProvider : Component, IExtenderProvider
{
    private readonly Dictionary<Control, Binding> _bindings = [];
    private ILocalizationContext? _localizationContext;

    public ModernLocalizationProvider() { }
    public ModernLocalizationProvider(IContainer container) { ModernCompatibility.ThrowIfNull(container, nameof(container)); container.Add(this); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ILocalizationContext? LocalizationContext
    {
        get => _localizationContext;
        set
        {
            if (ReferenceEquals(_localizationContext, value)) return;
            if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
            _localizationContext = value;
            if (_localizationContext is not null) _localizationContext.Changed += LocalizationChanged;
            ApplyAll();
        }
    }

    public bool CanExtend(object extendee) => extendee is Control;
    [DefaultValue(null)] public string? GetTextKey(Control control) => Get(control).TextKey;
    public void SetTextKey(Control control, string? value) { Get(control).TextKey = Normalize(value); Track(control); Apply(control); }
    [DefaultValue(null)] public string? GetPlaceholderKey(Control control) => Get(control).PlaceholderKey;
    public void SetPlaceholderKey(Control control, string? value)
    {
        var binding = Get(control);
        var normalized = Normalize(value);
        binding.PlaceholderKey = normalized;
        if (normalized is null)
        {
            switch (control)
            {
                case ModernInput input: input.PlaceholderText = null; break;
                case ModernSelect select: select.PlaceholderText = null; break;
                case ModernSelectMultiple multiple: multiple.PlaceholderText = null; break;
            }
        }
        Track(control);
        Apply(control);
    }
    [DefaultValue(null)] public string? GetAccessibleNameKey(Control control) => Get(control).AccessibleNameKey;
    public void SetAccessibleNameKey(Control control, string? value) { Get(control).AccessibleNameKey = Normalize(value); Track(control); Apply(control); }
    [DefaultValue(null)] public string? GetAccessibleDescriptionKey(Control control) => Get(control).AccessibleDescriptionKey;
    public void SetAccessibleDescriptionKey(Control control, string? value) { Get(control).AccessibleDescriptionKey = Normalize(value); Track(control); Apply(control); }

    /// <summary>绑定不属于 Control 层次的菜单项文本。</summary>
    public IDisposable BindText(ToolStripItem item, TextKey key)
    {
        ModernCompatibility.ThrowIfNull(item, nameof(item));
        var context = LocalizationContext;
        void ApplyItem() { if (!item.IsDisposed && context is not null) item.Text = context.Text(key); }
        EventHandler<LocaleChangedEventArgs> changed = (_, _) => ApplyItem();
        if (context is not null) context.Changed += changed;
        ApplyItem();
        return new DelegateSubscription(() => { if (context is not null) context.Changed -= changed; });
    }

    private void LocalizationChanged(object? sender, LocaleChangedEventArgs e)
    {
        var control = _bindings.Keys.FirstOrDefault(candidate => !candidate.IsDisposed && candidate.IsHandleCreated);
        if (control is not null && control.InvokeRequired) { control.BeginInvoke(ApplyAll); return; }
        ApplyAll();
    }
    private void ApplyAll()
    {
        foreach (var control in _bindings.Keys.ToArray()) Apply(control);
        foreach (var root in _bindings.Keys.Where(control => control.Parent is null || !_bindings.ContainsKey(control.Parent)).ToArray())
        {
            root.PerformLayout();
            root.Invalidate(true);
        }
    }
    private void Apply(Control control)
    {
        if (control.IsDisposed || LocalizationContext is null || !_bindings.TryGetValue(control, out var binding)) return;
        if (TryKey(binding.TextKey, out var textKey)) control.Text = LocalizationContext.Text(textKey);
        if (TryKey(binding.AccessibleNameKey, out var nameKey)) control.AccessibleName = LocalizationContext.Text(nameKey);
        if (TryKey(binding.AccessibleDescriptionKey, out var descriptionKey)) control.AccessibleDescription = LocalizationContext.Text(descriptionKey);
        if (TryKey(binding.PlaceholderKey, out var placeholderKey))
        {
            var text = LocalizationContext.Text(placeholderKey);
            switch (control)
            {
                case ModernInput input: input.PlaceholderText = text; break;
                case ModernSelect select: select.PlaceholderText = text; break;
                case ModernSelectMultiple multiple: multiple.PlaceholderText = text; break;
            }
        }
        if (control is ModernControl modern) modern.LocalizationContext = LocalizationContext;
    }
    private void Track(Control control)
    {
        control.Disposed -= ControlDisposed;
        control.Disposed += ControlDisposed;
        if (Get(control).IsEmpty) { control.Disposed -= ControlDisposed; _bindings.Remove(control); }
    }
    private void ControlDisposed(object? sender, EventArgs e) { if (sender is Control control) _bindings.Remove(control); }
    private Binding Get(Control control)
    {
        ModernCompatibility.ThrowIfNull(control, nameof(control));
        if (_bindings.TryGetValue(control, out var existing)) return existing;
        var binding = new Binding();
        _bindings[control] = binding;
        return binding;
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    private static bool TryKey(string? raw, out TextKey key)
    {
        key = default;
        if (raw is null) return false;
        var separator = raw.IndexOf('.');
        if (separator <= 0 || separator == raw.Length - 1) throw new FormatException($"Localization key '{raw}' must use 'module.name'.");
        key = new TextKey(raw.Substring(0, separator), raw.Substring(separator + 1));
        return true;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
            foreach (var control in _bindings.Keys) control.Disposed -= ControlDisposed;
            _bindings.Clear();
        }
        base.Dispose(disposing);
    }
    private sealed class Binding
    {
        public string? TextKey { get; set; }
        public string? PlaceholderKey { get; set; }
        public string? AccessibleNameKey { get; set; }
        public string? AccessibleDescriptionKey { get; set; }
        public bool IsEmpty => TextKey is null && PlaceholderKey is null && AccessibleNameKey is null && AccessibleDescriptionKey is null;
    }
    private sealed class DelegateSubscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
