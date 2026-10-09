using System.ComponentModel;
using System.Text;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;
using DP.Vision.Algorithms;
using DP.WorkFlow;
using DP.WorkFlow.LabelInspection;
using DP.WorkFlow.UI;

namespace WinFormsApp_test;

/// <summary>单节点、多配方、同一运行内切换的合成演示；任务文件只决定下一周期，不是生产MES协议。</summary>
internal sealed class LabelRecipeSelectionDemo : IWorkflowRuntimePluginModule
{
    public string ExtensionId => "sample.label-recipe-selection";
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        extensions.Nodes.Register(WorkflowNodeDescriptor.Create<TaskNode, TaskSnapshot>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
        extensions.Handlers.Register(new TaskHandler());
    }
    internal static async Task PopulateAsync(WorkflowDesignerSession session)
    {
        var entryNodeId = session.Document.EntryNodeId; // 保留导航器既有入口，Start显式进入循环。
        LabelInspectionDemo.Populate(session);
        var graph = session.Document.CanvasProjection;
        var inspect = (InspectLabelNodeModel)graph.Nodes.Single(n => n.Node.Id == "inspect").Node;
        var root = inspect.ResourceRoot;
        var a = WorkflowLabelRecipeProfile.FromNode(inspect, "A", 1);
        var b = a with { Id = "B", ReferenceImagePath = "ink.png",
            RecipeJson = new InspectionRecipeSerializer(new OpenCvImageCodec()).Serialize(new InspectionRecipe("固定内容比对B（合成差异NG）", 64, 48,
                EInspectionMode.Template, EAlignmentMode.AssumeAligned,
                new[] { new InspectionRegion("固定区", ERegionKind.Fixed, new PixelBounds(4, 4, 56, 40)) },
                new InspectionOptions(minimumContrast: 0, minimumSharpness: 0))) };
        await WorkflowLabelRecipeCatalogStore.PublishAsync(root, "recipes/index.json", a);
        await WorkflowLabelRecipeCatalogStore.PublishAsync(root, "recipes/index.json", b);
        var taskFile = Path.Combine(root, "recipe-key.txt");
        if (!File.Exists(taskFile)) await File.WriteAllTextAsync(taskFile, "A");
        var loop = new LoopNodeModel { Id = "cycles", Title = "100个检测周期", Iterations = 100 };
        var task = new TaskNode { Id = "task", Title = "捕获本次配方/周期", RecipeKeyFile = taskFile };
        var delay = new DelayNodeModel { Id = "delay", Title = "1秒周期间隔", DelayMs = 1000 };
        inspect.Title = "单节点：按本次任务选配方";
        inspect.RecipeCatalogPath = "recipes/index.json";
        inspect.RecipeKey = WorkflowInput<string>.FromBinding(new("task", nameof(TaskSnapshot.RecipeKey)));
        inspect.CycleId = WorkflowInput<string>.FromBinding(new("task", nameof(TaskSnapshot.CycleId)));
        graph.Nodes.Single(n => n.Node.Id == "image").X = 620;
        graph.Nodes.Single(n => n.Node.Id == "inspect").X = 900;
        graph.Nodes.Add(new() { Node = loop, X = 60, Y = 100 });
        graph.Nodes.Add(new() { Node = task, X = 340, Y = 100 });
        graph.Nodes.Add(new() { Node = delay, X = 900, Y = 300 });
        graph.Connections.Clear();
        Connect(entryNodeId, WorkflowPorts.Success, loop.Id);
        Connect(loop.Id, WorkflowPorts.Loop, task.Id); Connect(task.Id, WorkflowPorts.Success, "image");
        Connect("image", WorkflowPorts.Success, inspect.Id); Connect(inspect.Id, WorkflowPorts.Success, delay.Id);
        Connect(delay.Id, WorkflowPorts.Success, loop.Id);
        session.SetEntryNode(entryNodeId);
        session.Document.Name = "单节点动态配方演示：运行时编辑LabelDemo/recipe-key.txt为A或B";
        void Connect(string from, string port, string to) => graph.Connections.Add(new()
        { FromNodeId = from, FromPort = port, ToNodeId = to, ToPort = WorkflowPorts.Input });
    }
    [WorkflowNode("Sample.LabelRecipeTask", DisplayName = "演示配方任务", Category = "演示")]
    public sealed class TaskNode : WorkflowNodeModel
    {
        public override string NodeType => "Sample.LabelRecipeTask";
        [WorkflowProperty("配方任务文件", "合成演示：每周期只读取一次，内容为A、B或ID@版本，不是生产MES协议。")]
        public string RecipeKeyFile { get; set; } = string.Empty;
    }
    public sealed record TaskSnapshot(string RecipeKey, string CycleId);
    private sealed class TaskHandler : WorkflowNodeHandler<TaskNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(TaskNode node, IWorkflowNodeExecutionContext context, CancellationToken token)
        {
            await using var file = new FileStream(node.RecipeKeyFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous);
            if (file.Length is < 1 or > 512) throw new InvalidDataException("配方任务文件须为1至512字节。");
            var bytes = new byte[checked((int)file.Length)]; await file.ReadExactlyAsync(bytes, token);
            var key = Encoding.UTF8.GetString(bytes).Trim().TrimStart('\uFEFF');
            return NodeExecutionResult.Continue(output: new TaskSnapshot(key, Guid.NewGuid().ToString("N")));
        }
    }
}
