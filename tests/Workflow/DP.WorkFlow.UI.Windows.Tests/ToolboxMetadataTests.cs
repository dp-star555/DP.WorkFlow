using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using ModernUI.WinForms;

namespace DP.WorkFlow.Tests;

// 控件库用例：保护控件工具箱元数据（分类/图标）完整性，只在控件库源码改动时才需要重跑。
[Trait(TestCategories.Category, TestCategories.UiControls)]
public sealed class ToolboxMetadataTests
{
    [Fact]
    public void HighContrastThemeUsesWindowsSystemColors()
    {
        var theme = ModernTheme.HighContrast;
        Assert.Equal(SystemColors.Window, theme.Background);
        Assert.Equal(SystemColors.WindowText, theme.Text);
        Assert.Equal(SystemColors.Highlight, theme.Primary);
        Assert.Equal(SystemColors.Info, theme.OverlaySurface);
        Assert.Equal(SystemColors.InfoText, theme.ToolTipText);
        Assert.False(theme.FocusRing.IsEmpty);
    }

    [Fact]
    public void PublicToolboxItems_HaveChineseDisplayNamesAndEmbeddedIcons()
    {
        var toolboxTypes = typeof(ModernControl).Assembly.GetExportedTypes()
            .Where(type => type.GetCustomAttribute<ToolboxItemAttribute>(inherit: false) is { } attribute &&
                           !string.IsNullOrEmpty(attribute.ToolboxItemTypeName))
            .OrderBy(type => type.FullName)
            .ToArray();

        Assert.NotEmpty(toolboxTypes);
        foreach (var type in toolboxTypes)
        {
            var attributes = TypeDescriptor.GetAttributes(type);
            var displayName = Assert.IsType<DisplayNameAttribute>(attributes[typeof(DisplayNameAttribute)]);
            Assert.NotEqual(type.Name, displayName.DisplayName);
            Assert.True(displayName.DisplayName.Any(character => character > 127),
                $"{type.FullName} does not have a Chinese toolbox display name.");

            var bitmap = Assert.IsType<ToolboxBitmapAttribute>(attributes[typeof(ToolboxBitmapAttribute)]);
            var image = bitmap.GetImage(type, large: false);
            Assert.NotNull(image);
            Assert.Equal(16, image.Width);
            Assert.Equal(16, image.Height);

        }
    }
}
