namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowNodeEditorDialog
{
    private System.ComponentModel.IContainer components = null!;

    /// <summary>释放页面模型持有的图像订阅、子会话和设计器组件。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows 窗体设计器生成的代码

    /// <summary>
    /// 初始化统一节点窗口的稳定外壳。页面内容由 WorkflowNodeEditorModel.Pages 在运行时装入。
    /// </summary>
    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        workspacePanel = new Panel();
        commandPanel = new FlowLayoutPanel();
        cancelButton = new ModernUI.WinForms.ModernButton();
        okButton = new ModernUI.WinForms.ModernButton();
        applyButton = new ModernUI.WinForms.ModernButton();
        rootLayout.SuspendLayout();
        commandPanel.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(workspacePanel, 0, 0);
        rootLayout.Controls.Add(commandPanel, 0, 1);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.RowCount = 2;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        rootLayout.Size = new Size(720, 700);
        rootLayout.TabIndex = 0;
        // 
        // workspacePanel
        // 
        workspacePanel.Dock = DockStyle.Fill;
        workspacePanel.Location = new Point(3, 3);
        workspacePanel.Name = "workspacePanel";
        workspacePanel.Padding = new Padding(1);
        workspacePanel.Size = new Size(714, 646);
        workspacePanel.TabIndex = 1;
        // 
        // commandPanel
        // 
        commandPanel.Controls.Add(cancelButton);
        commandPanel.Controls.Add(okButton);
        commandPanel.Controls.Add(applyButton);
        commandPanel.Dock = DockStyle.Fill;
        commandPanel.FlowDirection = FlowDirection.RightToLeft;
        commandPanel.Location = new Point(3, 655);
        commandPanel.Name = "commandPanel";
        commandPanel.Padding = new Padding(8);
        commandPanel.Size = new Size(714, 42);
        commandPanel.TabIndex = 2;
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(620, 11);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(76, 30);
        cancelButton.TabIndex = 2;
        cancelButton.Text = "取消";
        // 
        // okButton
        // 
        okButton.ButtonType = ModernUI.WinForms.ModernButtonType.Primary;
        okButton.Location = new Point(539, 11);
        okButton.Name = "okButton";
        okButton.Size = new Size(76, 30);
        okButton.TabIndex = 1;
        okButton.Text = "确定";
        // 
        // applyButton
        // 
        applyButton.Location = new Point(458, 11);
        applyButton.Name = "applyButton";
        applyButton.Size = new Size(76, 30);
        applyButton.TabIndex = 0;
        applyButton.Text = "应用";
        // 
        // WorkflowNodeEditorDialog
        // 
        AcceptButton = okButton;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(720, 700);
        Controls.Add(rootLayout);
        MinimizeBox = false;
        MinimumSize = new Size(620, 480);
        Name = "WorkflowNodeEditorDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "节点信息";
        rootLayout.ResumeLayout(false);
        commandPanel.ResumeLayout(false);
        commandPanel.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel rootLayout = null!;
    private Panel workspacePanel = null!;
    private FlowLayoutPanel commandPanel = null!;
    private ModernUI.WinForms.ModernButton applyButton = null!;
    private ModernUI.WinForms.ModernButton okButton = null!;
    private ModernUI.WinForms.ModernButton cancelButton = null!;
}
