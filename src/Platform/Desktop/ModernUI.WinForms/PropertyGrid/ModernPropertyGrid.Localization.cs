using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

public sealed partial class ModernPropertyGrid
{
    private void OnLocalizationChanged(object? sender, LocaleChangedEventArgs e) => BeginInvokeIfRequired(() =>
    {
        SuspendLayout();
        try
        {
            ApplyLocalization();
            Rebuild();
        }
        finally { ResumeLayout(true); }
    });

    private void ApplyLocalization()
    {
        _search.LocalizationContext = _localizationContext;
        _clearSearch.LocalizationContext = _localizationContext;
        ModernUiSettings.ApplyLocalization(_scrollViewport, _localizationContext);
        _search.PlaceholderText = T(PropertyGridTextKeys.SearchPlaceholder);
        _clearSearch.AccessibleName = T(PropertyGridTextKeys.SearchClear);
        _detailsSplitter.AccessibleName = T(PropertyGridTextKeys.SplitterAccessibleName);
        if (_selectedPropertyKey is null) _details.Text = T(PropertyGridTextKeys.DetailsEmpty);
        RightToLeft = _localizationContext.Current.TextDirection == TextDirection.RightToLeft
            ? RightToLeft.Yes : RightToLeft.No;
    }

    private string T(TextKey key, IReadOnlyDictionary<string, object?>? arguments = null) =>
        _localizationContext.Text(key, arguments);

    private PropertyPresentation Presentation(PropertyDescriptor property)
    {
        if (_presentations.TryGetValue(property.Name, out var existing)) return existing;
        var presentation = _presentationProvider.GetPresentation(_selectedObject!, property, _localizationContext.Current.Culture);
        if (string.IsNullOrWhiteSpace(presentation.PropertyKey) || string.IsNullOrWhiteSpace(presentation.CategoryKey))
            throw new InvalidOperationException($"Property presentation for '{property.Name}' must provide stable PropertyKey and CategoryKey values.");
        if (_presentations.Any(pair => pair.Key != property.Name
            && string.Equals(pair.Value.PropertyKey, presentation.PropertyKey, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Property presentation key '{presentation.PropertyKey}' is duplicated.");
        _presentations[property.Name] = presentation;
        return presentation;
    }

    private void OnObjectPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        BeginInvokeIfRequired(() =>
        {
            if (string.IsNullOrWhiteSpace(e.PropertyName) || !RefreshEditorValue(e.PropertyName)) Rebuild();
        });

    private void BeginInvokeIfRequired(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private void PostLayout(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (IsHandleCreated) BeginInvoke(() => { if (!IsDisposed && !Disposing) action(); }); else action();
    }
}
