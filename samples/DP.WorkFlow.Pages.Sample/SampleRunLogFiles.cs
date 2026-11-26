using System.Globalization;
using System.Text;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// 示例用的“文本文件”运行日志存储（只为测试“历史”）：只管存在哪，不碰内容。每次运行一个文本文件
/// runlogs\日期\流程名\时分秒_运行ID.jsonl，一行一条 Workflow 交来的数据（原样）；
/// 每天一个 runs.txt 记有哪些运行（开始、结束、结果、文件），查历史时用。保留 30 天。
/// 实际项目存在哪由宿主（功能包）按设置决定，只要实现 <see cref="IWorkflowRunLogStore"/>。
/// </summary>
internal sealed class SampleRunLogFiles : IWorkflowRunLogStore, IDisposable
{
    private const int KeepDays = 30;
    private const string IndexName = "runs.txt";
    private static readonly Encoding Utf8 = new UTF8Encoding(true);
    private readonly object _sync = new();
    private readonly Dictionary<string, OpenRun> _open = new(StringComparer.Ordinal);

    public SampleRunLogFiles(string root)
    {
        Directory = Path.Combine(root, "runlogs");
        System.IO.Directory.CreateDirectory(Directory);
        DeleteExpired();
    }

    /// <summary>日志所在目录。</summary>
    public string Directory { get; }

    public void Append(WorkflowRunLogRecord record)
    {
        lock (_sync)
        {
            if (!_open.TryGetValue(record.FlowName, out var run) || run.RunId != record.RunId)
            {
                run?.Writer.Dispose();
                var started = record.RunStarted.ToLocalTime();
                var day = Path.Combine(Directory, started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                var folder = Path.Combine(day, Safe(record.FlowName));
                System.IO.Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, $"{started:HHmmss}_{record.RunId:N}.jsonl");
                var writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8)
                    { AutoFlush = true };
                _open[record.FlowName] = run = new OpenRun(record.RunId, file, Path.Combine(day, IndexName), writer);
                Index(run, record, null);
            }
            run.Writer.WriteLine(record.Data);
            if (record.EndState is { } state) Index(run, record, state);
        }
    }

    public Task<IReadOnlyList<WorkflowRunLogHistoryEntry>> FindAsync(WorkflowRunLogHistoryQuery query, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<WorkflowRunLogHistoryEntry>>(() =>
        {
            var runs = new Dictionary<string, WorkflowRunLogHistoryEntry>(StringComparer.OrdinalIgnoreCase);
            for (var day = query.From.ToLocalTime().Date; day < query.To.ToLocalTime(); day = day.AddDays(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Path.Combine(Directory, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), IndexName);
                if (!File.Exists(index)) continue;
                // 一次运行开始、结束各记一行，后面的为准。
                foreach (var line in ReadLines(index))
                    if (Parse(line) is { } entry) runs[entry.Key!] = entry;
            }
            return runs.Values
                .Where(entry => entry.Started >= query.From && entry.Started < query.To
                                && (query.FlowName is null || string.Equals(entry.FlowName, query.FlowName, StringComparison.Ordinal)))
                .OrderByDescending(entry => entry.Started).Take(query.MaxResults).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<string>> ReadAsync(WorkflowRunLogHistoryEntry entry, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<string>>(() => ReadLines(entry.Key!).ToArray(), cancellationToken);

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var run in _open.Values) run.Writer.Dispose();
            _open.Clear();
        }
    }

    // runs.txt 一行：开始	结束	结果	流程	运行ID	文件（相对 runlogs），用制表符隔开。
    private void Index(OpenRun run, WorkflowRunLogRecord record, WorkflowRunLogState? ended)
    {
        var fields = new[]
        {
            record.RunStarted.ToString("o", CultureInfo.InvariantCulture),
            ended is null ? string.Empty : record.Time.ToString("o", CultureInfo.InvariantCulture),
            ended?.ToString() ?? string.Empty,
            record.FlowName.Replace('\t', ' '),
            record.RunId.ToString("N"),
            run.File.Substring(Directory.Length + 1)
        };
        File.AppendAllText(run.IndexFile, string.Join("\t", fields) + Environment.NewLine, Utf8);
    }

    private WorkflowRunLogHistoryEntry? Parse(string line)
    {
        var fields = line.Split('\t');
        if (fields.Length != 6 || !DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
            || !Guid.TryParse(fields[4], out var runId)) return null;
        DateTimeOffset? ended = DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end) ? end : null;
        WorkflowRunLogState? state = Enum.TryParse<WorkflowRunLogState>(fields[2], out var value) ? value : null;
        return new WorkflowRunLogHistoryEntry(fields[3], runId, started, ended, state, Path.Combine(Directory, fields[5]));
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
            if (!DateTime.TryParseExact(Path.GetFileName(folder), "yyyy-MM-dd", null, DateTimeStyles.None, out var day) || day >= oldest) continue;
            try { System.IO.Directory.Delete(folder, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }

    private sealed class OpenRun(Guid runId, string file, string indexFile, StreamWriter writer)
    {
        public Guid RunId { get; } = runId;
        public string File { get; } = file;
        public string IndexFile { get; } = indexFile;
        public StreamWriter Writer { get; } = writer;
    }
}
