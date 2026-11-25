using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 运行总览页（流程管理）：上面是各流程的状态、恢复轮次、提示条数、记录健康，可以启动 / 停止；
/// 下面左边是选中流程的提示，右边是故障工作台。挂进操作台时作为一个功能页。
/// </summary>
public sealed class WorkflowRunCenterPage : WorkflowPage
{
    private readonly WorkflowRunCenterOverviewModel _overview;
    private readonly ModernDataGridView _flows = Table("流程", "状态", "恢复轮次", "提示", "记录");
    private readonly ModernButton _start = new() { Text = "启动", AutoSize = true, ButtonType = ModernButtonType.Primary };
    private readonly ModernButton _stop = new() { Text = "停止", AutoSize = true };
    private readonly ModernButton _startAll = new() { Text = "全部启动", AutoSize = true };
    private readonly ModernButton _stopAll = new() { Text = "全部停止", AutoSize = true, ButtonType = ModernButtonType.Danger };
    private readonly Label _message = Label();
    private string _shown = string.Empty;

    /// <summary>创建运行总览页。</summary>
    /// <param name="center">流程管理。</param>
    /// <param name="workbench">故障工作台模型；为空时按只显示方式新建一个（不接管兜底界面），页面释放时一起释放。</param>
    public WorkflowRunCenterPage(WorkflowRunCenter center, WorkflowFaultWorkbenchModel? workbench = null)
    {
        Guard.NotNull(center);
        _overview = new WorkflowRunCenterOverviewModel(center);
        Workbench = new WorkflowFaultWorkbenchControl { Model = workbench ?? OwnModel(center) };
        Notices = new WorkflowNoticesControl();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
        buttons.Controls.AddRange(new Control[] { _start, _stop, _startAll, _stopAll });
        _message.Dock = DockStyle.Bottom;
        _message.Height = 24;
        var flowsBody = new Panel();
        flowsBody.Controls.Add(_flows);
        flowsBody.Controls.Add(buttons);
        flowsBody.Controls.Add(_message);

        var layout = Grid(1, new RowStyle(SizeType.Percent, 40), new RowStyle(SizeType.Percent, 60));
        layout.Controls.Add(Card("流程", flowsBody), 0, 0);
        var lower = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, InitialPanel2Size = 640 };
        lower.Panel1.Controls.Add(Card("提示", Notices));
        lower.Panel2.Controls.Add(Card("故障处理", Workbench));
        layout.Controls.Add(lower, 0, 1);
        Controls.Add(layout);

        _flows.SelectionChanged += (_, _) => { Notices.Board = SelectedFlow is { } name ? _overview.GetNotices(name) : null; UpdateCommands(); };
        _start.Click += (_, _) => Act(WorkflowOperationKind.StartFlow, name => { _ = Observe(name, _overview.Start(name)); return Task.CompletedTask; });
        _stop.Click += (_, _) => Act(WorkflowOperationKind.StopFlow, name => _overview.Stop(name));
        _startAll.Click += (_, _) => Run(new WorkflowOperation(WorkflowOperationKind.StartAll, "*"), () =>
        {
            foreach (var task in _overview.StartAll()) _ = Observe(null, task);
            return Task.CompletedTask;
        });
        _stopAll.Click += (_, _) => Run(new WorkflowOperation(WorkflowOperationKind.StopAll, "*"), () => _overview.StopAll());
        Workbench.ActionError += (_, message) => ShowMessage(message);
    }

    /// <summary>故障工作台。</summary>
    public WorkflowFaultWorkbenchControl Workbench { get; }

    /// <summary>
    /// 操作准入（宿主的鉴权和审计），同时交给故障工作台；为空时直接执行。
    /// 单独启动只等命令发出，不等流程跑完。
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

    /// <summary>选中流程的提示。</summary>
    public WorkflowNoticesControl Notices { get; }

    private WorkflowFaultWorkbenchModel? OwnedModel { get; set; }

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
        var rows = _overview.GetRows();
        var signature = string.Join("|", rows.Select(row => $"{row.Name}:{row.StateText}:{row.RecoveryRound}:{row.NoticeCount}:{row.RecordingText}"));
        if (force || signature != _shown)
        {
            _shown = signature;
            var selected = SelectedFlow;
            var theme = WorkflowWinFormsTheme.Modern;
            _flows.Rows.Clear();
            foreach (var row in rows)
            {
                var index = _flows.Rows.Add(row.Name, row.StateText, row.RecoveryRound.ToString(CultureInfo.InvariantCulture),
                    row.NoticeCount == 0 ? string.Empty : row.NoticeCount.ToString(CultureInfo.InvariantCulture), row.RecordingText);
                var grid = _flows.Rows[index];
                grid.Tag = row.Name;
                grid.Cells[1].Style.ForeColor = row.InFault || row.State == E_WorkflowExecutionState.Faulted ? theme.Error
                    : row.State == E_WorkflowExecutionState.Running ? theme.Success
                    : row.State == E_WorkflowExecutionState.Paused ? theme.Warning
                    : theme.TextSecondary;
                if (row.NoticeCount > 0) grid.Cells[3].Style.ForeColor = theme.Warning;
                if (row.RecordingText.Length > 0) grid.Cells[4].Style.ForeColor = theme.Warning;
            }
            RestoreSelection(_flows, selected ?? rows.FirstOrDefault()?.Name, row => row.Tag);
        }
        Workbench.CanOperate = CanOperate;
        UpdateCommands();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) OwnedModel?.Dispose();
        base.Dispose(disposing);
    }

    private WorkflowFaultWorkbenchModel OwnModel(WorkflowRunCenter center) => OwnedModel = new WorkflowFaultWorkbenchModel(center);

    private string? SelectedFlow => _flows.SelectedRows.Count == 1 ? _flows.SelectedRows[0].Tag as string : null;

    private void UpdateCommands()
    {
        var rows = _overview.GetRows();
        var selected = rows.FirstOrDefault(row => row.Name == SelectedFlow);
        _start.Enabled = CanOperate && selected is not null && selected.State is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused);
        _stop.Enabled = CanOperate && selected is not null && selected.State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
        _startAll.Enabled = CanOperate && rows.Count > 0;
        _stopAll.Enabled = CanOperate && rows.Any(row => row.State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused);
    }

    private void Act(WorkflowOperationKind kind, Func<string, Task> action)
    {
        if (SelectedFlow is not { } name) return;
        Run(new WorkflowOperation(kind, name), () => action(name));
    }

    private async void Run(WorkflowOperation operation, Func<Task> action)
    {
        try { await (Gate ?? WorkflowOperationGates.Direct).RunAsync(operation, action); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ShowMessage(exception.Message); }
    }

    private async Task Observe(string? name, Task<WorkflowRunResult> run)
    {
        try
        {
            var result = await run.ConfigureAwait(false);
            if (!result.Success) ShowMessage($"{name ?? "流程"}：{result.Message}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or WorkflowCompilationException)
        {
            ShowMessage($"{name ?? "流程"}：{exception.Message}");
        }
    }
}
