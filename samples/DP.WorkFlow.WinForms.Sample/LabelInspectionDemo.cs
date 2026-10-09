using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;
using DP.Vision.Algorithms;
using DP.WorkFlow;
using DP.WorkFlow.UI;

namespace WinFormsApp_test;

/// <summary>无需OCR模型的真实空白检查演示，只生成合成图，不携带现场数据。</summary>
internal static class LabelInspectionDemo
{
    /// <summary>两套预置配方分支的按需缓存演示；判断节点True选A，False选B。</summary>
    internal static void PopulateCache(WorkflowDesignerSession session)
    {
        Populate(session);
        var graph = session.Document.CanvasProjection;
        var a = (InspectLabelNodeModel)graph.Nodes.Single(n => n.Node.Id == "inspect").Node;
        a.Title = "配方A：空白检查";
        var b = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(a);
        b.Id = "inspect-b"; b.Title = "配方B：固定内容比对"; b.ReferenceImagePath = "clean.png";
        b.RecipeJson = new InspectionRecipeSerializer(new OpenCvImageCodec()).Serialize(new InspectionRecipe("固定内容演示B", 64, 48,
            EInspectionMode.Template, EAlignmentMode.AssumeAligned,
            new[] { new InspectionRegion("固定区", ERegionKind.Fixed, new PixelBounds(4, 4, 56, 40)) },
            new InspectionOptions(minimumContrast: 0, minimumSharpness: 0)));
        var select = new DecisionNodeModel { Id = "select", Title = "选择配方：True=A / False=B",
            ConditionSource = E_DecisionConditionSource.Binding, Condition = WorkflowInput<bool>.FromLiteral(true) };
        graph.Nodes.Single(n => n.Node.Id == "inspect").X = 660;
        graph.Nodes.Single(n => n.Node.Id == "inspect").Y = 40;
        graph.Nodes.Add(new() { Node = select, X = 360, Y = 100 });
        graph.Nodes.Add(new() { Node = b, X = 660, Y = 220 });
        graph.Connections.Clear();
        graph.Connections.Add(new() { FromNodeId = "image", FromPort = WorkflowPorts.Success, ToNodeId = select.Id, ToPort = WorkflowPorts.Input });
        graph.Connections.Add(new() { FromNodeId = select.Id, FromPort = WorkflowPorts.True, ToNodeId = a.Id, ToPort = WorkflowPorts.Input });
        graph.Connections.Add(new() { FromNodeId = select.Id, FromPort = WorkflowPorts.False, ToNodeId = b.Id, ToPort = WorkflowPorts.Input });
        session.Document.Name = "标签按需缓存演示（30秒闲置到期）";
    }

    internal static void Populate(WorkflowDesignerSession session)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "LabelDemo");
        Directory.CreateDirectory(root);
        var codec = new OpenCvImageCodec();
        var good = Enumerable.Repeat((byte)255, 64 * 48).ToArray();
        var bad = (byte[])good.Clone();
        for (int y = 12; y < 24; y++) for (int x = 12; x < 24; x++) bad[y * 64 + x] = 0;
        File.WriteAllBytes(Path.Combine(root, "clean.png"), codec.EncodePng(new PixelSnapshot(64, 48, EImagePixelFormat.Gray8, good)));
        File.WriteAllBytes(Path.Combine(root, "ink.png"), codec.EncodePng(new PixelSnapshot(64, 48, EImagePixelFormat.Gray8, bad)));
        var recipe = new InspectionRecipe("空白检查演示", 64, 48, EInspectionMode.Free, EAlignmentMode.AssumeAligned,
            new[] { new InspectionRegion("空白区", ERegionKind.Blank, new PixelBounds(4, 4, 56, 40)) },
            new InspectionOptions(minimumContrast: 0, minimumSharpness: 0));
        var image = new AcquireVisionImageNodeModel { Id = "image", Title = "读取合成标签", FilePath = Path.Combine(root, "clean.png") };
        var inspect = new InspectLabelNodeModel
        {
            Id = "inspect", Title = "标签检测", Frame = WorkflowInput<DP.Vision.ImageFrame>.FromBinding(new("image", "$")),
            ResourceRoot = root, AuthorImagePath = "clean.png", RecipeJson = new InspectionRecipeSerializer(codec).Serialize(recipe)
        };
        session.Document.Name = "标签检测节点演示（空白检查）";
        session.Document.EntryNodeId = image.Id;
        session.Document.CanvasProjection.Nodes.Add(new() { Node = image, X = 100, Y = 100 });
        session.Document.CanvasProjection.Nodes.Add(new() { Node = inspect, X = 380, Y = 100 });
        session.Document.CanvasProjection.Connections.Add(new() { FromNodeId = image.Id, FromPort = WorkflowPorts.Success,
            ToNodeId = inspect.Id, ToPort = WorkflowPorts.Input });
    }
}
