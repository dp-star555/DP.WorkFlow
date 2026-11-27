using System.Drawing.Drawing2D;
using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 故障处理（运行页右栏，按窄栏竖排）：上面一条故障横幅和概况，中间是各流程停在哪里（画布 / 节点 @ 圈次 [分支]、能否就地恢复），
/// 下面是所选停点所在流程的去向；处理流程没处理完时在这里逐个流程选去向放行、重新运行处理流程或全部停止。
/// 顶部的“查看”可以切到最近结束的会话。
/// </summary>
/// <remarks>
/// 去向是否允许由停点判断，不允许的去向显示为灰色并写明原因。工作台不懂业务：设备是否安全、物料怎么处理由处理流程和业务系统负责。
/// </remarks>
public sealed class WorkflowFaultWorkbenchControl : UserControl
{
    private const string Current = "当前故障";
    private readonly ModernSelect _view = new();
    private readonly WorkflowBanner _banner = new() { Dock = DockStyle.Top };
    private readonly WorkflowFactList _facts = new() { Dock = DockStyle.Top };
    private readonly Label _stopsTitle = Heading("停在哪里");
    private readonly WorkflowItemList _stops = new() { Dock = DockStyle.Top, EmptyText = "没有停下的流程" };
    private readonly Label _optionsTitle = Heading("去向");
    private readonly WorkflowItemList _options = new() { RowHeight = 0, EmptyText = "先在上面选一个停点" };
    private readonly Label _hint = new() { Dock = DockStyle.Bottom, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ModernButton _release = new() { Text = "按所选去向放行", ButtonType = ModernButtonType.Primary, Dock = DockStyle.Top };
    private readonly ModernButton _rerun = new() { Text = "重新运行处理流程", Dock = DockStyle.Top };
    private readonly ModernButton _stopAll = new() { Text = "全部停止", ButtonType = ModernButtonType.DangerOutline, Dock = DockStyle.Top };
    private readonly WorkflowBlock _actions = new() { Dock = DockStyle.Bottom };
    private readonly Dictionary<string, WorkflowResumeDirective> _choices = new(StringComparer.Ordinal);
    private IReadOnlyList<WorkflowFaultSession> _history = Array.Empty<WorkflowFaultSession>();
    private WorkflowFaultWorkbenchModel? _model;
    private WorkflowFaultSessionView? _shown;
    private string _signature = string.Empty;
    private bool _rebuilding;

    /// <summary>初始化故障处理面板。</summary>
    public WorkflowFaultWorkbenchControl()
    {
        int S(int value) => LogicalToDeviceUnits(value);
        Padding = new Padding(S(12), S(10), S(12), S(10));
        _view.Dock = DockStyle.Top;
        _view.Height = S(30);
        _view.Items.Add(Current);
        _view.SelectedIndex = 0;
        _stops.Height = S(104);
        _options.Dock = DockStyle.Fill;
        _hint.Height = S(26);
        _stopsTitle.Height = _optionsTitle.Height = S(30);
        foreach (var button in new[] { _stopAll, _rerun, _release })
        {
            button.Height = S(32);
            _actions.Controls.Add(button);
            _actions.Controls.Add(new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 8 });
        }
        _actions.LogicalExtent = 3 * 40;

        Controls.Add(_options);
        Controls.Add(_optionsTitle);
        Controls.Add(_stops);
        Controls.Add(_stopsTitle);
        Controls.Add(_facts);
        Controls.Add(new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 8 });
        Controls.Add(_banner);
        Controls.Add(new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 10 });
        Controls.Add(_view);
        Controls.Add(_hint);
        Controls.Add(_actions);

        _stops.SelectionChanged += (_, _) => { if (!_rebuilding) ShowOptions(); };
        _options.SelectionChanged += (_, _) => ChooseOption();
        _view.SelectedIndexChanged += (_, _) => { if (!_rebuilding) { _signature = string.Empty; RefreshView(); } };
        _release.Click += (_, _) =>
        {
            var choices = new Dictionary<string, WorkflowResumeDirective>(_choices, StringComparer.Ordinal);
            var detail = string.Join("；", choices.Select(pair => pair.Key + "：" + pair.Value.DisplayName));
            Gated(new WorkflowOperation(WorkflowOperationKind.ReleaseFault, "*", detail), () => _model?.Release(choices, ConfirmManualAsync));
        };
        _rerun.Click += (_, _) =>
        {
            if (MessageBox.Show(FindForm(), "处理流程会从头再跑一遍。已经回退过的动作不会再回退；请确认现场可以重新处理。",
                    "重新运行处理流程", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Gated(new WorkflowOperation(WorkflowOperationKind.RerunFaultHandler, "*"), () => _model?.RerunHandler());
        };
        _stopAll.Click += (_, _) =>
        {
            if (MessageBox.Show(FindForm(), "停止全部受影响的流程？", "全部停止", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                Gated(new WorkflowOperation(WorkflowOperationKind.StopFaultedFlows, "*"), () => _model?.StopAll());
        };

        WorkflowRefreshClock.Subscribe(this, RefreshView);
        WorkflowWinFormsTheme.Follow(this, () => { _signature = string.Empty; RefreshView(); });
        RefreshView();
    }

    /// <summary>获取或设置模型；面板不负责释放模型。</summary>
    public WorkflowFaultWorkbenchModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value)) return;
            if (_model is not null) _model.Changed -= OnModelChanged;
            _model = value;
            if (_model is not null) _model.Changed += OnModelChanged;
            _choices.Clear();
            _signature = string.Empty;
            RefreshView();
        }
    }

    /// <summary>操作准入（宿主的鉴权和审计）；为空时直接执行。</summary>
    public IWorkflowOperationGate? Gate { get; set; }

    /// <summary>是否允许操作（放行、重新运行、全部停止）；宿主按权限设置。默认允许。</summary>
    public bool CanOperate
    {
        get => _canOperate;
        set
        {
            if (_canOperate == value) return;
            _canOperate = value;
            UpdateActions();
        }
    }

    private bool _canOperate = true;

    /// <summary>当前是否有故障会话（在看当前会话时）。</summary>
    public bool HasCurrentFault => _model?.GetCurrent() is not null;

    /// <summary>出现等待操作员选择的兜底询问时发生（界面线程），宿主可以据此切到这里。</summary>
    public event EventHandler? DecisionRequested;

    /// <summary>操作出错时发生（例如去向不被允许），由宿主显示。</summary>
    public event EventHandler<string>? ActionError;

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            var wasWaiting = _shown?.Fallback is not null;
            _signature = string.Empty;
            RefreshView();
            if (!wasWaiting && _shown?.Fallback is not null) DecisionRequested?.Invoke(this, EventArgs.Empty);
        }));
    }

    private void RefreshView()
    {
        if (IsDisposed) return;
        RebuildViewChoices();
        var view = SelectedView();
        var signature = Signature(view);
        if (signature == _signature) return;
        _signature = signature;
        if (view?.Session.Id != _shown?.Session.Id) _choices.Clear();
        _shown = view;
        if (view is null)
        {
            _banner.Show(WorkflowTone.Success, _model is null ? "未接入流程管理" : "没有故障",
                _model is null ? string.Empty : "流程出故障或业务系统上报故障时，这里显示各流程停在哪里和可选的去向。");
            _facts.Show(Array.Empty<WorkflowFact>());
            _facts.Height = 0;
        }
        else
        {
            var session = view.Session;
            var fault = session.PrimaryFault;
            var tone = view.Fallback is not null ? WorkflowTone.Error : session.State == WorkflowFaultSessionState.Ended ? WorkflowTone.Neutral : WorkflowTone.Warning;
            _banner.Show(tone, view.StateText, fault.Message + (session.Faults.Count > 1 ? $"（另有 {session.Faults.Count - 1} 条上报）" : string.Empty));
            var theme = WorkflowWinFormsTheme.Modern;
            var facts = new List<WorkflowFact> { new("开始", session.StartedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)) };
            if (!string.IsNullOrEmpty(fault.Source)) facts.Add(new WorkflowFact("来源", fault.Source));
            if (fault.AlarmCode != 0) facts.Add(new WorkflowFact("报警", fault.AlarmCode.ToString(CultureInfo.InvariantCulture)));
            facts.Add(new WorkflowFact("故障条件", session.ConditionsCleared ? "已消除" : "未消除", ValueColor: session.ConditionsCleared ? theme.Success : theme.Warning));
            facts.Add(new WorkflowFact("受影响", string.Join("、", session.AffectedFlows)));
            if (view.Fallback is not null) facts.Add(new WorkflowFact("没处理完", view.Fallback.Failure, ValueColor: theme.Error));
            else if (!string.IsNullOrEmpty(session.LastFailure)) facts.Add(new WorkflowFact("上次没处理完", session.LastFailure!));
            _facts.Show(facts);
            _facts.Height = LogicalToDeviceUnits(28 * facts.Count + 8);
        }
        RebuildStops(view);
        UpdateActions();
    }

    private void RebuildViewChoices()
    {
        var history = _model?.History ?? Array.Empty<WorkflowFaultSession>();
        if (history.Select(session => session.Id).SequenceEqual(_history.Select(session => session.Id))) return;
        var selectedId = _view.SelectedIndex > 0 && _view.SelectedIndex - 1 < _history.Count ? _history[_view.SelectedIndex - 1].Id : (Guid?)null;
        _history = history.ToArray();
        _rebuilding = true;
        try
        {
            _view.Items.Clear();
            _view.Items.Add(Current);
            foreach (var session in _history)
                _view.Items.Add(session.StartedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + session.PrimaryFault.Message);
            var index = selectedId is null ? -1 : _history.ToList().FindIndex(session => session.Id == selectedId);
            _view.SelectedIndex = index < 0 ? 0 : index + 1;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private WorkflowFaultSession? HistorySession => _view.SelectedIndex > 0 && _view.SelectedIndex - 1 < _history.Count ? _history[_view.SelectedIndex - 1] : null;

    private WorkflowFaultSessionView? SelectedView() =>
        _model is null ? null
        : HistorySession is { } session ? _model.Describe(session)
        : _model.GetCurrent();

    private static string Signature(WorkflowFaultSessionView? view) => view is null
        ? "-"
        : string.Join("|", view.Session.Id, view.Session.State, view.Session.Attempt, view.Fallback is null ? 0 : 1,
            view.Session.Faults.Count, view.Session.ConditionsCleared,
            string.Join(",", view.Flows.Select(flow => flow.FlowName + ":" + flow.StopPoints.Count + ":" + flow.Directive?.DisplayName)));

    private void RebuildStops(WorkflowFaultSessionView? view)
    {
        var stops = (view?.Flows ?? Array.Empty<WorkflowFaultFlowView>())
            .SelectMany(flow => flow.StopPoints.Select(stop => (Flow: flow, Stop: stop)))
            .ToArray();
        _stopsBy = stops.ToDictionary(item => item.Stop.Id, item => (item.Flow, item.Stop));
        var selected = _stops.SelectedKey;
        _rebuilding = true;
        try
        {
            _stops.Show(stops.Select(item => new WorkflowListItem(
                item.Stop.Id,
                $"{item.Flow.FlowName} · {item.Stop.NodeTitle}",
                item.Stop.Position + (item.Stop.CanRecoverInPlace ? " · 可就地恢复" : string.Empty)
                + (item.Stop.Rejection is null ? string.Empty : " · 上次去向被拒：" + item.Stop.Rejection),
                Badge: ReasonText(item.Stop.Reason),
                BadgeTone: item.Stop.Reason == WorkflowStopReason.Requested ? WorkflowTone.Neutral : WorkflowTone.Error,
                SubtitleTone: item.Stop.Rejection is null ? WorkflowTone.Neutral : WorkflowTone.Error)));
            _stops.Height = LogicalToDeviceUnits(52 * Math.Max(1, Math.Min(3, stops.Length)));
            // 默认选中第一个出故障的停点。
            if (selected is not Guid id || !_stopsBy.ContainsKey(id))
                _stops.SelectedKey = stops.Where(item => item.Stop.Reason != WorkflowStopReason.Requested).Select(item => (object?)item.Stop.Id).FirstOrDefault()
                                     ?? stops.Select(item => (object?)item.Stop.Id).FirstOrDefault();
        }
        finally
        {
            _rebuilding = false;
        }
        ShowOptions();
    }

    private Dictionary<Guid, (WorkflowFaultFlowView Flow, WorkflowStopPoint Stop)> _stopsBy = new();

    private (WorkflowFaultFlowView Flow, WorkflowStopPoint Stop)? SelectedFlowStop =>
        _stops.SelectedKey is Guid id && _stopsBy.TryGetValue(id, out var value) ? value : null;

    private void ShowOptions()
    {
        _rebuilding = true;
        try
        {
            if (SelectedFlowStop is not { } selection)
            {
                _optionsTitle.Text = "去向";
                _options.Show(Array.Empty<WorkflowListItem>());
                return;
            }
            var flow = selection.Flow;
            var deciding = _shown?.Fallback is not null;
            _optionsTitle.Text = deciding
                ? $"{flow.FlowName} 的去向" + (flow.NeedsDirective ? string.Empty : "（不选时照常继续）")
                : $"{flow.FlowName} 的去向：" + (flow.Directive?.DisplayName ?? "处理流程还没给");
            _options.EmptyText = deciding ? "这个停点没有可选的去向" : "去向由处理流程给出；没处理完时才在这里选。";
            _options.Show(flow.Options.Select((option, index) => new WorkflowListItem(index, option.Text,
                option.Allowed ? null : "不能选：" + option.Reason, Enabled: option.Allowed, SubtitleTone: WorkflowTone.Error)));
            var chosen = _choices.TryGetValue(flow.FlowName, out var picked) ? picked : deciding ? null : flow.Directive;
            var chosenIndex = chosen is null ? -1 : flow.Options.ToList().FindIndex(option => SameDirective(option.Directive, chosen));
            _options.SelectedKey = chosenIndex < 0 ? null : chosenIndex;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void ChooseOption()
    {
        if (_rebuilding || _shown?.Fallback is null || SelectedFlowStop is not { } selection || _options.SelectedKey is not int index) return;
        var options = selection.Flow.Options;
        if (index < 0 || index >= options.Count || !options[index].Allowed) return;
        _choices[selection.Flow.FlowName] = options[index].Directive;
        UpdateActions();
    }

    private void UpdateActions()
    {
        var deciding = _shown?.Fallback is not null && HistorySession is null;
        _actions.Visible = deciding;
        _rerun.Enabled = _canOperate && _shown?.Fallback?.HasHandler == true;
        _stopAll.Enabled = _canOperate;
        var missing = deciding
            ? _shown!.Flows.Where(flow => flow.NeedsDirective && !_choices.ContainsKey(flow.FlowName)).Select(flow => flow.FlowName).ToArray()
            : Array.Empty<string>();
        _release.Enabled = _canOperate && deciding && missing.Length == 0;
        _hint.Visible = deciding;
        _hint.Text = missing.Length > 0 ? "还要给这些流程选去向：" + string.Join("、", missing) : "各流程的去向都选好了。";
        _hint.ForeColor = missing.Length > 0 ? WorkflowWinFormsTheme.Modern.Warning : WorkflowWinFormsTheme.Modern.TextSecondary;
    }

    private static Label Heading(string text) => new WorkflowLabel
    {
        Text = text,
        Bold = true,
        Dock = DockStyle.Top,
        Height = 30,
        TextAlign = ContentAlignment.BottomLeft,
        UseMnemonic = false,
        AutoEllipsis = true
    };

    // 经过宿主的准入：拒绝或出错时显示消息；完成后刷新。
    private async void Gated(WorkflowOperation operation, Action action)
    {
        if (Gate is not { } gate)
        {
            Guarded(action);
            return;
        }
        try
        {
            await gate.RunAsync(operation, () => { action(); return Task.CompletedTask; });
            _choices.Clear();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ActionError?.Invoke(this, exception.Message);
        }
        if (IsDisposed) return;
        _signature = string.Empty;
        RefreshView();
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
            _choices.Clear();
        }
        catch (InvalidOperationException exception)
        {
            ActionError?.Invoke(this, exception.Message);
        }
        _signature = string.Empty;
        RefreshView();
    }

    /// <summary>回退到不支持回退的节点时，在界面线程上问操作员是否已人工处理好。</summary>
    private Task<bool> ConfirmManualAsync(WorkflowExecutionRecordEntry entry, CancellationToken cancellationToken)
    {
        if (IsDisposed)
            return Task.FromResult(false);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => answer.TrySetCanceled());
        _ = answer.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        BeginInvoke(new Action(() => answer.TrySetResult(MessageBox.Show(FindForm(),
            $"[{entry.FlowName}] “{entry.NodeTitle}”（{entry.Position}）不支持回退。\n请人工处理好后点“确定”；点“取消”则停止回退，回到工作台重新选。",
            "回退再继续", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)));
        return answer.Task;
    }

    private static bool SameDirective(WorkflowResumeDirective left, WorkflowResumeDirective right) =>
        left.Kind == right.Kind && string.Equals(left.EntryKey, right.EntryKey, StringComparison.Ordinal);

    private static string ReasonText(WorkflowStopReason reason) => reason switch
    {
        WorkflowStopReason.Fault => "故障",
        WorkflowStopReason.Interrupted => "被中断",
        _ => "被要求停下"
    };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _model is not null) _model.Changed -= OnModelChanged;
        base.Dispose(disposing);
    }
}

/// <summary>
/// 提示条（故障横幅等）：浅色底、粗体标题、正文自动换行，高度跟着宽度和文字变。
/// </summary>
public sealed class WorkflowBanner : Control
{
    private WorkflowTone _tone;
    private string _title = string.Empty;
    private string _message = string.Empty;

    /// <summary>创建提示条。</summary>
    public WorkflowBanner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    /// <summary>当前标题。</summary>
    public string Title => _title;

    /// <summary>换内容。</summary>
    public void Show(WorkflowTone tone, string title, string message)
    {
        if (_tone == tone && _title == title && _message == message) return;
        _tone = tone;
        _title = title;
        _message = message;
        Measure();
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Measure();
    }

    private void Measure()
    {
        int S(int value) => LogicalToDeviceUnits(value);
        using var bold = new Font(Font, FontStyle.Bold);
        var width = Math.Max(S(40), Width - S(24));
        var message = string.IsNullOrEmpty(_message) ? 0
            : TextRenderer.MeasureText(_message, Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + S(2);
        var height = S(20) + bold.Height + message;
        if (Height != height) Height = height;
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        int S(int value) => LogicalToDeviceUnits(value);
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = WorkflowCard.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), S(WorkflowWinFormsTheme.Palette.Radius)))
        using (var fill = new SolidBrush(WorkflowTones.Back(_tone)))
            e.Graphics.FillPath(fill, path);
        using var bold = new Font(Font, FontStyle.Bold);
        var fore = _tone == WorkflowTone.Neutral ? WorkflowWinFormsTheme.Modern.Text : WorkflowTones.Fore(_tone);
        TextRenderer.DrawText(e.Graphics, _title, bold, new Rectangle(S(12), S(10), Width - S(24), bold.Height), fore,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrEmpty(_message))
            TextRenderer.DrawText(e.Graphics, _message, Font, new Rectangle(S(12), S(10) + bold.Height + S(2), Width - S(24), Height - bold.Height - S(12)), fore,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
    }
}
