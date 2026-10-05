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

    /// <summary>初始化工具箱搜索框与分类树的静态外观；节点目录内容在运行时装入。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        searchHost = new Panel();
        searchInput = new ModernUI.WinForms.ModernInput();
        toolboxTreeView = new ModernUI.WinForms.ModernTreeView();
        searchHost.SuspendLayout();
        SuspendLayout();
        //
        // searchHost
        //
        searchHost.Controls.Add(searchInput);
        searchHost.Dock = DockStyle.Top;
        searchHost.Height = 46;
        searchHost.Name = "searchHost";
        searchHost.Padding = new Padding(8, 8, 8, 4);
        //
        // searchInput
        //
        searchInput.Dock = DockStyle.Fill;
        searchInput.Name = "searchInput";
        searchInput.PlaceholderText = "搜索节点";
        searchInput.TabIndex = 0;
        //
        // toolboxTreeView
        //
        toolboxTreeView.Dock = DockStyle.Fill;
        toolboxTreeView.FullRowSelect = true;
        toolboxTreeView.Name = "toolboxTreeView";
        toolboxTreeView.NodeHeight = 28;
        toolboxTreeView.ShowNodeToolTips = true;
        toolboxTreeView.Size = new Size(220, 574);
        toolboxTreeView.TabIndex = 1;
        //
        // WorkflowToolboxControl
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(toolboxTreeView);
        Controls.Add(searchHost);
        Name = "WorkflowToolboxControl";
        Size = new Size(220, 620);
        searchHost.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private Panel searchHost = null!;
    private ModernUI.WinForms.ModernInput searchInput = null!;
    private ModernUI.WinForms.ModernTreeView toolboxTreeView = null!;
}
