using System.Globalization;
using DP.WorkFlow.UI;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 设计页右侧的节点检查器：选中节点的图标、标题、类型和上次运行状态，下面分“参数 | 输出 | 提示”三页，
/// 底部“打开节点编辑”进入完整的节点窗口（专用页面、子画布、脚本）。
/// </summary>
/// <remarks>
/// 参数改动立即写进流程（和画布上的其他编辑一样能撤销），不另设“应用”。
/// “输出”页上面是多输出节点的输出端口开关（显示哪些输出，例如“失败”），下面是上次运行的输出结果。
/// 没选节点或选了多个时显示说明文字。
/// </remarks>
public sealed class WorkflowNodeInspectorControl : UserControl
{
    private readonly WorkflowNodeHeader _header = new();
    private readonly ModernTabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _parametersPage = new() { Text = "参数" };
    private readonly TabPage _resultsPage = new() { Text = "输出" };
    private readonly FlowLayoutPanel _ports = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        WrapContents = true,
        Padding = new Padding(4, 6, 4, 6),
        Visible = false
    };
    private readonly WorkflowLabel _portsTitle = new() { Text = "输出端口", Bold = true, AutoSize = true, Margin = new Padding(4, 6, 12, 4) };
    private string? _portsKey;
    private bool _syncingPorts;
    private readonly TabPage _noticesPage = new() { Text = "提示" };
    private readonly ModernDataGridView _notices = WorkflowPage.Table("类别", "说明", "次数");
    private readonly ModernButton _clearNotices = new() { Text = "确认并清除", ButtonType = ModernButtonType.Text };
    private readonly ModernButton _openEditor = new() { Text = "打开节点编辑", Dock = DockStyle.Fill };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "在画布上选中一个节点，在这里修改参数。双击节点打开完整的节点编辑。"
    };
    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private WorkflowDesignerSession? _session;
    private string? _nodeId;
    private WorkflowNodeResultPageModel? _resultModel;
    private Control? _resultControl;
    private long _noticeVersion = long.MinValue;

    /// <summary>创建检查器。</summary>
    /// <param name="properties">参数面板（工作台的同一个实例，宿主在它上面设置候选、服务表等）。</param>
    public WorkflowNodeInspectorControl(WorkflowPropertyPanel properties)
    {
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Properties.Dock = DockStyle.Fill;
        Properties.UseCompactLayout();
        Properties.ShowOutputPorts = false;
        _resultsPage.Controls.Add(_ports);
        _parametersPage.Controls.Add(Properties);
        WorkflowPage.ColumnWidths(_notices, 0, 1, 0);
        var noticeBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 0, 0) };
        noticeBar.Controls.Add(_clearNotices);
        _noticesPage.Controls.Add(_notices);
        _noticesPage.Controls.Add(noticeBar);
        _tabs.TabPages.Add(_parametersPage);
        _tabs.TabPages.Add(_resultsPage);
        _tabs.TabPages.Add(_noticesPage);

        WorkflowSizing.Fit(_clearNotices);
        var footer = new WorkflowBlock { Dock = DockStyle.Bottom, LogicalExtent = 52, LogicalPadding = new Padding(14, 10, 14, 10) };
        footer.Controls.Add(_openEditor);
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(WorkflowWinFormsTheme.Modern.BorderSecondary);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        _body.Controls.Add(_tabs);
        _body.Controls.Add(footer);
        _body.Controls.Add(_header);
        Controls.Add(_body);
        Controls.Add(_empty);

        _openEditor.Click += (_, _) => { if (SelectedNode is { } node) EditRequested?.Invoke(this, node); };
        _clearNotices.Click += (_, _) =>
        {
            if (_session is not null && _nodeId is not null) _session.AcknowledgeNodeNotices(_nodeId);
            RefreshNotices(force: true);
        };
        WorkflowRefreshClock.Subscribe(this, () => RefreshNotices(force: false));
        WorkflowWinFormsTheme.Follow(this, () =>
        {
            _empty.ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary;
            _header.Invalidate();
        });
        ShowNode();
    }

    /// <summary>参数面板。</summary>
    public WorkflowPropertyPanel Properties { get; }

    /// <summary>当前显示的节点；没选或多选时为空。</summary>
    public IWorkflowNodeModel? SelectedNode =>
        _session is null || _nodeId is null ? null : _session.Document.Nodes.FirstOrDefault(item => item.Node.Id == _nodeId)?.Node;

    /// <summary>点“打开节点编辑”时发生。</summary>
    public event EventHandler<IWorkflowNodeModel>? EditRequested;

    /// <summary>画布会话；检查器跟随它的选择。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            if (_session is not null) _session.Changed -= OnSessionChanged;
            _session = value;
            if (_session is not null) _session.Changed += OnSessionChanged;
            Properties.Session = value;
            ShowNode();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session is not null) _session.Changed -= OnSessionChanged;
            DisposeResults();
        }
        base.Dispose(disposing);
    }

    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnSessionChanged(sender, e)));
            return;
        }
        switch (e.Kind)
        {
            case WorkflowDesignerChangeKind.Selection:
            case WorkflowDesignerChangeKind.Document when SelectedNode is null || CurrentId() != _nodeId:
                ShowNode();
                break;
            case WorkflowDesignerChangeKind.Document:
                ShowPorts();
                _header.Invalidate();
                RefreshNotices(force: false);
                break;
            case WorkflowDesignerChangeKind.Runtime:
                _header.Invalidate();
                RefreshNotices(force: false);
                break;
        }
    }

    private string? CurrentId() => _session is { } session && session.SelectedNodeIds.Count == 1 ? session.SelectedNodeId : null;

    private void ShowNode()
    {
        var id = CurrentId();
        var changed = id != _nodeId;
        _nodeId = id;
        var node = SelectedNode;
        _body.Visible = node is not null;
        _empty.Visible = node is null;
        _header.Session = _session;
        _header.NodeId = node is null ? null : _nodeId;
        if (changed || node is null)
        {
            DisposeResults();
            if (node is not null && _session is not null)
            {
                _resultModel = new WorkflowNodeResultPageModel(_session, node.Id, _session);
                _resultControl = new WorkflowNodeResultsControl(_resultModel) { Dock = DockStyle.Fill };
                _resultsPage.Controls.Add(_resultControl);
                _resultControl.BringToFront();
                WorkflowWinFormsTheme.Apply(_resultControl);
            }
        }
        ShowPorts();
        RefreshNotices(force: true);
    }

    // 输出端口开关：只有多个输出的节点才有。换节点或端口列表变了才重建；勾选状态变了（含撤销）只改勾。
    private void ShowPorts()
    {
        var node = _session is not null && _nodeId is not null ? _session.Document.Nodes.FirstOrDefault(item => item.Node.Id == _nodeId) : null;
        var outputs = node is null ? Array.Empty<WorkflowPortDescriptor>() : _session!.GetDeclaredPorts(node.Node.Id, WorkflowPortDirection.Output);
        var key = node is null || outputs.Count <= 1 ? null : node.Node.Id + "|" + string.Join(",", outputs.Select(port => port.Key));
        _syncingPorts = true;
        try
        {
            if (key != _portsKey)
            {
                _portsKey = key;
                _ports.SuspendLayout();
                foreach (Control control in _ports.Controls.Cast<Control>().ToArray())
                    if (!ReferenceEquals(control, _portsTitle)) control.Dispose();
                _ports.Controls.Clear();
                _ports.Visible = key is not null;
                if (key is not null)
                {
                    _ports.Controls.Add(_portsTitle);
                    foreach (var port in outputs)
                    {
                        var check = WorkflowSizing.Fit(new ModernCheckbox
                        {
                            Text = WorkflowPorts.GetDisplayName(port.Key),
                            Theme = WorkflowWinFormsTheme.Modern,
                            Margin = new Padding(4, 2, 8, 2),
                            Tag = port
                        });
                        var nodeId = node!.Node.Id;
                        var portKey = port.Key;
                        check.CheckedChanged += (_, _) =>
                        {
                            if (_syncingPorts || _session is not { } session) return;
                            // 改不了（例如只剩一个显示的输出）时把勾恢复成实际状态。
                            if (!session.SetOutputPortVisible(nodeId, portKey, check.Checked) && IsHandleCreated) BeginInvoke(new Action(ShowPorts));
                        };
                        _ports.Controls.Add(check);
                    }
                }
                _ports.ResumeLayout(true);
            }
            if (node is null) return;
            foreach (var check in _ports.Controls.OfType<ModernCheckbox>())
                if (check.Tag is WorkflowPortDescriptor port) check.Checked = !node.IsOutputPortHidden(port);
        }
        finally
        {
            _syncingPorts = false;
        }
    }

    private void DisposeResults()
    {
        if (_resultControl is not null)
        {
            _resultsPage.Controls.Remove(_resultControl);
            _resultControl.Dispose();
            _resultControl = null;
        }
        _resultModel?.Dispose();
        _resultModel = null;
    }

    private void RefreshNotices(bool force)
    {
        if (IsDisposed) return;
        var version = _session?.NoticeVersion ?? -1;
        if (!force && version == _noticeVersion) return;
        _noticeVersion = version;
        var notices = _session is not null && _nodeId is not null ? _session.GetNodeNotices(_nodeId) : Array.Empty<WorkflowNotice>();
        _notices.Rows.Clear();
        foreach (var notice in notices.Reverse())
        {
            var index = _notices.Rows.Add(notice.Category, notice.Message, notice.Count.ToString(CultureInfo.InvariantCulture));
            _notices.Rows[index].Cells[1].ToolTipText = notice.Timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + notice.Message;
            _notices.Rows[index].Cells[0].Style.ForeColor = WorkflowWinFormsTheme.Modern.Warning;
        }
        _notices.ClearSelection();
        _noticesPage.Text = notices.Count == 0 ? "提示" : $"提示 ({notices.Count})";
        _clearNotices.Enabled = notices.Count > 0;
        _header.Invalidate();
    }
}

/// <summary>节点标题（检查器和节点编辑窗口共用）：图标方块、节点标题、类型 · 标识，右边一个上次运行状态的标签。</summary>
internal sealed class WorkflowNodeHeader : WorkflowBlock
{
    public WorkflowNodeHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Dock = DockStyle.Top;
        LogicalExtent = 58;
    }

    public WorkflowDesignerSession? Session { get; set; }

    public string? NodeId
    {
        get => _nodeId;
        set { _nodeId = value; Invalidate(); }
    }

    private string? _nodeId;

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Session is not { } session || NodeId is null
            || session.Document.Nodes.FirstOrDefault(item => item.Node.Id == NodeId)?.Node is not { } node)
            return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int S(int value) => LogicalToDeviceUnits(value);
        var box = new Rectangle(S(14), S(12), S(34), S(34));
        using (var path = WorkflowCard.Rounded(box, S(WorkflowWinFormsTheme.Palette.ControlRadius)))
        using (var fill = new SolidBrush(theme.PrimaryBackground))
            g.FillPath(fill, path);
        WorkflowDesignerControl.DrawIcon(g, session.GetNodeIcon(node), RectangleF.Inflate(box, -S(8), -S(8)),
            new WorkflowColor(theme.Primary.R, theme.Primary.G, theme.Primary.B, 255));

        var (badgeText, tone) = Badge(session.GetNodeRuntimeInfo(node.Id));
        using var small = new Font(Font.FontFamily, Font.Size * .88f);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        var badgeLeft = WorkflowTones.DrawBadge(g, badgeText, small, tone, Width - S(14), box, this);

        var left = box.Right + S(10);
        var width = Math.Max(0, badgeLeft - S(8) - left);
        using var bold = new Font(Font.FontFamily, Font.Size * 1.08f, FontStyle.Bold);
        TextRenderer.DrawText(g, node.Title, bold, new Rectangle(left, S(10), width, S(20)), theme.Text, flags);
        var type = session.Catalog.TryGet(node.NodeType, out var descriptor) && descriptor?.DisplayName is { } name ? name : node.NodeType;
        TextRenderer.DrawText(g, type + " · " + node.Id, small, new Rectangle(left, S(30), width, S(18)), theme.TextSecondary, flags);
    }

    private static (string Text, WorkflowTone Tone) Badge(WorkflowNodeRuntimeInfo? info) => info?.State switch
    {
        E_NodeState.Completed => ("成功", WorkflowTone.Success),
        E_NodeState.Failed => ("失败", WorkflowTone.Error),
        E_NodeState.Running => ("运行中", WorkflowTone.Accent),
        E_NodeState.Canceled => ("已取消", WorkflowTone.Warning),
        _ => ("未运行", WorkflowTone.Neutral)
    };
}
