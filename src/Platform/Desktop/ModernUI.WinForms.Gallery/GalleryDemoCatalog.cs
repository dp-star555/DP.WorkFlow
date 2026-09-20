namespace ModernUI.WinForms.Gallery;

internal enum GalleryDemoCategory
{
    Actions,
    Inputs,
    Selection,
    DataDisplay,
    NavigationAndLayout,
    FeedbackAndOverlays,
    DateAndTime,
    PropertyGrid,
    BusinessScenario
}

internal sealed record GalleryDemoDescriptor(
    GalleryDemoCategory Category,
    string Title,
    string Description,
    ModernIconKind Icon,
    string[] Features);

internal static class GalleryDemoCatalog
{
    public static IReadOnlyList<GalleryDemoDescriptor> All { get; } =
    [
        new(GalleryDemoCategory.Actions, "操作与命令", "按钮变体、共享命令、快捷键、菜单、加载与溢出", ModernIconKind.Play,
            ["Button", "Loading", "Command", "CommandBar", "ContextMenu", "Shortcut"]),
        new(GalleryDemoCategory.Inputs, "文本与数值输入", "原生编辑、双向滚动、自动完成、数据绑定与提交事件", ModernIconKind.Key,
            ["Input", "Password", "TextArea", "InputNumber", "ComboBox", "Slider"]),
        new(GalleryDemoCategory.Selection, "选择控件", "三态、业务值、绑定单/多选、标签与键盘导航", ModernIconKind.Info,
            ["Checkbox", "Radio", "Switch", "Select", "MultiSelect", "Segmented"]),
        new(GalleryDemoCategory.DataDisplay, "数据展示", "三态树、多列列表、可编辑表格、状态、进度与空状态", ModernIconKind.Search,
            ["Tree", "ListBox", "ListView", "DataGrid", "Badge", "Progress", "Empty"]),
        new(GalleryDemoCategory.NavigationAndLayout, "导航与布局", "图标页签、服务端分页、折叠、分隔和实时滚动", ModernIconKind.ChevronRight,
            ["Tabs", "Pagination", "Collapsible", "Splitter", "ScrollView", "StatusBar"]),
        new(GalleryDemoCategory.FeedbackAndOverlays, "反馈与浮层", "页面提示、队列反馈、通知操作、气泡、对话框与异步验证", ModernIconKind.Info,
            ["Alert", "Message", "Notification", "ToolTip", "Dialog", "Validation"]),
        new(GalleryDemoCategory.DateAndTime, "日期与时间", "禁用日期、范围同步、时间分段编辑与持续时间单位", ModernIconKind.Plus,
            ["DatePicker", "DateRange", "TimePicker", "Duration", "Keyboard"]),
        new(GalleryDemoCategory.PropertyGrid, "属性编辑器", "搜索、分类、描述、元数据、运行时刷新和自定义编辑器", ModernIconKind.Settings,
            ["TypeDescriptor", "Search", "INotifyPropertyChanged", "Validation", "Custom Editor"]),
        new(GalleryDemoCategory.BusinessScenario, "业务场景", "完整设备参数页：绑定、验证、命令、异步状态、本地化与保存恢复", ModernIconKind.Settings,
            ["BindingSource", "Validation", "Commands", "Async", "Localization", "Lifecycle"])
    ];
}
