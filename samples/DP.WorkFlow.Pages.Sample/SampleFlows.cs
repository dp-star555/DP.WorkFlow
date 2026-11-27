using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// 第一次运行时写入的模拟流程：上料、检测、下料三条循环流程，带提示和偶发故障；
/// 另有一条“组装”，中间是一个子流程块，用来看子画布原地展开。缺哪条补哪条，已有的不覆盖。
/// </summary>
internal static class SampleFlows
{
    public static void SeedIfEmpty(SampleWorkflowHost host)
    {
        var store = new WorkflowDocumentJsonStore(host.Nodes);
        var library = new WorkflowFlowLibrary(host.FlowDirectory);
        var first = Directory.GetFiles(host.FlowDirectory, "*.json").Length == 0;
        var documents = first
            ? new[]
            {
                Cycle("上料", ("取料", 500, 0, 0), ("移到对位", 350, 6, 0), ("放料", 400, 0, 0)),
                Cycle("检测", ("等待到位", 300, 0, 0), ("拍照检测", 650, 0, 9), ("上报结果", 250, 0, 0)),
                Cycle("下料", ("取成品", 450, 0, 0), ("下料输送", 700, 5, 0), ("计数", 150, 0, 14)),
                Assembly()
            }
            : new[] { Assembly() };
        foreach (var document in documents)
        {
            var path = library.PathOf(document.Name);
            if (File.Exists(path)) continue;
            store.SaveToFile(document, path);
            library.RecordVersion(document.Name, "示例", "示例程序生成");
        }
    }

    // 开始 → 循环 → 取壳体 → 装配（子流程块：开始 → 压装 → 拧螺丝 → 结束）→ 检查 → 回到循环；循环结束 → 结束。
    private static WorkflowDocument Assembly()
    {
        var child = new WorkflowDocument { Name = "装配", EntryNodeId = "Start" };
        child.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" }, X = 0, Y = 0 });
        child.Nodes.Add(new WorkflowCanvasNode { Node = new SampleStationNodeModel { Id = "Press", Title = "压装", DurationMs = 400 }, X = 216, Y = 0 });
        child.Nodes.Add(new WorkflowCanvasNode { Node = new SampleStationNodeModel { Id = "Screw", Title = "拧螺丝", DurationMs = 550, NoticeEvery = 7 }, X = 432, Y = 0 });
        child.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" }, X = 648, Y = 0 });
        void ConnectChild(string from, string to) =>
            child.Connections.Add(new WorkflowConnectionModel { FromNodeId = from, FromPort = WorkflowPorts.Success, ToNodeId = to });
        ConnectChild("Start", "Press");
        ConnectChild("Press", "Screw");
        ConnectChild("Screw", "End");

        var document = new WorkflowDocument { Name = "组装", EntryNodeId = "Start" };
        void Add(WorkflowNodeModel node, double x, double y) => document.Nodes.Add(new WorkflowCanvasNode { Node = node, X = x, Y = y });
        void Connect(string from, string port, string to) =>
            document.Connections.Add(new WorkflowConnectionModel { FromNodeId = from, FromPort = port, ToNodeId = to });
        Add(new StartNodeModel { Id = "Start", Title = "开始" }, 40, 160);
        Add(new LoopNodeModel { Id = "Loop", Title = "循环生产", Iterations = 0 }, 260, 160);
        Add(new SampleStationNodeModel { Id = "Shell", Title = "取壳体", DurationMs = 300 }, 500, 60);
        Add(new BlockNodeModel { Id = "Assemble", Title = "装配（子流程）", SubDocument = child }, 740, 60);
        Add(new SampleStationNodeModel { Id = "Check", Title = "检查", DurationMs = 350 }, 1076, 60);
        Add(new EndNodeModel { Id = "End", Title = "结束" }, 500, 280);
        Connect("Start", WorkflowPorts.Success, "Loop");
        Connect("Loop", WorkflowPorts.Loop, "Shell");
        Connect("Shell", WorkflowPorts.Success, "Assemble");
        Connect("Assemble", WorkflowPorts.Success, "Check");
        Connect("Check", WorkflowPorts.Success, "Loop");
        Connect("Loop", WorkflowPorts.Completed, "End");
        return document;
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
