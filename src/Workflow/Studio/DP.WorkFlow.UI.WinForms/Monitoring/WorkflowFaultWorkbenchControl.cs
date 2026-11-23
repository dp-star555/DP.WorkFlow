using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 故障工作台（第 8 类）：当前故障会话的上报、受影响的流程、每个流程停在哪里（画布 / 节点 @ 圈次 [分支]）、
/// 节点恢复类别和最后一步；处理流程没处理完时在这里逐个流程选去向放行、重新运行处理流程或全部停止。
/// 也可以查看最近结束的会话。
/// </summary>
/// <remarks>
/// 去向是否允许由停点判断，不允许的去向显示为灰色并写明原因。工作台不懂业务：设备是否安全、物料怎么处理由处理流程和业务系统负责。
/// </remarks>
public sealed class WorkflowFaultWorkbenchControl : UserControl
{
    private readonly Label _state = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 4, 0, 0) };
    private readonly Label _details = new() { AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 4, 0, 0) };
    private readonly ComboBox _view = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ListView _stops = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
        ShowItemToolTips = true
    };
    private readonly Label _optionsTitle = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 6) };
    private readonly ListBox _options = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false };
    private readonly Label _optionReason = new() { AutoSize = true, Dock = DockStyle.Bottom, MaximumSize = new Size(320, 0), Padding = new Padding(0, 6, 0, 0) };
    private readonly ModernButton _release = new() { Text = "按所选去向放行", ButtonType = ModernButtonType.Primary, AutoSize = true };
    private readonly ModernButton _rerun = new() { Text = "重新运行处理流程", AutoSize = true };
    private readonly ModernButton _stopAll = new() { Text = "全部停止", ButtonType = ModernButtonType.Danger, AutoSize = true };
    private readonly Label _hint = new() { AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
    private readonly Dictionary<string, WorkflowResumeDirective> _choices = new(StringComparer.Ordinal);
    private WorkflowFaultWorkbenchModel? _model;
    private WorkflowFaultSessionView? _shown;
    private string _signature = string.Empty;
    private bool _rebuilding;

    /// <summary>初始化工作台。</summary>
    public WorkflowFaultWorkbenchControl()
    {
        _stops.Columns.Add("流程", 120);
        _stops.Columns.Add("位置", 260);
        _stops.Columns.Add("节点", 140);
        _stops.Columns.Add("原因", 70);
        _stops.Columns.Add("恢复类别", 100);
        _stops.Columns.Add("最后一步", 160);
        _stops.Columns.Add("说明", 300);
        _stops.SelectedIndexChanged += (_, _) => ShowOptions();
        _options.ItemHeight = 26;
        _options.DrawItem += DrawOption;
        _options.SelectedIndexChanged += (_, _) => ChooseOption();
        _view.SelectedIndexChanged += (_, _) => { if (!_rebuilding) { _signature = string.Empty; RefreshView(); } };
        _release.Click += (_, _) =>
        {
            var choices = new Dictionary<string, WorkflowResumeDirective>(_choices, StringComparer.Ordinal);
            var detail = string.Join("；", choices.Select(pair => pair.Key + "：" + pair.Value.DisplayName));
            Gated(new WorkflowOperation(WorkflowOperationKind.ReleaseFault, "*", detail), () => _model?.Release(choices));
        };
        _rerun.Click += (_, _) =>
        {
            if (MessageBox.Show(FindForm(), "处理流程会从头再跑一遍。已经撤回过的动作不会再撤回；请确认现场可以重新处理。",
                    "重新运行处理流程", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Gated(new WorkflowOperation(WorkflowOperationKind.RerunFaultHandler, "*"), () => _model?.RerunHandler());
        };
        _stopAll.Click += (_, _) =>
        {
            if (MessageBox.Show(FindForm(), "停止全部受影响的流程？", "全部停止", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                Gated(new WorkflowOperation(WorkflowOperationKind.StopFaultedFlows, "*"), () => _model?.StopAll());
        };

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 0, 0, 8) };
        var titleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        titleRow.Controls.Add(_state);
        titleRow.Controls.Add(new Label { Text = "查看", AutoSize = true, Margin = new Padding(24, 4, 6, 0) });
        titleRow.Controls.Add(_view);
        header.Controls.Add(titleRow);
        header.Controls.Add(_message);
        header.Controls.Add(_details);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        right.Controls.Add(_options);
        right.Controls.Add(_optionReason);
        right.Controls.Add(_optionsTitle);
        var split = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, InitialPanel2Size = 300 };
        split.Panel1.Controls.Add(_stops);
        split.Panel2.Controls.Add(right);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
        actions.Controls.AddRange(new Control[] { _hint, _release, _rerun, _stopAll });

        Controls.Add(split);
        Controls.Add(actions);
        Controls.Add(header);

        WorkflowRefreshClock.Subscribe(this, RefreshView);
        WorkflowWinFormsTheme.Follow(this, () => { _signature = string.Empty; RefreshView(); });
        RefreshView();
    }

    /// <summary>获取或设置模型；工作台不负责释放模型。</summary>
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

    /// <summary>出现等待操作员选择的兜底询问时发生（界面线程），宿主可以据此切到工作台。</summary>
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
        var theme = WorkflowWinFormsTheme.Modern;
        if (view is null)
        {
            _state.Text = _model is null ? "未接入流程管理" : "没有故障";
            _state.ForeColor = theme.Success;
            _message.Text = _model is null ? string.Empty : "流程出故障或业务系统上报故障时，这里显示各流程停在哪里，以及处理流程给出的去向。";
            _message.ForeColor = theme.TextSecondary;
            _details.Text = string.Empty;
        }
        else
        {
            var session = view.Session;
            var fault = session.PrimaryFault;
            _state.Text = view.StateText;
            _state.ForeColor = view.Fallback is not null ? theme.Error : session.State == WorkflowFaultSessionState.Ended ? theme.TextSecondary : theme.Warning;
            _message.Text = fault.Message + (session.Faults.Count > 1 ? $"（另有 {session.Faults.Count - 1} 条上报）" : string.Empty);
            _message.ForeColor = theme.Text;
            var parts = new List<string> { "开始 " + session.StartedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) };
            if (!string.IsNullOrEmpty(fault.Source)) parts.Add("来源 " + fault.Source);
            if (fault.AlarmCode != 0) parts.Add("报警 " + fault.AlarmCode.ToString(CultureInfo.InvariantCulture));
            parts.Add(session.ConditionsCleared ? "故障条件已消除" : "故障条件未消除");
            parts.Add("受影响 " + string.Join("、", session.AffectedFlows));
            if (view.Fallback is not null) parts.Add("没处理完的原因：" + view.Fallback.Failure);
            else if (!string.IsNullOrEmpty(session.LastFailure)) parts.Add("上次没处理完：" + session.LastFailure);
            _details.Text = string.Join("　·　", parts);
            _details.ForeColor = view.Fallback is not null ? theme.Error : theme.TextSecondary;
        }
        RebuildStops(view);
        UpdateActions();
    }

    private void RebuildViewChoices()
    {
        var history = _model?.History ?? Array.Empty<WorkflowFaultSession>();
        var wanted = new List<object> { "当前" };
        wanted.AddRange(history.Select(session => (object)new HistoryChoice(session)));
        if (_view.Items.Count == wanted.Count && _view.Items.Cast<object>().Skip(1).Select(item => ((HistoryChoice)item).Session.Id)
                .SequenceEqual(history.Select(session => session.Id)))
            return;
        var selectedId = (_view.SelectedItem as HistoryChoice)?.Session.Id;
        _rebuilding = true;
        try
        {
            _view.Items.Clear();
            _view.Items.AddRange(wanted.ToArray());
            var index = selectedId is null ? 0 : Math.Max(0, wanted.FindIndex(item => item is HistoryChoice choice && choice.Session.Id == selectedId));
            _view.SelectedIndex = index;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private WorkflowFaultSessionView? SelectedView() =>
        _model is null ? null
        : _view.SelectedItem is HistoryChoice choice ? _model.Describe(choice.Session)
        : _model.GetCurrent();

    private static string Signature(WorkflowFaultSessionView? view) => view is null
        ? "-"
        : string.Join("|", view.Session.Id, view.Session.State, view.Session.Attempt, view.Fallback is null ? 0 : 1,
            view.Session.Faults.Count, view.Session.ConditionsCleared,
            string.Join(",", view.Flows.Select(flow => flow.FlowName + ":" + flow.StopPoints.Count + ":" + flow.Directive?.DisplayName)));

    private void RebuildStops(WorkflowFaultSessionView? view)
    {
        var theme = WorkflowWinFormsTheme.Modern;
        var selected = SelectedStop?.Id;
        _stops.BeginUpdate();
        try
        {
            _stops.Items.Clear();
            foreach (var flow in view?.Flows ?? Array.Empty<WorkflowFaultFlowView>())
            foreach (var stop in flow.StopPoints)
            {
                var item = new ListViewItem(flow.FlowName) { Tag = (flow, stop), ToolTipText = stop.Message };
                item.SubItems.Add(stop.Position.ToString());
                item.SubItems.Add(stop.NodeTitle);
                item.SubItems.Add(ReasonText(stop.Reason));
                item.SubItems.Add(WorkflowRecoveryCategories.DisplayName(stop.Category));
                item.SubItems.Add(stop.Progress ?? string.Empty);
                item.SubItems.Add(stop.Rejection is null ? stop.Message ?? string.Empty : $"{stop.Message}（上次去向被拒：{stop.Rejection}）");
                item.ForeColor = stop.Reason == WorkflowStopReason.Requested ? theme.TextSecondary : theme.Text;
                _stops.Items.Add(item);
                if (stop.Id == selected) item.Selected = true;
            }
            if (_stops.SelectedItems.Count == 0)
            {
                // 默认选中第一个出故障的停点。
                var first = _stops.Items.Cast<ListViewItem>().FirstOrDefault(item => ((ValueTuple<WorkflowFaultFlowView, WorkflowStopPoint>)item.Tag!).Item2.Reason != WorkflowStopReason.Requested)
                            ?? _stops.Items.Cast<ListViewItem>().FirstOrDefault();
                if (first is not null) first.Selected = true;
            }
        }
        finally
        {
            _stops.EndUpdate();
        }
        ShowOptions();
    }

    private WorkflowStopPoint? SelectedStop => SelectedFlowStop?.Stop;

    private (WorkflowFaultFlowView Flow, WorkflowStopPoint Stop)? SelectedFlowStop =>
        _stops.SelectedItems.Count == 1 && _stops.SelectedItems[0].Tag is ValueTuple<WorkflowFaultFlowView, WorkflowStopPoint> tag ? tag : null;

    private void ShowOptions()
    {
        var selection = SelectedFlowStop;
        _rebuilding = true;
        try
        {
            _options.Items.Clear();
            if (selection is not { } value)
            {
                _optionsTitle.Text = "去向";
                _optionReason.Text = string.Empty;
                return;
            }
            var flow = value.Flow;
            var deciding = _shown?.Fallback is not null;
            _optionsTitle.Text = deciding
                ? $"流程 {flow.FlowName} 的去向" + (flow.NeedsDirective ? string.Empty : "（只被要求停下，不选时照常继续）")
                : $"流程 {flow.FlowName} 的去向：" + (flow.Directive?.DisplayName ?? "处理流程还没给");
            foreach (var option in flow.Options) _options.Items.Add(option);
            var chosen = _choices.TryGetValue(flow.FlowName, out var picked) ? picked : deciding ? null : flow.Directive;
            if (chosen is not null)
                _options.SelectedIndex = flow.Options.ToList().FindIndex(option => SameDirective(option.Directive, chosen));
            _options.Enabled = true;
            UpdateOptionReason();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void ChooseOption()
    {
        if (_rebuilding) return;
        UpdateOptionReason();
        if (_shown?.Fallback is null || SelectedFlowStop is not { } selection || _options.SelectedItem is not WorkflowFaultDirectiveOption option) return;
        if (!option.Allowed) return;
        _choices[selection.Flow.FlowName] = option.Directive;
        UpdateActions();
    }

    private void UpdateOptionReason()
    {
        var theme = WorkflowWinFormsTheme.Modern;
        if (_options.SelectedItem is WorkflowFaultDirectiveOption { Allowed: false } option)
        {
            _optionReason.Text = "不能选：" + option.Reason;
            _optionReason.ForeColor = theme.Error;
        }
        else
        {
            _optionReason.Text = _shown?.Fallback is null ? "去向由处理流程给出；处理流程没处理完时才在这里选择。" : string.Empty;
            _optionReason.ForeColor = theme.TextSecondary;
        }
    }

    private void UpdateActions()
    {
        var deciding = _shown?.Fallback is not null && _view.SelectedItem is not HistoryChoice;
        _release.Visible = _rerun.Visible = _stopAll.Visible = deciding;
        _rerun.Enabled = _canOperate && _shown?.Fallback?.HasHandler == true;
        _stopAll.Enabled = _canOperate;
        var missing = deciding
            ? _shown!.Flows.Where(flow => flow.NeedsDirective && !_choices.ContainsKey(flow.FlowName)).Select(flow => flow.FlowName).ToArray()
            : Array.Empty<string>();
        _release.Enabled = _canOperate && deciding && missing.Length == 0;
        _hint.Text = !deciding ? string.Empty
            : missing.Length > 0 ? "还要给这些流程选去向：" + string.Join("、", missing)
            : "各流程的去向都选好了。";
        _hint.ForeColor = missing.Length > 0 ? WorkflowWinFormsTheme.Modern.Warning : WorkflowWinFormsTheme.Modern.TextSecondary;
    }

    private void DrawOption(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _options.Items.Count) return;
        var option = (WorkflowFaultDirectiveOption)_options.Items[e.Index];
        var theme = WorkflowWinFormsTheme.Modern;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? theme.PrimaryBackground : _options.BackColor))
            e.Graphics.FillRectangle(back, e.Bounds);
        var color = !option.Allowed ? theme.TextDisabled : selected ? theme.Primary : theme.Text;
        var text = option.Allowed ? option.Text : option.Text + "（不可用）";
        TextRenderer.DrawText(e.Graphics, text, _options.Font, Rectangle.Inflate(e.Bounds, -8, 0), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

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

    private sealed class HistoryChoice(WorkflowFaultSession session)
    {
        public WorkflowFaultSession Session { get; } = session;

        public override string ToString() =>
            Session.StartedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + Session.PrimaryFault.Message;
    }
}
