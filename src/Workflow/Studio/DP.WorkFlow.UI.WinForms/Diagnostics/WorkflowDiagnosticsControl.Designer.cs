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
        diagnosticsListView = new ModernUI.WinForms.ModernListView();
        severityColumn = new ColumnHeader();
        codeColumn = new ColumnHeader();
        nodeColumn = new ColumnHeader();
        messageColumn = new ColumnHeader();
        localizationProvider = new ModernUI.WinForms.ModernLocalizationProvider(components);
        SuspendLayout();
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
        Controls.Add(diagnosticsListView);
        Name = "WorkflowDiagnosticsControl";
        Size = new Size(720, 240);
        ResumeLayout(false);
    }

    #endregion

    private ModernUI.WinForms.ModernListView diagnosticsListView = null!;
    private ModernUI.WinForms.ModernLocalizationProvider localizationProvider = null!;
    private ColumnHeader severityColumn = null!;
    private ColumnHeader codeColumn = null!;
    private ColumnHeader nodeColumn = null!;
    private ColumnHeader messageColumn = null!;
}
