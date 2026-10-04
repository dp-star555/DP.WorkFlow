using DP.Vision.Algorithms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>插件来源、静态选择检查和运行准备报告的共享投影。</summary>
public sealed class WorkflowVisionAlgorithmDiagnostics : IWorkflowDiagnosticProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly VisionAlgorithmRuntime _runtime;
    private readonly WorkflowVisionAlgorithmBindings _bindings;
    private readonly Func<VisionAlgorithmResourceContext?> _resources;
    private readonly Dictionary<Guid, WorkflowVisionAlgorithmPreparationReport> _reports = new();
    private readonly Dictionary<Guid, string> _reportBases = new();
    private readonly Dictionary<Guid, long> _reportRevisions = new();
    private long _configurationRevision;
    private string _runBasePath = "$";
    private IReadOnlyList<WorkflowDiagnosticItem> _checkIssues = Array.Empty<WorkflowDiagnosticItem>();
    private long _revision;
    private bool _disposed;
    private string _status = "已发现插件；尚未检查当前配方资源。";

    /// <summary>连接冻结目录、运行时与绑定通知，资源目录按当前配方捕获。</summary>
    public WorkflowVisionAlgorithmDiagnostics(VisionAlgorithmCatalog catalog, VisionAlgorithmRuntime runtime,
        WorkflowVisionAlgorithmBindings bindings, Func<VisionAlgorithmResourceContext?> resources)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _bindings.PreparationChanged += OnPreparation;
    }
    /// <summary>只读目录与成功来源。</summary>
    public VisionAlgorithmCatalog Catalog { get; }
    /// <summary>资源检查或最近准备阶段；不会把已登记误称为已就绪。</summary>
    public string Status { get { lock (_gate) return _status; } }
    /// <summary>在新运行配置前捕获目标路径，后台准备不读取活动导航器。</summary>
    public void SetRunBasePath(string path) { lock (_gate) _runBasePath = path; }
    /// <inheritdoc/>
    public event EventHandler? Changed;
    /// <summary>导出可人工复核的来源、元数据、路径上下文及诊断，不序列化工厂或活动资源。</summary>
    public string ExportReport(WorkflowDocument document) => System.Text.Json.JsonSerializer.Serialize(new
    {
        GeneratedAt = DateTimeOffset.Now,
        Status,
        Resources = _resources(),
        Implementations = Catalog.Implementations.Select(d => new
        {
            d.ImplementationId, d.Engine, d.Version, d.CapabilityId, d.DisplayName, d.Category, d.Features,
            Contract = d.ContractType.FullName,
            Origin = Catalog.Origins.TryGetValue(d.ImplementationId, out var origin) ? origin : null,
            Parameters = d.Parameters.Select(p => new { p.Id, p.DisplayName, Type = p.ValueType.FullName, p.DefaultValue, p.IsFilePath, p.Minimum, p.Maximum })
        }),
        Diagnostics = Analyze(document)
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    private void Notify()
    {
        foreach (var callback in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((EventHandler)callback)(this, EventArgs.Empty); } catch { /* 界面订阅失败不影响资源处理。 */ }
    }
    /// <inheritdoc/>
    public IReadOnlyList<WorkflowDiagnosticItem> Analyze(WorkflowDocument document)
    {
        var captured = Capture(document);
        var errors = captured.Errors.ToList();
        var report = new VisionAlgorithmInspection(Catalog).Analyze(captured.Requests, _resources());
        errors.AddRange(Project(report.Issues, captured.Locations, false, true));
        errors.AddRange(Catalog.Diagnostics.Select(d => new WorkflowDiagnosticItem("ALG_PLUGIN_LOAD_FAILED", WorkflowValidationSeverity.Warning,
            "插件登记失败：" + d.Source, null) { Phase = "Discovery", Detail = d.Reason, BlocksRun = false }));
        lock (_gate)
        {
            errors.AddRange(_checkIssues);
            foreach (var runtime in _reports.Values.Where(r => r.Status == "Failed"))
                errors.AddRange(Project(runtime.Issues, runtime.Locations.ToDictionary(l => l.BindingKey, StringComparer.Ordinal), true, false));
        }
        errors.AddRange(_runtime.ReleaseFailures.Select(error => new WorkflowDiagnosticItem("ALG_RELEASE_FAILED", WorkflowValidationSeverity.Warning,
            "算法资源释放出现异常。", null) { Phase = "Release", Detail = error.ToString(), BlocksRun = false }));
        return errors;
    }
    /// <inheritdoc/>
    public void Invalidate()
    {
        lock (_gate) { _revision++; _configurationRevision++; _checkIssues = Array.Empty<WorkflowDiagnosticItem>(); _reports.Clear(); _status = "配方已修改；需要按当前配置重新检查资源。"; }
        Notify();
    }
    /// <summary>手动完整资源检查；只持有候选计划，不提交、不执行节点、不打开采集作用域。</summary>
    public async Task CheckAsync(WorkflowDocument document, CancellationToken cancellationToken)
    {
        long revision;
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); revision = ++_revision; _status = "正在检查当前配方资源，可取消…"; _checkIssues = Array.Empty<WorkflowDiagnosticItem>(); }
        Captured? captured = null;
        Notify();
        try
        {
            captured = Capture(document);
            var resources = _resources();
            if (captured.Errors.Count != 0) throw new InvalidOperationException(string.Join("；", captured.Errors.Select(e => e.Message)));
            using var plan = await _runtime.PrepareAsync(captured.Requests, resources, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) if (!_disposed && _revision == revision) _status = "本次资源检查通过；候选资源已归还，正式运行仍会重新准备。";
        }
        catch (OperationCanceledException)
        {
            lock (_gate) if (!_disposed && _revision == revision) _status = "资源检查已取消。";
            throw;
        }
        catch (Exception error)
        {
            var issues = VisionAlgorithmExceptionDiagnostics.Read(error);
            var projected = Project(issues, captured?.Locations ?? new Dictionary<string, WorkflowVisionAlgorithmLocation>(), true, false).ToArray();
            if (projected.Length == 0) projected = new[] { new WorkflowDiagnosticItem("ALG_RESOURCE_CHECK_FAILED", WorkflowValidationSeverity.Error, error.Message, null)
                { Phase = "Preparation", Detail = error.ToString(), BlocksRun = false } };
            lock (_gate) if (!_disposed && _revision == revision) { _status = "资源检查失败，详情见诊断。"; _checkIssues = projected; }
            throw;
        }
        finally { Notify(); }
    }
    /// <summary>显式升级当前节点的所有选择；先全部生成副本，再进入可撤销的配置提交。</summary>
    public void MigrateNode(WorkflowDesignerSession session, string nodeId)
    {
        var node = session.Canvas.Nodes.Single(n => n.Node.Id == nodeId).Node;
        if (node is not IWorkflowVisionAlgorithmNode algorithm) throw new InvalidOperationException("所选节点没有算法配置。");
        var inspector = new VisionAlgorithmInspection(Catalog);
        var upgraded = algorithm.GetAlgorithmSlots().ToDictionary(s => s.Name, s => inspector.Migrate(s.Selection), StringComparer.Ordinal);
        session.ExecuteNodeConfigurationChange(nodeId, current =>
        {
            foreach (var slot in ((IWorkflowVisionAlgorithmNode)current).GetAlgorithmSlots())
            {
                var copy = upgraded[slot.Name];
                slot.Selection.ImplementationId = copy.ImplementationId; slot.Selection.SettingsVersion = copy.SettingsVersion;
                slot.Selection.Settings = copy.Settings; slot.Selection.Dependencies = copy.Dependencies;
            }
        });
    }
    private void OnPreparation(WorkflowVisionAlgorithmPreparationReport report)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (report.Status == "Preparing")
            {
                _reportRevisions[report.ScopeId] = _configurationRevision;
                _reportBases[report.ScopeId] = _runBasePath;
                while (_reportRevisions.Count > 64) { var old = _reportRevisions.Keys.First(); _reportRevisions.Remove(old); _reportBases.Remove(old); }
            }
            if (!_reportRevisions.TryGetValue(report.ScopeId, out var revision) || revision != _configurationRevision)
            {
                if (report.Status == "Released") { _reportRevisions.Remove(report.ScopeId); _reportBases.Remove(report.ScopeId); }
                return;
            }
            if (!_reportBases.TryGetValue(report.ScopeId, out var basePath)) _reportBases[report.ScopeId] = basePath = _runBasePath;
            report = report with { Locations = report.Locations.Select(l => l with { PlanPath = basePath + l.PlanPath[1..] }).ToArray() };
            _reports[report.ScopeId] = report;
            while (_reports.Count > 32) _reports.Remove(_reports.Keys.First());
            if (report.Status == "Released") { _reportRevisions.Remove(report.ScopeId); _reportBases.Remove(report.ScopeId); }
            _status = report.Status switch
            {
                "Preparing" => "正在准备本轮算法资源…", "Prepared" => "算法候选已准备，等待运行事务提交。",
                "Ready" => "本轮算法绑定已提交。", "Failed" => "本轮算法准备失败，详情见诊断。",
                "Cancelled" => "本轮准备已取消。", "Released" => "本轮算法计划已归还；在途调用由租约继续保护。", _ => report.Status
            };
        }
        Notify();
    }
    private static IEnumerable<WorkflowDiagnosticItem> Project(IEnumerable<VisionAlgorithmIssue> issues,
        IReadOnlyDictionary<string, WorkflowVisionAlgorithmLocation> locations, bool fromRoot, bool blocksRun)
    {
        foreach (var issue in issues)
        {
            locations.TryGetValue(issue.BindingKey, out var location);
            var phase = issue.Phase switch { "Inspection" => "配置检查", "Preparation" => "资源准备", _ => issue.Phase };
            var dependency = string.IsNullOrEmpty(issue.DependencyPath) ? "" : "（依赖：" + issue.DependencyPath + "）";
            yield return new WorkflowDiagnosticItem(issue.Code, WorkflowValidationSeverity.Error,
                $"[{phase}] {location?.PlanPath ?? "$"} / {location?.Slot ?? ""} {issue.ImplementationId}{dependency}：{issue.Message}", location?.NodeId)
            { PlanPath = location?.PlanPath ?? "$", FromRoot = fromRoot, Detail = issue.Detail, Phase = issue.Phase, BlocksRun = blocksRun };
        }
    }
    private static Captured Capture(WorkflowDocument document)
    {
        var requests = new List<VisionAlgorithmRequest>();
        var locations = new Dictionary<string, WorkflowVisionAlgorithmLocation>(StringComparer.Ordinal);
        var errors = new List<WorkflowDiagnosticItem>();
        void Visit(WorkflowDocument current, string path, HashSet<WorkflowDocument> ancestors)
        {
            if (ancestors.Count > 64 || !ancestors.Add(current)) { errors.Add(new("ALG_DOCUMENT_CYCLE", WorkflowValidationSeverity.Error, "子文档形成循环或超过深度预算。", null)); return; }
            try
            {
                foreach (var node in current.Graph.Nodes)
                {
                    if (node is IWorkflowVisionAlgorithmNode algorithm)
                        try
                        {
                            foreach (var slot in algorithm.GetAlgorithmSlots())
                            {
                                var key = WorkflowVisionAlgorithmBindings.Key(path, node.Id, slot.Name);
                                var selection = VisionAlgorithmInspection.Snapshot(slot.Selection);
                                requests.Add(new(key, slot.ContractType, selection, slot.RequiredFeatures));
                                locations[key] = new(key, path, node.Id, slot.Name);
                            }
                        }
                        catch (Exception error) { errors.Add(new("ALG_NODE_CONFIGURATION_INVALID", WorkflowValidationSeverity.Error, error.Message, node.Id) { PlanPath = path, Detail = error.ToString() }); }
                    if (node is IWorkflowSubDocumentNode child) Visit(child.SubDocument, path + "/" + Uri.EscapeDataString(node.Id), ancestors);
                }
            }
            finally { ancestors.Remove(current); }
        }
        Visit(document, "$", new()); return new(requests, locations, errors);
    }
    private sealed record Captured(IReadOnlyList<VisionAlgorithmRequest> Requests, IReadOnlyDictionary<string, WorkflowVisionAlgorithmLocation> Locations,
        IReadOnlyList<WorkflowDiagnosticItem> Errors);
    /// <summary>解除准备报告订阅。</summary>
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _revision++; } _bindings.PreparationChanged -= OnPreparation; }
}
