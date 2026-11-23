using System.Globalization;
using DP.WorkFlow.Persistence.Json;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 流程库页：列出流程目录里的流程（名字、状态、版本、最近修改、修改人、节点和连线数），可以搜索，
/// 新建、复制、改名、删除、导入、导出、打开设计，查看历史版本、和当前比较、恢复。
/// </summary>
/// <remarks>
/// 状态由宿主给（<see cref="StatusOf"/>），例如“生产中”“已加载”“有诊断”。
/// 会改文件的操作在 <see cref="CanEdit"/> 为假时灰掉；改完发 <see cref="FilesChanged"/>，宿主据此重新加载到运行。
/// 列文件是 I/O：只在切到本页、操作之后、点“刷新”时列，不在每秒刷新里列。
/// </remarks>
public sealed class WorkflowFlowLibraryPage : WorkflowPage
{
    private readonly WorkflowFlowLibrary _library;
    private readonly TextBox _search = new() { Width = 260, Margin = new Padding(0, 3, 12, 0) };
    private readonly CheckBox _showDeleted = new() { Text = "显示已删除", AutoSize = true, Margin = new Padding(0, 5, 12, 0) };
    private readonly ModernDataGridView _table = Table("名称", "状态", "版本", "最近修改", "修改人", "节点 / 连线");
    private readonly ModernButton _open = new() { Text = "打开设计", AutoSize = true, ButtonType = ModernButtonType.Primary };
    private readonly ModernButton _new = new() { Text = "新建", AutoSize = true };
    private readonly ModernButton _copy = new() { Text = "复制", AutoSize = true };
    private readonly ModernButton _rename = new() { Text = "改名", AutoSize = true };
    private readonly ModernButton _delete = new() { Text = "删除", AutoSize = true, ButtonType = ModernButtonType.Danger };
    private readonly ModernButton _import = new() { Text = "导入…", AutoSize = true };
    private readonly ModernButton _export = new() { Text = "导出…", AutoSize = true };
    private readonly ModernButton _history = new() { Text = "历史版本…", AutoSize = true };
    private readonly ModernButton _refresh = new() { Text = "刷新", AutoSize = true };
    private readonly Label _summary = Label();
    private readonly Label _message = Label();
    private IReadOnlyList<WorkflowLibraryEntry> _entries = Array.Empty<WorkflowLibraryEntry>();
    private string _statusSignature = string.Empty;
    private bool _canEdit = true;

    /// <summary>创建流程库页。</summary>
    /// <param name="library">流程库。</param>
    public WorkflowFlowLibraryPage(WorkflowFlowLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _table.Columns[0].FillWeight = 160;
        _table.Columns[1].FillWeight = 80;
        _table.Columns[2].FillWeight = 50;
        _table.Columns[3].FillWeight = 110;

        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
        filter.Controls.Add(new Label { Text = "搜索", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        filter.Controls.Add(_search);
        filter.Controls.Add(_showDeleted);
        filter.Controls.Add(_refresh);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        buttons.Controls.AddRange(new Control[] { _open, _new, _copy, _rename, _delete, _import, _export, _history });
        _summary.Dock = DockStyle.Bottom;
        _summary.Height = 24;
        var body = new Panel();
        body.Controls.Add(_table);
        body.Controls.Add(buttons);
        body.Controls.Add(filter);
        body.Controls.Add(_summary);
        _message.Dock = DockStyle.Bottom;
        _message.Height = 24;
        Controls.Add(Card("流程库", body));
        Controls.Add(_message);

        _search.TextChanged += (_, _) => ShowRows();
        _showDeleted.CheckedChanged += (_, _) => Reload();
        _refresh.Click += (_, _) => Reload();
        _table.SelectionChanged += (_, _) => UpdateCommands();
        _table.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        _open.Click += (_, _) => OpenSelected();
        _new.Click += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        _copy.Click += (_, _) => Edit(entry => Done($"已复制为 {_library.Duplicate(entry.Name)}"));
        _rename.Click += (_, _) => Edit(entry =>
        {
            if (Ask("改名", "新名字（也是流程名）：", entry.Name) is not { } name || name == entry.Name) return;
            _library.Rename(entry.Name, name);
            Done($"{entry.Name} 已改名为 {name}", name);
        });
        _delete.Click += (_, _) => Edit(entry =>
        {
            if (MessageBox.Show(FindForm(), $"删除流程 {entry.Name}？历史版本会保留，之后可以从历史版本恢复。", "删除流程",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            _library.Delete(entry.Name, CurrentUser?.Invoke());
            Done($"已删除 {entry.Name}");
        });
        _import.Click += (_, _) => Import();
        _export.Click += (_, _) => Export();
        _history.Click += (_, _) => { if (Selected is { } entry) ShowHistory(entry); };
    }

    /// <summary>流程的状态文字（宿主给，例如“生产中”）；为空时不显示状态。必须快速、无 I/O。</summary>
    public Func<WorkflowLibraryEntry, string?>? StatusOf { get; set; }

    /// <summary>当前操作人（记进版本）；为空时不记。</summary>
    public Func<string?>? CurrentUser { get; set; }

    /// <summary>是否允许改文件（新建、复制、改名、删除、导入、恢复）；宿主按权限和整线状态设置。</summary>
    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (_canEdit == value) return;
            _canEdit = value;
            UpdateCommands();
        }
    }

    /// <summary>请求在设计页打开一个流程文件（界面线程）。</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>请求新建流程（界面线程）；宿主在设计页里新建，因为新流程要有入口节点。</summary>
    public event EventHandler? NewRequested;

    /// <summary>流程文件被本页改了（复制、改名、删除、导入、恢复）之后发生。</summary>
    public event EventHandler? FilesChanged;

    /// <summary>重新列文件。</summary>
    public void Reload()
    {
        try { _entries = _library.List(_showDeleted.Checked); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _entries = Array.Empty<WorkflowLibraryEntry>();
            ShowMessage(exception.Message);
        }
        _statusSignature = StatusSignature();
        ShowRows();
    }

    /// <inheritdoc />
    public override void ShowMessage(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => ShowMessage(message))); return; }
        _message.Text = message;
    }

    /// <inheritdoc />
    protected override void OnRefresh(bool canOperate, bool force)
    {
        if (force)
        {
            Reload();
            return;
        }
        // 状态（运行中、已加载）每秒对一次，变了才重画；不重新列文件。
        var signature = StatusSignature();
        if (signature != _statusSignature)
        {
            _statusSignature = signature;
            ShowRows();
        }
        UpdateCommands();
    }

    private string StatusSignature() => StatusOf is { } status ? string.Join("|", _entries.Select(entry => status(entry))) : string.Empty;

    private WorkflowLibraryEntry? Selected =>
        _table.SelectedRows.Count == 1 ? _table.SelectedRows[0].Tag as WorkflowLibraryEntry : null;

    private void ShowRows(string? select = null)
    {
        var selected = select ?? Selected?.Name;
        var keyword = _search.Text.Trim();
        var shown = _entries.Where(entry => keyword.Length == 0
            || entry.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
            || (entry.FlowName?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (entry.ModifiedBy?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0).ToArray();
        var theme = WorkflowWinFormsTheme.Modern;
        _table.Rows.Clear();
        foreach (var entry in shown)
        {
            var status = entry.IsDeleted ? "已删除" : entry.Error is not null ? "读不出" : StatusOf?.Invoke(entry) ?? string.Empty;
            var index = _table.Rows.Add(entry.Name, status,
                entry.Version == 0 ? "—" : "v" + entry.Version.ToString(CultureInfo.InvariantCulture),
                entry.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                entry.ModifiedBy ?? string.Empty,
                $"{entry.NodeCount} / {entry.ConnectionCount}");
            var row = _table.Rows[index];
            row.Tag = entry;
            if (entry.Error is not null) row.Cells[1].ToolTipText = entry.Error;
            row.Cells[1].Style.ForeColor = entry.IsDeleted ? theme.TextDisabled
                : entry.Error is not null || status.Contains("诊断") ? theme.Error
                : status.Contains("运行") || status.Contains("生产") ? theme.Success
                : theme.TextSecondary;
        }
        RestoreSelection(_table, selected, row => (row.Tag as WorkflowLibraryEntry)?.Name);
        _summary.Text = keyword.Length == 0
            ? $"共 {_entries.Count(entry => !entry.IsDeleted)} 个流程"
            : $"找到 {shown.Length} 个（共 {_entries.Count} 个）";
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        var entry = Selected;
        var live = entry is { IsDeleted: false };
        _open.Enabled = live && entry!.Error is null;
        _new.Enabled = _canEdit;
        _copy.Enabled = _canEdit && live;
        _rename.Enabled = _canEdit && live;
        _delete.Enabled = _canEdit && live;
        _import.Enabled = _canEdit;
        _export.Enabled = live;
        _history.Enabled = entry is not null;
    }

    private void OpenSelected()
    {
        if (Selected is { IsDeleted: false, Error: null, FilePath: { } path }) OpenRequested?.Invoke(this, path);
    }

    // 改文件的操作：出错显示消息，不让异常冒出去。
    private void Edit(Action<WorkflowLibraryEntry> action)
    {
        if (!_canEdit || Selected is not { IsDeleted: false } entry) return;
        Guarded(() => action(entry));
    }

    private void Guarded(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or ArgumentException or InvalidDataException or System.Text.Json.JsonException)
        {
            ShowMessage(exception.Message);
            Reload();
        }
    }

    private void Done(string message, string? select = null)
    {
        ShowMessage(message);
        Reload();
        if (select is not null) ShowRows(select);
        FilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Import()
    {
        if (!_canEdit) return;
        using var dialog = new OpenFileDialog { Filter = "流程文件 (*.json)|*.json", CheckFileExists = true, Multiselect = true };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        Guarded(() =>
        {
            var names = new List<string>();
            foreach (var file in dialog.FileNames)
            {
                var name = _library.Import(file);
                _library.RecordVersion(name, CurrentUser?.Invoke(), "导入自 " + Path.GetFileName(file));
                names.Add(name);
            }
            Done("已导入 " + string.Join("、", names), names.LastOrDefault());
        });
    }

    private void Export()
    {
        if (Selected is not { IsDeleted: false } entry) return;
        using var dialog = new SaveFileDialog { Filter = "流程文件 (*.json)|*.json", FileName = entry.Name + ".json", OverwritePrompt = true };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        Guarded(() =>
        {
            _library.Export(entry.Name, dialog.FileName);
            ShowMessage($"已导出到 {dialog.FileName}");
        });
    }

    private void ShowHistory(WorkflowLibraryEntry entry)
    {
        using var dialog = new WorkflowFlowHistoryDialog(_library, entry, _canEdit, CurrentUser);
        WorkflowWinFormsTheme.Follow(dialog);
        dialog.ShowDialog(FindForm());
        if (dialog.Restored is { } version)
            Done($"{entry.Name} 已恢复到 v{version}", entry.Name);
    }

    private string? Ask(string title, string prompt, string value)
    {
        using var dialog = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(380, 112), Font = Font
        };
        var hint = new Label { Text = prompt, AutoSize = false, Bounds = new Rectangle(12, 10, 356, 22) };
        var input = new TextBox { Text = value, MaxLength = 100, Bounds = new Rectangle(12, 36, 356, 24) };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Bounds = new Rectangle(206, 74, 76, 28) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(292, 74, 76, 28) };
        dialog.Controls.AddRange(new Control[] { hint, input, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        WorkflowWinFormsTheme.Follow(dialog);
        return dialog.ShowDialog(FindForm()) == DialogResult.OK && input.Text.Trim().Length > 0 ? input.Text.Trim() : null;
    }
}

/// <summary>历史版本：列出版本，和当前文件（或上一版）比较，恢复到所选版本。</summary>
internal sealed class WorkflowFlowHistoryDialog : Form
{
    private readonly WorkflowFlowLibrary _library;
    private readonly WorkflowLibraryEntry _entry;
    private readonly Func<string?>? _user;
    private readonly ModernDataGridView _versions = WorkflowPage.Table("版本", "时间", "保存人", "说明");
    private readonly TextBox _diff = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    private readonly ModernButton _compare = new() { Text = "和当前比较", AutoSize = true };
    private readonly ModernButton _comparePrevious = new() { Text = "和上一版比较", AutoSize = true };
    private readonly ModernButton _restore = new() { Text = "恢复到这一版", AutoSize = true, ButtonType = ModernButtonType.Primary };

    public WorkflowFlowHistoryDialog(WorkflowFlowLibrary library, WorkflowLibraryEntry entry, bool canEdit, Func<string?>? user)
    {
        _library = library;
        _entry = entry;
        _user = user;
        Text = $"历史版本 · {entry.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 560);
        MinimumSize = new Size(560, 360);
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 6) };
        buttons.Controls.AddRange(new Control[] { _compare, _comparePrevious, _restore });
        var split = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, InitialPanel2Size = 220 };
        split.Panel1.Controls.Add(_versions);
        var lower = new Panel { Dock = DockStyle.Fill };
        lower.Controls.Add(_diff);
        lower.Controls.Add(buttons);
        split.Panel2.Controls.Add(lower);
        Padding = new Padding(8);
        Controls.Add(split);

        foreach (var version in library.GetVersions(entry.Name).Reverse())
        {
            var index = _versions.Rows.Add("v" + version.Version.ToString(CultureInfo.InvariantCulture),
                version.SavedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                version.SavedBy ?? string.Empty, version.Note ?? string.Empty);
            _versions.Rows[index].Tag = version;
        }
        _compare.Enabled = !entry.IsDeleted;
        _restore.Enabled = canEdit;
        _versions.SelectionChanged += (_, _) => _comparePrevious.Enabled = Previous(Selected) is not null;
        _compare.Click += (_, _) => { if (Selected is { } version) ShowDiff($"v{version.Version} → 当前", version.Version, 0); };
        _comparePrevious.Click += (_, _) => { if (Selected is { } version && Previous(version) is { } previous) ShowDiff($"v{previous.Version} → v{version.Version}", previous.Version, version.Version); };
        _restore.Click += (_, _) => Restore();
        _diff.Text = _versions.Rows.Count == 0 ? "还没有历史版本。每次在设计页保存会记一版。" : "选一个版本，再点“和当前比较”或“和上一版比较”。";
    }

    /// <summary>恢复到的版本；没恢复时为空。</summary>
    public int? Restored { get; private set; }

    private WorkflowLibraryVersion? Selected => _versions.SelectedRows.Count == 1 ? _versions.SelectedRows[0].Tag as WorkflowLibraryVersion : null;

    private WorkflowLibraryVersion? Previous(WorkflowLibraryVersion? version) =>
        version is null ? null : _library.GetVersions(_entry.Name).LastOrDefault(item => item.Version < version.Version);

    private void ShowDiff(string title, int older, int newer)
    {
        try
        {
            var diff = WorkflowFlowLibrary.Compare(_library.ReadContent(_entry.Name, older), _library.ReadContent(_entry.Name, newer));
            _diff.Text = Describe(title, diff);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _diff.Text = exception.Message;
        }
    }

    internal static string Describe(string title, WorkflowFlowComparison diff)
    {
        if (diff.IsSame) return title + "：没有差别。";
        var lines = new List<string> { title + "：" };
        void Add(string label, IReadOnlyList<string> items)
        {
            if (items.Count > 0) lines.Add($"{label}（{items.Count}）：{string.Join("、", items)}");
        }
        Add("新增节点", diff.AddedNodes);
        Add("删掉节点", diff.RemovedNodes);
        Add("参数变了", diff.ChangedNodes);
        Add("只挪了位置", diff.MovedNodes);
        Add("新增连线", diff.AddedConnections);
        Add("删掉连线", diff.RemovedConnections);
        foreach (var other in diff.OtherChanges) lines.Add(other);
        return string.Join(Environment.NewLine, lines);
    }

    private void Restore()
    {
        if (Selected is not { } version) return;
        if (MessageBox.Show(this, $"把 {_entry.Name} 恢复到 v{version.Version}？当前内容会先记成一版，之后还能再恢复回来。", "恢复版本",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            _library.Restore(_entry.Name, version.Version, _user?.Invoke());
            Restored = version.Version;
            Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _diff.Text = exception.Message;
        }
    }
}
