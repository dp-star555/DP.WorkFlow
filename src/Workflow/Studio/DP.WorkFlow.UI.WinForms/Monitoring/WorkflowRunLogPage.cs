using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 运行日志页（只看）：选流程和某一次运行，把这次运行按子流程、循环圈、并行分支折叠成树。
/// 成功结束的层自动收起，连续成功的圈合并成“第 a–b 圈”，有失败、故障、提示的层和正在运行的那一圈保持展开；
/// 右边是选中记录的详情（位置、节点、结果、时间、令牌、消息和数据）。
/// </summary>
/// <remarks>
/// 数据来自 <see cref="WorkflowRunLogBook"/>（挂在流程管理的出口上，每个流程保留最近若干次运行，只在内存里）。
/// 只记流程控制本身；业务数据只显示节点自己写的消息和数据。
/// </remarks>
public sealed class WorkflowRunLogPage : WorkflowPage
{
    private const int Indent = 18;
    private readonly WorkflowRunLogBook _book;
    private readonly WorkflowRunLogView _view = new() { ControlOnly = true };
    private readonly ComboBox _flowPicker = Combo(200);
    private readonly ComboBox _runPicker = Combo(260);
    private readonly ComboBox _filter = Combo(150);
    private readonly CheckBox _controlOnly = Check("只看控制步骤", true);
    private readonly CheckBox _follow = Check("跟随最新", true);
    private readonly TextBox _search = new() { Width = 180, Margin = new Padding(0, 3, 10, 0) };
    private readonly ModernButton _expandAll = new() { Text = "全部展开", AutoSize = true };
    private readonly ModernButton _autoFold = new() { Text = "自动折叠", AutoSize = true };
    private readonly ModernButton _copy = new() { Text = "复制", AutoSize = true };
    private readonly ModernDataGridView _table = Table("位置 · 节点 · 消息", "时间", "结果", "耗时");
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, WordWrap = true };
    private readonly Label _status = Label();
    private IReadOnlyList<WorkflowRunLogLine> _lines = Array.Empty<WorkflowRunLogLine>();
    private IReadOnlyList<WorkflowRunLogSummary> _runs = Array.Empty<WorkflowRunLogSummary>();
    private string _flowsSignature = string.Empty;
    private string? _flow;
    private int _runIndex = -1;
    private long _seenVersion = -1;
    private bool _updating;

    /// <summary>创建运行日志页。</summary>
    /// <param name="book">运行日志本。</param>
    public WorkflowRunLogPage(WorkflowRunLogBook book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _filter.Items.AddRange(new object[] { "全部", "失败与故障", "提示" });
        _filter.SelectedIndex = 0;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 6, 6, 0) };
        top.Controls.Add(Caption("流程"));
        top.Controls.Add(_flowPicker);
        top.Controls.Add(Caption("运行"));
        top.Controls.Add(_runPicker);
        top.Controls.Add(_filter);
        top.Controls.Add(_controlOnly);
        top.Controls.Add(Caption("搜索"));
        top.Controls.Add(_search);
        top.Controls.Add(_expandAll);
        top.Controls.Add(_autoFold);
        top.Controls.Add(_follow);

        _table.VirtualMode = true;
        _table.Columns[0].FillWeight = 62;
        _table.Columns[1].FillWeight = 14;
        _table.Columns[2].FillWeight = 11;
        _table.Columns[3].FillWeight = 13;
        foreach (DataGridViewColumn column in _table.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _table.CellValueNeeded += OnCellValueNeeded;
        _table.CellPainting += OnCellPainting;
        _table.CellMouseClick += OnCellMouseClick;
        _table.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Toggle(e.RowIndex); };
        _table.KeyDown += OnTableKeyDown;
        _table.SelectionChanged += (_, _) => { if (!_updating) ShowDetails(); };
        _table.MouseWheel += (_, _) => StopFollowingWhenScrolledUp();

        var detailsBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        detailsBar.Controls.Add(_copy);
        var detailsBody = new Panel { Dock = DockStyle.Fill };
        _details.Dock = DockStyle.Fill;
        detailsBody.Controls.Add(_details);
        detailsBody.Controls.Add(detailsBar);

        var split = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, InitialPanel2Size = 340 };
        split.Panel1.Controls.Add(Card("运行记录", _table));
        split.Panel2.Controls.Add(Card("选中的记录", detailsBody));

        var legend = Label("自动折叠：成功结束的子流程、循环圈、并行分支收起，连续成功的圈合并；有失败、故障、提示的层和正在运行的那一圈展开。手动展开或收起后不再自动改动，点“自动折叠”恢复。");
        legend.Dock = DockStyle.Bottom;
        legend.Height = 22;
        _status.Dock = DockStyle.Bottom;
        _status.Height = 24;
        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(legend);
        Controls.Add(_status);

        _flowPicker.SelectedIndexChanged += (_, _) => { if (!_updating) SelectFlow(_flowPicker.SelectedItem as string); };
        _runPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            _runIndex = _runPicker.SelectedIndex;
            _follow.Checked = _runIndex == _runs.Count - 1;
            _view.ResetFolding();
            Rebuild(true);
        };
        _filter.SelectedIndexChanged += (_, _) =>
        {
            _view.Filter = _filter.SelectedIndex switch { 1 => WorkflowRunLogFilter.Failures, 2 => WorkflowRunLogFilter.Notices, _ => WorkflowRunLogFilter.All };
            Rebuild(true);
        };
        _controlOnly.CheckedChanged += (_, _) => { _view.ControlOnly = _controlOnly.Checked; Rebuild(true); };
        _search.TextChanged += (_, _) => { _view.Search = _search.Text; Rebuild(true); };
        _expandAll.Click += (_, _) => { _view.Overrides.Clear(); _view.ExpandAll = true; Rebuild(true); };
        _autoFold.Click += (_, _) => { _view.ResetFolding(); Rebuild(true); };
        _follow.CheckedChanged += (_, _) => { if (_follow.Checked) { _runIndex = -1; Rebuild(true); } };
        _copy.Click += (_, _) => { if (!string.IsNullOrEmpty(_details.Text)) Clipboard.SetText(_details.Text); };
    }

    /// <summary>运行日志本。</summary>
    public WorkflowRunLogBook Book => _book;

    /// <summary>当前看的流程名。</summary>
    public string? FlowName => _flow;

    /// <summary>当前显示的行（快照）。</summary>
    public IReadOnlyList<WorkflowRunLogLine> Lines => _lines;

    /// <summary>切到指定流程（看它最近一次运行）。</summary>
    /// <param name="name">流程名。</param>
    /// <returns>有这个流程的记录时返回 true。</returns>
    public bool ShowFlow(string name)
    {
        RefreshFlows();
        var index = _flowPicker.Items.IndexOf(name);
        if (index < 0) return false;
        _flowPicker.SelectedIndex = index;
        return true;
    }

    /// <inheritdoc />
    public override void ShowMessage(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => ShowMessage(message))); return; }
        _status.Text = message;
    }

    /// <inheritdoc />
    protected override void OnRefresh(bool canOperate, bool force)
    {
        RefreshFlows();
        Rebuild(force);
    }

    private void RefreshFlows()
    {
        var names = _book.Flows;
        var signature = string.Join("|", names);
        if (signature == _flowsSignature) return;
        _flowsSignature = signature;
        var selected = _flow;
        _updating = true;
        try
        {
            _flowPicker.BeginUpdate();
            _flowPicker.Items.Clear();
            foreach (var name in names) _flowPicker.Items.Add(name);
            _flowPicker.EndUpdate();
            var index = selected is null ? -1 : _flowPicker.Items.IndexOf(selected);
            if (index < 0 && names.Count > 0) index = 0;
            _flowPicker.SelectedIndex = index;
        }
        finally
        {
            _updating = false;
        }
        SelectFlow(_flowPicker.SelectedItem as string);
    }

    private void SelectFlow(string? name)
    {
        if (name == _flow && name is not null) return;
        _flow = name;
        _runIndex = -1;
        _follow.Checked = true;
        _view.ResetFolding();
        Rebuild(true);
    }

    private void Rebuild(bool force)
    {
        if (IsDisposed) return;
        var version = _book.Version;
        var running = _runs.Count > 0 && _runs[Math.Min(Math.Max(_runIndex, 0), _runs.Count - 1)].Ended is null;
        if (!force && version == _seenVersion && !running) return;
        _seenVersion = version;
        if (_flow is null)
        {
            _runs = Array.Empty<WorkflowRunLogSummary>();
            Show(Array.Empty<WorkflowRunLogLine>(), null);
            return;
        }
        _runs = _book.Runs(_flow);
        if (_follow.Checked || _runIndex >= _runs.Count) _runIndex = _runs.Count - 1;
        RefreshRunPicker();
        var now = DateTimeOffset.UtcNow;
        var snapshot = _book.Read(_flow, _runIndex, log => (Lines: _view.Build(log, now), log.EventCount, log.Truncated, log.FaultSessions));
        Show(snapshot.Lines ?? Array.Empty<WorkflowRunLogLine>(), snapshot.Lines is null ? null : snapshot);
    }

    private void RefreshRunPicker()
    {
        _updating = true;
        try
        {
            _runPicker.BeginUpdate();
            var texts = _runs.Select(Describe).ToArray();
            if (_runPicker.Items.Count != texts.Length || !texts.Select((text, index) => Equals(_runPicker.Items[index], text)).All(same => same))
            {
                _runPicker.Items.Clear();
                foreach (var text in texts) _runPicker.Items.Add(text);
            }
            _runPicker.SelectedIndex = _runIndex >= 0 && _runIndex < _runPicker.Items.Count ? _runIndex : -1;
            _runPicker.EndUpdate();
            var failures = _runIndex >= 0 && _runIndex < _runs.Count ? _runs[_runIndex].Failures : 0;
            var notices = _runIndex >= 0 && _runIndex < _runs.Count ? _runs[_runIndex].Notices : 0;
            var selected = _filter.SelectedIndex;
            _filter.Items[1] = failures > 0 ? $"失败与故障 {failures}" : "失败与故障";
            _filter.Items[2] = notices > 0 ? $"提示 {notices}" : "提示";
            _filter.SelectedIndex = selected;
        }
        finally
        {
            _updating = false;
        }
    }

    private static string Describe(WorkflowRunLogSummary run)
    {
        var text = WorkflowRunLogText.Time(run.Started).Substring(0, 8) + " 开始 · " + WorkflowRunLogText.State(run.State);
        if (run.Ended is { } ended) text += " · " + WorkflowRunLogText.Duration(ended - run.Started);
        return text + " · " + run.Steps.ToString("N0", CultureInfo.InvariantCulture) + " 步";
    }

    private void Show(IReadOnlyList<WorkflowRunLogLine> lines, (IReadOnlyList<WorkflowRunLogLine> Lines, long EventCount, bool Truncated, int FaultSessions)? snapshot)
    {
        var selectedKey = SelectedLine()?.Key;
        var first = _table.RowCount > 0 ? _table.FirstDisplayedScrollingRowIndex : -1;
        _updating = true;
        try
        {
            _lines = lines;
            if (_table.RowCount != lines.Count)
            {
                _table.RowCount = 0;
                _table.RowCount = lines.Count;
            }
            _table.Invalidate();
            var index = selectedKey is null ? -1 : IndexOf(selectedKey);
            _table.ClearSelection();
            if (index >= 0)
            {
                _table.CurrentCell = _table.Rows[index].Cells[0];
                _table.Rows[index].Selected = true;
            }
            if (lines.Count > 0)
            {
                if (_follow.Checked) _table.FirstDisplayedScrollingRowIndex = lines.Count - 1;
                else if (first >= 0) _table.FirstDisplayedScrollingRowIndex = Math.Min(first, lines.Count - 1);
            }
        }
        finally
        {
            _updating = false;
        }
        ShowDetails();
        if (snapshot is { } info)
        {
            var failures = _runIndex >= 0 && _runIndex < _runs.Count ? _runs[_runIndex].Failures : 0;
            var text = $"本次运行 {info.EventCount.ToString("N0", CultureInfo.InvariantCulture)} 条 · 显示 {lines.Count.ToString("N0", CultureInfo.InvariantCulture)} 行 · 失败 {failures} · 故障会话 {info.FaultSessions}";
            if (info.Truncated) text += $" · 超过 {_book.MaxRowsPerRun.ToString("N0", CultureInfo.InvariantCulture)} 行，后面的不再记";
            _status.Text = text + $" · 保留最近 {_book.MaxRuns} 次运行";
        }
        else
            _status.Text = _flow is null ? "还没有流程运行过。" : "这个流程还没有运行记录。";
    }

    private int IndexOf(string key)
    {
        for (var index = 0; index < _lines.Count; index++)
            if (_lines[index].Key == key) return index;
        return -1;
    }

    private WorkflowRunLogLine? SelectedLine()
    {
        var row = _table.CurrentCell?.RowIndex ?? -1;
        return row >= 0 && row < _lines.Count ? _lines[row] : null;
    }

    private void ShowDetails()
    {
        var line = SelectedLine();
        _details.Text = line is null || _flow is null ? string.Empty : WorkflowRunLogText.Details(_flow, line).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }

    private void Toggle(int index)
    {
        if (index < 0 || index >= _lines.Count || !_lines[index].HasChildren) return;
        _view.Toggle(_lines[index]);
        _follow.Checked = false;
        Rebuild(true);
    }

    private void StopFollowingWhenScrolledUp()
    {
        if (_follow.Checked && _table.RowCount > 0 && _table.FirstDisplayedScrollingRowIndex + _table.DisplayedRowCount(false) < _table.RowCount)
            _follow.Checked = false;
    }

    private void OnTableKeyDown(object? sender, KeyEventArgs e)
    {
        if (SelectedLine() is not { HasChildren: true } line) return;
        if ((e.KeyCode == Keys.Right && !line.Expanded) || (e.KeyCode == Keys.Left && line.Expanded))
        {
            Toggle(_table.CurrentCell!.RowIndex);
            e.Handled = true;
        }
    }

    private void OnCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _lines.Count || e.ColumnIndex != 0) return;
        var line = _lines[e.RowIndex];
        var glyph = 4 + line.Depth * Indent;
        if (e.X >= glyph - 2 && e.X <= glyph + 16) Toggle(e.RowIndex);
    }

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _lines.Count) return;
        var line = _lines[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            0 => line.Title,
            1 => WorkflowRunLogText.Time(line.Started),
            2 => WorkflowRunLogText.State(line.State),
            _ => line.Elapsed is { } elapsed ? WorkflowRunLogText.Duration(elapsed) : string.Empty
        };
    }

    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _lines.Count || e.Graphics is null) return;
        var line = _lines[e.RowIndex];
        var colors = WorkflowWinFormsTheme.Palette.Colors;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        if (e.ColumnIndex == 2)
        {
            e.PaintBackground(e.CellBounds, selected);
            var color = selected ? e.CellStyle!.SelectionForeColor : StateColor(line.State, colors);
            TextRenderer.DrawText(e.Graphics, WorkflowRunLogText.State(line.State), e.CellStyle!.Font, e.CellBounds, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.Handled = true;
            return;
        }
        if (e.ColumnIndex != 0) return;
        e.PaintBackground(e.CellBounds, selected);
        var bounds = e.CellBounds;
        var font = e.CellStyle!.Font;
        var text = selected ? e.CellStyle.SelectionForeColor : WorkflowWinFormsTheme.Solid(colors.Text);
        var muted = selected ? e.CellStyle.SelectionForeColor : WorkflowWinFormsTheme.Solid(colors.TextMuted);
        if (line.HasFailure)
            using (var bar = new SolidBrush(WorkflowWinFormsTheme.Solid(colors.Error)))
                e.Graphics.FillRectangle(bar, bounds.Left, bounds.Top + 2, 3, bounds.Height - 4);
        var x = bounds.Left + 4 + line.Depth * Indent;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        if (line.HasChildren)
            TextRenderer.DrawText(e.Graphics, line.Expanded ? "▾" : "▸", font, new Rectangle(x, bounds.Top, 16, bounds.Height), muted, flags);
        x += 18;
        var tagText = line.IsGroup ? "已合并" : line.Kind == WorkflowRunLogRowKind.Run ? null : WorkflowRunLogText.KindTag(line.Kind);
        if (tagText is not null)
        {
            var size = TextRenderer.MeasureText(e.Graphics, tagText, font, Size.Empty, flags);
            var tagColor = selected ? e.CellStyle.SelectionForeColor : TagColor(line, colors);
            using (var pen = new Pen(tagColor))
                e.Graphics.DrawRectangle(pen, x, bounds.Top + 4, size.Width + 6, bounds.Height - 9);
            TextRenderer.DrawText(e.Graphics, tagText, font, new Rectangle(x + 3, bounds.Top, size.Width + 2, bounds.Height), tagColor, flags);
            x += size.Width + 14;
        }
        var titleFont = line.Kind is WorkflowRunLogRowKind.Run or WorkflowRunLogRowKind.SubFlow or WorkflowRunLogRowKind.LoopBlock or WorkflowRunLogRowKind.Parallel
            ? new Font(font!, FontStyle.Bold) : font!;
        try
        {
            var titleSize = TextRenderer.MeasureText(e.Graphics, line.Title, titleFont, Size.Empty, flags);
            TextRenderer.DrawText(e.Graphics, line.Title, titleFont, new Rectangle(x, bounds.Top, Math.Max(0, bounds.Right - x), bounds.Height), text, flags | TextFormatFlags.EndEllipsis);
            x += titleSize.Width + 8;
        }
        finally
        {
            if (!ReferenceEquals(titleFont, font)) titleFont.Dispose();
        }
        var detail = line.Detail;
        if (line.NoticeCount > 0 && line.Kind != WorkflowRunLogRowKind.Notice && !line.Expanded)
            detail = (string.IsNullOrEmpty(detail) ? string.Empty : detail + " · ") + $"提示 {line.NoticeCount}";
        if (!string.IsNullOrEmpty(detail) && x < bounds.Right - 20)
            TextRenderer.DrawText(e.Graphics, detail, font, new Rectangle(x, bounds.Top, bounds.Right - x - 4, bounds.Height), muted, flags | TextFormatFlags.EndEllipsis);
        e.Handled = true;
    }

    private static Color StateColor(WorkflowRunLogState state, WorkflowThemeColors colors) => WorkflowWinFormsTheme.Solid(state switch
    {
        WorkflowRunLogState.Succeeded => colors.Success,
        WorkflowRunLogState.Failed or WorkflowRunLogState.Interrupted or WorkflowRunLogState.FaultRouted => colors.Error,
        WorkflowRunLogState.Running => colors.Accent,
        WorkflowRunLogState.Undone => colors.Warning,
        _ => colors.TextMuted
    });

    private static Color TagColor(WorkflowRunLogLine line, WorkflowThemeColors colors) => WorkflowWinFormsTheme.Solid(line.Kind switch
    {
        WorkflowRunLogRowKind.Fault => colors.Error,
        WorkflowRunLogRowKind.Recovery or WorkflowRunLogRowKind.Notice => colors.Warning,
        WorkflowRunLogRowKind.Message or WorkflowRunLogRowKind.Lifecycle => colors.TextMuted,
        _ when line.IsGroup => colors.TextMuted,
        _ => colors.Accent
    });

    private static ComboBox Combo(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(0, 2, 10, 0) };

    private static CheckBox Check(string text, bool value) => new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 5, 10, 0) };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 6, 0) };
}
