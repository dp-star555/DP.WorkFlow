using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

/// <summary>
/// R5-b 流程文件读写：每一种已注册节点的每一项可写配置，填入非默认值后保存再打开，必须原样保留。
/// 新增节点或配置项时自动纳入；R2-d、R3-a、R3-c 修改配置结构时，这里能立刻发现哪些项丢失。
/// </summary>
public sealed class WorkflowNodeConfigRoundTripTests
{
    public static TheoryData<string> NodeTypes()
    {
        var data = new TheoryData<string>();
        foreach (var nodeType in CreateCatalog().Snapshot().Keys.OrderBy(key => key, StringComparer.Ordinal))
            data.Add(nodeType);
        return data;
    }

    [Theory]
    [MemberData(nameof(NodeTypes))]
    public void EveryWritableSetting_SurvivesSaveAndLoad(string nodeType)
    {
        var catalog = CreateCatalog();
        var store = new WorkflowDocumentJsonStore(catalog);
        var descriptor = catalog.GetOrThrow(nodeType);
        var defaults = ConfigOf(store, descriptor.Factory());
        var node = descriptor.Factory();
        var populated = Populate(node);

        var saved = store.Serialize(Wrap(node));
        var loaded = store.Deserialize(saved);
        var reloadedConfig = ConfigOf(store, Assert.Single(loaded.Canvas.Nodes).Node);
        var savedConfig = ConfigOf(store, node);

        Assert.Empty(loaded.Migration.Warnings);
        Assert.Equal(savedConfig.ToJsonString(), reloadedConfig.ToJsonString());
        // 每一项填入的值都确实写进了文件（与默认值不同），而不是被序列化忽略。
        foreach (var property in populated)
            Assert.False(
                JsonNode.DeepEquals(defaults[property], savedConfig[property]),
                $"{nodeType}.{property} 填入非默认值后，保存内容与默认值相同（该项没有被保存）。");
    }

    [Fact]
    public void SaveLoadSave_IsStable()
    {
        var catalog = CreateCatalog();
        var store = new WorkflowDocumentJsonStore(catalog);
        var document = new WorkflowDocument { Name = "全部节点" };
        foreach (var descriptor in catalog.Snapshot().Values.OrderBy(item => item.NodeType, StringComparer.Ordinal))
        {
            var node = descriptor.Factory();
            Populate(node);
            _ = Wrap(node); // 补上子画布入口。
            node.Id = descriptor.NodeType;
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        }
        document.EntryNodeId = document.CanvasProjection.Nodes[0].Node.Id;

        var first = store.Serialize(document);
        var second = store.Serialize(store.Deserialize(first).Document);

        Assert.Equal(first, second);
    }

    [Fact]
    public void UnknownConfigKeys_AreIgnoredOnLoad()
    {
        // R7-b：以后去掉的配置项（例如 R2-d 去掉的 ResultVarKey），读取旧文件时直接忽略、不报错。
        var catalog = CreateCatalog();
        var store = new WorkflowDocumentJsonStore(catalog);
        var json = JsonNode.Parse(store.Serialize(Wrap(new ActionNodeModel { Id = "A", Title = "动作", FunctionKey = "Run" })))!;
        var config = json["Nodes"]![0]!["Config"]!.AsObject();
        config["RemovedSetting"] = "旧值";
        config["ResultVarKey"] = "AxisWaitResult";

        var loaded = store.Deserialize(json.ToJsonString());

        var action = Assert.IsType<ActionNodeModel>(Assert.Single(loaded.Canvas.Nodes).Node);
        Assert.Equal("Run", action.FunctionKey);
        Assert.DoesNotContain("RemovedSetting", store.Serialize(loaded.Document));
    }

    private static WorkflowNodeCatalog CreateCatalog() =>
        new WorkflowNodeCatalog()
            .RegisterStandardNodes()
            .RegisterCompositeNodes()
            .RegisterProcessNodes()
            .RegisterMotionNodes();

    private static WorkflowDocument Wrap(IWorkflowNodeModel node)
    {
        if (node is IWorkflowSubDocumentNode composite && string.IsNullOrEmpty(composite.SubDocument?.EntryNodeId))
        {
            // 子画布必须有入口才能保存；放一个开始节点。
            var child = new WorkflowDocument { Name = "子画布", EntryNodeId = "ChildStart" };
            child.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "ChildStart", Title = "开始" } });
            composite.SubDocument = child;
        }
        if (string.IsNullOrEmpty(node.Id))
            node.Id = "N";
        var document = new WorkflowDocument { Name = "单节点", EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        return document;
    }

    private static JsonObject ConfigOf(WorkflowDocumentJsonStore store, IWorkflowNodeModel node)
    {
        var json = JsonNode.Parse(store.Serialize(Wrap(node)))!;
        return json["Nodes"]![0]!["Config"]!.AsObject();
    }

    /// <summary>
    /// 给能自动构造的配置项填入非默认值：基本类型、字符串、枚举、<see cref="WorkflowInput{T}"/>（绑定）。
    /// 集合和嵌套对象由各节点自己的测试覆盖。
    /// </summary>
    /// <returns>已填写的配置项名称。</returns>
    private static IReadOnlyList<string> Populate(IWorkflowNodeModel node)
    {
        var populated = new List<string>();
        foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0
                || property.SetMethod is not { IsPublic: true }
                || property.Name is nameof(IWorkflowNodeModel.Id) or nameof(IWorkflowNodeModel.Title)
                || property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is not null)
                continue;
            var current = property.GetValue(node);
            if (!TryCreateDifferentValue(property.PropertyType, current, property.Name, out var value))
                continue;
            try
            {
                property.SetValue(node, value);
            }
            catch (TargetInvocationException)
            {
                continue; // 模型拒绝该值（有校验），跳过。
            }
            populated.Add(property.Name);
        }
        return populated;
    }

    private static bool TryCreateDifferentValue(Type type, object? current, string name, out object? value)
    {
        var core = Nullable.GetUnderlyingType(type) ?? type;
        value = null;
        if (core == typeof(string))
            value = "值_" + name;
        else if (core == typeof(bool))
            value = !(current as bool? ?? false);
        else if (core == typeof(int))
            value = (current as int? ?? 0) + 7;
        else if (core == typeof(long))
            value = (current as long? ?? 0) + 7;
        else if (core == typeof(double))
            value = (current as double? ?? 0) + 7.5;
        else if (core == typeof(TimeSpan))
            value = (current as TimeSpan? ?? TimeSpan.Zero) + TimeSpan.FromMilliseconds(1234);
        else if (core.IsEnum)
        {
            var values = Enum.GetValues(core).Cast<object>().ToArray();
            value = values.FirstOrDefault(item => !Equals(item, current));
        }
        else if (core.IsGenericType && core.GetGenericTypeDefinition() == typeof(WorkflowInput<>))
        {
            value = core.GetMethod(nameof(WorkflowInput<int>.FromBinding), BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { new WorkflowBindingKey("Source", name) });
        }
        return value is not null;
    }
}
