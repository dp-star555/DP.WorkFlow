using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;
using ModernUI.WinForms;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// 流程设计页：工作台 + 流程库的草稿 / 发布（和 Foundation 的设计页同一套做法，去掉了权限和审计）。
/// 保存只写草稿；“发布…”检查通过后写成正式文件并重新加载流程管理。“调试运行”在单独的运行宿主里跑草稿。
/// </summary>
internal sealed class SampleDesignPage : UserControl
{
    private readonly SampleWorkflowHost _host;
    private readonly WorkflowFlowLibrary _library;
    private readonly WorkflowDraftPublisher _publisher;
    private readonly WorkflowStudioControl _studio = new() { Dock = DockStyle.Fill };
    private readonly WorkflowDocumentWorkspace _workspace;
    private readonly ModernButton _publish = new() { Text = "发布…" };
    private readonly ToolStripMenuItem _open;
    private WorkflowStudioRuntimeBinding? _trial;
    private volatile string _trialLogName = SampleWorkflowHost.TrialRunLogNameOf(null);

    public SampleDesignPage(SampleWorkflowHost host)
    {
        _host = host;
        _library = new WorkflowFlowLibrary(host.FlowDirectory);
        _publisher = new WorkflowDraftPublisher(_library)
        {
            CheckDraft = host.CheckDraft,
            BlockedReason = () => host.IsRunning ? "有流程在运行，先停止再发布。" : host.IsTrialRunning ? "正在调试运行，先停止。" : null,
            CurrentUser = () => "示例用户"
        };
        _workspace = new WorkflowDocumentWorkspace(host.Nodes);
        _workspace.Saved += (_, path) => { if (_library.NameOf(path) is { } name) ShowMessage($"{name} 的草稿已保存。发布后运行页才会用新版本。"); };

        _studio.FileMenu.DropDownItems.Clear();
        _studio.AddFileMenuItem("新建流程", ModernIconKind.FileNew, CreateNew);
        _open = _studio.AddFileMenuItem("打开", ModernIconKind.FolderOpen, () => { });
        _studio.AddFileMenuItem("保存草稿", ModernIconKind.Save, _studio.Save);
        _studio.FileMenu.DropDownItems.Add(new ToolStripSeparator());
        _studio.AddFileMenuItem("放弃草稿", ModernIconKind.Undo, () => _ = DiscardAsync());
        _studio.FileMenu.DropDownOpening += (_, _) => FillOpenMenu();
        _studio.CanvasWorkspace.AddCommand(_publish);
        _publish.Click += async (_, _) => await PublishAsync();
        _studio.ConfirmDiscardChanges = () => MessageBox.Show(FindForm(), "当前流程还没保存，放弃修改？", "流程设计", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        _studio.InteractionError += (_, message) => ShowMessage(message);
        _studio.RunFaulted += (_, message) => ShowMessage($"{message}（详情见运行日志 · {_trialLogName}）");
        _studio.Workspace = _workspace;
        Controls.Add(_studio);

        if (_library.List().FirstOrDefault() is { } first) OpenFlow(first.Name);
        else CreateNew();
    }

    public void ShowMessage(string message) => _studio.CanvasWorkspace.StatusMessage = message;

    /// <summary>打开流程库里的一个流程（参数是正式文件或草稿路径）。</summary>
    public void OpenFile(string path)
    {
        if (_library.NameOf(path) is { } name) OpenFlow(name, reopen: !_workspace.IsDirty);
    }

    public void CreateNew()
    {
        if (_workspace.IsDirty && _studio.ConfirmDiscardChanges?.Invoke() == false) return;
        var name = _library.UniqueName("流程");
        Directory.CreateDirectory(_library.DraftDirectory);
        _workspace.New(name);
        _workspace.SaveAs(_library.DraftPathOf(name));
        EnsureTrialRun();
    }

    /// <summary>
    /// 流程库改了文件（恢复版本、放弃草稿、改名、删除……）后调用：当前打开的流程按磁盘重新打开，设计页显示的就是流程库里的草稿。
    /// 有没保存的修改时不覆盖，只提示。
    /// </summary>
    public void LibraryChanged()
    {
        if (_trial?.State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused)
        {
            ShowMessage("流程库有变动；调试运行结束后从“打开”重新打开流程即可看到。");
            return;
        }
        if (CurrentName() is not { } name || _library.List().All(entry => !string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            // 当前流程被改名或删除了：打开第一个，没有就新建。
            if (_workspace.IsDirty) { ShowMessage("当前流程在流程库里已改名或删除，修改还没保存。"); return; }
            if (_library.List().FirstOrDefault() is { } first) OpenFlow(first.Name, reopen: true);
            else CreateNew();
            return;
        }
        if (_workspace.IsDirty)
        {
            ShowMessage($"{name} 在流程库里有变动（例如恢复了版本），当前还有没保存的修改，没有重新打开。");
            return;
        }
        OpenFlow(name, reopen: true);
    }

    /// <summary>重新判断调试运行能不能用（整线启停后）。</summary>
    public void RefreshRunCommands() => _studio.RefreshRunCommands();

    private string? CurrentName() => _library.NameOf(_workspace.CurrentFilePath);

    private void OpenFlow(string name, bool reopen = false)
    {
        if (!reopen && string.Equals(CurrentName(), name, StringComparison.OrdinalIgnoreCase)) return;
        if (!reopen && _workspace.IsDirty && _studio.ConfirmDiscardChanges?.Invoke() == false) return;
        try
        {
            _workspace.Open(_library.EnsureDraft(name));
            EnsureTrialRun();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            ShowMessage(exception.Message);
        }
    }

    private void FillOpenMenu()
    {
        _open.DropDownItems.Clear();
        var current = CurrentName();
        foreach (var entry in _library.List())
        {
            var name = entry.Name;
            var mark = !entry.IsPublished ? "（未发布）" : entry.HasDraft ? "（有改动）" : string.Empty;
            _open.DropDownItems.Add(new ToolStripMenuItem(name + mark, null, (_, _) => OpenFlow(name))
            {
                Checked = string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
            });
        }
        if (_open.DropDownItems.Count == 0) _open.DropDownItems.Add(new ToolStripMenuItem("流程库是空的") { Enabled = false });
    }

    // 调试运行：一个运行宿主跟着设计页走；每次运行换一份新的上下文。
    private void EnsureTrialRun()
    {
        if (_trial is not null || _workspace.Navigator is not { } navigator) return;
        _trial = new WorkflowStudioRuntimeBinding(_host.CreateTrialHost(() => _trialLogName), navigator)
        {
            AutoConfigureBeforeRun = true,
            RunContext = new WorkflowContext(_host.Services),
            RunBlockedReason = () => _host.IsRunning ? "运行页有流程在跑，先停止再调试运行。" : null
        };
        _trial.RunConfiguring += _ =>
        {
            _trial.RunContext = new WorkflowContext(_host.Services);
            // 运行日志按流程分开记；名字在界面线程取好，记录线程只读这个字段。
            _trialLogName = SampleWorkflowHost.TrialRunLogNameOf(CurrentName());
        };
        _studio.RuntimeBinding = _trial;
    }

    private async Task PublishAsync()
    {
        if (CurrentName() is not { } name) return;
        if (_workspace.IsDirty)
        {
            ShowMessage("先保存草稿（Ctrl+S），再发布。");
            return;
        }
        try
        {
            if (await WorkflowPublishDialog.RunAsync(FindForm(), _publisher, name) is not { } version) return;
            OpenFlow(name, reopen: true);
            _host.Load();
            ShowMessage($"{name} 已发布：{WorkflowPublishDialog.Describe(version)}。运行页已重新加载。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            ShowMessage(exception.Message);
        }
    }

    private async Task DiscardAsync()
    {
        if (CurrentName() is not { } name) return;
        var published = File.Exists(_library.PathOf(name));
        var question = published ? $"放弃 {name} 的草稿，回到已发布的版本？" : $"{name} 还没发布过，放弃会删掉它。继续？";
        if (MessageBox.Show(FindForm(), question, "放弃草稿", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            await _publisher.DiscardAsync(name);
            if (published) OpenFlow(name, reopen: true);
            else CreateNew();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowMessage(exception.Message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _studio.RuntimeBinding = null;
            _trial?.Dispose();
            _studio.Workspace = null;
            _workspace.Dispose();
        }
        base.Dispose(disposing);
    }
}
