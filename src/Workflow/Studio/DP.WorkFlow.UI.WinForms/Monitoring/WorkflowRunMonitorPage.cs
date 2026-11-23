using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 运行监视页（只看不改）：选一个流程，上面一排是状态、运行次数、圈数、节拍和界面同步情况，
/// 中间是只读画布（节点状态、令牌位置、提示角标），下面是令牌、并行域、子流程、事件（可筛选）、输出和节点耗时。
/// </summary>
/// <remarks>
/// 画布和下方表格都挂在流程自己的运行宿主上，按界面节拍拉快照（<see cref="WorkflowStudioRuntimeBinding"/>），不拖慢执行线程。
/// 文档由宿主提供（<c>documentOf</c>），应与流程管理加载时的一致；取不到时只显示下方表格。
/// </remarks>
public sealed class WorkflowRunMonitorPage : WorkflowPage
{
    private readonly WorkflowRunCenter _center;
    private readonly WorkflowRunCenterOverviewModel _overview;
    private readonly WorkflowNodeCatalog _catalog;
    private readonly Func<WorkflowRunCenterFlow, WorkflowDocument?> _documentOf;
    private readonly ComboBox _flowPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Margin = new Padding(0, 2, 12, 0) };
    private readonly ModernButton _resetStatistics = new() { Text = "统计清零", AutoSize = true };
    private readonly Label _state = Label();
    private readonly Label _runs = Label();
    private readonly Label _laps = Label();
    private readonly Label _cycle = Label();
    private readonly Label _sync = Label();
    private readonly Label _message = Label();
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill };
    private readonly Label _noCanvas = Label("取不到这个流程的文档，只显示下方的运行数据。");
    private readonly WorkflowRunStatistics _statistics = new();
    private string _flowsSignature = string.Empty;
    private WorkflowRunCenterFlow? _flow;
    private WorkflowDesignerNavigator? _navigator;
    private WorkflowStudioRuntimeBinding? _binding;
    private long _observedSequence = -1;

    /// <summary>创建运行监视页。</summary>
    /// <param name="center">流程管理。</param>
    /// <param name="catalog">节点目录（画布用）。</param>
    /// <param name="documentOf">取流程的文档；为空或返回空时不显示画布。</param>
    public WorkflowRunMonitorPage(WorkflowRunCenter center, WorkflowNodeCatalog catalog, Func<WorkflowRunCenterFlow, WorkflowDocument?>? documentOf = null)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
        _overview = new WorkflowRunCenterOverviewModel(_center);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _documentOf = documentOf ?? (_ => null);
        Canvas = new WorkflowDesignerControl { Dock = DockStyle.Fill, ReadOnly = true, AllowDrop = false };
        Monitor = new WorkflowRuntimeMonitorControl { Dock = DockStyle.Fill };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6, 6, 6, 0) };
        top.Controls.Add(new Label { Text = "流程", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        top.Controls.Add(_flowPicker);
        top.Controls.Add(_resetStatistics);

        var figures = Grid(5, new RowStyle(SizeType.Absolute, 96));
        for (var column = 0; column < 5; column++)
            figures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        figures.Dock = DockStyle.Top;
        figures.Height = 96;
        figures.Controls.Add(Card("状态", _state), 0, 0);
        figures.Controls.Add(Card("运行次数", _runs), 1, 0);
        figures.Controls.Add(Card("圈数", _laps), 2, 0);
        figures.Controls.Add(Card("节拍", _cycle), 3, 0);
        figures.Controls.Add(Card("界面同步", _sync), 4, 0);

        _noCanvas.TextAlign = ContentAlignment.MiddleCenter;
        _noCanvas.Visible = false;
        _canvasHost.Controls.Add(Canvas);
        _canvasHost.Controls.Add(_noCanvas);
        var split = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, InitialPanel2Size = 260 };
        split.Panel1.Controls.Add(Card("流程画布（只读）", _canvasHost));
        split.Panel2.Controls.Add(Monitor);

        _message.Dock = DockStyle.Bottom;
        _message.Height = 24;
        Controls.Add(split);
        Controls.Add(figures);
        Controls.Add(top);
        Controls.Add(_message);

        _flowPicker.SelectedIndexChanged += (_, _) => Bind(_flowPicker.SelectedItem as string);
        _resetStatistics.Click += (_, _) => { _statistics.Reset(); _observedSequence = -1; ShowFigures(); };
        Canvas.NoticesPageRequested += (_, _) => Monitor.Focus();
        // 统计和数字跟统一刷新时钟走：每帧看一眼画布会话上的最新快照，序号变了才计数。
        WorkflowRefreshClock.Subscribe(this, OnFrame);
        ShowFigures();
    }

    /// <summary>只读画布。</summary>
    public WorkflowDesignerControl Canvas { get; }

    /// <summary>下方的运行数据（令牌、并行域、子流程、事件、输出、节点耗时）。</summary>
    public WorkflowRuntimeMonitorControl Monitor { get; }

    /// <summary>当前统计。</summary>
    public WorkflowRunStatistics Statistics => _statistics;

    /// <summary>当前看的流程名；没有时为空。</summary>
    public string? FlowName => _flow?.Name;

    /// <summary>切到指定流程。</summary>
    /// <param name="name">流程名。</param>
    /// <returns>找到时返回 true。</returns>
    public bool ShowFlow(string name)
    {
        RefreshFlows();
        var index = _flowPicker.Items.IndexOf(name);
        if (index < 0) return false;
        _flowPicker.SelectedIndex = index;
        return true;
    }

    /// <inheritdoc />
    public override void ShowMessage(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => ShowMessage(message))); return; }
        _message.Text = message;
    }

    /// <inheritdoc />
    protected override void OnRefresh(bool canOperate, bool force)
    {
        RefreshFlows();
        ShowFigures();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) Unbind();
        base.Dispose(disposing);
    }

    private void RefreshFlows()
    {
        var names = _center.Flows.Select(flow => flow.Name).ToArray();
        var signature = string.Join("|", names);
        if (signature == _flowsSignature) return;
        _flowsSignature = signature;
        var selected = _flowPicker.SelectedItem as string ?? _flow?.Name;
        _flowPicker.BeginUpdate();
        _flowPicker.Items.Clear();
        foreach (var name in names) _flowPicker.Items.Add(name);
        _flowPicker.EndUpdate();
        var index = selected is null ? -1 : Array.IndexOf(names, selected);
        if (index < 0 && names.Length > 0) index = 0;
        if (index >= 0) _flowPicker.SelectedIndex = index;
        else Bind(null);
    }

    private void Bind(string? name)
    {
        var flow = name is null ? null : _center.GetFlow(name);
        if (ReferenceEquals(flow, _flow) && flow is not null) return;
        Unbind();
        _flow = flow;
        _statistics.Reset();
        _observedSequence = -1;
        if (flow is null)
        {
            ShowFigures();
            return;
        }
        WorkflowDocument? document = null;
        try { document = _documentOf(flow); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            ShowMessage($"读不出流程 {flow.Name} 的文档：{exception.Message}");
        }
        if (document is not null && !string.IsNullOrWhiteSpace(document.EntryNodeId))
        {
            _navigator = new WorkflowDesignerNavigator(document, _catalog);
            _binding = new WorkflowStudioRuntimeBinding(flow.Host, _navigator);
            Canvas.Session = _navigator.CurrentSession;
            Monitor.RuntimeBinding = _binding;
            Canvas.Visible = true;
            _noCanvas.Visible = false;
            BeginInvokeFit();
        }
        else
        {
            Canvas.Session = null;
            Canvas.Visible = false;
            _noCanvas.Visible = true;
        }
        ShowFigures();
    }

    private void Unbind()
    {
        Canvas.Session = null;
        Monitor.RuntimeBinding = null;
        _binding?.Dispose();
        _binding = null;
        _navigator = null;
        _flow = null;
    }

    private void BeginInvokeFit()
    {
        if (!IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            if (Canvas.Session is { } session && Canvas.ClientSize.Width > 0 && Canvas.ClientSize.Height > 0)
                session.FitToView(Canvas.ClientSize.Width, Canvas.ClientSize.Height);
        }));
    }

    private void OnFrame()
    {
        if (_navigator?.RootSession.RuntimeSnapshot is { } snapshot && snapshot.Sequence != _observedSequence)
        {
            _statistics.Observe(snapshot, DateTimeOffset.UtcNow);
            _observedSequence = snapshot.Sequence;
        }
        ShowFigures();
    }

    private void ShowFigures()
    {
        var theme = WorkflowWinFormsTheme.Modern;
        if (_flow is null)
        {
            _state.Text = _center.Flows.Count == 0 ? "没有流程" : "未选择流程";
            _state.ForeColor = theme.TextSecondary;
            _runs.Text = _laps.Text = _cycle.Text = _sync.Text = "—";
            return;
        }
        var row = _overview.GetRows().FirstOrDefault(item => string.Equals(item.Name, _flow.Name, StringComparison.Ordinal));
        var state = _flow.State;
        var inFault = row?.InFault == true;
        _state.Text = row?.StateText ?? state.ToString();
        _state.ForeColor = inFault || state == E_WorkflowExecutionState.Faulted ? theme.Error
            : state == E_WorkflowExecutionState.Running ? theme.Success
            : state == E_WorkflowExecutionState.Paused ? theme.Warning
            : theme.TextSecondary;
        var s = _statistics;
        _runs.Text = s.Runs == 0 ? "0" : $"{s.Runs}（成功 {s.Succeeded} · 出错 {s.Failed} · 取消 {s.Canceled}）";
        _laps.Text = s.Laps.ToString(CultureInfo.InvariantCulture);
        _cycle.Text = s.AverageCycle is { } average
            ? $"近 {s.CycleSamples} 次平均 {Milliseconds(average)} · 最近 {Milliseconds(s.LastCycle!.Value)}"
            : "还没有完整的一圈";
        var interval = _binding?.FrameIntervalMilliseconds ?? WorkflowUiRefresh.FrameIntervalMilliseconds;
        _sync.Text = s.DisplayLatency is { } latency
            ? $"每 {interval} ms 拉取 · 显示延迟 {Milliseconds(latency)}"
            : $"每 {interval} ms 拉取";
    }

    private static string Milliseconds(TimeSpan value) =>
        value.TotalSeconds >= 10 ? value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
        : value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
}
