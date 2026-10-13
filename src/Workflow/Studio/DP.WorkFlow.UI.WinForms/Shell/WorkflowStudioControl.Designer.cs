namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowStudioControl
{
    private System.ComponentModel.IContainer components = null!;

    #region 组件设计器生成的代码

    /// <summary>初始化画布工作区（工具栏、工具箱、画布、诊断/监视）及工作台自己的“文件”菜单。</summary>
    private void InitializeComponent()
    {
        canvasWorkspace = new WorkflowCanvasWorkspaceControl();
        fileMenu = new ToolStripDropDownButton();
        fileSeparator = new ToolStripSeparator();
        propertyPanel = new WorkflowPropertyPanel();
        SuspendLayout();
        // 
        // fileMenu
        // 
        fileMenu.Name = "fileMenu";
        fileMenu.Size = new Size(45, 22);
        fileMenu.Text = "文件";
        // 
        // fileSeparator
        // 
        fileSeparator.Name = "fileSeparator";
        fileSeparator.Size = new Size(6, 25);
        // 
        // canvasWorkspace
        // 
        canvasWorkspace.Dock = DockStyle.Fill;
        canvasWorkspace.Location = new Point(0, 0);
        canvasWorkspace.Name = "canvasWorkspace";
        canvasWorkspace.Size = new Size(1180, 720);
        canvasWorkspace.TabIndex = 0;
        canvasWorkspace.Toolbar.Items.Insert(0, fileSeparator);
        canvasWorkspace.Toolbar.Items.Insert(0, fileMenu);
        // 
        // propertyPanel
        // 
        propertyPanel.BackColor = Color.FromArgb(15, 23, 42);
        propertyPanel.Dock = DockStyle.Fill;
        propertyPanel.ForeColor = Color.FromArgb(226, 232, 240);
        propertyPanel.HideScriptProperty = false;
        propertyPanel.Location = new Point(0, 0);
        propertyPanel.Name = "propertyPanel";
        propertyPanel.Session = null;
        propertyPanel.Size = new Size(390, 620);
        propertyPanel.EntryNodeId = null;
        propertyPanel.TabIndex = 0;
        // 
        // WorkflowStudioControl
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(15, 23, 42);
        Controls.Add(canvasWorkspace);
        Name = "WorkflowStudioControl";
        Size = new Size(1180, 720);
        ResumeLayout(false);
    }

    #endregion

    private WorkflowCanvasWorkspaceControl canvasWorkspace = null!;
    private ToolStripDropDownButton fileMenu = null!;
    private ToolStripSeparator fileSeparator = null!;
    private WorkflowPropertyPanel propertyPanel = null!;
}
