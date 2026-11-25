using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 运行页（原“运行总览”和“运行监视”合成一页，按设计稿“运行”画板）：
/// 左边流程列表（状态、提示、单独启停），中间是所选流程的指标条和只读画布，
/// 右边“故障处理 | 节点耗时”（有故障时自动切到故障处理），下面是轨迹、令牌、并行域、子流程、输出。
/// </summary>
/// <remarks>
/// 画布和下方表格挂在流程自己的运行宿主上，按界面节拍拉快照（<see cref="WorkflowStudioRuntimeBinding"/>），不拖慢执行线程。
/// 单独启动 / 停止和故障去向都经过 <see cref="Gate"/>（宿主的鉴权和审计）；整线启停由宿主的顶部按钮负责。
/// </remarks>
public sealed class WorkflowRunPage : WorkflowPage
{
    private readonly WorkflowRunCenter _center;
    private readonly WorkflowRunCenterOverviewModel _overview;
    private readonly WorkflowNodeCatalog _catalog;
    private readonly Func<WorkflowRunCenterFlow, WorkflowDocument?> _documentOf;
    private readonly WorkflowItemList _flows = new() { EmptyText = "还没有加载流程" };
    private readonly WorkflowCard _flowsCard;
    private readonly ModernButton _resetStatistics = new() { Text = "统计清零", ButtonType = ModernButtonType.Text };
    private readonly WorkflowMetricStrip _figures = new();
    private readonly WorkflowFactList _timings = new();
    private readonly ModernTabControl _sideTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _faultPage = new() { Text = "故障处理" };
    private readonly TabPage _timingPage = new() { Text = "节点耗时" };
    private readonly WorkflowCard _canvasCard;
    private readonly WorkflowStatusLine _status = new();
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill };
    private readonly Label _noCanvas = Label("取不到这个流程的文档，只显示下方的运行数据。");
    private readonly WorkflowRunStatistics _statistics = new();
    private WorkflowRunCenterFlow? _flow;
    private WorkflowDesignerNavigator? _navigator;
    private WorkflowStudioRuntimeBinding? _binding;
    private long _observedSequence = -1;
    private bool _hadFault;

    /// <summary>创建运行页。</summary>
    /// <param name="center">流程管理。</param>
    /// <param name="catalog">节点目录（画布用）。</param>
    /// <param name="documentOf">取流程的文档；为空或返回空时不显示画布。</param>
    /// <param name="workbench">故障处理模型；为空时按只显示方式新建一个，页面释放时一起释放。</param>
    public WorkflowRunPage(WorkflowRunCenter center, WorkflowNodeCatalog catalog,
        Func<WorkflowRunCenterFlow, WorkflowDocument?>? documentOf = null, WorkflowFaultWorkbenchModel? workbench = null)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
        _overview = new WorkflowRunCenterOverviewModel(_center);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _documentOf = documentOf ?? (_ => null);
        if (workbench is null) _ownedWorkbench = workbench = new WorkflowFaultWorkbenchModel(center);
        Canvas = new WorkflowDesignerControl { Dock = DockStyle.Fill, ReadOnly = true, AllowDrop = false };
        Monitor = new WorkflowRuntimeMonitorControl { Dock = DockStyle.Fill };
        Workbench = new WorkflowFaultWorkbenchControl { Dock = DockStyle.Fill, Model = workbench };

        // 左：流程卡 | 中：指标条、画布卡 | 右：故障处理 / 节点耗时卡；下面运行数据卡；最下状态行。卡片之间隔 10。
        _flowsCard = Section("流程", null, _flows, DockStyle.Left, 260);
        _figures.AddTool(_resetStatistics);
        _noCanvas.TextAlign = ContentAlignment.MiddleCenter;
        _noCanvas.Visible = false;
        _canvasHost.Controls.Add(Canvas);
        _canvasHost.Controls.Add(_noCanvas);
        _canvasCard = Section("流程画布", "只读", _canvasHost);
        var column = new Panel { Dock = DockStyle.Fill };
        column.Controls.Add(_canvasCard);
        column.Controls.Add(Gap(DockStyle.Top));
        column.Controls.Add(_figures);

        _faultPage.Controls.Add(Workbench);
        _timingPage.Controls.Add(_timings);
        _sideTabs.TabPages.Add(_faultPage);
        _sideTabs.TabPages.Add(_timingPage);
        var side = new WorkflowCard { Dock = DockStyle.Right, LogicalExtent = 320 };
        side.Controls.Add(_sideTabs);

        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(column);
        middle.Controls.Add(Gap(DockStyle.Left));
        middle.Controls.Add(_flowsCard);
        middle.Controls.Add(Gap(DockStyle.Right));
        middle.Controls.Add(side);

        var split = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, InitialPanel2Size = 200 };
        split.SplitterWidth = LogicalToDeviceUnits(10);
        split.Panel1.Controls.Add(middle);
        var bottom = new WorkflowCard { Dock = DockStyle.Fill };
        bottom.Controls.Add(Monitor);
        split.Panel2.Controls.Add(bottom);

        Controls.Add(split);
        Controls.Add(_status);

        _flows.SelectionChanged += (_, _) => Bind(_flows.SelectedKey as string);
        _flows.ActionClicked += (_, item) => StartOrStop((string)item.Key);
        _resetStatistics.Click += (_, _) => { _statistics.Reset(); _observedSequence = -1; ShowFigures(); };
        Canvas.NoticesPageRequested += (_, _) => Monitor.Focus();
        Workbench.ActionError += (_, message) => ShowMessage(message);
        Workbench.DecisionRequested += (_, _) => _sideTabs.SelectedTab = _faultPage;
        // 统计和数字跟统一刷新时钟走：每帧看一眼画布会话上的最新快照，序号变了才计数。
        WorkflowRefreshClock.Subscribe(this, OnFrame);
        RefreshFlows();
    }

    private readonly WorkflowFaultWorkbenchModel? _ownedWorkbench;

    /// <summary>只读画布。</summary>
    public WorkflowDesignerControl Canvas { get; }

    /// <summary>下方的运行数据（轨迹、令牌、并行域、子流程、输出、节点耗时）。</summary>
    public WorkflowRuntimeMonitorControl Monitor { get; }

    /// <summary>右栏的故障处理。</summary>
    public WorkflowFaultWorkbenchControl Workbench { get; }

    /// <summary>当前统计。</summary>
    public WorkflowRunStatistics Statistics => _statistics;

    /// <summary>当前看的流程名；没有时为空。</summary>
    public string? FlowName => _flow?.Name;

    /// <summary>
    /// 操作准入（宿主的鉴权和审计），同时交给故障处理；为空时直接执行。单独启动只等命令发出，不等流程跑完。
    /// </summary>
    public IWorkflowOperationGate? Gate
    {
        get => _gate;
        set
        {
            _gate = value;
            Workbench.Gate = value;
        }
    }

    private IWorkflowOperationGate? _gate;

    /// <summary>切到指定流程。</summary>
    /// <param name="name">流程名。</param>
    /// <returns>找到时返回 true。</returns>
    public bool ShowFlow(string name)
    {
        RefreshFlows();
        if (_center.GetFlow(name) is null) return false;
        _flows.SelectedKey = name;
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
        Workbench.CanOperate = canOperate;
        RefreshFlows();
        ShowFigures();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            _ownedWorkbench?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void RefreshFlows()
    {
        var rows = _overview.GetRows();
        _flows.Show(rows.Select(row =>
        {
            var active = row.State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
            var subtitle = row.StateText;
            if (row.NoticeCount > 0) subtitle += $" · 提示 {row.NoticeCount}";
            if (row.RecoveryRound > 0) subtitle += $" · 恢复 {row.RecoveryRound} 轮";
            if (row.RecordingText.Length > 0) subtitle += " · " + row.RecordingText;
            var tone = StateTone(row);
            return new WorkflowListItem(row.Name, row.Name, subtitle, tone,
                Badge: row.InFault ? "故障" : null, BadgeTone: WorkflowTone.Error,
                Action: active ? ModernIconKind.Stop : ModernIconKind.Play,
                ActionText: active ? "停止这个流程" : "启动这个流程",
                Enabled: CanOperate,
                SubtitleTone: tone is WorkflowTone.Error or WorkflowTone.Warning ? tone : WorkflowTone.Neutral);
        }));
        var faults = rows.Count(row => row.InFault || row.State == E_WorkflowExecutionState.Faulted);
        HeaderOf(_flowsCard).Subtitle = rows.Count == 0 ? null : faults > 0 ? $"{rows.Count} 个 · {faults} 个故障" : $"{rows.Count} 个";
        if (_flows.SelectedKey is null && rows.Count > 0)
            _flows.SelectedKey = (rows.FirstOrDefault(row => row.InFault) ?? rows[0]).Name;

        // 刚出故障时切到故障处理；故障处理完后切回节点耗时。
        var hasFault = Workbench.HasCurrentFault;
        if (hasFault != _hadFault) _sideTabs.SelectedTab = hasFault ? _faultPage : _timingPage;
        _hadFault = hasFault;
        _faultPage.Text = hasFault ? "故障处理 ●" : "故障处理";
    }

    private static WorkflowTone StateTone(WorkflowRunCenterFlowRow row) =>
        row.InFault || row.State == E_WorkflowExecutionState.Faulted ? WorkflowTone.Error
        : row.State == E_WorkflowExecutionState.Running ? WorkflowTone.Success
        : row.State == E_WorkflowExecutionState.Paused ? WorkflowTone.Warning
        : WorkflowTone.Neutral;

    private async void StartOrStop(string name)
    {
        if (!CanOperate || _center.GetFlow(name) is not { } flow) return;
        var active = flow.State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
        try
        {
            await (Gate ?? WorkflowOperationGates.Direct).RunAsync(new WorkflowOperation(active ? WorkflowOperationKind.StopFlow : WorkflowOperationKind.StartFlow, name),
                () =>
                {
                    if (active) return _overview.Stop(name);
                    _ = Observe(name, _overview.Start(name));
                    return Task.CompletedTask;
                });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowMessage(exception.Message);
        }
        if (!IsDisposed) RefreshFlows();
    }

    private async Task Observe(string name, Task<WorkflowRunResult> run)
    {
        try
        {
            var result = await run.ConfigureAwait(false);
            if (!result.Success) ShowMessage($"{name}：{result.Message}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or WorkflowCompilationException)
        {
            ShowMessage($"{name}：{exception.Message}");
        }
    }

    private void Bind(string? name)
    {
        var flow = name is null ? null : _center.GetFlow(name);
        if (ReferenceEquals(flow, _flow) && flow is not null) return;
        Unbind();
        _flow = flow;
        _statistics.Reset();
        _statistics.LoopNodeIds = Array.Empty<string>();
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
            _statistics.LoopNodeIds = document.Nodes.Where(item => item.Node is IWorkflowLoopNode).Select(item => item.Node.Id).ToArray();
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
        var note = $"每 {interval} ms 刷新";
        if (_flow is null)
        {
            _figures.Show(_center.Flows.Count == 0 ? "没有流程" : "未选择流程", "在左边选一个流程", Array.Empty<WorkflowMetric>(), note);
            _timings.Show(Array.Empty<WorkflowFact>());
            _status.Summary = string.Empty;
            return;
        }
        var row = _overview.GetRows().FirstOrDefault(item => string.Equals(item.Name, _flow.Name, StringComparison.Ordinal));
        var stateText = row?.StateText ?? _flow.State.ToString();
        var s = _statistics;
        var subtitle = stateText + (row is { RecoveryRound: > 0 } ? $" · 恢复 {row.RecoveryRound} 轮" : string.Empty);
        _figures.Show(_flow.Name, subtitle, new[]
        {
            new WorkflowMetric(s.Runs.ToString("N0", CultureInfo.InvariantCulture), "运行次数"),
            new WorkflowMetric(s.AverageCycle is { } average ? Milliseconds(average) : "—", $"平均节拍（近 {Math.Max(1, s.CycleSamples)} 次）"),
            new WorkflowMetric(s.LastCycle is { } last ? Milliseconds(last) : "—", "最近一圈"),
            new WorkflowMetric(s.Laps.ToString("N0", CultureInfo.InvariantCulture), "圈数"),
            new WorkflowMetric(s.Failed.ToString("N0", CultureInfo.InvariantCulture), "出错", s.Failed > 0 ? theme.Error : null)
        }, note);
        HeaderOf(_canvasCard).Subtitle = _navigator?.RootSession.RuntimeSnapshot is { } snapshot ? $"只读 · 快照 #{snapshot.Sequence}" : "只读";

        var facts = new List<WorkflowFact> { WorkflowFact.Group("本次节点耗时") };
        var timings = Monitor.TimingTrends;
        var slowest = timings.Count == 0 ? null : timings.OrderByDescending(item => item.Elapsed).First();
        foreach (var item in timings.Take(8))
            facts.Add(new WorkflowFact(NodeTitle(item.NodeId), item.ExecutionCount == 0 ? "—" : WorkflowDesignerInteraction.FormatElapsed(item.Elapsed),
                Strong: ReferenceEquals(item, slowest)));
        if (timings.Count == 0) facts.Add(new WorkflowFact("还没有节点执行"));
        facts.Add(WorkflowFact.Group("界面同步", s.DisplayLatency is null ? string.Empty : "正常"));
        facts.Add(new WorkflowFact("拉取周期", $"{interval} ms"));
        facts.Add(new WorkflowFact("最新快照", s.LastSequence < 0 ? "—" : "#" + s.LastSequence.ToString(CultureInfo.InvariantCulture)));
        facts.Add(new WorkflowFact("显示延迟", s.DisplayLatency is { } latency ? Milliseconds(latency) : "—"));
        facts.Add(new WorkflowFact("执行线程等界面", "从不"));
        _timings.Show(facts);
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
