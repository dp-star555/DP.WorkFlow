using System.Text;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// 示例用的运行日志本地存储（只为测试“历史”）：每次运行一个文件，一行一条事件（<see cref="WorkflowRunEventJson"/>），
/// 放在 runlogs\日期\流程名\时分秒_运行ID.jsonl，保留 30 天。实际项目怎么存由宿主自己决定，只要实现 <see cref="IWorkflowRunLogHistory"/>。
/// </summary>
internal sealed class SampleRunLogFiles : IWorkflowRunLogHistory, IDisposable
{
    private const int KeepDays = 30;
    private static readonly WorkflowOutletOptions Filter = WorkflowRunLogBook.OutletOptions();
    private readonly object _sync = new();
    private readonly Dictionary<string, StreamWriter> _writers = new(StringComparer.Ordinal);

    public SampleRunLogFiles(string root)
    {
        Directory = Path.Combine(root, "runlogs");
        System.IO.Directory.CreateDirectory(Directory);
        DeleteExpired();
    }

    /// <summary>日志所在目录。</summary>
    public string Directory { get; }

    /// <summary>挂到流程管理每个流程的出口上（保留出口原有回调，和运行日志本收到的事件相同）。</summary>
    public void Attach(WorkflowRunCenter center)
    {
        foreach (var flow in center.Flows)
        {
            var name = flow.Name;
            var previous = flow.Outlet.OnEvent;
            flow.Outlet.OnEvent = item =>
            {
                previous?.Invoke(item);
                Save(name, item.Event);
            };
        }
    }

    /// <summary>包一层运行宿主的 Sink：事件照常交给 <paramref name="inner"/>，同时存到本地（名字每次启动时取）。</summary>
    public IWorkflowRunEventSink Wrap(IWorkflowRunEventSink inner, Func<string> flowName) => new Tee(this, inner, flowName);

    /// <summary>存一条事件：根运行开始时新开一个文件，其余接在该名字当前的文件后面；过滤和运行日志本一样。</summary>
    public void Save(string flowName, WorkflowRunEvent runEvent)
    {
        if (WorkflowOutlet.GetLevel(runEvent) < Filter.MinimumLevel || Filter.Categories?.Contains(runEvent.Category) == false) return;
        lock (_sync)
        {
            try
            {
                if (IsRootStart(runEvent))
                {
                    if (_writers.TryGetValue(flowName, out var old)) old.Dispose();
                    var started = runEvent.Timestamp.ToLocalTime();
                    var folder = Path.Combine(Directory, started.ToString("yyyy-MM-dd"), Safe(flowName));
                    System.IO.Directory.CreateDirectory(folder);
                    var file = Path.Combine(folder, $"{started:HHmmss}_{runEvent.RunId:N}.jsonl");
                    _writers[flowName] = new StreamWriter(file, append: true, new UTF8Encoding(false)) { AutoFlush = true };
                }
                if (_writers.TryGetValue(flowName, out var writer))
                    writer.WriteLine(WorkflowRunEventJson.Write(runEvent));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 示例存储：写不进就算了，不影响运行和内存里的运行日志。
                System.Diagnostics.Trace.TraceWarning("运行日志写文件失败：" + exception.Message);
            }
        }
    }

    public Task<IReadOnlyList<WorkflowRunLogHistoryEntry>> FindAsync(WorkflowRunLogHistoryQuery query, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<WorkflowRunLogHistoryEntry>>(() =>
        {
            var entries = new List<WorkflowRunLogHistoryEntry>();
            for (var day = query.From.ToLocalTime().Date; day < query.To.ToLocalTime(); day = day.AddDays(1))
            {
                var dayFolder = Path.Combine(Directory, day.ToString("yyyy-MM-dd"));
                if (!System.IO.Directory.Exists(dayFolder)) continue;
                foreach (var flowFolder in System.IO.Directory.GetDirectories(dayFolder))
                {
                    var name = Path.GetFileName(flowFolder);
                    if (query.FlowName is not null && !string.Equals(name, Safe(query.FlowName), StringComparison.Ordinal)) continue;
                    foreach (var file in System.IO.Directory.GetFiles(flowFolder, "*.jsonl"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (Describe(name, file) is { } entry && entry.Started >= query.From && entry.Started < query.To) entries.Add(entry);
                    }
                }
            }
            return entries.OrderByDescending(entry => entry.Started).Take(query.MaxResults).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<WorkflowRunEvent>> LoadAsync(WorkflowRunLogHistoryEntry entry, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<WorkflowRunEvent>>(() =>
        {
            lock (_sync)
                if (_writers.Values.FirstOrDefault(writer => writer.BaseStream is FileStream stream && stream.Name == entry.Key) is { } open) open.Flush();
            return ReadLines(entry.Key!).Select(WorkflowRunEventJson.Read).ToArray();
        }, cancellationToken);

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var writer in _writers.Values) writer.Dispose();
            _writers.Clear();
        }
    }

    // 列表只读首行（开始）和末行（结束和结果）。
    private static WorkflowRunLogHistoryEntry? Describe(string flowName, string file)
    {
        string? first = null, last = null;
        foreach (var line in ReadLines(file))
        {
            if (line.Length == 0) continue;
            first ??= line;
            last = line;
        }
        if (first is null) return null;
        var start = WorkflowRunEventJson.Read(first);
        var end = WorkflowRunEventJson.Read(last!);
        var state = end.ParentRunId is null && end.Parents is not { Count: > 0 } ? end.EventType switch
        {
            "RunCompleted" => WorkflowRunLogState.Succeeded,
            "RunCanceled" => WorkflowRunLogState.Canceled,
            "RunFaulted" => WorkflowRunLogState.Failed,
            _ => (WorkflowRunLogState?)null
        } : null;
        return new WorkflowRunLogHistoryEntry(flowName, start.RunId, start.Timestamp, state is null ? null : end.Timestamp,
            state, file);
    }

    // 正在写的文件也能读。
    private static IEnumerable<string> ReadLines(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private void DeleteExpired()
    {
        var oldest = DateTime.Today.AddDays(-KeepDays);
        foreach (var folder in System.IO.Directory.GetDirectories(Directory))
        {
            if (!DateTime.TryParseExact(Path.GetFileName(folder), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day) || day >= oldest) continue;
            try { System.IO.Directory.Delete(folder, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool IsRootStart(WorkflowRunEvent runEvent) =>
        runEvent.EventType == "RunStarted" && runEvent.Parents is not { Count: > 0 } && runEvent.ParentRunId is null;

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }

    private sealed class Tee(SampleRunLogFiles files, IWorkflowRunEventSink inner, Func<string> flowName) : IWorkflowRunEventSink
    {
        private string _current = string.Empty;

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(IReadOnlyList<WorkflowRunEvent> events, bool requestImmediateFlush, CancellationToken cancellationToken)
        {
            foreach (var runEvent in events)
            {
                if (IsRootStart(runEvent) || _current.Length == 0) _current = flowName();
                files.Save(_current, runEvent);
            }
            return inner.WriteAsync(events, requestImmediateFlush, cancellationToken);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    }
}
