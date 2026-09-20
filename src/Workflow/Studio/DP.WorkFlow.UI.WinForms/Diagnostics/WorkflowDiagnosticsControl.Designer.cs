namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowDiagnosticsControl
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeModel();
            DisposeLocalization();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region 组件设计器生成的代码

    /// <summary>初始化诊断命令、摘要和列表；行数据在运行时刷新。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        diagnosticsCommandBar = new ModernUI.WinForms.ModernCommandBar();
        diagnosticsAlert = new ModernUI.WinForms.ModernAlert();
        diagnosticsListView = new ModernUI.WinForms.ModernListView();
        severityColumn = new ColumnHeader();
        codeColumn = new ColumnHeader();
        nodeColumn = new ColumnHeader();
        messageColumn = new ColumnHeader();
        localizationProvider = new ModernUI.WinForms.ModernLocalizationProvider(components);
        rootLayout.SuspendLayout();
        SuspendLayout();
        // rootLayout
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(diagnosticsCommandBar, 0, 0);
        rootLayout.Controls.Add(diagnosticsAlert, 0, 1);
        rootLayout.Controls.Add(diagnosticsListView, 0, 2);
        // diagnosticsCommandBar
        diagnosticsCommandBar.Dock = DockStyle.Fill;
        diagnosticsCommandBar.Margin = new Padding(0, 0, 0, 4);
        diagnosticsCommandBar.Name = "diagnosticsCommandBar";
        // diagnosticsAlert
        diagnosticsAlert.Dock = DockStyle.Fill;
        diagnosticsAlert.Margin = new Padding(0, 0, 0, 6);
        diagnosticsAlert.Name = "diagnosticsAlert";
        diagnosticsAlert.Status = ModernUI.WinForms.ModernVisualStatus.Success;
        // diagnosticsListView
        diagnosticsListView.BorderStyle = BorderStyle.None;
        diagnosticsListView.Columns.AddRange(new ColumnHeader[] { severityColumn, codeColumn, nodeColumn, messageColumn });
        diagnosticsListView.Dock = DockStyle.Fill;
        diagnosticsListView.FullRowSelect = true;
        diagnosticsListView.HideSelection = false;
        diagnosticsListView.Name = "diagnosticsListView";
        diagnosticsListView.UseCompatibleStateImageBehavior = false;
        diagnosticsListView.View = View.Details;
        // columns
        severityColumn.Width = 78;
        codeColumn.Width = 100;
        nodeColumn.Width = 140;
        messageColumn.Width = 600;
        // WorkflowDiagnosticsControl
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(rootLayout);
        Name = "WorkflowDiagnosticsControl";
        Size = new Size(720, 240);
        rootLayout.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel rootLayout = null!;
    private ModernUI.WinForms.ModernCommandBar diagnosticsCommandBar = null!;
    private ModernUI.WinForms.ModernAlert diagnosticsAlert = null!;
    private ModernUI.WinForms.ModernListView diagnosticsListView = null!;
    private ModernUI.WinForms.ModernLocalizationProvider localizationProvider = null!;
    private ColumnHeader severityColumn = null!;
    private ColumnHeader codeColumn = null!;
    private ColumnHeader nodeColumn = null!;
    private ColumnHeader messageColumn = null!;
}
