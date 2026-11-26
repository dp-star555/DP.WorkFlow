using DP.WorkFlow.UI;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 画布工作区（设计页的主体，按设计稿排成卡片）：
/// 上面一条命令卡（宿主的“文件”等条目、撤销、重做、路径和“已修改”标记；右边适合画布、自动布局、对齐、公共数据、校验、调试运行），
/// 中间左边工具箱卡、中间画布卡、右边可选的检查器卡（<see cref="Inspector"/>，默认隐藏，可浮出或固定，见 <see cref="InspectorPinned"/>），
/// 最下面一行状态（节点数、连线数、选中节点、宿主消息）。没有底部页签：诊断直接画在画布上（出错节点标红带角标，悬停看原因），
/// 节点提示和运行页一样用画布角标，公共数据在命令卡上的“公共数据”窗口里编辑，运行明细看运行日志页。
/// 工作台主画布和节点窗口的子画布共用这一个控件；两者的差别只在运行命令（<see cref="RunCommands"/>）、
/// 检查器和宿主加入的条目。
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
    private readonly ModernButton _inspectorButton = new() { Name = "inspectorButton", Text = "节点属性", ButtonType = ModernButtonType.Text, Icon = ModernIconKind.Columns, Visible = false };
    private readonly ModernButton _pinButton = new() { Name = "pinButton", Text = "固定", ButtonType = ModernButtonType.Text, Icon = ModernIconKind.Pin };
    private readonly System.Windows.Forms.Timer _autoHide = new() { Interval = 300 };
    private int _outsideTicks;
    private readonly ModernButton _fitButton = new() { Name = "fitButton", Text = "适合画布", ButtonType = ModernButtonType.Text};
    private readonly ModernButton _autoLayoutButton = new() { Name = "autoLayoutButton", Text = "自动布局", ButtonType = ModernButtonType.Text};
    private readonly ModernButton _alignButton = new() { Name = "alignButton", Text = "对齐 ▾", ButtonType = ModernButtonType.Text};
    private readonly ModernContextMenu _alignMenu = new();
    private readonly ModernButton _publicDataButton = new() { Name = "publicDataButton", Text = "公共数据", ButtonType = ModernButtonType.Text };
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
    private readonly WorkflowStatusLine _status = new();
    private readonly WorkflowCardHeader _inspectorHeader = new("节点属性");
    private Panel _middle = null!;
    private WorkflowPublicDataManifestModel? _publicDataModel;
    private WorkflowPublicDataPage? _publicDataPage;
    private WorkflowPublicDataWindow? _publicDataWindow;
    private WorkflowDesignerSession? _session;
    private IWorkflowCanvasRunCommands? _runCommands;
    private Control? _inspector;
    private bool _autoFitPending;
    private bool _autoFitQueued;

    /// <summary>创建画布工作区。</summary>
    public WorkflowCanvasWorkspaceControl()
    {
        SuspendLayout();
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Dock = DockStyle.Fill;
        _toolbar.Items.AddRange(new ToolStripItem[] { _undoButton, _redoButton, _editSeparator, _breadcrumbLabel, _stateLabel });
        _toolbar.ItemAdded += (_, e) => { if (e.Item is { } item) SpaceToolbarItem(item); };
        BreadcrumbText = null;

        Designer = new WorkflowDesignerControl
        {
            AllowDrop = true,
            Dock = DockStyle.Fill
      };
        Toolbox = new WorkflowToolboxControl { Dock = DockStyle.Fill };
        Diagnostics = new WorkflowCanvasDiagnostics();
        Diagnostics.Changed += (_, _) => ShowDiagnostics();

        // 命令卡：左边工具条（宿主条目、撤销重做、路径），右边一组按钮。
        _commands.Controls.AddRange(new Control[] { _stopButton, _resumeButton, _pauseButton, _runButton, _validateButton, _publicDataButton, Separator(), _alignButton, _autoLayoutButton, _fitButton, _inspectorButton });
        foreach (Control command in _commands.Controls)
            if (command is ModernButton button) WorkflowSizing.Fit(button);
        _commandCard.LogicalExtent = 52;
        _commandCard.Resize += (_, _) => CenterCommands();
        _commandCard.Controls.Add(_toolbar);
        _commandCard.Controls.Add(_commands);

        // 中间：工具箱卡 | 画布卡 | 检查器卡，卡片之间留 10。
        _toolboxCard.LogicalExtent = 220;
        _toolboxCard.Controls.Add(Toolbox);
        _toolboxCard.Controls.Add(new WorkflowCardHeader("工具箱", "拖到画布"));
        _canvasCard.Controls.Add(Designer);
        _inspectorCard.LogicalExtent = 340;
        _inspectorHeader.AddAction(_pinButton);
        _inspectorCard.Controls.Add(_inspectorHeader);
        var middle = _middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(_canvasCard);
        middle.Controls.Add(new WorkflowBlock { Dock = DockStyle.Left, LogicalExtent = 10 });
        middle.Controls.Add(_toolboxCard);
        middle.Controls.Add(_inspectorGap);
        middle.Controls.Add(_inspectorCard);

        Controls.Add(middle);
        Controls.Add(new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 10 });
        Controls.Add(_commandCard);
        Controls.Add(new WorkflowBlock { Dock = DockStyle.Bottom, LogicalExtent = 10 });
        Controls.Add(_status);
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
        _validateButton.Click += (_, _) => Diagnostics.RefreshDiagnostics();
        _publicDataButton.Click += (_, _) => ShowPublicData();
        _runButton.Click += async (_, _) => await RunAsync();
        _inspectorButton.Click += (_, _) => ToggleInspector();
        _pinButton.Click += (_, _) => InspectorPinned = !InspectorPinned;
        _autoHide.Tick += (_, _) => AutoHideInspector();
        middle.Resize += (_, _) => PlaceInspector();
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
                value.BringToFront();
                WorkflowWinFormsTheme.Apply(_inspectorCard);
            }
            _inspectorButton.Visible = value is not null;
            _inspectorShown = value is not null && _inspectorPinned;
            PlaceInspector();
        }
    }

    /// <summary>
    /// 检查器是否固定在画布右边。默认不固定：检查器隐藏，点命令卡上的“节点属性”浮在画布右侧，
    /// 鼠标移开且焦点不在里面时自动收起；固定后像普通卡片一样占一栏，直到取消固定。
    /// </summary>
    public bool InspectorPinned
    {
        get => _inspectorPinned;
        set
        {
            if (_inspectorPinned == value) return;
            _inspectorPinned = value;
            _inspectorShown = value && _inspector is not null;
            PlaceInspector();
        }
    }

    private bool _inspectorPinned;
    private bool _inspectorShown;

    /// <summary>检查器当前是否可见（固定或浮出）。</summary>
    public bool InspectorVisible => _inspectorCard.Visible;

    /// <summary>浮出检查器（不固定时鼠标移开会自动收起）。</summary>
    public void ShowInspector()
    {
        if (_inspector is null) return;
        _inspectorShown = true;
        _outsideTicks = 0;
        PlaceInspector();
    }

    private void ToggleInspector()
    {
        if (_inspectorCard.Visible)
        {
            _inspectorPinned = false;
            _inspectorShown = false;
            PlaceInspector();
        }
        else ShowInspector();
    }

    // 固定：停靠在右边占一栏；浮出：盖在画布卡右侧，高度和中间一栏相同。
    private void PlaceInspector()
    {
        if (_middle is null) return;
        var visible = _inspector is not null && _inspectorShown;
        _middle.SuspendLayout();
        if (visible && _inspectorPinned)
        {
            // 停靠按子控件倒序排：检查器和间隔放回最后，才会排在画布卡右边。
            _inspectorCard.Dock = DockStyle.Right;
            _middle.Controls.SetChildIndex(_inspectorGap, _middle.Controls.Count - 1);
            _middle.Controls.SetChildIndex(_inspectorCard, _middle.Controls.Count - 1);
            _inspectorCard.ApplyMetrics();
        }
        else if (visible)
        {
            _inspectorCard.Dock = DockStyle.None;
            var width = LogicalToDeviceUnits(_inspectorCard.LogicalExtent);
            _inspectorCard.Bounds = new Rectangle(Math.Max(0, _middle.ClientSize.Width - width), 0, Math.Min(width, _middle.ClientSize.Width), _middle.ClientSize.Height);
            _inspectorCard.BringToFront();
        }
        _inspectorCard.Visible = visible;
        _inspectorGap.Visible = visible && _inspectorPinned;
        _middle.ResumeLayout(true);
        _pinButton.Text = _inspectorPinned ? "取消固定" : "固定";
        _inspectorButton.ButtonType = visible ? ModernButtonType.Default : ModernButtonType.Text;
        _autoHide.Enabled = visible && !_inspectorPinned;
        _outsideTicks = 0;
    }

    private void AutoHideInspector()
    {
        if (!_inspectorCard.Visible || _inspectorPinned) { _autoHide.Enabled = false; return; }
        var cursor = Cursor.Position;
        var inside = _inspectorCard.ContainsFocus
                     || _inspectorCard.RectangleToScreen(_inspectorCard.ClientRectangle).Contains(cursor)
                     || _inspectorButton.RectangleToScreen(_inspectorButton.ClientRectangle).Contains(cursor)
                     || MouseButtons != MouseButtons.None;
        _outsideTicks = inside ? 0 : _outsideTicks + 1;
        if (_outsideTicks < 2) return;
        _inspectorShown = false;
        PlaceInspector();
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
        get => _status.Message;
        set => _status.Message = value ?? string.Empty;
    }

    /// <summary>在命令卡右边、“校验”左边加一个宿主按钮（例如“发布”）。</summary>
    /// <param name="command">按钮。</param>
    public void AddCommand(Control command)
    {
        Guard.NotNull(command);
        if (command is ModernButton button) WorkflowSizing.Fit(button);
        _hostCommands.Add(command);
        _commands.Controls.Add(command);
        _commands.Controls.SetChildIndex(command, _commands.Controls.GetChildIndex(_validateButton) + 1);
        WorkflowWinFormsTheme.Apply(command);
    }

    /// <summary>画布。</summary>
    public WorkflowDesignerControl Designer { get; }

    /// <summary>工具箱。</summary>
    public WorkflowToolboxControl Toolbox { get; }

    /// <summary>画布诊断；结果画在画布上。</summary>
    public WorkflowCanvasDiagnostics Diagnostics { get; }

    /// <summary>打开公共数据窗口（不挡住画布；已打开时切到前面）。</summary>
    public void ShowPublicData()
    {
        if (_publicDataWindow is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }
        var page = _publicDataPage = new WorkflowPublicDataPage { Model = _publicDataModel };
        page.UseActivated += (_, use) => RevealNode(use.PlanPath, use.NodeId, $"节点 {use.NodeId}");
        page.EditError += (_, message) => InteractionError?.Invoke(this, message);
        var window = _publicDataWindow = new WorkflowPublicDataWindow(page);
        window.FormClosed += (_, _) =>
        {
            if (!ReferenceEquals(_publicDataWindow, window)) return;
            _publicDataWindow = null;
            _publicDataPage = null;
        };
        window.Show(FindForm());
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
            if (_inspector is WorkflowNodeInspectorControl inspector) inspector.Session = value;
            var oldPublicData = _publicDataModel;
            _publicDataModel = value is null ? null : new WorkflowPublicDataManifestModel(value);
            if (_publicDataPage is not null) _publicDataPage.Model = _publicDataModel;
            oldPublicData?.Dispose();
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

    /// <summary>运行以故障结束时发生（故障详情在运行日志里）。</summary>
    public event EventHandler<string>? RunFaulted;

    /// <summary>运行前检查诊断，再执行运行命令；故障时通知宿主。</summary>
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
                // 运行失败只记录：失败节点标红、故障写进运行日志，不弹窗。
                RunFaulted?.Invoke(this, $"流程运行失败：{result.Message}");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException
                                          or NotSupportedException or WorkflowCompilationException)
        {
            InteractionError?.Invoke(this, exception.Message);
        }
    }

    private void CenterCommands()
    {
        var top = Math.Max(0, (_commandCard.DisplayRectangle.Height - LogicalToDeviceUnits(38)) / 2);
        _commands.Padding = new Padding(0, top, LogicalToDeviceUnits(6), 0);
        _toolbar.Padding = new Padding(LogicalToDeviceUnits(8), 0, 0, 0);
    }

    // 工具条条目统一留白：图标按钮、菜单和路径之间不再挤在一起。
    private void SpaceToolbarItem(ToolStripItem item)
    {
        item.Margin = new Padding(LogicalToDeviceUnits(2), 0, LogicalToDeviceUnits(2), 0);
        item.Padding = item is ToolStripSeparator ? Padding.Empty : new Padding(LogicalToDeviceUnits(6), LogicalToDeviceUnits(4), LogicalToDeviceUnits(6), LogicalToDeviceUnits(4));
    }

    /// <summary>重新读取运行命令的可用性和提示，例如导航层级变化后。</summary>
    public void RefreshRunCommands() => UpdateRuntimeCommands();

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        foreach (ToolStripItem item in _toolbar.Items) SpaceToolbarItem(item);
        CenterCommands();
        QueueAutoFit();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Toolbox.NodeTypeActivated -= OnNodeTypeActivated;
            _autoHide.Dispose();
            if (_session is not null) _session.Changed -= OnSessionChanged;
            if (_runCommands is not null) _runCommands.Changed -= OnRunCommandsChanged;
            _publicDataWindow?.Close();
            Diagnostics.Dispose();
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
        if (e.Kind is WorkflowDesignerChangeKind.Document or WorkflowDesignerChangeKind.Selection) UpdateStatus();
        UpdateCommands();
    }

    /// <summary>状态行：节点数、连线数、选中的节点。</summary>
    private void UpdateStatus()
    {
        if (_session is not { } session)
        {
            _status.Summary = string.Empty;
            return;
        }
        var text = $"节点 {session.Document.Nodes.Count} · 连线 {session.Document.Connections.Count}";
        var selected = session.SelectedNodeIds.Count;
        if (selected == 1 && session.Document.Nodes.FirstOrDefault(item => item.Node.Id == session.SelectedNodeId) is { } node)
            text += " · 选中：" + node.Node.Title;
        else if (selected > 1)
            text += $" · 选中 {selected} 个节点";
        _status.Summary = text;
    }

    /// <summary>诊断结果画到画布上，“校验”按钮显示问题条数（有错误为红色）。</summary>
    private void ShowDiagnostics()
    {
        var items = Diagnostics.Items;
        Designer.Diagnostics = items;
        var count = items.Count;
        _validateButton.Text = count == 0 ? "校验" : $"校验 · {count}";
        _validateButton.ButtonType = count == 0 ? ModernButtonType.Default
            : items.Any(item => item.Severity == WorkflowValidationSeverity.Error) ? ModernButtonType.DangerOutline
            : ModernButtonType.Warning;
    }

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
