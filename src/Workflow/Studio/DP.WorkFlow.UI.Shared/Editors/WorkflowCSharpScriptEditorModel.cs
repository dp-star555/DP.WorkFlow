using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptEngine;

namespace DP.WorkFlow.UI;

/// <summary>将独立 Roslyn 脚本服务配置为工作流设计期环境。</summary>
public static class WorkflowCSharpScriptEditorModel
{
    private static readonly RoslynScriptService Service = new();

    /// <summary>获取脚本编辑器向用户展示的工作流上下文 API 说明。</summary>
    public static readonly IReadOnlyList<string> ContextApi = new[]
    {
        "context — 通用 CSharpProgramContext",
        "cancellationToken — 本次程序执行取消令牌",
        "workflow — 当前 WorkflowScriptGlobals",
        "workflow.GetVariable<T>(key) — 读取流程变量",
        "workflow.GetService<T>() — 解析宿主注册服务",
        "workflow.SetVariable(key, value) — 写入流程变量",
        "workflow.Trace(message) — 写入运行 Trace",
        "workflow.Console.WriteLine(value) — 写入运行监视器输出"
    };

    /// <summary>创建脚本编辑、编译和智能提示共用的 Roslyn 环境。</summary>
    /// <param name="referencePaths">脚本显式引用的 DLL 路径。</param>
    /// <param name="scriptNodeAssembly">可选具体脚本节点程序集，用于兼容引用其旧宿主类型的已有脚本。</param>
    /// <returns>编辑器、编译器和运行时可以共同复现的显式环境。</returns>
    public static RoslynScriptEnvironment CreateEnvironment(
        IEnumerable<string>? referencePaths = null,
        System.Reflection.Assembly? scriptNodeAssembly = null) => new()
    {
        Imports = RoslynScriptEnvironment.StandardImports.Concat(new[] { "DP.WorkFlow", "ScriptEngine" }).ToArray(),
        References = new[]
        {
            typeof(IWorkflowScriptHostContext).Assembly,
            scriptNodeAssembly
        }.Where(assembly => assembly is not null).Cast<System.Reflection.Assembly>().Distinct().ToArray(),
        ReferencePaths = (referencePaths ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        ContextItems = new[]
        {
            new RoslynScriptContextItem("context", ContextApi[0]),
            new RoslynScriptContextItem("cancellationToken", ContextApi[1]),
            new RoslynScriptContextItem("workflow", ContextApi[2]),
            new RoslynScriptContextItem("workflow.GetVariable<T>(\"key\")", ContextApi[3]),
            new RoslynScriptContextItem("workflow.GetService<T>()", ContextApi[4]),
            new RoslynScriptContextItem("workflow.SetVariable(\"key\", value)", ContextApi[5]),
            new RoslynScriptContextItem("workflow.Trace(\"message\")", ContextApi[6]),
            new RoslynScriptContextItem("workflow.Console.WriteLine(\"message\")", ContextApi[7])
        }
    };

    /// <summary>将脚本文本规范化为完整的程序源代码。</summary>
    /// <param name="source">源数据或路径点集合。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static string EnsureProgramSource(string? source) => WorkflowCSharpEditorSource.Normalize(source);

    /// <summary>编译脚本并返回结构化诊断项。</summary>
    /// <param name="source">源数据或路径点集合。</param>
    /// <param name="referencePaths">脚本显式引用的 DLL 路径。</param>
    /// <param name="scriptNodeAssembly">可选具体脚本节点程序集。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static IReadOnlyList<RoslynScriptDiagnostic> GetDiagnosticItems(
        string? source,
        IEnumerable<string>? referencePaths = null,
        System.Reflection.Assembly? scriptNodeAssembly = null) =>
        Service.GetDiagnostics(source, CreateEnvironment(referencePaths, scriptNodeAssembly));

    /// <summary>编译脚本并返回适合界面显示的诊断文本。</summary>
    /// <param name="script">脚本文本。</param>
    /// <param name="referencePaths">脚本显式引用的 DLL 路径。</param>
    /// <param name="scriptNodeAssembly">可选具体脚本节点程序集。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static IReadOnlyList<string> GetDiagnostics(
        string? script,
        IEnumerable<string>? referencePaths = null,
        System.Reflection.Assembly? scriptNodeAssembly = null) =>
        GetDiagnosticItems(script, referencePaths, scriptNodeAssembly)
            .Select(FormatDiagnostic).ToArray();

    /// <summary>将脚本诊断格式化为包含位置的文本。</summary>
    /// <param name="item">目标数据项。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static string FormatDiagnostic(RoslynScriptDiagnostic item)
    {
        var location = item.Line > 0 ? $" ({item.Line},{item.Column})" : string.Empty;
        var severity = item.Severity switch
        {
            DiagnosticSeverity.Error => "错误",
            DiagnosticSeverity.Warning => "警告",
            DiagnosticSeverity.Info => "信息",
            _ => "提示"
        };
        return $"{severity} {item.Id}{location}: {item.Message}";
    }

    /// <summary>异步获取指定光标位置的语义补全项。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="caretIndex">光标索引。</param>
    /// <param name="cancellationToken">用于取消异步操作的令牌。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static async Task<IReadOnlyList<string>> GetSemanticCompletionsAsync(string text, int caretIndex, CancellationToken cancellationToken = default) =>
        (await Service.GetCompletionsAsync(text, caretIndex, CreateEnvironment(), cancellationToken).ConfigureAwait(false))
        .Select(item => item.InsertionText).ToArray();

    /// <summary>同步获取指定光标位置的补全项。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="caretIndex">光标索引。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static IReadOnlyList<string> GetCompletions(string text, int caretIndex) =>
        GetSemanticCompletionsAsync(text, caretIndex).GetAwaiter().GetResult();

    /// <summary>异步计算脚本的语法高亮区间。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="cancellationToken">用于取消异步操作的令牌。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static Task<IReadOnlyList<RoslynScriptHighlightSpan>> GetHighlightSpansAsync(string text, CancellationToken cancellationToken = default) =>
        Service.GetHighlightSpansAsync(text, CreateEnvironment(), cancellationToken);

    /// <summary>读取脚本中的 using 命名空间。</summary>
    /// <param name="script">脚本文本。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static IReadOnlyList<string> GetUsings(string? script) => RoslynScriptService.GetUsings(script);
    /// <summary>替换脚本中的 using 命名空间。</summary>
    /// <param name="script">脚本文本。</param>
    /// <param name="namespaces">using 命名空间集合。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static string SetUsings(string? script, IEnumerable<string> namespaces) => RoslynScriptService.SetUsings(script, namespaces);
    /// <summary>计算当前补全单词的起始位置。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="caretIndex">光标索引。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static int GetCompletionStart(string text, int caretIndex) => RoslynScriptService.GetCompletionStart(text, caretIndex);
}

/// <summary>Studio-owned C# editor template; it depends only on the stable script host capability.</summary>
internal static class WorkflowCSharpEditorSource
{
    private const string DefaultSource = """
using ScriptEngine;
using DP.WorkFlow;

public sealed class WorkflowProgram : ICSharpProgram
{
    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        var workflow = context.GetHostContext<IWorkflowScriptHostContext>();

        workflow.Console.WriteLine("运行成功");
        return null;
    }
}
""";

    internal static string Normalize(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return DefaultSource;
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var hasProgramContract = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .SelectMany(type => type.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
            .Any(type => type.Type.ToString().EndsWith(nameof(ICSharpProgram), StringComparison.Ordinal));
        if (hasProgramContract) return source;
        var existingUsings = RoslynScriptService.GetUsings(source);
        var body = RoslynScriptService.SetUsings(source, Array.Empty<string>());
        var usings = existingUsings.Concat(new[] { "ScriptEngine", "DP.WorkFlow" })
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal)
            .Select(item => $"using {item};");
        var indentedBody = string.Join(Environment.NewLine,
            body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "        " + line));
        return $$"""
{{string.Join(Environment.NewLine, usings)}}

public sealed class WorkflowProgram : ICSharpProgram
{
    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        var workflow = context.GetHostContext<IWorkflowScriptHostContext>();
        var Context = workflow.Context;
        var Console = workflow.Console;
        var CancellationToken = cancellationToken;
#pragma warning disable CS8321
        T? GetVariable<T>(string key) => workflow.GetVariable<T>(key);
        T? GetService<T>() where T : class => workflow.GetService<T>();
        void SetVariable(string key, object value) => workflow.SetVariable(key, value);
        bool RemoveVariable(string key) => workflow.RemoveVariable(key);
        void PublishData(string key, object value) => workflow.PublishData(key, value);
        void Trace(string message) => workflow.Trace(message);
#pragma warning restore CS8321

{{indentedBody}}
    }
}
""";
    }
}
