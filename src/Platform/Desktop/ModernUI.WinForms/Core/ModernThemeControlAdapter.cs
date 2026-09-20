namespace ModernUI.WinForms;

/// <summary>
/// Centralizes theme discovery and assignment for public controls that cannot derive from
/// ModernControl because they preserve a native WinForms base class.
/// </summary>
internal static class ModernThemeControlAdapter
{
    public static bool TryGetTheme(Control control, out ModernTheme theme)
    {
        theme = control switch
        {
            ModernControl modern => modern.Theme,
            ModernTabControl tabs => tabs.Theme,
            ModernTreeView tree => tree.Theme,
            ModernListBox list => list.Theme,
            ModernListView listView => listView.Theme,
            ModernDataGridView grid => grid.Theme,
            ModernCheckedListBox checkedList => checkedList.Theme,
            ModernLinkLabel link => link.Theme,
            ModernGroupBox group => group.Theme,
            ModernMenuStrip menuStrip => menuStrip.Theme,
            ModernToolStrip toolStrip => toolStrip.Theme,
            ModernSplitter splitter => splitter.Theme,
            ModernStatusBar status => status.Theme,
            ModernPropertyGrid.WinForms.ModernPropertyGrid propertyGrid => propertyGrid.Theme,
            _ => null!
        };
        return theme is not null;
    }

    public static bool TryApplyTheme(Control control, ModernTheme theme)
    {
        switch (control)
        {
            case ModernControl modern: modern.Theme = theme; break;
            case ModernTabControl tabs: tabs.Theme = theme; break;
            case ModernTreeView tree: tree.Theme = theme; break;
            case ModernListBox list: list.Theme = theme; break;
            case ModernListView listView: listView.Theme = theme; break;
            case ModernDataGridView grid: grid.Theme = theme; break;
            case ModernCheckedListBox checkedList: checkedList.Theme = theme; break;
            case ModernLinkLabel link: link.Theme = theme; break;
            case ModernGroupBox group: group.Theme = theme; break;
            case ModernMenuStrip menuStrip: menuStrip.Theme = theme; break;
            case ModernToolStrip toolStrip: toolStrip.Theme = theme; break;
            case ModernSplitter splitter: splitter.Theme = theme; break;
            case ModernStatusBar status: status.Theme = theme; break;
            case ModernScrollView scroll: scroll.ApplyTheme(theme); break;
            case ModernPropertyGrid.WinForms.ModernPropertyGrid propertyGrid: propertyGrid.Theme = theme; break;
            default: return false;
        }
        return true;
    }
}
