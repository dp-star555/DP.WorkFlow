using System.Globalization;
using DP.WorkFlow.Persistence.Json;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 流程库页（按设计稿“流程库”画板：顶部工具条，左边流程表，右边详情和版本）：列出流程目录里的流程（名字、状态、草稿、已发布版本、最近修改、发布人、节点和连线数），可以搜索，
/// 新建、复制、改名、删除、导入、导出、打开设计，查看修改、发布、放弃草稿，查看历史版本、比较、恢复。
/// </summary>
/// <remarks>
/// 状态由宿主给（<see cref="StatusOf"/>），例如“生产中”“已加载”“有诊断”。
/// 设计页编辑的是草稿；新建、复制、导入、恢复也都落成草稿。发布才替换正式文件并记一版（见 <see cref="WorkflowFlowLibrary"/>）。
/// 会改文件的操作在 <see cref="CanEdit"/> 为假时灰掉；改完发 <see cref="FilesChanged"/>，宿主据此重新加载到运行。
/// 列文件是 I/O：只在切到本页、操作之后、点“刷新”时列，不在每秒刷新里列。
/// </remarks>
public sealed class WorkflowFlowLibraryPage : WorkflowPage
{
    private readonly WorkflowFlowLibrary _library;
    private readonly ModernInput _search = new() { PlaceholderText = "搜索名称、流程名或修改人", Width = 260 };
    private readonly CheckBox _showDeleted = new() { Text = "显示已删除" };
    private readonly ModernSegmentedControl _filter = new();
    private readonly Label _directory = new() { AutoSize = true, UseMnemonic = false };
    private readonly ModernDataGridView _table = Table("名称", "状态", "草稿", "版本", "最近修改", "修改人", "节点 / 连线");
    private readonly WorkflowFactList _detail = new();
    private readonly WorkflowCard _tableCard;
    private readonly WorkflowCard _detailCard;
    private readonly WorkflowStatusLine _status = new();
    private readonly ModernButton _open = new() { Text = "打开设计", ButtonType = ModernButtonType.Primary };
    private readonly ModernButton _new = new() { Text = "新建" };
    private readonly ModernButton _copy = new() { Text = "复制" };
    private readonly ModernButton _rename = new() { Text = "改名" };
    private readonly ModernButton _delete = new() { Text = "删除", ButtonType = ModernButtonType.Danger };
    private readonly ModernButton _import = new() { Text = "导入…" };
    private readonly ModernButton _export = new() { Text = "导出…" };
    private readonly ModernButton _history = new() { Text = "历史版本…" };
    private readonly ModernButton _changes = new() { Text = "查看修改" };
    private readonly ModernButton _publish = new() { Text = "发布…", ButtonType = ModernButtonType.Primary };
    private readonly ModernButton _discard = new() { Text = "放弃草稿" };
    private readonly ModernButton _refresh = new() { Text = "刷新" };
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
        _table.Columns[2].FillWeight = 70;
        _table.Columns[3].FillWeight = 50;
        _table.Columns[4].FillWeight = 110;

        _table.Columns[5].FillWeight = 70;
        _table.Columns[6].FillWeight = 70;

        // 顶部工具条：新建、导入、搜索、筛选；右边是流程目录和刷新。
        int S(int value) => LogicalToDeviceUnits(value);
        foreach (var item in new[] { "全部", "已发布", "草稿", "有诊断" })
            _filter.Items.Add(new ModernSegmentedItem(item));
        _filter.Width = S(320);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
        foreach (var button in new[] { _new, _import })
        {
            WorkflowCommandSizing.Fit(button);
            tools.Controls.Add(button);
        }
        foreach (var control in new Control[] { _search, _filter, _showDeleted })
        {
            control.Margin = new Padding(S(10), S(9), 0, 0);
            tools.Controls.Add(control);
        }
        _showDeleted.Margin = new Padding(S(10), S(13), 0, 0);
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
        WorkflowCommandSizing.Fit(_refresh);
        _refresh.ButtonType = ModernButtonType.Text;
        right.Controls.Add(_refresh);
        _directory.Margin = new Padding(0, S(14), S(4), 0);
        right.Controls.Add(_directory);
        var bar = new WorkflowCard { Dock = DockStyle.Top, LogicalExtent = 52 };
        bar.Controls.Add(tools);
        bar.Controls.Add(right);
        tools.Padding = new Padding(S(6), S(3), 0, 0);

        // 右边详情：概况、版本，下面两排操作按钮。
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Padding = new Padding(S(10), S(4), S(10), S(8)) };
        foreach (var button in new[] { _open, _changes, _publish, _discard, _copy, _rename, _export, _history, _delete })
        {
            WorkflowCommandSizing.Fit(button);
            actions.Controls.Add(button);
        }
        var detailBody = new Panel { Dock = DockStyle.Fill };
        detailBody.Controls.Add(_detail);
        detailBody.Controls.Add(actions);
        _tableCard = Section("流程", null, _table);
        _detailCard = Section("详情", null, detailBody, DockStyle.Right, 360);
        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(_tableCard);
        middle.Controls.Add(Gap(DockStyle.Right));
        middle.Controls.Add(_detailCard);

        Controls.Add(middle);
        Controls.Add(Gap(DockStyle.Top));
        Controls.Add(bar);
        Controls.Add(_status);
        _filter.SelectedIndexChanged += (_, _) => ShowRows();

        _search.TextChanged += (_, _) => ShowRows();
        _showDeleted.CheckedChanged += (_, _) => Reload();
        _refresh.Click += (_, _) => Reload();
        _table.SelectionChanged += (_, _) => UpdateCommands();
        _table.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        _open.Click += (_, _) => OpenSelected();
        _new.Click += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        _copy.Click += (_, _) => Edit(entry => Done($"已复制为 {_library.Duplicate(entry.Name)}（草稿，发布后才会加载）"));
        _rename.Click += (_, _) => Edit(entry =>
        {
            if (Ask("改名", "新名字（也是流程名）：", entry.Name) is not { } name || name == entry.Name) return;
            _library.Rename(entry.Name, name);
            Done($"{entry.Name} 已改名为 {name}", name);
        });
        _delete.Click += (_, _) => Edit(entry =>
        {
            if (MessageBox.Show(FindForm(), $"删除流程 {entry.Name}（连同草稿）？已发布的版本历史会保留，之后可以从历史版本恢复。", "删除流程",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            _library.Delete(entry.Name, CurrentUser?.Invoke());
            Done($"已删除 {entry.Name}");
        });
        _import.Click += (_, _) => Import();
        _export.Click += (_, _) => Export();
        _history.Click += (_, _) => { if (Selected is { } entry) ShowHistory(entry); };
        _changes.Click += (_, _) => ShowChanges();
        _publish.Click += async (_, _) => await PublishAsync().ConfigureAwait(true);
        _discard.Click += async (_, _) => await DiscardAsync().ConfigureAwait(true);
        Publisher = new WorkflowDraftPublisher(library);
    }

    /// <summary>发布 / 放弃草稿用的发布器：宿主在上面设置检查、准入和操作人。</summary>
    public WorkflowDraftPublisher Publisher { get; }

    /// <summary>流程的状态文字（宿主给，例如“生产中”）；为空时不显示状态。必须快速、无 I/O。</summary>
    public Func<WorkflowLibraryEntry, string?>? StatusOf { get; set; }

    /// <summary>当前操作人（记进版本）；为空时不记。和 <see cref="Publisher"/> 的操作人是同一个。</summary>
    public Func<string?>? CurrentUser
    {
        get => Publisher.CurrentUser;
        set => Publisher.CurrentUser = value;
    }

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

    /// <summary>请求在设计页打开一个流程（界面线程）；参数是已发布的文件路径，从没发布过的流程是草稿路径。</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>请求新建流程（界面线程）；宿主在设计页里新建，因为新流程要有入口节点。</summary>
    public event EventHandler? NewRequested;

    /// <summary>流程文件被本页改了（复制、改名、删除、导入、恢复、发布、放弃草稿）之后发生。</summary>
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
        _status.Message = message;
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
        var filter = _filter.SelectedIndex;
        var shown = _entries.Where(entry => (filter switch
            {
                1 => entry.IsPublished && !entry.IsDeleted,
                2 => !entry.IsDeleted && (entry.HasDraft || !entry.IsPublished),
                3 => entry.Error is not null || (StatusOf?.Invoke(entry)?.Contains("诊断") ?? false),
                _ => true
            }) && (keyword.Length == 0
            || entry.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
            || (entry.FlowName?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (entry.ModifiedBy?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)).ToArray();
        var theme = WorkflowWinFormsTheme.Modern;
        _table.Rows.Clear();
        foreach (var entry in shown)
        {
            var status = entry.IsDeleted ? "已删除" : entry.Error is not null ? "读不出" : StatusOf?.Invoke(entry) ?? string.Empty;
            var draft = entry.IsDeleted ? string.Empty : !entry.IsPublished ? "未发布" : entry.HasDraft ? "有修改" : string.Empty;
            var index = _table.Rows.Add(entry.Name, status, draft,
                entry.Version == 0 ? "—" : "v" + entry.Version.ToString(CultureInfo.InvariantCulture),
                entry.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                entry.ModifiedBy ?? string.Empty,
                $"{entry.NodeCount} / {entry.ConnectionCount}");
            var row = _table.Rows[index];
            row.Tag = entry;
            if (entry.Error is not null) row.Cells[1].ToolTipText = entry.Error;
            row.Cells[2].Style.ForeColor = theme.Warning;
            if (entry.DraftModifiedUtc is { } draftTime)
                row.Cells[2].ToolTipText = "草稿保存于 " + draftTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            row.Cells[1].Style.ForeColor = entry.IsDeleted ? theme.TextDisabled
                : entry.Error is not null || status.Contains("诊断") ? theme.Error
                : status.Contains("运行") || status.Contains("生产") ? theme.Success
                : theme.TextSecondary;
        }
        RestoreSelection(_table, selected, row => (row.Tag as WorkflowLibraryEntry)?.Name);
        var drafts = _entries.Count(entry => !entry.IsDeleted && (entry.HasDraft || !entry.IsPublished));
        var live = _entries.Count(entry => !entry.IsDeleted);
        var published = _entries.Count(entry => !entry.IsDeleted && entry.IsPublished);
        var issues = _entries.Count(entry => entry.Error is not null || (StatusOf?.Invoke(entry)?.Contains("诊断") ?? false));
        SetFilterText(0, $"全部 {live}");
        SetFilterText(1, $"已发布 {published}");
        SetFilterText(2, $"草稿 {drafts}");
        SetFilterText(3, $"有诊断 {issues}");
        _directory.Text = _library.Directory;
        HeaderOf(_tableCard).Subtitle = $"{shown.Length} 个";
        _status.Summary = keyword.Length == 0
            ? $"流程 {live} 个" + (drafts > 0 ? $" · {drafts} 个有没发布的修改" : string.Empty)
            : $"找到 {shown.Length} 个（共 {_entries.Count} 个）";
        UpdateCommands();
    }

    private void SetFilterText(int index, string text)
    {
        if (_filter.Items[index].Text != text) _filter.Items[index].Text = text;
    }

    private string? _detailKey;

    private void ShowDetail(WorkflowLibraryEntry? entry)
    {
        var key = entry is null ? null : $"{entry.Name}|{entry.ModifiedUtc.Ticks}|{entry.Version}|{entry.HasDraft}|{StatusOf?.Invoke(entry)}";
        if (key == _detailKey) return;
        _detailKey = key;
        var header = HeaderOf(_detailCard);
        if (entry is null)
        {
            header.Text = "详情";
            header.Subtitle = null;
            header.Invalidate();
            _detail.Show(new[] { new WorkflowFact("选中一个流程查看详情和版本") });
            return;
        }
        var status = entry.IsDeleted ? "已删除" : entry.Error is not null ? "读不出" : StatusOf?.Invoke(entry) ?? string.Empty;
        header.Text = entry.Name;
        header.Subtitle = status;
        header.Invalidate();
        var facts = new List<WorkflowFact>
        {
            new("文件", entry.FilePath is null ? "未发布" : Path.GetFileName(entry.FilePath)),
            new("流程名", entry.FlowName ?? "—"),
            new("节点 / 连线", $"{entry.NodeCount} / {entry.ConnectionCount}"),
            new("草稿", entry.IsDeleted ? "—" : !entry.IsPublished ? "未发布" : entry.HasDraft ? "有修改" : "无",
                ValueColor: entry.HasDraft || !entry.IsPublished ? WorkflowWinFormsTheme.Modern.Warning : null),
            new("最近修改", entry.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new("修改人", entry.ModifiedBy ?? "—")
        };
        if (entry.Error is not null) facts.Add(new WorkflowFact("错误", entry.Error, ValueColor: WorkflowWinFormsTheme.Modern.Error));
        IReadOnlyList<WorkflowLibraryVersion> versions;
        try { versions = _library.GetVersions(entry.Name); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            versions = Array.Empty<WorkflowLibraryVersion>();
        }
        facts.Add(WorkflowFact.Group("版本", versions.Count == 0 ? "还没有发布" : $"{versions.Count} 版"));
        foreach (var version in versions.OrderByDescending(item => item.Version).Take(6))
            facts.Add(new WorkflowFact(
                $"v{version.Version.ToString(CultureInfo.InvariantCulture)}  {version.Note ?? string.Empty}",
                $"{version.SavedBy ?? string.Empty} · {version.SavedUtc.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)}",
                Strong: version.Version == entry.Version));
        _detail.Show(facts);
    }

    private void UpdateCommands()
    {
        var entry = Selected;
        ShowDetail(entry);
        var live = entry is { IsDeleted: false };
        _open.Enabled = live && entry!.Error is null;
        _new.Enabled = _canEdit;
        _copy.Enabled = _canEdit && live;
        _rename.Enabled = _canEdit && live;
        _delete.Enabled = _canEdit && live;
        _import.Enabled = _canEdit;
        _export.Enabled = live;
        _history.Enabled = entry is not null;
        var hasDraft = live && (entry!.HasDraft || !entry.IsPublished);
        _changes.Enabled = hasDraft;
        _publish.Enabled = _canEdit && hasDraft && entry!.Error is null;
        _discard.Enabled = _canEdit && hasDraft;
    }

    private void OpenSelected()
    {
        if (Selected is { IsDeleted: false, Error: null } entry)
            OpenRequested?.Invoke(this, entry.FilePath ?? _library.DraftPathOf(entry.Name));
    }

    private void ShowChanges()
    {
        if (Selected is not { IsDeleted: false } entry) return;
        Guarded(() => MessageBox.Show(FindForm(), Publisher.DescribeChanges(entry.Name), $"没发布的修改 · {entry.Name}", MessageBoxButtons.OK, MessageBoxIcon.Information));
    }

    private async Task PublishAsync()
    {
        if (!_canEdit || Selected is not { IsDeleted: false } entry) return;
        try
        {
            if (await WorkflowPublishDialog.RunAsync(FindForm(), Publisher, entry.Name).ConfigureAwait(true) is { } version)
                Done($"{entry.Name} 已发布为 {WorkflowPublishDialog.Describe(version)}", entry.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or InvalidDataException or System.Text.Json.JsonException or System.Security.SecurityException)
        {
            ShowMessage(exception.Message);
            Reload();
        }
    }

    private async Task DiscardAsync()
    {
        if (!_canEdit || Selected is not { IsDeleted: false } entry) return;
        var text = entry.IsPublished
            ? $"放弃 {entry.Name} 没发布的修改，回到已发布的 v{entry.Version}？"
            : $"{entry.Name} 还从没发布过，放弃草稿后这个流程就没了。继续？";
        if (MessageBox.Show(FindForm(), text, "放弃草稿", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            await Publisher.DiscardAsync(entry.Name).ConfigureAwait(true);
            Done($"已放弃 {entry.Name} 的草稿", entry.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            ShowMessage(exception.Message);
            Reload();
        }
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
                names.Add(_library.Import(file));
            Done("已导入 " + string.Join("、", names) + "（草稿，发布后才会加载）", names.LastOrDefault());
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
        using var dialog = new WorkflowFlowHistoryDialog(_library, entry, _canEdit);
        WorkflowWinFormsTheme.Follow(dialog);
        dialog.ShowDialog(FindForm());
        if (dialog.Restored is { } version)
            Done($"{entry.Name} 的草稿已恢复成 v{version}，发布后生效", entry.Name);
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

/// <summary>历史版本（每次发布一版）：列出版本，和已发布的（或上一版）比较，把所选版本恢复成草稿。</summary>
internal sealed class WorkflowFlowHistoryDialog : Form
{
    private readonly WorkflowFlowLibrary _library;
    private readonly WorkflowLibraryEntry _entry;
    private readonly ModernDataGridView _versions = WorkflowPage.Table("版本", "时间", "发布人", "说明");
    private readonly TextBox _diff = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    private readonly ModernButton _compare = new() { Text = "和已发布的比较" };
    private readonly ModernButton _comparePrevious = new() { Text = "和上一版比较" };
    private readonly ModernButton _restore = new() { Text = "恢复成草稿" , ButtonType = ModernButtonType.Primary };

    public WorkflowFlowHistoryDialog(WorkflowFlowLibrary library, WorkflowLibraryEntry entry, bool canEdit)
    {
        _library = library;
        _entry = entry;
        Text = $"历史版本 · {entry.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 560);
        MinimumSize = new Size(560, 360);
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top , WrapContents = false, Padding = new Padding(0, 6, 0, 6) };
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
        _compare.Enabled = entry.IsPublished;
        _restore.Enabled = canEdit;
        _versions.SelectionChanged += (_, _) => _comparePrevious.Enabled = Previous(Selected) is not null;
        _compare.Click += (_, _) => { if (Selected is { } version) ShowDiff($"v{version.Version} → 已发布", version.Version, 0); };
        _comparePrevious.Click += (_, _) => { if (Selected is { } version && Previous(version) is { } previous) ShowDiff($"v{previous.Version} → v{version.Version}", previous.Version, version.Version); };
        _restore.Click += (_, _) => Restore();
        _diff.Text = _versions.Rows.Count == 0 ? "还没有历史版本。每次发布会记一版。" : "选一个版本，再点“和当前比较”或“和上一版比较”。";
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

    internal static string Describe(string title, WorkflowFlowComparison diff) => WorkflowFlowComparisonText.Describe(title, diff);

    private void Restore()
    {
        if (Selected is not { } version) return;
        var replace = File.Exists(_library.DraftPathOf(_entry.Name)) ? "现有的草稿会被替换。" : string.Empty;
        if (MessageBox.Show(this, $"把 v{version.Version} 恢复成 {_entry.Name} 的草稿？{replace}发布后才生效，正在跑的流程不受影响。", "恢复版本",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            _library.Restore(_entry.Name, version.Version);
            Restored = version.Version;
            Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _diff.Text = exception.Message;
        }
    }
}
