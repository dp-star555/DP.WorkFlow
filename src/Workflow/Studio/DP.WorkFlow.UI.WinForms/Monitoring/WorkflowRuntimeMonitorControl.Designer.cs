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

    /// <summary>初始化现代命令栏、运行摘要、监视页签和固定列表列。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        runtimeCommandBar = new ModernUI.WinForms.ModernCommandBar();
        runtimeAlert = new ModernUI.WinForms.ModernAlert();
        monitorTabs = new ModernUI.WinForms.ModernTabControl();
        tokensPage = new TabPage();
        tokensListView = new ModernUI.WinForms.ModernListView();
        scopesPage = new TabPage();
        scopesListView = new ModernUI.WinForms.ModernListView();
        childrenPage = new TabPage();
        childrenListView = new ModernUI.WinForms.ModernListView();
        tracePage = new TabPage();
        traceListView = new ModernUI.WinForms.ModernListView();
        traceToolbar = new FlowLayoutPanel();
        traceFilterTextBox = new ModernUI.WinForms.ModernInput();
        traceDateRange = new ModernUI.WinForms.ModernDateRangePicker();
        pauseTraceCheckBox = new ModernUI.WinForms.ModernCheckbox();
        outputPage = new TabPage();
        outputListView = new ModernUI.WinForms.ModernListView();
        timingPage = new TabPage();
        timingListView = new ModernUI.WinForms.ModernListView();
        localizationProvider = new ModernUI.WinForms.ModernLocalizationProvider(components);
        validationProvider = new ModernUI.WinForms.ModernValidationProvider(components);
        commandManager = new ModernUI.WinForms.ModernCommandManager(components);
        rootLayout.SuspendLayout();
        monitorTabs.SuspendLayout();
        tracePage.SuspendLayout();
        traceToolbar.SuspendLayout();
        SuspendLayout();

        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(runtimeCommandBar, 0, 0);
        rootLayout.Controls.Add(runtimeAlert, 0, 1);
        rootLayout.Controls.Add(monitorTabs, 0, 2);

        runtimeCommandBar.Dock = DockStyle.Fill;
        runtimeCommandBar.Margin = new Padding(0, 0, 0, 4);
        runtimeCommandBar.Name = "runtimeCommandBar";
        runtimeAlert.Dock = DockStyle.Fill;
        runtimeAlert.Margin = new Padding(0, 0, 0, 6);
        runtimeAlert.Name = "runtimeAlert";
        runtimeAlert.Status = ModernUI.WinForms.ModernVisualStatus.Primary;

        monitorTabs.TabPages.Add(tokensPage);
        monitorTabs.TabPages.Add(scopesPage);
        monitorTabs.TabPages.Add(childrenPage);
        monitorTabs.TabPages.Add(tracePage);
        monitorTabs.TabPages.Add(outputPage);
        monitorTabs.TabPages.Add(timingPage);
        monitorTabs.Dock = DockStyle.Fill;
        monitorTabs.Name = "monitorTabs";
        monitorTabs.SelectedIndex = 0;

        ConfigurePage(tokensPage, tokensListView);
        ConfigureList(tokensListView, 100, 150, 160, 190);
        ConfigurePage(scopesPage, scopesListView);
        ConfigureList(scopesListView, 100, 160, 160, 100, 90);
        ConfigurePage(childrenPage, childrenListView);
        ConfigureList(childrenListView, 150, 180, 120, 110, 260);

        tracePage.Padding = new Padding(3);
        tracePage.UseVisualStyleBackColor = false;
        ConfigureList(traceListView, 78, 120, 140, 90, 140, 120, 360);
        traceListView.Dock = DockStyle.Fill;
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
        tracePage.Controls.Add(traceListView);
        tracePage.Controls.Add(traceToolbar);

        ConfigurePage(outputPage, outputListView);
        ConfigureList(outputListView, 130, 180, 560);
        ConfigurePage(timingPage, timingListView);
        ConfigureList(timingListView, 190, 110, 130, 130, 140);

        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(rootLayout);
        Name = "WorkflowRuntimeMonitorControl";
        Size = new Size(980, 360);
        rootLayout.ResumeLayout(false);
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

    private static void ConfigureList(ListView list, params int[] widths)
    {
        list.BorderStyle = BorderStyle.None;
        list.Dock = DockStyle.Fill;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.UseCompatibleStateImageBehavior = false;
        list.View = View.Details;
        foreach (var width in widths) list.Columns.Add(string.Empty, width);
    }

    #endregion

    private TableLayoutPanel rootLayout = null!;
    private ModernUI.WinForms.ModernCommandBar runtimeCommandBar = null!;
    private ModernUI.WinForms.ModernAlert runtimeAlert = null!;
    private ModernUI.WinForms.ModernTabControl monitorTabs = null!;
    private TabPage tokensPage = null!;
    private ModernUI.WinForms.ModernListView tokensListView = null!;
    private TabPage scopesPage = null!;
    private ModernUI.WinForms.ModernListView scopesListView = null!;
    private TabPage childrenPage = null!;
    private ModernUI.WinForms.ModernListView childrenListView = null!;
    private TabPage tracePage = null!;
    private ModernUI.WinForms.ModernListView traceListView = null!;
    private FlowLayoutPanel traceToolbar = null!;
    private ModernUI.WinForms.ModernInput traceFilterTextBox = null!;
    private ModernUI.WinForms.ModernDateRangePicker traceDateRange = null!;
    private ModernUI.WinForms.ModernCheckbox pauseTraceCheckBox = null!;
    private TabPage outputPage = null!;
    private ModernUI.WinForms.ModernListView outputListView = null!;
    private TabPage timingPage = null!;
    private ModernUI.WinForms.ModernListView timingListView = null!;
    private ModernUI.WinForms.ModernLocalizationProvider localizationProvider = null!;
    private ModernUI.WinForms.ModernValidationProvider validationProvider = null!;
    private ModernUI.WinForms.ModernCommandManager commandManager = null!;
}
