using System.Drawing.Drawing2D;
using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 运行日志页（只看）：选流程和某一次运行（或“全部流程”：各流程最近一次运行排在一起），把运行按子流程、循环圈、并行分支折叠成树。
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
    private const string AllFlows = "全部流程";
    private readonly WorkflowRunLogBook _book;
    private readonly WorkflowRunLogView _view = new() { ControlOnly = true };
    private readonly ModernSelect _flowPicker = new() { PlaceholderText = "流程" };
    private readonly ModernSelect _runPicker = new() { PlaceholderText = "运行" };
    private readonly ModernSelect _filter = new();
    private readonly ModernCheckbox _controlOnly = new() { Text = "只看控制步骤", Checked = true };
    private readonly ModernSwitch _follow = new() { Checked = true };
    private readonly ModernInput _search = new() { PlaceholderText = "搜索节点、消息或数据" };
    private readonly ModernButton _expandAll = new() { Text = "全部展开", ButtonType = ModernButtonType.Text };
    private readonly ModernButton _autoFold = new() { Text = "自动折叠", ButtonType = ModernButtonType.Text };
    private readonly ModernButton _copy = new() { Text = "复制", ButtonType = ModernButtonType.Text };
    private readonly TreeTable _table = Table<TreeTable>("位置 · 节点 · 消息", "时间", "结果", "耗时");
    private readonly WorkflowFactList _details = new();
    private readonly WorkflowCard _tableCard;
    private readonly WorkflowCard _detailsCard;
    private readonly WorkflowStatusLine _status = new();
    private string _detailsText = string.Empty;
    private IReadOnlyList<WorkflowRunLogLine> _lines = Array.Empty<WorkflowRunLogLine>();
    private IReadOnlyList<WorkflowRunLogSummary> _runs = Array.Empty<WorkflowRunLogSummary>();
    private string _flowsSignature = string.Empty;
    private string? _flow;
    private int _runIndex = -1;
    private long _seenVersion = -1;
    private bool _running;
    private bool _all;
    private bool _pending;
    private bool _updating;

    /// <summary>创建运行日志页。</summary>
    /// <param name="book">运行日志本。</param>
    public WorkflowRunLogPage(WorkflowRunLogBook book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _filter.Items.AddRange(new object[] { "全部", "失败与故障", "提示" });
        _filter.SelectedIndex = 0;

        // 工具条卡：流程、运行、结果、只看控制步骤、搜索；右边“跟随最新”。
        var bar = new WorkflowToolbar();
        bar.Add(_flowPicker, 160);
        bar.Add(_runPicker, 280);
        bar.Add(_filter, 140);
        bar.Add(_controlOnly);
        bar.Add(_search, 220);
        bar.AddRight(new Label { Text = "跟随最新" });
        bar.AddRight(_follow);

        _table.VirtualMode = true;
        // 卡片已经有圆角边框；表格自己再画一圈（150% 缩放下是 1.5 px 的深色线、圆角被裁掉）就成了黑框，这里不画。
        _table.CornerRadius = 0;
        ColumnWidths(_table, 1, 0, 0, 0);
        foreach (DataGridViewColumn column in _table.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _table.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _table.Columns[3].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        _table.CellValueNeeded += OnCellValueNeeded;
        _table.PaintCell = PaintCell;
        // 短列不按内容测列头（列头文字会被截成“结…”），直接按列头文字给够最小宽度。
        for (var index = 1; index < _table.Columns.Count; index++)
            _table.Columns[index].MinimumWidth = TextRenderer.MeasureText(_table.Columns[index].HeaderText, _table.Font).Width + _table.LogicalToDeviceUnits(28);
        _table.CellMouseClick += OnCellMouseClick;
        _table.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Toggle(e.RowIndex); };
        _table.KeyDown += OnTableKeyDown;
        _table.SelectionChanged += (_, _) => { if (!_updating) ShowDetails(); };
        _table.MouseWheel += (_, _) => StopFollowingWhenScrolledUp();

        _tableCard = Section("运行记录", null, _table);
        HeaderOf(_tableCard).AddAction(_autoFold);
        HeaderOf(_tableCard).AddAction(_expandAll);
        _detailsCard = Section("选中的记录", null, _details, DockStyle.Right, 380);
        HeaderOf(_detailsCard).AddAction(_copy);
        _expandAll.AccessibleDescription = "展开全部层级；手动展开或收起后不再自动改动，点“自动折叠”恢复。";
        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(_tableCard);
        middle.Controls.Add(Gap(DockStyle.Right));
        middle.Controls.Add(_detailsCard);

        Controls.Add(middle);
        Controls.Add(Gap(DockStyle.Top));
        Controls.Add(bar);
        Controls.Add(_status);

        _flowPicker.SelectedIndexChanged += (_, _) => { if (!_updating) SelectFlow(_flowPicker.SelectedItem as string); };
        _runPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            if (_flow is null) return;
            _runIndex = _runPicker.SelectedIndex;
            _follow.Checked = _runIndex == _runs.Count - 1;
            _view.ResetFolding();
            Rebuild(true);
        };
        _filter.SelectedIndexChanged += (_, _) =>
        {
            // 刷新时改选项文字（带上条数）会让下拉重新选中一次；这时不能再重建，否则重建里又改文字，无限递归直到栈溢出。
            if (_updating) return;
            _view.Filter = _filter.SelectedIndex switch { 1 => WorkflowRunLogFilter.Failures, 2 => WorkflowRunLogFilter.Notices, _ => WorkflowRunLogFilter.All };
            Rebuild(true);
        };
        _controlOnly.CheckedChanged += (_, _) => { _view.ControlOnly = _controlOnly.Checked; Rebuild(true); };
        _search.TextChanged += (_, _) => { _view.Search = _search.Text; Rebuild(true); };
        _expandAll.Click += (_, _) => { _view.Overrides.Clear(); _view.ExpandAll = true; Rebuild(true); };
        _autoFold.Click += (_, _) => { _view.ResetFolding(); Rebuild(true); };
        _follow.CheckedChanged += (_, _) => { if (_follow.Checked) { _runIndex = -1; Rebuild(true); } };
        _copy.Click += (_, _) =>
        {
            if (_detailsText.Length == 0) return;
            Clipboard.SetText(_detailsText);
            ShowMessage("已复制选中记录的详情");
        };
    }

    /// <summary>运行日志本。</summary>
    public WorkflowRunLogBook Book => _book;

    /// <summary>当前看的流程名；“全部流程”时为空。</summary>
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
        _status.Message = message;
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
            _flowPicker.Items.Clear();
            if (names.Count > 1) _flowPicker.Items.Add(AllFlows);
            foreach (var name in names) _flowPicker.Items.Add(name);
            // 默认看全部流程；之前选了某个流程就留在它上面。
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
        if (name == AllFlows) name = null;
        var all = name is null && _flowPicker.SelectedItem as string == AllFlows;
        if (name == _flow && (name is not null || all == _all)) return;
        _flow = name;
        _all = all;
        _runIndex = -1;
        _follow.Checked = true;
        _view.ResetFolding();
        Rebuild(true);
    }

    private void Rebuild(bool force)
    {
        if (IsDisposed) return;
        var version = _book.Version;
        if (!force)
        {
            if (version == _seenVersion && !_running) return;
            // 不跟随最新时界面不动（看的东西不会被刷走）；只提示有新记录。
            if (!_follow.Checked)
            {
                if (!_pending && version != _seenVersion) ShowMessage("有新的运行记录；打开“跟随最新”后刷新");
                _pending |= version != _seenVersion;
                return;
            }
        }
        _seenVersion = version;
        _pending = false;
        var now = DateTimeOffset.UtcNow;
        var flows = _all ? _book.Flows : _flow is null ? Array.Empty<string>() : new[] { _flow };
        var lines = new List<WorkflowRunLogLine>();
        var info = new RunInfo();
        _runs = _flow is null ? Array.Empty<WorkflowRunLogSummary>() : _book.Runs(_flow);
        if (_follow.Checked || _runIndex >= _runs.Count) _runIndex = _runs.Count - 1;
        RefreshRunPicker();
        _running = false;
        foreach (var flow in flows)
        {
            var runs = _all ? _book.Runs(flow) : _runs;
            var index = _all ? runs.Count - 1 : _runIndex;
            if (index < 0 || index >= runs.Count) continue;
            var snapshot = _book.Read(flow, index, log => (Lines: _view.Build(log, now), log.EventCount, log.Truncated, log.FaultSessions));
            if (snapshot.Lines is null) continue;
            lines.AddRange(snapshot.Lines);
            _running |= runs[index].Ended is null;
            info = new RunInfo(info.Runs + 1, info.Events + snapshot.EventCount, info.Truncated || snapshot.Truncated,
                info.FaultSessions + snapshot.FaultSessions, info.Failures + runs[index].Failures, info.Notices + runs[index].Notices);
        }
        RefreshFilterCounts(info);
        Show(lines, info);
    }

    /// <summary>正在看的运行（全部流程时是各流程最近一次的合计）。</summary>
    private readonly record struct RunInfo(int Runs, long Events, bool Truncated, int FaultSessions, int Failures, int Notices);

    private void RefreshRunPicker()
    {
        _updating = true;
        try
        {
            var texts = _all ? new[] { "各流程最近一次运行" } : _runs.Select(Describe).ToArray();
            if (_runPicker.Items.Count != texts.Length || !texts.Select((text, index) => Equals(_runPicker.Items[index], text)).All(same => same))
            {
                _runPicker.Items.Clear();
                foreach (var text in texts) _runPicker.Items.Add(text);
            }
            _runPicker.Enabled = !_all;
            _runPicker.SelectedIndex = _all ? 0 : _runIndex >= 0 && _runIndex < _runPicker.Items.Count ? _runIndex : -1;
        }
        finally
        {
            _updating = false;
        }
    }

    private void RefreshFilterCounts(RunInfo info)
    {
        _updating = true;
        try
        {
            var selected = _filter.SelectedIndex;
            var failures = info.Failures > 0 ? $"失败与故障 {info.Failures}" : "失败与故障";
            var notices = info.Notices > 0 ? $"提示 {info.Notices}" : "提示";
            // 文字没变就不动选项（改选中的那一项会触发重新选中）。
            if (!Equals(_filter.Items[1], failures)) _filter.Items[1] = failures;
            if (!Equals(_filter.Items[2], notices)) _filter.Items[2] = notices;
            if (_filter.SelectedIndex != selected) _filter.SelectedIndex = selected;
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

    private void Show(IReadOnlyList<WorkflowRunLogLine> lines, RunInfo info)
    {
        var selectedKey = SelectedLine()?.Key;
        var first = _table.RowCount > 0 ? _table.FirstDisplayedScrollingRowIndex : -1;
        _updating = true;
        try
        {
            _lines = lines;
            // 只增减差的行：先清零再加回会让整张表的行全部重建一遍。
            if (_table.RowCount != lines.Count)
                _table.RowCount = lines.Count;
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
                if (_follow.Checked) ScrollTo(_table, lines.Count - 1);
                else if (first >= 0) ScrollTo(_table, Math.Min(first, lines.Count - 1));
            }
        }
        finally
        {
            _updating = false;
        }
        ShowDetails();
        if (info.Runs > 0)
        {
            HeaderOf(_tableCard).Subtitle = $"{lines.Count.ToString("N0", CultureInfo.InvariantCulture)} 行 · 失败 {info.Failures}";
            var text = (_all ? $"{info.Runs} 个流程各自最近一次运行共 " : "本次运行 ")
                       + $"{info.Events.ToString("N0", CultureInfo.InvariantCulture)} 条 · 故障会话 {info.FaultSessions}";
            if (info.Truncated) text += $" · 超过 {_book.MaxRowsPerRun.ToString("N0", CultureInfo.InvariantCulture)} 行，后面的不再记";
            _status.Summary = text + $" · 保留最近 {_book.MaxRuns} 次运行 · 成功的子流程、循环、并行分支自动收起";
        }
        else
        {
            HeaderOf(_tableCard).Subtitle = null;
            _status.Summary = _flow is null ? "还没有流程运行过。" : "这个流程还没有运行记录。";
        }
        if (!_pending && _status.Message.StartsWith("有新的运行记录", StringComparison.Ordinal))
            _status.Message = string.Empty;
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
        if (line is null)
        {
            _detailsText = string.Empty;
            HeaderOf(_detailsCard).Subtitle = null;
            _details.Show(new[] { new WorkflowFact("在左边选一条记录查看详情") });
            return;
        }
        string Label(string? nodeType, string key) => _book.DataLabel(line.FlowName, nodeType, key);
        _detailsText = WorkflowRunLogText.Details(line, Label).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        HeaderOf(_detailsCard).Subtitle = line.Title;
        var facts = new List<WorkflowFact>();
        var data = false;
        foreach (var (label, value) in WorkflowRunLogText.DetailRows(line, Label))
        {
            if (label.Length == 0)
            {
                if (!data) facts.Add(WorkflowFact.Group("数据"));
                data = true;
                var split = value.IndexOf(" = ", StringComparison.Ordinal);
                facts.Add(split < 0 ? new WorkflowFact(value) : new WorkflowFact(value.Substring(0, split), value.Substring(split + 3)));
                continue;
            }
            var color = label == "结果" ? StateColor(line.State, WorkflowWinFormsTheme.Palette.Colors) : (Color?)null;
            facts.Add(new WorkflowFact(label, value, ValueColor: color));
        }
        _details.Show(facts);
    }

    private void Toggle(int index)
    {
        if (index < 0 || index >= _lines.Count || !_lines[index].HasChildren) return;
        _view.Toggle(_lines[index]);
        _follow.Checked = false;
        Rebuild(true);
    }

    // 表格还没排版（页面没显示、高度不够一行）时设滚动位置会抛“没有可供显示行的空间”，这时不滚。
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
        var glyph = _table.LogicalToDeviceUnits(4 + line.Depth * Indent);
        if (e.X >= glyph - 2 && e.X <= glyph + _table.LogicalToDeviceUnits(18)) Toggle(e.RowIndex);
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

    /// <summary>
    /// 运行日志专用表格：第一列（树形缩进、引导线、展开箭头、种类标记）和结果列自己画。
    /// 直接重写 OnCellPainting，不依赖基类是否触发 CellPainting 事件。
    /// </summary>
    private sealed class TreeTable : ModernDataGridView
    {
        public Func<DataGridViewCellPaintingEventArgs, bool>? PaintCell { get; set; }

        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
        {
            if (PaintCell?.Invoke(e) == true)
            {
                e.Handled = true;
                return;
            }
            base.OnCellPainting(e);
        }
    }

    /// <summary>画运行日志的一格；返回是否已画（未画的交给表格默认绘制）。</summary>
    private bool PaintCell(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _lines.Count || e.Graphics is null || e.CellStyle is null) return false;
        var line = _lines[e.RowIndex];
        var colors = WorkflowWinFormsTheme.Palette.Colors;
        // 选中行只换底色（浅主色），文字、标记、圆点保持原来的颜色；表格的选中前景色就是普通文字色，用了会全变黑。
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var graphics = e.Graphics;
        var bounds = e.CellBounds;
        var font = e.CellStyle.Font!;
        var text = WorkflowWinFormsTheme.Solid(colors.Text);
        var muted = WorkflowWinFormsTheme.Solid(colors.TextMuted);
        // 量宽度不能带省略号标志（不给宽度时会量成 0，后面的文字就叠上去了），画的时候再加。
        var measure = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var flags = measure | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
        var pad = _table.LogicalToDeviceUnits(6);
        e.PaintBackground(bounds, selected);
        switch (e.ColumnIndex)
        {
            case 1:
                TextRenderer.DrawText(graphics, WorkflowRunLogText.Time(line.Started), font, Inner(bounds, pad), muted, flags);
                return true;
            case 2:
            {
                // 结果：前面一个彩色圆点，文字同色；没有结果的行（消息、提示）留空。
                if (line.State == WorkflowRunLogState.None) return true;
                var color = StateColor(line.State, colors);
                var dot = _table.LogicalToDeviceUnits(7);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color))
                    graphics.FillEllipse(brush, bounds.Left + pad, bounds.Top + (bounds.Height - dot) / 2f, dot, dot);
                graphics.SmoothingMode = SmoothingMode.Default;
                var left = pad + dot + _table.LogicalToDeviceUnits(5);
                TextRenderer.DrawText(graphics, WorkflowRunLogText.State(line.State), font,
                    new Rectangle(bounds.Left + left, bounds.Top, Math.Max(0, bounds.Width - left - pad), bounds.Height), color, flags);
                return true;
            }
            case 3:
                if (line.Elapsed is { } elapsed)
                    TextRenderer.DrawText(graphics, WorkflowRunLogText.Duration(elapsed), font, Inner(bounds, pad),
                        line.State == WorkflowRunLogState.Running ? WorkflowWinFormsTheme.Solid(colors.Accent) : text,
                        (flags & ~TextFormatFlags.Left) | TextFormatFlags.Right);
                return true;
            case not 0:
                return false;
        }

        // 失败所在的各层：最左边一条红色竖条。
        if (line.HasFailure)
            using (var bar = new SolidBrush(WorkflowWinFormsTheme.Solid(colors.Error)))
                graphics.FillRectangle(bar, bounds.Left, bounds.Top, _table.LogicalToDeviceUnits(3), bounds.Height - 1);
        // 每一层一条浅色竖向引导线，层级一眼能看出来。
        var indent = _table.LogicalToDeviceUnits(Indent);
        var glyph = _table.LogicalToDeviceUnits(16);
        var origin = bounds.Left + _table.LogicalToDeviceUnits(4);
        using (var guide = new Pen(WorkflowWinFormsTheme.Solid(colors.Line.Mix(colors.Surface, .35))))
            for (var level = 1; level <= line.Depth; level++)
            {
                var guideX = origin + (level - 1) * indent + glyph / 2;
                graphics.DrawLine(guide, guideX, bounds.Top, guideX, bounds.Bottom);
            }
        var x = origin + line.Depth * indent;
        if (line.HasChildren) DrawChevron(graphics, new Rectangle(x, bounds.Top, glyph, bounds.Height), line.Expanded, muted);
        x += glyph + _table.LogicalToDeviceUnits(4);

        // 种类标记：圆角浅底胶囊（失败红、恢复 / 提示黄、合并 / 消息灰、其余主色）。
        var tagText = line.IsGroup ? "已合并" : line.Kind == WorkflowRunLogRowKind.Run ? null : WorkflowRunLogText.KindTag(line.Kind);
        if (tagText is not null)
        {
            var (fore, back) = TagColors(line, colors);
            using var tagFont = new Font(font.FontFamily, font.Size * .86f, font.Style, font.Unit);
            var size = TextRenderer.MeasureText(graphics, tagText, tagFont, Size.Empty, measure);
            var height = Math.Min(bounds.Height - _table.LogicalToDeviceUnits(6), size.Height + _table.LogicalToDeviceUnits(2));
            var pill = new Rectangle(x, bounds.Top + (bounds.Height - height) / 2, size.Width + _table.LogicalToDeviceUnits(12), height);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(pill, height / 2))
            using (var brush = new SolidBrush(back))
                graphics.FillPath(brush, path);
            graphics.SmoothingMode = SmoothingMode.Default;
            TextRenderer.DrawText(graphics, tagText, tagFont, pill, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            x = pill.Right + _table.LogicalToDeviceUnits(6);
        }

        // 标题：结构层（运行、子流程、循环、并行）加粗；合并的圈用浅色。
        var strong = line.Kind is WorkflowRunLogRowKind.Run or WorkflowRunLogRowKind.SubFlow or WorkflowRunLogRowKind.LoopBlock or WorkflowRunLogRowKind.Parallel;
        using (var titleFont = strong ? new Font(font, FontStyle.Bold) : null)
        {
            var useFont = titleFont ?? font;
            var titleSize = TextRenderer.MeasureText(graphics, line.Title, useFont, Size.Empty, measure);
            TextRenderer.DrawText(graphics, line.Title, useFont, new Rectangle(x, bounds.Top, Math.Max(0, bounds.Right - x - pad), bounds.Height),
                line.IsGroup ? muted : text, flags);
            x += titleSize.Width + _table.LogicalToDeviceUnits(12);
        }
        var detail = line.Detail;
        if (line.NoticeCount > 0 && line.Kind != WorkflowRunLogRowKind.Notice && !line.Expanded && !line.IsGroup)
            detail = (string.IsNullOrEmpty(detail) ? string.Empty : detail + " · ") + $"提示 {line.NoticeCount}";
        else if (line.IsGroup && line.NoticeCount > 0)
            detail += $" · 提示 {line.NoticeCount}";
        if (!string.IsNullOrEmpty(detail) && x < bounds.Right - pad * 3)
            TextRenderer.DrawText(graphics, detail, font, new Rectangle(x, bounds.Top, bounds.Right - x - pad, bounds.Height), muted, flags);
        return true;
    }

    private static Rectangle Inner(Rectangle bounds, int pad) => new(bounds.Left + pad, bounds.Top, Math.Max(0, bounds.Width - pad * 2), bounds.Height);

    /// <summary>展开 / 收起箭头：抗锯齿的折线（收起朝右，展开朝下）。</summary>
    private void DrawChevron(Graphics graphics, Rectangle box, bool expanded, Color color)
    {
        var half = _table.LogicalToDeviceUnits(4) / 1f;
        var cx = box.Left + box.Width / 2f;
        var cy = box.Top + box.Height / 2f;
        var points = expanded
            ? new[] { new PointF(cx - half, cy - half / 2), new PointF(cx, cy + half / 2), new PointF(cx + half, cy - half / 2) }
            : new[] { new PointF(cx - half / 2, cy - half), new PointF(cx + half / 2, cy), new PointF(cx - half / 2, cy + half) };
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(color, _table.LogicalToDeviceUnits(2) * .8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            graphics.DrawLines(pen, points);
        graphics.SmoothingMode = SmoothingMode.Default;
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color StateColor(WorkflowRunLogState state, WorkflowThemeColors colors) => WorkflowWinFormsTheme.Solid(state switch
    {
        WorkflowRunLogState.Succeeded => colors.Success,
        WorkflowRunLogState.Failed or WorkflowRunLogState.Interrupted or WorkflowRunLogState.FaultRouted => colors.Error,
        WorkflowRunLogState.Running => colors.Accent,
        WorkflowRunLogState.Undone => colors.Warning,
        _ => colors.TextMuted
    });

    private static (Color Fore, Color Back) TagColors(WorkflowRunLogLine line, WorkflowThemeColors colors)
    {
        var (fore, back) = line.Kind switch
        {
            WorkflowRunLogRowKind.Fault => (colors.Error, colors.ErrorSurface),
            WorkflowRunLogRowKind.Recovery or WorkflowRunLogRowKind.Notice => (colors.Warning, colors.WarningSurface),
            WorkflowRunLogRowKind.Message or WorkflowRunLogRowKind.Lifecycle => (colors.TextMuted, colors.SurfaceAlt),
            _ when line.IsGroup => (colors.TextMuted, colors.SurfaceAlt),
            _ => (colors.Accent, colors.AccentSoft)
        };
        return (WorkflowWinFormsTheme.Solid(fore), WorkflowWinFormsTheme.Solid(back));
    }
}
