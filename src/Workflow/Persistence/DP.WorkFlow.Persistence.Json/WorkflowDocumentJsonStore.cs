using System.Text.Json;

namespace DP.WorkFlow.Persistence.Json;

/// <summary>
/// 提供 Schema 5 工作流文档持久化；只读写当前版本。
/// </summary>
public sealed class WorkflowDocumentJsonStore
{
    /// <summary>当前流程文档版本。</summary>
    public const int CurrentSchemaVersion = 5;

    private readonly WorkflowNodeCatalog _catalog;
    private readonly JsonSerializerOptions _options;

    /// <summary>初始化 JSON 存储。</summary>
    public WorkflowDocumentJsonStore(WorkflowNodeCatalog catalog, JsonSerializerOptions? options = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options is null ? CreateDefaultOptions() : new JsonSerializerOptions(options);
        _options.PropertyNameCaseInsensitive = true;
        if (!_options.Converters.Any(converter => converter is WorkflowBindingKeyJsonConverter))
            _options.Converters.Add(new WorkflowBindingKeyJsonConverter());
    }

    /// <summary>将正式工作流文档序列化为当前版本 JSON。</summary>
    /// <param name="document">包含入口、语义图和布局的文档。</param>
    /// <returns>当前版本 JSON。</returns>
    public string Serialize(WorkflowDocument document)
    {
        Guard.NotNull(document);
        ValidateEntry(document);
        var root = CreateDocument(document, new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance));
        root.PublicData = document.PublicData
            .Select(item => new WorkflowJsonPublicData
            {
                Key = item.Key,
                Kind = item.Kind,
                ValueType = item.ValueType,
                DefaultValue = item.DefaultValueText,
                Persistent = item.Persistent,
                DisplayName = item.DisplayName,
                ResetOnRecovery = item.ResetOnRecovery
            })
            .ToList();
        return JsonSerializer.Serialize(root, _options);
    }

    /// <summary>加载当前版本流程 JSON，并返回加载报告（例如插件未加载的节点）。</summary>
    public WorkflowLoadResult Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("流程 JSON 不能为空。", nameof(json));

        using var parsed = JsonDocument.Parse(json);
        var schemaVersion = parsed.RootElement.TryGetProperty("SchemaVersion", out var versionElement)
            ? versionElement.GetInt32()
            : 0;
        if (schemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException($"流程文档版本 {schemaVersion} 不受支持，只支持版本 {CurrentSchemaVersion}。");

        var document = JsonSerializer.Deserialize<WorkflowJsonDocument>(json, _options)
            ?? throw new InvalidOperationException("流程 JSON 反序列化失败。");
        var warnings = new List<string>();
        var workflowDocument = ReadDocument(document, warnings);
        ReadPublicData(document, workflowDocument);
        return new WorkflowLoadResult(workflowDocument, warnings);
    }

    /// <summary>以原子替换方式保存正式工作流文档。</summary>
    public void SaveToFile(WorkflowDocument document, string filePath)
    {
        Guard.NotNull(document);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空。", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, Serialize(document));
        Polyfill.MoveFile(temporaryPath, fullPath, true);
    }

    /// <summary>从文件加载流程，并保留加载报告。</summary>
    public WorkflowLoadResult LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空。", nameof(filePath));
        return Deserialize(File.ReadAllText(filePath));
    }

    /// <summary>读入根文档的公共数据清单。</summary>
    private static void ReadPublicData(WorkflowJsonDocument source, WorkflowDocument target)
    {
        foreach (var item in source.PublicData ?? new List<WorkflowJsonPublicData>())
        {
            target.PublicData.Add(new WorkflowPublicDataDeclaration(
                item.Key?.Trim() ?? string.Empty,
                item.Kind,
                item.Kind == WorkflowPublicDataKind.Signal ? E_SignalValueType.Boolean : item.ValueType,
                item.DefaultValue,
                item.Persistent,
                item.DisplayName,
                item.ResetOnRecovery));
        }
    }

    private WorkflowJsonDocument CreateDocument(WorkflowDocument workflowDocument, HashSet<WorkflowDocument> ancestors)
    {
        if (!ancestors.Add(workflowDocument))
            throw new InvalidOperationException("检测到子文档循环引用，无法序列化复合流程。");
        var canvas = workflowDocument;
        var document = new WorkflowJsonDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Name = workflowDocument.Name,
            EntryNodeId = workflowDocument.EntryNodeId
        };
        foreach (var item in canvas.Nodes)
        {
            var node = item.Node;
            var descriptor = node is UnknownWorkflowNodeModel ? null : _catalog.GetOrThrow(node.NodeType);
            document.Nodes.Add(new WorkflowJsonNode
            {
                Id = node.Id,
                Title = node.Title,
                NodeType = node.NodeType,
                NodeVersion = node is UnknownWorkflowNodeModel unknown ? unknown.NodeVersion : descriptor!.NodeVersion,
                X = item.X,
                Y = item.Y,
                Width = item.Width,
                Height = item.Height,
                PortSides = new Dictionary<string, WorkflowPortSide>(item.PortSides, StringComparer.Ordinal),
                HiddenOutputPorts = item.HiddenOutputPorts.OrderBy(key => key, StringComparer.Ordinal).ToList(),
                ExposedOutputMembers = item.ExposedOutputMembers.OrderBy(key => key, StringComparer.Ordinal).ToList(),
                Config = node is UnknownWorkflowNodeModel unknownNode
                    ? unknownNode.RawConfig.Clone()
                    : JsonSerializer.SerializeToElement(node, descriptor!.ModelType, _options),
                SubDocument = node is IWorkflowSubDocumentNode composite
                    ? CreateChildDocument(composite, new HashSet<WorkflowDocument>(ancestors, ReferenceEqualityComparer.Instance))
                    : null
            });
        }
        foreach (var connection in canvas.Connections)
        {
            document.Connections.Add(new WorkflowJsonConnection
            {
                FromNodeId = connection.FromNodeId,
                FromPort = connection.FromPort,
                ToNodeId = connection.ToNodeId,
                ToPort = connection.ToPort,
                FromSide = connection.FromSide,
                ToSide = connection.ToSide,
                LabelPosition = connection.LabelPosition,
                Note = string.IsNullOrWhiteSpace(connection.Note) ? null : connection.Note,
                Disabled = connection.Enabled ? null : true,
                Waypoints = connection.Waypoints
                    .Select(point => new WorkflowJsonPoint { X = point.X, Y = point.Y })
                    .ToList()
            });
        }
        return document;
    }

    private WorkflowDocument ReadDocument(WorkflowJsonDocument document, List<string> warnings)
    {
        var workflowDocument = new WorkflowDocument { Name = document.Name };
        var canvas = workflowDocument;
        foreach (var source in document.Nodes)
        {
            IWorkflowNodeModel node;
            var nodeType = source.NodeType;
            if (_catalog.TryGet(nodeType, out var descriptor))
            {
                var knownDescriptor = descriptor!;
                if (source.NodeVersion != knownDescriptor.NodeVersion)
                    throw new NotSupportedException($"节点 {source.Id}/{source.NodeType} 的版本必须为 {knownDescriptor.NodeVersion}，实际为 {source.NodeVersion}。");
                node = (IWorkflowNodeModel?)JsonSerializer.Deserialize(source.Config, knownDescriptor.ModelType, _options)
                    ?? throw new InvalidOperationException($"节点配置反序列化失败：{source.Id}/{source.NodeType}。");
            }
            else
            {
                node = new UnknownWorkflowNodeModel(nodeType, source.NodeVersion, source.Config);
                warnings.Add($"节点 {source.Id} 的类型 {nodeType} 未注册，已作为 Unknown 节点保留。");
            }

            node.Id = source.Id;
            node.Title = source.Title;
            if (source.SubDocument is not null)
            {
                if (node is IWorkflowSubDocumentNode composite)
                    composite.SubDocument = ReadDocument(source.SubDocument, warnings);
                else
                    warnings.Add($"节点 {source.Id}/{source.NodeType} 带有子画布，但节点类型不支持子画布，已忽略。");
            }
            var canvasNode = new WorkflowCanvasNode
            {
                Node = node,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height
            };
            foreach (var pair in source.PortSides)
                canvasNode.PortSides[pair.Key] = pair.Value;
            foreach (var portKey in source.HiddenOutputPorts)
                canvasNode.HiddenOutputPorts.Add(portKey);
            foreach (var member in source.ExposedOutputMembers ?? new List<string>())
                canvasNode.ExposedOutputMembers.Add(member);
            canvas.Nodes.Add(canvasNode);
        }
        AppendConnections(canvas, document.Connections);
        workflowDocument.EntryNodeId = ResolveEntryNodeId(document.EntryNodeId, canvas);
        return workflowDocument;
    }

    private WorkflowJsonDocument CreateChildDocument(IWorkflowSubDocumentNode composite, HashSet<WorkflowDocument> ancestors)
    {
        ValidateEntry(composite.SubDocument);
        return CreateDocument(composite.SubDocument, ancestors);
    }

    private static void ValidateEntry(WorkflowDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.EntryNodeId))
            throw new InvalidOperationException("工作流文档必须显式设置 EntryNodeId。");
        if (!document.Graph.Nodes.Any(node => string.Equals(node.Id, document.EntryNodeId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"工作流入口节点不存在：{document.EntryNodeId}。");
    }

    private static string ResolveEntryNodeId(string persistedEntryNodeId, WorkflowDocument canvas)
    {
        if (string.IsNullOrWhiteSpace(persistedEntryNodeId))
            throw new InvalidOperationException("工作流文档缺少 EntryNodeId。");
        if (!canvas.Nodes.Any(item => string.Equals(item.Node.Id, persistedEntryNodeId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"工作流入口节点不存在：{persistedEntryNodeId}。");
        return persistedEntryNodeId;
    }

    private static void AppendConnections(WorkflowDocument canvas, IEnumerable<WorkflowJsonConnection> sources)
    {
        foreach (var source in sources)
        {
            var connection = new WorkflowConnectionModel
            {
                FromNodeId = source.FromNodeId,
                FromPort = source.FromPort,
                ToNodeId = source.ToNodeId,
                ToPort = source.ToPort,
                FromSide = source.FromSide,
                ToSide = source.ToSide,
                LabelPosition = Polyfill.Clamp(source.LabelPosition, 0, 1),
                Note = string.IsNullOrWhiteSpace(source.Note) ? null : source.Note,
                Enabled = source.Disabled != true
            };
            foreach (var point in source.Waypoints)
                connection.Waypoints.Add(new WorkflowPoint(point.X, point.Y));
            canvas.Connections.Add(connection);
        }
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = null,
            WriteIndented = true
        };
        options.Converters.Add(new WorkflowBindingKeyJsonConverter());
        return options;
    }
}
