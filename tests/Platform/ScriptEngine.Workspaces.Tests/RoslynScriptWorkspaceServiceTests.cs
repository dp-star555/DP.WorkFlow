using Microsoft.CodeAnalysis;
using ScriptEngine.Workspaces;

namespace ScriptEngine.Workspaces.Tests;

public sealed class RoslynScriptWorkspaceServiceTests
{
    [Fact]
    public async Task GetCodeFixesAsync_AddsMissingImportFromReferencedType()
    {
        const string source = "internal sealed class Demo { StringBuilder Build() => new StringBuilder(); }";
        var project = Project(("Main.cs", source));
        using var compiler = new RoslynScriptService();
        var diagnostic = (await compiler.GetProjectDiagnosticsAsync(project))
            .First(item => item.Id == "CS0246" && item.SourceName == "Main.cs");
        var workspace = new RoslynScriptWorkspaceService();

        var fixes = await workspace.GetCodeFixesAsync(project, "Main.cs", diagnostic);

        var fix = Assert.Single(fixes, item => item.Title == "添加 using System.Text");
        Assert.Equal(RoslynScriptWorkspaceEditKind.AddImport, fix.Kind);
        Assert.Equal(project.ComputeSourceRevision(), fix.OriginalProjectRevision);
        Assert.Equal(new[] { "Main.cs" }, fix.ChangedFiles);
        var changedSource = Assert.Single(fix.Project.SourceFiles).Source;
        Assert.StartsWith("using System.Text;", changedSource, StringComparison.Ordinal);
        var changedDiagnostics = await compiler.GetProjectDiagnosticsAsync(fix.Project);
        Assert.DoesNotContain(changedDiagnostics, item => item.Id == "CS0246");
    }

    [Fact]
    public async Task RenameSymbolAsync_RenamesDeclarationAndReferencesAcrossFiles()
    {
        var project = Project(
            ("Main.cs", "internal sealed class Runner { int Run() => Helper.Calculate(21); }"),
            ("Helper.cs", "internal static class Helper { public static int Calculate(int value) => value * 2; }"));
        var position = project.SourceFiles[1].Source.IndexOf("Calculate", StringComparison.Ordinal) + 2;
        var workspace = new RoslynScriptWorkspaceService();

        var edit = await workspace.RenameSymbolAsync(project, "Helper.cs", position, "Double");

        Assert.NotNull(edit);
        Assert.Equal(RoslynScriptWorkspaceEditKind.Rename, edit!.Kind);
        Assert.Equal(new[] { "Helper.cs", "Main.cs" }, edit.ChangedFiles.OrderBy(item => item, StringComparer.Ordinal).ToArray());
        Assert.All(edit.Project.SourceFiles, file => Assert.DoesNotContain("Calculate", file.Source, StringComparison.Ordinal));
        Assert.All(edit.Project.SourceFiles, file => Assert.Contains("Double", file.Source, StringComparison.Ordinal));
        using var compiler = new RoslynScriptService();
        Assert.DoesNotContain(
            await compiler.GetProjectDiagnosticsAsync(edit.Project),
            item => item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task FindReferencesAsync_ReturnsDeclarationAndCrossFileUsages()
    {
        var project = Project(
            ("Main.cs", "internal sealed class Runner { int Run() => Helper.Calculate(21); }"),
            ("Helper.cs", "internal static class Helper { public static int Calculate(int value) => value * 2; }"));
        var position = project.SourceFiles[0].Source.IndexOf("Calculate", StringComparison.Ordinal) + 2;
        var workspace = new RoslynScriptWorkspaceService();

        var locations = await workspace.FindReferencesAsync(project, "Main.cs", position);

        Assert.Equal(2, locations.Count);
        Assert.Contains(locations, item => item is { FileName: "Helper.cs", IsDefinition: true });
        Assert.Contains(locations, item => item is { FileName: "Main.cs", IsDefinition: false });
        Assert.All(locations, item =>
        {
            Assert.True(item.Line >= 1);
            Assert.True(item.Column >= 1);
        });
    }

    [Fact]
    public async Task GetCodeFixesAsync_DoesNotSuggestDeniedNamespace()
    {
        const string source = "internal sealed class Demo { FileInfo? Value { get; } }";
        var project = Project(("Main.cs", source));
        var environment = new RoslynScriptEnvironment { SecurityPolicy = RoslynScriptSecurityPolicy.Restricted };
        using var compiler = new RoslynScriptService();
        var diagnostic = (await compiler.GetProjectDiagnosticsAsync(project, environment))
            .First(item => item.Id == "CS0246" && item.SourceName == "Main.cs");
        var workspace = new RoslynScriptWorkspaceService();

        var fixes = await workspace.GetCodeFixesAsync(project, "Main.cs", diagnostic, environment);

        Assert.DoesNotContain(fixes, item => item.Title.IndexOf("System.IO", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public async Task GetCodeFixesAsync_RemovesUnusedUsingWithoutChangingOtherFiles()
    {
        const string source = "using System.Text;\ninternal sealed class Demo { }";
        var project = Project(("Main.cs", source), ("Other.cs", "internal sealed class Other { }"));
        var diagnostic = new RoslynScriptDiagnostic(
            "CS8019",
            DiagnosticSeverity.Warning,
            "Unnecessary using directive.",
            0,
            "using System.Text;".Length,
            1,
            1,
            "Main.cs",
            RoslynScriptDiagnosticOrigin.UserSource);
        var workspace = new RoslynScriptWorkspaceService();

        var fixes = await workspace.GetCodeFixesAsync(project, "Main.cs", diagnostic);

        var edit = Assert.Single(fixes);
        Assert.Equal(RoslynScriptWorkspaceEditKind.RemoveImport, edit.Kind);
        Assert.Equal(new[] { "Main.cs" }, edit.ChangedFiles);
        Assert.DoesNotContain("System.Text", edit.Project.SourceFiles.Single(file => file.FileName == "Main.cs").Source, StringComparison.Ordinal);
        Assert.Equal(project.SourceFiles[1].Source, edit.Project.SourceFiles.Single(file => file.FileName == "Other.cs").Source);
    }

    [Fact]
    public async Task RenameSymbolAsync_ReportsNameConflict()
    {
        const string source = "internal sealed class Demo { int Value; int Existing; int Read() => Value; }";
        var project = Project(("Main.cs", source));
        var position = source.IndexOf("Value", StringComparison.Ordinal) + 2;
        var workspace = new RoslynScriptWorkspaceService();

        var edit = await workspace.RenameSymbolAsync(project, "Main.cs", position, "Existing");

        Assert.NotNull(edit);
        Assert.True(edit!.HasConflicts, string.Join(Environment.NewLine, edit.Project.SourceFiles.Select(file => file.Source)));
    }

    [Fact]
    public async Task RenameSymbolAsync_RejectsKeywordName()
    {
        var project = Project(("Main.cs", "internal sealed class Demo { int Value => 1; }"));
        using var workspace = new RoslynScriptWorkspaceService();

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.RenameSymbolAsync(project, "Main.cs", 23, "class"));
    }

    [Fact]
    public async Task GetCompletionsAsync_ReturnsRoslynCrossFileCompletionAndCommitSpan()
    {
        const string source = "internal sealed class Runner { int Run() => Helper.Cal; }";
        var project = Project(
            ("Main.cs", source),
            ("Helper.cs", "internal static class Helper { public static int Calculate(int value) => value * 2; }"));
        using var workspace = new RoslynScriptWorkspaceService();

        var items = await workspace.GetCompletionsAsync(
            project,
            "Main.cs",
            source.IndexOf("Cal", StringComparison.Ordinal) + 3);

        var calculate = Assert.Single(items, item => item.DisplayText == "Calculate");
        Assert.Equal(RoslynScriptCompletionKind.Method, calculate.Kind);
        Assert.Equal("Calculate", calculate.InsertionText);
        Assert.Equal(source.IndexOf("Cal", StringComparison.Ordinal), calculate.ReplacementStart);
        Assert.Equal(3, calculate.ReplacementLength);
        Assert.False(string.IsNullOrWhiteSpace(calculate.Description));
    }

    [Fact]
    public async Task GetCompletionsAsync_UnimportedTypeAddsUsingOnCommit()
    {
        const string source = "internal sealed class Demo { StringBui }";
        var project = Project(("Main.cs", source));
        using var workspace = new RoslynScriptWorkspaceService();

        var items = await workspace.GetCompletionsAsync(
            project,
            "Main.cs",
            source.IndexOf("StringBui", StringComparison.Ordinal) + "StringBui".Length);

        var item = Assert.Single(items, candidate => candidate.DisplayText == "StringBuilder");
        Assert.NotNull(item.ReplacementStart);
        Assert.Contains("StringBuilder", item.InsertionText, StringComparison.Ordinal);
        Assert.NotNull(item.TextChanges);
        var changedSource = source;
        foreach (var change in item.TextChanges!.OrderByDescending(change => change.Start))
            changedSource = changedSource.Remove(change.Start, change.Length).Insert(change.Start, change.NewText);
        Assert.Contains("using System.Text;", changedSource, StringComparison.Ordinal);
        Assert.Contains("StringBuilder", changedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCompletionsAsync_RestrictedPolicyFiltersDeniedImports()
    {
        const string source = "internal sealed class Demo { FileIn }";
        var project = Project(("Main.cs", source));
        var environment = new RoslynScriptEnvironment { SecurityPolicy = RoslynScriptSecurityPolicy.Restricted };
        using var workspace = new RoslynScriptWorkspaceService();

        var items = await workspace.GetCompletionsAsync(project, "Main.cs", source.IndexOf("FileIn", StringComparison.Ordinal) + 6, environment);

        Assert.DoesNotContain(items, item => item.DisplayText == "FileInfo");
    }

    [Fact]
    public async Task WorkspaceCache_ReusesShapeAndUpdatesChangedDocumentIncrementally()
    {
        var first = Project(("Main.cs", "internal sealed class Demo { int Value => 1; }"));
        var second = Project(("Main.cs", "internal sealed class Demo { int Value => 2; }"));
        using var workspace = new RoslynScriptWorkspaceService(new RoslynScriptWorkspaceOptions { CacheCapacity = 2 });

        await workspace.FindReferencesAsync(first, "Main.cs", first.SourceFiles[0].Source.IndexOf("Value", StringComparison.Ordinal));
        await workspace.FindReferencesAsync(second, "Main.cs", second.SourceFiles[0].Source.IndexOf("Value", StringComparison.Ordinal));

        var statistics = workspace.GetCacheStatistics();
        Assert.Equal(1, statistics.Misses);
        Assert.Equal(1, statistics.Hits);
        Assert.Equal(1, statistics.IncrementalDocumentUpdates);
        Assert.Equal(1, statistics.EntryCount);
    }

    [Fact]
    public async Task WorkspaceCache_EvictsLeastRecentProjectShapeAtConfiguredCapacity()
    {
        var first = Project(("Main.cs", "internal sealed class First { int Value => 1; }"));
        var second = Project(("Other.cs", "internal sealed class Second { int Value => 2; }"));
        using var workspace = new RoslynScriptWorkspaceService(new RoslynScriptWorkspaceOptions { CacheCapacity = 1 });

        await workspace.FindReferencesAsync(first, "Main.cs", first.SourceFiles[0].Source.IndexOf("Value", StringComparison.Ordinal));
        await workspace.FindReferencesAsync(second, "Other.cs", second.SourceFiles[0].Source.IndexOf("Value", StringComparison.Ordinal));

        var statistics = workspace.GetCacheStatistics();
        Assert.Equal(2, statistics.Misses);
        Assert.Equal(1, statistics.EntryCount);
    }

    [Fact]
    public async Task WorkspaceEdit_ContainsPreviewableFileChanges()
    {
        const string source = "internal sealed class Demo { int Value => 1; int Read() => Value; }";
        var project = Project(("Main.cs", source));
        using var workspace = new RoslynScriptWorkspaceService();

        var edit = await workspace.RenameSymbolAsync(
            project,
            "Main.cs",
            source.IndexOf("Value", StringComparison.Ordinal),
            "Result");

        Assert.NotNull(edit);
        var change = Assert.Single(edit!.FileChanges!);
        Assert.Equal("Main.cs", change.FileName);
        Assert.Equal(source, change.OriginalSource);
        Assert.Contains("Result", change.ChangedSource, StringComparison.Ordinal);
        Assert.True(change.OriginalLength > 0);
    }

    private static RoslynScriptProject Project(params (string Name, string Source)[] files) => new()
    {
        SourceFiles = files.Select(file => new RoslynScriptSourceFile(file.Name, file.Source)).ToArray()
    };
}
