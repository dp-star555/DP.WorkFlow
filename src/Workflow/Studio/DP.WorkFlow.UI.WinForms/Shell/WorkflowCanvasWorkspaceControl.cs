using DP.WorkFlow.UI;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 画布工作区：工具栏（撤销、重做、适合画布、布局、运行/暂停/继续/停止）、工具箱、画布，以及底部“诊断 | 运行监视”。
/// 工作台主画布和节点窗口的子画布共用这一个控件；两者的差别只在运行命令（<see cref="RunCommands"/>）、
/// 监视来源（<see cref="WorkflowRuntimeMonitorControl.Source"/>）和宿主在工具栏前端加入的条目（例如“文件”菜单）。
/// </summary>
public sealed class WorkflowCanvasWorkspaceControl : UserControl
{
    private readonly ModernToolStrip _toolbar = new();
    private readonly ToolStripButton _undoButton = new() { Name = "_undoButton", Text = "撤销" };
    private readonly ToolStripButton _redoButton = new() { Name = "_redoButton", Text = "重做" };
    private readonly ToolStripSeparator _editSeparator = new();
    private readonly ToolStripLabel _breadcrumbLabel = new() { Name = "_breadcrumbLabel", ForeColor = Color.FromArgb(148, 163, 184) };
    private readonly ToolStripSeparator _navigationSeparator = new();
    private readonly ToolStripButton _fitButton = new() { Name = "fitButton", Text = "适合画布" };
    private readonly ToolStripDropDownButton _layoutMenu = new() { Name = "layoutMenu", Text = "布局" };
    private readonly ToolStripSeparator _runtimeSeparator = new();
    private readonly ToolStripButton _runButton = new() { Name = "_runButton", Text = "运行" };
    private readonly ToolStripButton _pauseButton = new() { Name = "_pauseButton", Text = "暂停" };
    private readonly ToolStripButton _resumeButton = new() { Name = "_resumeButton", Text = "继续" };
    private readonly ToolStripButton _stopButton = new() { Name = "_stopButton", Text = "停止" };
    private readonly ToolStripLabel _runtimeLabel = new() { Name = "_runtimeLabel", ForeColor = Color.FromArgb(148, 163, 184), Text = "Idle" };
    private readonly ModernSplitter _toolboxAndEditor = new();
    private readonly ModernSplitter _canvasAndBottom = new();
    private readonly ModernTabControl _bottomTabs = new();
    private readonly TabPage _diagnosticsPage = new() { Name = "diagnosticsPage", Text = "诊断", Padding = new Padding(6) };
    private readonly TabPage _runtimePage = new() { Name = "runtimePage", Text = "运行监视", Padding = new Padding(6) };
    private WorkflowDesignerSession? _session;
    private IWorkflowCanvasRunCommands? _runCommands;
    private bool _autoFitPending;
    private bool _autoFitQueued;

    /// <summary>创建画布工作区。</summary>
    public WorkflowCanvasWorkspaceControl()
    {
        SuspendLayout();
        BackColor = Color.FromArgb(15, 23, 42);
        _toolbar.BackColor = Color.FromArgb(22, 32, 49);
        _toolbar.ForeColor = Color.FromArgb(226, 232, 240);
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            _undoButton, _redoButton, _editSeparator, _breadcrumbLabel, _navigationSeparator, _fitButton, _layoutMenu,
            _runtimeSeparator, _runButton, _pauseButton, _resumeButton, _stopButton, _runtimeLabel
        });
        BreadcrumbText = null;

        Designer = new WorkflowDesignerControl
        {
            AllowDrop = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(226, 232, 240),
            Dock = DockStyle.Fill
        };
        Toolbox = new WorkflowToolboxControl { Dock = DockStyle.Fill };
        Diagnostics = new WorkflowDiagnosticsControl { Dock = DockStyle.Fill };
        RuntimeMonitor = new WorkflowRuntimeMonitorControl { Dock = DockStyle.Fill };
        _diagnosticsPage.Controls.Add(Diagnostics);
        _runtimePage.Controls.Add(RuntimeMonitor);
        _bottomTabs.TabPages.Add(_diagnosticsPage);
        _bottomTabs.TabPages.Add(_runtimePage);
        _bottomTabs.Dock = DockStyle.Fill;

        // 先给出足够尺寸，避免默认 150px 宽度下设置最小尺寸、分隔位置越界。
        _canvasAndBottom.Size = new Size(916, 695);
        _canvasAndBottom.Dock = DockStyle.Fill;
        _canvasAndBottom.Orientation = Orientation.Horizontal;
        _canvasAndBottom.InitialPanel2Size = 220;
        _canvasAndBottom.SplitterDistance = 475;
        _canvasAndBottom.Panel1.Controls.Add(Designer);
        _canvasAndBottom.Panel2.Controls.Add(_bottomTabs);

        _toolboxAndEditor.Size = new Size(1180, 695);
        _toolboxAndEditor.Dock = DockStyle.Fill;
        _toolboxAndEditor.FixedPanel = FixedPanel.Panel1;
        _toolboxAndEditor.InitialPanel2Size = 0;
        _toolboxAndEditor.SplitterDistance = 210;
        _toolboxAndEditor.Panel1.Controls.Add(Toolbox);
        _toolboxAndEditor.Panel2.Controls.Add(_canvasAndBottom);

        Controls.Add(_toolboxAndEditor);
        Controls.Add(_toolbar);
        Size = new Size(1180, 720);
        ResumeLayout(false);

        AddMenuItem(_layoutMenu, "自动布局", ModernIconKind.AutoLayout, () => Session?.AutoLayout());
        AddMenuItem(_layoutMenu, "左对齐", ModernIconKind.AlignLeft, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Left));
        AddMenuItem(_layoutMenu, "右对齐", ModernIconKind.AlignRight, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Right));
        AddMenuItem(_layoutMenu, "顶端对齐", ModernIconKind.AlignTop, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Top));
        AddMenuItem(_layoutMenu, "底端对齐", ModernIconKind.AlignBottom, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Bottom));
        AddMenuItem(_layoutMenu, "水平分布", ModernIconKind.DistributeHorizontal, () => Session?.DistributeSelectedNodes(WorkflowNodeDistribution.Horizontal));
        AddMenuItem(_layoutMenu, "垂直分布", ModernIconKind.DistributeVertical, () => Session?.DistributeSelectedNodes(WorkflowNodeDistribution.Vertical));
        var theme = ModernTheme.Dark;
        SetIconOnly(_undoButton, ModernIconKind.Undo, theme.Text, "撤销 (Ctrl+Z)");
        SetIconOnly(_redoButton, ModernIconKind.Redo, theme.Text, "重做 (Ctrl+Y)");
        SetIconOnly(_fitButton, ModernIconKind.FitWindow, theme.Text, "适合画布");
        SetIconOnly(_runButton, ModernIconKind.Play, theme.Success, "运行");
        SetIconOnly(_pauseButton, ModernIconKind.Pause, theme.Warning, "暂停");
        SetIconOnly(_resumeButton, ModernIconKind.Resume, theme.Success, "继续");
        SetIconOnly(_stopButton, ModernIconKind.Stop, theme.Error, "停止");

        _undoButton.Click += (_, _) => Session?.Undo();
        _redoButton.Click += (_, _) => Session?.Redo();
        _fitButton.Click += (_, _) => Session?.FitToView(Designer.ClientSize.Width, Designer.ClientSize.Height);
        _runButton.Click += async (_, _) => await RunAsync();
        _pauseButton.Click += (_, _) => RunCommands?.Pause();
        _resumeButton.Click += (_, _) => RunCommands?.Resume();
        _stopButton.Click += (_, _) => RunCommands?.Cancel();
        Toolbox.NodeTypeActivated += OnNodeTypeActivated;
        Designer.InteractionError += (_, message) => InteractionError?.Invoke(this, message);
        Designer.SizeChanged += (_, _) => QueueAutoFit();
        Load += (_, _) =>
        {
            if (_toolboxAndEditor.Width > 500)
                _toolboxAndEditor.SplitterDistance = Math.Min(210, _toolboxAndEditor.Width - 300);
            QueueAutoFit();
        };
        UpdateCommands();
        WorkflowWinFormsStyle.Apply(this);
    }

    /// <summary>画布。</summary>
    public WorkflowDesignerControl Designer { get; }

    /// <summary>工具箱。</summary>
    public WorkflowToolboxControl Toolbox { get; }

    /// <summary>诊断面板。</summary>
    public WorkflowDiagnosticsControl Diagnostics { get; }

    /// <summary>运行监视。</summary>
    public WorkflowRuntimeMonitorControl RuntimeMonitor { get; }

    /// <summary>工具栏；宿主可在最前面插入自己的条目（例如“文件”菜单）。</summary>
    public ModernToolStrip Toolbar => _toolbar;

    /// <summary>工具栏上的路径文字；为空时不显示路径。</summary>
    public string? BreadcrumbText
    {
        get => _breadcrumbLabel.Visible ? _breadcrumbLabel.Text : null;
        set
        {
            var visible = !string.IsNullOrEmpty(value);
            _breadcrumbLabel.Text = value ?? string.Empty;
            _breadcrumbLabel.Visible = visible;
            _navigationSeparator.Visible = visible;
        }
    }

    /// <summary>画布会话；同时用于工具箱和诊断。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            if (_session is not null) _session.Changed -= OnSessionChanged;
            _session = value;
            if (_session is not null) _session.Changed += OnSessionChanged;
            Designer.Session = value;
            Toolbox.Session = value;
            Diagnostics.Session = value;
            _autoFitPending = value is not null;
            QueueAutoFit();
            UpdateCommands();
        }
    }

    /// <summary>运行命令；为空时运行按钮不可用。</summary>
    public IWorkflowCanvasRunCommands? RunCommands
    {
        get => _runCommands;
        set
        {
            if (ReferenceEquals(_runCommands, value)) return;
            if (_runCommands is not null) _runCommands.Changed -= OnRunCommandsChanged;
            _runCommands = value;
            if (_runCommands is not null) _runCommands.Changed += OnRunCommandsChanged;
            UpdateRuntimeCommands();
        }
    }

    /// <summary>画布交互或运行入口报错时发生。</summary>
    public event EventHandler<string>? InteractionError;

    /// <summary>运行以故障结束时发生（故障详情已写入运行监视）。</summary>
    public event EventHandler<string>? RunFaulted;

    /// <summary>运行前检查诊断，再执行运行命令；故障时切到运行监视。</summary>
    public async Task RunAsync()
    {
        if (RunCommands is not { } commands) return;
        Diagnostics.RefreshDiagnostics();
        if (!Diagnostics.CanRun)
        {
            InteractionError?.Invoke(this, "当前流程存在配置或编译错误，不能运行。");
            return;
        }
        try
        {
            var result = await commands.RunAsync();
            if (!result.Success && result.State == E_WorkflowExecutionState.Faulted)
            {
                // 运行失败只记录：失败节点标红、故障写入运行监视轨迹，切到运行监视查看，不弹窗。
                _bottomTabs.SelectedTab = _runtimePage;
                RunFaulted?.Invoke(this, $"流程运行失败：{result.Message}");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException
                                          or NotSupportedException or WorkflowCompilationException)
        {
            InteractionError?.Invoke(this, exception.Message);
        }
    }

    /// <summary>切到运行监视页。</summary>
    public void ShowRuntimeMonitor() => _bottomTabs.SelectedTab = _runtimePage;

    /// <summary>重新读取运行命令的可用性和提示，例如导航层级变化后。</summary>
    public void RefreshRunCommands() => UpdateRuntimeCommands();

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        QueueAutoFit();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Toolbox.NodeTypeActivated -= OnNodeTypeActivated;
            if (_session is not null) _session.Changed -= OnSessionChanged;
            if (_runCommands is not null) _runCommands.Changed -= OnRunCommandsChanged;
        }
        base.Dispose(disposing);
    }

    private void AddMenuItem(ToolStripDropDownItem menu, string text, ModernIconKind icon, Action action) =>
        menu.DropDownItems.Add(new ToolStripMenuItem(text, CreateIcon(icon, ModernTheme.Dark.Text), (_, _) => action()));

    /// <summary>工具栏按钮只显示图标，文字保留为提示与无障碍名称。</summary>
    private void SetIconOnly(ToolStripItem item, ModernIconKind icon, Color color, string toolTip)
    {
        item.Image = CreateIcon(icon, color);
        item.DisplayStyle = ToolStripItemDisplayStyle.Image;
        item.AccessibleName = item.Text;
        item.ToolTipText = toolTip;
        item.AutoToolTip = false;
    }

    /// <summary>创建与工具栏同尺寸的图标。</summary>
    public Bitmap CreateIcon(ModernIconKind icon, Color color) =>
        ModernIcons.CreateBitmap(icon, color, _toolbar.LogicalToDeviceUnits(16));

    // 会话绑定可能早于宿主布局；延迟到消息队列再读取画布的最终尺寸。
    // 仅消费一次待居中请求，后续编辑及窗口尺寸变化不重置用户视口。
    private void QueueAutoFit()
    {
        if (!_autoFitPending || _autoFitQueued || !IsHandleCreated || IsDisposed)
            return;
        _autoFitQueued = true;
        BeginInvoke((Action)(() =>
        {
            _autoFitQueued = false;
            if (IsDisposed || !_autoFitPending || Session is null
                || Designer.ClientSize.Width <= 0 || Designer.ClientSize.Height <= 0)
                return;
            _autoFitPending = false;
            Session.FitToView(Designer.ClientSize.Width, Designer.ClientSize.Height);
        }));
    }

    private void OnNodeTypeActivated(object? sender, WorkflowToolboxItem item)
    {
        if (Session is null) return;
        var center = WorkflowDesignerGeometry.ScreenToCanvas(Session, Designer.ClientSize.Width / 2d, Designer.ClientSize.Height / 2d);
        Session.AddNode(item.NodeType, center.X - 90, center.Y - 30);
    }

    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e) => UpdateCommands();

    private void OnRunCommandsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(UpdateRuntimeCommands);
        else UpdateRuntimeCommands();
    }

    private void UpdateCommands()
    {
        _undoButton.Enabled = Session?.CanUndo == true;
        _redoButton.Enabled = Session?.CanRedo == true;
        UpdateRuntimeCommands();
    }

    private void UpdateRuntimeCommands()
    {
        if (IsDisposed) return;
        var commands = RunCommands;
        var state = commands?.State ?? E_WorkflowExecutionState.Idle;
        _runButton.Enabled = commands?.CanRun == true;
        _runButton.Text = commands?.RunText ?? "运行";
        // 不能运行时提示里说明原因。
        _runButton.ToolTipText = commands?.RunToolTip ?? "运行";
        _pauseButton.Enabled = state == E_WorkflowExecutionState.Running;
        _resumeButton.Enabled = state == E_WorkflowExecutionState.Paused;
        _stopButton.Enabled = state is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
        _runtimeLabel.Text = state.ToString();
    }
}
