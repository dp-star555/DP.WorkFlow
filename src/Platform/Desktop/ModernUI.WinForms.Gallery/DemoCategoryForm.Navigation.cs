using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildNavigation()
    {
        var tabImages = Own(new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit });
        tabImages.Images.Add(ModernIcons.CreateBitmap(ModernIconKind.Info, _theme.TextSecondary, 16));
        tabImages.Images.Add(ModernIcons.CreateBitmap(ModernIconKind.Settings, _theme.TextSecondary, 16));
        tabImages.Images.Add(ModernIcons.CreateBitmap(ModernIconKind.Search, _theme.TextSecondary, 16));
        var tabs = new ModernTabControl { Width = 720, Height = 220, ImageList = tabImages };
        tabs.TabPages.Add(new TabPage("概览") { ImageIndex = 0, Controls = { new Label { Text = "设备概览页面\r\n保留原生 TabPage 内容管理", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter } } });
        tabs.TabPages.Add(new TabPage("参数") { ImageIndex = 1, Controls = { new Label { Text = "参数配置页面", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter } } });
        tabs.TabPages.Add(new TabPage("诊断") { ImageIndex = 2, Controls = { new Label { Text = "运行诊断页面", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter } } });
        tabs.SelectedIndexChanged += (_, _) => Report($"Tab = {tabs.SelectedTab?.Text}");
        AddSection("带图标的页签导航", "原生 TabPage 管理、图标、键盘切换、现代标签头动画和内容宿主。", tabs);

        var pagination = new ModernPagination { Width = 560, TotalCount = 236, PageSize = 20, PageIndex = 3, ShowTotal = true };
        pagination.PageChanged += (_, _) => Report($"请求第 {pagination.PageIndex}/{pagination.PageCount} 页，PageSize={pagination.PageSize}");
        var pageSize = DemoButton("切换 PageSize", () =>
        {
            pagination.PageSize = pagination.PageSize == 20 ? 50 : 20;
            Report($"PageSize = {pagination.PageSize}，PageCount = {pagination.PageCount}");
        });
        AddSection("服务端分页", "首页、末页、前后页、总量、边界禁用、PageChanged 和动态 PageSize。", Column(pagination, Row(pageSize)));

        var collapsible = new ModernCollapsiblePanel { Width = 720, Height = 160, Text = "高级设置", Expanded = true };
        collapsible.Content = FormRows(
            ("超时", new ModernInputNumber { Width = 200, Value = 3000, Minimum = 100, Maximum = 30000, DecimalPlaces = 0 }),
            ("重试", new ModernSwitch { Checked = true }));
        var toggle = DemoButton("展开/折叠", () =>
        {
            collapsible.Expanded = !collapsible.Expanded;
            Report($"高级设置 Expanded = {collapsible.Expanded}");
        });
        AddSection("折叠容器", "任意组合内容、动画展开和公开 Expanded 状态。", Column(collapsible, Row(toggle)));

        var group = new ModernGroupBox { Width = 720, Height = 130, Text = "采集策略(&A)" };
        var groupedSwitch = new ModernSwitch { Location = new Point(22, 42), Checked = true, AccessibleName = "自动曝光" };
        var groupedInput = new ModernInputNumber
        {
            Location = new Point(110, 40), Width = 220, Value = 1500,
            DecimalPlaces = 0, SuffixText = " μs"
        };
        group.Controls.AddRange([groupedSwitch, groupedInput]);
        AddSection("原生分组容器", "ModernGroupBox 保留子控件容器、助记键、Tab 导航和 Grouping 无障碍角色，并使用主题化圆角边框。", group);

        var splitter = new ModernSplitter { Width = 720, Height = 180, InitialPanel2Size = 230, Orientation = Orientation.Vertical };
        splitter.Panel1.Controls.Add(new Label { Text = "主工作区\r\n拖动中间胶囊握柄", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        splitter.Panel2.Controls.Add(new Label { Text = "属性/诊断侧栏", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        splitter.SplitterMoved += (_, _) => Report($"SplitterDistance = {splitter.SplitterDistance}");
        AddSection("可拖动 Splitter", "固定 Panel2、最小尺寸、Hover/拖动反馈和 SplitterMoved。", splitter);

        var scrollContent = new DemoControlColumn(Enumerable.Range(1, 9).Select(index => (Control)new ModernPanel
        {
            Width = 680, Height = 54,
            Controls = { new Label { Text = $"滚动内容卡片 {index:00}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0) } }
        }));
        var scroll = new ModernScrollView { Width = 720, Height = 190, Content = scrollContent, WheelStep = 54, LiveScrollDuringThumbDrag = true };
        AddSection("ModernScrollView", "宽命中轨道、细胶囊滑块、滚轮边界路由、实时拖动和原生子 HWND 合成。", scroll);
    }
}
