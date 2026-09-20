using System.Globalization;
using System.Reflection;
using System.Resources;
using ModernUI.Localization;

namespace ModernUI.WinForms;

/// <summary>ModernUI 框架拥有的稳定文本 Key。</summary>
public static class ModernUiTextKeys
{
    private const string Module = "modernUi";
    public static TextKey InputPlaceholder { get; } = new(Module, "InputPlaceholder");
    public static TextKey SelectPlaceholder { get; } = new(Module, "SelectPlaceholder");
    public static TextKey NumberIncrease { get; } = new(Module, "NumberIncrease");
    public static TextKey NumberDecrease { get; } = new(Module, "NumberDecrease");
    public static TextKey ShowPassword { get; } = new(Module, "ShowPassword");
    public static TextKey HidePassword { get; } = new(Module, "HidePassword");
    public static TextKey NumberValidationRange { get; } = new(Module, "NumberValidationRange");
    public static TextKey Expand { get; } = new(Module, "Expand");
    public static TextKey Collapse { get; } = new(Module, "Collapse");
    public static TextKey RemoveItem { get; } = new(Module, "RemoveItem");
    public static TextKey Validating { get; } = new(Module, "Validating");
    public static TextKey DateUnavailable { get; } = new(Module, "DateUnavailable");
    internal static TextKey SelectDate { get; } = new(Module, "SelectDate");
    internal static TextKey StartDate { get; } = new(Module, "StartDate");
    internal static TextKey EndDate { get; } = new(Module, "EndDate");
    internal static TextKey Pagination { get; } = new(Module, "Pagination");
    internal static TextKey FirstPage { get; } = new(Module, "FirstPage");
    internal static TextKey PreviousPage { get; } = new(Module, "PreviousPage");
    internal static TextKey NextPage { get; } = new(Module, "NextPage");
    internal static TextKey LastPage { get; } = new(Module, "LastPage");
    internal static TextKey PaginationSummary { get; } = new(Module, "PaginationSummary");
    internal static TextKey PaginationSummaryWithTotal { get; } = new(Module, "PaginationSummaryWithTotal");
    public static TextKey ValidationIssues { get; } = new(Module, "ValidationIssues");
    public static TextKey GoToValidationIssue { get; } = new(Module, "GoToValidationIssue");
    public static TextKey MoreActions { get; } = new(Module, "MoreActions");
    public static TextKey DurationMilliseconds { get; } = new(Module, "DurationMilliseconds");
    public static TextKey DurationSeconds { get; } = new(Module, "DurationSeconds");
    public static TextKey DurationMinutes { get; } = new(Module, "DurationMinutes");
    public static TextKey DurationHours { get; } = new(Module, "DurationHours");
    public static TextKey DurationDays { get; } = new(Module, "DurationDays");
    public static TextKey Confirm { get; } = new(Module, "Confirm");
    public static TextKey Cancel { get; } = new(Module, "Cancel");
    public static TextKey NoData { get; } = new(Module, "NoData");
}

/// <summary>创建独立的 ModernUI 本地化上下文和运行时语言管理器。</summary>
public static class ModernUiLocalization
{
    private static readonly ITextCatalog Catalog = new ResourceManagerTextCatalog("modernUi",
        new ResourceManager("ModernUI.WinForms.Localization.ModernUiStrings", Assembly.GetExecutingAssembly()));

    public static ITextCatalog TextCatalog => Catalog;

    public static ILocalizationContext DefaultContext { get; } = CreateContext(CultureInfo.GetCultureInfo("zh-CN"));

    public static ILocalizationContext CreateContext(CultureInfo culture) =>
        new FixedLocalizationContext(CreateSnapshot(culture));

    public static ILocalizationManager CreateManager(CultureInfo initialCulture) =>
        new LocalizationManager(CreateSnapshot(initialCulture),
            (culture, _) => Task.FromResult(CreateSnapshot(culture)));

    private static LocalizationSnapshot CreateSnapshot(CultureInfo culture) => new(culture, Catalog,
        culture.TextInfo.IsRightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight);
}
