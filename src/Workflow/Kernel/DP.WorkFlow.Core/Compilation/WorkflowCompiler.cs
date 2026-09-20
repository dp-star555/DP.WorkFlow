namespace DP.WorkFlow;

/// <summary>
/// 将可编辑工作流文档编译为隔离且只读的执行计划。
/// </summary>
public sealed class WorkflowCompiler
{
    private readonly WorkflowGraphValidator _validator;
    private readonly WorkflowNodeCatalog? _catalog;
    private readonly bool _validateBindings;

    /// <summary>初始化定义编译器；传入节点目录后启用端口和静态输出类型规则。</summary>
    /// <param name="catalog">节点类型目录；为空时跳过端口声明、连接基数和静态输出类型相关检查。</param>
    /// <param name="validateBindings">是否执行绑定来源必然性、成员路径和类型兼容性分析。</param>
    public WorkflowCompiler(
        WorkflowNodeCatalog? catalog = null,
        bool validateBindings = true)
    {
        _catalog = catalog;
        _validateBindings = validateBindings;
        _validator = new WorkflowGraphValidator(catalog);
    }

    /// <summary>校验并编译指定文档及其全部嵌套子文档。</summary>
    /// <param name="document">设计者可编辑的工作流文档；编译过程不会修改图或布局。</param>
    /// <returns>包含冻结节点配置、控制拓扑、并行作用域、子计划和诊断的执行计划。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> 为空。</exception>
    /// <exception cref="ArgumentException">文档入口为空或不属于语义图。</exception>
    /// <exception cref="InvalidOperationException">子文档存在引用循环，或子文档无法确定唯一入口。</exception>
    /// <exception cref="WorkflowCompilationException">结构、端口、并行或绑定分析产生阻塞错误。</exception>
    public WorkflowExecutionPlan Compile(WorkflowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CompileCore(
            document,
            new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>递归编译单个文档，并使用引用身份集合阻止子文档循环引用。</summary>
    private WorkflowExecutionPlan CompileCore(
        WorkflowDocument document,
        HashSet<WorkflowDocument> ancestors)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ancestors.Add(document))
            throw new InvalidOperationException("检测到子文档循环引用，无法编译复合流程。");
        var startNodeId = document.EntryNodeId;
        var diagnostics = _validator.Validate(document).ToList();
        var blockingErrors = diagnostics
            .Where(item => item.Severity == WorkflowValidationSeverity.Error)
            .ToArray();
        if (blockingErrors.Length > 0)
            throw new WorkflowCompilationException(blockingErrors);

        var graph = WorkflowGraphIndex.Create(document.Graph);
        var nodes = graph.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (!nodes.ContainsKey(startNodeId))
            throw new ArgumentException($"起始节点不存在：{startNodeId}。", nameof(startNodeId));

        diagnostics.AddRange(WorkflowNodeConfigurationValidator.Validate(nodes.Values));
        foreach (var entries in nodes.Values.Where(node => node is IWorkflowRecoveryEntryNode)
                     .GroupBy(node => ((IWorkflowRecoveryEntryNode)node).RecoveryEntryKey, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(entries.Key) || entries.Count() > 1)
                diagnostics.Add(new WorkflowValidationError("WFRE01", "恢复入口键必须非空且在当前文档内唯一。", entries.First().Id));
        }
        var outgoing = graph.OutgoingByPort.ToDictionary(pair => pair.Key, pair => pair.Value);
        var parallelScopes = WorkflowParallelScopeAnalyzer.Analyze(graph, diagnostics);
        if (_validateBindings)
        {
            var bindingAnalysis = new WorkflowBindingAnalyzer(_catalog).Analyze(
                startNodeId,
                graph,
                parallelScopes.Values.ToArray());
            diagnostics.AddRange(bindingAnalysis.Diagnostics);
        }
        var parallelErrors = diagnostics
            .Where(item => item.Severity == WorkflowValidationSeverity.Error)
            .ToArray();
        if (parallelErrors.Length > 0)
            throw new WorkflowCompilationException(parallelErrors);

        var reachableNodeIds = graph.CollectReachable(startNodeId);
        foreach (var nodeId in nodes.Keys.Where(nodeId => !reachableNodeIds.Contains(nodeId)))
        {
            diagnostics.Add(new WorkflowValidationError(
                "WF101",
                $"节点 {nodeId} 无法从起始节点 {startNodeId} 到达。",
                nodeId,
                WorkflowValidationSeverity.Warning));
        }

        var childPlans = new Dictionary<string, WorkflowExecutionPlan>(StringComparer.Ordinal);
        foreach (var composite in nodes.Values.OfType<IWorkflowSubDocumentNode>())
        {
            var childEntryNodeId = composite.SubDocument.EntryNodeId;
            if (_validateBindings && !string.IsNullOrWhiteSpace(childEntryNodeId))
            {
                diagnostics.AddRange(
                    new WorkflowBindingAnalyzer(_catalog).AnalyzeSubDocumentDeclarations(composite, childEntryNodeId));
            }
            childPlans[composite.Id] = CompileCore(
                composite.SubDocument,
                new HashSet<WorkflowDocument>(ancestors, ReferenceEqualityComparer.Instance));
        }

        var finalErrors = diagnostics
            .Where(item => item.Severity == WorkflowValidationSeverity.Error)
            .ToArray();
        if (finalErrors.Length > 0)
            throw new WorkflowCompilationException(finalErrors);

        return new WorkflowExecutionPlan(
            document.Name,
            startNodeId,
            nodes,
            outgoing,
            parallelScopes,
            childPlans,
            diagnostics);
    }

}

/// <summary>表示工作流定义因结构校验失败而无法编译。</summary>
public sealed class WorkflowCompilationException : Exception
{
    /// <summary>使用阻止生成运行定义的全部校验错误初始化异常。</summary>
    /// <param name="errors">严重程度为 Error 的结构、并行或绑定诊断；调用方可用于定位画布节点。</param>
    public WorkflowCompilationException(IReadOnlyList<WorkflowValidationError> errors)
        : base("工作流结构校验失败：" + string.Join("; ", errors.Select(error => error.Message)))
    {
        Errors = errors;
    }

    /// <summary>获取全部结构错误。</summary>
    public IReadOnlyList<WorkflowValidationError> Errors { get; }
}
