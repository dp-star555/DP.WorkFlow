namespace DP.WorkFlow.UI.WinForms;

partial class CSharpUsingManagerDialog
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    #region Windows 窗体设计器生成的代码

    /// <summary>初始化 using 输入、列表、整理命令和确认按钮布局。</summary>
    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        instructionLabel = new Label();
        inputLayout = new TableLayoutPanel();
        _input = new ModernUI.WinForms.ModernComboBox();
        addButton = new Button();
        _imports = new ListBox();
        commandPanel = new FlowLayoutPanel();
        removeButton = new Button();
        sortButton = new Button();
        defaultsButton = new Button();
        hintLabel = new Label();
        dialogButtons = new FlowLayoutPanel();
        cancelButton = new Button();
        okButton = new Button();
        rootLayout.SuspendLayout();
        inputLayout.SuspendLayout();
        commandPanel.SuspendLayout();
        dialogButtons.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(instructionLabel, 0, 0);
        rootLayout.Controls.Add(inputLayout, 0, 1);
        rootLayout.Controls.Add(_imports, 0, 2);
        rootLayout.Controls.Add(commandPanel, 0, 3);
        rootLayout.Controls.Add(hintLabel, 0, 4);
        rootLayout.Controls.Add(dialogButtons, 0, 5);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(10);
        rootLayout.RowCount = 6;
        rootLayout.RowStyles.Add(new RowStyle());
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        rootLayout.RowStyles.Add(new RowStyle());
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        rootLayout.Size = new Size(560, 440);
        rootLayout.TabIndex = 0;
        // 
        // instructionLabel
        // 
        instructionLabel.AutoSize = true;
        instructionLabel.Location = new Point(13, 10);
        instructionLabel.Name = "instructionLabel";
        instructionLabel.Size = new Size(359, 17);
        instructionLabel.TabIndex = 0;
        instructionLabel.Text = "输入命名空间后按 Enter；列表支持多选，Delete 或双击可删除。";
        // 
        // inputLayout
        // 
        inputLayout.ColumnCount = 2;
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        inputLayout.ColumnStyles.Add(new ColumnStyle());
        inputLayout.Controls.Add(_input, 0, 0);
        inputLayout.Controls.Add(addButton, 1, 0);
        inputLayout.Dock = DockStyle.Fill;
        inputLayout.Location = new Point(13, 30);
        inputLayout.Name = "inputLayout";
        inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        inputLayout.Size = new Size(534, 28);
        inputLayout.TabIndex = 1;
        // 
        // _input
        // 
        _input.Dock = DockStyle.Fill;
        _input.Location = new Point(3, 3);
        _input.Name = "_input";
        _input.Size = new Size(447, 25);
        _input.TabIndex = 0;
        // 
        // addButton
        // 
        addButton.AutoSize = true;
        addButton.Location = new Point(456, 3);
        addButton.Name = "addButton";
        addButton.Size = new Size(75, 22);
        addButton.TabIndex = 1;
        addButton.Text = "添加";
        // 
        // _imports
        // 
        _imports.Dock = DockStyle.Fill;
        _imports.IntegralHeight = false;
        _imports.ItemHeight = 17;
        _imports.Location = new Point(13, 64);
        _imports.Name = "_imports";
        _imports.SelectionMode = SelectionMode.MultiExtended;
        _imports.Size = new Size(534, 268);
        _imports.TabIndex = 2;
        // 
        // commandPanel
        // 
        commandPanel.Controls.Add(removeButton);
        commandPanel.Controls.Add(sortButton);
        commandPanel.Controls.Add(defaultsButton);
        commandPanel.Dock = DockStyle.Fill;
        commandPanel.Location = new Point(13, 338);
        commandPanel.Name = "commandPanel";
        commandPanel.Size = new Size(534, 32);
        commandPanel.TabIndex = 3;
        commandPanel.WrapContents = false;
        // 
        // removeButton
        // 
        removeButton.AutoSize = true;
        removeButton.Location = new Point(3, 3);
        removeButton.Name = "removeButton";
        removeButton.Size = new Size(75, 27);
        removeButton.TabIndex = 0;
        removeButton.Text = "删除所选";
        // 
        // sortButton
        // 
        sortButton.AutoSize = true;
        sortButton.Location = new Point(84, 3);
        sortButton.Name = "sortButton";
        sortButton.Size = new Size(78, 27);
        sortButton.TabIndex = 1;
        sortButton.Text = "排序并去重";
        // 
        // defaultsButton
        // 
        defaultsButton.AutoSize = true;
        defaultsButton.Location = new Point(168, 3);
        defaultsButton.Name = "defaultsButton";
        defaultsButton.Size = new Size(101, 27);
        defaultsButton.TabIndex = 2;
        defaultsButton.Text = "添加常用 using";
        // 
        // hintLabel
        // 
        hintLabel.AutoSize = true;
        hintLabel.ForeColor = Color.Gray;
        hintLabel.Location = new Point(13, 373);
        hintLabel.Name = "hintLabel";
        hintLabel.Size = new Size(347, 17);
        hintLabel.TabIndex = 4;
        hintLabel.Text = "只填写命名空间，例如 System.Text；不要填写 using 和分号。";
        // 
        // dialogButtons
        // 
        dialogButtons.Controls.Add(cancelButton);
        dialogButtons.Controls.Add(okButton);
        dialogButtons.Dock = DockStyle.Fill;
        dialogButtons.FlowDirection = FlowDirection.RightToLeft;
        dialogButtons.Location = new Point(13, 393);
        dialogButtons.Name = "dialogButtons";
        dialogButtons.Size = new Size(534, 34);
        dialogButtons.TabIndex = 5;
        // 
        // cancelButton
        // 
        cancelButton.AutoSize = true;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(456, 3);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(75, 27);
        cancelButton.TabIndex = 0;
        cancelButton.Text = "取消";
        // 
        // okButton
        // 
        okButton.AutoSize = true;
        okButton.DialogResult = DialogResult.OK;
        okButton.Location = new Point(375, 3);
        okButton.Name = "okButton";
        okButton.Size = new Size(75, 27);
        okButton.TabIndex = 1;
        okButton.Text = "应用";
        // 
        // CSharpUsingManagerDialog
        // 
        AcceptButton = okButton;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(560, 440);
        Controls.Add(rootLayout);
        MinimumSize = new Size(460, 340);
        Name = "CSharpUsingManagerDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "using 管理";
        rootLayout.ResumeLayout(false);
        rootLayout.PerformLayout();
        inputLayout.ResumeLayout(false);
        inputLayout.PerformLayout();
        commandPanel.ResumeLayout(false);
        commandPanel.PerformLayout();
        dialogButtons.ResumeLayout(false);
        dialogButtons.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel rootLayout = null!;
    private Label instructionLabel = null!;
    private TableLayoutPanel inputLayout = null!;
    private ModernUI.WinForms.ModernComboBox _input = null!;
    private Button addButton = null!;
    private ListBox _imports = null!;
    private FlowLayoutPanel commandPanel = null!;
    private Button removeButton = null!;
    private Button sortButton = null!;
    private Button defaultsButton = null!;
    private Label hintLabel = null!;
    private FlowLayoutPanel dialogButtons = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;
}
