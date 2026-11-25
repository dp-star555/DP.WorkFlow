using DP.WorkFlow.UI;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 画布工作区（设计页的主体，按设计稿排成卡片）：
/// 上面一条命令卡（宿主的“文件”等条目、撤销、重做、路径和“已修改”标记；右边适合画布、自动布局、对齐、校验、调试运行），
/// 中间左边工具箱卡、中间画布卡、右边可选的检查器卡（<see cref="Inspector"/>），
/// 下面页签卡“诊断 | 节点提示 | 运行监视 | 公共数据”，最下面一行状态（节点数、连线数、选中节点、宿主消息）。
/// 工作台主画布和节点窗口的子画布共用这一个控件；两者的差别只在运行命令（<see cref="RunCommands"/>）、
/// 监视来源（<see cref="WorkflowRuntimeMonitorControl.Source"/>）、检查器和宿主加入的条目。
/// </summary>
public sealed class WorkflowCanvasWorkspaceControl : UserControl
{
    private readonly ModernToolStrip _toolbar = new();
    private readonly ToolStripButton _undoButton = new() { Name = "_undoButton", Text = "撤销" };
    private readonly ToolStripButton _redoButton = new() { Name = "_redoButton", Text = "重做" };
    private readonly ToolStripSeparator _editSeparator = new();
    private readonly ToolStripLabel _breadcrumbLabel = new() { Name = "_breadcrumbLabel" };
    private readonly ToolStripLabel _stateLabel = new() { Name = "_stateLabel", Visible = false };
    private readonly List<(ToolStripItem Item, ModernIconKind Icon, Func<ModernTheme, Color> Color)> _icons = new();
    private readonly FlowLayoutPanel _commands = new()
    {
        Dock = DockStyle.Right,
        AutoSize = true,
        WrapContents = false,
        FlowDirection = FlowDirection.RightToLeft
  };
    private readonly ModernButton _fitButton = new() { Name = "fitButton", Text = "适合画布", ButtonType = ModernButtonType.Text};
    private readonly ModernButton _autoLayoutButton = new() { Name = "autoLayoutButton", Text = "自动布局", ButtonType = ModernButtonType.Text};
    private readonly ModernButton _alignButton = new() { Name = "alignButton", Text = "对齐 ▾", ButtonType = ModernButtonType.Text};
    private readonly ModernContextMenu _alignMenu = new();
    private readonly ModernButton _validateButton = new() { Name = "validateButton", Text = "校验"};
    private readonly ModernButton _runButton = new() { Name = "_runButton", Text = "调试运行", ButtonType = ModernButtonType.Primary, Icon = ModernIconKind.Play };
    private readonly ModernButton _pauseButton = new() { Name = "_pauseButton", Text = "暂停", Icon = ModernIconKind.Pause };
    private readonly ModernButton _resumeButton = new() { Name = "_resumeButton", Text = "继续", Icon = ModernIconKind.Resume };
    private readonly ModernButton _stopButton = new() { Name = "_stopButton", Text = "停止", Icon = ModernIconKind.Stop, ButtonType = ModernButtonType.DangerOutline };
    private readonly List<Control> _hostCommands = new();
    private readonly WorkflowCard _commandCard = new() { Dock = DockStyle.Top };
    private readonly WorkflowCard _toolboxCard = new() { Dock = DockStyle.Left };
    private readonly WorkflowCard _canvasCard = new() { Dock = DockStyle.Fill };
    private readonly WorkflowCard _inspectorCard = new() { Dock = DockStyle.Right, Visible = false };
    private readonly WorkflowBlock _inspectorGap = new() { Dock = DockStyle.Right, LogicalExtent = 10, Visible = false };
    private readonly WorkflowCard _bottomCard = new() { Dock = DockStyle.Fill };
    private readonly ModernSplitter _canvasAndBottom = new();
    private readonly ModernTabControl _bottomTabs = new();
    private readonly TabPage _diagnosticsPage = new() { Name = "diagnosticsPage", Text = "诊断", Padding = new Padding(6) };
    private readonly TabPage _noticesPage = new() { Name = "noticesPage", Text = "节点提示", Padding = new Padding(6) };
    private readonly TabPage _runtimePage = new() { Name = "runtimePage", Text = "运行监视", Padding = new Padding(6) };
    private readonly TabPage _publicDataPage = new() { Name = "publicDataPage", Text = "公共数据", Padding = new Padding(6) };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _messageLabel = new() { Dock = DockStyle.Right , TextAlign = ContentAlignment.MiddleRight };
    private WorkflowPublicDataManifestModel? _publicDataModel;
    private WorkflowDesignerSession? _session;
    private IWorkflowCanvasRunCommands? _runCommands;
    private Control? _inspector;
    private bool _autoFitPending;
    private bool _autoFitQueued;

    /// <summary>创建画布工作区。</summary>
    public WorkflowCanvasWorkspaceControl()
    {
        SuspendLayout();
        int S(int value) => LogicalToDeviceUnits(value);
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Dock = DockStyle.Fill;
        _toolbar.Items.AddRange(new ToolStripItem[] { _undoButton, _redoButton, _editSeparator, _breadcrumbLabel, _stateLabel });
        BreadcrumbText = null;

        Designer = new WorkflowDesignerControl
        {
            AllowDrop = true,
            Dock = DockStyle.Fill
      };
        Toolbox = new WorkflowToolboxControl { Dock = DockStyle.Fill };
        Diagnostics = new WorkflowDiagnosticsControl { Dock = DockStyle.Fill };
        RuntimeMonitor = new WorkflowRuntimeMonitorControl { Dock = DockStyle.Fill };
        Notices = new WorkflowNoticesControl { Dock = DockStyle.Fill };
        _diagnosticsPage.Controls.Add(Diagnostics);
        _runtimePage.Controls.Add(RuntimeMonitor);
        _noticesPage.Controls.Add(Notices);
        PublicData = new WorkflowPublicDataPage { Dock = DockStyle.Fill };
        _publicDataPage.Controls.Add(PublicData);
        PublicData.UseActivated += (_, use) => RevealNode(use.PlanPath, use.NodeId, $"节点 {use.NodeId}");
        PublicData.EditError += (_, message) => InteractionError?.Invoke(this, message);
        _bottomTabs.TabPages.Add(_diagnosticsPage);
        _bottomTabs.TabPages.Add(_noticesPage);
        _bottomTabs.TabPages.Add(_runtimePage);
        _bottomTabs.TabPages.Add(_publicDataPage);
        _bottomTabs.Dock = DockStyle.Fill;
        Notices.CountChanged += (_, _) => _noticesPage.Text = Notices.NoticeCount == 0 ? "节点提示" : $"节点提示 ({Notices.NoticeCount})";
        Notices.NoticeActivated += (_, notice) => RevealNotice(notice);
        Designer.NoticesPageRequested += (_, _) => ShowNotices();
        Diagnostics.SummaryChanged += (_, _) => UpdateDiagnosticsCount();

        // 命令卡：左边工具条（宿主条目、撤销重做、路径），右边一组按钮。
        _commands.Controls.AddRange(new Control[] { _stopButton, _resumeButton, _pauseButton, _runButton, _validateButton, Separator(), _alignButton, _autoLayoutButton, _fitButton });
        foreach (Control command in _commands.Controls)
            if (command is ModernButton button) WorkflowCommandSizing.Fit(button);
        _commandCard.LogicalExtent = 46;
        _commandCard.Controls.Add(_toolbar);
        _commandCard.Controls.Add(_commands);

        // 中间：工具箱卡 | 画布卡 | 检查器卡，卡片之间留 10。
        _toolboxCard.LogicalExtent = 220;
        _toolboxCard.Controls.Add(Toolbox);
        _toolboxCard.Controls.Add(new WorkflowCardHeader("工具箱", "拖到画布"));
        _canvasCard.Controls.Add(Designer);
        _inspectorCard.LogicalExtent = 300;
        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(_canvasCard);
        middle.Controls.Add(new WorkflowBlock { Dock = DockStyle.Left, LogicalExtent = 10 });
        middle.Controls.Add(_toolboxCard);
        middle.Controls.Add(_inspectorGap);
        middle.Controls.Add(_inspectorCard);
        _bottomCard.Controls.Add(_bottomTabs);

        // 中间和底部页签之间可以拖动分隔，默认底部 190。
        _canvasAndBottom.Size = new Size(1180, 640);
        _canvasAndBottom.Dock = DockStyle.Fill;
        _canvasAndBottom.Orientation = Orientation.Horizontal;
        _canvasAndBottom.FixedPanel = FixedPanel.Panel2;
        _canvasAndBottom.SplitterWidth = S(10);
        _canvasAndBottom.InitialPanel2Size = 190;
        _canvasAndBottom.SplitterDistance = 440;
        _canvasAndBottom.Panel1.Controls.Add(middle);
        _canvasAndBottom.Panel2.Controls.Add(_bottomCard);

        var status = new WorkflowBlock { Dock = DockStyle.Bottom, LogicalExtent = 30, LogicalPadding = new Padding(4, 4, 4, 0) };
        status.Controls.Add(_statusLabel);
        status.Controls.Add(_messageLabel);

        Controls.Add(_canvasAndBottom);
        Controls.Add(new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 10 });
        Controls.Add(_commandCard);
        Controls.Add(status);
        Size = new Size(1180, 720);
        ResumeLayout(false);

        AddMenuItem(_alignMenu, "左对齐", ModernIconKind.AlignLeft, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Left));
        AddMenuItem(_alignMenu, "右对齐", ModernIconKind.AlignRight, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Right));
        AddMenuItem(_alignMenu, "顶端对齐", ModernIconKind.AlignTop, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Top));
        AddMenuItem(_alignMenu, "底端对齐", ModernIconKind.AlignBottom, () => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Bottom));
        AddMenuItem(_alignMenu, "水平分布", ModernIconKind.DistributeHorizontal, () => Session?.DistributeSelectedNodes(WorkflowNodeDistribution.Horizontal));
        AddMenuItem(_alignMenu, "垂直分布", ModernIconKind.DistributeVertical, () => Session?.DistributeSelectedNodes(WorkflowNodeDistribution.Vertical));
        SetIconOnly(_undoButton, ModernIconKind.Undo, theme => theme.Text, "撤销 (Ctrl+Z)");
        SetIconOnly(_redoButton, ModernIconKind.Redo, theme => theme.Text, "重做 (Ctrl+Y)");

        _undoButton.Click += (_, _) => Session?.Undo();
        _redoButton.Click += (_, _) => Session?.Redo();
        _fitButton.Click += (_, _) => Session?.FitToView(Designer.ClientSize.Width, Designer.ClientSize.Height);
        _autoLayoutButton.Click += (_, _) => Session?.AutoLayout();
        _alignButton.Click += (_, _) => _alignMenu.Show(_alignButton, new Point(0, _alignButton.Height));
        _validateButton.Click += (_, _) =>
        {
            Diagnostics.RefreshDiagnostics();
            _bottomTabs.SelectedTab = _diagnosticsPage;
      };
        _runButton.Click += async (_, _) => await RunAsync();
        _pauseButton.Click += (_, _) => RunCommands?.Pause();
        _resumeButton.Click += (_, _) => RunCommands?.Resume();
        _stopButton.Click += (_, _) => RunCommands?.Cancel();
        Toolbox.NodeTypeActivated += OnNodeTypeActivated;
        Designer.InteractionError += (_, message) => InteractionError?.Invoke(this, message);
        Designer.SizeChanged += (_, _) => QueueAutoFit();
        Load += (_, _) => QueueAutoFit();
        UpdateCommands();
        WorkflowWinFormsTheme.Follow(this, ApplyThemeImages);
    }

    /// <summary>右侧检查器卡的内容；为空时不显示检查器（节点窗口里的子画布）。</summary>
    public Control? Inspector
    {
        get => _inspector;
        set
        {
            if (ReferenceEquals(_inspector, value)) return;
            if (_inspector is not null) _inspectorCard.Controls.Remove(_inspector);
            _inspector = value;
            if (value is WorkflowNodeInspectorControl inspector) inspector.Session = _session;
            if (value is not null)
            {
                value.Dock = DockStyle.Fill;
                _inspectorCard.Controls.Add(value);
                WorkflowWinFormsTheme.Apply(_inspectorCard);
            }
            _inspectorCard.Visible = value is not null;
            _inspectorGap.Visible = value is not null;
        }
    }

    /// <summary>路径后面的文档状态标记（例如“已修改”）；为空时不显示。</summary>
    public string? DocumentState
    {
        get => _stateLabel.Visible ? _stateLabel.Text : null;
        set
        {
            _stateLabel.Text = value is null ? string.Empty : " " + value + " ";
            _stateLabel.Visible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>状态行右边的消息（宿主的保存、发布结果等）；为空时清除。</summary>
    public string? StatusMessage
    {
        get => _messageLabel.Text;
        set => _messageLabel.Text = value ?? string.Empty;
    }

    /// <summary>在命令卡右边、“校验”左边加一个宿主按钮（例如“发布”）。</summary>
    /// <param name="command">按钮。</param>
    public void AddCommand(Control command)
    {
        Guard.NotNull(command);
        if (command is ModernButton button) WorkflowCommandSizing.Fit(button);
        _hostCommands.Add(command);
        _commands.Controls.Add(command);
        _commands.Controls.SetChildIndex(command, _commands.Controls.GetChildIndex(_validateButton) + 1);
        WorkflowWinFormsTheme.Apply(command);
    }

    /// <summary>画布。</summary>
    public WorkflowDesignerControl Designer { get; }

    /// <summary>工具箱。</summary>
    public WorkflowToolboxControl Toolbox { get; }

    /// <summary>诊断面板。</summary>
    public WorkflowDiagnosticsControl Diagnostics { get; }

    /// <summary>运行监视。</summary>
    public WorkflowRuntimeMonitorControl RuntimeMonitor { get; }

    /// <summary>提示页：全部节点提示与画布级提示，提示板取自画布会话（运行绑定设置）。</summary>
    public WorkflowNoticesControl Notices { get; }

    /// <summary>公共数据清单页（随画布会话切换）。</summary>
    public WorkflowPublicDataPage PublicData { get; }

    /// <summary>切到公共数据页。</summary>
    public void ShowPublicData() => _bottomTabs.SelectedTab = _publicDataPage;

    /// <summary>切到提示页。</summary>
    public void ShowNotices()
    {
        Notices.RefreshNotices();
        _bottomTabs.SelectedTab = _noticesPage;
    }

    /// <summary>命令卡左边的工具条；宿主可在最前面插入自己的条目（例如“文件”菜单、保存）。</summary>
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
            Notices.Board = value?.Notices;
            if (_inspector is WorkflowNodeInspectorControl inspector) inspector.Session = value;
            _publicDataModel?.Dispose();
            _publicDataModel = value is null ? null : new WorkflowPublicDataManifestModel(value);
            PublicData.Model = _publicDataModel;
            _autoFitPending = value is not null;
            QueueAutoFit();
            UpdateStatus();
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
            _publicDataModel?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AddMenuItem(ToolStrip menu, string text, ModernIconKind icon, Action action)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => action());
        _icons.Add((item, icon, theme => theme.Text));
        menu.Items.Add(item);
    }

    private Control Separator() => new Panel { Width = LogicalToDeviceUnits(1), Height = LogicalToDeviceUnits(20), Tag = "separator" };

    /// <summary>
    /// 登记一个随主题重画图标的工具栏条目（宿主加入的“文件”菜单等也可以用）；立即按当前主题画一次。
    /// </summary>
    /// <param name="item">条目。</param>
    /// <param name="icon">图标。</param>
    /// <param name="color">从主题取图标颜色；为空时用正文色。</param>
    public void RegisterThemedIcon(ToolStripItem item, ModernIconKind icon, Func<ModernTheme, Color>? color = null)
    {
        Guard.NotNull(item);
        var pick = color ?? (theme => theme.Text);
        _icons.Add((item, icon, pick));
        item.Image = CreateIcon(icon, pick(WorkflowWinFormsTheme.Modern));
    }

    private void ApplyThemeImages()
    {
        var theme = WorkflowWinFormsTheme.Modern;
        foreach (var (item, icon, color) in _icons)
        {
            var old = item.Image;
            item.Image = CreateIcon(icon, color(theme));
            old?.Dispose();
        }
        _breadcrumbLabel.ForeColor = theme.Text;
        _stateLabel.ForeColor = theme.TextSecondary;
        _stateLabel.BackColor = theme.Control;
        _statusLabel.ForeColor = theme.TextSecondary;
        _messageLabel.ForeColor = theme.TextSecondary;
        foreach (Control control in _commands.Controls)
            if (Equals(control.Tag, "separator")) control.BackColor = theme.BorderSecondary;
        _alignMenu.Theme = theme;
    }

    /// <summary>工具栏按钮只显示图标，文字保留为提示与无障碍名称。</summary>
    private void SetIconOnly(ToolStripItem item, ModernIconKind icon, Func<ModernTheme, Color> color, string toolTip)
    {
        _icons.Add((item, icon, color));
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

    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        // 提示板由运行绑定在应用快照时交给会话，所以随运行类变化同步一次。
        if (e.Kind == WorkflowDesignerChangeKind.Runtime) Notices.Board = _session?.Notices;
        if (e.Kind is WorkflowDesignerChangeKind.Document or WorkflowDesignerChangeKind.Selection) UpdateStatus();
        UpdateCommands();
    }

    /// <summary>状态行：节点数、连线数、选中的节点。</summary>
    private void UpdateStatus()
    {
        if (_session is not { } session)
        {
            _statusLabel.Text = string.Empty;
            return;
        }
        var text = $"节点 {session.Document.Nodes.Count} · 连线 {session.Document.Connections.Count}";
        var selected = session.SelectedNodeIds.Count;
        if (selected == 1 && session.Document.Nodes.FirstOrDefault(item => item.Node.Id == session.SelectedNodeId) is { } node)
            text += " · 选中：" + node.Node.Title;
        else if (selected > 1)
            text += $" · 选中 {selected} 个节点";
        _statusLabel.Text = text;
    }

    private void UpdateDiagnosticsCount()
    {
        var count = Diagnostics.IssueCount;
        _validateButton.Text = count == 0 ? "校验" : $"校验 · {count}";
        _validateButton.ButtonType = count == 0 ? ModernButtonType.Default : ModernButtonType.Warning;
        _diagnosticsPage.Text = count == 0 ? "诊断" : $"诊断 ({count})";
    }

    /// <summary>
    /// 定位提示：本画布上的节点直接选中；子画布里的提示选中本画布上包含它的最外层复合节点（子画布以节点窗口打开）。
    /// </summary>
    private void RevealNotice(WorkflowNotice notice) =>
        RevealNode(notice.PlanPath, notice.NodeId, "提示位于 " + WorkflowNoticeLocation.Describe(notice));

    /// <summary>定位到画布路径下的节点；子画布里的节点选中本画布上包含它的最外层复合节点。</summary>
    private void RevealNode(string planPath, string? nodeId, string what)
    {
        if (_session is not { } session) return;
        var here = WorkflowNoticeLocation.Split(session.NoticePlanPath);
        var there = WorkflowNoticeLocation.Split(planPath);
        if (there.Count < here.Count || !there.Take(here.Count).SequenceEqual(here, StringComparer.Ordinal))
        {
            InteractionError?.Invoke(this, $"{what}，不在当前画布中。");
            return;
        }
        var target = there.Count > here.Count ? there[here.Count] : nodeId;
        if (target is null || !session.Document.Nodes.Any(node => node.Node.Id == target)) return;
        session.SelectedNodeId = target;
        Designer.Focus();
    }

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
        var active = state is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
        _runButton.Visible = !active;
        _runButton.Enabled = commands?.CanRun == true;
        _runButton.Text = commands?.RunText ?? "调试运行";
        // 不能运行时悬停说明原因。
        _runButton.AccessibleDescription = commands?.RunToolTip;
        _pauseButton.Visible = state == E_WorkflowExecutionState.Running;
        _resumeButton.Visible = state == E_WorkflowExecutionState.Paused;
        _stopButton.Visible = active;
    }
}
