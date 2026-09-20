using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildPropertyGrid()
    {
        var settings = new DemoSettings();
        var grid = new ModernPropertyGrid.WinForms.ModernPropertyGrid { Width = 760, Height = 500 };
        grid.RegisterEditor(new DemoPropertyActionEditorProvider());
        grid.SelectedObject = settings;
        grid.PropertyValueChanged += (_, e) => Report($"{e.Property.DisplayName}: {e.OldValue ?? "<空>"} → {e.NewValue ?? "<空>"}");
        grid.ValidationFailed += (_, e) => Report($"{e.Property.DisplayName} 验证失败：{e.Exception.Message}");
        var external = DemoButton("外部更新增益", () =>
        {
            settings.Gain = settings.Gain >= 24 ? 0 : Math.Round(settings.Gain + .5, 1);
            Report($"INotifyPropertyChanged 外部更新 Gain = {settings.Gain:0.0} dB");
        });
        var refresh = DemoButton("刷新属性", () =>
        {
            grid.RefreshProperties();
            Report("已重新通过 TypeDescriptor 发现属性");
        });
        AddSection("完整属性编辑", "Ctrl+F 搜索/Esc 清除、分类折叠、描述面板、范围与单位、Enum、MultiSelect、只读属性、自定义 Action 编辑器和运行时刷新。",
            Column(grid, Row(external, refresh)));
    }
}
