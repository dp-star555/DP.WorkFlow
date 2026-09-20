namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowPropertyPanel
{
    private System.ComponentModel.IContainer components = null!;

    /// <summary>释放参数模型、搜索计时器以及设计器组件。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchTimer.Dispose();
            DisposeModel();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region 组件设计器生成的代码

    /// <summary>
    /// 初始化参数面板的稳定外壳。每个具体参数行必须继续由运行时根据节点 CLR 属性动态创建。
    /// </summary>
    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        searchLayout = new TableLayoutPanel();
        _search = new TextBox();
        _searchSummary = new Label();
        _clearSearch = new Button();
        _content = new FlowLayoutPanel();
        _details = new Label();
        rootLayout.SuspendLayout();
        searchLayout.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(searchLayout, 0, 0);
        rootLayout.Controls.Add(_content, 0, 1);
        rootLayout.Controls.Add(_details, 0, 2);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        rootLayout.Size = new Size(390, 620);
        rootLayout.TabIndex = 0;
        // 
        // searchLayout
        // 
        searchLayout.ColumnCount = 3;
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36F));
        searchLayout.Controls.Add(_search, 0, 0);
        searchLayout.Controls.Add(_searchSummary, 1, 0);
        searchLayout.Controls.Add(_clearSearch, 2, 0);
        searchLayout.Dock = DockStyle.Fill;
        searchLayout.Padding = new Padding(8, 8, 6, 7);
        // 
        // _search
        // 
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0);
        _search.Name = "_search";
        _search.PlaceholderText = "搜索参数（Ctrl+F）";
        _search.TabIndex = 0;
        // 
        // _searchSummary
        // 
        _searchSummary.Dock = DockStyle.Fill;
        _searchSummary.ForeColor = Color.FromArgb(148, 163, 184);
        _searchSummary.TextAlign = ContentAlignment.MiddleRight;
        // 
        // _clearSearch
        // 
        _clearSearch.AccessibleName = "清除参数搜索";
        _clearSearch.Dock = DockStyle.Fill;
        _clearSearch.FlatStyle = FlatStyle.Flat;
        _clearSearch.Margin = new Padding(6, 0, 0, 0);
        _clearSearch.Name = "_clearSearch";
        _clearSearch.TabIndex = 1;
        _clearSearch.Text = "×";
        // 
        // _content
        // 
        _content.AutoScroll = true;
        _content.BackColor = Color.FromArgb(15, 23, 42);
        _content.Dock = DockStyle.Fill;
        _content.FlowDirection = FlowDirection.TopDown;
        _content.Location = new Point(3, 51);
        _content.Name = "_content";
        _content.Padding = new Padding(8);
        _content.Size = new Size(384, 490);
        _content.TabIndex = 1;
        _content.WrapContents = false;
        // 
        // _details
        // 
        _details.BackColor = Color.FromArgb(15, 23, 42);
        _details.BorderStyle = BorderStyle.FixedSingle;
        _details.Dock = DockStyle.Fill;
        _details.ForeColor = Color.FromArgb(148, 163, 184);
        _details.Location = new Point(3, 544);
        _details.Name = "_details";
        _details.Padding = new Padding(8, 6, 8, 6);
        _details.Size = new Size(384, 76);
        _details.TabIndex = 2;
        _details.Text = "参数说明\r\n选择或聚焦一个参数可查看用途、类型和内部名称。";
        // 
        // WorkflowPropertyPanel
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(15, 23, 42);
        Controls.Add(rootLayout);
        ForeColor = Color.FromArgb(226, 232, 240);
        Name = "WorkflowPropertyPanel";
        Size = new Size(390, 620);
        rootLayout.ResumeLayout(false);
        searchLayout.ResumeLayout(false);
        searchLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel rootLayout = null!;
    private TableLayoutPanel searchLayout = null!;
    private TextBox _search = null!;
    private Label _searchSummary = null!;
    private Button _clearSearch = null!;
    private FlowLayoutPanel _content = null!;
    private Label _details = null!;
}
