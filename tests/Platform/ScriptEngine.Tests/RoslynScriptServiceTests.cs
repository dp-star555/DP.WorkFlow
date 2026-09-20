using ScriptEngine;

namespace ScriptEngine.Tests;

public sealed class RoslynScriptServiceTests
{
    [Fact]
    public async Task Service_ExecutesWithoutWorkflowDependency()
    {
        var service = new RoslynScriptService();
        var environment = new RoslynScriptEnvironment
        {
            References = new[] { typeof(TestGlobals).Assembly },
            ContextItems = new[] { new RoslynScriptContextItem("context", "程序上下文") }
        };
        const string source = """
using ScriptEngine;

public sealed class TestProgram : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        var globals = context.GetHostContext<ScriptEngine.Tests.RoslynScriptServiceTests.TestGlobals>();
        return new ValueTask<object?>(globals.Value * 2);
    }
}
""";

        var result = await service.ExecuteAsync(source, new TestGlobals { Value = 21 }, environment);

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Equal(42, result.ReturnValue);
    }

    [Fact]
    public async Task Completion_IsInferredFromLocalVariableType()
    {
        var source = "class Demo { void Run() { var text = \"abc\"; text. } }";
        var caret = source.IndexOf("text. }", StringComparison.Ordinal) + "text.".Length;
        var items = await new RoslynScriptService().GetCompletionsAsync(source, caret);

        Assert.Contains(items, item => item.InsertionText.StartsWith("Substring", StringComparison.Ordinal));
        Assert.Contains(items, item => item.InsertionText == "Length");
    }

    [Fact]
    public async Task Completion_PartialIdentifierIncludesContextAndKeywords()
    {
        const string source = "class Demo { void Run() { con } }";
        var caret = source.IndexOf("con", StringComparison.Ordinal) + "con".Length;
        var environment = new RoslynScriptEnvironment
        {
            ContextItems = new[] { new RoslynScriptContextItem("context", "脚本上下文") }
        };
        using var service = new RoslynScriptService();

        var items = await service.GetCompletionsAsync(source, caret, environment);

        Assert.Contains(items, item => item.InsertionText == "context");
        Assert.Contains(items, item => item.InsertionText == "Console");
        Assert.Contains(items, item => item.InsertionText == "const");
        Assert.Contains(items, item => item.InsertionText == "continue");
    }

    [Fact]
    public async Task Completion_SubsequencePatternFindsSemanticMember()
    {
        const string source = "class Demo { void Run() { Console.WLi } }";
        var caret = source.IndexOf("WLi", StringComparison.Ordinal) + 3;
        using var service = new RoslynScriptService();

        var items = await service.GetCompletionsAsync(source, caret);

        Assert.Contains(items, item => item.InsertionText == "WriteLine()");
    }

    [Fact]
    public async Task Completion_ContainsSemanticKindDescriptionAndCaretPlacement()
    {
        var source = "class Demo { void Run() { Console.Wri } }";
        var caret = source.IndexOf("Wri", StringComparison.Ordinal) + 3;
        using var service = new RoslynScriptService();

        var items = await service.GetCompletionsAsync(source, caret);
        var writeLine = Assert.Single(items, item => item.InsertionText == "WriteLine()");

        Assert.Equal(RoslynScriptCompletionKind.Method, writeLine.Kind);
        Assert.Equal(-1, writeLine.CaretOffset);
        Assert.False(string.IsNullOrWhiteSpace(writeLine.Description));
    }

    [Fact]
    public async Task Completion_DistinguishesMethodPropertyConstantAndTypeKinds()
    {
        const string memberSource = "class Demo { const int Limit = 1; string Name { get; } = \"x\"; void Run() { this. } }";
        var memberCaret = memberSource.IndexOf("this.", StringComparison.Ordinal) + "this.".Length;
        const string typeSource = "interface IThing { } class Demo { void Run() { ITh } }";
        var typeCaret = typeSource.IndexOf("ITh", StringComparison.Ordinal) + 3;
        using var service = new RoslynScriptService();

        var members = await service.GetCompletionsAsync(memberSource, memberCaret);
        var types = await service.GetCompletionsAsync(typeSource, typeCaret);

        Assert.Contains(members, item => item.InsertionText == "Run()" && item.Kind == RoslynScriptCompletionKind.Method);
        Assert.Contains(members, item => item.InsertionText == "Name" && item.Kind == RoslynScriptCompletionKind.Property);
        Assert.Contains(members, item => item.InsertionText == "Limit" && item.Kind == RoslynScriptCompletionKind.Constant);
        Assert.Contains(types, item => item.InsertionText == "IThing" && item.Kind == RoslynScriptCompletionKind.Interface);
    }

    [Fact]
    public async Task SignatureHelp_IdentifiesActiveParameterAndOverloads()
    {
        const string source = "class Demo { void Run() { var value = string.Concat(\"a\", ); } }";
        var caret = source.IndexOf(", ", StringComparison.Ordinal) + 2;
        using var service = new RoslynScriptService();

        var help = await service.GetSignatureHelpAsync(source, caret);

        Assert.NotNull(help);
        Assert.Equal(1, help!.ActiveParameter);
        Assert.NotEmpty(help.Parameters);
        Assert.NotEmpty(help.Overloads);
    }

    [Fact]
    public async Task QuickInfo_ReturnsSymbolSignature()
    {
        const string source = "class Demo { DateTime Value { get; } }";
        var position = source.IndexOf("DateTime", StringComparison.Ordinal) + 2;
        using var service = new RoslynScriptService();

        var info = await service.GetQuickInfoAsync(source, position);

        Assert.NotNull(info);
        Assert.Contains("DateTime", info!.DisplayText, StringComparison.Ordinal);
        Assert.True(info.Length > 0);
    }

    [Fact]
    public async Task AnalysisOperations_ReuseBoundedSnapshot()
    {
        const string source = "class Demo { string Value => string.Empty; }";
        using var service = new RoslynScriptService(new RoslynScriptServiceOptions { AnalysisCacheCapacity = 2 });

        _ = await service.GetDiagnosticsAsync(source);
        _ = await service.GetHighlightSpansAsync(source);
        var reused = service.GetCacheStatistics();
        _ = await service.GetDiagnosticsAsync(source + " // revision 2");
        _ = await service.GetDiagnosticsAsync(source + " // revision 3");
        var bounded = service.GetCacheStatistics();

        Assert.True(reused.Hits >= 1);
        Assert.Equal(1, reused.EntryCount);
        Assert.InRange(bounded.EntryCount, 1, 2);
        Assert.True(bounded.IncrementalParses >= 2);
    }

    [Fact]
    public void FormatSource_FormatsValidSourceAndPreservesInvalidSource()
    {
        const string valid = "class Demo{void Run(){var value=1;}}";
        const string invalid = "class Demo {";

        var formatted = RoslynScriptService.FormatSource(valid, 4, "\n");

        Assert.Contains("\n    void Run()", formatted, StringComparison.Ordinal);
        Assert.Equal(invalid, RoslynScriptService.FormatSource(invalid));
    }

    [Fact]
    public async Task MultiFileProject_CompilesExecutesAndPreservesDiagnosticFileName()
    {
        var project = new RoslynScriptProject
        {
            SourceFiles = new[]
            {
                new RoslynScriptSourceFile("Main.cs", """
using ScriptEngine;
public sealed class MultiFileProgram : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
        => new ValueTask<object?>(Helper.Double(21));
}
"""),
                new RoslynScriptSourceFile("Features/Helper.cs", """
internal static class Helper
{
    public static int Double(int value) => value * 2;
}
""")
            }
        };
        using var service = new RoslynScriptService();

        var execution = await service.ExecuteProjectAsync("multi-file", project);

        Assert.DoesNotContain(execution.Diagnostics, item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Equal(42, execution.ReturnValue);

        var mainSource = project.SourceFiles[0].Source;
        var definitionPosition = mainSource.IndexOf("Double", StringComparison.Ordinal) + 2;
        var definition = await service.GetProjectDefinitionAsync(project, "Main.cs", definitionPosition);
        Assert.NotNull(definition);
        Assert.True(definition!.IsSource);
        Assert.Equal("Features/Helper.cs", definition.SourceName);

        var completionSource = mainSource.Replace("Helper.Double(21)", "Helper.");
        var completionProject = new RoslynScriptProject
        {
            SourceFiles = new[]
            {
                new RoslynScriptSourceFile("Main.cs", completionSource),
                project.SourceFiles[1]
            }
        };
        var completions = await service.GetProjectCompletionsAsync(
            completionProject,
            "Main.cs",
            completionSource.IndexOf("Helper.", StringComparison.Ordinal) + "Helper.".Length);
        Assert.Contains(completions, item => item.InsertionText == "Double()");

        var broken = new RoslynScriptProject
        {
            SourceFiles = new[]
            {
                project.SourceFiles[0],
                new RoslynScriptSourceFile("Features/Helper.cs", "internal static class Helper { static int Value => missing; }")
            }
        };
        var diagnostics = await service.GetProjectDiagnosticsAsync(broken);
        Assert.Contains(diagnostics, item => item.Id == "CS0103"
                                             && item.SourceName == "Features/Helper.cs"
                                             && item.Origin == RoslynScriptDiagnosticOrigin.UserSource);
    }

    [Fact]
    public async Task Definition_ReturnsSourceAndMetadataLocations()
    {
        const string source = "class Demo { void Target() { } void Run() { Target(); } DateTime Value { get; } }";
        var call = source.LastIndexOf("Target", StringComparison.Ordinal) + 2;
        var dateTime = source.IndexOf("DateTime", StringComparison.Ordinal) + 2;
        using var service = new RoslynScriptService();

        var sourceDefinition = await service.GetDefinitionAsync(source, call);
        var metadataDefinition = await service.GetDefinitionAsync(source, dateTime);

        Assert.NotNull(sourceDefinition);
        Assert.True(sourceDefinition!.IsSource);
        Assert.Equal("Program.cs", sourceDefinition.SourceName);
        Assert.Equal(source.IndexOf("Target", StringComparison.Ordinal), sourceDefinition.Start);
        Assert.NotNull(metadataDefinition);
        Assert.False(metadataDefinition!.IsSource);
        Assert.Contains("DateTime", metadataDefinition.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeActions_InsertMissingSemicolonAndUsing()
    {
        using var service = new RoslynScriptService();
        const string missingSemicolon = "class Demo { void Run() { var value = 1 } }";
        var semicolonDiagnostic = Assert.Single(service.GetDiagnostics(missingSemicolon), item => item.Id == "CS1002");
        var semicolonAction = Assert.Single(service.GetCodeActions(missingSemicolon, semicolonDiagnostic));
        var fixedSemicolon = RoslynScriptService.ApplyCodeAction(missingSemicolon, semicolonAction);
        Assert.DoesNotContain(service.GetDiagnostics(fixedSemicolon), item => item.Id == "CS1002");

        const string missingUsing = "class Demo { StringBuilder Build() => new StringBuilder(); }";
        var usingDiagnostic = service.GetDiagnostics(missingUsing).First(item => item.Id == "CS0246");
        var usingAction = Assert.Single(service.GetCodeActions(missingUsing, usingDiagnostic));
        var fixedUsing = RoslynScriptService.ApplyCodeAction(missingUsing, usingAction);
        Assert.StartsWith("using System.Text;", fixedUsing, StringComparison.Ordinal);
        Assert.DoesNotContain(service.GetDiagnostics(fixedUsing), item => item.Id == "CS0246");
    }

    [Fact]
    public void SyntacticHighlighting_DistinguishesXmlDocumentationTags()
    {
        const string source = "/// <summary>处理批次</summary>\npublic sealed class Demo { }";
        using var service = new RoslynScriptService();

        var spans = service.GetSyntacticHighlightSpans(source);
        var xmlText = spans
            .Where(span => span.Kind == RoslynScriptHighlightKind.XmlDocTag)
            .Select(span => source.Substring(span.Start, span.Length))
            .ToArray();

        Assert.Contains("<summary>", xmlText);
        Assert.Contains("</summary>", xmlText);
        Assert.Contains(spans, span => span.Kind == RoslynScriptHighlightKind.Comment
                                      && span.Start <= source.IndexOf("处理批次", StringComparison.Ordinal)
                                      && span.Start + span.Length >= source.IndexOf("处理批次", StringComparison.Ordinal) + "处理批次".Length);
    }

    [Fact]
    public void FoldingSpans_ContainTypeMemberIfAndSwitchBlocks()
    {
        const string source = """
class Demo
{
    void Run()
    {
        if (true)
        {
            switch (DateTime.Now.Day)
            {
                case 1:
                    break;
            }
        }
    }
}
""";
        using var service = new RoslynScriptService();

        var spans = service.GetFoldingSpans(source);

        Assert.Contains(spans, span => span is { StartLine: 0, EndLine: 13, Kind: RoslynScriptFoldingKind.Type });
        Assert.Contains(spans, span => span is { StartLine: 2, EndLine: 12, Kind: RoslynScriptFoldingKind.Member });
        Assert.Contains(spans, span => span is { StartLine: 4, EndLine: 11, Kind: RoslynScriptFoldingKind.Statement });
        Assert.Contains(spans, span => span is { StartLine: 6, EndLine: 10, Kind: RoslynScriptFoldingKind.Statement });
    }

    [Fact]
    public void FoldingSpans_StandaloneScopeStartsAtItsOwnOpeningBrace()
    {
        const string source = """
class Demo
{
    void Run()
    {
        {
            var value = 1;
        }
    }
}
""";
        using var service = new RoslynScriptService();

        var spans = service.GetFoldingSpans(source);

        Assert.Contains(spans, span => span is { StartLine: 4, EndLine: 6, Kind: RoslynScriptFoldingKind.Statement });
    }

    [Fact]
    public void FoldingSpans_ContainRegion()
    {
        const string source = "#region Demo\nclass Demo\n{\n}\n#endregion";
        using var service = new RoslynScriptService();

        var spans = service.GetFoldingSpans(source);

        Assert.Contains(spans, span => span is { StartLine: 0, EndLine: 4, Kind: RoslynScriptFoldingKind.Region });
    }

    [Theory]
    [InlineData("class Demo\n{\n", 4)]
    [InlineData("    class Demo\n    {\n", 8)]
    [InlineData("class Demo\n{\n    void Run(\n", 8)]
    [InlineData("class Demo\n{\n    void Run()\n    {\n        if (true)\n", 12)]
    [InlineData("class Demo\n{\n    void Run()\n    {\n        if (true)\n            Go();\n", 8)]
    public void GetIndentation_NewLine_AlignsWithCSharpStructure(string source, int expectedIndentation)
    {
        using var service = new RoslynScriptService();

        var indentation = service.GetIndentation(source, source.Length);

        Assert.Equal(expectedIndentation, indentation);
    }

    [Fact]
    public void GetIndentation_BlankLineInsidePairedBraces_UsesInnerScope()
    {
        const string source = "class Demo\n{\n    void Run()\n    {\n        if (true)\n        {\n\n        }\n    }\n}";
        using var service = new RoslynScriptService();
        var caret = source.IndexOf("\n\n", StringComparison.Ordinal) + 1;

        var indentation = service.GetIndentation(source, caret);

        Assert.Equal(12, indentation);
    }

    [Fact]
    public void GetIndentation_ClosingBrace_AlignsWithOpeningBrace()
    {
        const string source = "    class Demo\n    {\n        }";
        using var service = new RoslynScriptService();
        var caret = source.IndexOf('}') + 1;

        var indentation = service.GetIndentation(source, caret);

        Assert.Equal(4, indentation);
    }

    [Fact]
    public void Diagnostics_ContainOneBasedLineAndColumn()
    {
        const string source = """
using ScriptEngine;

public sealed class BrokenProgram : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        return missingValue;
    }
}
""";

        var diagnostic = Assert.Single(new RoslynScriptService().GetDiagnostics(source), item => item.Id == "CS0103");

        Assert.Equal(7, diagnostic.Line);
        Assert.True(diagnostic.Column > 0);
        Assert.True(diagnostic.Length > 0);
    }

    [Fact]
    public void UsingManager_ReplacesOnlyUsingRegion()
    {
        var source = "using System;\n\nreturn 1;";
        var changed = RoslynScriptService.SetUsings(source, new[] { "System.Linq", "System" });

        Assert.Equal(new[] { "System", "System.Linq" }, RoslynScriptService.GetUsings(changed));
        Assert.Contains("return 1;", changed, StringComparison.Ordinal);
    }

    [Fact]
    public void UsingManager_PreservesAliasStaticAndGlobalSemantics()
    {
        const string source = "global using System.Text;\nusing static System.Math;\nusing Alias = System.String;\nclass Demo { }";

        var entries = RoslynScriptService.GetUsings(source);
        var changed = RoslynScriptService.SetUsings("class Demo { }", entries);

        Assert.Contains("global System.Text", entries);
        Assert.Contains("static System.Math", entries);
        Assert.Contains("Alias = System.String", entries);
        Assert.DoesNotContain(new RoslynScriptService().GetDiagnostics(changed), item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task CompileProgram_MutatedImportList_InvalidatesStableEnvironmentSnapshot()
    {
        var imports = new List<string> { "System", "System.Threading", "System.Threading.Tasks" };
        var environment = new RoslynScriptEnvironment { Imports = imports };
        using var service = new RoslynScriptService();
        var source = CounterProgramSource("return new StringBuilder().Append(42).ToString();");

        var before = await service.CompileProgramAsync(source, environment);
        imports.Add("System.Text");
        var after = await service.CompileProgramAsync(source, environment);

        Assert.False(before.Success);
        Assert.True(after.Success);
        Assert.NotEqual(before.Revision, after.Revision);
    }

    [Fact]
    public async Task CompileProgram_ModernRecordAndRequiredMembers_AreSupportedByTargetCompatibilityLayer()
    {
        const string source = """
using ScriptEngine;

public sealed record Result(int Value);
public sealed class Options
{
    public required string Name { get; init; }
}
public sealed class ModernProgram : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        var options = new Options { Name = "ready" };
        return new ValueTask<object?>(new Result(options.Name.Length).Value);
    }
}
""";
        using var service = new RoslynScriptService();

        var result = await service.ExecuteAsync("modern-language", source);

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Equal(5, result.ReturnValue);
    }

    [Fact]
    public async Task CompileProgram_DoesNotLoadDynamicAssembly()
    {
        using var service = new RoslynScriptService();
        var source = CounterProgramSource("return ++_counter;") + Environment.NewLine + "// " + Guid.NewGuid().ToString("N");

        var result = await service.CompileProgramAsync(source);

        Assert.True(result.Success);
        var artifact = Assert.IsType<RoslynScriptCompilationArtifact>(result.Artifact);
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => string.Equals(assembly.GetName().Name, artifact.AssemblyName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UnchangedRevisionStaysHotAndChangedRevisionReplacesIt()
    {
        using var service = new RoslynScriptService();
        const string scriptId = "stable-script";

        var first = await service.ExecuteAsync(scriptId, CounterProgramSource("return ++_counter;"));
        var second = await service.ExecuteAsync(scriptId, CounterProgramSource("return ++_counter;"));
        var changed = await service.ExecuteAsync(scriptId, CounterProgramSource("_counter += 10; return _counter;"));
        var changedAgain = await service.ExecuteAsync(scriptId, CounterProgramSource("_counter += 10; return _counter;"));

        Assert.Equal(1, first.ReturnValue);
        Assert.Equal(2, second.ReturnValue);
        Assert.Equal(10, changed.ReturnValue);
        Assert.Equal(20, changedAgain.ReturnValue);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task ExecuteAsync_UnchangedHotScript_DoesNotRebuildPlatformMetadataOnEveryRun()
    {
        using var service = new RoslynScriptService();
        var source = CounterProgramSource("return 42;");
        const string scriptId = "hot-allocation-script";

        // 先完成 Roslyn、JIT 和程序集加载预热，只测稳定热路径。
        for (var index = 0; index < 10; index++)
            _ = await service.ExecuteAsync(scriptId, source);

        var before = GC.GetAllocatedBytesForCurrentThread();
        const int runCount = 100;
        for (var index = 0; index < runCount; index++)
            _ = await service.ExecuteAsync(scriptId, source);
        var allocatedPerRun = (GC.GetAllocatedBytesForCurrentThread() - before) / runCount;

        Assert.True(
            allocatedPerRun < 32 * 1024,
            $"未修改脚本热执行每次分配 {allocatedPerRun:N0} 字节，疑似重复构建编译元数据。");
    }
#endif

    [Fact]
    public async Task RuntimeSlots_RespectConfiguredCapacity()
    {
        using var service = new RoslynScriptService(new RoslynScriptServiceOptions { RuntimeSlotCapacity = 1 });
        var source = CounterProgramSource("return ++_counter;");

        Assert.Equal(1, (await service.ExecuteAsync("slot-a", source)).ReturnValue);
        Assert.Equal(2, (await service.ExecuteAsync("slot-a", source)).ReturnValue);
        Assert.Equal(1, (await service.ExecuteAsync("slot-b", source)).ReturnValue);
        Assert.Equal(1, (await service.ExecuteAsync("slot-a", source)).ReturnValue);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesNewProgramInstanceForEveryRun()
    {
        using var service = new RoslynScriptService();
        const string source = """
using ScriptEngine;

public sealed class InstanceProgram : ICSharpProgram
{
    private int _counter;

    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        return new ValueTask<object?>(++_counter);
    }
}
""";

        var first = await service.ExecuteAsync("instance-script", source);
        var second = await service.ExecuteAsync("instance-script", source);

        Assert.Equal(1, first.ReturnValue);
        Assert.Equal(1, second.ReturnValue);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidReplacementDoesNotSilentlyRunPreviousRevision()
    {
        using var service = new RoslynScriptService();
        const string scriptId = "replace-failure";
        var validSource = CounterProgramSource("return 42;");

        var valid = await service.ExecuteAsync(scriptId, validSource);
        var invalid = await service.ExecuteAsync(scriptId, "not valid C#");
        var validAgain = await service.ExecuteAsync(scriptId, validSource);

        Assert.Equal(42, valid.ReturnValue);
        Assert.Null(invalid.ReturnValue);
        Assert.Contains(invalid.Diagnostics, item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Equal(42, validAgain.ReturnValue);
    }

    [Fact]
    public async Task RemoveProgram_ClearsLoadedStaticState()
    {
        using var service = new RoslynScriptService();
        const string scriptId = "remove-script";
        var source = CounterProgramSource("return ++_counter;");

        Assert.Equal(1, (await service.ExecuteAsync(scriptId, source)).ReturnValue);
        Assert.Equal(2, (await service.ExecuteAsync(scriptId, source)).ReturnValue);
        service.RemoveProgram(scriptId);
        Assert.Equal(1, (await service.ExecuteAsync(scriptId, source)).ReturnValue);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveOldRevisionCanFinishAfterNewRevisionBecomesCurrent()
    {
        using var service = new RoslynScriptService();
        var environment = new RoslynScriptEnvironment { References = new[] { typeof(TestGlobals).Assembly } };
        var globals = new TestGlobals();
        const string oldSource = """
using ScriptEngine;

public sealed class WaitingProgram : ICSharpProgram
{
    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        var globals = context.GetHostContext<ScriptEngine.Tests.RoslynScriptServiceTests.TestGlobals>();
        globals.Started.TrySetResult(true);
        await globals.Release.Task;
        return 1;
    }
}
""";
        var newSource = CounterProgramSource("return 2;");

        var oldRun = service.ExecuteAsync("concurrent-replace", oldSource, globals, environment);
        var started = await Task.WhenAny(globals.Started.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(globals.Started.Task, started);
        var newRun = await service.ExecuteAsync("concurrent-replace", newSource, globals, environment);
        globals.Release.TrySetResult(true);
        var oldResult = await oldRun;

        Assert.Equal(2, newRun.ReturnValue);
        Assert.Equal(1, oldResult.ReturnValue);
        Assert.Equal(2, (await service.ExecuteAsync("concurrent-replace", newSource, globals, environment)).ReturnValue);
    }

    [Fact]
    public void GeneratedImportDiagnostic_IsNotReportedAsUserSource()
    {
        var environment = new RoslynScriptEnvironment { Imports = new[] { "Namespace.That.Does.Not.Exist" } };

        using var service = new RoslynScriptService();
        var generatedDiagnostic = Assert.Single(
            service.GetDiagnostics("public sealed class Demo { }", environment),
            item => item.SourceName == "ScriptEngine.Imports.g.cs");

        Assert.Equal(RoslynScriptDiagnosticOrigin.GeneratedSource, generatedDiagnostic.Origin);
    }

    private static string CounterProgramSource(string body) => $$"""
using ScriptEngine;

public sealed class CounterProgram : ICSharpProgram
{
    private static int _counter;

    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        {{body}}
    }
}
""";

    [Fact]
    public void ReferenceInspector_ReadsManagedMetadataWithoutLoadingDll()
    {
        var path = typeof(TestGlobals).Assembly.Location;
        var before = AppDomain.CurrentDomain.GetAssemblies().Length;

        var info = RoslynScriptReferenceInspector.Inspect(path);

        Assert.Equal(typeof(TestGlobals).Assembly.GetName().Name, info.AssemblyName);
        Assert.Equal(path, info.Path);
        Assert.Equal(64, info.Sha256.Length);
        Assert.NotEmpty(info.Dependencies);
        Assert.Equal(before, AppDomain.CurrentDomain.GetAssemblies().Length);
    }

    [Fact]
    public void ReferenceLock_RoundTripsAndDetectsChangedDll()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScriptEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var referencePath = Path.Combine(directory, "Locked.dll");
        var lockPath = Path.Combine(directory, "script.references.lock.xml");
        try
        {
            File.Copy(typeof(TestGlobals).Assembly.Location, referencePath);
            var created = RoslynScriptReferenceLock.Create(new[] { referencePath }, directory);
            created.Save(lockPath);
            var loaded = RoslynScriptReferenceLock.Load(lockPath);

            var valid = loaded.Validate(directory);
            Assert.True(valid.IsValid);
            Assert.Equal(referencePath, Assert.Single(valid.ResolvedPaths));

            File.Copy(typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.Location, referencePath, overwrite: true);
            File.SetLastWriteTimeUtc(referencePath, DateTime.UtcNow.AddSeconds(2));
            var changed = loaded.Validate(directory);
            Assert.False(changed.IsValid);
            Assert.Contains(changed.Issues, item => item.Code is "SRL0002" or "SRL0003");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitDllReference_IsResolvedAtRuntime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScriptEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var referencePath = Path.Combine(directory, "ExternalMath.dll");
        try
        {
            CreateManagedReference(referencePath);
            var environment = new RoslynScriptEnvironment { ReferencePaths = new[] { referencePath } };
            using var service = new RoslynScriptService();
            var source = CounterProgramSource("return ExternalMath.Double(21);");

            var result = await service.ExecuteAsync("external-reference", source, environment: environment);

            Assert.DoesNotContain(result.Diagnostics, item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            Assert.Equal(42, result.ReturnValue);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task SecurityPolicy_RestrictedNamespaceProducesEngineDiagnostic()
    {
        var environment = new RoslynScriptEnvironment { SecurityPolicy = RoslynScriptSecurityPolicy.Restricted };
        using var service = new RoslynScriptService();
        var source = CounterProgramSource("return System.IO.File.Exists(\"missing.txt\");");

        var result = await service.CompileProgramAsync(source, environment);

        Assert.Contains(result.Diagnostics, item => item.Id == "SE2001"
                                                    && item.Origin == RoslynScriptDiagnosticOrigin.UserSource);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitReferenceDiscoversPrivateSameDirectoryDependency()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScriptEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var dependencyPath = Path.Combine(directory, "PrivateDependency.dll");
        var facadePath = Path.Combine(directory, "ExternalFacade.dll");
        try
        {
            CreateManagedAssembly(dependencyPath, "PrivateDependency", "public static class PrivateDependency { public static int Value => 42; }");
            CreateManagedAssembly(
                facadePath,
                "ExternalFacade",
                "public static class ExternalFacade { public static int Read() => PrivateDependency.Value; }",
                dependencyPath);
            var environment = new RoslynScriptEnvironment { ReferencePaths = new[] { facadePath } };
            using var service = new RoslynScriptService();

            var result = await service.ExecuteAsync(
                "transitive-reference",
                CounterProgramSource("return ExternalFacade.Read();"),
                environment: environment);

            Assert.DoesNotContain(result.Diagnostics, item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            Assert.Equal(42, result.ReturnValue);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task CompileProgram_ReferencedDllContentChanges_ProducesNewRevision()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScriptEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var referencePath = Path.Combine(directory, "ChangingReference.dll");
        try
        {
            File.Copy(typeof(TestGlobals).Assembly.Location, referencePath, overwrite: true);
            using var service = new RoslynScriptService();
            var environment = new RoslynScriptEnvironment { ReferencePaths = new[] { referencePath } };
            var source = CounterProgramSource("return 1;");
            var first = await service.CompileProgramAsync(source, environment);

            File.Copy(typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.Location, referencePath, overwrite: true);
            File.SetLastWriteTimeUtc(referencePath, DateTime.UtcNow.AddSeconds(2));
            var second = await service.CompileProgramAsync(source, environment);

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.NotEqual(first.Revision, second.Revision);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task WorkerClient_ExecutesScriptOutsideHostProcess()
    {
        var options = new RoslynScriptWorkerOptions
        {
            WorkerPath = FindWorkerPath(),
            Timeout = TimeSpan.FromSeconds(15),
            MaximumWorkingSetBytes = 512L * 1024 * 1024
        };

        var result = await RoslynScriptWorkerClient.ExecuteAsync(
            CounterProgramSource("context.Output.WriteLine(\"worker-ready\"); return 42;"),
            options);

        Assert.True(result.Success, result.WorkerError);
        Assert.Equal("42", result.ReturnValueText);
        Assert.Contains("worker-ready", result.Output, StringComparison.Ordinal);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            Assert.True(result.OperatingSystemIsolationApplied);
    }

    [Fact]
    public async Task WorkerClient_ExecutesMultiFileProject()
    {
        var project = new RoslynScriptProject
        {
            SourceFiles = new[]
            {
                new RoslynScriptSourceFile("Main.cs", """
using ScriptEngine;
public sealed class WorkerProgram : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
        => new ValueTask<object?>(WorkerHelper.Value);
}
"""),
                new RoslynScriptSourceFile("WorkerHelper.cs", "internal static class WorkerHelper { public static int Value => 84; }")
            }
        };
        var options = new RoslynScriptWorkerOptions
        {
            WorkerPath = FindWorkerPath(),
            Timeout = TimeSpan.FromSeconds(15),
            MaximumWorkingSetBytes = 512L * 1024 * 1024
        };

        var result = await RoslynScriptWorkerClient.ExecuteProjectAsync(project, options);

        Assert.True(result.Success, result.WorkerError);
        Assert.Equal("84", result.ReturnValueText);
    }

    [Fact]
    public async Task WorkerPool_ReusesPersistentProcessForMultipleRequests()
    {
        var options = new RoslynScriptWorkerOptions
        {
            WorkerPath = FindWorkerPath(),
            Timeout = TimeSpan.FromSeconds(15),
            MaximumWorkingSetBytes = 512L * 1024 * 1024
        };
        using var pool = new RoslynScriptWorkerPool(options, workerCount: 1, maximumExecutionsPerWorker: 3);

        var first = await pool.ExecuteAsync(CounterProgramSource("Console.WriteLine(\"protocol-noise\"); return 21;"));
        var second = await pool.ExecuteAsync(CounterProgramSource("return 42;"));

        Assert.True(first.Success, first.WorkerError);
        Assert.True(second.Success, second.WorkerError);
        Assert.Equal("21", first.ReturnValueText);
        Assert.Equal("42", second.ReturnValueText);
    }

    [Fact]
    public async Task WorkerClient_WindowsJobObjectBlocksChildProcessCreation()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        var options = new RoslynScriptWorkerOptions
        {
            WorkerPath = FindWorkerPath(),
            Timeout = TimeSpan.FromSeconds(15),
            MaximumWorkingSetBytes = 512L * 1024 * 1024,
            RequireWindowsJobObject = true,
            MaximumProcessCount = 1
        };
        var source = CounterProgramSource("""
try
{
    using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = "/c exit 0",
        UseShellExecute = false,
        CreateNoWindow = true
    }))
    {
        if (child is null) return false;
        child.WaitForExit();
        return child.ExitCode == 0;
    }
}
catch
{
    return false;
}
""");

        var result = await RoslynScriptWorkerClient.ExecuteAsync(source, options);

        Assert.True(result.Success, result.WorkerError);
        Assert.True(result.OperatingSystemIsolationApplied);
        Assert.Equal("False", result.ReturnValueText);
    }

    [Fact]
    public async Task WorkerPool_TimedOutWorkerIsReplacedForNextRequest()
    {
        var options = new RoslynScriptWorkerOptions
        {
            WorkerPath = FindWorkerPath(),
            Timeout = TimeSpan.FromSeconds(3),
            MaximumWorkingSetBytes = 512L * 1024 * 1024
        };
        using var pool = new RoslynScriptWorkerPool(options, workerCount: 1);
        const string endlessSource = """
using ScriptEngine;
public sealed class EndlessPooledProgram : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        while (true) { }
    }
}
""";

        var timedOut = await pool.ExecuteAsync(endlessSource);
        var recovered = await pool.ExecuteAsync(CounterProgramSource("return 7;"));

        Assert.True(timedOut.TimedOut);
        Assert.True(recovered.Success, recovered.WorkerError);
        Assert.Equal("7", recovered.ReturnValueText);
    }

    [Fact]
    public async Task Analysis_RapidLargeSnapshotsKeepsCacheBoundedAndLatestSnapshotValid()
    {
        var comments = string.Join(Environment.NewLine, Enumerable.Range(0, 10_000).Select(index => "// source line " + index));
        var methods = string.Join(Environment.NewLine, Enumerable.Range(0, 320).Select(index =>
            $"    public int Method{index}(int value) => value + {index};"));
        var source = comments + Environment.NewLine + "internal sealed class LargeDemo\n{" + Environment.NewLine + methods + Environment.NewLine + "}";
        using var service = new RoslynScriptService(new RoslynScriptServiceOptions { AnalysisCacheCapacity = 4 });
        var analyses = Enumerable.Range(0, 12)
            .Select(index => service.GetDiagnosticsAsync(source + Environment.NewLine + "// revision " + index))
            .ToArray();

        await Task.WhenAll(analyses);
        var finalDiagnostics = await service.GetDiagnosticsAsync(source + Environment.NewLine + "// final");
        var statistics = service.GetCacheStatistics();

        Assert.DoesNotContain(finalDiagnostics, item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.InRange(statistics.EntryCount, 1, 4);
        Assert.True(statistics.Misses >= 12);
    }

    [Fact]
    public async Task WorkerClient_InfiniteLoopIsTerminatedByHardTimeout()
    {
        var options = new RoslynScriptWorkerOptions
        {
            WorkerPath = FindWorkerPath(),
            Timeout = TimeSpan.FromMilliseconds(700),
            MaximumWorkingSetBytes = 512L * 1024 * 1024
        };
        const string source = """
using ScriptEngine;
public sealed class EndlessProgram : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
    {
        while (true) { }
    }
}
""";

        var result = await RoslynScriptWorkerClient.ExecuteAsync(source, options);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
    }

    private static string FindWorkerPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DP.WorkFlow.sln")))
            directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("无法定位 DP.WorkFlow 解决方案目录。");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
#if NETFRAMEWORK
        return Path.Combine(directory.FullName, "src", "Platform", "Scripting", "ScriptEngine.Worker", "bin", configuration, "net48", "ScriptEngine.Worker.exe");
#else
        return Path.Combine(directory.FullName, "src", "Platform", "Scripting", "ScriptEngine.Worker", "bin", configuration, "net8.0", "ScriptEngine.Worker.dll");
#endif
    }

    private static void CreateManagedReference(string path) => CreateManagedAssembly(
        path,
        "ExternalMath",
        "public static class ExternalMath { public static int Double(int value) => value * 2; }");

    private static void CreateManagedAssembly(string path, string assemblyName, string source, params string[] references)
    {
        var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var metadataReferences = new[] { typeof(object).Assembly.Location }
            .Concat(references)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            metadataReferences,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }

    public sealed class TestGlobals
    {
        public int Value { get; set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    }
}
