using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// 示例的流程管理（相当于 Foundation 里的 WorkflowService 的精简版）：登记节点，读 flows\*.json 建流程管理，
/// 发布后重新加载；设计页的试运行用单独的运行宿主。
/// </summary>
internal sealed class SampleWorkflowHost : IDisposable
{
    private readonly WorkflowRuntimePluginCatalog _plugins;
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<WorkflowRuntimeHost> _trials = new();
    private readonly string _storageFile;
    private string _runLogStorage = string.Empty;

    /// <summary>运行日志保存方式：不保存（只在内存）。</summary>
    public const string NoStorage = "不保存";

    /// <summary>运行日志保存方式：文本文件（<see cref="SampleRunLogFiles"/>）。</summary>
    public const string TextStorage = "文本文件";

    public SampleWorkflowHost(string root)
    {
        FlowDirectory = Path.Combine(root, "flows");
        Directory.CreateDirectory(FlowDirectory);
        LogFiles = new SampleRunLogFiles(root);
        _storageFile = Path.Combine(root, "runlog-storage.txt");
        RunLogStorage = File.Exists(_storageFile) && File.ReadAllText(_storageFile).Trim() == NoStorage ? NoStorage : TextStorage;
        _plugins = new WorkflowRuntimePluginCatalog(new WorkflowNodeCatalog(), new WorkflowNodeHandlerCatalog())
            .Register(new WorkflowStandardRuntimePluginModule())
            .Register(new WorkflowCompositeRuntimePluginModule())
            .Register(new WorkflowProcessRuntimePluginModule())
            .Register(new SampleNodesModule())
            .Freeze();
    }

    /// <summary>已发布流程所在目录（流程库）。</summary>
    public string FlowDirectory { get; }

    public WorkflowNodeCatalog Nodes => _plugins.Nodes;

    public WorkflowRunCenterOptions Options { get; } = new() { FlowOutlet = WorkflowRunLogBook.OutletOptions() };

    public WorkflowRunLogBook RunLog { get; } = new();

    /// <summary>“文本文件”方式的存储（示例测试用），运行日志页的“历史”从这里读。</summary>
    public SampleRunLogFiles LogFiles { get; }

    /// <summary>
    /// 运行日志保存方式（相当于功能包的设置，存在 sample-data\runlog-storage.txt）：选了哪种就把哪种存储设到 <see cref="RunLog"/> 上；
    /// “不保存”时只在内存里，运行日志页也没有“历史”。给人看的 Trace 和这个无关。
    /// </summary>
    public string RunLogStorage
    {
        get => _runLogStorage;
        set
        {
            _runLogStorage = value == NoStorage ? NoStorage : TextStorage;
            RunLog.Store = _runLogStorage == TextStorage ? LogFiles : null;
            File.WriteAllText(_storageFile, _runLogStorage);
        }
    }

    public IServiceProvider Services { get; } = new WorkflowServiceProvider();

    public WorkflowRunCenter? Center { get; private set; }

    /// <summary>读不出的流程文件（文件名：原因）。</summary>
    public IReadOnlyList<string> LoadIssues { get; private set; } = Array.Empty<string>();

    /// <summary>流程管理重建后发生（界面线程）。</summary>
    public event EventHandler? CenterChanged;

    public bool IsRunning => Center?.Flows.Any(flow => flow.IsRunning) ?? false;

    public bool IsTrialRunning => _trials.Any(host => host.State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused);

    /// <summary>读全部已发布流程，换掉原来的流程管理。</summary>
    public void Load()
    {
        if (IsRunning) throw new InvalidOperationException("有流程在运行，先停止再重新加载。");
        var center = new WorkflowRunCenter(_plugins.Nodes, _plugins.Handlers, Options, Services);
        var store = new WorkflowDocumentJsonStore(_plugins.Nodes);
        var issues = new List<string>();
        _sources.Clear();
        foreach (var file in Directory.GetFiles(FlowDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var json = File.ReadAllText(file);
                var flow = center.AddFlow(store.Deserialize(json).Document);
                _sources[flow.Name] = json;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                issues.Add(Path.GetFileName(file) + "：" + exception.Message);
            }
        }
        RunLog.Attach(center);
        var old = Center;
        Center = center;
        LoadIssues = issues;
        old?.Dispose();
        CenterChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>流程管理加载时的文档（运行页按它画画布）。</summary>
    public WorkflowDocument? LoadedDocumentOf(string name) =>
        _sources.TryGetValue(name, out var json) ? new WorkflowDocumentJsonStore(_plugins.Nodes).Deserialize(json).Document : null;

    /// <summary>发布前检查草稿：能读、能编译。</summary>
    public IReadOnlyList<string> CheckDraft(string name, string json)
    {
        try
        {
            new WorkflowCompiler(_plugins.Nodes).Compile(new WorkflowDocumentJsonStore(_plugins.Nodes).Deserialize(json).Document);
            return Array.Empty<string>();
        }
        catch (WorkflowCompilationException exception)
        {
            return exception.Errors.Where(error => error.Severity == WorkflowValidationSeverity.Error).Select(error => error.Message).ToArray();
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return new[] { $"{name} 读不出：{exception.Message}" };
        }
    }

    public void StartAll()
    {
        if (IsTrialRunning) throw new InvalidOperationException("设计页在试运行，先停止试运行。");
        _ = Center?.StartAll();
    }

    public Task StopAllAsync() => Center?.StopAllAsync() ?? Task.CompletedTask;

    /// <summary>设计页试运行在运行日志里的名字前缀，后面接流程名，例如“试运行 · 上料”。</summary>
    public const string TrialRunLogName = "试运行";

    /// <summary>试运行在运行日志里的名字。</summary>
    /// <param name="flowName">流程名；还没保存的流程为空。</param>
    public static string TrialRunLogNameOf(string? flowName) =>
        TrialRunLogName + " · " + (string.IsNullOrWhiteSpace(flowName) ? "未保存的流程" : flowName);

    /// <summary>创建设计页试运行的宿主。</summary>
    /// <param name="logName">每次启动时取运行日志里的名字（按流程分开记，见 <see cref="TrialRunLogNameOf"/>）。</param>
    public WorkflowRuntimeHost CreateTrialHost(Func<string> logName)
    {
        // 试运行记进运行日志，按流程记在“试运行 · 流程名”下，和正式流程分开。
        WorkflowRuntimeHost? host = null;
        var options = new WorkflowExecutionOptions();
        options.Recording.Sink = RunLog.CreateSink(logName, () => host?.Plan);
        host = new WorkflowRuntimeHost(_plugins.Nodes, _plugins.Handlers, options);
        _trials.Add(host);
        return host;
    }

    public void Dispose()
    {
        foreach (var trial in _trials)
        {
            trial.Cancel();
            trial.Dispose();
        }
        Center?.Dispose();
        LogFiles.Dispose();
    }
}
