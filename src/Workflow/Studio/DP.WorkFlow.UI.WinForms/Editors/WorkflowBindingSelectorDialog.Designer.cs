namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowBindingSelectorDialog
{
    /// <summary>WinForms 设计器生成组件容器。</summary>
    private System.ComponentModel.IContainer components = null!;

    /// <summary>释放设计器组件。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    #region Windows 窗体设计器生成的代码

    /// <summary>
    /// 初始化静态界面结构。请优先使用 Visual Studio WinForms 设计器修改布局，
    /// 不要在业务代码中重复创建这些控件。
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        searchTextBox = new ModernUI.WinForms.ModernInput();
        bindingTreeView = new TreeView();
        statusLabel = new Label();
        commandPanel = new FlowLayoutPanel();
        cancelButton = new ModernUI.WinForms.ModernButton();
        okButton = new ModernUI.WinForms.ModernButton();
        commandPanel.SuspendLayout();
        SuspendLayout();
        // 
        // searchTextBox
        // 
        searchTextBox.Dock = DockStyle.Top;
        searchTextBox.Name = "searchTextBox";
        searchTextBox.PlaceholderText = "搜索节点、成员、路径或类型…";
        searchTextBox.Size = new Size(544, 32);
        searchTextBox.TabIndex = 0;
        // 
        // bindingTreeView
        // 
        bindingTreeView.Dock = DockStyle.Fill;
        bindingTreeView.HideSelection = false;
        bindingTreeView.Name = "bindingTreeView";
        bindingTreeView.Size = new Size(544, 523);
        bindingTreeView.TabIndex = 1;
        // 
        // statusLabel
        // 
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(544, 24);
        statusLabel.TabIndex = 2;
        statusLabel.Text = " 0 个兼容候选";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // commandPanel
        // 
        commandPanel.Controls.Add(cancelButton);
        commandPanel.Controls.Add(okButton);
        commandPanel.Dock = DockStyle.Bottom;
        commandPanel.FlowDirection = FlowDirection.RightToLeft;
        commandPanel.Name = "commandPanel";
        commandPanel.Padding = new Padding(4);
        commandPanel.Size = new Size(544, 42);
        commandPanel.TabIndex = 3;
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(88, 30);
        cancelButton.TabIndex = 1;
        cancelButton.Text = "取消";
        // 
        // okButton
        // 
        okButton.ButtonType = ModernUI.WinForms.ModernButtonType.Primary;
        okButton.Name = "okButton";
        okButton.Size = new Size(88, 30);
        okButton.TabIndex = 0;
        okButton.Text = "确定";
        // 
        // WorkflowBindingSelectorDialog
        // 
        AcceptButton = okButton;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(544, 612);
        Controls.Add(bindingTreeView);
        Controls.Add(statusLabel);
        Controls.Add(searchTextBox);
        Controls.Add(commandPanel);
        MinimumSize = new Size(420, 420);
        Name = "WorkflowBindingSelectorDialog";
        StartPosition = FormStartPosition.CenterParent;
        Text = "选择工作流绑定";
        commandPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private ModernUI.WinForms.ModernInput searchTextBox = null!;
    private TreeView bindingTreeView = null!;
    private Label statusLabel = null!;
    private FlowLayoutPanel commandPanel = null!;
    private ModernUI.WinForms.ModernButton okButton = null!;
    private ModernUI.WinForms.ModernButton cancelButton = null!;
}
