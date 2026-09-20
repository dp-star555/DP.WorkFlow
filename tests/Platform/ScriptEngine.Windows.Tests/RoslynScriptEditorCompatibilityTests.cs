using Microsoft.CodeAnalysis;

namespace ScriptEngine.Windows.Tests;

public sealed class RoslynScriptEditorCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionDebounce_Uses300msAndImmediateRequestConsumesTimer(bool projectMode)
    {
        RunInSta(() =>
        {
            using var single = new ScriptEngine.WinForms.RoslynScriptEditorControl { LiveDiagnosticsEnabled = false, Text = "ab" };
            using var project = new ScriptEngine.WinForms.RoslynScriptProjectEditorControl { LiveDiagnosticsEnabled = false };
            var provider = new PendingCompletionProvider();
            single.CompletionProvider = provider;
            project.CompletionProvider = provider;
            project.ActiveEditor.Text = "ab";
            single.SelectionStart = 2;
            project.ActiveEditor.SelectionStart = 2;
            object target = projectMode ? (object)project : single;
            Assert.Equal(300, projectMode ? project.AutoCompletionDelay : single.AutoCompletionDelay);
            var timer = (System.Windows.Forms.Timer)target.GetType().GetField("_completionTimer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;

            InvokePrivate(target, projectMode ? "ScheduleCompletion" : "ScheduleAutomaticCompletion", 'b');
            Assert.True(timer.Enabled);
            Assert.Equal(0, provider.Calls);

            var request = (Task)InvokePrivateWithResult(target, projectMode ? "ShowProjectCompletionsAsync" : "ShowAutomaticCompletionsAsync")!;
            Assert.False(timer.Enabled);
            Assert.Equal(1, provider.Calls);
            if (projectMode) project.AddFile("Other.cs", "class Other { }");
            else single.Text = "abc";
            Assert.True(provider.Token.IsCancellationRequested);
            // 模拟提供器忽略取消后仍返回旧结果；控件必须丢弃。
            provider.Result.SetResult(new[] { new RoslynScriptCompletionItem("stale", "stale") });
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!request.IsCompleted && DateTime.UtcNow < deadline)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.True(request.IsCompleted);
            request.GetAwaiter().GetResult();
            Assert.False(single.IsCompletionListVisible);
            Assert.False(project.ActiveEditor.IsCompletionListVisible);
        });
    }

    private sealed class PendingCompletionProvider : ScriptEngine.Workspaces.IRoslynScriptCompletionProvider
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<IReadOnlyList<RoslynScriptCompletionItem>> Result { get; } = new();

        public Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(
            RoslynScriptProject project, string activeFileName, int position,
            RoslynScriptEnvironment? environment = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            return Result.Task;
        }
    }

    [Fact]
    public void WinFormsEditor_CreatesFormatsFoldsAndMarksDiagnostics()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Text = "class Demo{void Run(){missing;}}"
            };
            editor.CreateControl();

            Assert.True(editor.FormatDocument());
            editor.RefreshFolding();
            var diagnostics = editor.GetDiagnostics();
            editor.ShowDiagnostics(diagnostics);

            Assert.Contains(diagnostics, item => item.Id == "CS0103");
            Assert.True(editor.FindNext("missing", wholeWord: true));
            editor.ToggleLineComment();
            Assert.Contains("// missing", editor.Text, StringComparison.Ordinal);

            editor.Text = "class Demo { void Run() { var value = 1 } }";
            var missingSemicolon = Assert.Single(editor.GetDiagnostics(), item => item.Id == "CS1002");
            var action = Assert.Single(editor.GetCodeActions(missingSemicolon));
            editor.ApplyCodeAction(action);
            Assert.DoesNotContain(editor.GetDiagnostics(), item => item.Id == "CS1002");
        });
    }

    [Fact]
    public void CompletionList_PreservesLabelsAndDoesNotConflictWithCallTips()
    {
        RunInSta(() =>
        {
            const string source = "class Demo { void Run(ScriptEngine.CSharpProgramContext context) { context. } }";
            using var form = new System.Windows.Forms.Form { Width = 640, Height = 400 };
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                LiveDiagnosticsEnabled = false,
                Text = source
            };
            form.Controls.Add(editor);
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            editor.SelectionStart = source.IndexOf("context.", StringComparison.Ordinal) + "context.".Length;
            editor.Focus();
            var scintilla = FindDescendant<ScintillaNET.Scintilla>(editor);
            var items = editor.GetCompletionsAsync().GetAwaiter().GetResult();

            Assert.Contains(items, item => item.InsertionText == "GetHostContext()");
            Assert.False(ScriptEngine.WinForms.RoslynScriptEditorControl.ShouldIncludeSnippetCompletions("context.", 8));
            Assert.False(ScriptEngine.WinForms.RoslynScriptEditorControl.ShouldIncludeSnippetCompletions("context.Wri", 11));
            Assert.True(ScriptEngine.WinForms.RoslynScriptEditorControl.ShouldIncludeSnippetCompletions("if", 2));
            editor.ShowCompletionItems(items);
            System.Windows.Forms.Application.DoEvents();
            Assert.True(editor.IsCompletionListVisible);
            Assert.False(scintilla.AutoCActive);
            Assert.False(scintilla.CallTipActive);
        });
    }

    [Fact]
    public void SnippetShortcut_TwoTabPressesExpandTemplate()
    {
        RunInSta(() =>
        {
            using var form = new System.Windows.Forms.Form { Width = 640, Height = 400 };
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                LiveDiagnosticsEnabled = false,
                Text = "if"
            };
            form.Controls.Add(editor);
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            editor.SelectionStart = 2;
            editor.ShowCompletionItems(new[]
            {
                new RoslynScriptCompletionItem("if", "if", Kind: RoslynScriptCompletionKind.Keyword),
                new RoslynScriptCompletionItem("if 代码片段", "if", Kind: RoslynScriptCompletionKind.Snippet, FilterText: "if")
            });
            var scintilla = FindDescendant<ScintillaNET.Scintilla>(editor);

            InvokePrivate(scintilla, "OnKeyDown", new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Tab));
            InvokePrivate(scintilla, "OnKeyDown", new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Tab));

            Assert.Equal("if (true)\r\n{\r\n    \r\n}", editor.Text);
            Assert.Equal(editor.Text.IndexOf("    \r\n", StringComparison.Ordinal) + 4, editor.SelectionStart);
        });
    }

    [Fact]
    public void CustomCompletionPopup_FuzzyMatchCommitsSelectedItem()
    {
        RunInSta(() =>
        {
            using var form = new System.Windows.Forms.Form { Width = 640, Height = 400 };
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                LiveDiagnosticsEnabled = false,
                Text = "// 中文输入法文本\r\nghct"
            };
            form.Controls.Add(editor);
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            editor.SelectionStart = editor.TextLength;
            var scintilla = FindDescendant<ScintillaNET.Scintilla>(editor);
            editor.ShowCompletionItems(new[]
            {
                new RoslynScriptCompletionItem("GetHashCode", "GetHashCode()", Kind: RoslynScriptCompletionKind.Method),
                new RoslynScriptCompletionItem("GetHostContext", "GetHostContext()", Kind: RoslynScriptCompletionKind.Method)
            });

            InvokePrivate(scintilla, "OnKeyDown", new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter));

            Assert.Equal("// 中文输入法文本\r\nGetHostContext()", editor.Text);
            Assert.False(editor.IsCompletionListVisible);
        });
    }

    [Fact]
    public void WorkspacesCompletion_CommitAppliesImportAndIdentifierInOneUndoStep()
    {
        RunInSta(() =>
        {
            const string source = "internal sealed class Demo { StringBui }";
            using var workspace = new ScriptEngine.Workspaces.RoslynScriptWorkspaceService();
            using var form = new System.Windows.Forms.Form { Width = 700, Height = 440 };
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                LiveDiagnosticsEnabled = false,
                CompletionProvider = workspace,
                Text = source
            };
            form.Controls.Add(editor);
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            editor.SelectionStart = source.IndexOf("StringBui", StringComparison.Ordinal) + "StringBui".Length;
            var items = editor.GetCompletionsAsync().GetAwaiter().GetResult();
            editor.ShowCompletionItems(items);
            var scintilla = FindDescendant<ScintillaNET.Scintilla>(editor);

            InvokePrivate(scintilla, "OnKeyDown", new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter));

            Assert.Contains("using System.Text;", editor.Text, StringComparison.Ordinal);
            Assert.Contains("StringBuilder", editor.Text, StringComparison.Ordinal);
            editor.Undo();
            Assert.Equal(source, editor.Text);
        });
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void InputBehavior_SnippetExpansionPreservesDocumentLineEnding(string newline)
    {
        var behavior = new ScriptEngine.WinForms.ScriptEditorInputBehavior();
        var source = "    if" + newline + "// existing";

        var armed = behavior.ProcessTab(source, 6, 0, control: false, alt: false, shift: false);
        var expand = behavior.ProcessTab(source, 6, 0, control: false, alt: false, shift: false);
        var created = behavior.TryCreateSnippetEdit("if", source, expand.Start, expand.End, string.Empty, out var edit);

        Assert.Equal(ScriptEngine.WinForms.ScriptSnippetDecisionKind.Armed, armed.Kind);
        Assert.Equal(ScriptEngine.WinForms.ScriptSnippetDecisionKind.Expand, expand.Kind);
        Assert.True(created);
        Assert.Contains(newline + "    {" + newline + "        " + newline + "    }", edit.NewText, StringComparison.Ordinal);
        Assert.DoesNotContain(newline == "\n" ? "\r\n" : "\n\n", edit.NewText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0F)]
    [InlineData(1.5F)]
    [InlineData(2.5F)]
    public void CompletionPopup_MultiMonitorEdgePlacementStaysInsideWorkingArea(float scale)
    {
        var workingArea = new System.Drawing.Rectangle(1920, 0, 1920, 1080);
        var popupSize = new System.Drawing.Size((int)(720 * scale), (int)(290 * scale));

        var bounds = ScriptEngine.WinForms.ScriptCompletionPopup.CalculatePopupBounds(
            workingArea,
            new System.Drawing.Point(3820, 1040),
            popupSize,
            scale);

        Assert.True(workingArea.Contains(bounds));
        Assert.True(bounds.Top < 1040);
    }

    [Fact]
    public void StandaloneOpeningBrace_AlignsWithControlStatement()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Text = "        if()\r\n            {}"
            };
            editor.CreateControl();
            editor.SelectionStart = editor.Text.IndexOf("{}", StringComparison.Ordinal) + 1;

            InvokePrivate(editor, "ApplyAutomaticIndent", '{');

            Assert.Equal("        if()\r\n        {}", editor.Text);
            Assert.Equal(editor.Text.IndexOf("{}", StringComparison.Ordinal) + 1, editor.SelectionStart);
        });
    }

    [Fact]
    public void PairedBraceLineBreak_CreatesOneIndentedBlankLine()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Text = "if (true)\r\n{\r\n}"
            };
            editor.CreateControl();
            editor.SelectionStart = editor.Text.IndexOf('}');

            InvokePrivate(editor, "InsertAutomaticPair", '\n');
            InvokePrivate(editor, "ApplyAutomaticIndent", '\n');

            Assert.Equal("if (true)\r\n{\r\n    \r\n}", editor.Text);
            Assert.Equal(editor.Text.IndexOf("    \r\n", StringComparison.Ordinal) + 4, editor.SelectionStart);
        });
    }

    [Fact]
    public void WinFormsProjectEditor_CompilesAndManagesMultipleFiles()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptProjectEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Project = CreateMultiFileProject()
            };
            editor.CreateControl();

            var compilation = editor.CompileProjectAsync().GetAwaiter().GetResult();

            Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(item => item.Message)));
            Assert.Equal(new[] { "Main.cs", "Helper.cs" }, editor.FileNames);
            Assert.True(editor.SelectFile("Helper.cs"));
            Assert.Equal("Helper.cs", editor.ActiveFileName);
            Assert.True(editor.RenameFile("Helper.cs", "Features/Helper.cs"));
            editor.AddFile("Model.cs", "internal sealed class Model { }");
            Assert.Contains("Model.cs", editor.ModifiedFileNames);
            Assert.True(editor.RemoveFile("Model.cs"));

            Assert.NotNull(editor.ProjectEditingService);
            Assert.NotNull(editor.CompletionProvider);
            Assert.True(editor.SelectFile("Features/Helper.cs"));
            var valuePosition = editor.ActiveEditor.Text.IndexOf("Value", StringComparison.Ordinal) + 2;
            var rename = editor.CreateRenameEditAsync("Features/Helper.cs", valuePosition, "Answer")
                .GetAwaiter().GetResult();
            Assert.NotNull(rename);
            editor.ApplyWorkspaceEdit(rename!);
            Assert.DoesNotContain("Helper.Value", editor.Project.SourceFiles.Single(file => file.FileName == "Main.cs").Source, StringComparison.Ordinal);
            Assert.DoesNotContain("int Value", editor.Project.SourceFiles.Single(file => file.FileName == "Features/Helper.cs").Source, StringComparison.Ordinal);
            Assert.True(editor.CompileProjectAsync().GetAwaiter().GetResult().Success);
            Assert.True(editor.CanUndoProjectEdit);
            Assert.True(editor.UndoProjectEdit());
            Assert.Contains("int Value", editor.Project.SourceFiles.Single(file => file.FileName == "Features/Helper.cs").Source, StringComparison.Ordinal);
            Assert.True(editor.RedoProjectEdit());
            Assert.Contains("int Answer", editor.Project.SourceFiles.Single(file => file.FileName == "Features/Helper.cs").Source, StringComparison.Ordinal);
            Assert.NotEmpty(editor.ModifiedFileNames);
            editor.MarkProjectSaved();
            Assert.Empty(editor.ModifiedFileNames);

            var revision = editor.Project.ComputeSourceRevision();
            var staleEdit = rename! with { OriginalProjectRevision = revision };
            editor.ActiveEditor.Text += Environment.NewLine + "// newer revision";
            Assert.False(editor.CanUndoProjectEdit);
            Assert.Throws<InvalidOperationException>(() => editor.ApplyWorkspaceEdit(staleEdit));
        });
    }

    [Fact]
    public void ProjectEditor_ModifiedCloseAndReferencesPanelBehaveAtomically()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptProjectEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Project = CreateMultiFileProject()
            };
            editor.CreateControl();
            Assert.True(editor.SelectFile("Helper.cs"));
            editor.ActiveEditor.Text += " // changed";
            var promptedFile = string.Empty;

            Assert.False(editor.TryCloseFile("Helper.cs", fileName =>
            {
                promptedFile = fileName;
                return false;
            }));
            Assert.Equal("Helper.cs", promptedFile);
            Assert.True(editor.TryCloseFile("Helper.cs", _ => true));
            Assert.Single(editor.FileNames);

            var locations = new[]
            {
                new ScriptEngine.Workspaces.RoslynScriptReferenceLocation("Main.cs", 0, 1, 1, 1, true)
            };
            InvokePrivate(editor, "ShowReferencesPanel", (object)locations);
            Assert.True(editor.ReferencesPanelVisible);
            editor.HideReferencesPanel();
            Assert.False(editor.ReferencesPanelVisible);
        });
    }

    [Fact]
    public void WpfEditor_ExposesSameEditingSurfaceThroughWindowsFormsHost()
    {
        RunInSta(() =>
        {
            var editor = new ScriptEngine.Wpf.RoslynScriptEditorControl
            {
                ScriptText = "class Demo{void Run(){var value=1;}}",
                LiveDiagnosticsEnabled = false
            };
            var window = new System.Windows.Window { Content = editor, Width = 640, Height = 420 };
            window.Show();
            window.UpdateLayout();

            Assert.True(editor.FormatDocument());
            Assert.True(editor.FindNext("value", wholeWord: true));
            editor.RefreshFolding();
            editor.CollapseAllFolds();
            editor.ExpandAllFolds();
            editor.ShowWhitespace = true;
            editor.Zoom = 1;

            Assert.True(editor.ShowWhitespace);
            Assert.Equal(1, editor.Zoom);
            window.Close();
        });
    }

    [Fact]
    public void WpfProjectEditor_HostsSameMultiFileProjectSurface()
    {
        RunInSta(() =>
        {
            var editor = new ScriptEngine.Wpf.RoslynScriptProjectEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Project = CreateMultiFileProject()
            };
            var window = new System.Windows.Window { Content = editor, Width = 700, Height = 460 };
            window.Show();
            window.UpdateLayout();

            Assert.Equal(2, editor.FileNames.Count);
            Assert.True(editor.SelectFile("Helper.cs"));
            Assert.True(editor.CompileProjectAsync().GetAwaiter().GetResult().Success);
            window.Close();
        });
    }

    [Fact]
    public void WinFormsEditor_RestrictedPolicyReportsDeniedAccess()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                LiveDiagnosticsEnabled = false,
                ScriptEnvironment = new RoslynScriptEnvironment
                {
                    SecurityPolicy = RoslynScriptSecurityPolicy.Restricted
                },
                Text = "class Demo { bool Read() => System.IO.File.Exists(\"x\"); }"
            };

            var diagnostic = Assert.Single(editor.GetDiagnostics(), item => item.Id == "SE2001");

            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(RoslynScriptDiagnosticOrigin.UserSource, diagnostic.Origin);
        });
    }

    private static RoslynScriptProject CreateMultiFileProject() => new()
    {
        SourceFiles = new[]
        {
            new RoslynScriptSourceFile("Main.cs", """
using ScriptEngine;
public sealed class Program : ICSharpProgram
{
    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
        => new ValueTask<object?>(Helper.Value);
}
"""),
            new RoslynScriptSourceFile("Helper.cs", "internal static class Helper { public static int Value => 42; }")
        }
    };

    private static void InvokePrivate(object instance, string methodName, params object[] arguments) =>
        _ = InvokePrivateWithResult(instance, methodName, arguments);

    private static object? InvokePrivateWithResult(object instance, string methodName, params object[] arguments)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到方法 {methodName}。");
        return method.Invoke(instance, arguments);
    }

    private static T FindDescendant<T>(System.Windows.Forms.Control parent) where T : System.Windows.Forms.Control
    {
        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            if (child is T match) return match;
            if (child.HasChildren)
            {
                try { return FindDescendant<T>(child); }
                catch (InvalidOperationException) { }
            }
        }
        throw new InvalidOperationException($"找不到子控件 {typeof(T).Name}。");
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }
}
