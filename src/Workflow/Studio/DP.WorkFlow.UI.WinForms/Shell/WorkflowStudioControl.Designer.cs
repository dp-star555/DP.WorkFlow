namespace DP.WorkFlow.UI.WinForms;

partial class WorkflowStudioControl
{
    private System.ComponentModel.IContainer components = null!;

    #region 组件设计器生成的代码

    /// <summary>初始化工作台工具栏、左右分栏、画布区及底部诊断/监视页。</summary>
    private void InitializeComponent()
    {
        _toolbar = new ModernUI.WinForms.ModernToolStrip();
        fileMenu = new ToolStripDropDownButton();
        fileSeparator = new ToolStripSeparator();
        _undoButton = new ToolStripButton();
        _redoButton = new ToolStripButton();
        editSeparator = new ToolStripSeparator();
        _breadcrumbLabel = new ToolStripLabel();
        navigationSeparator = new ToolStripSeparator();
        fitButton = new ToolStripButton();
        layoutMenu = new ToolStripDropDownButton();
        runtimeSeparator = new ToolStripSeparator();
        _runButton = new ToolStripButton();
        _pauseButton = new ToolStripButton();
        _resumeButton = new ToolStripButton();
        _stopButton = new ToolStripButton();
        _runtimeLabel = new ToolStripLabel();
        toolboxAndEditor = new ModernUI.WinForms.ModernSplitter();
        toolboxControl = new WorkflowToolboxControl();
        canvasAndDiagnostics = new ModernUI.WinForms.ModernSplitter();
        designerControl = new WorkflowDesignerControl();
        bottomTabs = new ModernUI.WinForms.ModernTabControl();
        diagnosticsPage = new TabPage();
        diagnosticsControl = new WorkflowDiagnosticsControl();
        runtimePage = new TabPage();
        runtimeMonitorControl = new WorkflowRuntimeMonitorControl();
        propertyPanel = new WorkflowPropertyPanel();
        _toolbar.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)toolboxAndEditor).BeginInit();
        toolboxAndEditor.Panel1.SuspendLayout();
        toolboxAndEditor.Panel2.SuspendLayout();
        toolboxAndEditor.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)canvasAndDiagnostics).BeginInit();
        canvasAndDiagnostics.Panel1.SuspendLayout();
        canvasAndDiagnostics.Panel2.SuspendLayout();
        canvasAndDiagnostics.SuspendLayout();
        diagnosticsPage.SuspendLayout();
        runtimePage.SuspendLayout();
        SuspendLayout();
        // 
        // _toolbar
        // 
        _toolbar.BackColor = Color.FromArgb(22, 32, 49);
        _toolbar.ForeColor = Color.FromArgb(226, 232, 240);
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Items.AddRange(new ToolStripItem[] { fileMenu, fileSeparator, _undoButton, _redoButton, editSeparator, _breadcrumbLabel, navigationSeparator, fitButton, layoutMenu, runtimeSeparator, _runButton, _pauseButton, _resumeButton, _stopButton, _runtimeLabel });
        _toolbar.Location = new Point(0, 0);
        _toolbar.Name = "_toolbar";
        _toolbar.Size = new Size(1180, 25);
        _toolbar.TabIndex = 1;
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
        // _undoButton
        // 
        _undoButton.Name = "_undoButton";
        _undoButton.Size = new Size(36, 22);
        _undoButton.Text = "撤销";
        // 
        // _redoButton
        // 
        _redoButton.Name = "_redoButton";
        _redoButton.Size = new Size(36, 22);
        _redoButton.Text = "重做";
        // 
        // editSeparator
        // 
        editSeparator.Name = "editSeparator";
        editSeparator.Size = new Size(6, 25);
        // 
        // _breadcrumbLabel
        // 
        _breadcrumbLabel.ForeColor = Color.FromArgb(148, 163, 184);
        _breadcrumbLabel.Name = "_breadcrumbLabel";
        _breadcrumbLabel.Size = new Size(36, 22);
        _breadcrumbLabel.Text = "Root";
        // 
        // navigationSeparator
        // 
        navigationSeparator.Name = "navigationSeparator";
        navigationSeparator.Size = new Size(6, 25);
        // 
        // fitButton
        // 
        fitButton.Name = "fitButton";
        fitButton.Size = new Size(60, 22);
        fitButton.Text = "适合画布";
        // 
        // layoutMenu
        // 
        layoutMenu.Name = "layoutMenu";
        layoutMenu.Size = new Size(45, 22);
        layoutMenu.Text = "布局";
        // 
        // runtimeSeparator
        // 
        runtimeSeparator.Name = "runtimeSeparator";
        runtimeSeparator.Size = new Size(6, 25);
        // 
        // _runButton
        // 
        _runButton.Name = "_runButton";
        _runButton.Size = new Size(36, 22);
        _runButton.Text = "运行";
        // 
        // _pauseButton
        // 
        _pauseButton.Name = "_pauseButton";
        _pauseButton.Size = new Size(36, 22);
        _pauseButton.Text = "暂停";
        // 
        // _resumeButton
        // 
        _resumeButton.Name = "_resumeButton";
        _resumeButton.Size = new Size(36, 22);
        _resumeButton.Text = "继续";
        // 
        // _stopButton
        // 
        _stopButton.Name = "_stopButton";
        _stopButton.Size = new Size(36, 22);
        _stopButton.Text = "停止";
        // 
        // _runtimeLabel
        // 
        _runtimeLabel.ForeColor = Color.FromArgb(148, 163, 184);
        _runtimeLabel.Name = "_runtimeLabel";
        _runtimeLabel.Size = new Size(30, 22);
        _runtimeLabel.Text = "Idle";
        // 
        // toolboxAndEditor
        // 
        toolboxAndEditor.Dock = DockStyle.Fill;
        toolboxAndEditor.FixedPanel = FixedPanel.Panel1;
        toolboxAndEditor.InitialPanel2Size = 0;
        toolboxAndEditor.Location = new Point(0, 25);
        toolboxAndEditor.Name = "toolboxAndEditor";
        // 
        // toolboxAndEditor.Panel1
        // 
        toolboxAndEditor.Panel1.Controls.Add(toolboxControl);
        // 
        // toolboxAndEditor.Panel2
        // 
        toolboxAndEditor.Panel2.Controls.Add(canvasAndDiagnostics);
        toolboxAndEditor.Size = new Size(1180, 695);
        toolboxAndEditor.SplitterDistance = 259;
        toolboxAndEditor.TabIndex = 0;
        // 
        // toolboxControl
        // 
        toolboxControl.Dock = DockStyle.Fill;
        toolboxControl.Location = new Point(0, 0);
        toolboxControl.Name = "toolboxControl";
        toolboxControl.Session = null;
        toolboxControl.Size = new Size(259, 695);
        toolboxControl.TabIndex = 0;
        // 
        // canvasAndDiagnostics
        //
        canvasAndDiagnostics.Dock = DockStyle.Fill;
        canvasAndDiagnostics.InitialPanel2Size = 220;
        canvasAndDiagnostics.Location = new Point(0, 0);
        canvasAndDiagnostics.Name = "canvasAndDiagnostics";
        canvasAndDiagnostics.Orientation = Orientation.Horizontal;
        canvasAndDiagnostics.Panel1.Controls.Add(designerControl);
        canvasAndDiagnostics.Panel2.Controls.Add(bottomTabs);
        canvasAndDiagnostics.Size = new Size(916, 695);
        canvasAndDiagnostics.SplitterDistance = 475;
        canvasAndDiagnostics.TabIndex = 0;
        //
        // designerControl
        // 
        designerControl.AllowDrop = true;
        designerControl.BackColor = Color.FromArgb(30, 30, 30);
        designerControl.Dock = DockStyle.Fill;
        designerControl.ForeColor = Color.FromArgb(226, 232, 240);
        designerControl.Location = new Point(0, 0);
        designerControl.Name = "designerControl";
        designerControl.Session = null;
        designerControl.Size = new Size(916, 535);
        designerControl.TabIndex = 0;
        //
        // bottomTabs
        //
        bottomTabs.TabPages.Add(diagnosticsPage);
        bottomTabs.TabPages.Add(runtimePage);
        bottomTabs.Dock = DockStyle.Fill;
        bottomTabs.Location = new Point(0, 0);
        bottomTabs.Name = "bottomTabs";
        bottomTabs.SelectedIndex = 0;
        bottomTabs.Size = new Size(916, 220);
        bottomTabs.TabIndex = 0;
        //
        // diagnosticsPage
        //
        diagnosticsPage.Controls.Add(diagnosticsControl);
        diagnosticsPage.Name = "diagnosticsPage";
        diagnosticsPage.Padding = new Padding(6);
        diagnosticsPage.Text = "诊断";
        diagnosticsControl.Dock = DockStyle.Fill;
        diagnosticsControl.Name = "diagnosticsControl";
        //
        // runtimePage
        //
        runtimePage.Controls.Add(runtimeMonitorControl);
        runtimePage.Name = "runtimePage";
        runtimePage.Padding = new Padding(6);
        runtimePage.Text = "运行监视";
        runtimeMonitorControl.Dock = DockStyle.Fill;
        runtimeMonitorControl.Name = "runtimeMonitorControl";
        // 
        // propertyPanel
        // 
        propertyPanel.BackColor = Color.FromArgb(15, 23, 42);
        propertyPanel.Dock = DockStyle.Fill;
        propertyPanel.ForeColor = Color.FromArgb(226, 232, 240);
        propertyPanel.HideScriptProperty = false;
        propertyPanel.HideSpecialActions = false;
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
        Controls.Add(toolboxAndEditor);
        Controls.Add(_toolbar);
        Name = "WorkflowStudioControl";
        Size = new Size(1180, 720);
        _toolbar.ResumeLayout(false);
        _toolbar.PerformLayout();
        toolboxAndEditor.Panel1.ResumeLayout(false);
        toolboxAndEditor.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)toolboxAndEditor).EndInit();
        toolboxAndEditor.ResumeLayout(false);
        canvasAndDiagnostics.Panel1.ResumeLayout(false);
        canvasAndDiagnostics.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)canvasAndDiagnostics).EndInit();
        canvasAndDiagnostics.ResumeLayout(false);
        diagnosticsPage.ResumeLayout(false);
        runtimePage.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private ModernUI.WinForms.ModernToolStrip _toolbar = null!;
    private ToolStripDropDownButton fileMenu = null!;
    private ToolStripSeparator fileSeparator = null!;
    private ToolStripButton _undoButton = null!;
    private ToolStripButton _redoButton = null!;
    private ToolStripSeparator editSeparator = null!;
    private ToolStripLabel _breadcrumbLabel = null!;
    private ToolStripSeparator navigationSeparator = null!;
    private ToolStripButton fitButton = null!;
    private ToolStripDropDownButton layoutMenu = null!;
    private ToolStripSeparator runtimeSeparator = null!;
    private ToolStripButton _runButton = null!;
    private ToolStripButton _pauseButton = null!;
    private ToolStripButton _resumeButton = null!;
    private ToolStripButton _stopButton = null!;
    private ToolStripLabel _runtimeLabel = null!;
    private ModernUI.WinForms.ModernSplitter toolboxAndEditor = null!;
    private WorkflowToolboxControl toolboxControl = null!;
    private ModernUI.WinForms.ModernSplitter canvasAndDiagnostics = null!;
    private WorkflowDesignerControl designerControl = null!;
    private ModernUI.WinForms.ModernTabControl bottomTabs = null!;
    private TabPage diagnosticsPage = null!;
    private WorkflowDiagnosticsControl diagnosticsControl = null!;
    private TabPage runtimePage = null!;
    private WorkflowRuntimeMonitorControl runtimeMonitorControl = null!;
    private WorkflowPropertyPanel propertyPanel = null!;
}
