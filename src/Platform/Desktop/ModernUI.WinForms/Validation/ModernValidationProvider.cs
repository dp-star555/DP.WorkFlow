using System.ComponentModel;
using ModernUI.Localization;

namespace ModernUI.WinForms;

/// <summary>集中设置、展示和定位表单中的现代验证结果。</summary>
[Description("ModernValidationProvider 表单验证协调器")]
[DisplayName("现代验证提供器")]
[ToolboxBitmap(typeof(ModernValidationProvider), "Toolbox.Icons.Feedback.bmp")]
[ToolboxItem(true)]
public sealed class ModernValidationProvider : Component
{
    private readonly Dictionary<Control, Entry> _entries = [];
    private readonly Dictionary<Control, Func<CancellationToken, ValueTask<ModernValidationOutcome>>> _validators = [];
    private readonly Dictionary<Control, CancellationTokenSource> _validationRuns = [];
    private readonly ModernToolTip _toolTip = new() { Placement = ModernToolTipPlacement.Top };

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ILocalizationContext LocalizationContext { get; set; } = ModernUiLocalization.DefaultContext;

    public ModernValidationProvider() { }
    public ModernValidationProvider(IContainer container) { ModernCompatibility.ThrowIfNull(container, nameof(container)); container.Add(this); }

    /// <summary>获取当前非空验证结果。</summary>
    [Browsable(false)]
    public IReadOnlyList<ModernValidationResult> Results => _entries
        .Where(pair => pair.Value.State != ModernValidationState.None)
        .Select(pair => new ModernValidationResult(pair.Key, pair.Value.State, pair.Value.Message)).ToArray();

    /// <summary>设置控件验证结果，并同步到支持统一验证接口的控件。</summary>
    public void SetValidation(Control control, ModernValidationState state, string? message = null)
    {
        ModernCompatibility.ThrowIfNull(control, nameof(control));
        RemoveEntry(control);
        message ??= string.Empty;
        if (state != ModernValidationState.None)
        {
            var entry = new Entry(state, message);
            _entries.Add(control, entry);
            control.Enter += ShowMessage;
            control.MouseEnter += ShowMessage;
            control.Disposed += ControlDisposed;
        }
        if (control is IModernValidationControl validation)
        {
            validation.ValidationState = state;
            validation.ValidationMessage = message;
        }
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>清除单个控件的验证结果。</summary>
    public void Clear(Control control) => SetValidation(control, ModernValidationState.None);

    /// <summary>清除所有验证结果。</summary>
    public void Clear()
    {
        foreach (var control in _entries.Keys.ToArray()) SetValidation(control, ModernValidationState.None);
    }

    /// <summary>为控件注册可取消的异步验证规则；再次验证会取消该控件的旧任务。</summary>
    public void SetAsyncValidator(Control control, Func<CancellationToken, ValueTask<ModernValidationOutcome>> validator)
    {
        ModernCompatibility.ThrowIfNull(control, nameof(control));
        ModernCompatibility.ThrowIfNull(validator, nameof(validator));
        _validators[control] = validator;
        control.Disposed -= ValidatorControlDisposed;
        control.Disposed += ValidatorControlDisposed;
    }

    /// <summary>并行执行所有异步验证规则。</summary>
    /// <returns>所有规则均未返回 Error 时返回 true。</returns>
    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _validators.Where(pair => !pair.Key.IsDisposed)
            .Select(pair => ValidateOneAsync(pair.Key, pair.Value, cancellationToken)).ToArray();
        var outcomes = await Task.WhenAll(tasks);
        return outcomes.All(outcome => outcome.State != ModernValidationState.Error);
    }

    /// <summary>聚焦并滚动到第一个错误控件。</summary>
    public bool FocusFirstInvalid()
    {
        var control = _entries.FirstOrDefault(pair => pair.Value.State == ModernValidationState.Error).Key;
        return control is not null && FocusInvalid(control);
    }

    /// <summary>定位并聚焦指定验证结果所属控件。</summary>
    public bool FocusInvalid(Control control)
    {
        ModernCompatibility.ThrowIfNull(control, nameof(control));
        if (control.IsDisposed || !_entries.ContainsKey(control)) return false;
        ScrollControlIntoView(control);
        control.Focus();
        ShowMessage(control, EventArgs.Empty);
        return true;
    }

    public event EventHandler? ValidationChanged;

    private void ShowMessage(object? sender, EventArgs e)
    {
        if (sender is not Control control || !_entries.TryGetValue(control, out var entry) || string.IsNullOrEmpty(entry.Message)) return;
        _toolTip.Show(control, entry.Message, entry.State == ModernValidationState.Error ? ModernToolTipKind.Error : ModernToolTipKind.Default, 3000);
    }

    private static void ScrollControlIntoView(Control control)
    {
        for (Control? current = control.Parent; current is not null; current = current.Parent)
            if (current is ScrollableControl scrollable) scrollable.ScrollControlIntoView(control);
    }

    private async Task<ModernValidationOutcome> ValidateOneAsync(Control control,
        Func<CancellationToken, ValueTask<ModernValidationOutcome>> validator, CancellationToken cancellationToken)
    {
        if (_validationRuns.Remove(control, out var previous)) { previous.Cancel(); previous.Dispose(); }
        var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _validationRuns[control] = run;
        SetValidation(control, ModernValidationState.Pending, LocalizationContext.Text(ModernUiTextKeys.Validating));
        try
        {
            var outcome = await validator(run.Token);
            if (!run.IsCancellationRequested && !control.IsDisposed) SetValidation(control, outcome.State, outcome.Message);
            return outcome;
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            return new ModernValidationOutcome(ModernValidationState.None, string.Empty);
        }
        finally
        {
            if (_validationRuns.TryGetValue(control, out var current) && ReferenceEquals(current, run)) _validationRuns.Remove(control);
            run.Dispose();
        }
    }

    private void ControlDisposed(object? sender, EventArgs e) { if (sender is Control control) RemoveEntry(control); }
    private void ValidatorControlDisposed(object? sender, EventArgs e)
    {
        if (sender is not Control control) return;
        _validators.Remove(control);
        if (_validationRuns.Remove(control, out var run)) { run.Cancel(); run.Dispose(); }
    }
    private void RemoveEntry(Control control)
    {
        if (!_entries.Remove(control)) return;
        control.Enter -= ShowMessage;
        control.MouseEnter -= ShowMessage;
        control.Disposed -= ControlDisposed;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var control in _entries.Keys.ToArray()) RemoveEntry(control);
            foreach (var control in _validators.Keys) control.Disposed -= ValidatorControlDisposed;
            _validators.Clear();
            foreach (var run in _validationRuns.Values) { run.Cancel(); run.Dispose(); }
            _validationRuns.Clear();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record Entry(ModernValidationState State, string Message);
}

/// <summary>描述一个控件的公开验证结果。</summary>
public sealed record ModernValidationResult(Control Control, ModernValidationState State, string Message);

/// <summary>表示一条异步验证规则的结果。</summary>
public sealed record ModernValidationOutcome(ModernValidationState State, string Message);
