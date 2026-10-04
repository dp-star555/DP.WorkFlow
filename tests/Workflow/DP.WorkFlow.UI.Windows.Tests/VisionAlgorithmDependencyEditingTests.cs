using System.ComponentModel;
using DP.Vision.Algorithms;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionAlgorithmDependencyEditingTests
{
    private const string Prep = "Algorithm.algorithm.Dependency.prep";
    private const string Leaf = Prep + ".Dependency.leaf";

    [Fact]
    public void MissingPlugin_StillExposesStoredNestedParameters_WithoutChangingTheirIdentities()
    {
        var fixture = Create();
        fixture.Node.Algorithm.ImplementationId = "missing.root";
        fixture.Node.Algorithm.Settings["root.key"] = "root-value";
        fixture.Node.Algorithm.Dependencies["legacy.slot"] = new()
            { ImplementationId = "missing.dep", Settings = new() { ["private.key"] = "preserve" } };
        using var inspector = Inspector(fixture);
        var parameter = inspector.Entries.Single(entry => entry.Name == "Algorithm.algorithm.Dependency.legacy%2Eslot.RawParameter.private%2Ekey");
        Assert.Equal("preserve", parameter.Value);
        Assert.Contains("未识别参数", parameter.GroupPath);
        inspector.SetValue(parameter, "updated");
        Assert.Equal("updated", fixture.Node.Algorithm.Dependencies["legacy.slot"].Settings["private.key"]);
        Assert.True(fixture.Session.Undo());
        Assert.Equal("preserve", fixture.Node.Algorithm.Dependencies["legacy.slot"].Settings["private.key"]);
        Assert.True(fixture.Session.Redo());
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<DependencyNode>()));
        var restored = Assert.IsType<DependencyNode>(store.Deserialize(store.Serialize(fixture.Session.Document)).Document.CanvasProjection.Nodes.Single().Node);
        Assert.Equal("root-value", restored.Algorithm.Settings["root.key"]);
        Assert.Equal("updated", restored.Algorithm.Dependencies["legacy.slot"].Settings["private.key"]);
        Assert.Equal("missing.dep", restored.Algorithm.Dependencies["legacy.slot"].ImplementationId);
        Assert.Equal(0, fixture.Module.Preparations);
    }

    [Fact]
    public void BothDesktopPanels_ExpandParametersInline_AndKeepCollapsedGroupsThroughRebuild()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var fixture = Create();
                fixture.Node.Algorithm.Dependencies["prep"] = new() { ImplementationId = "test.prep" };
                var provider = WorkflowVisionAlgorithmProperties.CreateProvider(fixture.Catalog);
                using var host = new System.Windows.Forms.Form { ClientSize = new(520, 700), ShowInTaskbar = false,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new(-3000, -3000) };
                using var forms = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel
                    { AdditionalProperties = provider, Session = fixture.Session, EntryNodeId = fixture.Node.Id, Dock = System.Windows.Forms.DockStyle.Fill };
                host.Controls.Add(forms); host.Show(); System.Windows.Forms.Application.DoEvents();
                var grid = FormsDescendants(forms).OfType<ModernPropertyGrid.WinForms.ModernPropertyGrid>().Single();
                grid.AnimateCategoryExpansion = false;
                System.Windows.Forms.Control GainRow() => FormsDescendants(grid).Single(control => control.Tag is PropertyDescriptor descriptor && descriptor.Name == Prep + ".gain");
                ModernUI.WinForms.ModernButton Parameters() => FormsDescendants(grid).OfType<ModernUI.WinForms.ModernButton>().Single(button => button.AccessibleName == "初始化参数");
                Assert.True(GainRow().Visible);
                Assert.DoesNotContain(FormsDescendants(forms).OfType<System.Windows.Forms.Button>(), button => button.Text == "编辑集合/对象…");
                Parameters().PerformClick(); Assert.False(GainRow().Visible);
                grid.RefreshProperties(); System.Windows.Forms.Application.DoEvents(); Assert.False(GainRow().Visible);
                Parameters().PerformClick(); Assert.True(GainRow().Visible);
                var row = GainRow(); var body = row.Parent!;
                Assert.True(body.ClientRectangle.Contains(row.Bounds));
                WorkflowPropertyPanelIntegrationTests.Capture(forms, "parameters-inline.png");
                var wpf = new DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel
                    { AdditionalProperties = provider, Session = fixture.Session, EntryNodeId = fixture.Node.Id };
                System.Windows.Controls.Button WpfParameters() => WpfDescendants(wpf).OfType<System.Windows.Controls.Button>()
                    .Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "初始化参数");
                System.Windows.FrameworkElement WpfGainRow() => (System.Windows.FrameworkElement)WpfDescendants(wpf).OfType<System.Windows.Controls.TextBlock>()
                    .Single(label => label.Text == "增益").Parent;
                Assert.Equal(System.Windows.Visibility.Visible, WpfGainRow().Visibility);
                WpfParameters().RaiseEvent(new(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(System.Windows.Visibility.Collapsed, WpfGainRow().Visibility);
                wpf.AdditionalProperties = WorkflowVisionAlgorithmProperties.CreateProvider(fixture.Catalog);
                Assert.Equal(System.Windows.Visibility.Collapsed, WpfGainRow().Visibility);
                WpfParameters().RaiseEvent(new(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(System.Windows.Visibility.Visible, WpfGainRow().Visibility);
                wpf.Session = null;
                Assert.Equal(0, fixture.Module.Preparations);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    private static IEnumerable<System.Windows.Forms.Control> FormsDescendants(System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls)
        { yield return child; foreach (var nested in FormsDescendants(child)) yield return nested; }
    }

    private static IEnumerable<System.Windows.DependencyObject> WpfDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        { yield return child; foreach (var nested in WpfDescendants(child)) yield return nested; }
    }

    [Fact]
    public void AlgorithmParameters_AreInlineGroups_AndPreserveUnknownPluginSettingsThroughUndo()
    {
        var fixture = Create();
        fixture.Node.Algorithm.Dependencies["prep"] = new() { ImplementationId = "test.prep", Settings = new() { ["private.key"] = "preserve" } };
        using var inspector = Inspector(fixture);
        Assert.DoesNotContain(inspector.Entries.Where(entry => entry.Name.StartsWith("Algorithm.")),
            entry => entry.EditorKind == WorkflowPropertyEditorKind.Structured);
        var gain = Entry(inspector, Prep + ".gain");
        Assert.Contains("初始化参数", gain.GroupPath);
        var unknown = inspector.Entries.Single(entry => entry.DisplayName == "private.key");
        inspector.SetValue(unknown, "updated");
        Assert.Equal("updated", fixture.Node.Algorithm.Dependencies["prep"].Settings["private.key"]);
        Assert.True(fixture.Session.Undo());
        Assert.Equal("preserve", fixture.Node.Algorithm.Dependencies["prep"].Settings["private.key"]);
        Assert.True(fixture.Session.Redo());
        Assert.Equal("updated", fixture.Node.Algorithm.Dependencies["prep"].Settings["private.key"]);
        Assert.Equal(0, fixture.Module.Preparations);
    }

    [Fact]
    public void BothDesktopNodeEditors_ReceiveHostProperties_OnTheEditingCopy()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var fixture = Create();
                fixture.Node.Algorithm.Dependencies["prep"] = new() { ImplementationId = "test.prep" };
                var provider = WorkflowVisionAlgorithmProperties.CreateProvider(fixture.Catalog);
                var observed = new List<IWorkflowNodeModel>();
                IReadOnlyList<WorkflowPropertyEntry> Observe(IWorkflowNodeModel node) { observed.Add(node); return provider(node); }
                WorkflowPropertyChoiceProvider choices = (_, _) => [];
                var formsModel = new WorkflowNodeEditorModel(fixture.Session, fixture.Node.Id, fixture.Node.Id, null, choices, Observe);
                using (var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(formsModel))
                {
                    var panel = FindFormsPanel(dialog);
                    Assert.Same(choices, panel.ChoiceProvider);
                    Assert.NotNull(panel.AdditionalProperties);
                    Assert.Contains(observed, n => ReferenceEquals(n, formsModel.EditingNode));
                }
                observed.Clear();
                var wpfModel = new WorkflowNodeEditorModel(fixture.Session, fixture.Node.Id, fixture.Node.Id, null, choices, Observe);
                var window = new DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow(wpfModel);
                var wpfPanel = FindWpfPanel(window);
                Assert.Same(choices, wpfPanel.ChoiceProvider);
                Assert.Contains(observed, n => ReferenceEquals(n, wpfModel.EditingNode));
                Assert.DoesNotContain(observed, n => ReferenceEquals(n, fixture.Node));
                wpfPanel.Session = null;
                wpfModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Assert.Equal(0, fixture.Module.Preparations);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(UiTestThread.JoinBudgetSeconds))); Assert.Null(failure);
    }

    private static DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel FindFormsPanel(System.Windows.Forms.Control control)
    {
        if (control is DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel panel) return panel;
        foreach (System.Windows.Forms.Control child in control.Controls)
        {
            var found = FindFormsPanelOrNull(child);
            if (found is not null) return found;
        }
        throw new InvalidOperationException("No property panel.");
    }
    private static DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel? FindFormsPanelOrNull(System.Windows.Forms.Control control)
    {
        if (control is DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel panel) return panel;
        return control.Controls.Cast<System.Windows.Forms.Control>().Select(FindFormsPanelOrNull).FirstOrDefault(p => p is not null);
    }
    private static DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel FindWpfPanel(System.Windows.DependencyObject root)
    {
        var queue = new Queue<System.Windows.DependencyObject>(); queue.Enqueue(root);
        while (queue.TryDequeue(out var current))
        {
            if (current is DP.WorkFlow.UI.Wpf.WorkflowPropertyPanel panel) return panel;
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(current).OfType<System.Windows.DependencyObject>()) queue.Enqueue(child);
        }
        throw new InvalidOperationException("No property panel.");
    }

    [Fact]
    public void MissingDependency_SelectsExplicitly_ThenEditsNestedParameters_WithUndoAndSerialization()
    {
        var fixture = Create();
        using var inspector = Inspector(fixture);
        Assert.Empty(fixture.Node.Algorithm.Dependencies);
        Assert.Equal(string.Empty, Entry(inspector, Prep + ".ImplementationId").Value);
        Assert.Contains(Entry(inspector, Prep + ".ImplementationId").Choices, c => Equals(c.Value, "test.prep"));
        Assert.DoesNotContain(Entry(inspector, Prep + ".ImplementationId").Choices, c => Equals(c.Value, "test.root"));
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.prep");
        var gain = Entry(inspector, Prep + ".gain");
        inspector.SetValue(gain, "2.5");
        inspector.SetValue(Entry(inspector, Leaf + ".ImplementationId"), "test.leaf");
        Assert.Empty(new VisionAlgorithmInspection(fixture.Catalog).Analyze(Requests(fixture.Node), checkFiles: false).Issues);
        Assert.True(fixture.Session.Undo());
        Assert.Empty(fixture.Node.Algorithm.Dependencies["prep"].Dependencies);
        Assert.True(fixture.Session.Redo());
        Assert.Equal("test.leaf", fixture.Node.Algorithm.Dependencies["prep"].Dependencies["leaf"].ImplementationId);
        // 旧属性条目在撤销后的新选择图上写入，不持有旧对象。
        inspector.SetValue(gain, "3");
        Assert.Equal("3", fixture.Node.Algorithm.Dependencies["prep"].Settings["gain"]);
        var store = new WorkflowDocumentJsonStore(fixture.Session.Catalog);
        var loaded = Assert.IsType<DependencyNode>(Assert.Single(store.Deserialize(store.Serialize(fixture.Session.Document)).Document.CanvasProjection.Nodes).Node);
        Assert.Equal("3", loaded.Algorithm.Dependencies["prep"].Settings["gain"]);
        Assert.Equal("test.leaf", loaded.Algorithm.Dependencies["prep"].Dependencies["leaf"].ImplementationId);
        Assert.Equal(0, fixture.Module.Preparations);
    }

    [Fact]
    public void SwitchingDependency_RequiresExplicitReset_IsUndoable_AndRejectsStaleParameters()
    {
        var fixture = Create();
        using var inspector = Inspector(fixture);
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.prep");
        var oldGain = Entry(inspector, Prep + ".gain");
        inspector.SetValue(oldGain, "2");
        inspector.SetValue(Entry(inspector, Leaf + ".ImplementationId"), "test.leaf");
        Assert.Throws<InvalidOperationException>(() => inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.other"));
        Assert.Equal("2", fixture.Node.Algorithm.Dependencies["prep"].Settings["gain"]);
        inspector.SetValue(Entry(inspector, Prep + ".Reset"), true);
        Assert.Empty(fixture.Node.Algorithm.Dependencies["prep"].Settings);
        Assert.Empty(fixture.Node.Algorithm.Dependencies["prep"].Dependencies);
        Assert.True(fixture.Session.Undo());
        Assert.Equal("test.leaf", fixture.Node.Algorithm.Dependencies["prep"].Dependencies["leaf"].ImplementationId);
        Assert.True(fixture.Session.Redo());
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.other");
        Assert.Throws<InvalidOperationException>(() => inspector.SetValue(oldGain, "3"));
        Assert.Empty(fixture.Node.Algorithm.Dependencies["prep"].Settings);
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "");
        Assert.Empty(fixture.Node.Algorithm.Dependencies);
        Assert.Contains(new VisionAlgorithmInspection(fixture.Catalog).Analyze(Requests(fixture.Node), checkFiles: false).Issues,
            i => i.Code == "ALG_DEPENDENCY_MISSING");
    }

    [Fact]
    public void InvalidNestedValue_RollsBack_AndDoesNotCorruptNextEdit()
    {
        var fixture = Create();
        using var inspector = Inspector(fixture);
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.prep");
        var gain = Entry(inspector, Prep + ".gain");
        inspector.SetValue(gain, "2");
        Assert.Throws<ArgumentOutOfRangeException>(() => inspector.SetValue(gain, "99"));
        Assert.Equal("2", fixture.Node.Algorithm.Dependencies["prep"].Settings["gain"]);
        inspector.SetValue(gain, "3");
        Assert.Equal("3", fixture.Node.Algorithm.Dependencies["prep"].Settings["gain"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => inspector.SetValue(Entry(inspector, Prep + ".SettingsVersion"), 0));
        Assert.Equal(1, fixture.Node.Algorithm.Dependencies["prep"].SettingsVersion);
        inspector.SetValue(Entry(inspector, Prep + ".Reset"), true);
        inspector.SetValue(Entry(inspector, Prep + ".SettingsVersion"), 2);
        Assert.Throws<InvalidOperationException>(() => inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.other"));
        inspector.SetValue(Entry(inspector, Prep + ".Reset"), true);
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.other");
        Assert.Equal(1, fixture.Node.Algorithm.Dependencies["prep"].SettingsVersion);
    }

    [Fact]
    public void DynamicDependencies_ReprojectOnEdit_UnknownSelectionsRemainRepairable()
    {
        var fixture = Create();
        fixture.Node.Algorithm.Dependencies["obsolete"] = new() { ImplementationId = "missing.engine", Settings = new() { ["private"] = "preserve" } };
        using var inspector = Inspector(fixture);
        Assert.Contains(inspector.Entries, e => e.Name == "Algorithm.algorithm.Dependency.obsolete.Status");
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.prep");
        inspector.SetValue(Entry(inspector, Leaf + ".ImplementationId"), "test.leaf");
        inspector.SetValue(Entry(inspector, Prep + ".enabled"), false);
        Assert.Contains("没有声明", Entry(inspector, Leaf + ".Status").Value!.ToString());
        Assert.Equal("test.leaf", fixture.Node.Algorithm.Dependencies["prep"].Dependencies["leaf"].ImplementationId);
        inspector.SetValue(Entry(inspector, Leaf + ".ImplementationId"), "");
        Assert.Empty(fixture.Node.Algorithm.Dependencies["prep"].Dependencies);
        Assert.Equal("preserve", fixture.Node.Algorithm.Dependencies["obsolete"].Settings["private"]);
        Assert.Equal(0, fixture.Module.Preparations);
    }

    [Fact]
    public async Task NodeEditor_IsolatesDependencyEdits_CancelDiscards_ApplyIsOneUndoStep()
    {
        var fixture = Create();
        var provider = WorkflowVisionAlgorithmProperties.CreateProvider(fixture.Catalog);
        await using (var cancelled = new WorkflowNodeEditorModel(fixture.Session, fixture.Node.Id, fixture.Node.Id, null, null, provider))
        {
            using var properties = PageInspector(cancelled);
            properties.SetValue(Entry(properties, Prep + ".ImplementationId"), "test.prep");
            properties.SetValue(Entry(properties, Prep + ".gain"), "2");
            Assert.Empty(fixture.Node.Algorithm.Dependencies);
        }
        Assert.Empty(fixture.Node.Algorithm.Dependencies);
        await using (var editor = new WorkflowNodeEditorModel(fixture.Session, fixture.Node.Id, fixture.Node.Id, null, null, provider))
        {
            using var properties = PageInspector(editor);
            properties.SetValue(Entry(properties, Prep + ".ImplementationId"), "test.prep");
            properties.SetValue(Entry(properties, Prep + ".gain"), "3");
            properties.SetValue(Entry(properties, Leaf + ".ImplementationId"), "test.leaf");
            editor.ApplyChanges();
        }
        Assert.Equal("3", fixture.Node.Algorithm.Dependencies["prep"].Settings["gain"]);
        Assert.True(fixture.Session.Undo()); Assert.Empty(fixture.Node.Algorithm.Dependencies);
        Assert.True(fixture.Session.Redo()); Assert.Equal("3", fixture.Node.Algorithm.Dependencies["prep"].Settings["gain"]);
        Assert.Equal(0, fixture.Module.Preparations);
    }

    [Fact]
    public void Projection_DetectsCycles_AndDependencyDescriptorFailure_WithoutInitializingResources()
    {
        var fixture = Create();
        fixture.Node.Algorithm.Dependencies["prep"] = new() { ImplementationId = "test.prep" };
        fixture.Node.Algorithm.Dependencies["prep"].Dependencies["leaf"] = fixture.Node.Algorithm;
        var provider = WorkflowVisionAlgorithmProperties.CreateProvider(fixture.Catalog);
        Assert.Contains(provider(fixture.Node), e => e.Name == Leaf + ".Status" && e.Value!.ToString()!.Contains("循环"));
        fixture.Node.Algorithm.Dependencies["prep"].Dependencies.Clear();
        fixture.Node.Algorithm.Dependencies["prep"].Settings["explode"] = "true";
        fixture.Module.ValidationFailure = true;
        var entries = provider(fixture.Node);
        Assert.Contains(entries, e => e.Name == Prep + ".Status" && e.Value!.ToString()!.Contains("依赖描述"));
        Assert.Contains("invalid configuration", entries.Single(e => e.Name == Prep + ".Status").Value!.ToString());
        Assert.Equal(entries.Count, entries.Select(e => e.Name).Distinct().Count());
        Assert.Equal(0, fixture.Module.Preparations);
    }

    [Fact]
    public void DependencyAndParameterIdentities_DoNotCollide_WithReservedFieldsOrDots()
    {
        var fixture = Create();
        using var inspector = Inspector(fixture);
        inspector.SetValue(Entry(inspector, Prep + ".ImplementationId"), "test.prep");
        Assert.Contains(inspector.Entries, e => e.Name == Prep + ".Parameter.ImplementationId");
        Assert.Contains(inspector.Entries, e => e.Name == Prep + ".a%2Eb");
        Assert.Equal(inspector.Entries.Count, inspector.Entries.Select(e => e.Name).Distinct().Count());
    }

    private static WorkflowPropertyInspectorModel PageInspector(WorkflowNodeEditorModel editor)
    {
        var page = Assert.IsType<WorkflowPropertyEditorPageModel>(editor.Pages.Single(p => p.Kind == WorkflowNodeEditorPageKind.Properties).Model);
        return new(page.Session, page.EntryNodeId, page.ChoiceProvider, page.AdditionalProperties);
    }
    private static WorkflowPropertyEntry Entry(WorkflowPropertyInspectorModel inspector, string name) => inspector.Entries.Single(e => e.Name == name);
    private static IEnumerable<VisionAlgorithmRequest> Requests(DependencyNode node) => [new("node", typeof(IRootAlgorithm), node.Algorithm)];
    private static WorkflowPropertyInspectorModel Inspector(Fixture f) => new(f.Session, f.Node.Id, null, WorkflowVisionAlgorithmProperties.CreateProvider(f.Catalog));
    private static Fixture Create()
    {
        var module = new EditingModule();
        var catalog = VisionAlgorithmCatalog.Compose([module]);
        var node = new DependencyNode { Id = "algorithm" };
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<DependencyNode>())) { SelectedNodeId = node.Id };
        return new(node, session, catalog, module);
    }
    private sealed record Fixture(DependencyNode Node, WorkflowDesignerSession Session, VisionAlgorithmCatalog Catalog, EditingModule Module);
    public sealed class DependencyNode : WorkflowNodeModel, IWorkflowVisionAlgorithmNode
    {
        public override string NodeType => "Test.DependencyEditing";
        [Browsable(false)]
        public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "test.root" };
        public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => [new("algorithm", typeof(IRootAlgorithm), Algorithm)];
    }
    [VisionCapability("test.root", "测试", "组合")] public interface IRootAlgorithm { }
    [VisionCapability("test.prep", "测试", "预处理")] public interface IPrepAlgorithm { }
    [VisionCapability("test.leaf", "测试", "基础")] public interface ILeafAlgorithm { }
    private sealed class EditingModule : IVisionAlgorithmModule
    {
        public string ExtensionId => "test.editing";
        public int Preparations { get; private set; }
        public bool ValidationFailure { get; set; }
        public void Register(IVisionAlgorithmRegistration registrations)
        {
            registrations.Add(new("test.root", "Test", "1", Factory<IRootAlgorithm>(_ => [new("prep", typeof(IPrepAlgorithm))])));
            registrations.Add(new("test.prep", "Test", "1", Factory<IPrepAlgorithm>(config =>
            {
                if (config.Settings.ContainsKey("explode")) throw new InvalidOperationException("invalid dependency metadata");
                return config.Settings.TryGetValue("enabled", out var enabled) && enabled == "False"
                    ? [] : [new("leaf", typeof(ILeafAlgorithm))];
            }), parameters: [new("gain", "增益", typeof(double), "1", minimum: 1, maximum: 4),
                new("enabled", "启用", typeof(bool), "True"), new("ImplementationId", "保留字参数", typeof(string)), new("a.b", "带点参数", typeof(string))]));
            registrations.Add(new("test.other", "Other", "1", Factory<IPrepAlgorithm>(_ => [])));
            registrations.Add(new("test.leaf", "Test", "1", Factory<ILeafAlgorithm>(_ => [])));
        }
        private VisionAlgorithmFactory<T> Factory<T>(Func<VisionAlgorithmConfiguration, IReadOnlyList<VisionAlgorithmDependency>> dependencies) where T : class =>
            new VisionAlgorithmFactory<T>((_, _, _) => { Preparations++; throw new InvalidOperationException("Editing must not initialize resources."); }, dependencies)
                .WithConfigurationPolicy(_ => ValidationFailure ? ["invalid configuration"] : []);
    }
}
