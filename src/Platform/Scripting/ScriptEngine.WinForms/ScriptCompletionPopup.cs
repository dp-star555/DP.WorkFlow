using ScriptEngine;
using ScriptEngine.Language;

namespace ScriptEngine.WinForms;

/// <summary>
/// 不夺取编辑器焦点的分类补全窗口。列表、模糊过滤、文档预览和高 DPI 定位都隐藏在该模块中，
/// 编辑器只需提供候选项并转发少量导航按键。
/// </summary>
internal sealed class ScriptCompletionPopup : ToolStripDropDown
{
    private readonly CompletionContent _content;
    private readonly ToolStripControlHost _host;
    private IReadOnlyList<RoslynScriptCompletionItem> _sourceItems = Array.Empty<RoslynScriptCompletionItem>();
    private bool _isOpen;

    public ScriptCompletionPopup()
    {
        AutoClose = false;
        AutoSize = false;
        DropShadowEnabled = true;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        _content = new CompletionContent();
        _content.ItemAccepted += (_, _) => TryAcceptSelection();
        _host = new ToolStripControlHost(_content)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        Items.Add(_host);
    }

    public event EventHandler<RoslynScriptCompletionItem>? ItemAccepted;

    public RoslynScriptCompletionItem? SelectedItem => _content.SelectedItem;

    public bool IsOpen => _isOpen;

    public void Open(
        Control owner,
        Point caretScreenPosition,
        IEnumerable<RoslynScriptCompletionItem> items,
        string filterText,
        Color background,
        Color foreground)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        _sourceItems = items.ToArray();
        if (_sourceItems.Count == 0)
        {
            Dismiss();
            return;
        }

        _content.ApplyTheme(background, foreground);
        _content.SetItems(_sourceItems, filterText);
        if (_content.ItemCount == 0)
        {
            Dismiss();
            return;
        }

        var scale = Math.Max(1F, owner.DeviceDpi / 96F);
        var popupSize = new Size((int)Math.Round(720 * scale), (int)Math.Round(290 * scale));
        _host.Size = popupSize;
        _content.Size = popupSize;
        Size = popupSize;
        var workingArea = Screen.FromPoint(caretScreenPosition).WorkingArea;
        var bounds = CalculatePopupBounds(workingArea, caretScreenPosition, popupSize, scale);
        if (bounds.Size != popupSize)
        {
            _host.Size = bounds.Size;
            _content.Size = bounds.Size;
            Size = bounds.Size;
        }
        if (_isOpen) Location = bounds.Location;
        else Show(bounds.Location);
        _isOpen = true;
        owner.Focus();
    }

    internal static Rectangle CalculatePopupBounds(
        Rectangle workingArea,
        Point caretScreenPosition,
        Size popupSize,
        float scale)
    {
        var width = Math.Min(popupSize.Width, workingArea.Width);
        var height = Math.Min(popupSize.Height, workingArea.Height);
        var x = Math.Min(Math.Max(caretScreenPosition.X, workingArea.Left), Math.Max(workingArea.Left, workingArea.Right - width));
        var below = caretScreenPosition.Y + (int)Math.Round(6 * Math.Max(1F, scale));
        var y = below + height <= workingArea.Bottom
            ? below
            : Math.Max(workingArea.Top, caretScreenPosition.Y - height - (int)Math.Round(22 * Math.Max(1F, scale)));
        return new Rectangle(x, y, width, height);
    }

    public void UpdateFilter(string filterText)
    {
        if (!_isOpen) return;
        _content.SetItems(_sourceItems, filterText);
        if (_content.ItemCount == 0) Dismiss();
    }

    public bool HandleNavigationKey(Keys keyCode)
    {
        if (!_isOpen) return false;
        switch (keyCode)
        {
            case Keys.Up:
                _content.MoveSelection(-1);
                return true;
            case Keys.Down:
                _content.MoveSelection(1);
                return true;
            case Keys.PageUp:
                _content.MoveSelection(-8);
                return true;
            case Keys.PageDown:
                _content.MoveSelection(8);
                return true;
            case Keys.Home:
                _content.SelectBoundary(first: true);
                return true;
            case Keys.End:
                _content.SelectBoundary(first: false);
                return true;
            case Keys.Enter:
            case Keys.Tab:
                TryAcceptSelection();
                return true;
            case Keys.Escape:
                Dismiss();
                return true;
            default:
                return false;
        }
    }

    public void Dismiss()
    {
        _isOpen = false;
        _sourceItems = Array.Empty<RoslynScriptCompletionItem>();
        _content.SetItems(_sourceItems, string.Empty);
        if (Visible)
        {
            Close(ToolStripDropDownCloseReason.Keyboard);
            Hide();
        }
    }

    public bool TryAcceptSelection()
    {
        var item = SelectedItem;
        if (item is null) return false;
        _content.RecordAccepted(item);
        Dismiss();
        ItemAccepted?.Invoke(this, item);
        return true;
    }

    private sealed class CompletionContent : UserControl
    {
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.None,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            VirtualMode = true,
            BorderStyle = BorderStyle.None
        };
        private readonly RichTextBox _description = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            TabStop = false
        };
        private readonly ImageList _images = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
        private IReadOnlyList<RoslynScriptCompletionItem> _items = Array.Empty<RoslynScriptCompletionItem>();
        private readonly Dictionary<string, long> _recentSelections = new(StringComparer.OrdinalIgnoreCase);
        private int _selectedIndex = -1;
        private long _selectionSequence;

        public CompletionContent()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _list.Columns.Add("名称", 285);
            _list.Columns.Add("类别", 120);
            _list.SmallImageList = _images;
            foreach (RoslynScriptCompletionKind kind in Enum.GetValues(typeof(RoslynScriptCompletionKind)))
            {
                using var image = RoslynScriptEditorControl.CreateCompletionImage(kind);
                _images.Images.Add(kind.ToString(), new Bitmap(image));
            }
            _list.Resize += (_, _) =>
            {
                if (_list.Columns.Count < 2 || _list.ClientSize.Width <= 0) return;
                _list.Columns[0].Width = Math.Max(140, (int)Math.Round(_list.ClientSize.Width * 0.70));
                _list.Columns[1].Width = Math.Max(80, _list.ClientSize.Width - _list.Columns[0].Width - 4);
            };
            _list.RetrieveVirtualItem += (_, eventArgs) => eventArgs.Item = CreateListItem(_items[eventArgs.ItemIndex]);
            _list.SelectedIndexChanged += (_, _) =>
            {
                if (_list.SelectedIndices.Count > 0) _selectedIndex = _list.SelectedIndices[0];
                UpdateDescription();
            };
            _list.DoubleClick += (_, _) =>
            {
                if (_list.SelectedIndices.Count > 0) ItemAccepted?.Invoke(this, EventArgs.Empty);
            };
            layout.Controls.Add(_list, 0, 0);
            layout.Controls.Add(_description, 1, 0);
            Controls.Add(layout);
        }

        public event EventHandler? ItemAccepted;

        public int ItemCount => _items.Count;

        public RoslynScriptCompletionItem? SelectedItem =>
            _selectedIndex < 0 || _selectedIndex >= _items.Count ? null : _items[_selectedIndex];

        public void ApplyTheme(Color background, Color foreground)
        {
            BackColor = background;
            ForeColor = foreground;
            _list.BackColor = background;
            _list.ForeColor = foreground;
            _description.BackColor = Blend(background, foreground, 0.055);
            _description.ForeColor = foreground;
        }

        public void SetItems(IEnumerable<RoslynScriptCompletionItem> source, string filterText)
        {
            _list.SelectedIndices.Clear();
            _items = source
                .Select(item => new RankedItem(
                    item,
                    RoslynScriptCompletionMatcher.GetScore(item.FilterText ?? item.DisplayText, filterText),
                    _recentSelections.TryGetValue(Identity(item), out var sequence) ? sequence : 0))
                .Where(item => item.Score < int.MaxValue)
                .OrderBy(item => item.Score)
                .ThenByDescending(item => item.Item.MatchPriority)
                .ThenByDescending(item => item.RecentSequence)
                .ThenBy(item => item.Item.SortText ?? item.Item.DisplayText, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Item.DisplayText, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Item)
                .ToArray();
            _list.VirtualListSize = _items.Count;
            _selectedIndex = _items.Count > 0 ? 0 : -1;
            if (_items.Count > 0) SetSelectedIndex(0);
            else _description.Clear();
            _list.Invalidate();
        }

        public void RecordAccepted(RoslynScriptCompletionItem item)
        {
            _recentSelections[Identity(item)] = ++_selectionSequence;
            if (_recentSelections.Count <= 128) return;
            var oldest = _recentSelections.OrderBy(entry => entry.Value).Take(_recentSelections.Count - 128).Select(entry => entry.Key).ToArray();
            foreach (var key in oldest) _recentSelections.Remove(key);
        }

        public void MoveSelection(int delta)
        {
            if (_items.Count == 0) return;
            var current = _selectedIndex < 0 ? 0 : _selectedIndex;
            SetSelectedIndex(Math.Max(0, Math.Min(_items.Count - 1, current + delta)));
        }

        public void SelectBoundary(bool first)
        {
            if (_items.Count == 0) return;
            SetSelectedIndex(first ? 0 : _items.Count - 1);
        }

        private void SetSelectedIndex(int index)
        {
            _selectedIndex = index;
            if (_list.IsHandleCreated)
            {
                _list.SelectedIndices.Clear();
                _list.SelectedIndices.Add(index);
                _list.EnsureVisible(index);
            }
            UpdateDescription();
            _list.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _images.Dispose();
            base.Dispose(disposing);
        }

        private ListViewItem CreateListItem(RoslynScriptCompletionItem item)
        {
            var row = new ListViewItem(item.DisplayText)
            {
                ImageKey = item.Kind.ToString(),
                ToolTipText = item.Description ?? string.Empty
            };
            row.SubItems.Add(string.IsNullOrWhiteSpace(item.InlineDescription)
                ? GetCategoryName(item.Kind)
                : item.InlineDescription);
            return row;
        }

        private void UpdateDescription()
        {
            var item = SelectedItem;
            _description.Text = item is null
                ? string.Empty
                : string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        item.DisplayText,
                        string.IsNullOrWhiteSpace(item.InlineDescription) ? GetCategoryName(item.Kind) : item.InlineDescription,
                        item.Description
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string GetCategoryName(RoslynScriptCompletionKind kind) => kind switch
        {
            RoslynScriptCompletionKind.Class or RoslynScriptCompletionKind.Type => "类",
            RoslynScriptCompletionKind.Interface => "接口",
            RoslynScriptCompletionKind.Struct => "结构",
            RoslynScriptCompletionKind.Enum => "枚举",
            RoslynScriptCompletionKind.Delegate => "委托",
            RoslynScriptCompletionKind.Method => "方法",
            RoslynScriptCompletionKind.Property => "属性",
            RoslynScriptCompletionKind.Field => "字段",
            RoslynScriptCompletionKind.Constant => "常量",
            RoslynScriptCompletionKind.Event => "事件",
            RoslynScriptCompletionKind.Namespace => "命名空间",
            RoslynScriptCompletionKind.Variable => "变量",
            RoslynScriptCompletionKind.Keyword => "关键字",
            RoslynScriptCompletionKind.Snippet => "代码片段",
            RoslynScriptCompletionKind.HostContext => "宿主上下文",
            _ => string.Empty
        };

        private static Color Blend(Color background, Color foreground, double amount)
        {
            var ratio = Math.Max(0D, Math.Min(1D, amount));
            return Color.FromArgb(
                (int)Math.Round(background.R + (foreground.R - background.R) * ratio),
                (int)Math.Round(background.G + (foreground.G - background.G) * ratio),
                (int)Math.Round(background.B + (foreground.B - background.B) * ratio));
        }

        private static string Identity(RoslynScriptCompletionItem item) =>
            item.Kind + "\0" + item.DisplayText + "\0" + item.InsertionText;

        private sealed record RankedItem(RoslynScriptCompletionItem Item, int Score, long RecentSequence);
    }
}
