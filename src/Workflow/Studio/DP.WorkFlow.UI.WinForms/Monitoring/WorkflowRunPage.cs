using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 运行页（原“运行总览”和“运行监视”合成一页，按设计稿“运行”画板）：
/// 左边流程列表（状态、提示、单独启停），中间是所选流程的指标条和只读画布，
/// 右边“故障处理”只在有故障时出来；节点耗时是指标条上的小工具，点一下看当时的最新数据，不实时刷新。轨迹等明细看运行日志页。
/// </summary>
/// <remarks>
/// 画布挂在流程自己的运行宿主上，按界面节拍拉快照（<see cref="WorkflowStudioRuntimeBinding"/>），不拖慢执行线程。
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
    private readonly ModernButton _timingButton = new() { Text = "节点耗时", ButtonType = ModernButtonType.Text, Icon = ModernIconKind.Info };
    private readonly WorkflowMetricStrip _figures = new();
    private readonly WorkflowCard _faultCard;
    private readonly WorkflowBlock _faultGap = Gap(DockStyle.Right);
    private WorkflowTimingWindow? _timingWindow;
    private readonly WorkflowCard _canvasCard;
    private readonly WorkflowStatusLine _status = new();
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill };
    private readonly Label _noCanvas = Label("取不到这个流程的文档，不显示画布。");
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
        Workbench = new WorkflowFaultWorkbenchControl { Dock = DockStyle.Fill, Model = workbench };

        // 左：流程卡 | 中：指标条、画布卡 | 右：故障处理卡（有故障才出来）；最下状态行。卡片之间隔 10。
        _flowsCard = Section("流程", null, _flows, DockStyle.Left, 260);
        _figures.AddTool(_timingButton);
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

        _faultCard = Section("故障处理", null, Workbench, DockStyle.Right, 320);
        _faultCard.Visible = _faultGap.Visible = false;

        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(column);
        middle.Controls.Add(Gap(DockStyle.Left));
        middle.Controls.Add(_flowsCard);
        middle.Controls.Add(_faultGap);
        middle.Controls.Add(_faultCard);

        Controls.Add(middle);
        Controls.Add(_status);

        _flows.SelectionChanged += (_, _) => Bind(_flows.SelectedKey as string);
        _flows.ActionClicked += (_, item) => StartOrStop((string)item.Key);
        _resetStatistics.Click += (_, _) => { _statistics.Reset(); _observedSequence = -1; ShowFigures(); };
        Workbench.ActionError += (_, message) => ShowMessage(message);
        Workbench.DecisionRequested += (_, _) => ShowFaultCard(true);
        _timingButton.Click += (_, _) => ShowTimings();
        // 统计和数字跟统一刷新时钟走：每帧看一眼画布会话上的最新快照，序号变了才计数。
        WorkflowRefreshClock.Subscribe(this, OnFrame);
        RefreshFlows();
    }

    private readonly WorkflowFaultWorkbenchModel? _ownedWorkbench;

    /// <summary>只读画布。</summary>
    public WorkflowDesignerControl Canvas { get; }

    /// <summary>右栏的故障处理（有故障时才显示）。</summary>
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
            _timingWindow?.Dispose();
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
                // 没有操作权限的人不显示单独启停按钮（不是灰掉）。
                Action: !CanOperate ? ModernIconKind.None : active ? ModernIconKind.Stop : ModernIconKind.Play,
                ActionText: active ? "停止这个流程" : "启动这个流程",
                SubtitleTone: tone is WorkflowTone.Error or WorkflowTone.Warning ? tone : WorkflowTone.Neutral);
        }));
        var faults = rows.Count(row => row.InFault || row.State == E_WorkflowExecutionState.Faulted);
        HeaderOf(_flowsCard).Subtitle = rows.Count == 0 ? null : faults > 0 ? $"{rows.Count} 个 · {faults} 个故障" : $"{rows.Count} 个";
        if (_flows.SelectedKey is null && rows.Count > 0)
            _flows.SelectedKey = (rows.FirstOrDefault(row => row.InFault) ?? rows[0]).Name;

        // 出故障时右边出来故障处理，处理完收起，画布变宽。
        var hasFault = Workbench.HasCurrentFault;
        if (hasFault != _hadFault) ShowFaultCard(hasFault);
        _hadFault = hasFault;
    }

    private void ShowFaultCard(bool visible)
    {
        if (_faultCard.Visible == visible) return;
        _faultCard.Visible = _faultGap.Visible = visible;
        BeginInvokeFit();
    }

    // 节点耗时：打开时取一次当时的数据，要看最新的点“刷新”；不跟着运行实时刷新。
    private void ShowTimings()
    {
        if (_timingWindow is null || _timingWindow.IsDisposed)
        {
            _timingWindow = new WorkflowTimingWindow(TimingFacts);
            _timingWindow.Show(FindForm());
        }
        else
        {
            _timingWindow.Activate();
        }
        _timingWindow.Reload();
    }

    private (string Title, IReadOnlyList<WorkflowFact> Facts) TimingFacts()
    {
        var interval = _binding?.FrameIntervalMilliseconds ?? WorkflowUiRefresh.FrameIntervalMilliseconds;
        var s = _statistics;
        var snapshot = _navigator?.RootSession.RuntimeSnapshot;
        var facts = new List<WorkflowFact> { WorkflowFact.Group("本次节点耗时") };
        var timings = snapshot?.Nodes.Values
            .Where(node => node.ExecutionCount > 0)
            .OrderByDescending(node => node.Elapsed)
            .ToArray() ?? Array.Empty<WorkflowNodeRuntimeInfo>();
        for (var index = 0; index < timings.Length; index++)
            facts.Add(new WorkflowFact(NodeTitle(timings[index].NodeId), WorkflowDesignerInteraction.FormatElapsed(timings[index].Elapsed), Strong: index == 0));
        if (timings.Length == 0) facts.Add(new WorkflowFact(_flow is null ? "未选择流程" : "还没有节点执行"));
        facts.Add(WorkflowFact.Group("界面同步", s.DisplayLatency is null ? string.Empty : "正常"));
        facts.Add(new WorkflowFact("拉取周期", $"{interval} ms"));
        facts.Add(new WorkflowFact("最新快照", s.LastSequence < 0 ? "—" : "#" + s.LastSequence.ToString(CultureInfo.InvariantCulture)));
        facts.Add(new WorkflowFact("显示延迟", s.DisplayLatency is { } latency ? Milliseconds(latency) : "—"));
        facts.Add(new WorkflowFact("执行线程等界面", "从不"));
        var title = _flow is null ? "节点耗时" : $"节点耗时 · {_flow.Name} · {DateTime.Now:HH:mm:ss}";
        return (title, facts);
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
        // 单独停止前确认，免得误点停掉正在跑的流程。
        if (active && MessageBox.Show(FindForm(), $"确定停止流程“{name}”？", "停止流程", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
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

/// <summary>节点耗时小窗：不实时刷新，点“刷新”取当时最新的数据。</summary>
internal sealed class WorkflowTimingWindow : Form
{
    private readonly Func<(string Title, IReadOnlyList<WorkflowFact> Facts)> _read;
    private readonly WorkflowFactList _facts = new();

    public WorkflowTimingWindow(Func<(string Title, IReadOnlyList<WorkflowFact> Facts)> read)
    {
        _read = read;
        Text = "节点耗时";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(380, 520);
        MinimumSize = new Size(300, 300);
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        Padding = new Padding(10);
        var refresh = WorkflowSizing.Fit(new ModernButton { Text = "刷新", ButtonType = ModernButtonType.Primary, Icon = ModernIconKind.Refresh });
        refresh.Click += (_, _) => Reload();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(refresh);
        Controls.Add(_facts);
        Controls.Add(buttons);
        WorkflowWinFormsTheme.Follow(this);
    }

    /// <summary>取一次最新的数据。</summary>
    public void Reload()
    {
        var (title, facts) = _read();
        Text = title;
        _facts.Show(facts);
    }
}
