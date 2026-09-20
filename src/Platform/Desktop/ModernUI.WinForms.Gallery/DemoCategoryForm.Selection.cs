using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildSelection()
    {
        var autoSave = new ModernCheckbox { Text = "自动保存", Checked = true, Width = 120 };
        var triState = new ModernCheckbox { Text = "部分选择", CheckState = CheckState.Indeterminate, ThreeState = true, Width = 120 };
        var primary = new ModernRadioButton { Text = "主通道", Checked = true, Width = 100 };
        var backup = new ModernRadioButton { Text = "备用通道", Width = 100 };
        var enabled = new ModernSwitch { Checked = true, AccessibleName = "启用采集" };
        autoSave.CheckedChanged += (_, _) => Report($"自动保存 = {autoSave.Checked}");
        triState.CheckStateChanged += (_, _) => Report($"三态选择 = {triState.CheckState}");
        primary.CheckedChanged += (_, _) => { if (primary.Checked) Report("通道 = 主通道"); };
        backup.CheckedChanged += (_, _) => { if (backup.Checked) Report("通道 = 备用通道"); };
        enabled.CheckedChanged += (_, _) => Report($"采集开关 = {enabled.Checked}");
        AddSection("基础选择与状态事件", "Checkbox 三态、Radio 分组、Switch 动画；键盘焦点与鼠标焦点分离并保留 UIA。",
            Row(autoSave, triState, primary, backup, enabled));

        var options = new BindingList<DemoOption>
        {
            new("continuous", "连续采集"), new("trigger", "外部触发"), new("single", "单帧采集")
        };
        var select = new ModernSelect
        {
            Width = 300, PlaceholderText = "选择采集模式", DataSource = options,
            DisplayMember = nameof(DemoOption.Name), ValueMember = nameof(DemoOption.Id)
        };
        select.SelectedValue = "continuous";
        select.SelectedIndexChanged += (_, _) => Report($"单选值 = {select.SelectedValue ?? "<空>"}");
        var multiple = new ModernSelectMultiple
        {
            Width = 520, PlaceholderText = "选择输出内容", DisplayMode = MultipleSelectionDisplayMode.Tags,
            MaxVisibleTags = 3, ValueMember = nameof(DemoOption.Id), DisplayMember = nameof(DemoOption.Name),
            DataSource = new BindingList<DemoOption>
            {
                new("raw", "原始图像"), new("result", "检测结果"), new("feature", "特征数据"),
                new("log", "运行日志"), new("thumbnail", "缩略图")
            }
        };
        multiple.SetSelectedValues(["raw", "result", "log", "thumbnail"]);
        multiple.SelectionChanged += (_, _) => Report($"多选值 = {string.Join(", ", multiple.SelectedValues)}");
        var clear = DemoButton("清空选择", () =>
        {
            select.SelectedValue = null;
            multiple.SetSelectedItems([]);
            Report("单选和多选已清空");
        });
        var displayMode = DemoButton("切换摘要/标签", () =>
        {
            multiple.DisplayMode = multiple.DisplayMode == MultipleSelectionDisplayMode.Tags
                ? MultipleSelectionDisplayMode.Summary : MultipleSelectionDisplayMode.Tags;
            Report($"多选展示模式 = {multiple.DisplayMode}");
        });
        AddSection("绑定单选与多选", "DisplayMember/ValueMember、SelectedValue(s)、Popup 键盘导航、Tag 删除、超量摘要和清空选择。",
            Column(FormRows(("单选", select), ("多选", multiple)), Row(clear, displayMode)));

        var segmented = new ModernSegmentedControl { Width = 600 };
        segmented.Items.Add(new ModernSegmentedItem("选择", "select") { Icon = ModernIconKind.Search });
        segmented.Items.Add(new ModernSegmentedItem("矩形", "rectangle"));
        segmented.Items.Add(new ModernSegmentedItem("椭圆", "ellipse"));
        segmented.Items.Add(new ModernSegmentedItem("多边形", "polygon"));
        segmented.Items.Add(new ModernSegmentedItem("不可用", "disabled") { Enabled = false });
        segmented.SelectedValue = "rectangle";
        segmented.SelectedIndexChanged += (_, _) => Report($"分段业务值 = {segmented.SelectedValue}");
        AddSection("带图标与禁用项的分段选择", "展示 SelectedValue、滑动动画、方向键/Home/End 和不可选项跳过。", segmented);
    }
}
