using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 运行监视页（只看不改，按设计稿“运行监视”画板）：顶部指标条（流程、状态、运行次数、节拍、圈数），
/// 中间左边只读画布（节点状态、令牌位置、提示角标），右边本次节点耗时和界面同步，下面是轨迹、令牌、并行域、子流程、输出和节点耗时。
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
    private readonly ComboBox _flowPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ModernButton _resetStatistics = new() { Text = "统计清零", ButtonType = ModernButtonType.Text };
    private readonly WorkflowMetricStrip _figures = new();
    private readonly WorkflowFactList _side = new();
    private readonly WorkflowCard _canvasCard;
    private readonly WorkflowStatusLine _status = new();
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

        // 顶部指标条 | 中间（画布卡、右侧耗时与同步卡） | 底部运行数据卡 | 状态行，卡片之间隔 10。
        _figures.AddTool(_resetStatistics);
        _figures.AddTool(_flowPicker);
        _noCanvas.TextAlign = ContentAlignment.MiddleCenter;
        _noCanvas.Visible = false;
        _canvasHost.Controls.Add(Canvas);
        _canvasHost.Controls.Add(_noCanvas);
        _canvasCard = Section("流程画布", "只读", _canvasHost);
        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(_canvasCard);
        middle.Controls.Add(Gap(DockStyle.Right));
        middle.Controls.Add(Section("本次节点耗时", null, _side, DockStyle.Right, 300));

        var split = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, InitialPanel2Size = 250 };
        split.SplitterWidth = LogicalToDeviceUnits(10);
        split.Panel1.Controls.Add(middle);
        var bottom = new WorkflowCard { Dock = DockStyle.Fill };
        bottom.Controls.Add(Monitor);
        split.Panel2.Controls.Add(bottom);

        Controls.Add(split);
        Controls.Add(Gap(DockStyle.Top));
        Controls.Add(_figures);
        Controls.Add(_status);

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
        _status.Message = message;
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
        var interval = _binding?.FrameIntervalMilliseconds ?? WorkflowUiRefresh.FrameIntervalMilliseconds;
        var note = $"界面每 {interval} ms 拉取一次快照";
        if (_flow is null)
        {
            _figures.Show(_center.Flows.Count == 0 ? "没有流程" : "未选择流程", "在右边选一个流程", Array.Empty<WorkflowMetric>(), note);
            _side.Show(Array.Empty<WorkflowFact>());
            _status.Summary = string.Empty;
            return;
        }
        var row = _overview.GetRows().FirstOrDefault(item => string.Equals(item.Name, _flow.Name, StringComparison.Ordinal));
        var state = _flow.State;
        var inFault = row?.InFault == true;
        var stateColor = inFault || state == E_WorkflowExecutionState.Faulted ? theme.Error
            : state == E_WorkflowExecutionState.Running ? theme.Success
            : state == E_WorkflowExecutionState.Paused ? theme.Warning
            : theme.TextSecondary;
        var s = _statistics;
        var stateText = row?.StateText ?? state.ToString();
        _figures.Show(_flow.Name, stateText, new[]
        {
            new WorkflowMetric(s.Runs.ToString("N0", CultureInfo.InvariantCulture), "运行次数"),
            new WorkflowMetric(s.AverageCycle is { } average ? Milliseconds(average) : "—", $"平均节拍（近 {Math.Max(1, s.CycleSamples)} 次）"),
            new WorkflowMetric(s.LastCycle is { } last ? Milliseconds(last) : "—", "最近一圈"),
            new WorkflowMetric(s.Laps.ToString("N0", CultureInfo.InvariantCulture), "圈数"),
            new WorkflowMetric(s.Succeeded.ToString("N0", CultureInfo.InvariantCulture), "成功"),
            new WorkflowMetric(s.Failed.ToString("N0", CultureInfo.InvariantCulture), "出错", s.Failed > 0 ? theme.Error : null),
            new WorkflowMetric(stateText, "状态", stateColor)
        }, note);
        HeaderOf(_canvasCard).Subtitle = _navigator?.RootSession.RuntimeSnapshot is { } snapshot ? $"只读 · 快照 #{snapshot.Sequence}" : "只读";

        var facts = new List<WorkflowFact>();
        var timings = Monitor.TimingTrends;
        var slowest = timings.Count == 0 ? null : timings.OrderByDescending(item => item.Elapsed).First();
        foreach (var item in timings.Take(8))
            facts.Add(new WorkflowFact(NodeTitle(item.NodeId), item.ExecutionCount == 0 ? "—" : $"{item.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms",
                Strong: ReferenceEquals(item, slowest)));
        if (timings.Count == 0) facts.Add(new WorkflowFact("还没有节点执行", string.Empty));
        facts.Add(WorkflowFact.Group("界面同步", s.DisplayLatency is null ? string.Empty : "正常"));
        facts.Add(new WorkflowFact("拉取周期", $"{interval} ms（{(1000.0 / Math.Max(1, interval)).ToString("0.#", CultureInfo.InvariantCulture)} Hz）"));
        facts.Add(new WorkflowFact("最新快照", s.LastSequence < 0 ? "—" : "#" + s.LastSequence.ToString(CultureInfo.InvariantCulture)));
        facts.Add(new WorkflowFact("显示延迟", s.DisplayLatency is { } latency ? Milliseconds(latency) : "—"));
        facts.Add(new WorkflowFact("执行线程等待界面", "从不", Strong: true));
        _side.Show(facts);
        _status.Summary = $"{_flow.Name} · {stateText} · 成功 {s.Succeeded} · 出错 {s.Failed} · 取消 {s.Canceled}";
    }

    private string NodeTitle(string nodeId)
    {
        var node = _navigator?.RootSession.Document.Nodes.FirstOrDefault(item => item.Node.Id == nodeId)?.Node;
        return node is null || string.IsNullOrWhiteSpace(node.Title) ? nodeId : node.Title;
    }

    private static string Milliseconds(TimeSpan value) =>
        value.TotalSeconds >= 10 ? value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
        : value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
}
