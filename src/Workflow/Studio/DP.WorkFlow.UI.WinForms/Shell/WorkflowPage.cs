using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// Workflow 页面的基类，结构与 DP.Foundation 的 <c>FeaturePage</c> 相同：同样的刷新入口
/// （<see cref="OnRefresh"/>：页面可见时每秒一次，切换到本页、操作结束、主题变化时 force），
/// 同样的提示入口（<see cref="ShowMessage"/>）和卡片 / 表格 / 文字工厂，颜色全部取自当前主题。
/// 挂进操作台时把基类换成 FeaturePage，或用一个薄适配器把 <see cref="RefreshPage"/> / <see cref="ShowMessage"/> 接到外壳，页面代码不用改。
/// </summary>
/// <remarks>
/// 单独运行（例如工作室窗口）时 <see cref="AutoRefresh"/> 为真，页面按统一刷新时钟自己每秒刷新一次；
/// 由外壳驱动刷新时把它设为假。
/// </remarks>
public class WorkflowPage : UserControl
{
    private const int RefreshIntervalMilliseconds = 1000;
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _canOperate = true;

    /// <summary>初始化页面：填满父容器，按当前主题着色并跟随主题切换。</summary>
    public WorkflowPage()
    {
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Padding = new Padding(6);
        WorkflowRefreshClock.Subscribe(this, OnClock);
        VisibleChanged += (_, _) => { if (Visible && IsHandleCreated) RefreshPage(_canOperate, true); };
        WorkflowWinFormsTheme.Changed += OnThemeChanged;
    }

    /// <summary>是否由页面自己按统一刷新时钟每秒刷新；外壳驱动刷新时设为假。</summary>
    public bool AutoRefresh { get; set; } = true;

    /// <summary>当前是否允许操作（外壳按登录与权限给出；这只是交互准入，鉴权仍在动作入口）。</summary>
    public bool CanOperate => _canOperate;

    /// <summary>刷新入口（外壳调用）：页面可见时每秒一次（force 为假）；切换到本页、操作结束、主题变化后 force 为真。</summary>
    /// <param name="canOperate">是否允许操作；为假时页面禁用操作按钮。</param>
    /// <param name="force">是否强制全部刷新。</param>
    public void RefreshPage(bool canOperate, bool force)
    {
        if (IsDisposed) return;
        _canOperate = canOperate;
        _lastRefresh = DateTime.UtcNow;
        OnRefresh(canOperate, force);
    }

    /// <summary>页面刷新：只读缓存或快照，不做 I/O。</summary>
    /// <param name="canOperate">是否允许操作。</param>
    /// <param name="force">是否强制全部刷新。</param>
    protected virtual void OnRefresh(bool canOperate, bool force)
    {
    }

    /// <summary>本页发起的操作进入执行、完成或失败时的提示文字；页面重写它显示到自己的提示控件。</summary>
    /// <param name="message">提示文字。</param>
    public virtual void ShowMessage(string message)
    {
    }

    /// <summary>主题变化后调用：重新着色；页面自绘的部分重写它补上。</summary>
    public virtual void ApplyTheme() => WorkflowWinFormsTheme.Apply(this);

    /// <summary>卡片：带标题的圆角面板，圆角取当前主题。</summary>
    /// <param name="title">标题。</param>
    /// <param name="body">内容。</param>
    public ModernPanel Card(string title, Control body)
    {
        Guard.NotNull(body);
        var palette = WorkflowWinFormsTheme.Palette;
        var card = new ModernPanel { Dock = DockStyle.Fill, Margin = new Padding(6), Padding = new Padding(16), Radius = palette.Radius, ShowBorder = true };
        var heading = Label(title);
        heading.Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold);
        heading.Dock = DockStyle.Top;
        heading.Height = 30;
        body.Dock = DockStyle.Fill;
        card.Controls.Add(body);
        card.Controls.Add(heading);
        return card;
    }

    /// <summary>设计稿样式的卡片：42 px 标题行（粗体标题、灰色副标题、分隔线），下面是内容。</summary>
    /// <param name="title">标题。</param>
    /// <param name="subtitle">副标题；可为空。</param>
    /// <param name="body">内容。</param>
    /// <param name="dock">卡片停靠方向。</param>
    /// <param name="extent">停靠方向上的逻辑尺寸（填满时忽略）。</param>
    public static WorkflowCard Section(string title, string? subtitle, Control body, DockStyle dock = DockStyle.Fill, int extent = 0)
    {
        Guard.NotNull(body);
        var card = new WorkflowCard { Dock = dock, LogicalExtent = extent };
        body.Dock = DockStyle.Fill;
        card.Controls.Add(body);
        card.Controls.Add(new WorkflowCardHeader(title, subtitle));
        return card;
    }

    /// <summary>卡片之间的 10 px 间隔。</summary>
    /// <param name="dock">停靠方向。</param>
    public static WorkflowBlock Gap(DockStyle dock) => new() { Dock = dock, LogicalExtent = 10 };

    /// <summary>取卡片的标题行（用来更新副标题）。</summary>
    /// <param name="card">由 <see cref="Section"/> 创建的卡片。</param>
    public static WorkflowCardHeader HeaderOf(WorkflowCard card) => card.Controls.OfType<WorkflowCardHeader>().First();

    /// <summary>网格布局（列数，行样式）；一列时列宽占满。</summary>
    /// <param name="columns">列数。</param>
    /// <param name="rows">行样式。</param>
    public static TableLayoutPanel Grid(int columns, params RowStyle[] rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = columns, RowCount = rows.Length };
        foreach (var row in rows)
            grid.RowStyles.Add(row);
        if (columns == 1)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    /// <summary>单行文字（过长省略）。</summary>
    /// <param name="text">文字。</param>
    public static Label Label(string text = "") =>
        new() { Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

    /// <summary>只读表格（整行选择、列宽填满），表头与隔行取次一级表面色。</summary>
    /// <param name="captions">列标题。</param>
    public static ModernDataGridView Table(params string[] captions)
    {
        var table = new ModernDataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            Theme = WorkflowWinFormsTheme.Table(WorkflowWinFormsTheme.Modern)
        };
        foreach (var caption in captions)
            table.Columns.Add(caption, caption);
        return table;
    }

    /// <summary>
    /// 设置列宽：权重为 0 的列按内容（含列头）自适应，其余列按权重比例分剩下的宽度。
    /// 时间、状态、次数这类短列完整显示，长文本列占余下空间。
    /// </summary>
    /// <param name="table">表格。</param>
    /// <param name="weights">每列的权重，按列顺序。</param>
    public static void ColumnWidths(DataGridView table, params int[] weights)
    {
        Guard.NotNull(table);
        for (var i = 0; i < Math.Min(weights.Length, table.Columns.Count); i++)
        {
            var column = table.Columns[i];
            if (weights[i] <= 0)
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
            else
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                column.FillWeight = weights[i];
                column.MinimumWidth = 80;
            }
        }
    }

    /// <summary>滚动到指定行；表格不可见或高度放不下一行时跳过（此时设置会抛异常）。</summary>
    /// <param name="table">表格。</param>
    /// <param name="row">行号。</param>
    public static void ScrollTo(DataGridView table, int row)
    {
        Guard.NotNull(table);
        if (row < 0 || row >= table.RowCount || !table.Visible
            || table.DisplayRectangle.Height <= table.ColumnHeadersHeight + table.RowTemplate.Height) return;
        try
        {
            table.FirstDisplayedScrollingRowIndex = row;
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>重建表格后按稳定 ID 恢复选择；目标消失时保持未选中，不回退到第一行。</summary>
    /// <param name="table">表格。</param>
    /// <param name="id">要恢复的 ID。</param>
    /// <param name="key">取一行的 ID。</param>
    public static void RestoreSelection(DataGridView table, object? id, Func<DataGridViewRow, object?> key)
    {
        Guard.NotNull(table);
        Guard.NotNull(key);
        table.ClearSelection();
        table.CurrentCell = null;
        if (id is null)
            return;
        var row = table.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => Equals(key(row), id));
        if (row is not null)
        {
            table.CurrentCell = row.Cells[0];
            row.Selected = true;
        }
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTheme();
        RefreshPage(_canOperate, true);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) WorkflowWinFormsTheme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }

    private void OnClock()
    {
        if (!AutoRefresh || (DateTime.UtcNow - _lastRefresh).TotalMilliseconds < RefreshIntervalMilliseconds) return;
        RefreshPage(_canOperate, false);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        foreach (var table in Descendants(this).OfType<ModernDataGridView>())
            table.Theme = WorkflowWinFormsTheme.Table(WorkflowWinFormsTheme.Modern);
        ApplyTheme();
        RefreshPage(_canOperate, true);
        Invalidate(true);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
