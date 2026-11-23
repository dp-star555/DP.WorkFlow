namespace DP.WorkFlow.Tests;

public sealed class WorkflowMotionCatalogTests
{
    private static readonly WorkflowMotionCatalog Catalog = new(new[]
    {
        new WorkflowMotionObject(WorkflowMotionObjectKind.Axis, "X", "上料X", "card0", 1),
        new WorkflowMotionObject(WorkflowMotionObjectKind.AxisStep, "pick.down", "取料下降"),
        new WorkflowMotionObject(WorkflowMotionObjectKind.DigitalInput, "vac.ok", "真空到位", "card0", 3),
        new WorkflowMotionObject(WorkflowMotionObjectKind.DigitalOutput, "vac.on", "真空", "card0", 3),
        new WorkflowMotionObject(WorkflowMotionObjectKind.Cylinder, "push", "推料气缸")
    });

    [Fact]
    public void Choices_ComeFromCatalog_ByEditorKey()
    {
        Assert.Equal(new object?[] { "card0" }, Catalog.GetChoices(WorkflowMotionEditorKeys.AxisDevice, "DeviceId")!.Select(x => x.Value));
        var axis = Assert.Single(Catalog.GetChoices(WorkflowMotionEditorKeys.AxisNumber, "AxisId")!);
        Assert.Equal("1", axis.Value);
        Assert.Contains("上料X", axis.Key);
        Assert.Equal(new object?[] { "pick.down" }, Catalog.GetChoices(WorkflowMotionEditorKeys.AxisStep, "StepKey")!.Select(x => x.Value));
        Assert.Equal(2, Catalog.GetChoices(WorkflowMotionEditorKeys.IoIndex, "Index")!.Count);
        Assert.Null(Catalog.GetChoices("Host/Other", "X"));
    }

    [Fact]
    public void EnvironmentCheck_ReportsMissingLiteralObjects_AndSkipsWithoutCatalog()
    {
        var document = new WorkflowDocument { Name = "检查" };
        foreach (var node in new IWorkflowNodeModel[]
                 {
                     new AxisServoNodeModel { Id = "servo", Title = "使能", DeviceId = "card0", AxisId = "1" },
                     new AxisActionNodeModel { Id = "step", Title = "下降", StepKey = "pick.up" },
                     new IoWriteNodeModel { Id = "write", Title = "开真空", DriveId = "card0", Index = "4" },
                     new IoWaitNodeModel { Id = "wait", Title = "等真空", DriveId = "card0", Index = "3" },
                     new CylinderControlNodeModel { Id = "cyl", Title = "推料", CylinderName = "push" },
                     new AxisStopNodeModel { Id = "bound", Title = "绑定", DeviceIdSource = E_AxisValueSource.Binding, AxisIdSource = E_AxisValueSource.Binding }
                 })
            document.Nodes.Add(new WorkflowCanvasNode { Node = node });

        Assert.Empty(WorkflowEnvironmentCheck.Check(document, new WorkflowServiceProvider()));

        var issues = WorkflowEnvironmentCheck.Check(document, new WorkflowServiceProvider().Add<IWorkflowMotionCatalog>(Catalog));
        Assert.Equal(new[] { "step", "write" }, issues.Select(issue => issue.NodePath));
        Assert.Contains("pick.up", issues[0].Message);
        Assert.Contains("card0[4]", issues[1].Message);
    }

    [Fact]
    public void ServiceProvider_KeepsEveryChoiceSource()
    {
        var other = new WorkflowMotionCatalog(new[] { new WorkflowMotionObject(WorkflowMotionObjectKind.Vacuum, "v1") });
        var services = new WorkflowServiceProvider().AddPropertyChoices(Catalog).AddPropertyChoices(other);
        var sources = Assert.IsAssignableFrom<IEnumerable<IWorkflowPropertyChoiceSource>>(services.GetService(typeof(IEnumerable<IWorkflowPropertyChoiceSource>)));
        Assert.Equal(2, sources.Count());
    }
}
