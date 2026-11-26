using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;
using ModernUI.WinForms;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// 示例主窗口：上面一条工具条（标题、主题、全部启动 / 停止），左边页面导航，右边当前页面。
/// 运行页和节点提示页跟着流程管理走：发布后流程管理重建，这两页也重建。
/// </summary>
internal sealed class SampleForm : Form
{
    private readonly SampleWorkflowHost _host;
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly WorkflowItemList _nav = new();
    private readonly ModernSelect _theme = new();
    private readonly ModernButton _startAll = new() { Text = "全部启动", ButtonType = ModernButtonType.Primary, Icon = ModernIconKind.Play };
    private readonly ModernButton _stopAll = new() { Text = "全部停止", Icon = ModernIconKind.Stop };
    private readonly Label _summary = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private readonly SampleDesignPage _design;
    private readonly WorkflowRunLogPage _log;
    private readonly WorkflowFlowLibraryPage _library;
    private readonly Dictionary<string, Control> _pages = new();
    private WorkflowRunPage? _run;
    private WorkflowNoticeCenterPage? _notices;
    private WorkflowFaultWorkbenchModel? _workbench;

    public SampleForm(SampleWorkflowHost host)
    {
        _host = host;
        // 标题带上实际加载的 ModernUI 版本，换包后一眼能看出是不是还在用旧的 dll。
        var modernUi = typeof(ModernDataGridView).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion?.Split('+')[0];
        Text = $"Workflow 页面示例 · ModernUI {modernUi}";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1440, 900);
        MinimumSize = new Size(1100, 700);
        Padding = new Padding(10);

        _design = new SampleDesignPage(host) { Dock = DockStyle.Fill };
        _log = new WorkflowRunLogPage(host.RunLog) { History = host.LogFiles };
        _library = new WorkflowFlowLibraryPage(new WorkflowFlowLibrary(host.FlowDirectory))
        {
            StatusOf = entry => entry.FilePath is null ? null
                : host.Center?.GetFlow(entry.Name) is { } flow ? (flow.IsRunning ? "运行中" : "已加载") : "未加载",
            CurrentUser = () => "示例用户"
        };
        _library.Publisher.CheckDraft = host.CheckDraft;
        _library.Publisher.BlockedReason = () => host.IsRunning ? "有流程在运行，先停止再发布。" : null;
        _library.OpenRequested += (_, file) => { _design.OpenFile(file); Navigate("design"); };
        _library.NewRequested += (_, _) => { _design.CreateNew(); Navigate("design"); };
        _library.FilesChanged += (_, _) => { if (!host.IsRunning) host.Load(); };
        _pages["design"] = _design;
        _pages["log"] = _log;
        _pages["library"] = _library;

        Controls.Add(_content);
        Controls.Add(new WorkflowBlock { Dock = DockStyle.Left, LogicalExtent = 10 });
        Controls.Add(BuildNavigation());
        Controls.Add(new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 10 });
        Controls.Add(BuildHeader());

        host.CenterChanged += (_, _) => RebuildCenterPages();
        RebuildCenterPages();
        _clock.Tick += (_, _) => RefreshShell();
        _clock.Start();
        WorkflowWinFormsTheme.Follow(this, () => BackColor = WorkflowWinFormsTheme.Modern.Background);
        Navigate("run");
        if (host.LoadIssues.Count > 0) _design.ShowMessage("有流程读不出：" + string.Join("；", host.LoadIssues));
    }

    private Control BuildHeader()
    {
        var bar = new WorkflowToolbar();
        var title = new Label { Text = "Workflow 页面示例" };
        title.Font = new Font(Font.FontFamily, Font.Size * 1.15f, FontStyle.Bold);
        bar.Add(title);
        bar.AddSeparator();
        bar.Add(new Label { Text = "主题" });
        foreach (var palette in new[] { WorkflowThemePalette.Blue, WorkflowThemePalette.Night, WorkflowThemePalette.Mint, WorkflowThemePalette.Warm, WorkflowThemePalette.Orange })
            _theme.Items.Add(new PaletteChoice(palette));
        _theme.SelectedIndex = 0;
        _theme.SelectedIndexChanged += (_, _) => { if (_theme.SelectedItem is PaletteChoice choice) WorkflowWinFormsTheme.Use(choice.Palette); };
        bar.Add(_theme, 150);
        bar.AddSeparator();
        bar.Add(_summary, 260);
        bar.AddRight(_startAll);
        bar.AddRight(_stopAll);
        _startAll.Click += (_, _) => Guard(() => { _host.StartAll(); return Task.CompletedTask; });
        _stopAll.Click += (_, _) => Guard(_host.StopAllAsync);
        return bar;
    }

    private Control BuildNavigation()
    {
        var card = new WorkflowCard { Dock = DockStyle.Left, LogicalExtent = 210 };
        card.Controls.Add(_nav);
        card.Controls.Add(new WorkflowCardHeader("页面"));
        _nav.Show(new[]
        {
            new WorkflowListItem("design", "流程设计", "画布、工具箱、节点属性"),
            new WorkflowListItem("run", "运行", "启停、画布、故障处理"),
            new WorkflowListItem("notices", "节点提示", "按节点汇总的提示"),
            new WorkflowListItem("log", "运行日志", "每一步的记录"),
            new WorkflowListItem("library", "流程库", "草稿、发布、版本")
        });
        _nav.SelectionChanged += (_, _) => { if (_nav.SelectedKey is string key) Navigate(key); };
        return card;
    }

    private void Navigate(string key)
    {
        _nav.SelectedKey = key;
        if (!_pages.TryGetValue(key, out var page)) return;
        if (page.Parent != _content)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
        }
        foreach (Control other in _content.Controls)
            other.Visible = ReferenceEquals(other, page);
    }

    // 运行页、节点提示页绑在流程管理上：流程管理换了就重建，保留当前看的流程。
    private void RebuildCenterPages()
    {
        if (_host.Center is not { } center) return;
        var shown = _run?.FlowName;
        var wasVisible = _nav.SelectedKey as string;
        Replace("run", ref _run, null);
        Replace("notices", ref _notices, null);
        _workbench?.Dispose();
        _workbench = new WorkflowFaultWorkbenchModel(center, _host.Options);
        _run = new WorkflowRunPage(center, _host.Nodes, flow => _host.LoadedDocumentOf(flow.Name), _workbench);
        _notices = new WorkflowNoticeCenterPage(center, flow => _host.LoadedDocumentOf(flow.Name));
        _pages["run"] = _run;
        _pages["notices"] = _notices;
        if (shown is not null) _run.ShowFlow(shown);
        if (wasVisible is "run" or "notices") Navigate(wasVisible);
    }

    private void Replace<T>(string key, ref T? page, T? next) where T : Control
    {
        if (page is not null)
        {
            _content.Controls.Remove(page);
            page.Dispose();
        }
        page = next;
        _pages.Remove(key);
    }

    private void RefreshShell()
    {
        var flows = _host.Center?.Flows ?? Array.Empty<WorkflowRunCenterFlow>();
        var running = flows.Count(flow => flow.IsRunning);
        _summary.Text = $"共 {flows.Count} 条流程，{running} 条在运行";
        _startAll.Enabled = running < flows.Count && !_host.IsTrialRunning;
        _stopAll.Enabled = running > 0;
        _library.CanEdit = !_host.IsRunning && !_host.IsTrialRunning;
        _design.RefreshRunCommands();
    }

    private async void Guard(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        RefreshShell();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!_host.IsRunning) return;
        e.Cancel = true;
        await _host.StopAllAsync();
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Dispose();
            _workbench?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record PaletteChoice(WorkflowThemePalette Palette)
    {
        public override string ToString() => Palette.Name;
    }
}
