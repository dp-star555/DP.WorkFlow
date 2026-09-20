using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class VisualStatesForm
{
    private string G(string name) => _localizationManager.Context.Text(GalleryLocalization.Key(name));

    private T LocalizedText<T>(T control, string key) where T : Control
    {
        BindGalleryText(key, text => control.Text = text);
        return control;
    }

    private ModernInput LocalizedPlaceholder(ModernInput input, string key)
    {
        BindGalleryText(key, text => input.PlaceholderText = text);
        return input;
    }

    private void BindGalleryText(string key, Action<string> apply)
    {
        void Update() => apply(G(key));
        _galleryTextBindings.Add(Update);
        Update();
    }

    private void ApplyGalleryTexts()
    {
        SuspendLayout();
        try { foreach (var update in _galleryTextBindings) update(); }
        finally { ResumeLayout(true); }
    }

    private void UpdateDpiCaption()
    {
        var scalePercent = (int)Math.Round(DeviceDpi / 96d * 100d);
        _titleLabel.Text = $"{G("HeaderTitle")} · {scalePercent}% ({DeviceDpi} DPI)";
    }

    private void ApplyCurrentTheme()
    {
        _themeLabel.Text = G(_theme.IsDark ? "ThemeDark" : "ThemeLight");
        ModernUiSettings.DefaultTheme = _theme;
        ModernUiSettings.ApplyTheme(this, _theme);
        ApplySecondaryText(this);
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _localizationManager is IDisposable disposable) disposable.Dispose();
        base.Dispose(disposing);
    }

    private void ApplySecondaryText(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (Equals(child.Tag, "secondary")) child.ForeColor = _theme.TextSecondary;
            ApplySecondaryText(child);
        }
    }
}
