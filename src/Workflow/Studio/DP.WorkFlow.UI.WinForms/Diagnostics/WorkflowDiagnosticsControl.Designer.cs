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

    /// <summary>初始化诊断表格；行数据在运行时刷新。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        diagnosticsTable = WorkflowPage.Table(string.Empty, string.Empty, string.Empty, string.Empty);
        localizationProvider = new ModernUI.WinForms.ModernLocalizationProvider(components);
        SuspendLayout();
        // diagnosticsTable：级别、代码按内容，节点和说明按比例。
        diagnosticsTable.Name = "diagnosticsTable";
        WorkflowPage.ColumnWidths(diagnosticsTable, 0, 0, 25, 75);
        // WorkflowDiagnosticsControl
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(diagnosticsTable);
        Name = "WorkflowDiagnosticsControl";
        Size = new Size(720, 240);
        ResumeLayout(false);
    }

    #endregion

    private ModernUI.WinForms.ModernDataGridView diagnosticsTable = null!;
    private ModernUI.WinForms.ModernLocalizationProvider localizationProvider = null!;
}
