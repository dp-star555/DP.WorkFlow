using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildDataDisplay()
    {
        var progress = new ModernProgressBar { Width = 330, Value = 68, ShowPercentage = true, Status = ModernVisualStatus.Success };
        var indeterminate = new ModernProgressBar { Width = 220, Indeterminate = true, Status = ModernVisualStatus.Primary };
        var verticalProgress = new ModernProgressBar
        {
            Width = 100, Height = 120, Value = 42, ShowPercentage = true,
            Orientation = Orientation.Vertical, ReverseDirection = true,
            DisplayText = "42 / 100", Status = ModernVisualStatus.Warning
        };
        var badgeCount = new ModernBadge { Count = 128, OverflowCount = 99, Width = 52 };
        var badgeDot = new ModernBadge { Dot = true, Status = ModernVisualStatus.Success, AccessibleName = "设备在线" };
        var advance = DemoButton("推进 10%", () =>
        {
            progress.Value = progress.Value >= 100 ? 0 : progress.Value + 10;
            Report($"确定进度 = {progress.ProgressPercentage:0}%");
        });
        AddSection("Badge、进度与加载", "数量溢出、语义圆点、自定义文本、水平/垂直与反向进度、确定/不确定进度和 Spinner。",
            Column(Row(badgeCount, badgeDot, progress, new ModernSpinner()), Row(indeterminate, advance, verticalProgress)));

        var tree = new ModernTreeView { Width = 310, Height = 220, CheckBoxes = true };
        var line = tree.Nodes.Add("生产线 A · Main inspection and material handling workstation");
        line.Nodes.AddRange([new TreeNode("Line Camera 01 · 8192px acquisition channel") { Checked = true }, new TreeNode("PLC"), new TreeNode("机器人")]);
        tree.Nodes.Add("生产线 B").Nodes.AddRange([new TreeNode("备用相机"), new TreeNode("安全门")]);
        tree.ExpandAll();
        tree.AfterCheck += (_, e) => { if (e.Node is not null) Report($"Tree {e.Node.Text} = {tree.GetNodeCheckState(e.Node)}"); };
        tree.AfterSelect += (_, e) => { if (e.Node is not null) Report($"Tree 当前节点 = {e.Node.Text}"); };
        var list = new ModernListBox
        {
            Width = 230, Height = 220, HorizontalScrollbar = true, HorizontalExtent = 430
        };
        list.Items.AddRange(Enumerable.Range(1, 16)
            .Select(index => $"最近任务 {index:000} · Inspection Camera / Recipe-A")
            .Cast<object>().ToArray());
        list.SelectedIndexChanged += (_, _) => Report($"ListBox 当前任务 = {list.SelectedItem}");
        AddSection("Tree 三态与 ListBox", "可配置父子勾选传播；宽内容展示 RTL 感知的双轴现代滚动条，同时保留原生键盘、绑定和 UIA。", Row(tree, list));

        var checkedList = new ModernCheckedListBox { Width = 420, Height = 170, CheckOnClick = true };
        checkedList.Items.AddRange(["相机在线", "PLC 握手", "安全门关闭", "机器人回零", "配方已下发"]);
        checkedList.SetItemChecked(0, true);
        checkedList.SetItemChecked(1, true);
        checkedList.CheckedItemsChanged += (_, _) => Report($"CheckedList 已勾选 {checkedList.CheckedItems.Count} 项，焦点行 {checkedList.SelectedIndex}");
        AddSection("常驻批量勾选工作区", "ModernCheckedListBox 持续展示批量选择；焦点行与勾选集合彼此独立，保留原生 CheckedItems、键盘、Designer 和无障碍语义。", checkedList);

        var listView = new ModernListView { Width = 720, Height = 210, CheckBoxes = true, RowHeight = 32 };
        listView.Columns.Add("设备", 360);
        listView.Columns.Add("连接", 220);
        listView.Columns.Add("周期与最近诊断", 300, HorizontalAlignment.Right);
        foreach (var item in new[]
                 {
                     new[] { "Line Camera 01", "在线", "36.2 ms" },
                     new[] { "PLC", "在线", "2.0 ms" },
                     new[] { "Robot", "待机", "--" },
                     new[] { "Safety IO", "在线", "1.0 ms" }
                 })
        {
            var row = new ListViewItem(item[0]) { Checked = true };
            row.SubItems.Add(item[1]);
            row.SubItems.Add(item[2]);
            listView.Items.Add(row);
        }
        listView.SelectedIndexChanged += (_, _) => Report($"ListView 选中 {listView.SelectedItems.Count} 行");
        listView.ItemChecked += (_, e) => BeginInvoke((Action)(() => Report($"ListView {e.Item.Text} Checked = {e.Item.Checked}")));
        AddSection("多列 ListView", "Details 表头、整行选择、Checkbox、列对齐、Hover、键盘和原生 ListView 数据模型。", listView);

        var grid = new ModernDataGridView { Width = 720, Height = 230, AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "设备", Width = 360 });
        grid.Columns.Add(new ModernDataGridViewComboBoxColumn
        {
            Name = "state", HeaderText = "运行模式", Width = 240,
            Items = { "自动", "手动", "维护" }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "当前值与最近诊断", Width = 320 });
        grid.Rows.Add("Line Camera 01", "自动", "36.2 ms");
        grid.Rows.Add("PLC", "手动", "2 ms");
        grid.Rows.Add("Robot", "维护", "Home");
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                Report($"Grid [{e.RowIndex + 1}, {grid.Columns[e.ColumnIndex].HeaderText}] = {grid[e.ColumnIndex, e.RowIndex].Value}");
        };
        AddSection("可编辑 DataGridView", "文本编辑、ModernSelect 下拉列、一键进入编辑态、双轴滚动、RTL 沟槽、行选择和原生 DataGridView 绑定/UIA。", grid);

        var empty = new ModernEmptyState
        {
            Width = 360, Height = 130, Description = "当前筛选条件没有匹配设备",
            Icon = ModernIconKind.Search,
            ActionCommand = new ModernCommand(() => Report("空状态恢复命令已执行")) { Text = "清除筛选" }
        };
        AddSection("空状态与恢复命令", "无数据/无搜索结果的统一占位；点击空状态执行 ActionCommand。", empty);
    }
}
