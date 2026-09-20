namespace WinFormsApp_test;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeRuntimeResources();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        workflowStudioControl1 = new DP.WorkFlow.UI.WinForms.WorkflowStudioControl();
        SuspendLayout();
        // 
        // workflowStudioControl1
        // 
        workflowStudioControl1.BackColor = Color.FromArgb(15, 23, 42);
        workflowStudioControl1.ConfirmDiscardChanges = null;
        workflowStudioControl1.Dock = DockStyle.Fill;
        workflowStudioControl1.ForeColor = Color.FromArgb(241, 241, 241);
        workflowStudioControl1.Location = new Point(0, 0);
        workflowStudioControl1.Name = "workflowStudioControl1";
        workflowStudioControl1.Navigator = null;
        workflowStudioControl1.RuntimeBinding = null;
        workflowStudioControl1.Session = null;
        workflowStudioControl1.Size = new Size(1021, 623);
        workflowStudioControl1.EntryNodeId = null;
        workflowStudioControl1.TabIndex = 0;
        workflowStudioControl1.Workspace = null;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1021, 623);
        Controls.Add(workflowStudioControl1);
        Name = "Form1";
        Text = "Form1";
        ResumeLayout(false);
    }

    #endregion

    private DP.WorkFlow.UI.WinForms.WorkflowStudioControl workflowStudioControl1;
}
