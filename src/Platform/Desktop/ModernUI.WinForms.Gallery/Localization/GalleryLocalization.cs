using System.Globalization;
using System.Reflection;
using System.Resources;
using ModernPropertyGrid.WinForms;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal static class GalleryLocalization
{
    private static readonly ITextCatalog GalleryCatalog = new ResourceManagerTextCatalog("gallery",
        new ResourceManager("ModernUI.WinForms.Gallery.Localization.GalleryStrings", Assembly.GetExecutingAssembly()));
    private static readonly ITextCatalog Catalog = new CompositeTextCatalog(
        ModernUiLocalization.TextCatalog, PropertyGridLocalization.TextCatalog, GalleryCatalog);
    public static ILocalizationManager CreateManager(CultureInfo culture) =>
        new LocalizationManager(Snapshot(culture), (next, _) => Task.FromResult(Snapshot(next)));
    public static ILocalizationContext CreateContext(CultureInfo culture, TextDirection direction) =>
        new FixedLocalizationContext(new LocalizationSnapshot(culture, Catalog, direction));
    public static TextKey Key(string name) => new("gallery", name);
    private static LocalizationSnapshot Snapshot(CultureInfo culture) => new(culture, Catalog,
        culture.TextInfo.IsRightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight);
}
