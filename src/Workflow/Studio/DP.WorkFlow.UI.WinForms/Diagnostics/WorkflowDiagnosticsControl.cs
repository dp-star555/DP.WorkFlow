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
    private readonly ModernCommand _navigateCommand;

    /// <summary>初始化工作流诊断列表控件。</summary>
    public WorkflowDiagnosticsControl()
    {
        InitializeComponent();
        diagnosticsListView.ShowItemToolTips = true;
        _localizationContext = _defaultLocalizationManager.Context;
        _navigateCommand = new ModernCommand(NavigateSelected) { Icon = ModernIconKind.Search, CanExecutePredicate = CanNavigate };
        diagnosticsCommandBar.Commands.Add(_navigateCommand);
        diagnosticsListView.DoubleClick += (_, _) => NavigateSelected();
        diagnosticsListView.SelectedIndexChanged += (_, _) => _navigateCommand.RaiseCanExecuteChanged();
        LocalizationContext = _defaultLocalizationManager.Context;
    }

    /// <summary>获取或设置页面使用的本地化上下文；切换不会丢失当前诊断选择。</summary>
    public ILocalizationContext LocalizationContext
    {
        get => _localizationContext;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
            _localizationContext = value;
            _localizationContext.Changed += LocalizationChanged;
            localizationProvider.LocalizationContext = value;
            diagnosticsCommandBar.LocalizationContext = value;
            diagnosticsAlert.LocalizationContext = value;
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
    public string SummaryText => diagnosticsAlert.Text;

    /// <summary>执行 Recreate Model 相关处理。</summary>
    private void RecreateModel()
    {
        DisposeModel();
        if (_session is not null && !string.IsNullOrWhiteSpace(_startNodeId))
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

    /// <summary>处理“Model Changed”事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
            BeginInvoke(RefreshItems);
        else
            RefreshItems();
    }

    private bool CanNavigate() => diagnosticsListView.SelectedItems.Count == 1 &&
        diagnosticsListView.SelectedItems[0].Tag is WorkflowDiagnosticItem;

    private void NavigateSelected()
    {
        if (diagnosticsListView.SelectedItems.Count == 1 &&
            diagnosticsListView.SelectedItems[0].Tag is WorkflowDiagnosticItem item) _model?.NavigateTo(item);
    }

    private void LocalizationChanged(object? sender, LocaleChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(ApplyLocalization); return; }
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        severityColumn.Text = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsSeverity);
        codeColumn.Text = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsCode);
        nodeColumn.Text = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsNode);
        messageColumn.Text = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsMessage);
        _navigateCommand.Text = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsNavigate);
        AccessibleName = LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsAccessibleName);
        diagnosticsListView.AccessibleName = AccessibleName;
        RefreshItems();
    }

    /// <summary>刷新Items。</summary>
    private void RefreshItems()
    {
        var selected = diagnosticsListView.SelectedItems.Count == 1 ? diagnosticsListView.SelectedItems[0].Tag : null;
        diagnosticsListView.BeginUpdate();
        diagnosticsListView.Items.Clear();
        foreach (var diagnostic in _model?.Items ?? Array.Empty<WorkflowDiagnosticItem>())
        {
            var item = new ListViewItem(LocalizationContext.Text(diagnostic.Severity == WorkflowValidationSeverity.Error
                ? WorkflowUiTextKeys.DiagnosticsError : WorkflowUiTextKeys.DiagnosticsWarning))
            {
                Tag = diagnostic,
                ToolTipText = diagnostic.Detail ?? diagnostic.Message,
                ForeColor = diagnostic.Severity == WorkflowValidationSeverity.Error
                    ? Color.FromArgb(248, 113, 113)
                    : Color.FromArgb(251, 191, 36)
            };
            item.SubItems.Add(diagnostic.Code);
            item.SubItems.Add(diagnostic.NodeId ?? string.Empty);
            item.SubItems.Add(diagnostic.Message);
            diagnosticsListView.Items.Add(item);
            if (ReferenceEquals(selected, diagnostic) || Equals(selected, diagnostic)) item.Selected = true;
        }
        diagnosticsListView.EndUpdate();
        var errors = (_model?.Items ?? Array.Empty<WorkflowDiagnosticItem>()).Count(item => item.Severity == WorkflowValidationSeverity.Error);
        var warnings = (_model?.Items ?? Array.Empty<WorkflowDiagnosticItem>()).Count - errors;
        diagnosticsAlert.Status = errors > 0 ? ModernVisualStatus.Error : warnings > 0 ? ModernVisualStatus.Warning : ModernVisualStatus.Success;
        diagnosticsAlert.Text = errors + warnings == 0
            ? LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsEmpty)
            : LocalizationContext.Text(WorkflowUiTextKeys.DiagnosticsSummary,
                new Dictionary<string, object?> { ["errors"] = errors, ["warnings"] = warnings });
        _navigateCommand.RaiseCanExecuteChanged();
    }

    private void DisposeLocalization()
    {
        if (_provider != null) _provider.Changed -= ProviderChanged;
        if (_localizationContext is not null) _localizationContext.Changed -= LocalizationChanged;
        if (_defaultLocalizationManager is IDisposable disposable) disposable.Dispose();
    }
}
