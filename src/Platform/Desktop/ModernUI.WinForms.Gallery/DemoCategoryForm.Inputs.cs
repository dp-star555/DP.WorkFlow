using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildInputs()
    {
        var input = new ModernInput { Width = 280, Text = "Line Camera 01", PlaceholderText = "设备名称", MaxLength = 24 };
        var password = new ModernInput { Width = 280, Text = "camera-admin", UseSystemPasswordChar = true, ShowPasswordRevealButton = true, PlaceholderText = "访问密钥" };
        var readOnly = new ModernInput { Width = 280, Text = "CAM-2026-A001", ReadOnly = true };
        var casing = new ModernInput { Width = 280, PlaceholderText = "自动转换为大写", CharacterCasing = CharacterCasing.Upper };
        input.TextChanged += (_, _) => Report($"设备名称 = {input.Text}（{input.Text.Length}/24）");
        AddSection("原生文本能力", "输入法、密码、只读、MaxLength、大小写转换、选择、剪贴板和撤销仍由原生 TextBox 提供。",
            FormRows(("普通输入", input), ("密码输入", password), ("只读标识", readOnly), ("大写转换", casing)));

        var masked = new ModernMaskedInput { Width = 280, Mask = "0000-0000", Text = "20260813" };
        var rich = new ModernRichTextBox
        {
            Width = 620, Height = 110,
            Text = "维护记录\r\n支持 RTF、原生选择、链接、撤销、剪贴板和运行时只读。"
        };
        masked.TextCommitted += (_, _) => Report($"掩码文本已提交：{masked.Text}");
        rich.TextCommitted += (_, _) => Report($"富文本已提交：{rich.Text.Length} 字符");
        AddSection("掩码与富文本编辑", "ModernMaskedInput 保留掩码验证；ModernRichTextBox 保留 RTF、链接、选择和撤销；Committed 只代表用户完成编辑。",
            Column(FormRows(("批次号", masked)), rich));

        var longLines = Enumerable.Range(1, 18)
            .Select(index => $"{index:00}  采集日志：Line Camera 01 / Exposure=1500 / Result=OK")
            .ToArray();
        var area = new ModernTextArea
        {
            Width = 680, Height = 150, Lines = longLines, ScrollBars = ScrollBars.Both,
            WordWrap = false, AcceptsTab = true
        };
        var wrap = DemoButton("切换自动换行", () =>
        {
            area.WordWrap = !area.WordWrap;
            Report($"TextArea WordWrap = {area.WordWrap}；Shift+滚轮强制横向滚动");
        });
        AddSection("多行文本与滚动", "双向胶囊滚动条、长行、Tab/Enter、普通滚轮纵向和 Shift+滚轮横向。",
            Column(area, Row(wrap)));

        var comboItems = new BindingList<DemoOption>
        {
            new("auto", "自动模式"), new("manual", "手动模式"), new("maintenance", "维护模式")
        };
        var combo = new ModernComboBox
        {
            Width = 280, DataSource = comboItems, DisplayMember = nameof(DemoOption.Name),
            ValueMember = nameof(DemoOption.Id), Text = "自动模式", DropDownWidth = 340
        };
        combo.SelectedIndexChanged += (_, _) => Report($"ComboBox 选择值 = {combo.SelectedValue ?? combo.Text}");
        var addCandidate = DemoButton("追加候选", () =>
        {
            comboItems.Add(new($"mode-{comboItems.Count + 1}", $"动态模式 {comboItems.Count + 1}"));
            Report($"绑定候选已动态增加到 {comboItems.Count} 项");
        });
        var number = new ModernInputNumber
        {
            Width = 240, Minimum = 10, Maximum = 10000, Value = 1500, Increment = 10,
            DecimalPlaces = 0, ThousandsSeparator = true, PrefixText = "≈ ", SuffixText = " μs"
        };
        var slider = new ModernSlider
        {
            Width = 360, Minimum = 0, Maximum = 100, Value = 42, Step = 5,
            ShowValue = true, TickStyle = TickStyle.BottomRight, TickFrequency = 20,
            ValueFormatString = "0'%'"
        };
        number.ValueChanged += (_, _) => Report($"曝光时间 = {number.Value:0} μs");
        slider.ValueChanged += (_, _) => Report($"阈值预览 = {slider.Value:0}");
        slider.ValueCommitted += (_, _) => ModernMessage.Info(this, $"阈值已提交：{slider.Value:0}");
        AddSection("自动完成、绑定与数值提交", "可编辑 ComboBox 支持自由输入/自动完成和 IBindingList 更新；Number 支持格式、前后缀和范围策略；Slider 支持刻度、方向和提交事件。",
            Column(FormRows(("工作模式", combo), ("曝光时间", number), ("阈值", slider)), Row(addCandidate)));
    }
}
