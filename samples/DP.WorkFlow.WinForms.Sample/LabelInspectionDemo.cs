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
