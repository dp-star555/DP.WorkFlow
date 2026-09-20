using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DP.WorkFlow.Persistence.Json;

/// <summary>
/// 提供 Schema 4 工作流文档持久化以及 Schema 1/2/3 的显式迁移。
/// </summary>
public sealed class WorkflowDocumentJsonStore
{
    /// <summary>当前流程文档版本。</summary>
    public const int CurrentSchemaVersion = 4;

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
    /// <returns>Schema 4 JSON。</returns>
    public string Serialize(WorkflowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateEntry(document);
        return JsonSerializer.Serialize(
            CreateDocument(
                document,
                new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance),
                allowEntryInference: false),
            _options);
    }

    /// <summary>加载当前或旧版流程 JSON，并返回迁移报告。</summary>
    public WorkflowLoadResult Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("流程 JSON 不能为空。", nameof(json));

        using var parsed = JsonDocument.Parse(json);
        var schemaVersion = parsed.RootElement.TryGetProperty("SchemaVersion", out var versionElement)
            ? versionElement.GetInt32()
            : 1;

        return schemaVersion >= 3
            ? ReadCurrent(json, schemaVersion)
            : ReadLegacy(json, schemaVersion);
    }

    /// <summary>以原子替换方式保存正式工作流文档。</summary>
    public void SaveToFile(WorkflowDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空。", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, Serialize(document));
        File.Move(temporaryPath, fullPath, true);
    }

    /// <summary>从文件加载流程，并保留迁移报告。</summary>
    public WorkflowLoadResult LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空。", nameof(filePath));
        return Deserialize(File.ReadAllText(filePath));
    }

    private WorkflowLoadResult ReadCurrent(string json, int schemaVersion)
    {
        if (schemaVersion > CurrentSchemaVersion)
            throw new NotSupportedException($"流程文档版本 {schemaVersion} 高于当前支持版本 {CurrentSchemaVersion}。");

        var document = JsonSerializer.Deserialize<WorkflowJsonDocument>(json, _options)
            ?? throw new InvalidOperationException("流程 JSON 反序列化失败。");
        var report = CreateReport(schemaVersion);
        var workflowDocument = ReadDocument(document, report, schemaVersion);
        if (schemaVersion < CurrentSchemaVersion)
            report.AddWarning($"Schema {schemaVersion} 已迁移到 Schema {CurrentSchemaVersion}，根入口已写入正式文档语义。");
        return new WorkflowLoadResult(workflowDocument, report);
    }

    private WorkflowLoadResult ReadLegacy(string json, int schemaVersion)
    {
        var legacy = JsonSerializer.Deserialize<LegacyWorkflowDocument>(json, _options)
            ?? throw new InvalidOperationException("旧版流程 JSON 反序列化失败。");
        var report = CreateReport(schemaVersion);
        var workflowDocument = new WorkflowDocument { Name = legacy.Name ?? string.Empty };
        var canvas = workflowDocument.CanvasProjection;

        foreach (var source in legacy.Nodes)
        {
            var nodeType = source.NodeKey?.Trim();
            if (string.IsNullOrWhiteSpace(nodeType))
                throw new InvalidOperationException($"旧版节点 {source.Id} 缺少 NodeKey。");
            var originalNodeType = nodeType;
            nodeType = NormalizeLegacyNodeType(nodeType);
            if (!string.Equals(originalNodeType, nodeType, StringComparison.Ordinal))
                report.AddWarning($"节点 {source.Id} 的旧类型 {originalNodeType} 已迁移为 {nodeType}。");
            if (!_catalog.TryGet(nodeType, out var descriptor))
                throw new NotSupportedException($"旧版节点 {source.Id} 的类型 {nodeType} 尚未注册，无法解释字符串属性。");

            var node = descriptor!.Factory();
            ApplyLegacyProperties(node, source.Properties, report);
            node.Id = source.Id ?? node.Id;
            if (string.IsNullOrWhiteSpace(node.Title))
                node.Title = node.Id;

            canvas.Nodes.Add(new WorkflowCanvasNode
            {
                Node = node,
                X = ReadLegacyDouble(source.Properties, "X"),
                Y = ReadLegacyDouble(source.Properties, "Y"),
                Width = ReadLegacyDouble(source.Properties, "Width", 180),
                Height = ReadLegacyDouble(source.Properties, "Height", 60)
            });
        }

        foreach (var source in legacy.Connections)
        {
            var connection = new WorkflowConnectionModel
            {
                FromNodeId = source.FromNodeId ?? string.Empty,
                FromPort = ConvertLegacyLabelToPort(source.Label),
                ToNodeId = source.ToNodeId ?? string.Empty,
                ToPort = WorkflowPorts.Input
            };
            foreach (var point in source.Waypoints)
                connection.Waypoints.Add(new WorkflowPoint(point.X, point.Y));
            canvas.Connections.Add(connection);
        }

        workflowDocument.EntryNodeId = ResolveCompatibilityEntry(workflowDocument);
        report.AddWarning($"旧版 Schema {schemaVersion} 已迁移到 Schema {CurrentSchemaVersion}；保存前请检查分支端口和节点配置。");
        return new WorkflowLoadResult(workflowDocument, report);
    }

    private void ApplyLegacyProperties(
        IWorkflowNodeModel node,
        IReadOnlyDictionary<string, string?> properties,
        WorkflowMigrationReport report)
    {
        var modelType = node.GetType();
        foreach (var pair in properties)
        {
            var property = modelType.GetProperty(pair.Key, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null && pair.Key.EndsWith("BindingKey", StringComparison.OrdinalIgnoreCase))
            {
                var migratedName = pair.Key[..^3];
                property = modelType.GetProperty(migratedName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            }
            if (property is null || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                if (pair.Key is not ("X" or "Y" or "Width" or "Height" or "NodeType"))
                    report.AddWarning($"节点 {node.Id}/{node.NodeType} 的旧属性 {pair.Key} 无法映射，已忽略。");
                continue;
            }

            try
            {
                property.SetValue(node, ConvertLegacyValue(pair.Value, property.PropertyType));
            }
            catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException or ArgumentException)
            {
                report.AddWarning($"节点 {node.Id}/{node.NodeType} 的属性 {pair.Key} 迁移失败：{exception.Message}");
            }
        }
    }

    private object? ConvertLegacyValue(string? raw, Type targetType)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var coreType = nullableType ?? targetType;
        if (raw is null)
            return nullableType is not null || !coreType.IsValueType ? null : Activator.CreateInstance(coreType);
        if (coreType == typeof(string))
            return raw;
        if (coreType.IsGenericType && coreType.GetGenericTypeDefinition() == typeof(WorkflowInput<>))
        {
            if (!WorkflowBindingKey.TryParse(raw, out var binding))
                throw new FormatException($"旧绑定键 '{raw}' 不是 nodeId|memberPath 格式。");
            return coreType.GetMethod("FromBinding", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { binding });
        }
        if (coreType.IsEnum)
            return Enum.Parse(coreType, raw, true);

        var converter = TypeDescriptor.GetConverter(coreType);
        if (converter.CanConvertFrom(typeof(string)))
            return converter.ConvertFrom(null, CultureInfo.InvariantCulture, raw);
        return JsonSerializer.Deserialize(raw, targetType, _options);
    }

    private WorkflowJsonDocument CreateDocument(
        WorkflowDocument workflowDocument,
        HashSet<WorkflowDocument> ancestors,
        bool allowEntryInference)
    {
        if (!ancestors.Add(workflowDocument))
            throw new InvalidOperationException("检测到子文档循环引用，无法序列化复合流程。");
        var canvas = workflowDocument.CanvasProjection;
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
                Config = node is UnknownWorkflowNodeModel unknownNode
                    ? unknownNode.RawConfig.Clone()
                    : JsonSerializer.SerializeToElement(node, descriptor!.ModelType, _options),
                SubDocument = node is IWorkflowSubDocumentNode composite
                    ? CreateChildDocument(
                        composite,
                        new HashSet<WorkflowDocument>(ancestors, ReferenceEqualityComparer.Instance),
                        allowEntryInference)
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
                State = connection.State,
                Diagnostic = connection.Diagnostic,
                Waypoints = connection.Waypoints
                    .Select(point => new WorkflowJsonPoint { X = point.X, Y = point.Y })
                    .ToList()
            });
        }
        return document;
    }

    private WorkflowDocument ReadDocument(
        WorkflowJsonDocument document,
        WorkflowMigrationReport report,
        int sourceSchemaVersion)
    {
        var workflowDocument = new WorkflowDocument { Name = document.Name };
        var canvas = workflowDocument.CanvasProjection;
        foreach (var source in document.Nodes)
        {
            IWorkflowNodeModel node;
            var nodeType = NormalizeLegacyNodeType(source.NodeType);
            if (!string.Equals(nodeType, source.NodeType, StringComparison.Ordinal))
                report.AddWarning($"节点 {source.Id} 的旧类型 {source.NodeType} 已迁移为 {nodeType}。");
            if (_catalog.TryGet(nodeType, out var descriptor))
            {
                var knownDescriptor = descriptor!;
                if (sourceSchemaVersion >= CurrentSchemaVersion && source.NodeVersion != knownDescriptor.NodeVersion)
                    throw new NotSupportedException($"节点 {source.Id}/{source.NodeType} 的版本必须为 {knownDescriptor.NodeVersion}，实际为 {source.NodeVersion}。");
                if (sourceSchemaVersion < CurrentSchemaVersion && source.NodeVersion > knownDescriptor.NodeVersion)
                    throw new NotSupportedException($"节点 {source.Id}/{source.NodeType} 的版本 {source.NodeVersion} 高于当前版本 {knownDescriptor.NodeVersion}。");
                var normalizedConfig = NormalizeLegacyBindingInputs(source.Config, knownDescriptor.ModelType, source.Id, nodeType, report);
                node = (IWorkflowNodeModel?)JsonSerializer.Deserialize(normalizedConfig, knownDescriptor.ModelType, _options)
                    ?? throw new InvalidOperationException($"节点配置反序列化失败：{source.Id}/{source.NodeType}。");
            }
            else
            {
                node = new UnknownWorkflowNodeModel(nodeType, source.NodeVersion, source.Config);
                report.AddWarning($"节点 {source.Id} 的类型 {nodeType} 未注册，已作为 Unknown 节点保留。");
            }

            node.Id = source.Id;
            node.Title = source.Title;
            var serializedChildDocument = source.SubDocument ?? source.SubCanvas;
            if (serializedChildDocument is not null)
            {
                if (node is IWorkflowSubDocumentNode composite)
                {
                    var childDocument = ReadDocument(serializedChildDocument, report, sourceSchemaVersion);
                    composite.SubDocument = childDocument;
                }
                else
                    report.AddWarning($"节点 {source.Id}/{source.NodeType} 带有子画布，但节点类型不支持子画布，已忽略。");
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
            canvas.Nodes.Add(canvasNode);
        }
        AppendConnections(canvas, document.Connections);
        workflowDocument.EntryNodeId = ResolveEntryNodeId(document.EntryNodeId, canvas, sourceSchemaVersion, report);
        return workflowDocument;
    }

    private WorkflowJsonDocument CreateChildDocument(
        IWorkflowSubDocumentNode composite,
        HashSet<WorkflowDocument> ancestors,
        bool allowEntryInference)
    {
        var childDocument = composite.SubDocument;
        if (!allowEntryInference)
            ValidateEntry(childDocument);
        var serialized = CreateDocument(childDocument, ancestors, allowEntryInference);
        serialized.EntryNodeId = !string.IsNullOrWhiteSpace(childDocument.EntryNodeId)
            ? childDocument.EntryNodeId
            : ResolveCompatibilityEntry(childDocument);
        return serialized;
    }

    private static void ValidateEntry(WorkflowDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.EntryNodeId))
            throw new InvalidOperationException("工作流文档必须显式设置 EntryNodeId。");
        if (!document.Graph.Nodes.Any(node => string.Equals(node.Id, document.EntryNodeId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"工作流入口节点不存在：{document.EntryNodeId}。");
    }

    private static string ResolveCompatibilityEntry(WorkflowDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.EntryNodeId)
            && document.Graph.Nodes.Any(node => string.Equals(node.Id, document.EntryNodeId, StringComparison.Ordinal)))
        {
            return document.EntryNodeId;
        }
        var starts = document.Graph.Nodes
            .Where(node => string.Equals(node.NodeType, "Start", StringComparison.Ordinal))
            .Select(node => node.Id)
            .ToArray();
        if (starts.Length == 1)
            return starts[0];
        return document.Graph.Nodes.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("空工作流文档无法推断入口节点。");
    }

    private static string ResolveEntryNodeId(
        string persistedEntryNodeId,
        WorkflowCanvasModel canvas,
        int sourceSchemaVersion,
        WorkflowMigrationReport report)
    {
        var nodeIds = canvas.Nodes.Select(item => item.Node.Id).ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(persistedEntryNodeId))
        {
            if (!nodeIds.Contains(persistedEntryNodeId))
                throw new InvalidOperationException($"工作流入口节点不存在：{persistedEntryNodeId}。");
            return persistedEntryNodeId;
        }
        if (sourceSchemaVersion >= CurrentSchemaVersion)
            throw new InvalidOperationException("Schema 4 工作流文档缺少 EntryNodeId。");

        var starts = canvas.Nodes
            .Where(item => string.Equals(item.Node.NodeType, "Start", StringComparison.Ordinal))
            .Select(item => item.Node.Id)
            .ToArray();
        var inferred = starts.Length == 1
            ? starts[0]
            : canvas.Nodes.FirstOrDefault()?.Node.Id
              ?? throw new InvalidOperationException("空工作流文档无法推断入口节点。");
        report.AddWarning($"旧文档缺少 EntryNodeId，已推断为 {inferred}。");
        return inferred;
    }

    private static JsonElement NormalizeLegacyBindingInputs(JsonElement config, Type modelType, string nodeId, string nodeType, WorkflowMigrationReport report)
    {
        if (config.ValueKind != JsonValueKind.Object) return config;
        var root = JsonNode.Parse(config.GetRawText())?.AsObject();
        if (root is null) return config;
        var changed = false;
        if (string.Equals(nodeType, "WarnJumpToNode", StringComparison.Ordinal)
            && root.TryGetPropertyValue("SafePointKey", out var legacyTarget)
            && legacyTarget is not null
            && !root.ContainsKey("TargetNodeId"))
        {
            root["TargetNodeId"] = legacyTarget.DeepClone();
            root.Remove("SafePointKey");
            changed = true;
            report.AddWarning($"节点 {nodeId}/{nodeType} 的旧属性 SafePointKey 已迁移为 TargetNodeId。");
        }
        foreach (var pair in root.ToArray())
        {
            if (!pair.Key.EndsWith("BindingKey", StringComparison.OrdinalIgnoreCase) || pair.Value is null) continue;
            var targetName = pair.Key[..^3];
            var target = modelType.GetProperty(targetName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (target is null || !target.PropertyType.IsGenericType || target.PropertyType.GetGenericTypeDefinition() != typeof(WorkflowInput<>)) continue;
            var raw = pair.Value.GetValue<string?>();
            if (string.IsNullOrWhiteSpace(raw) || !WorkflowBindingKey.TryParse(raw, out _)) continue;
            root[target.Name] = new JsonObject
            {
                [nameof(WorkflowInput<object>.Source)] = nameof(WorkflowValueSource.Binding),
                [nameof(WorkflowInput<object>.Binding)] = raw
            };
            root.Remove(pair.Key);
            changed = true;
            report.AddWarning($"节点 {nodeId}/{nodeType} 的旧属性 {pair.Key} 已迁移为 {target.Name}。");
        }
        changed |= MigrateCaptureNodeConfig(root, modelType, nodeId, nodeType, report);
        return changed ? JsonSerializer.SerializeToElement(root) : config;
    }

    /// <summary>
    /// 采集节点从"宿主设备键 + 裸参数"迁移为"逻辑源 + 带单位的中立请求"。
    /// 旧<see cref="bool"/>触发开关和裸曝光/增益没有单位，无法与Provider私有刻度区分，
    /// 因此这里只做一次性的语义映射，并把每次替换写进迁移报告，避免用户以为参数被静默丢弃。
    /// </summary>
    private static bool MigrateCaptureNodeConfig(
        JsonObject root,
        Type modelType,
        string nodeId,
        string nodeType,
        WorkflowMigrationReport report)
    {
        if (!string.Equals(nodeType, "Vision.CaptureFrame", StringComparison.Ordinal)) return false;
        var changed = false;
        if (root.TryGetPropertyValue("CameraId", out var legacyCameraId))
        {
            var sourceId = legacyCameraId?.GetValue<string?>();
            if (!root.ContainsKey("Source") && !string.IsNullOrWhiteSpace(sourceId))
                root["Source"] = new JsonObject { ["SourceId"] = sourceId.Trim() };
            root.Remove("CameraId");
            changed = true;
            report.AddWarning($"节点 {nodeId}/{nodeType} 的旧属性 CameraId 已迁移为逻辑源 Source；"
                + "请在机器配置中把该标识发布到本机实际使用的Provider。");
        }
        if (root.TryGetPropertyValue("Triggered", out var legacyTriggered))
        {
            if (!root.ContainsKey("TriggerMode"))
            {
                // 旧实现无论开关都显式写设备触发参数，因此 false 对应"自由运行"而不是"保持当前设置"。
                var external = legacyTriggered?.GetValue<bool>() == true;
                var mapped = CreateEnumValueNode(modelType, "TriggerMode", external ? "External" : "FreeRun");
                if (mapped is not null) root["TriggerMode"] = mapped;
            }
            root.Remove("Triggered");
            changed = true;
            report.AddWarning($"节点 {nodeId}/{nodeType} 的旧属性 Triggered 已迁移为 TriggerMode。");
        }
        changed |= MigrateLegacyPhysicalQuantity(root, modelType, "Exposure", "ExposureMicroseconds", "微秒", nodeId, nodeType, report);
        changed |= MigrateLegacyPhysicalQuantity(root, modelType, "Gain", "GainDecibels", "分贝", nodeId, nodeType, report);
        return changed;
    }

    /// <summary>旧属性用0表示"保持设备当前设置"；新契约用空值表达，不能把0当成有效物理量写入。</summary>
    private static bool MigrateLegacyPhysicalQuantity(
        JsonObject root,
        Type modelType,
        string legacyName,
        string targetName,
        string unit,
        string nodeId,
        string nodeType,
        WorkflowMigrationReport report)
    {
        if (!root.TryGetPropertyValue(legacyName, out var legacy)) return false;
        var value = legacy is null ? 0 : legacy.GetValue<double>();
        if (!root.ContainsKey(targetName) && value > 0)
            root[targetName] = JsonValue.Create(value);
        root.Remove(legacyName);
        report.AddWarning(value > 0
            ? $"节点 {nodeId}/{nodeType} 的旧属性 {legacyName} 已迁移为 {targetName}（{unit}）。"
            : $"节点 {nodeId}/{nodeType} 的旧属性 {legacyName}=0 表示保持设备当前设置，已迁移为 {targetName} 留空。");
        return true;
    }

    /// <summary>按模型属性类型把枚举名写成该枚举的底层数值，避免在持久化层引用节点所在程序集。</summary>
    private static JsonNode? CreateEnumValueNode(Type modelType, string propertyName, string enumMemberName)
    {
        var property = modelType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        var enumType = property is null ? null : Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (enumType is null || !enumType.IsEnum || !Enum.IsDefined(enumType, enumMemberName)) return null;
        var parsed = Enum.Parse(enumType, enumMemberName);
        return JsonValue.Create(Convert.ChangeType(parsed, Enum.GetUnderlyingType(enumType), CultureInfo.InvariantCulture));
    }

    private static string NormalizeLegacyNodeType(string nodeType) => nodeType switch
    {
        "RetryCurrentNode" => "WarnRetryCurrentNode",
        "ReturnToSafePoint" => "WarnJumpToNode",
        _ => nodeType
    };

    private WorkflowMigrationReport CreateReport(int sourceVersion) => new()
    {
        SourceSchemaVersion = sourceVersion,
        TargetSchemaVersion = CurrentSchemaVersion
    };

    private static string ConvertLegacyLabelToPort(string? label) =>
        string.IsNullOrWhiteSpace(label) ? WorkflowPorts.Success : label.Trim();

    private static double ReadLegacyDouble(
        IReadOnlyDictionary<string, string?> properties,
        string key,
        double defaultValue = 0) =>
        properties.TryGetValue(key, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    private static void AppendConnections(WorkflowCanvasModel canvas, IEnumerable<WorkflowJsonConnection> sources)
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
                LabelPosition = Math.Clamp(source.LabelPosition, 0, 1),
                State = source.State,
                Diagnostic = source.Diagnostic
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
