using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildDateTime()
    {
        var date = new ModernDatePicker
        {
            Width = 280, Value = DateTime.Today, MinimumDate = DateTime.Today.AddMonths(-2),
            MaximumDate = DateTime.Today.AddMonths(3), CustomFormat = "yyyy-MM-dd"
        };
        date.DateEnabledPredicate = candidate => candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        date.ValueChanged += (_, _) => Report($"日期 = {date.Value:yyyy-MM-dd}");
        var openDate = DemoButton("展开日历", () => date.DroppedDown = true);
        var today = DemoButton("回到今天", () => { date.Value = DateTime.Today; Report("日期已重置为今天"); });
        AddSection("日期选择与禁用规则", "托管 Calendar、Min/Max、周末禁用、今天标记、键盘方向/PageUp/PageDown/Home/End/Enter/Esc。",
            Column(FormRows(("工作日期", date)), Row(openDate, today)));

        var range = new ModernDateRangePicker
        {
            Width = 520, StartDate = DateTime.Today.AddDays(-7), EndDate = DateTime.Today
        };
        range.RangeChanged += (_, _) => Report($"范围 = {range.StartDate:yyyy-MM-dd} ～ {range.EndDate:yyyy-MM-dd}");
        var reverseRange = DemoButton("制造反向范围", () =>
        {
            range.StartDate = DateTime.Today.AddDays(5);
            Report($"Start > End 后自动归一化：{range.StartDate:MM-dd} ～ {range.EndDate:MM-dd}");
        });
        var lastWeek = DemoButton("最近 7 天", () =>
        {
            range.StartDate = DateTime.Today.AddDays(-6);
            range.EndDate = DateTime.Today;
            Report("范围已设为最近 7 天");
        });
        AddSection("日期范围与重开同步", "两个原生输入共享完整范围；端点自动归一化，重开任一 Calendar 都显示起点、终点及区间高亮。",
            Column(FormRows(("日期范围", range)), Row(lastWeek, reverseRange)));

        var time = new ModernTimePicker { Width = 280, Value = DateTime.Now.TimeOfDay, CustomFormat = "HH:mm:ss" };
        time.ValueChanged += (_, _) => Report($"时间 = {time.Value:hh\\:mm\\:ss}");
        var setShift = DemoButton("设为 08:30", () => time.Value = new TimeSpan(8, 30, 0));
        AddSection("时间点编辑", "原生时/分/秒分段选择、方向键和现代全高步进表面；Value 保持一天内 TimeSpan 语义。",
            Column(FormRows(("运行时间", time)), Row(setShift)));

        var duration = new ModernDurationInput
        {
            Width = 420, Value = TimeSpan.FromMinutes(90), Minimum = TimeSpan.Zero,
            Maximum = TimeSpan.FromDays(10), Unit = ModernDurationUnit.Minutes
        };
        duration.ValueChanged += (_, _) => Report($"持续时间 = {duration.Value:c}（Unit={duration.Unit}）");
        var presets = Row(
            DemoButton("500 ms", () => { duration.Unit = ModernDurationUnit.Milliseconds; duration.Value = TimeSpan.FromMilliseconds(500); }),
            DemoButton("90 分钟", () => { duration.Unit = ModernDurationUnit.Minutes; duration.Value = TimeSpan.FromMinutes(90); }),
            DemoButton("2.5 小时", () => { duration.Unit = ModernDurationUnit.Hours; duration.Value = TimeSpan.FromHours(2.5); }));
        AddSection("持续时间与单位", "时间点和时长分离；毫秒/秒/分钟/小时/天切换不改变底层 TimeSpan，并遵守 Min/Max。",
            Column(FormRows(("持续时间", duration)), presets));
    }
}
