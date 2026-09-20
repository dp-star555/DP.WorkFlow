namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowToolboxControl
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    #region 组件设计器生成的代码

    /// <summary>初始化工具箱树的静态外观；节点目录内容在运行时装入。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        toolboxTreeView = new TreeView();
        SuspendLayout();
        // 
        // toolboxTreeView
        // 
        toolboxTreeView.BackColor = Color.FromArgb(15, 23, 42);
        toolboxTreeView.BorderStyle = BorderStyle.None;
        toolboxTreeView.Dock = DockStyle.Fill;
        toolboxTreeView.ForeColor = Color.FromArgb(226, 232, 240);
        toolboxTreeView.FullRowSelect = true;
        toolboxTreeView.HideSelection = false;
        toolboxTreeView.ItemHeight = 24;
        toolboxTreeView.Name = "toolboxTreeView";
        toolboxTreeView.ShowLines = true;
        toolboxTreeView.ShowNodeToolTips = true;
        toolboxTreeView.ShowPlusMinus = true;
        toolboxTreeView.ShowRootLines = true;
        toolboxTreeView.Size = new Size(220, 620);
        toolboxTreeView.TabIndex = 0;
        // 
        // WorkflowToolboxControl
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(15, 23, 42);
        Controls.Add(toolboxTreeView);
        ForeColor = Color.FromArgb(226, 232, 240);
        Name = "WorkflowToolboxControl";
        Size = new Size(220, 620);
        ResumeLayout(false);
    }

    #endregion

    private TreeView toolboxTreeView = null!;
}
