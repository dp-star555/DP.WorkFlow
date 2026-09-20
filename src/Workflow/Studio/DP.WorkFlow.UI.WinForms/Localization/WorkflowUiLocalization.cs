using System.Globalization;
using System.Reflection;
using System.Resources;
using ModernPropertyGrid.WinForms;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

internal static class WorkflowUiTextKeys
{
    private const string Module = "workflowUi";
    public static TextKey DiagnosticsSeverity { get; } = new(Module, "DiagnosticsSeverity");
    public static TextKey DiagnosticsCode { get; } = new(Module, "DiagnosticsCode");
    public static TextKey DiagnosticsNode { get; } = new(Module, "DiagnosticsNode");
    public static TextKey DiagnosticsMessage { get; } = new(Module, "DiagnosticsMessage");
    public static TextKey DiagnosticsError { get; } = new(Module, "DiagnosticsError");
    public static TextKey DiagnosticsWarning { get; } = new(Module, "DiagnosticsWarning");
    public static TextKey DiagnosticsNavigate { get; } = new(Module, "DiagnosticsNavigate");
    public static TextKey DiagnosticsAccessibleName { get; } = new(Module, "DiagnosticsAccessibleName");
    public static TextKey DiagnosticsEmpty { get; } = new(Module, "DiagnosticsEmpty");
    public static TextKey DiagnosticsSummary { get; } = new(Module, "DiagnosticsSummary");
    public static TextKey RuntimeTabTokens { get; } = new(Module, "RuntimeTabTokens");
    public static TextKey RuntimeTabScopes { get; } = new(Module, "RuntimeTabScopes");
    public static TextKey RuntimeTabChildren { get; } = new(Module, "RuntimeTabChildren");
    public static TextKey RuntimeTabTrace { get; } = new(Module, "RuntimeTabTrace");
    public static TextKey RuntimeTabOutput { get; } = new(Module, "RuntimeTabOutput");
    public static TextKey RuntimeTabTiming { get; } = new(Module, "RuntimeTabTiming");
    public static TextKey RuntimeToken { get; } = new(Module, "RuntimeToken");
    public static TextKey RuntimeCurrentNode { get; } = new(Module, "RuntimeCurrentNode");
    public static TextKey RuntimeScope { get; } = new(Module, "RuntimeScope");
    public static TextKey RuntimeAncestorToken { get; } = new(Module, "RuntimeAncestorToken");
    public static TextKey RuntimeDispatchNode { get; } = new(Module, "RuntimeDispatchNode");
    public static TextKey RuntimeMergeNode { get; } = new(Module, "RuntimeMergeNode");
    public static TextKey RuntimeProgress { get; } = new(Module, "RuntimeProgress");
    public static TextKey RuntimeCompleted { get; } = new(Module, "RuntimeCompleted");
    public static TextKey RuntimeParentNode { get; } = new(Module, "RuntimeParentNode");
    public static TextKey RuntimeChildWorkflow { get; } = new(Module, "RuntimeChildWorkflow");
    public static TextKey RuntimeState { get; } = new(Module, "RuntimeState");
    public static TextKey RuntimeElapsed { get; } = new(Module, "RuntimeElapsed");
    public static TextKey RuntimeRunId { get; } = new(Module, "RuntimeRunId");
    public static TextKey RuntimeSequence { get; } = new(Module, "RuntimeSequence");
    public static TextKey RuntimeTime { get; } = new(Module, "RuntimeTime");
    public static TextKey RuntimeNode { get; } = new(Module, "RuntimeNode");
    public static TextKey RuntimeStep { get; } = new(Module, "RuntimeStep");
    public static TextKey RuntimeMessage { get; } = new(Module, "RuntimeMessage");
    public static TextKey RuntimeOutput { get; } = new(Module, "RuntimeOutput");
    public static TextKey RuntimeExecutionCount { get; } = new(Module, "RuntimeExecutionCount");
    public static TextKey RuntimeTotalElapsed { get; } = new(Module, "RuntimeTotalElapsed");
    public static TextKey RuntimeAverageElapsed { get; } = new(Module, "RuntimeAverageElapsed");
    public static TextKey RuntimeFilterPlaceholder { get; } = new(Module, "RuntimeFilterPlaceholder");
    public static TextKey RuntimePauseTrace { get; } = new(Module, "RuntimePauseTrace");
    public static TextKey RuntimeDateRangeAccessible { get; } = new(Module, "RuntimeDateRangeAccessible");
    public static TextKey RuntimeRefresh { get; } = new(Module, "RuntimeRefresh");
    public static TextKey RuntimeExport { get; } = new(Module, "RuntimeExport");
    public static TextKey RuntimeResetFilters { get; } = new(Module, "RuntimeResetFilters");
    public static TextKey RuntimeSummaryEmpty { get; } = new(Module, "RuntimeSummaryEmpty");
    public static TextKey RuntimeSummary { get; } = new(Module, "RuntimeSummary");
    public static TextKey RuntimeRangeTooLong { get; } = new(Module, "RuntimeRangeTooLong");
    public static TextKey RuntimeYes { get; } = new(Module, "RuntimeYes");
    public static TextKey RuntimeNo { get; } = new(Module, "RuntimeNo");
    public static TextKey RuntimeCsvFilter { get; } = new(Module, "RuntimeCsvFilter");
    public static TextKey RuntimeExported { get; } = new(Module, "RuntimeExported");
    public static TextKey RuntimeExportFailedTitle { get; } = new(Module, "RuntimeExportFailedTitle");
    public static TextKey RuntimeExportFailed { get; } = new(Module, "RuntimeExportFailed");
    public static TextKey RuntimeAccessibleName { get; } = new(Module, "RuntimeAccessibleName");
    public static TextKey ExecutionStateIdle { get; } = new(Module, "ExecutionStateIdle");
    public static TextKey ExecutionStateRunning { get; } = new(Module, "ExecutionStateRunning");
    public static TextKey ExecutionStatePaused { get; } = new(Module, "ExecutionStatePaused");
    public static TextKey ExecutionStateCompleted { get; } = new(Module, "ExecutionStateCompleted");
    public static TextKey ExecutionStateFaulted { get; } = new(Module, "ExecutionStateFaulted");
    public static TextKey ExecutionStateCanceled { get; } = new(Module, "ExecutionStateCanceled");
    public static TextKey NodeStateIdle { get; } = new(Module, "NodeStateIdle");
    public static TextKey NodeStateRunning { get; } = new(Module, "NodeStateRunning");
    public static TextKey NodeStateCompleted { get; } = new(Module, "NodeStateCompleted");
    public static TextKey NodeStateFailed { get; } = new(Module, "NodeStateFailed");
    public static TextKey NodeStateCanceled { get; } = new(Module, "NodeStateCanceled");
    public static TextKey TraceStepNodeStarted { get; } = new(Module, "TraceStepNodeStarted");
    public static TextKey TraceStepNodeCompleted { get; } = new(Module, "TraceStepNodeCompleted");
    public static TextKey TraceStepNodeFailed { get; } = new(Module, "TraceStepNodeFailed");
    public static TextKey TraceStepParallelMerged { get; } = new(Module, "TraceStepParallelMerged");
    public static TextKey TraceStepScriptOutput { get; } = new(Module, "TraceStepScriptOutput");
}

/// <summary>组合 ModernUI 框架文本与 DP.WorkFlow WinForms 业务文本。</summary>
public static class WorkflowUiLocalization
{
    private static readonly ITextCatalog WorkflowCatalog = new ResourceManagerTextCatalog("workflowUi",
        new ResourceManager("DP.WorkFlow.UI.WinForms.Localization.WorkflowUiStrings", Assembly.GetExecutingAssembly()));
    private static readonly ITextCatalog Catalog = new CompositeTextCatalog(
        ModernUiLocalization.TextCatalog, PropertyGridLocalization.TextCatalog, WorkflowCatalog);
    public static ILocalizationManager CreateManager(CultureInfo initialCulture) =>
        new LocalizationManager(CreateSnapshot(initialCulture), (culture, _) => Task.FromResult(CreateSnapshot(culture)));
    private static LocalizationSnapshot CreateSnapshot(CultureInfo culture) => new(culture, Catalog,
        culture.TextInfo.IsRightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight);
}
