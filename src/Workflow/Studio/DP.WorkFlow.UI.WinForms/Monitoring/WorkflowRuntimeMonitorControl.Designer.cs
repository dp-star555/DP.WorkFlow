namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowRuntimeMonitorControl
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeRuntimeMonitor();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region 组件设计器生成的代码

    /// <summary>初始化监视页签和各页表格。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        monitorTabs = new ModernUI.WinForms.ModernTabControl();
        tokensPage = new TabPage();
        tokensTable = WorkflowPage.Table();
        scopesPage = new TabPage();
        scopesTable = WorkflowPage.Table();
        childrenPage = new TabPage();
        childrenTable = WorkflowPage.Table();
        tracePage = new TabPage();
        traceTable = WorkflowPage.Table();
        traceToolbar = new FlowLayoutPanel();
        traceFilterTextBox = new ModernUI.WinForms.ModernInput();
        traceDateRange = new ModernUI.WinForms.ModernDateRangePicker();
        pauseTraceCheckBox = new ModernUI.WinForms.ModernCheckbox();
        outputPage = new TabPage();
        outputTable = WorkflowPage.Table();
        timingPage = new TabPage();
        timingTable = WorkflowPage.Table();
        localizationProvider = new ModernUI.WinForms.ModernLocalizationProvider(components);
        validationProvider = new ModernUI.WinForms.ModernValidationProvider(components);
        commandManager = new ModernUI.WinForms.ModernCommandManager(components);
        monitorTabs.SuspendLayout();
        tracePage.SuspendLayout();
        traceToolbar.SuspendLayout();
        SuspendLayout();

        monitorTabs.TabPages.Add(tracePage);
        monitorTabs.TabPages.Add(tokensPage);
        monitorTabs.TabPages.Add(scopesPage);
        monitorTabs.TabPages.Add(childrenPage);
        monitorTabs.TabPages.Add(outputPage);
        monitorTabs.TabPages.Add(timingPage);
        monitorTabs.Dock = DockStyle.Fill;
        monitorTabs.Name = "monitorTabs";
        monitorTabs.SelectedIndex = 0;

        ConfigurePage(tokensPage, tokensTable);
        ConfigureTable(tokensTable, 0, 30, 30, 40);
        ConfigurePage(scopesPage, scopesTable);
        ConfigureTable(scopesTable, 0, 50, 50, 0, 0);
        ConfigurePage(childrenPage, childrenTable);
        ConfigureTable(childrenTable, 25, 30, 25, 0, 20);

        tracePage.Padding = new Padding(3);
        tracePage.UseVisualStyleBackColor = false;
        ConfigureTable(traceTable, 0, 0, 20, 0, 20, 0, 60);
        traceTable.Dock = DockStyle.Fill;
        traceToolbar.Controls.Add(traceFilterTextBox);
        traceToolbar.Controls.Add(traceDateRange);
        traceToolbar.Controls.Add(pauseTraceCheckBox);
        traceToolbar.Dock = DockStyle.Top;
        traceToolbar.Height = 42;
        traceToolbar.Name = "traceToolbar";
        traceToolbar.Padding = new Padding(2, 3, 2, 3);
        traceToolbar.WrapContents = false;
        traceFilterTextBox.Name = "traceFilterTextBox";
        traceFilterTextBox.Width = 250;
        traceDateRange.Name = "traceDateRange";
        traceDateRange.Width = 310;
        pauseTraceCheckBox.Name = "pauseTraceCheckBox";
        pauseTraceCheckBox.Width = 140;
        pauseTraceCheckBox.Padding = new Padding(4, 6, 4, 0);
        tracePage.Controls.Add(traceTable);
        tracePage.Controls.Add(traceToolbar);

        ConfigurePage(outputPage, outputTable);
        ConfigureTable(outputTable, 0, 25, 75);
        ConfigurePage(timingPage, timingTable);
        ConfigureTable(timingTable, 1, 0, 0, 0, 0);

        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(monitorTabs);
        Name = "WorkflowRuntimeMonitorControl";
        Size = new Size(980, 360);
        monitorTabs.ResumeLayout(false);
        tracePage.ResumeLayout(false);
        traceToolbar.ResumeLayout(false);
        traceToolbar.PerformLayout();
        ResumeLayout(false);
    }

    private static void ConfigurePage(TabPage page, Control content)
    {
        page.Padding = new Padding(3);
        page.UseVisualStyleBackColor = false;
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
    }

    /// <summary>按权重建列：0 按内容自适应，其余按比例分剩下的宽度（列头文字在本地化时设置）。</summary>
    private static void ConfigureTable(ModernUI.WinForms.ModernDataGridView table, params int[] weights)
    {
        foreach (var _ in weights) table.Columns.Add(new DataGridViewTextBoxColumn());
        WorkflowPage.ColumnWidths(table, weights);
    }

    #endregion

    private ModernUI.WinForms.ModernTabControl monitorTabs = null!;
    private TabPage tokensPage = null!;
    private ModernUI.WinForms.ModernDataGridView tokensTable = null!;
    private TabPage scopesPage = null!;
    private ModernUI.WinForms.ModernDataGridView scopesTable = null!;
    private TabPage childrenPage = null!;
    private ModernUI.WinForms.ModernDataGridView childrenTable = null!;
    private TabPage tracePage = null!;
    private ModernUI.WinForms.ModernDataGridView traceTable = null!;
    private FlowLayoutPanel traceToolbar = null!;
    private ModernUI.WinForms.ModernInput traceFilterTextBox = null!;
    private ModernUI.WinForms.ModernDateRangePicker traceDateRange = null!;
    private ModernUI.WinForms.ModernCheckbox pauseTraceCheckBox = null!;
    private TabPage outputPage = null!;
    private ModernUI.WinForms.ModernDataGridView outputTable = null!;
    private TabPage timingPage = null!;
    private ModernUI.WinForms.ModernDataGridView timingTable = null!;
    private ModernUI.WinForms.ModernLocalizationProvider localizationProvider = null!;
    private ModernUI.WinForms.ModernValidationProvider validationProvider = null!;
    private ModernUI.WinForms.ModernCommandManager commandManager = null!;
}
