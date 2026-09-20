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
    private readonly ModernCommand _refreshCommand;
    private readonly ModernCommand _pauseCommand;
    private readonly ModernCommand _exportCommand;
    private readonly ModernCommand _resetFiltersCommand;
    private WorkflowStudioRuntimeBinding? _runtimeBinding;
    private ILocalizationContext _localizationContext = null!;
    private bool _synchronizingPause;

    /// <summary>初始化运行时监视控件及其共享命令。</summary>
    public WorkflowRuntimeMonitorControl()
    {
        InitializeComponent();
        _refreshCommand = new ModernCommand(RefreshRuntimeData)
        {
            Icon = ModernIconKind.Search,
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.R,
            CanExecutePredicate = () => RuntimeBinding is not null
        };
        _pauseCommand = new ModernCommand(ToggleTracePause)
        {
            Icon = ModernIconKind.Play,
            CheckOnExecute = true
        };
        _exportCommand = new ModernCommand(ExportTraceCsv)
        {
            Icon = ModernIconKind.Info,
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.E,
            CanExecutePredicate = () => _model.TraceEntries.Count > 0
        };
        _resetFiltersCommand = new ModernCommand(ResetFilters)
        {
            Icon = ModernIconKind.Close,
            CanExecutePredicate = HasActiveFilters
        };
        runtimeCommandBar.Commands.Add(_refreshCommand);
        runtimeCommandBar.Commands.Add(_pauseCommand);
        runtimeCommandBar.Commands.Add(new ModernCommand { Kind = ModernCommandKind.Separator });
        runtimeCommandBar.Commands.Add(_exportCommand);
        runtimeCommandBar.Commands.Add(_resetFiltersCommand);
        commandManager.Commands.Add(_refreshCommand);
        commandManager.Commands.Add(_exportCommand);

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
            ArgumentNullException.ThrowIfNull(value);
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
            if (_runtimeBinding is not null) _runtimeBinding.StateChanged -= OnRuntimeChanged;
            _runtimeBinding = value;
            if (_runtimeBinding is not null) _runtimeBinding.StateChanged += OnRuntimeChanged;
            _refreshCommand.RaiseCanExecuteChanged();
            RefreshRuntimeData();
        }
    }

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
    public string SummaryText => runtimeAlert.Text;

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
        if (InvokeRequired) BeginInvoke(RefreshRuntimeData); else RefreshRuntimeData();
    }

    private void ModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
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
        _resetFiltersCommand.RaiseCanExecuteChanged();
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
                _resetFiltersCommand.RaiseCanExecuteChanged();
                return;
            }
        }
        validationProvider.SetValidation(traceDateRange, ModernValidationState.None);
        _model.SetTraceDateRange(traceDateRange.StartDate, traceDateRange.EndDate);
        _resetFiltersCommand.RaiseCanExecuteChanged();
    }

    private void PauseTraceChanged(object? sender, EventArgs e)
    {
        if (_synchronizingPause) return;
        _synchronizingPause = true;
        try
        {
            _pauseCommand.IsChecked = pauseTraceCheckBox.Checked;
            _model.SetTracePaused(pauseTraceCheckBox.Checked);
        }
        finally { _synchronizingPause = false; }
    }

    private void ToggleTracePause()
    {
        _synchronizingPause = true;
        try
        {
            pauseTraceCheckBox.Checked = _pauseCommand.IsChecked;
            _model.SetTracePaused(_pauseCommand.IsChecked);
        }
        finally { _synchronizingPause = false; }
    }

    private void ResetFilters()
    {
        traceFilterTextBox.Text = string.Empty;
        traceDateRange.StartDate = null;
        traceDateRange.EndDate = null;
        validationProvider.SetValidation(traceDateRange, ModernValidationState.None);
        _model.SetTraceDateRange(null, null);
        _resetFiltersCommand.RaiseCanExecuteChanged();
    }

    private bool HasActiveFilters() => traceFilterTextBox.Text.Length > 0
        || traceDateRange.StartDate is not null || traceDateRange.EndDate is not null;

    private void RefreshLists(bool autoScrollTrace = true)
    {
        var culture = LocalizationContext.Current.Culture;
        Replace(tokensListView, _model.Tokens.Select(item => ((object)item.TokenId, new[]
        {
            item.TokenId.ToString(culture), item.CurrentNodeId, item.ScopePath, item.AncestorTokens
        })));
        Replace(scopesListView, _model.ParallelScopes.Select(item => ((object)item.ScopeId, new[]
        {
            item.ScopeId.ToString(culture), item.ScopeNodeId, item.MergeNodeId,
            $"{item.CompletedBranches.ToString(culture)}/{item.TotalBranches.ToString(culture)}",
            LocalizationContext.Text(item.IsCompleted ? WorkflowUiTextKeys.RuntimeYes : WorkflowUiTextKeys.RuntimeNo)
        })));
        Replace(childrenListView, _model.ChildWorkflows.Select(item => ((object)item.RunId, new[]
        {
            item.ParentNodeId, item.WorkflowName, WorkflowRuntimeText.ExecutionState(LocalizationContext, item.State),
            $"{item.Elapsed.TotalMilliseconds.ToString("F0", culture)} ms", item.RunId.ToString("N")
        })));
        Replace(traceListView, _model.TraceEntries.Select(item => ((object)item.Sequence, new[]
        {
            item.Sequence.ToString(culture), item.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", culture),
            item.NodeId, item.TokenId.ToString(culture), item.ScopePath,
            WorkflowRuntimeText.TraceStep(LocalizationContext, item.Step), item.Message
        })));
        Replace(outputListView, _model.OutputEntries.Select(item => ((object)item.Sequence, new[]
        {
            item.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", culture), item.NodeId, item.Message
        })));
        Replace(timingListView, _model.TimingTrends.Select(item => ((object)item.NodeId, new[]
        {
            item.NodeId, item.ExecutionCount.ToString(culture), $"{item.Elapsed.TotalMilliseconds.ToString("F1", culture)} ms",
            $"{item.AverageMilliseconds.ToString("F1", culture)} ms", WorkflowRuntimeText.NodeState(LocalizationContext, item.State)
        })));
        if (autoScrollTrace && !_model.IsTracePaused && traceListView.IsHandleCreated && traceListView.Items.Count > 0)
            traceListView.Items[traceListView.Items.Count - 1].EnsureVisible();
        UpdateSummary();
        _exportCommand.RaiseCanExecuteChanged();
        _resetFiltersCommand.RaiseCanExecuteChanged();
    }

    private void UpdateSummary()
    {
        var count = _model.Tokens.Count + _model.ParallelScopes.Count + _model.TraceEntries.Count;
        runtimeAlert.Status = count == 0 ? ModernVisualStatus.Primary : ModernVisualStatus.Success;
        runtimeAlert.Text = count == 0
            ? LocalizationContext.Text(WorkflowUiTextKeys.RuntimeSummaryEmpty)
            : LocalizationContext.Text(WorkflowUiTextKeys.RuntimeSummary, new Dictionary<string, object?>
            {
                ["tokens"] = _model.Tokens.Count,
                ["scopes"] = _model.ParallelScopes.Count,
                ["trace"] = _model.TraceEntries.Count
            });
        runtimeAlert.Description = string.Empty;
    }

    private void RefreshRuntimeData()
    {
        _model.SetSnapshot(_runtimeBinding?.Host.GetSnapshot());
        _model.SetTraceBatch(_runtimeBinding?.Host.Engine?.GetTraceBatch());
    }

    private void ExportTraceCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeCsvFilter),
            FileName = $"workflow-trace-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
            _model.ExportTraceCsv(writer, new WorkflowTraceCsvHeaders(
                LocalizationContext.Text(WorkflowUiTextKeys.RuntimeSequence),
                LocalizationContext.Text(WorkflowUiTextKeys.RuntimeTime),
                LocalizationContext.Text(WorkflowUiTextKeys.RuntimeNode),
                LocalizationContext.Text(WorkflowUiTextKeys.RuntimeToken),
                LocalizationContext.Text(WorkflowUiTextKeys.RuntimeScope),
                LocalizationContext.Text(WorkflowUiTextKeys.RuntimeStep),
                LocalizationContext.Text(WorkflowUiTextKeys.RuntimeMessage)));
            if (FindForm() is { } owner)
                ModernMessage.Success(owner, LocalizationContext.Text(WorkflowUiTextKeys.RuntimeExported,
                    new Dictionary<string, object?> { ["count"] = _model.TraceEntries.Count }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowApplicationError(new ApplicationError(WorkflowErrorCodes.RuntimeExportFailed,
                new Dictionary<string, object?> { ["message"] = exception.Message }, Guid.NewGuid().ToString("N")));
        }
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
            tokensListView.AccessibleName = tokensPage.Text;
            scopesListView.AccessibleName = scopesPage.Text;
            childrenListView.AccessibleName = childrenPage.Text;
            traceListView.AccessibleName = tracePage.Text;
            outputListView.AccessibleName = outputPage.Text;
            timingListView.AccessibleName = timingPage.Text;
            SetHeaders(tokensListView, WorkflowUiTextKeys.RuntimeToken, WorkflowUiTextKeys.RuntimeCurrentNode,
                WorkflowUiTextKeys.RuntimeScope, WorkflowUiTextKeys.RuntimeAncestorToken);
            SetHeaders(scopesListView, WorkflowUiTextKeys.RuntimeScope, WorkflowUiTextKeys.RuntimeDispatchNode,
                WorkflowUiTextKeys.RuntimeMergeNode, WorkflowUiTextKeys.RuntimeProgress, WorkflowUiTextKeys.RuntimeCompleted);
            SetHeaders(childrenListView, WorkflowUiTextKeys.RuntimeParentNode, WorkflowUiTextKeys.RuntimeChildWorkflow,
                WorkflowUiTextKeys.RuntimeState, WorkflowUiTextKeys.RuntimeElapsed, WorkflowUiTextKeys.RuntimeRunId);
            SetHeaders(traceListView, WorkflowUiTextKeys.RuntimeSequence, WorkflowUiTextKeys.RuntimeTime,
                WorkflowUiTextKeys.RuntimeNode, WorkflowUiTextKeys.RuntimeToken, WorkflowUiTextKeys.RuntimeScope,
                WorkflowUiTextKeys.RuntimeStep, WorkflowUiTextKeys.RuntimeMessage);
            SetHeaders(outputListView, WorkflowUiTextKeys.RuntimeTime, WorkflowUiTextKeys.RuntimeNode, WorkflowUiTextKeys.RuntimeOutput);
            SetHeaders(timingListView, WorkflowUiTextKeys.RuntimeNode, WorkflowUiTextKeys.RuntimeExecutionCount,
                WorkflowUiTextKeys.RuntimeTotalElapsed, WorkflowUiTextKeys.RuntimeAverageElapsed, WorkflowUiTextKeys.RuntimeState);
            traceFilterTextBox.PlaceholderText = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeFilterPlaceholder);
            traceFilterTextBox.AccessibleName = traceFilterTextBox.PlaceholderText;
            pauseTraceCheckBox.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimePauseTrace);
            traceDateRange.AccessibleName = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeDateRangeAccessible);
            _refreshCommand.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeRefresh);
            _pauseCommand.Text = pauseTraceCheckBox.Text;
            _exportCommand.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeExport);
            _resetFiltersCommand.Text = LocalizationContext.Text(WorkflowUiTextKeys.RuntimeResetFilters);
            runtimeCommandBar.Commands.ResetBindings();
            if (!TraceRangeIsValid) TraceDateRangeChanged(this, EventArgs.Empty);
            RefreshLists(autoScrollTrace: false);
        }
        finally { ResumeLayout(true); }
    }

    private void SetHeaders(ListView list, params TextKey[] keys)
    {
        for (var index = 0; index < Math.Min(list.Columns.Count, keys.Length); index++)
            list.Columns[index].Text = LocalizationContext.Text(keys[index]);
    }

    private static void Replace(ListView list, IEnumerable<(object Key, string[] Cells)> rows)
    {
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag).ToHashSet();
        var topIndex = list.IsHandleCreated ? list.TopItem?.Index ?? 0 : 0;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var row in rows)
            {
                var item = new ListViewItem(row.Cells[0]) { Tag = row.Key };
                item.SubItems.AddRange(row.Cells.Skip(1).ToArray());
                item.Selected = selected.Contains(row.Key);
                list.Items.Add(item);
            }
            if (list.IsHandleCreated && list.Items.Count > 0 && topIndex > 0)
                list.Items[Math.Min(topIndex, list.Items.Count - 1)].EnsureVisible();
        }
        finally { list.EndUpdate(); }
    }

    private void DisposeRuntimeMonitor()
    {
        if (_runtimeBinding is not null) _runtimeBinding.StateChanged -= OnRuntimeChanged;
        _model.Changed -= ModelChanged;
        if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
        commandManager.Owner = null;
        if (_defaultLocalizationManager is IDisposable disposable) disposable.Dispose();
    }
}
