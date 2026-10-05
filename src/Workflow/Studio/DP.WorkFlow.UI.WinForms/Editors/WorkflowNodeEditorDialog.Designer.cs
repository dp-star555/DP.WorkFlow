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
    /// 初始化统一节点窗口的稳定外壳。右侧特殊内容由 WorkflowNodeEditorModel.Pages 在运行时装入。
    /// </summary>
    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        headerLayout = new TableLayoutPanel();
        nodeIdLabel = new Label();
        nodeIdTextBox = new ModernUI.WinForms.ModernInput();
        nodeTypeLabel = new Label();
        nodeTypeTextBox = new ModernUI.WinForms.ModernInput();
        titleLabel = new Label();
        titleTextBox = new ModernUI.WinForms.ModernInput();
        workspacePanel = new Panel();
        commandPanel = new FlowLayoutPanel();
        cancelButton = new ModernUI.WinForms.ModernButton();
        okButton = new ModernUI.WinForms.ModernButton();
        applyButton = new ModernUI.WinForms.ModernButton();
        rootLayout.SuspendLayout();
        headerLayout.SuspendLayout();
        commandPanel.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(headerLayout, 0, 0);
        rootLayout.Controls.Add(workspacePanel, 0, 1);
        rootLayout.Controls.Add(commandPanel, 0, 2);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        rootLayout.Size = new Size(720, 700);
        rootLayout.TabIndex = 0;
        // 
        // headerLayout
        // 
        headerLayout.ColumnCount = 2;
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.Controls.Add(nodeIdLabel, 0, 0);
        headerLayout.Controls.Add(nodeIdTextBox, 1, 0);
        headerLayout.Controls.Add(nodeTypeLabel, 0, 1);
        headerLayout.Controls.Add(nodeTypeTextBox, 1, 1);
        headerLayout.Controls.Add(titleLabel, 0, 2);
        headerLayout.Controls.Add(titleTextBox, 1, 2);
        headerLayout.Dock = DockStyle.Fill;
        headerLayout.Location = new Point(3, 3);
        headerLayout.Name = "headerLayout";
        headerLayout.Padding = new Padding(10);
        headerLayout.RowCount = 3;
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33333F));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33333F));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33333F));
        headerLayout.Size = new Size(714, 106);
        headerLayout.TabIndex = 0;
        // 
        // nodeIdLabel
        // 
        nodeIdLabel.Dock = DockStyle.Fill;
        nodeIdLabel.Location = new Point(13, 10);
        nodeIdLabel.Name = "nodeIdLabel";
        nodeIdLabel.Size = new Size(86, 28);
        nodeIdLabel.TabIndex = 0;
        nodeIdLabel.Text = "节点名称";
        nodeIdLabel.TextAlign = ContentAlignment.MiddleRight;
        // 
        // nodeIdTextBox
        // 
        nodeIdTextBox.Dock = DockStyle.Fill;
        nodeIdTextBox.Location = new Point(105, 13);
        nodeIdTextBox.Name = "nodeIdTextBox";
        nodeIdTextBox.ReadOnly = true;
        nodeIdTextBox.Size = new Size(596, 23);
        nodeIdTextBox.TabIndex = 0;
        // 
        // nodeTypeLabel
        // 
        nodeTypeLabel.Dock = DockStyle.Fill;
        nodeTypeLabel.Location = new Point(13, 38);
        nodeTypeLabel.Name = "nodeTypeLabel";
        nodeTypeLabel.Size = new Size(86, 28);
        nodeTypeLabel.TabIndex = 1;
        nodeTypeLabel.Text = "节点类型";
        nodeTypeLabel.TextAlign = ContentAlignment.MiddleRight;
        // 
        // nodeTypeTextBox
        // 
        nodeTypeTextBox.Dock = DockStyle.Fill;
        nodeTypeTextBox.Location = new Point(105, 41);
        nodeTypeTextBox.Name = "nodeTypeTextBox";
        nodeTypeTextBox.ReadOnly = true;
        nodeTypeTextBox.Size = new Size(596, 23);
        nodeTypeTextBox.TabIndex = 1;
        // 
        // titleLabel
        // 
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.Location = new Point(13, 66);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(86, 30);
        titleLabel.TabIndex = 2;
        titleLabel.Text = "标题";
        titleLabel.TextAlign = ContentAlignment.MiddleRight;
        // 
        // titleTextBox
        // 
        titleTextBox.Dock = DockStyle.Fill;
        titleTextBox.Location = new Point(105, 69);
        titleTextBox.Name = "titleTextBox";
        titleTextBox.Size = new Size(596, 23);
        titleTextBox.TabIndex = 2;
        // 
        // workspacePanel
        // 
        workspacePanel.Dock = DockStyle.Fill;
        workspacePanel.Location = new Point(3, 115);
        workspacePanel.Name = "workspacePanel";
        workspacePanel.Padding = new Padding(1);
        workspacePanel.Size = new Size(714, 534);
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
        headerLayout.ResumeLayout(false);
        headerLayout.PerformLayout();
        commandPanel.ResumeLayout(false);
        commandPanel.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel rootLayout = null!;
    private TableLayoutPanel headerLayout = null!;
    private Label nodeIdLabel = null!;
    private ModernUI.WinForms.ModernInput nodeIdTextBox = null!;
    private Label nodeTypeLabel = null!;
    private ModernUI.WinForms.ModernInput nodeTypeTextBox = null!;
    private Label titleLabel = null!;
    private ModernUI.WinForms.ModernInput titleTextBox = null!;
    private Panel workspacePanel = null!;
    private FlowLayoutPanel commandPanel = null!;
    private ModernUI.WinForms.ModernButton applyButton = null!;
    private ModernUI.WinForms.ModernButton okButton = null!;
    private ModernUI.WinForms.ModernButton cancelButton = null!;
}
