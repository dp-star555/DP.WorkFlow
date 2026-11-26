using System.ComponentModel;
using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;
using DP.WorkFlow;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>以现代命令、筛选、反馈和本地化呈现 Token、作用域、子流程、Trace、输出及耗时趋势。</summary>
public sealed partial class WorkflowRuntimeMonitorControl : UserControl
{
    private const int MaximumTraceRangeDays = 90;
    private readonly WorkflowRuntimeMonitorModel _model = new();
    private readonly ILocalizationManager _defaultLocalizationManager = WorkflowUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
    private readonly ModernCommand _exportCommand;
    private readonly ModernContextMenu _listMenu = new() { Theme = WorkflowWinFormsTheme.Modern };
    private DataGridView? _menuList;
    private WorkflowStudioRuntimeBinding? _runtimeBinding;
    private IWorkflowRuntimeMonitorSource? _source;
    private ILocalizationContext _localizationContext = null!;
    // 运行数据变化只置脏标记，由统一刷新时钟合并刷新：快速循环时每秒可能有上千次变化，逐次重建表格会占满界面线程。
    private readonly IDisposable _refreshFrame;
    private bool _runtimeDirty;
    private bool _applyingRuntimeData;
    private bool _traceReset = true;
    private IReadOnlyList<WorkflowTraceMonitorItem> _shownTrace = Array.Empty<WorkflowTraceMonitorItem>();
    private IReadOnlyList<WorkflowTraceMonitorItem> _shownOutput = Array.Empty<WorkflowTraceMonitorItem>();

    /// <summary>初始化运行时监视控件及其共享命令。</summary>
    public WorkflowRuntimeMonitorControl()
    {
        InitializeComponent();
        // 每张表右键菜单导出当前表格；Ctrl+Shift+E 导出轨迹。
        _exportCommand = new ModernCommand(() => ExportCsv(_menuList ?? traceTable))
        {
            Icon = ModernIconKind.Save,
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.E,
            CanExecutePredicate = () => (_menuList ?? traceTable).RowCount > 0
        };
        _listMenu.SetCommands(new[] { _exportCommand });
        _listMenu.Opening += (_, e) =>
        {
            _menuList = _listMenu.SourceControl as DataGridView;
            _exportCommand.RaiseCanExecuteChanged();
            if (_menuList is null) e.Cancel = true;
        };
        _listMenu.Closed += (_, _) => _menuList = null;
        foreach (var list in new DataGridView[] { tokensTable, scopesTable, childrenTable, traceTable, outputTable, timingTable })
            list.ContextMenuStrip = _listMenu;
        commandManager.Commands.Add(_exportCommand);

        // 轨迹与输出可能有数千行，使用虚拟模式：刷新只更新行数，只为可见单元格取值（按时间顺序，不支持点列头排序）。
        foreach (var table in new DataGridView[] { traceTable, outputTable })
        {
            table.VirtualMode = true;
            foreach (DataGridViewColumn column in table.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        traceTable.CellValueNeeded += (_, e) => e.Value = TraceCell(_shownTrace, e.RowIndex, e.ColumnIndex);
        outputTable.CellValueNeeded += (_, e) => e.Value = OutputCell(_shownOutput, e.RowIndex, e.ColumnIndex);
        _refreshFrame = WorkflowRefreshClock.Subscribe(this, FlushRuntimeData);
        monitorTabs.SelectedIndexChanged += (_, _) => RefreshLists(autoScrollTrace: false);
        VisibleChanged += (_, _) => { if (Visible) FlushRuntimeData(); };

        traceFilterTextBox.TextChanged += TraceFilterChanged;
        traceDateRange.RangeChanged += TraceDateRangeChanged;
        pauseTraceCheckBox.CheckedChanged += PauseTraceChanged;
        _model.Changed += ModelChanged;
        LocalizationContext = _defaultLocalizationManager.Context;
        traceDateRange.StartDate = null;
        traceDateRange.EndDate = null;
        RefreshLists(autoScrollTrace: false);
    }

    /// <summary>获取或设置页面本地化上下文；切换语言保留筛选、页签、选择和运行模型。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ILocalizationContext LocalizationContext
    {
        get => _localizationContext;
        set
        {
            Guard.NotNull(value);
            if (ReferenceEquals(_localizationContext, value)) return;
            if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
            _localizationContext = value;
            _localizationContext.Changed += LocalizationChanged;
            localizationProvider.LocalizationContext = value;
            validationProvider.LocalizationContext = value;
            ModernUiSettings.ApplyLocalization(this, value);
            ApplyLocalization();
        }
    }

    /// <summary>获取或设置工作流 Studio 运行时绑定。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WorkflowStudioRuntimeBinding? RuntimeBinding
    {
        get => _runtimeBinding;
        set
        {
            if (ReferenceEquals(_runtimeBinding, value)) return;
            _runtimeBinding = value;
            Source = value;
        }
    }

    /// <summary>
    /// 获取或设置监视的数据来源：主画布为整个运行（<see cref="RuntimeBinding"/>），
    /// 节点窗口的子画布为该复合节点的子运行（<see cref="WorkflowSessionRuntimeMonitorSource"/>）。
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowRuntimeMonitorSource? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value)) return;
            if (_source is not null) _source.Changed -= OnRuntimeChanged;
            _source = value;
            _traceReset = true;
            if (_source is not null) _source.Changed += OnRuntimeChanged;
            RefreshRuntimeData();
        }
    }

    /// <summary>按节点汇总的最近执行耗时（运行监视页侧栏用）。</summary>
    public IReadOnlyList<WorkflowNodeTimingTrendItem> TimingTrends => _model.TimingTrends;

    /// <summary>获取或设置 Trace 文本筛选条件。</summary>
    [Browsable(false)]
    public string TraceFilter { get => traceFilterTextBox.Text; set => traceFilterTextBox.Text = value ?? string.Empty; }

    /// <summary>获取或设置 Trace 界面是否暂停刷新。</summary>
    [Browsable(false)]
    public bool TracePaused { get => pauseTraceCheckBox.Checked; set => pauseTraceCheckBox.Checked = value; }

    /// <summary>获取当前筛选后的 Trace 数量。</summary>
    [Browsable(false)]
    public int VisibleTraceCount => _model.TraceEntries.Count;

    /// <summary>获取当前语言下的运行摘要。</summary>
    [Browsable(false)]
    public string SummaryText { get; private set; } = string.Empty;

    /// <summary>获取日期范围是否通过页面验证。</summary>
    [Browsable(false)]
    public bool TraceRangeIsValid => traceDateRange.ValidationState != ModernValidationState.Error;

    /// <summary>获取或设置 Trace 起始日期。</summary>
    [Browsable(false)]
    public DateTime? TraceStartDate { get => traceDateRange.StartDate; set => traceDateRange.StartDate = value; }

    /// <summary>获取或设置 Trace 结束日期。</summary>
    [Browsable(false)]
    public DateTime? TraceEndDate { get => traceDateRange.EndDate; set => traceDateRange.EndDate = value; }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        commandManager.Owner = FindForm();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (IsHandleCreated) commandManager.Owner = FindForm();
    }

    private void OnRuntimeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(MarkRuntimeDirty); return; }
        MarkRuntimeDirty();
    }

    /// <summary>记录运行数据已变化；定时器到期时合并为一次刷新。</summary>
    private void MarkRuntimeDirty()
    {
        if (IsDisposed) return;
        _runtimeDirty = true;
    }

    /// <summary>把累计的运行数据变化一次性应用到界面；控件不可见时推迟到重新显示。</summary>
    private void FlushRuntimeData()
    {
        if (!_runtimeDirty || IsDisposed || !Visible) return;
        _runtimeDirty = false;
        RefreshRuntimeData();
    }

    private void ModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || _applyingRuntimeData) return;
        if (InvokeRequired) BeginInvoke(() => RefreshLists()); else RefreshLists();
    }

    private void LocalizationChanged(object? sender, LocaleChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(ApplyLocalization); return; }
        ApplyLocalization();
    }

    private void TraceFilterChanged(object? sender, EventArgs e)
    {
        _model.SetTraceFilter(traceFilterTextBox.Text);
    }

    private void TraceDateRangeChanged(object? sender, EventArgs e)
    {
        if (traceDateRange.StartDate is { } start && traceDateRange.EndDate is { } end)
        {
            var days = (end.Date - start.Date).Days + 1;
            if (days > MaximumTraceRangeDays)
            {
                validationProvider.SetValidation(traceDateRange, ModernValidationState.Error,
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeRangeTooLong,
                        new Dictionary<string, object?> { ["days"] = MaximumTraceRangeDays }));
                return;
            }
        }
        validationProvider.SetValidation(traceDateRange, ModernValidationState.None);
        _model.SetTraceDateRange(traceDateRange.StartDate, traceDateRange.EndDate);
    }

    private void PauseTraceChanged(object? sender, EventArgs e) => _model.SetTracePaused(pauseTraceCheckBox.Checked);

    /// <summary>只刷新当前页签的表格（其余页签切换时再刷新），并更新摘要。</summary>
    private void RefreshLists(bool autoScrollTrace = true)
    {
        var culture = LocalizationContext.Current.Culture;
        var page = monitorTabs.SelectedTab;
        if (page == tokensPage)
            Replace(tokensTable, _model.Tokens.Select(item => ((object)item.TokenId, new[]
            {
                item.TokenId.ToString(culture), item.CurrentNodeId, item.ScopePath, item.AncestorTokens
            })));
        else if (page == scopesPage)
            Replace(scopesTable, _model.ParallelScopes.Select(item => ((object)item.ScopeId, new[]
            {
                item.ScopeId.ToString(culture), item.ScopeNodeId, item.MergeNodeId,
                $"{item.CompletedBranches.ToString(culture)}/{item.TotalBranches.ToString(culture)}",
                LocalizationContext.Text(item.IsCompleted ? WorkflowUiTextKeys.RuntimeYes : WorkflowUiTextKeys.RuntimeNo)
            })));
        else if (page == childrenPage)
            Replace(childrenTable, _model.ChildWorkflows.Select(item => ((object)item.RunId, new[]
            {
                item.ParentNodeId, item.WorkflowName,
                WorkflowRuntimeText.ExecutionState(LocalizationContext, item.State)
                    + (WorkflowRuntimeMonitorModel.DescribeChildProgress(item) is { Length: > 0 } progress ? " · " + progress : string.Empty),
                $"{item.Elapsed.TotalMilliseconds.ToString("F0", culture)} ms", item.RunId.ToString("N")
            })));
        else if (page == tracePage)
        {
            _shownTrace = ReplaceVirtual(traceTable, _shownTrace, _model.TraceEntries);
            if (autoScrollTrace && !_model.IsTracePaused && traceTable.IsHandleCreated)
                WorkflowPage.ScrollTo(traceTable, _shownTrace.Count - 1);
        }
        else if (page == outputPage)
            _shownOutput = ReplaceVirtual(outputTable, _shownOutput, _model.OutputEntries);
        else if (page == timingPage)
            Replace(timingTable, _model.TimingTrends.Select(item => ((object)item.NodeId, new[]
            {
                item.NodeId, item.ExecutionCount.ToString(culture), $"{item.Elapsed.TotalMilliseconds.ToString("F1", culture)} ms",
                $"{item.AverageMilliseconds.ToString("F1", culture)} ms", WorkflowRuntimeText.NodeState(LocalizationContext, item.State)
            })));
        UpdateSummary();
        _exportCommand.RaiseCanExecuteChanged();
    }

    private string TraceCell(IReadOnlyList<WorkflowTraceMonitorItem> rows, int index, int column)
    {
        if (index < 0 || index >= rows.Count) return string.Empty;
        var row = rows[index];
        var culture = LocalizationContext.Current.Culture;
        return column switch
        {
            0 => row.Sequence.ToString(culture),
            1 => row.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", culture),
            2 => row.NodeId,
            3 => row.TokenId.ToString(culture),
            4 => row.ScopePath,
            5 => WorkflowRuntimeText.TraceStep(LocalizationContext, row.Step),
            _ => row.Message
        };
    }

    private string OutputCell(IReadOnlyList<WorkflowTraceMonitorItem> rows, int index, int column)
    {
        if (index < 0 || index >= rows.Count) return string.Empty;
        var row = rows[index];
        return column switch
        {
            0 => row.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", LocalizationContext.Current.Culture),
            1 => row.NodeId,
            _ => row.Message
        };
    }

    /// <summary>
    /// 更新虚拟表格：只改行数并重绘可见行；选中行按轨迹序号和节点跟随（有界缓存丢弃旧记录后行号会前移）。
    /// </summary>
    private static IReadOnlyList<WorkflowTraceMonitorItem> ReplaceVirtual(
        DataGridView table, IReadOnlyList<WorkflowTraceMonitorItem> previous, IReadOnlyList<WorkflowTraceMonitorItem> rows)
    {
        if (ReferenceEquals(previous, rows) && table.RowCount == rows.Count) return rows;
        var selected = table.SelectedRows.Cast<DataGridViewRow>()
            .Where(row => row.Index < previous.Count)
            .Select(row => (previous[row.Index].Sequence, previous[row.Index].NodeId))
            .ToHashSet();
        table.ClearSelection();
        table.RowCount = rows.Count;
        if (selected.Count > 0)
            for (var index = 0; index < rows.Count; index++)
                if (selected.Contains((rows[index].Sequence, rows[index].NodeId)))
                    table.Rows[index].Selected = true;
        table.Invalidate();
        return rows;
    }

    private void UpdateSummary()
    {
        var count = _model.Tokens.Count + _model.ParallelScopes.Count + _model.TraceEntries.Count;
        SummaryText = count == 0
            ? LocalizationContext.Text(WorkflowUiTextKeys.RuntimeSummaryEmpty)
            : LocalizationContext.Text(WorkflowUiTextKeys.RuntimeSummary, new Dictionary<string, object?>
            {
                ["tokens"] = _model.Tokens.Count,
                ["scopes"] = _model.ParallelScopes.Count,
                ["trace"] = _model.TraceEntries.Count
            });
    }

    private void RefreshRuntimeData()
    {
        _runtimeDirty = false;
        // 快照和轨迹各自触发一次模型变化，合并成一次表格刷新。
        _applyingRuntimeData = true;
        try
        {
            _model.SetSnapshot(_source?.GetSnapshot());
            // 换了来源后整窗读取一次，此后只按游标读取新条目。
            if (_traceReset) _model.SetTraceBatch(_source?.GetTraceBatch());
            else _model.AppendTraceBatch(_source?.GetTraceBatch(_model.TraceCursor));
            _traceReset = false;
        }
        finally { _applyingRuntimeData = false; }
        RefreshLists();
    }

    /// <summary>导出右键所在表格；轨迹表按完整轨迹字段导出，其余表格按当前列导出。</summary>
    private void ExportCsv(DataGridView list)
    {
        var name = ReferenceEquals(list, traceTable) ? "trace" : list.Name.Replace("Table", string.Empty, StringComparison.Ordinal);
        using var dialog = new SaveFileDialog
        {
            Filter = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeCsvFilter),
            FileName = $"workflow-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
            if (ReferenceEquals(list, traceTable))
                _model.ExportTraceCsv(writer, new WorkflowTraceCsvHeaders(
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeSequence),
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTime),
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeNode),
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeToken),
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeScope),
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeStep),
                    LocalizationContext.Text(WorkflowUiTextKeys.RuntimeMessage)));
            else
                WriteListCsv(writer, list);
            if (FindForm() is { } owner)
                ModernMessage.Success(owner, LocalizationContext.Text(WorkflowUiTextKeys.RuntimeExported,
                    new Dictionary<string, object?> { ["count"] = ReferenceEquals(list, traceTable) ? _model.TraceEntries.Count 
                        : list.RowCount }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowApplicationError(new ApplicationError(WorkflowErrorCodes.RuntimeExportFailed,
                new Dictionary<string, object?> { ["message"] = exception.Message }, Guid.NewGuid().ToString("N")));
        }
    }

    private static void WriteListCsv(TextWriter writer, DataGridView list)
    {
        static string Csv(string? value) => value is null || value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value ?? string.Empty
            : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        writer.WriteLine(string.Join(",", list.Columns.Cast<DataGridViewColumn>().Select(column => Csv(column.HeaderText))));
        // 按当前显示顺序（含排序）逐行导出；虚拟模式下取值会触发 CellValueNeeded。
        foreach (DataGridViewRow row in list.Rows)
            writer.WriteLine(string.Join(",", row.Cells.Cast<DataGridViewCell>().Select(cell => Csv(Convert.ToString(cell.Value, CultureInfo.CurrentCulture)))));
    }

    private void ShowApplicationError(ApplicationError error)
    {
        var arguments = error.Arguments?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, object?>();
        arguments["traceId"] = error.TraceId ?? string.Empty;
        ModernDialog.Show(this, new ModernDialogOptions
        {
            Title = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeExportFailedTitle),
            Message = LocalizationContext.Text(new TextKey("workflowUi", error.Code), arguments),
            Status = ModernVisualStatus.Error,
            ShowCancelButton = false,
            LocalizationContext = LocalizationContext
        });
    }

    private void ApplyLocalization()
    {
        SuspendLayout();
        try
        {
            AccessibleName = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeAccessibleName);
            tokensPage.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTabTokens);
            scopesPage.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTabScopes);
            childrenPage.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTabChildren);
            tracePage.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTabTrace);
            outputPage.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTabOutput);
            timingPage.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTabTiming);
            tokensTable.AccessibleName = tokensPage.Text;
            scopesTable.AccessibleName = scopesPage.Text;
            childrenTable.AccessibleName = childrenPage.Text;
            traceTable.AccessibleName = tracePage.Text;
            outputTable.AccessibleName = outputPage.Text;
            timingTable.AccessibleName = timingPage.Text;
            SetHeaders(tokensTable, WorkflowUiTextKeys.RuntimeToken, WorkflowUiTextKeys.RuntimeCurrentNode,
                WorkflowUiTextKeys.RuntimeScope, WorkflowUiTextKeys.RuntimeAncestorToken);
            SetHeaders(scopesTable, WorkflowUiTextKeys.RuntimeScope, WorkflowUiTextKeys.RuntimeDispatchNode,
                WorkflowUiTextKeys.RuntimeMergeNode, WorkflowUiTextKeys.RuntimeProgress, WorkflowUiTextKeys.RuntimeCompleted);
            SetHeaders(childrenTable, WorkflowUiTextKeys.RuntimeParentNode, WorkflowUiTextKeys.RuntimeChildWorkflow,
                WorkflowUiTextKeys.RuntimeState, WorkflowUiTextKeys.RuntimeElapsed, WorkflowUiTextKeys.RuntimeRunId);
            SetHeaders(traceTable, WorkflowUiTextKeys.RuntimeSequence, WorkflowUiTextKeys.RuntimeTime,
                WorkflowUiTextKeys.RuntimeNode, WorkflowUiTextKeys.RuntimeToken, WorkflowUiTextKeys.RuntimeScope,
                WorkflowUiTextKeys.RuntimeStep, WorkflowUiTextKeys.RuntimeMessage);
            SetHeaders(outputTable, WorkflowUiTextKeys.RuntimeTime, WorkflowUiTextKeys.RuntimeNode, WorkflowUiTextKeys.RuntimeOutput);
            SetHeaders(timingTable, WorkflowUiTextKeys.RuntimeNode, WorkflowUiTextKeys.RuntimeExecutionCount,
                WorkflowUiTextKeys.RuntimeTotalElapsed, WorkflowUiTextKeys.RuntimeAverageElapsed, WorkflowUiTextKeys.RuntimeState);
            traceFilterTextBox.PlaceholderText = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeFilterPlaceholder);
            traceFilterTextBox.AccessibleName = traceFilterTextBox.PlaceholderText;
            pauseTraceCheckBox.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimePauseTrace);
            traceDateRange.AccessibleName = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeDateRangeAccessible);
            _exportCommand.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeExport);
            if (!TraceRangeIsValid) TraceDateRangeChanged(this, EventArgs.Empty);
            RefreshLists(autoScrollTrace: false);
        }
        finally { ResumeLayout(true); }
    }

    private void SetHeaders(DataGridView table, params TextKey[] keys)
    {
        for (var index = 0; index < Math.Min(table.Columns.Count, keys.Length); index++)
            table.Columns[index].HeaderText = LocalizationContext.Text(keys[index]);
    }

    /// <summary>重建行，保留选中行、滚动位置和用户点列头选的排序。</summary>
    private static void Replace(DataGridView table, IEnumerable<(object Key, string[] Cells)> rows)
    {
        var selected = table.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Tag).ToHashSet();
        var top = table.FirstDisplayedScrollingRowIndex;
        var sortColumn = table.SortedColumn;
        var sortOrder = table.SortOrder;
        table.SuspendLayout();
        try
        {
            table.Rows.Clear();
            foreach (var row in rows)
            {
                var index = table.Rows.Add(row.Cells);
                table.Rows[index].Tag = row.Key;
            }
            if (sortColumn is not null && sortOrder != SortOrder.None)
                table.Sort(sortColumn, sortOrder == SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
            table.ClearSelection();
            foreach (DataGridViewRow row in table.Rows)
                if (selected.Contains(row.Tag)) row.Selected = true;
            if (top > 0) WorkflowPage.ScrollTo(table, Math.Min(top, table.RowCount - 1));
        }
        finally { table.ResumeLayout(); }
    }

    private void DisposeRuntimeMonitor()
    {
        if (_source is not null) _source.Changed -= OnRuntimeChanged;
        _refreshFrame.Dispose();
        _model.Changed -= ModelChanged;
        if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
        commandManager.Owner = null;
        _listMenu.Dispose();
        if (_defaultLocalizationManager is IDisposable disposable) disposable.Dispose();
    }
}
