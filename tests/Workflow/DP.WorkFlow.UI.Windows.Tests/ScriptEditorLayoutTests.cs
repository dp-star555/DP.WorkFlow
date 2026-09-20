using System.Drawing;
using System.Windows.Media;
using System.Windows.Forms;
using ScriptEngine.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class ScriptEditorLayoutTests
{
    [Fact]
    public void WinFormsScriptWorkspace_UsesCompactDiagnosticsAndFitsCommonDesktop()
    {
        RunInSta(() =>
        {
            var (session, startId, scriptId) = CreateScriptSession();
            var model = new WorkflowNodeEditorModel(session, startId, scriptId);
            using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(model);
            dialog.CreateControl();
            dialog.PerformLayout();

            var controls = Descendants(dialog).ToArray();
            var scriptLayout = controls
                .OfType<TableLayoutPanel>()
                .Single(panel => panel.Controls.OfType<ScriptEngine.WinForms.RoslynScriptEditorControl>().Any());
            var propertyGrid = controls.OfType<ModernPropertyGrid.WinForms.ModernPropertyGrid>().Single();

            Assert.Contains(controls, control => control is ModernUI.WinForms.ModernCommandBar);
            Assert.Equal(propertyGrid.Theme.Background.ToArgb(), propertyGrid.BackColor.ToArgb());
            Assert.True(propertyGrid.Theme.Background.R < 100 && propertyGrid.Theme.Container.R < 100,
                $"参数表仍使用浅色卡片主题：背景 {propertyGrid.Theme.Background}，卡片 {propertyGrid.Theme.Container}。");
            Assert.True(dialog.ClientSize.Height <= 720, $"脚本窗口默认客户区过高：{dialog.ClientSize.Height}px。");
            Assert.True(scriptLayout.RowStyles[2].Height <= 40, $"无诊断时诊断区域过高：{scriptLayout.RowStyles[2].Height}px。");
        });
    }

    [Fact]
    public void RoslynEditor_InitialTextHasImmediateSyntaxHighlightAndVisibleCaret()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                BackColor = System.Drawing.Color.FromArgb(30, 30, 30),
                ForeColor = System.Drawing.Color.FromArgb(226, 232, 240),
                Text = "using ScriptEngine;"
            };
            editor.CreateControl();
            System.Windows.Forms.Application.DoEvents();
            var native = Descendants(editor).OfType<ScintillaNET.Scintilla>().Single();
            var keywordColor = native.Styles[native.GetStyleAt(0)].ForeColor;
            var plainColor = native.Styles[ScintillaNET.Style.Default].ForeColor;
            var caretContrast = Math.Abs(native.CaretForeColor.R - native.BackColor.R)
                                + Math.Abs(native.CaretForeColor.G - native.BackColor.G)
                                + Math.Abs(native.CaretForeColor.B - native.BackColor.B);

            Assert.NotEqual(plainColor.ToArgb(), keywordColor.ToArgb());
            Assert.True(caretContrast >= 180,
                $"光标与编辑区背景对比度不足：caret={native.CaretForeColor}, background={native.BackColor}。");
            Assert.True(native.CaretWidth >= 2, $"光标宽度过小：{native.CaretWidth}px。");
            Assert.False(native.HScrollBar);
            Assert.Equal(ScintillaNET.WrapMode.Word, native.WrapMode);
        });
    }

    [Fact]
    public void RoslynEditor_ShowsIndentGuidesCurrentLineAndDiagnosticOverview()
    {
        RunInSta(() =>
        {
            const string source = "class Demo\n{\n    missing\n}";
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl { Text = source };
            Assert.True(editor.LiveDiagnosticsEnabled);
            editor.LiveDiagnosticsEnabled = false;
            using var host = new Form { ClientSize = new Size(760, 520) };
            host.Controls.Add(editor);
            editor.Dock = DockStyle.Fill;
            host.Show();
            host.PerformLayout();
            System.Windows.Forms.Application.DoEvents();
            var native = Descendants(editor).OfType<ScintillaNET.Scintilla>().Single();
            var overview = Descendants(editor).Single(control => control.Name == "ScriptDiagnosticOverview");
            var start = source.IndexOf("missing", StringComparison.Ordinal);
            editor.ShowDiagnostics(new[]
            {
                new ScriptEngine.RoslynScriptDiagnostic(
                    "CS0103",
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
                    "名称不存在",
                    start,
                    "missing".Length,
                    Line: 3,
                    Column: 5)
            });

            Assert.Equal(ScintillaNET.IndentView.LookBoth, native.IndentationGuides);
            Assert.True(native.CaretLineVisible);
            Assert.True(overview.Width > 0);
            Assert.NotEqual(0u, native.IndicatorAllOnFor(start) & 1u);
        });
    }

    [Fact]
    public void RoslynEditor_LiveDiagnosticsMarksBrokenSourceAfterDebounce()
    {
        RunInSta(() =>
        {
            const string source = "class Demo { void Run() { missing; } }";
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                LiveDiagnosticsDelay = 200,
                Text = source
            };
            editor.CreateControl();
            var native = Descendants(editor).OfType<ScintillaNET.Scintilla>().Single();
            var start = source.IndexOf("missing", StringComparison.Ordinal);
            var timeout = DateTime.UtcNow.AddSeconds(8);
            while ((native.IndicatorAllOnFor(start) & 1) == 0 && DateTime.UtcNow < timeout)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }

            Assert.NotEqual(0u, native.IndicatorAllOnFor(start) & 1u);
        });
    }

    [Fact]
    public void RoslynEditor_EnablesAutomaticCompletionAndRoslynFolding()
    {
        RunInSta(() =>
        {
            const string source = "class Demo\n{\n    void Run()\n    {\n        if (true)\n        {\n        }\n    }\n}";
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl { Text = source };
            editor.CreateControl();
            editor.RefreshFolding();
            var native = Descendants(editor).OfType<ScintillaNET.Scintilla>().Single();

            Assert.True(editor.AutoCompletionEnabled);
            Assert.Equal(2, editor.AutoCompletionMinimumPrefixLength);
            Assert.True(native.Margins[1].Width > 0);
            Assert.Equal(ScintillaNET.Marker.MaskFolders, native.Margins[1].Mask);
            Assert.Equal(ScintillaNET.MarkerSymbol.RgbaImage, native.Markers[ScintillaNET.Marker.Folder].Symbol);
            Assert.Equal(ScintillaNET.MarkerSymbol.RgbaImage, native.Markers[ScintillaNET.Marker.FolderOpen].Symbol);
            Assert.Equal(ScintillaNET.MarkerSymbol.Empty, native.Markers[ScintillaNET.Marker.FolderSub].Symbol);
            Assert.True((native.Lines[0].FoldLevelFlags & ScintillaNET.FoldLevelFlags.Header) != 0);
            Assert.True((native.Lines[2].FoldLevelFlags & ScintillaNET.FoldLevelFlags.Header) != 0);
            Assert.True((native.Lines[4].FoldLevelFlags & ScintillaNET.FoldLevelFlags.Header) != 0);

            editor.CollapseAllFolds();
            Assert.False(native.Lines[0].Expanded);
            editor.ExpandAllFolds();
            Assert.True(native.Lines[0].Expanded);
        });
    }

    [Fact]
    public void RoslynEditor_CollapsingBlockKeepsFollowingBlankLinesVisible()
    {
        RunInSta(() =>
        {
            const string source = """
class Demo
{
    void Run()
    {
        if (true)
        {
        }


        switch (0)
        {
        }
    }
}
""";
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Text = source
            };
            editor.CreateControl();
            editor.RefreshFolding();
            var native = Descendants(editor).OfType<ScintillaNET.Scintilla>().Single();

            native.Lines[4].ToggleFold();

            Assert.False(native.Lines[5].Visible);
            Assert.False(native.Lines[6].Visible);
            Assert.True(native.Lines[7].Visible);
            Assert.True(native.Lines[8].Visible);
            Assert.True(native.Lines[9].Visible);
        });
    }

    [Fact]
    public void RoslynEditor_ProvidesFormattingSnippetsCommentsFindAndActiveScopeGuide()
    {
        RunInSta(() =>
        {
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl
            {
                LiveDiagnosticsEnabled = false,
                Text = "class Demo{void Run(){var value=1;}}"
            };
            editor.CreateControl();

            Assert.True(editor.FormatDocument());
            Assert.Contains(Environment.NewLine + "    void Run()", editor.Text, StringComparison.Ordinal);
            Assert.True(editor.FindNext("value", wholeWord: true));
            editor.ToggleLineComment();
            Assert.Contains("// var value", editor.Text, StringComparison.Ordinal);
            editor.ToggleLineComment();
            Assert.DoesNotContain("// var value", editor.Text, StringComparison.Ordinal);

            var native = Descendants(editor).OfType<ScintillaNET.Scintilla>().Single();
            editor.Text = "if";
            editor.SelectionStart = editor.TextLength;
            Assert.True(editor.InsertSnippet("if"));
            Assert.Contains("if (true)", editor.Text, StringComparison.Ordinal);
            Assert.Contains("{" + Environment.NewLine, editor.Text, StringComparison.Ordinal);

            editor.Text = "class Demo\n{\n    void Run()\n    {\n        var value = 1;\n    }\n}";
            native.SetEmptySelection(editor.Text.IndexOf("value", StringComparison.Ordinal));
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(4, native.HighlightGuide);
        });
    }

    [Fact]
    public void RoslynEditor_TypingDoesNotClearStableSemanticColors()
    {
        RunInSta(() =>
        {
            const string source = "public sealed class Demo { public void Run() { Console.WriteLine(); } }";
            using var editor = new ScriptEngine.WinForms.RoslynScriptEditorControl { Text = source };
            editor.CreateControl();
            var native = Descendants(editor).OfType<ScintillaNET.Scintilla>().Single();
            var semanticPosition = source.IndexOf("Console", StringComparison.Ordinal);
            var semanticStyle = 40 + (int)ScriptEngine.RoslynScriptHighlightKind.Type;
            native.StartStyling(semanticPosition);
            native.SetStyling("Console".Length, semanticStyle);
            Assert.Equal(semanticStyle, native.GetStyleAt(semanticPosition));

            editor.SelectionStart = editor.TextLength;
            editor.SelectedText = " ";
            System.Windows.Forms.Application.DoEvents();

            Assert.Equal(semanticStyle, native.GetStyleAt(semanticPosition));
        });
    }

    [Fact]
    public void WinFormsScriptWorkspace_CompletionStateWaitsForUiDiagnostics()
    {
        RunInSta(() =>
        {
            var (session, startId, scriptId) = CreateScriptSession();
            var model = new WorkflowNodeEditorModel(session, startId, scriptId);
            using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(model);
            dialog.CreateControl();
            var controls = Descendants(dialog).ToArray();
            var workspace = controls.OfType<DP.WorkFlow.UI.WinForms.WorkflowCSharpScriptEditorControl>().Single();
            _ = workspace.Handle; // 保证工作线程必须排队派发到UI线程。
            var editor = controls.OfType<ScriptEngine.WinForms.RoslynScriptEditorControl>().Single();
            editor.SelectAll(); editor.SelectedText = "not valid C#";
            var compilation = workspace.CompileAsync();
            Assert.True(SpinWait.SpinUntil(() => workspace.Diagnostics.Count > 0, TimeSpan.FromSeconds(10)));
            Thread.Sleep(50); // 后台已结束，刻意不泵送UI队列。
            Assert.Equal(WorkflowScriptCompilationState.Compiling, workspace.CompilationState);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!compilation.IsCompleted && DateTime.UtcNow < deadline)
            {
                System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5);
            }
            Assert.True(compilation.IsCompleted);
            Assert.Equal(WorkflowScriptCompilationState.Failed, workspace.CompilationState);
            var layout = controls.OfType<TableLayoutPanel>().Single(p => p.Name == "ScriptWorkspace");
            Assert.True(layout.RowStyles[2].Height > 0);
        });
    }

    [Fact]
    public void WinFormsScriptWorkspace_PreservesLastCompileErrorsAfterTextChanges()
    {
        RunInSta(() =>
        {
            var (session, startId, scriptId) = CreateScriptSession();
            var model = new WorkflowNodeEditorModel(session, startId, scriptId);
            using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(model);
            dialog.CreateControl();
            dialog.PerformLayout();
            var controls = Descendants(dialog).ToArray();
            var workspace = controls.OfType<DP.WorkFlow.UI.WinForms.WorkflowCSharpScriptEditorControl>().Single();
            var editor = controls.OfType<ScriptEngine.WinForms.RoslynScriptEditorControl>().Single();
            var commandBar = controls.OfType<ModernUI.WinForms.ModernCommandBar>().Single();
            var compile = commandBar.Commands.Single(command => command.Text == "编译");
            var diagnostics = controls.OfType<ListBox>().Single(list => list.Name == "ScriptDiagnostics");
            var scriptLayout = controls.OfType<TableLayoutPanel>()
                .Single(panel => panel.Name == "ScriptWorkspace");

            editor.SelectAll();
            editor.SelectedText = """
                using ScriptEngine;
                public sealed class Bad : ICSharpProgram
                {
                    public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)
                    {
                        return new ValueTask<object?>(null)
                    }
                }
                """;
            Assert.True(compile.TryExecute());
            var timeout = DateTime.UtcNow.AddSeconds(8);
            while (workspace.CompilationState == WorkflowScriptCompilationState.Compiling && DateTime.UtcNow < timeout)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }
            Assert.NotEqual(WorkflowScriptCompilationState.Compiling, workspace.CompilationState);
            var status = controls.OfType<ModernUI.WinForms.ModernStatusBar>().Single();
            var feedback = Ancestor<ModernUI.WinForms.ModernPanel>(diagnostics);
            Assert.NotNull(feedback);
            Assert.Same(feedback, Ancestor<ModernUI.WinForms.ModernPanel>(status));
            var statusText = string.Join(" ", status.Items.Cast<ToolStripItem>().Select(item => item.Text));
            Assert.Contains("个错误", statusText, StringComparison.Ordinal);
            Assert.DoesNotContain("Ctrl+Space", statusText, StringComparison.Ordinal);
            Assert.True(feedback!.ClientSize.Height >= status.PreferredSize.Height,
                $"反馈框发生垂直裁剪：框高 {feedback.ClientSize.Height}px，状态栏首选高度 {status.PreferredSize.Height}px。");
            Assert.InRange(scriptLayout.RowStyles[2].Height, 36, 60);
            Assert.Equal(1, workspace.Diagnostics.Count(item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
            var previousDiagnostics = diagnostics.Items.Cast<object>().Select(Convert.ToString).Where(text => text is not null).ToArray();
            Assert.Contains(previousDiagnostics, text => text!.StartsWith("错误 CS", StringComparison.Ordinal));

            editor.SelectionStart = editor.TextLength;
            editor.SelectedText = " ";
            System.Windows.Forms.Application.DoEvents();

            Assert.True(scriptLayout.RowStyles[2].Height > 40, "修改代码后不应立即隐藏上次编译错误。");
            Assert.Contains(diagnostics.Items.Cast<object>().Select(Convert.ToString),
                text => text?.Contains("上次编译", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    public void WinFormsNodeEditor_InitiallyFocusesTheCodeCaret()
    {
        RunInSta(() =>
        {
            var (session, startId, scriptId) = CreateScriptSession();
            var model = new WorkflowNodeEditorModel(session, startId, scriptId);
            using var dialog = new DP.WorkFlow.UI.WinForms.WorkflowNodeEditorDialog(model);
            dialog.Show();
            System.Windows.Forms.Application.DoEvents();
            var native = Descendants(dialog).OfType<ScintillaNET.Scintilla>().Single();

            Assert.True(native.Focused, "脚本窗口首次显示时输入焦点应位于代码区。 ");
            dialog.Close();
        });
    }

    [Fact]
    public void BufferedScriptProperty_UsesTheSameEditorControlGroup()
    {
        RunInSta(() =>
        {
            var page = WorkflowScriptEditorPageModel.CreateBuffer("return null;");
            using var workspace = new DP.WorkFlow.UI.WinForms.WorkflowCSharpScriptEditorControl
            {
                Page = page,
                Dock = DockStyle.Fill
            };
            using var host = new Form { ClientSize = new Size(320, 520) };
            host.Controls.Add(workspace);
            host.Show();
            host.PerformLayout();
            for (var index = 0; index < 3; index++)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }
            var controls = Descendants(workspace).ToArray();
            var commands = controls.OfType<ModernUI.WinForms.ModernCommandBar>().Single();

            Assert.Single(controls.OfType<ScriptEngine.WinForms.RoslynScriptEditorControl>());
            Assert.Single(controls.OfType<ModernUI.WinForms.ModernListBox>());
            Assert.Single(controls.OfType<ModernUI.WinForms.ModernStatusBar>());
            Assert.InRange(commands.Width, 250, 320);
            var expectedActions = commands.Commands.Count(command => command.Kind == ModernUI.WinForms.ModernCommandKind.Action && command.Visible);
            var visiblePresenters = controls.OfType<ModernUI.WinForms.ModernButton>()
                .Where(button => button.Parent == commands && button.Command is not null && button.Visible)
                .ToArray();
            Assert.Equal(expectedActions, visiblePresenters.Length);
            Assert.Equal(visiblePresenters.Length, visiblePresenters.Select(button => button.Left).Distinct().Count());
            Assert.All(visiblePresenters, button => Assert.True(commands.ClientRectangle.Contains(button.Bounds),
                $"工具栏命令超出可视区域：{button.Command?.Text}, bounds={button.Bounds}, client={commands.ClientRectangle}。"));
            Assert.False(commands.OverflowVisible);
            Assert.False(commands.Commands.Single(command => command.Text == "DLL 引用").Visible);
            Assert.Equal(Keys.Control | Keys.Space,
                commands.Commands.Single(command => command.Text == "代码补全").ShortcutKeys);
            Assert.False(page.SupportsReferenceManagement);
            host.Close();
        });
    }

    [Fact]
    public void WpfScriptWorkspace_UsesDarkCommandsAndCompactDiagnostics()
    {
        RunInSta(() =>
        {
            var (session, startId, scriptId) = CreateScriptSession();
            var model = new WorkflowNodeEditorModel(session, startId, scriptId);
            var window = new DP.WorkFlow.UI.Wpf.WorkflowNodeEditorWindow(model);
            window.Measure(new System.Windows.Size(1060, 720));
            window.Arrange(new System.Windows.Rect(0, 0, 1060, 720));
            window.UpdateLayout();

            var elements = LogicalDescendants(window).ToArray();
            var scriptLayout = elements.OfType<System.Windows.Controls.Grid>()
                .Single(grid => grid.Children.OfType<ScriptEngine.Wpf.RoslynScriptEditorControl>().Any());
            var compile = elements.OfType<System.Windows.Controls.Button>()
                .Single(button => string.Equals(button.Content as string, "编译", StringComparison.Ordinal));
            var commandColor = Assert.IsType<SolidColorBrush>(compile.Background).Color;
            var feedback = elements.OfType<System.Windows.Controls.Border>()
                .Single(border => border.Name == "ScriptFeedback");
            var feedbackElements = LogicalDescendants(feedback).ToArray();

            Assert.Contains(feedbackElements, element => element is System.Windows.Controls.ListBox list && list.Name == "ScriptDiagnostics");
            Assert.Contains(feedbackElements, element => element is System.Windows.Controls.TextBlock);
            var toolbar = elements.OfType<System.Windows.Controls.ToolBar>().Single();
            Assert.False(toolbar.HasOverflowItems);
            Assert.True(window.Height <= 760, $"脚本窗口默认高度过高：{window.Height}px。");
            Assert.True(scriptLayout.RowDefinitions[2].Height.Value <= 40,
                $"无诊断时诊断区域过高：{scriptLayout.RowDefinitions[2].Height.Value}px。");
            Assert.True(commandColor.R < 100 && commandColor.G < 100 && commandColor.B < 100,
                $"脚本命令仍使用浅色系统按钮：{commandColor}。");

            var referenceWindow = new ScriptEngine.Wpf.RoslynScriptReferenceManagerWindow(window, Array.Empty<string>());
            referenceWindow.Measure(new System.Windows.Size(900, 500));
            referenceWindow.Arrange(new System.Windows.Rect(0, 0, 900, 500));
            referenceWindow.UpdateLayout();
            var referenceColor = Assert.IsType<SolidColorBrush>(referenceWindow.Background).Color;
            var referenceButton = LogicalDescendants(referenceWindow).OfType<System.Windows.Controls.Button>().First();
            var referenceButtonColor = Assert.IsType<SolidColorBrush>(referenceButton.Background).Color;
            Assert.True(referenceColor.R < 100 && referenceButtonColor.R < 100,
                $"WPF DLL 引用窗口未继承深色主题：窗口 {referenceColor}，按钮 {referenceButtonColor}。");
            referenceWindow.Close();
            window.Close();
        });
    }

    [Fact]
    public void WorkflowPropertyPanel_ValueRefresh_ReusesExistingEditorControls()
    {
        RunInSta(() =>
        {
            var (session, startId, _) = CreateScriptSession();
            using var panel = new DP.WorkFlow.UI.WinForms.WorkflowPropertyPanel
            {
                Session = session,
                EntryNodeId = startId,
                Size = new Size(420, 620)
            };
            panel.CreateControl();
            panel.PerformLayout();
            var grid = Descendants(panel).OfType<ModernPropertyGrid.WinForms.ModernPropertyGrid>().Single();
            var before = Descendants(grid).OfType<ModernUI.WinForms.ModernInput>().Cast<Control>().ToArray();
            Assert.NotEmpty(before);

            session.Canvas.Nodes.Single(node => node.Node.Id == session.SelectedNodeId).Node.Title = "更新后的标题";
            session.NotifyNodeConfigurationChanged();
            System.Windows.Forms.Application.DoEvents();
            var after = Descendants(grid).OfType<ModernUI.WinForms.ModernInput>().Cast<Control>().ToArray();

            Assert.Equal(before.Length, after.Length);
            Assert.All(before, editor => Assert.Contains(editor, after));
        });
    }

    [Fact]
    public void WinFormsReferenceManager_MatchesDarkScriptWorkspace()
    {
        RunInSta(() =>
        {
            using var dialog = new RoslynScriptReferenceManagerDialog(Array.Empty<string>());
            dialog.CreateControl();
            Assert.True(dialog.BackColor.R < 100 && dialog.BackColor.G < 100 && dialog.BackColor.B < 100,
                $"DLL 引用窗口仍使用浅色背景：{dialog.BackColor}。");
            Assert.All(Descendants(dialog).OfType<Button>(), button => Assert.InRange(button.Height, 28, 34));
        });
    }

    private static (WorkflowDesignerSession Session, string StartId, string ScriptId) CreateScriptSession()
    {
        var canvasDocument = new WorkflowDocument { Name = "Script UI" };
        var canvas = canvasDocument.CanvasProjection;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var session = new WorkflowDesignerSession(canvasDocument, catalog);
        var start = session.AddNode("Start", 20, 20);
        var script = session.AddNode("CSharpScript", 160, 20);
        return (session, start.Node.Id, script.Node.Id);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static T? Ancestor<T>(Control control) where T : Control
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            if (parent is T match) return match;
        return null;
    }

    private static IEnumerable<System.Windows.DependencyObject> LogicalDescendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child)) yield return descendant;
        }
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI 布局测试超时。");
        Assert.Null(failure);
    }
}
