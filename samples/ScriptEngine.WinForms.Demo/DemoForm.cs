using Microsoft.CodeAnalysis;
using global::ScriptEngine.WinForms;
using global::ScriptEngine.Workspaces;

namespace ScriptEngine.WinForms.Demo;

/// <summary>展示脚本编辑、补全、诊断、编译、运行和 DLL 引用管理的最小宿主。</summary>
internal sealed class DemoForm : Form
{
    private static readonly Color WindowColor = Color.FromArgb(24, 24, 27);
    private static readonly Color SurfaceColor = Color.FromArgb(30, 30, 30);
    private static readonly Color RaisedColor = Color.FromArgb(39, 39, 42);
    private static readonly Color BorderColor = Color.FromArgb(63, 63, 70);
    private static readonly Color TextColor = Color.FromArgb(226, 232, 240);
    private static readonly Color MutedTextColor = Color.FromArgb(148, 163, 184);
    private static readonly Color AccentColor = Color.FromArgb(14, 165, 233);

    private readonly global::ScriptEngine.RoslynScriptService _scriptService = new();
    private readonly RoslynScriptWorkspaceService _workspaceService = new();
    private readonly StringWriter _scriptOutput = new();
    private readonly RoslynScriptEditorControl _editor = new();
    private readonly RichTextBox _outputBox = new();
    private readonly DataGridView _diagnosticsGrid = new();
    private readonly TabControl _resultTabs = new();
    private readonly TabPage _outputTab = new("运行输出");
    private readonly TabPage _problemsTab = new("问题 (0)");
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    private Button _compileButton = null!;
    private Button _runButton = null!;
    private Button _cancelButton = null!;
    private Button _referencesButton = null!;
    private Button _collapseButton = null!;
    private Button _expandButton = null!;
    private Button _formatButton = null!;
    private Button _resetButton = null!;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _completionCancellation;
    private IReadOnlyList<string> _referencePaths = Array.Empty<string>();

    public DemoForm()
    {
        Text = "ScriptEngine · C# 脚本编辑器演示";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 780);
        MinimumSize = new Size(920, 640);
        BackColor = WindowColor;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9F);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        Controls.Add(CreateLayout());
        ConfigureEditor();
        WireCommands();
        ResetDemo();
    }

    private Control CreateLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = WindowColor,
            ColumnCount = 1,
            RowCount = 4,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
        layout.Controls.Add(CreateHeader(), 0, 0);
        layout.Controls.Add(CreateCommandBar(), 0, 1);
        layout.Controls.Add(CreateWorkspace(), 0, 2);
        layout.Controls.Add(CreateStatusBar(), 0, 3);
        return layout;
    }

    private static Control CreateHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = RaisedColor };
        header.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = AccentColor });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(22, 11),
            Text = "C# SCRIPT PLAYGROUND",
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            ForeColor = Color.White
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(24, 40),
            Text = "当前 ScriptEngine.WinForms 控件效果 · Roslyn + ScintillaNET",
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = MutedTextColor
        });
        return header;
    }

    private Control CreateCommandBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(12, 7, 12, 6)
        };
        _compileButton = CreateCommandButton("编译检查", 94, accented: false);
        _runButton = CreateCommandButton("▶ 运行脚本", 108, accented: true);
        _cancelButton = CreateCommandButton("停止", 72, accented: false);
        _cancelButton.Enabled = false;
        _referencesButton = CreateCommandButton("DLL 引用…", 96, accented: false);
        _collapseButton = CreateCommandButton("全部折叠", 84, accented: false);
        _expandButton = CreateCommandButton("全部展开", 84, accented: false);
        _formatButton = CreateCommandButton("格式化", 72, accented: false);
        _resetButton = CreateCommandButton("恢复示例", 88, accented: false);

        bar.Controls.Add(_compileButton);
        bar.Controls.Add(_runButton);
        bar.Controls.Add(_cancelButton);
        bar.Controls.Add(CreateDivider());
        bar.Controls.Add(_referencesButton);
        bar.Controls.Add(_collapseButton);
        bar.Controls.Add(_expandButton);
        bar.Controls.Add(_formatButton);
        bar.Controls.Add(_resetButton);
        bar.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(12, 8, 0, 0),
            Text = "输入 2 个字符自动补全 · Ctrl+Space 手动补全", 
            ForeColor = MutedTextColor
        });
        return bar;
    }

    private static Button CreateCommandButton(string text, int width, bool accented)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(width, 32),
            Margin = new Padding(3, 1, 3, 1),
            FlatStyle = FlatStyle.Flat,
            BackColor = accented ? AccentColor : RaisedColor,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = accented ? AccentColor : BorderColor;
        button.FlatAppearance.MouseOverBackColor = accented
            ? Color.FromArgb(2, 132, 199)
            : Color.FromArgb(55, 55, 60);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(3, 105, 161);
        return button;
    }

    private static Control CreateDivider() => new Panel
    {
        Size = new Size(1, 25),
        Margin = new Padding(9, 5, 9, 0),
        BackColor = BorderColor
    };

    private Control CreateWorkspace()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 490,
            SplitterWidth = 6,
            BackColor = BorderColor,
            Panel1MinSize = 260,
            Panel2MinSize = 130
        };

        var editorFrame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = BorderColor
        };
        editorFrame.Controls.Add(_editor);
        split.Panel1.Controls.Add(editorFrame);

        ConfigureResultTabs();
        split.Panel2.Controls.Add(_resultTabs);
        return split;
    }

    private void ConfigureResultTabs()
    {
        _resultTabs.Dock = DockStyle.Fill;
        _resultTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _resultTabs.ItemSize = new Size(120, 28);
        _resultTabs.SizeMode = TabSizeMode.Fixed;
        _resultTabs.Padding = new Point(14, 4);
        _resultTabs.BackColor = WindowColor;
        _resultTabs.DrawItem += (_, eventArgs) =>
        {
            var selected = eventArgs.Index == _resultTabs.SelectedIndex;
            using var background = new SolidBrush(selected ? RaisedColor : SurfaceColor);
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                _resultTabs.TabPages[eventArgs.Index].Text,
                Font,
                eventArgs.Bounds,
                selected ? Color.White : MutedTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        _outputTab.BackColor = SurfaceColor;
        _outputTab.Padding = new Padding(8);
        _outputBox.Dock = DockStyle.Fill;
        _outputBox.ReadOnly = true;
        _outputBox.BorderStyle = BorderStyle.None;
        _outputBox.BackColor = SurfaceColor;
        _outputBox.ForeColor = TextColor;
        _outputBox.Font = new Font("Consolas", 10.5F);
        _outputBox.DetectUrls = false;
        _outputTab.Controls.Add(_outputBox);

        _problemsTab.BackColor = SurfaceColor;
        _problemsTab.Padding = new Padding(4);
        ConfigureDiagnosticsGrid();
        _problemsTab.Controls.Add(_diagnosticsGrid);

        _resultTabs.TabPages.Add(_outputTab);
        _resultTabs.TabPages.Add(_problemsTab);
    }

    private void ConfigureDiagnosticsGrid()
    {
        _diagnosticsGrid.Dock = DockStyle.Fill;
        _diagnosticsGrid.BackgroundColor = SurfaceColor;
        _diagnosticsGrid.BorderStyle = BorderStyle.None;
        _diagnosticsGrid.GridColor = BorderColor;
        _diagnosticsGrid.ReadOnly = true;
        _diagnosticsGrid.AllowUserToAddRows = false;
        _diagnosticsGrid.AllowUserToDeleteRows = false;
        _diagnosticsGrid.AllowUserToResizeRows = false;
        _diagnosticsGrid.RowHeadersVisible = false;
        _diagnosticsGrid.AutoGenerateColumns = false;
        _diagnosticsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _diagnosticsGrid.MultiSelect = false;
        _diagnosticsGrid.EnableHeadersVisualStyles = false;
        _diagnosticsGrid.ColumnHeadersHeight = 29;
        _diagnosticsGrid.ColumnHeadersDefaultCellStyle.BackColor = RaisedColor;
        _diagnosticsGrid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
        _diagnosticsGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = RaisedColor;
        _diagnosticsGrid.DefaultCellStyle.BackColor = SurfaceColor;
        _diagnosticsGrid.DefaultCellStyle.ForeColor = TextColor;
        _diagnosticsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 64, 89);
        _diagnosticsGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        _diagnosticsGrid.RowTemplate.Height = 27;
        _diagnosticsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "级别", Width = 72 });
        _diagnosticsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "代码", Width = 82 });
        _diagnosticsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "位置", Width = 72 });
        _diagnosticsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "消息", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private Control CreateStatusBar()
    {
        var status = new StatusStrip
        {
            Dock = DockStyle.Fill,
            SizingGrip = false,
            BackColor = Color.FromArgb(9, 105, 151),
            ForeColor = Color.White,
            Padding = new Padding(8, 0, 8, 0)
        };
        _statusLabel.Text = "就绪";
        status.Items.Add(_statusLabel);
        status.Items.Add(new ToolStripStatusLabel("net8.0 · Roslyn C#"));
        return status;
    }

    private void ConfigureEditor()
    {
        _editor.Dock = DockStyle.Fill;
        _editor.BackColor = SurfaceColor;
        _editor.ForeColor = Color.FromArgb(212, 212, 212);
        _editor.Font = new Font("Consolas", 11F);
        _editor.ScriptService = _scriptService;
        _editor.CompletionProvider = _workspaceService;
        ApplyEnvironment();
    }

    private void WireCommands()
    {
        _compileButton.Click += async (_, _) => await CompileAsync();
        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton.Click += (_, _) => CancelOperation();
        _referencesButton.Click += (_, _) => ManageReferences();
        _collapseButton.Click += (_, _) => _editor.CollapseAllFolds();
        _expandButton.Click += (_, _) => _editor.ExpandAllFolds();
        _formatButton.Click += (_, _) =>
        {
            _editor.FormatDocument();
            SetStatus("代码已格式化");
        };
        _resetButton.Click += (_, _) => ResetDemo();
        _editor.CompletionRequested += (_, _) => _ = ShowCompletionsAsync();
        _diagnosticsGrid.CellDoubleClick += (_, _) => NavigateToSelectedDiagnostic();
    }

    private void ApplyEnvironment()
    {
        _editor.ScriptEnvironment = new global::ScriptEngine.RoslynScriptEnvironment
        {
            References = new[] { typeof(DemoHostContext).Assembly },
            ReferencePaths = _referencePaths,
            Output = _scriptOutput,
            ContextItems = new[]
            {
                new global::ScriptEngine.RoslynScriptContextItem("context", "当前脚本执行上下文"),
                new global::ScriptEngine.RoslynScriptContextItem("cancellationToken", "脚本取消令牌")
            }
        };
    }

    private async Task CompileAsync()
    {
        var cancellation = BeginOperation("正在执行 Roslyn 编译…");
        if (cancellation is null) return;
        _resultTabs.SelectedTab = _outputTab;
        _outputBox.Clear();
        AppendOutput("正在编译当前脚本…");

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _editor.CompileProgramAsync(cancellation.Token);
            stopwatch.Stop();
            ShowDiagnostics(result.Diagnostics);

            var errorCount = result.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error);
            var warningCount = result.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning);
            if (result.Success)
            {
                AppendOutput($"✓ 编译成功，用时 {stopwatch.ElapsedMilliseconds} ms；{warningCount} 个警告。");
                AppendOutput($"  Revision: {result.Revision.Substring(0, 12)}…");
                SetStatus("编译成功");
            }
            else
            {
                AppendOutput($"✕ 编译失败：{errorCount} 个错误，{warningCount} 个警告。");
                _resultTabs.SelectedTab = _problemsTab;
                SetStatus("编译失败，请查看问题列表");
            }
        }
        catch (OperationCanceledException)
        {
            AppendOutput("操作已取消。");
            SetStatus("编译已取消");
        }
        catch (Exception exception)
        {
            ShowException("编译发生异常", exception);
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private async Task RunAsync()
    {
        var cancellation = BeginOperation("正在编译并运行脚本…");
        if (cancellation is null) return;
        _resultTabs.SelectedTab = _outputTab;
        _outputBox.Clear();
        _scriptOutput.GetStringBuilder().Clear();
        AppendOutput($"[{DateTime.Now:HH:mm:ss}] 启动脚本 script-editor-demo");

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _scriptService.ExecuteAsync(
                "script-editor-demo",
                _editor.Text,
                new DemoHostContext(),
                _editor.ScriptEnvironment,
                cancellation.Token);
            stopwatch.Stop();
            ShowDiagnostics(result.Diagnostics);

            if (_scriptOutput.GetStringBuilder().Length > 0)
            {
                AppendOutput(string.Empty);
                AppendOutput("--- 脚本标准输出 ---");
                AppendOutput(_scriptOutput.ToString().TrimEnd());
            }

            if (result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            {
                AppendOutput(string.Empty);
                AppendOutput("✕ 脚本未运行，请先修复编译错误。");
                _resultTabs.SelectedTab = _problemsTab;
                SetStatus("运行失败，请查看问题列表");
                return;
            }

            AppendOutput(string.Empty);
            AppendOutput($"返回值: {result.ReturnValue ?? "<null>"}");
            AppendOutput($"✓ 执行完成，用时 {stopwatch.ElapsedMilliseconds} ms。");
            SetStatus("脚本执行成功");
        }
        catch (OperationCanceledException)
        {
            AppendOutput("操作已取消。");
            SetStatus("脚本已取消");
        }
        catch (Exception exception)
        {
            if (_scriptOutput.GetStringBuilder().Length > 0)
                AppendOutput(_scriptOutput.ToString().TrimEnd());
            ShowException("脚本运行异常", exception);
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private CancellationTokenSource? BeginOperation(string status)
    {
        if (_operationCancellation is not null) return null;
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        _compileButton.Enabled = false;
        _runButton.Enabled = false;
        _referencesButton.Enabled = false;
        _resetButton.Enabled = false;
        _cancelButton.Enabled = true;
        SetStatus(status);
        return cancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_operationCancellation, cancellation)) _operationCancellation = null;
        cancellation.Dispose();
        if (IsDisposed || Disposing) return;
        _compileButton.Enabled = true;
        _runButton.Enabled = true;
        _referencesButton.Enabled = true;
        _resetButton.Enabled = true;
        _cancelButton.Enabled = false;
    }

    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
        _cancelButton.Enabled = false;
        SetStatus("正在取消…");
    }

    private async Task ShowCompletionsAsync()
    {
        _completionCancellation?.Cancel();
        _completionCancellation?.Dispose();
        var cancellation = _completionCancellation = new CancellationTokenSource();
        try
        {
            var items = await _editor.GetCompletionsAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested || IsDisposed || Disposing) return;
            _editor.ShowCompletionItems(items);
        }
        catch (OperationCanceledException)
        {
            // 后续补全请求已经取代当前请求。
        }
        catch (Exception exception)
        {
            if (!IsDisposed && !Disposing) SetStatus($"补全失败：{exception.Message}");
        }
    }

    private void ManageReferences()
    {
        using var dialog = new RoslynScriptReferenceManagerDialog(_referencePaths);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _referencePaths = dialog.ReferencePaths.ToArray();
        ApplyEnvironment();
        _referencesButton.Text = _referencePaths.Count == 0
            ? "DLL 引用…"
            : $"DLL 引用 ({_referencePaths.Count})";
        SetStatus($"已配置 {_referencePaths.Count} 个外部 DLL 引用");
    }

    private void ResetDemo()
    {
        _editor.Text = DefaultScript;
        _editor.ShowDiagnostics(Array.Empty<global::ScriptEngine.RoslynScriptDiagnostic>());
        _diagnosticsGrid.Rows.Clear();
        _problemsTab.Text = "问题 (0)";
        _outputBox.Text = "示例已载入。可以直接点击“编译检查”或“运行脚本”。" + Environment.NewLine
            + "尝试删除一个分号，可观察错误列表和编辑器波浪线。";
        _resultTabs.SelectedTab = _outputTab;
        SetStatus("示例已载入");
        _editor.Focus();
    }

    private void ShowDiagnostics(IReadOnlyList<global::ScriptEngine.RoslynScriptDiagnostic> diagnostics)
    {
        _editor.ShowDiagnostics(diagnostics);
        _diagnosticsGrid.Rows.Clear();
        foreach (var diagnostic in diagnostics)
        {
            var severity = diagnostic.Severity == DiagnosticSeverity.Error ? "错误" : "警告";
            var location = diagnostic.Line > 0 ? $"{diagnostic.Line}:{diagnostic.Column}" : "—";
            var rowIndex = _diagnosticsGrid.Rows.Add(severity, diagnostic.Id, location, diagnostic.Message);
            var row = _diagnosticsGrid.Rows[rowIndex];
            row.Tag = diagnostic;
            row.DefaultCellStyle.ForeColor = diagnostic.Severity == DiagnosticSeverity.Error
                ? Color.FromArgb(248, 113, 113)
                : Color.FromArgb(251, 191, 36);
        }
        _problemsTab.Text = $"问题 ({diagnostics.Count})";
    }

    private void NavigateToSelectedDiagnostic()
    {
        if (_diagnosticsGrid.CurrentRow?.Tag is not global::ScriptEngine.RoslynScriptDiagnostic diagnostic
            || diagnostic.Origin != global::ScriptEngine.RoslynScriptDiagnosticOrigin.UserSource
            || diagnostic.Start < 0)
            return;
        _editor.Select(diagnostic.Start, Math.Max(1, diagnostic.Length));
        _editor.Focus();
    }

    private void ShowException(string title, Exception exception)
    {
        AppendOutput(string.Empty);
        AppendOutput($"✕ {title}: {exception.GetType().Name}");
        AppendOutput(exception.Message);
        SetStatus(title);
    }

    private void AppendOutput(string text)
    {
        _outputBox.AppendText(text);
        _outputBox.AppendText(Environment.NewLine);
        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.ScrollToCaret();
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        _operationCancellation?.Cancel();
        _completionCancellation?.Cancel();
        base.OnFormClosing(eventArgs);
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _completionCancellation?.Dispose();
        _operationCancellation?.Dispose();
        _workspaceService.Dispose();
        _scriptService.Dispose();
        _scriptOutput.Dispose();
        base.OnFormClosed(eventArgs);
    }

    private const string DefaultScript = """
using ScriptEngine;
using ScriptEngine.WinForms.Demo;

/// <summary>
/// 演示一个可以由工作流宿主热加载的 C# 程序。
/// </summary>
public sealed class DemoProgram : ICSharpProgram
{
    public async ValueTask<object?> ExecuteAsync(
        CSharpProgramContext context,
        CancellationToken cancellationToken)
    {
        var host = context.GetHostContext<DemoHostContext>();
        var samples = new[] { 12, 18, 23, 31, 42 };

        context.Output.WriteLine($"正在处理批次 {host.BatchName}…");
        await Task.Delay(300, cancellationToken);

        var passed = samples.Count(value => value >= host.PassThreshold);
        var average = samples.Average();
        context.Output.WriteLine($"阈值: {host.PassThreshold}");
        context.Output.WriteLine($"平均值: {average:F2}");

        if (average >= host.PassThreshold)
        {
            context.Output.WriteLine("批次平均值达标。");
        }

        switch (passed)
        {
            case 0:
                context.Output.WriteLine("没有合格样本。");
                break;
            default:
                context.Output.WriteLine($"合格样本数: {passed}");
                break;
        }

        return $"{passed}/{samples.Length} 个样本合格";
    }
}
""";
}
