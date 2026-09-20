using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>汇总显示 ModernValidationProvider 当前记录的验证问题。</summary>
[Description("ModernValidationSummary 验证结果摘要")]
[DisplayName("现代验证摘要")]
[ToolboxBitmap(typeof(ModernValidationSummary), "Toolbox.Icons.Feedback.bmp")]
[ToolboxItem(true)]
public sealed class ModernValidationSummary : ModernControl
{
    private ModernValidationProvider? _provider;

    public ModernValidationSummary()
    {
        AccessibleRole = AccessibleRole.Alert;
        Height = 76;
        MinimumSize = new Size(180, 40);
        Cursor = Cursors.Hand;
    }

    [Category("Behavior"), DefaultValue(null)]
    [Description("为摘要提供验证结果的协调器。")]
    public ModernValidationProvider? Provider
    {
        get => _provider;
        set
        {
            if (ReferenceEquals(_provider, value)) return;
            if (_provider is not null) _provider.ValidationChanged -= ProviderChanged;
            _provider = value;
            if (_provider is not null) _provider.ValidationChanged += ProviderChanged;
            ProviderChanged(this, EventArgs.Empty);
        }
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var results = Provider?.Results ?? [];
        if (results.Count == 0) return;
        var rect = RectangleF.Inflate(bounds, -1, -1);
        canvas.Fill(Geometry.Blend(Theme.Container, Theme.Error, Theme.IsDark ? .15f : .06f), rect, ScaleLogical(Theme.Radius));
        canvas.Draw(Theme.Error, ScaleLogical(1), rect, ScaleLogical(Theme.Radius));
        canvas.DrawIcon(ModernIconKind.Info, Theme.Error, new RectangleF(12, 12, 16, 16), ScaleLogical(1.5f));
        var text = string.Join("   ", results.Take(3).Select(result => result.Message));
        if (results.Count > 3) text += $"   +{results.Count - 3}";
        canvas.DrawText(FrameworkText(ModernUiTextKeys.ValidationIssues,
            new Dictionary<string, object?> { ["count"] = results.Count }), Font, Theme.Error, new Rectangle(38, 7, Width - 50, 24), ContentAlignment.MiddleLeft);
        canvas.DrawText(text, Font, Theme.TextSecondary, new Rectangle(38, 31, Width - 50, Height - 38), ContentAlignment.TopLeft);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ValidationSummaryAccessibleObject(this);

    protected override void OnClick(EventArgs e) { base.OnClick(e); Provider?.FocusFirstInvalid(); }

    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        UpdateAccessibleSummary();
    }

    private void ProviderChanged(object? sender, EventArgs e)
    {
        UpdateAccessibleSummary();
        Invalidate();
        if (IsHandleCreated)
        {
            AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
            AccessibilityNotifyClients(ModernAccessibilityEvents.LiveRegionChanged, -1);
        }
    }

    private void UpdateAccessibleSummary()
    {
        var count = Provider?.Results.Count ?? 0;
        AccessibleName = count == 0 ? string.Empty : FrameworkText(ModernUiTextKeys.ValidationIssues,
            new Dictionary<string, object?> { ["count"] = count });
    }

    private sealed class ValidationSummaryAccessibleObject(ModernValidationSummary owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Alert;
        public override int GetChildCount() => owner.Provider?.Results.Count ?? 0;
        public override AccessibleObject? GetChild(int index)
        {
            var results = owner.Provider?.Results ?? [];
            return index >= 0 && index < results.Count ? new ValidationResultAccessibleObject(owner, this, results[index]) : null;
        }
    }

    private sealed class ValidationResultAccessibleObject(
        ModernValidationSummary owner,
        AccessibleObject parent,
        ModernValidationResult result) : AccessibleObject
    {
        public override string? Name { get => result.Message; set { } }
        public override AccessibleRole Role => AccessibleRole.Alert;
        public override AccessibleObject? Parent => parent;
        public override Rectangle Bounds => owner.IsHandleCreated ? owner.RectangleToScreen(owner.ClientRectangle) : Rectangle.Empty;
        public override AccessibleStates State => AccessibleStates.Focusable | (result.Control.Enabled ? AccessibleStates.None : AccessibleStates.Unavailable);
        public override string? DefaultAction => owner.FrameworkText(ModernUiTextKeys.GoToValidationIssue);
        public override void DoDefaultAction() => owner.Provider?.FocusInvalid(result.Control);
    }

    protected override void Dispose(bool disposing) { if (disposing && _provider is not null) _provider.ValidationChanged -= ProviderChanged; base.Dispose(disposing); }
}
