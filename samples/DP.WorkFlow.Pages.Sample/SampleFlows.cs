using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>第一次运行时写入的模拟流程：上料、检测、下料三条循环流程，带提示和偶发故障。</summary>
internal static class SampleFlows
{
    public static void SeedIfEmpty(SampleWorkflowHost host)
    {
        if (Directory.GetFiles(host.FlowDirectory, "*.json").Length > 0) return;
        var store = new WorkflowDocumentJsonStore(host.Nodes);
        var library = new WorkflowFlowLibrary(host.FlowDirectory);
        foreach (var document in new[]
                 {
                     Cycle("上料", ("取料", 500, 0, 0), ("移到对位", 350, 6, 0), ("放料", 400, 0, 0)),
                     Cycle("检测", ("等待到位", 300, 0, 0), ("拍照检测", 650, 0, 9), ("上报结果", 250, 0, 0)),
                     Cycle("下料", ("取成品", 450, 0, 0), ("下料输送", 700, 5, 0), ("计数", 150, 0, 14))
                 })
        {
            store.SaveToFile(document, library.PathOf(document.Name));
            library.RecordVersion(document.Name, "示例", "示例程序生成");
        }
    }

    // 开始 → 循环（不限次数）→ 各工位依次执行 → 回到循环；循环结束 → 结束。
    private static WorkflowDocument Cycle(string name, params (string Title, int Ms, int NoticeEvery, int FailEvery)[] steps)
    {
        var document = new WorkflowDocument { Name = name, EntryNodeId = "Start" };
        void Add(WorkflowNodeModel node, double x, double y) => document.Nodes.Add(new WorkflowCanvasNode { Node = node, X = x, Y = y });
        void Connect(string from, string port, string to) =>
            document.Connections.Add(new WorkflowConnectionModel { FromNodeId = from, FromPort = port, ToNodeId = to });

        Add(new StartNodeModel { Id = "Start", Title = "开始" }, 40, 160);
        Add(new LoopNodeModel { Id = "Loop", Title = "循环生产", Iterations = 0 }, 260, 160);
        var previous = "Loop";
        var port = WorkflowPorts.Loop;
        for (var i = 0; i < steps.Length; i++)
        {
            var id = "Step" + (i + 1);
            var (title, ms, notice, fail) = steps[i];
            Add(new SampleStationNodeModel { Id = id, Title = title, DurationMs = ms, NoticeEvery = notice, FailEvery = fail }, 500 + i * 240, 60);
            Connect(previous, port, id);
            previous = id;
            port = WorkflowPorts.Success;
        }
        Connect(previous, WorkflowPorts.Success, "Loop");
        Add(new EndNodeModel { Id = "End", Title = "结束" }, 500, 280);
        Connect("Start", WorkflowPorts.Success, "Loop");
        Connect("Loop", WorkflowPorts.Completed, "End");
        return document;
    }
}
