using DP.WorkFlow.Testing;

namespace DP.WorkFlow.Tests;

/// <summary>
/// R5-a：事件流规则检查器自身的测试。用手工构造的事件序列确认每条规则都能发现对应的违反，
/// 并且合规的序列不报错。检查器对真实运行的核对由程序集级 [WorkflowEventStreamCheck] 自动完成。
/// </summary>
public sealed class WorkflowEventStreamInvariantsTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public void AssemblyLevelCheck_IsActiveForEveryTest()
    {
        // 防止 xUnit 升级或工程配置变化让程序集级核对静默失效。
        Assert.True(WorkflowEventStreamCheckAttribute.IsActiveForCurrentTest);
    }

    [Fact]
    [SkipWorkflowEventStreamCheck("验证跳过特性本身")]
    public void SkipAttribute_DisablesCheckForThatTest()
    {
        Assert.False(WorkflowEventStreamCheckAttribute.IsActiveForCurrentTest);
    }

    [Fact]
    public void ValidRun_HasNoViolations()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeStarted", "A", 1)
            .Output("A", 1, outputSequence: 1)
            .Node("NodeCompleted", "A", 1)
            .Node("NodeStarted", "B", 1)
            .Input("B", 1, "A", 1)
            .Node("NodeCompleted", "B", 1)
            .Run("RunCompleted")
            .Build();

        Assert.Empty(WorkflowEventStreamInvariants.Check(events));
    }

    [Fact]
    public void ContinuedRun_AfterTerminal_IsAllowed()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Run("RunCompleted")
            .Run("RunContinued")
            .Node("NodeStarted", "A", 1)
            .Node("NodeCompleted", "A", 1)
            .Run("RunCompleted")
            .Build();

        Assert.Empty(WorkflowEventStreamInvariants.Check(events));
    }

    [Fact]
    public void NonEngineRun_IsNotChecked()
    {
        // 直接调用记录器写入的测试数据（第一条不是 RunStarted）不在核对范围内。
        var events = new Stream().Node("NodeCompleted", "A", 1).Build();

        Assert.Empty(WorkflowEventStreamInvariants.Check(events));
    }

    [Fact]
    public void SequenceGap_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Skip()
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("序号不连续"));
    }

    [Fact]
    public void NodeStartedWithoutEnd_IsReportedAtRunEnd()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeStarted", "A", 1)
            .Run("RunFaulted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("没有完成 / 失败 / 取消"));
    }

    [Fact]
    public void NodeEndedTwice_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeStarted", "A", 1)
            .Node("NodeCompleted", "A", 1)
            .Node("NodeFailed", "A", 1)
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("重复结束"));
    }

    [Fact]
    public void NodeEndWithoutStart_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeCompleted", "A", 1)
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("没有对应的 NodeStarted"));
    }

    [Fact]
    public void NodeDataOutsideExecution_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeStarted", "A", 1)
            .Node("NodeCompleted", "A", 1)
            .Output("A", 1, outputSequence: 1)
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("不在进行中"));
    }

    [Fact]
    public void InputFromUncommittedOutput_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeStarted", "B", 1)
            .Input("B", 1, "A", 7)
            .Node("NodeCompleted", "B", 1)
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("没有对应的 OutputCommitted"));
    }

    [Fact]
    public void EventAfterRunEnd_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Run("RunCompleted")
            .Node("NodeStarted", "A", 1)
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("运行结束之后"));
    }

    [Fact]
    public void FaultRoutedWithoutFailure_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeStarted", "A", 1)
            .Node("NodeCompleted", "A", 1)
            .Node("NodeFaultRouted", "A", 1)
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("之前没有 NodeFailed"));
    }

    [Fact]
    public void ParallelMergedWithoutStart_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Node("NodeStarted", "P", 1)
            .Node("NodeCompleted", "P", 1)
            .Structure("ParallelMerged", "P")
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("没有对应的 ParallelStarted"));
    }

    [Fact]
    public void PairedParallelAndPause_HaveNoViolations()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Run("Paused")
            .Run("Resumed")
            .Node("NodeStarted", "P", 1)
            .Structure("ParallelStarted", "P")
            .Node("NodeCompleted", "P", 1)
            .Structure("ParallelMerged", "P")
            .Run("RunCompleted")
            .Build();

        Assert.Empty(WorkflowEventStreamInvariants.Check(events));
    }

    [Fact]
    public void ResumedWithoutPause_IsReported()
    {
        var events = new Stream()
            .Run("RunStarted")
            .Run("Resumed")
            .Run("RunCompleted")
            .Build();

        Assert.Contains(WorkflowEventStreamInvariants.Check(events), item => item.Contains("此前没有暂停"));
    }

    [Fact]
    public void ChildStartedAfterParentEnded_IsReported()
    {
        var parentId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        var parent = new Stream(parentId, start)
            .Run("RunStarted")
            .Run("RunCompleted")
            .Build();
        var child = new Stream(Guid.NewGuid(), start.AddSeconds(1), parentId)
            .Run("RunStarted")
            .Run("RunCompleted")
            .Build();

        var violations = WorkflowEventStreamInvariants.Check(child, id => id == parentId ? parent : null);

        Assert.Contains(violations, item => item.Contains("父运行结束"));
    }

    /// <summary>按顺序拼一段事件，序号自动递增。</summary>
    private sealed class Stream
    {
        private readonly List<WorkflowRunEvent> _events = new();
        private readonly Guid _runId;
        private readonly Guid? _parentRunId;
        private DateTimeOffset _time;
        private long _sequence;

        public Stream(Guid? runId = null, DateTimeOffset? start = null, Guid? parentRunId = null)
        {
            _runId = runId ?? RunId;
            _time = start ?? DateTimeOffset.UtcNow;
            _parentRunId = parentRunId;
        }

        public Stream Skip()
        {
            _sequence++;
            return this;
        }

        public Stream Run(string type) =>
            Add(WorkflowRunEventCategory.Lifecycle, type, null, 0, null);

        public Stream Node(string type, string nodeId, int count) =>
            Add(type == "NodeFailed" ? WorkflowRunEventCategory.Fault : WorkflowRunEventCategory.NodeExecution, type, nodeId, count, null);

        public Stream Structure(string type, string nodeId) =>
            Add(WorkflowRunEventCategory.Trace, type, nodeId, 0, null);

        public Stream Output(string nodeId, int count, long outputSequence) =>
            Add(WorkflowRunEventCategory.DataFlow, "OutputCommitted", nodeId, count,
                new Dictionary<string, WorkflowTraceValue>
                {
                    ["OutputExecutionSequence"] = Scalar(outputSequence)
                });

        public Stream Input(string nodeId, int count, string sourceNodeId, long sourceSequence) =>
            Add(WorkflowRunEventCategory.DataFlow, "InputResolved", nodeId, count,
                new Dictionary<string, WorkflowTraceValue>
                {
                    ["SourceKind"] = new(WorkflowTraceValueKind.Scalar, "String", "NodeOutput", "NodeOutput"),
                    ["SourceNodeId"] = new(WorkflowTraceValueKind.Scalar, "String", sourceNodeId, sourceNodeId),
                    ["SourceOutputSequence"] = Scalar(sourceSequence)
                });

        public IReadOnlyList<WorkflowRunEvent> Build() => _events.ToArray();

        private static WorkflowTraceValue Scalar(long value) =>
            new(WorkflowTraceValueKind.Scalar, "Int64", value.ToString(), value);

        private Stream Add(
            WorkflowRunEventCategory category,
            string type,
            string? nodeId,
            int count,
            IReadOnlyDictionary<string, WorkflowTraceValue>? data)
        {
            var identity = nodeId is null
                ? null
                : new WorkflowExecutionIdentity(_runId, 1, Array.Empty<long>(), Array.Empty<long>(), count);
            _time = _time.AddMilliseconds(1);
            _events.Add(new WorkflowRunEvent(
                _runId, ++_sequence, _time, category, type, WorkflowEventWriteMode.Buffered, "测试",
                nodeId, nodeId is null ? null : "Test", identity, null, null, 1, data, _parentRunId));
            return this;
        }
    }
}
