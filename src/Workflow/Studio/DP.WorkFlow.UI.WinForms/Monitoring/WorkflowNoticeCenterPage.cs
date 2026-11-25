using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点提示页（按设计稿“节点提示中心”画板）：汇总流程管理里所有流程的提示板。
/// 顶部是类别、流程、搜索筛选和“全部确认”；左边按节点分组，中间是提示表，右边是选中提示的详情和“确认此位置”。
/// </summary>
/// <remarks>
/// 提示不中断运行，也不进报警；确认即清除这个位置（画布路径 + 节点）的提示，同一条提示再出现时重新累计。
/// 每次刷新只比较各提示板的版本，变了才重建列表。
/// </remarks>
public sealed class WorkflowNoticeCenterPage : WorkflowPage
{
    private const string AllFlows = "全部流程";
    private readonly WorkflowRunCenter _center;
    private readonly Func<WorkflowRunCenterFlow, WorkflowDocument?> _documentOf;
    private readonly Dictionary<string, WorkflowDocument?> _documents = new(StringComparer.Ordinal);
    private readonly ModernSegmentedControl _categories = new();
    private readonly ComboBox _flowPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ModernInput _search = new() { PlaceholderText = "搜索内容、节点或类别", Width = 240 };
    private readonly ModernButton _acknowledgeAll = new() { Text = "全部确认" };
    private readonly ListBox _nodes = new() { BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false };
    private readonly ModernDataGridView _table = Table("类别", "流程", "节点", "内容", "次数", "最近出现");
    private readonly WorkflowFactList _detail = new();
    private readonly ModernButton _acknowledge = new() { Text = "确认此位置" };
    private readonly WorkflowCard _nodesCard;
    private readonly WorkflowCard _tableCard;
    private readonly WorkflowStatusLine _status = new();
    private IReadOnlyList<Entry> _all = Array.Empty<Entry>();
    private IReadOnlyList<Entry> _shown = Array.Empty<Entry>();
    private string _versions = string.Empty;
    private string _categorySignature = string.Empty;
    private string _flowSignature = string.Empty;
    private bool _updating;

    /// <summary>创建节点提示页。</summary>
    /// <param name="center">流程管理。</param>
    /// <param name="documentOf">取流程的文档（用来显示节点标题）；为空时只显示节点标识。</param>
    public WorkflowNoticeCenterPage(WorkflowRunCenter center, Func<WorkflowRunCenterFlow, WorkflowDocument?>? documentOf = null)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
        _documentOf = documentOf ?? (_ => null);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
        _categories.Width = 320;
        foreach (var control in new Control[] { _categories, _flowPicker, _search })
        {
            control.Margin = new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(9), 0, 0);
            filters.Controls.Add(control);
        }
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
        WorkflowCommandSizing.Fit(_acknowledgeAll);
        right.Controls.Add(_acknowledgeAll);
        right.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(6), 0);
        var bar = new WorkflowCard { Dock = DockStyle.Top, LogicalExtent = 52 };
        bar.Controls.Add(filters);
        bar.Controls.Add(right);

        _nodes.ItemHeight = LogicalToDeviceUnits(48);
        _nodes.DrawItem += DrawNode;
        _nodesCard = Section("按节点", null, _nodes, DockStyle.Left, 260);
        _tableCard = Section("提示", null, _table);
        var detailBody = new Panel { Dock = DockStyle.Fill };
        var detailFooter = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(46), FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
        WorkflowCommandSizing.Fit(_acknowledge);
        detailFooter.Controls.Add(_acknowledge);
        detailBody.Controls.Add(_detail);
        detailBody.Controls.Add(detailFooter);

        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(_tableCard);
        middle.Controls.Add(Gap(DockStyle.Left));
        middle.Controls.Add(_nodesCard);
        middle.Controls.Add(Gap(DockStyle.Right));
        middle.Controls.Add(Section("详情", null, detailBody, DockStyle.Right, 320));

        Controls.Add(middle);
        Controls.Add(Gap(DockStyle.Top));
        Controls.Add(bar);
        Controls.Add(_status);

        _table.Columns[0].FillWeight = 12;
        _table.Columns[1].FillWeight = 14;
        _table.Columns[2].FillWeight = 16;
        _table.Columns[3].FillWeight = 44;
        _table.Columns[4].FillWeight = 8;
        _table.Columns[5].FillWeight = 16;
        _categories.SelectedIndexChanged += (_, _) => { if (!_updating) ApplyFilter(); };
        _flowPicker.SelectedIndexChanged += (_, _) => { if (!_updating) ApplyFilter(); };
        _search.TextChanged += (_, _) => ApplyFilter();
        _nodes.SelectedIndexChanged += (_, _) => { if (!_updating) SelectNode(); };
        _table.SelectionChanged += (_, _) => { if (!_updating) ShowDetail(); };
        _acknowledge.Click += (_, _) => { if (Selected is { } entry) Acknowledge(new[] { entry }); };
        _acknowledgeAll.Click += (_, _) => Acknowledge(_shown);
        ShowDetail();
    }

    /// <summary>当前显示的提示条数（经过筛选）。</summary>
    public int ShownCount => _shown.Count;

    private Entry? Selected =>
        _table.SelectedRows.Count == 1 && _table.SelectedRows[0].Tag is Entry entry ? entry : null;

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
        var versions = string.Join("|", _center.Flows.Select(flow => flow.Name + ":" + flow.Host.Notices.Version.ToString(CultureInfo.InvariantCulture)));
        if (!force && versions == _versions) return;
        _versions = versions;
        _all = _center.Flows
            .SelectMany(flow => flow.Host.Notices.GetAll().Select(notice => new Entry(flow, notice, NodeTitle(flow, notice.NodeId))))
            .OrderByDescending(entry => entry.Notice.Timestamp)
            .ToArray();
        RebuildFilters();
        ApplyFilter();
    }

    private void RebuildFilters()
    {
        _updating = true;
        try
        {
            var groups = _all.GroupBy(entry => entry.Notice.Category).OrderByDescending(group => group.Count()).ToArray();
            var signature = _all.Count + "|" + string.Join("|", groups.Select(group => group.Key + ":" + group.Count()));
            if (signature != _categorySignature)
            {
                _categorySignature = signature;
                var selected = _categories.SelectedItem?.Value as string;
                _categories.Items.Clear();
                _categories.Items.Add(new ModernSegmentedItem($"全部 {_all.Count}", string.Empty));
                foreach (var group in groups.Take(4))
                    _categories.Items.Add(new ModernSegmentedItem($"{group.Key} {group.Count()}", group.Key));
                var index = selected is null ? 0 : Math.Max(0, _categories.Items.ToList().FindIndex(item => Equals(item.Value, selected)));
                _categories.SelectedIndex = index;
                _categories.Width = LogicalToDeviceUnits(90) * _categories.Items.Count;
            }
            var flows = _center.Flows.Select(flow => flow.Name).ToArray();
            var flowSignature = string.Join("|", flows);
            if (flowSignature != _flowSignature)
            {
                _flowSignature = flowSignature;
                var selected = _flowPicker.SelectedItem as string;
                _flowPicker.Items.Clear();
                _flowPicker.Items.Add(AllFlows);
                foreach (var name in flows) _flowPicker.Items.Add(name);
                _flowPicker.SelectedIndex = Math.Max(0, selected is null ? 0 : _flowPicker.Items.IndexOf(selected));
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void ApplyFilter()
    {
        var category = _categories.SelectedItem?.Value as string;
        var flow = _flowPicker.SelectedItem as string;
        var text = _search.Text.Trim();
        _shown = _all.Where(entry =>
                (string.IsNullOrEmpty(category) || entry.Notice.Category == category)
                && (flow is null || flow == AllFlows || entry.Flow.Name == flow)
                && (text.Length == 0
                    || entry.Notice.Message.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.Notice.Category.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.NodeTitle.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    || (entry.Notice.NodeId ?? string.Empty).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();
        RebuildNodes();
        RebuildTable(null);
    }

    private void RebuildNodes()
    {
        (string Flow, string PlanPath, string? NodeId)? selected = _nodes.SelectedItem is NodeGroup current ? current.Key : null;
        var groups = _shown.GroupBy(entry => (entry.Flow.Name, entry.Notice.PlanPath, entry.Notice.NodeId))
            .Select(group => new NodeGroup(group.Key, group.First(), group.Sum(entry => entry.Notice.Count)))
            .OrderByDescending(group => group.Count)
            .ToArray();
        _updating = true;
        try
        {
            _nodes.BeginUpdate();
            _nodes.Items.Clear();
            foreach (var group in groups) _nodes.Items.Add(group);
            _nodes.EndUpdate();
            var index = selected is null ? -1 : Array.FindIndex(groups, group => group.Key == selected);
            _nodes.SelectedIndex = index;
        }
        finally
        {
            _updating = false;
        }
        HeaderOf(_nodesCard).Subtitle = groups.Length == 0 ? "没有提示" : $"{groups.Length} 个位置有提示";
    }

    private void SelectNode()
    {
        if (_nodes.SelectedItem is not NodeGroup group) { RebuildTable(null); return; }
        RebuildTable(group.Key);
    }

    private void RebuildTable((string Flow, string PlanPath, string? NodeId)? location)
    {
        var key = Selected?.Key;
        var rows = location is { } only
            ? _shown.Where(entry => (entry.Flow.Name, entry.Notice.PlanPath, entry.Notice.NodeId) == only).ToArray()
            : _shown;
        var theme = WorkflowWinFormsTheme.Modern;
        _updating = true;
        try
        {
            _table.Rows.Clear();
            foreach (var entry in rows)
            {
                var index = _table.Rows.Add(entry.Notice.Category, entry.Flow.Name, entry.NodeTitle, entry.Notice.Message,
                    entry.Notice.Count.ToString(CultureInfo.InvariantCulture),
                    entry.Notice.Timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                var row = _table.Rows[index];
                row.Tag = entry;
                row.Cells[0].Style.ForeColor = theme.Warning;
            }
            RestoreSelection(_table, key, row => (row.Tag as Entry)?.Key);
        }
        finally
        {
            _updating = false;
        }
        var total = _all.Sum(entry => entry.Notice.Count);
        HeaderOf(_tableCard).Subtitle = $"{rows.Count} 条";
        _status.Summary = _all.Count == 0
            ? "没有提示。提示在节点右上角累计计数，不中断运行，也不进报警；确认后清零。"
            : $"未确认提示 {_all.Count} 条（共 {total} 次） · 每个位置最多保留 {_center.Flows.FirstOrDefault()?.Host.Notices.MaxPerTarget ?? 50} 条";
        _acknowledgeAll.Enabled = _shown.Count > 0;
        ShowDetail();
    }

    private void ShowDetail()
    {
        if (Selected is not { } entry)
        {
            _detail.Show(new[] { new WorkflowFact("选中一条提示查看详情") });
            _acknowledge.Enabled = false;
            return;
        }
        var notice = entry.Notice;
        var history = _all.Where(item => item.Flow == entry.Flow && item.Notice.PlanPath == notice.PlanPath && item.Notice.NodeId == notice.NodeId).ToArray();
        var facts = new List<WorkflowFact>
        {
            WorkflowFact.Group(notice.Category, "不影响运行"),
            new(notice.Message, string.Empty, Strong: true),
            new("流程", entry.Flow.Name),
            new("位置", WorkflowNoticeLocation.Describe(notice)),
            new("节点", entry.NodeTitle),
            new("次数", notice.Count.ToString(CultureInfo.InvariantCulture)),
            new("最近出现", notice.Timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            new("所在运行", notice.RunId.ToString("N").Substring(0, 8)),
            WorkflowFact.Group("这个位置的提示", history.Length.ToString(CultureInfo.InvariantCulture) + " 条")
        };
        facts.AddRange(history.Take(6).Select(item => new WorkflowFact(item.Notice.Message, "× " + item.Notice.Count.ToString(CultureInfo.InvariantCulture))));
        _detail.Show(facts);
        _acknowledge.Enabled = true;
    }

    private void Acknowledge(IEnumerable<Entry> entries)
    {
        var cleared = 0;
        foreach (var location in entries.Select(entry => (entry.Flow, entry.Notice.PlanPath, entry.Notice.NodeId)).Distinct().ToArray())
            cleared += location.Flow.Host.Notices.Acknowledge(location.PlanPath, location.NodeId);
        ShowMessage(cleared == 0 ? "没有要确认的提示。" : $"已确认 {cleared} 条提示。");
        RefreshPage(CanOperate, true);
    }

    private string NodeTitle(WorkflowRunCenterFlow flow, string? nodeId)
    {
        if (nodeId is null) return "画布";
        if (!_documents.TryGetValue(flow.Name, out var document))
        {
            try { document = _documentOf(flow); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
            {
                document = null;
            }
            _documents[flow.Name] = document;
        }
        var node = document?.Nodes.FirstOrDefault(item => item.Node.Id == nodeId)?.Node;
        return node is null || string.IsNullOrWhiteSpace(node.Title) ? nodeId : node.Title;
    }

    private void DrawNode(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _nodes.Items[e.Index] is not NodeGroup group) return;
        var theme = WorkflowWinFormsTheme.Modern;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? theme.PrimarySurface : _nodes.BackColor))
            e.Graphics.FillRectangle(back, e.Bounds);
        int S(int value) => LogicalToDeviceUnits(value);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        using var bold = new Font(_nodes.Font, FontStyle.Bold);
        var countText = group.Count.ToString(CultureInfo.InvariantCulture);
        var countWidth = TextRenderer.MeasureText(countText, bold).Width;
        var left = e.Bounds.Left + S(14);
        var width = Math.Max(0, e.Bounds.Width - S(28) - countWidth - S(8));
        TextRenderer.DrawText(e.Graphics, group.First.NodeTitle, bold, new Rectangle(left, e.Bounds.Top + S(6), width, S(20)), theme.Text, flags);
        var sub = group.First.Flow.Name + (group.Key.NodeId is null ? " · 画布" : " · " + group.Key.NodeId);
        TextRenderer.DrawText(e.Graphics, sub, _nodes.Font, new Rectangle(left, e.Bounds.Top + S(26), width, S(18)), theme.TextSecondary, flags);
        TextRenderer.DrawText(e.Graphics, countText, bold, new Rectangle(e.Bounds.Right - S(14) - countWidth, e.Bounds.Top, countWidth, e.Bounds.Height),
            theme.Warning, flags | TextFormatFlags.VerticalCenter);
    }

    private sealed record Entry(WorkflowRunCenterFlow Flow, WorkflowNotice Notice, string NodeTitle)
    {
        public (string, string, string?, string, string) Key => (Flow.Name, Notice.PlanPath, Notice.NodeId, Notice.Category, Notice.Message);
    }

    private sealed record NodeGroup((string Flow, string PlanPath, string? NodeId) Key, Entry First, int Count);
}
