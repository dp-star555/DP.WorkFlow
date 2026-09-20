using System.Globalization;
using System.Reflection;
using System.Resources;
using ModernUI.Localization;

namespace ModernPropertyGrid.WinForms;

public static class PropertyGridTextKeys
{
    private static TextKey Key(string name) => new("propertyGrid", name);
    public static readonly TextKey SearchPlaceholder = Key("SearchPlaceholder");
    public static readonly TextKey SearchClear = Key("SearchClear");
    public static readonly TextKey SummaryCount = Key("SummaryCount");
    public static readonly TextKey SummaryFiltered = Key("SummaryFiltered");
    public static readonly TextKey EmptyNoObject = Key("EmptyNoObject");
    public static readonly TextKey EmptyNoMatch = Key("EmptyNoMatch");
    public static readonly TextKey DetailsEmpty = Key("DetailsEmpty");
    public static readonly TextKey DetailsValue = Key("DetailsValue");
    public static readonly TextKey DetailsNoDescription = Key("DetailsNoDescription");
    public static readonly TextKey ValidationInvalid = Key("ValidationInvalid");
    public static readonly TextKey CategoryHeader = Key("CategoryHeader");
    public static readonly TextKey MultiSelectUnsupported = Key("MultiSelectUnsupported");
    public static readonly TextKey SplitterAccessibleName = Key("SplitterAccessibleName");
}

public static class PropertyGridLocalization
{
    private static readonly ResourceManager Resources = new(
        "ModernUI.WinForms.PropertyGrid.Localization.PropertyGridStrings", Assembly.GetExecutingAssembly());
    private static readonly ITextCatalog PropertyGridCatalog = new ResourceManagerTextCatalog("propertyGrid", Resources);
    public static ITextCatalog TextCatalog { get; } = new CompositeTextCatalog(
        ModernUI.WinForms.ModernUiLocalization.TextCatalog, PropertyGridCatalog);
    public static ILocalizationContext CreateContext(CultureInfo culture) => new FixedLocalizationContext(
        new LocalizationSnapshot(culture, TextCatalog,
            culture.TextInfo.IsRightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight));
}
