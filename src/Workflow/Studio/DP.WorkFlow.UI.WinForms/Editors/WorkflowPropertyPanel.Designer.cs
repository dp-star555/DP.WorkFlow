namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowPropertyPanel
{
    private System.ComponentModel.IContainer components = null!;

    /// <summary>释放参数模型以及设计器组件。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeModel();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region 组件设计器生成的代码

    /// <summary>
    /// 初始化参数面板外壳。参数行由 ModernPropertyGrid 按节点属性在运行时动态创建。
    /// </summary>
    private void InitializeComponent()
    {
        SuspendLayout();
        // 
        // WorkflowPropertyPanel
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.FromArgb(226, 232, 240);
        Name = "WorkflowPropertyPanel";
        Size = new Size(390, 620);
        ResumeLayout(false);
    }

    #endregion
}
