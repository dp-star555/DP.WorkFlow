using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// WinForms 编译诊断列表；双击诊断项可定位问题节点。
/// 静态列表布局位于同名 Designer.cs，诊断内容由共享模型动态填充。
/// </summary>
public sealed partial class WorkflowDiagnosticsControl : UserControl
{
    private WorkflowDesignerSession? _session;
    private WorkflowDiagnosticsModel? _model;
    private string? _startNodeId;
    private IWorkflowDiagnosticProvider? _provider;
    private WorkflowDesignerNavigator? _navigator;
    private readonly ILocalizationManager _defaultLocalizationManager = WorkflowUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
    private ILocalizationContext _localizationContext = null!;
    private string _summaryText = string.Empty;

    /// <summary>初始化工作流诊断列表控件。</summary>
    public WorkflowDiagnosticsControl()
    {
        InitializeComponent();
        _localizationContext = _defaultLocalizationManager.Context;
        // 双击诊断行定位到对应节点。
        diagnosticsTable.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) NavigateSelected(); };
        LocalizationContext = _defaultLocalizationManager.Context;
        // 错误 / 警告行的文字颜色取自当前主题，切换主题后重建列表。
        WorkflowWinFormsTheme.Changed += OnThemeChanged;
        Disposed += (_, _) => WorkflowWinFormsTheme.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        diagnosticsTable.Theme = WorkflowWinFormsTheme.Table(WorkflowWinFormsTheme.Modern);
        RefreshItems();
    }

    /// <summary>获取或设置页面使用的本地化上下文；切换不会丢失当前诊断选择。</summary>
    public ILocalizationContext LocalizationContext
    {
        get => _localizationContext;
        set
        {
            Guard.NotNull(value);
            if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
            _localizationContext = value;
            _localizationContext.Changed += LocalizationChanged;
            localizationProvider.LocalizationContext = value;
            ApplyLocalization();
        }
    }

    public WorkflowDesignerSession? Session
    {
        get => _session;
        set { _session = value; RecreateModel(); }
    }

    public string? EntryNodeId
    {
        get => _startNodeId;
        set { _startNodeId = value; RecreateModel(); }
    }

    private bool _followsDocumentEntry;

    /// <summary>
    /// 是否按会话文档的当前入口检查，不需要设置 <see cref="EntryNodeId"/>；
    /// 用于节点窗口中正在编辑的子画布，子画布可能稍后才放入启动节点。
    /// </summary>
    public bool FollowsDocumentEntry
    {
        get => _followsDocumentEntry;
        set { _followsDocumentEntry = value; RecreateModel(); }
    }

    public bool CanRun => _model?.CanRun == true;
    /// <summary>领域检查及后台准备报告。</summary>
    public IWorkflowDiagnosticProvider? Provider
    {
        get => _provider;
        set { if (_provider != null) _provider.Changed -= ProviderChanged; _provider = value; if (_provider != null) _provider.Changed += ProviderChanged; RecreateModel(); }
    }
    /// <summary>诊断定位所用导航器。</summary>
    public WorkflowDesignerNavigator? Navigator { get => _navigator; set { _navigator = value; RecreateModel(); } }
    /// <summary>运行前重新检查外部文件及选择。</summary>
    public void RefreshDiagnostics() => _model?.Refresh();
    /// <inheritdoc/>
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RefreshDiagnostics(); }
    private void ProviderChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(RefreshDiagnostics); else RefreshDiagnostics();
    }

    /// <summary>获取当前语言下的诊断摘要文本。</summary>
    public string SummaryText => _summaryText;

    /// <summary>当前错误和警告条数（工具栏“校验”和页签上的数字）。</summary>
    public int IssueCount { get; private set; }

    /// <summary>诊断列表刷新后发生。</summary>
    public event EventHandler? SummaryChanged;

    /// <summary>执行 Recreate Model 相关处理。</summary>
    private void RecreateModel()
    {
        DisposeModel();
        if (_session is not null && _followsDocumentEntry)
        {
            _model = new WorkflowDiagnosticsModel(_session);
            _model.Changed += OnModelChanged;
        }
        else if (_session is not null && !string.IsNullOrWhiteSpace(_startNodeId))
        {
            _model = new WorkflowDiagnosticsModel(_session, _startNodeId, _provider, _navigator);
            _model.Changed += OnModelChanged;
        }
        RefreshItems();
    }

    /// <summary>执行 Dispose Model 相关处理。</summary>
    private void DisposeModel()
    {
        if (_model is null)
            return;
        _model.Changed -= OnModelChanged;
        _model.Dispose();
        _model = null;
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
            BeginInvoke(RefreshItems);
        else
            RefreshItems();
    }

    private void NavigateSelected()
    {
        if (diagnosticsTable.SelectedRows.Count == 1 &&
            diagnosticsTable.SelectedRows[0].Tag is WorkflowDiagnosticItem item) _model?.NavigateTo(item);
    }

    private void LocalizationChanged(object? sender, LocaleChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(ApplyLocalization); return; }
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        diagnosticsTable.Columns[0].HeaderText = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsSeverity);
        diagnosticsTable.Columns[1].HeaderText = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsCode);
        diagnosticsTable.Columns[2].HeaderText = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsNode);
        diagnosticsTable.Columns[3].HeaderText = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsMessage);
        AccessibleName = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsAccessibleName);
        diagnosticsTable.AccessibleName = AccessibleName;
        RefreshItems();
    }

    /// <summary>刷新Items。</summary>
    private void RefreshItems()
    {
        var selected = diagnosticsTable.SelectedRows.Count == 1 ? diagnosticsTable.SelectedRows[0].Tag : null;
        diagnosticsTable.Rows.Clear();
        foreach (var diagnostic in _model?.Items ?? Array.Empty<WorkflowDiagnosticItem>())
        {
            var index = diagnosticsTable.Rows.Add(LocalizationContext.Text(diagnostic.Severity == WorkflowValidationSeverity.Error
                    ? WorkflowUiTextKeys.DiagnosticsError : WorkflowUiTextKeys.DiagnosticsWarning),
                diagnostic.Code, diagnostic.NodeId ?? string.Empty, diagnostic.Message);
            var row = diagnosticsTable.Rows[index];
            row.Tag = diagnostic;
            row.Cells[3].ToolTipText = diagnostic.Detail ?? diagnostic.Message;
            row.Cells[0].Style.ForeColor = diagnostic.Severity == WorkflowValidationSeverity.Error
                ? WorkflowWinFormsTheme.Modern.Error
                : WorkflowWinFormsTheme.Modern.Warning;
        }
        WorkflowPage.RestoreSelection(diagnosticsTable, selected, row => row.Tag);
        var errors = (_model?.Items ?? Array.Empty<WorkflowDiagnosticItem>()).Count(item => item.Severity == WorkflowValidationSeverity.Error);
        var warnings = (_model?.Items ?? Array.Empty<WorkflowDiagnosticItem>()).Count - errors;
        _summaryText = errors + warnings == 0
            ? LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsEmpty)
            : LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsSummary,
                new Dictionary<string, object?> { ["errors"] = errors, ["warnings"] = warnings });
        IssueCount = errors + warnings;
        SummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeLocalization()
    {
        if (_provider != null) _provider.Changed -= ProviderChanged;
        if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
        if (_defaultLocalizationManager is IDisposable disposable) disposable.Dispose();
    }
}
