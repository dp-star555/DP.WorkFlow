using System.ComponentModel;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptEngine;

namespace DP.WorkFlow;

/// <summary>在宿主进程内执行受信任 Roslyn C# 脚本。</summary>
[WorkflowNode("CSharpScript", DisplayName = "C# 脚本", Category = "2.Function/脚本")]
public sealed class CSharpScriptNodeModel : WorkflowNodeModel, IWorkflowDynamicPortProvider, IWorkflowScriptReferenceNode
{
    /// <inheritdoc />
    public override string NodeType => "CSharpScript";

    /// <inheritdoc />
    public string ScriptId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>脚本正文；最后一个表达式或 return 值作为节点输出。</summary>
    public string Script { get; set; } = WorkflowCSharpProgramSource.DefaultSource;

    /// <inheritdoc />
    public string ScriptLanguage => "CSharp";

    /// <inheritdoc />
    public IList<string> ScriptReferencePaths { get; set; } = new List<string>();

    /// <summary>非空时将脚本返回值写入该流程变量。</summary>
    public string ResultVarKey { get; set; } = "ScriptResult";

    /// <summary>协作式执行超时；小于等于零表示只使用工作流取消令牌。</summary>
    public int TimeoutMilliseconds { get; set; } = 30000;

    /// <summary>脚本异常时是否沿 Error 输出继续；否则流程失败。</summary>
    public bool ContinueOnError { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<WorkflowPortDescriptor> GetPorts(IReadOnlyList<WorkflowPortDescriptor> basePorts) =>
        ContinueOnError
            ? basePorts
            : basePorts.Where(port => port.Direction != WorkflowPortDirection.Output || port.Key != "Error").ToArray();
}

/// <summary>工作流 C# 程序模板及旧顶层脚本迁移。</summary>
public static class WorkflowCSharpProgramSource
{
    public const string DefaultSource = """
using ScriptEngine;
using DP.WorkFlow;

public sealed class WorkflowProgram : ICSharpProgram
{
    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        var workflow = context.GetHostContext<WorkflowScriptGlobals>();

        workflow.Console.WriteLine("运行成功");
        return null;
    }
}
""";

    public static string Normalize(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return DefaultSource;
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var hasProgramContract = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .SelectMany(type => type.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
            .Any(type => type.Type.ToString().EndsWith(nameof(ICSharpProgram), StringComparison.Ordinal));
        if (hasProgramContract) return source;
        var existingUsings = ScriptEngine.RoslynScriptService.GetUsings(source);
        var body = ScriptEngine.RoslynScriptService.SetUsings(source, Array.Empty<string>());
        var usings = existingUsings.Concat(new[] { "ScriptEngine", "DP.WorkFlow" })
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal)
            .Select(item => $"using {item};");
        var indentedBody = string.Join(Environment.NewLine, body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "        " + line));
        return $$"""
{{string.Join(Environment.NewLine, usings)}}

public sealed class WorkflowProgram : ICSharpProgram
{
    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        var workflow = context.GetHostContext<WorkflowScriptGlobals>();
        var Context = workflow.Context;
        var Console = workflow.Console;
        var CancellationToken = cancellationToken;
#pragma warning disable CS8321
        T? GetVariable<T>(string key) => workflow.GetVariable<T>(key);
        T? GetService<T>() where T : class => workflow.GetService<T>();
        void SetVariable(string key, object value) => workflow.SetVariable(key, value);
        bool RemoveVariable(string key) => workflow.RemoveVariable(key);
        void Trace(string message) => workflow.Trace(message);
#pragma warning restore CS8321

{{indentedBody}}
    }
}
""";
    }
}

/// <summary>Roslyn 脚本节点标准输出。</summary>
public sealed record CSharpScriptNodeResult([property: DisplayName("是否成功")] bool Success, [property: DisplayName("返回值")] object? Value, [property: DisplayName("错误")] string? Error, [property: DisplayName("诊断")] IReadOnlyList<string> Diagnostics);

/// <summary>将脚本标准输出写入当前节点的运行 Trace，避免修改进程级 Console.Out。</summary>
public sealed class WorkflowScriptConsole : IWorkflowScriptConsole
{
    private readonly IWorkflowNodeExecutionContext _context;

    internal WorkflowScriptConsole(IWorkflowNodeExecutionContext context) => _context = context;

    public void Write(object? value) => WriteCore(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, false);
    public void Write(string? value) => WriteCore(value ?? string.Empty, false);
    public void WriteLine() => WriteCore(string.Empty, true);
    public void WriteLine(object? value) => WriteCore(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, true);
    public void WriteLine(string? value) => WriteCore(value ?? string.Empty, true);
    public void WriteLine(string format, params object?[] args) => WriteCore(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args), true);

    private void WriteCore(string message, bool newLine) =>
        _context.Trace("ScriptOutput", message, new Dictionary<string, object?> { ["NewLine"] = newLine });
}

/// <summary>向脚本暴露的受控工作流 API。</summary>
public sealed class WorkflowScriptGlobals : IWorkflowScriptHostContext
{
    internal WorkflowScriptGlobals(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        Context = context;
        CancellationToken = cancellationToken;
        Console = new WorkflowScriptConsole(context);
    }

    /// <summary>当前节点执行上下文。脚本属于受信任代码，可访问已注册宿主服务。</summary>
    public IWorkflowNodeExecutionContext Context { get; }

    /// <summary>本次节点执行取消令牌。</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>写入运行监视器输出窗口的脚本控制台。</summary>
    public WorkflowScriptConsole Console { get; }

    IWorkflowScriptConsole IWorkflowScriptHostContext.Console => Console;

    /// <summary>从宿主服务容器解析脚本依赖。</summary>
    public T? GetService<T>() where T : class => Context.Services.GetService(typeof(T)) as T;

    /// <summary>读取流程变量；缺失或类型不匹配时返回默认值。</summary>
    public T? GetVariable<T>(string key) => Context.TryGetVariable<T>(key, out var value) ? value : default;

    /// <summary>写入流程变量。</summary>
    public void SetVariable(string key, object value) => Context.SetVariable(key, value);

    /// <summary>删除流程变量。</summary>
    public bool RemoveVariable(string key) => Context.RemoveVariable(key);

    /// <summary>显式暂存公共数据发布。</summary>
    public void PublishData(string key, object value) => Context.PublishData(key, value);

    /// <summary>写入脚本 Trace。</summary>
    public void Trace(string message) => Context.Trace("Script", message);
}

/// <summary>工作流节点到独立脚本服务的适配器。</summary>
public sealed class CSharpScriptNodeHandler : WorkflowNodeHandler<CSharpScriptNodeModel>
{
    private static readonly IReadOnlyList<string> WorkflowImports =
        RoslynScriptEnvironment.StandardImports.Concat(new[] { "DP.WorkFlow", "ScriptEngine" }).ToArray();
    private static readonly IReadOnlyList<System.Reflection.Assembly> WorkflowReferences = new[]
    {
        typeof(WorkflowScriptGlobals).Assembly,
        typeof(IWorkflowNodeExecutionContext).Assembly
    }.Distinct().ToArray();

    private readonly RoslynScriptService _scriptService;
    private readonly ConcurrentDictionary<string, ScriptExecutionPlan> _executionPlans = new(StringComparer.Ordinal);

    /// <summary>使用进程共享脚本服务创建处理器，使稳定脚本能够长期保持热加载。</summary>
    public CSharpScriptNodeHandler() : this(RoslynScriptService.Shared)
    {
    }

    /// <summary>使用指定脚本服务创建处理器，便于宿主控制生命周期和测试隔离。</summary>
    /// <param name="scriptService">脚本编译与运行门面。</param>
    public CSharpScriptNodeHandler(RoslynScriptService scriptService)
    {
        _scriptService = scriptService ?? throw new ArgumentNullException(nameof(scriptService));
    }

    private ScriptExecutionPlan GetExecutionPlan(CSharpScriptNodeModel node)
    {
        var source = node.Script;
        if (_executionPlans.TryGetValue(node.ScriptId, out var current)
            && current.Matches(source, node.ScriptReferencePaths))
            return current;

        var referenceConfiguration = node.ScriptReferencePaths.ToArray();
        var referencePaths = referenceConfiguration
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var created = CreateExecutionPlan(source, referenceConfiguration, referencePaths);
        return _executionPlans.AddOrUpdate(
            node.ScriptId,
            created,
            (_, existing) => existing.Matches(source, node.ScriptReferencePaths) ? existing : created);
    }

    private static ScriptExecutionPlan CreateExecutionPlan(
        string source,
        IReadOnlyList<string> referenceConfiguration,
        IReadOnlyList<string> referencePaths) => new(
        source,
        referenceConfiguration,
        WorkflowCSharpProgramSource.Normalize(source),
        new RoslynScriptEnvironment
        {
            Imports = WorkflowImports,
            References = WorkflowReferences,
            ReferencePaths = referencePaths
        });

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CSharpScriptNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.Script))
            return NodeExecutionResult.Fail("C# 脚本不能为空。");
        if (string.IsNullOrWhiteSpace(node.ScriptId))
            return NodeExecutionResult.Fail("C# 脚本节点缺少稳定 ScriptId。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (node.TimeoutMilliseconds > 0) timeout.CancelAfter(node.TimeoutMilliseconds);
        try
        {
            var plan = GetExecutionPlan(node);
            var result = await _scriptService.ExecuteAsync(
                node.ScriptId,
                plan.NormalizedSource,
                new WorkflowScriptGlobals(context, timeout.Token),
                plan.Environment,
                timeout.Token).ConfigureAwait(false);
            var diagnostics = result.Diagnostics.Select(item => $"{item.Severity} {item.Id}: {item.Message}").ToArray();
            if (result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            {
                var failed = new CSharpScriptNodeResult(false, null, "脚本编译失败。", diagnostics);
                context.Trace("ScriptCompilationFailed", failed.Error, new Dictionary<string, object?> { ["Diagnostics"] = diagnostics });
                return node.ContinueOnError
                    ? NodeExecutionResult.Continue("Error", failed)
                    : NodeExecutionResult.Fail(string.Join(Environment.NewLine, diagnostics));
            }

            var output = new CSharpScriptNodeResult(true, result.ReturnValue, null, diagnostics);
            if (!string.IsNullOrWhiteSpace(node.ResultVarKey) && result.ReturnValue is not null)
                context.SetVariable(node.ResultVarKey.Trim(), result.ReturnValue);
            context.Trace("ScriptCompleted", "C# 脚本执行完成。", new Dictionary<string, object?> { ["ResultType"] = result.ReturnValue?.GetType().FullName });
            return NodeExecutionResult.Continue(output: output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            const string message = "脚本执行超过配置的协作式超时时间。";
            var output = new CSharpScriptNodeResult(false, null, message, Array.Empty<string>());
            context.Trace("ScriptTimeout", message);
            return node.ContinueOnError
                ? NodeExecutionResult.Continue("Error", output)
                : NodeExecutionResult.Fail(message);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            var output = new CSharpScriptNodeResult(false, null, exception.Message, Array.Empty<string>());
            context.Trace("ScriptFailed", exception.Message, new Dictionary<string, object?> { ["ExceptionType"] = exception.GetType().FullName });
            return node.ContinueOnError
                ? NodeExecutionResult.Continue("Error", output)
                : NodeExecutionResult.Fail("脚本执行失败: " + exception.Message);
        }
    }

    /// <summary>缓存一个节点当前源码和引用对应的规范化源码及不可变编译环境。</summary>
    private sealed class ScriptExecutionPlan
    {
        public ScriptExecutionPlan(
            string source,
            IReadOnlyList<string> referenceConfiguration,
            string normalizedSource,
            RoslynScriptEnvironment environment)
        {
            Source = source;
            ReferenceConfiguration = referenceConfiguration;
            NormalizedSource = normalizedSource;
            Environment = environment;
        }

        public string Source { get; }
        public IReadOnlyList<string> ReferenceConfiguration { get; }
        public string NormalizedSource { get; }
        public RoslynScriptEnvironment Environment { get; }

        public bool Matches(string source, IList<string> referenceConfiguration)
        {
            if (!string.Equals(Source, source, StringComparison.Ordinal)
                || ReferenceConfiguration.Count != referenceConfiguration.Count)
                return false;
            for (var index = 0; index < ReferenceConfiguration.Count; index++)
                if (!string.Equals(ReferenceConfiguration[index], referenceConfiguration[index], StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
    }
}
