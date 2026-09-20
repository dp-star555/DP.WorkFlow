namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowBlockMappingEditorDialog
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    #region Windows 窗体设计器生成的代码

    /// <summary>初始化 Block 映射窗口的静态布局；运行时列由主代码动态添加。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        mappingTabs = new TabControl();
        inputTabPage = new TabPage();
        inputGrid = new DataGridView();
        outputTabPage = new TabPage();
        outputGrid = new DataGridView();
        commandPanel = new FlowLayoutPanel();
        cancelButton = new Button();
        okButton = new Button();
        mappingTabs.SuspendLayout();
        inputTabPage.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)inputGrid).BeginInit();
        outputTabPage.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)outputGrid).BeginInit();
        commandPanel.SuspendLayout();
        SuspendLayout();
        // 
        // mappingTabs
        // 
        mappingTabs.Controls.Add(inputTabPage);
        mappingTabs.Controls.Add(outputTabPage);
        mappingTabs.Dock = DockStyle.Fill;
        mappingTabs.Name = "mappingTabs";
        mappingTabs.SelectedIndex = 0;
        mappingTabs.Size = new Size(964, 537);
        mappingTabs.TabIndex = 0;
        // 
        // inputTabPage
        // 
        inputTabPage.Controls.Add(inputGrid);
        inputTabPage.Location = new Point(4, 26);
        inputTabPage.Name = "inputTabPage";
        inputTabPage.Padding = new Padding(3);
        inputTabPage.Size = new Size(956, 507);
        inputTabPage.TabIndex = 0;
        inputTabPage.Text = "输入映射";
        inputTabPage.UseVisualStyleBackColor = true;
        // 
        // inputGrid
        // 
        inputGrid.AllowUserToAddRows = true;
        inputGrid.AllowUserToDeleteRows = true;
        inputGrid.AutoGenerateColumns = false;
        inputGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        inputGrid.BackgroundColor = Color.FromArgb(15, 23, 42);
        inputGrid.Dock = DockStyle.Fill;
        inputGrid.GridColor = Color.FromArgb(51, 65, 85);
        inputGrid.Name = "inputGrid";
        inputGrid.RowHeadersVisible = false;
        inputGrid.Size = new Size(950, 501);
        inputGrid.TabIndex = 0;
        // 
        // outputTabPage
        // 
        outputTabPage.Controls.Add(outputGrid);
        outputTabPage.Location = new Point(4, 26);
        outputTabPage.Name = "outputTabPage";
        outputTabPage.Padding = new Padding(3);
        outputTabPage.Size = new Size(956, 507);
        outputTabPage.TabIndex = 1;
        outputTabPage.Text = "输出映射";
        outputTabPage.UseVisualStyleBackColor = true;
        // 
        // outputGrid
        // 
        outputGrid.AllowUserToAddRows = true;
        outputGrid.AllowUserToDeleteRows = true;
        outputGrid.AutoGenerateColumns = false;
        outputGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        outputGrid.BackgroundColor = Color.FromArgb(15, 23, 42);
        outputGrid.Dock = DockStyle.Fill;
        outputGrid.GridColor = Color.FromArgb(51, 65, 85);
        outputGrid.Name = "outputGrid";
        outputGrid.RowHeadersVisible = false;
        outputGrid.Size = new Size(950, 501);
        outputGrid.TabIndex = 0;
        // 
        // commandPanel
        // 
        commandPanel.Controls.Add(cancelButton);
        commandPanel.Controls.Add(okButton);
        commandPanel.Dock = DockStyle.Bottom;
        commandPanel.FlowDirection = FlowDirection.RightToLeft;
        commandPanel.Name = "commandPanel";
        commandPanel.Padding = new Padding(6);
        commandPanel.Size = new Size(964, 44);
        commandPanel.TabIndex = 1;
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(88, 28);
        cancelButton.TabIndex = 1;
        cancelButton.Text = "取消";
        cancelButton.UseVisualStyleBackColor = true;
        // 
        // okButton
        // 
        okButton.DialogResult = DialogResult.None;
        okButton.Name = "okButton";
        okButton.Size = new Size(88, 28);
        okButton.TabIndex = 0;
        okButton.Text = "确定";
        okButton.UseVisualStyleBackColor = true;
        // 
        // WorkflowBlockMappingEditorDialog
        // 
        AcceptButton = okButton;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(15, 23, 42);
        CancelButton = cancelButton;
        ClientSize = new Size(964, 581);
        Controls.Add(mappingTabs);
        Controls.Add(commandPanel);
        ForeColor = Color.FromArgb(226, 232, 240);
        MinimumSize = new Size(720, 460);
        Name = "WorkflowBlockMappingEditorDialog";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Block 映射";
        mappingTabs.ResumeLayout(false);
        inputTabPage.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)inputGrid).EndInit();
        outputTabPage.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)outputGrid).EndInit();
        commandPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TabControl mappingTabs = null!;
    private TabPage inputTabPage = null!;
    private TabPage outputTabPage = null!;
    private DataGridView inputGrid = null!;
    private DataGridView outputGrid = null!;
    private FlowLayoutPanel commandPanel = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;
}
