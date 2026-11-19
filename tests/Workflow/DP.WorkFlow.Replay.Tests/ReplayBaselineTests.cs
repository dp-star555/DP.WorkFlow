using System.Runtime.CompilerServices;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests.Replay;

/// <summary>
/// R5-c 回放基准：打开 Flows/v4 中的流程文件，用模拟设备运行，规整后的事件序列必须与
/// Baselines/ 中的基准一致。基准由改造前的引擎生成，改造后对比，确保除刻意修改外行为不变。
/// </summary>
/// <remarks>
/// 刻意修改行为时：设置环境变量 <c>DP_WORKFLOW_UPDATE_BASELINES=1</c> 运行本工程测试，
/// 基准文件会被重写；提交时在说明中写清哪些差异是预期的。
/// </remarks>
public sealed class ReplayBaselineTests
{
    /// <summary>当前流程文件格式版本；格式升级时新增目录，旧目录保留，作为旧文件兼容样本。</summary>
    internal const string FlowFormatFolder = "v4";

    public static TheoryData<string> FlowNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in ReplayFlows.All.Keys)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(FlowNames))]
    public async Task Replay_MatchesBaseline(string flowName)
    {
        var store = new WorkflowDocumentJsonStore(ReplayFlows.CreateCatalog());
        var flowPath = ReplayPaths.Flow(FlowFormatFolder, flowName);
        if (ReplayPaths.UpdateRequested && !File.Exists(flowPath))
            ReplayPaths.Write(flowPath, store.Serialize(ReplayFlows.All[flowName]()));
        var loaded = store.Deserialize(File.ReadAllText(flowPath));

        var actual = await RunAsync(loaded.Document);

        var baselinePath = ReplayPaths.Baseline(flowName);
        if (ReplayPaths.UpdateRequested)
            ReplayPaths.Write(baselinePath, actual);
        Assert.True(File.Exists(baselinePath), $"缺少基准 {baselinePath}；设置 DP_WORKFLOW_UPDATE_BASELINES=1 生成。");
        Assert.Equal(ReplayPaths.ReadNormalized(baselinePath), actual);
    }

    [Theory]
    [MemberData(nameof(FlowNames))]
    public async Task Replay_IsDeterministicAcrossRuns(string flowName)
    {
        // 基准本身必须可重复：同一流程连跑两次，规整后的事件完全相同（排除并行交错、时间等不稳定因素）。
        var first = await RunAsync(ReplayFlows.All[flowName]());
        var second = await RunAsync(ReplayFlows.All[flowName]());

        Assert.Equal(first, second);
    }

    internal static async Task<string> RunAsync(WorkflowDocument document)
    {
        var sink = new CollectingSink();
        var plan = new WorkflowCompiler(ReplayFlows.CreateCatalog()).Compile(document);
        var engine = new WorkflowEngine(
            plan,
            ReplayFlows.CreateHandlers(),
            new WorkflowContext(ReplayFlows.CreateDevices()),
            new WorkflowExecutionOptions { Recording = new WorkflowRunRecordingOptions { Sink = sink } });

        await engine.RunAsync();

        return ReplayNormalizer.Normalize(sink.Events).Replace("\r\n", "\n");
    }

    private sealed class CollectingSink : IWorkflowRunEventSink
    {
        private readonly List<WorkflowRunEvent> _events = new();

        public IReadOnlyList<WorkflowRunEvent> Events
        {
            get
            {
                lock (_events)
                    return _events.ToArray();
            }
        }

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            lock (_events)
                _events.AddRange(events);
            return ValueTask.FromResult(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

/// <summary>基准与流程文件的位置：读写都在源码目录，生成的文件直接进入版本库。</summary>
internal static class ReplayPaths
{
    public static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("DP_WORKFLOW_UPDATE_BASELINES") == "1";

    public static string Flow(string formatFolder, string name) =>
        Path.Combine(ProjectDirectory(), "Flows", formatFolder, name + ".json");

    public static string Baseline(string name) =>
        Path.Combine(ProjectDirectory(), "Baselines", name + ".txt");

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n"));
    }

    public static string ReadNormalized(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n");

    private static string ProjectDirectory([CallerFilePath] string sourceFile = "") =>
        Path.GetDirectoryName(sourceFile)!;
}
