using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

public sealed partial class ModernPropertyGrid
{
    private PropertyDescriptor[] GetProperties()
    {
        if (_selectedObject is null) return [];
        var comparer = StringComparer.Create(_localizationContext.Current.Culture, ignoreCase: false);
        return TypeDescriptor.GetProperties(_selectedObject).Cast<PropertyDescriptor>()
            .Where(property => property.IsBrowsable)
            .OrderBy(property => Presentation(property).CategoryText, comparer)
            .ThenBy(property => property.Attributes[typeof(PropertyOrderAttribute)] is PropertyOrderAttribute order ? order.Order : 0)
            .ThenBy(property => Presentation(property).DisplayName, comparer)
            .ToArray();
    }

    private void Rebuild()
    {
        if (_building || IsDisposed) return;
        _building = true;
        var rebuildVersion = ++_rebuildVersion;
        try
        {
            _content.SuspendLayout();
            foreach (Control existing in _content.Controls.Cast<Control>().ToArray())
            {
                if (existing.Tag is CategoryBodyState state) state.CancelAnimation();
                existing.Dispose();
            }
            _content.Controls.Clear();
            var restorePropertyKey = _selectedPropertyKey;
            var restoreScrollOffset = _scrollViewport.ScrollOffset;
            var restoreEditorFocus = _editors.FirstOrDefault(pair => pair.Value.ContainsFocus).Key;
            _editors.Clear();
            _presentations.Clear();
            _rows.Clear();
            _selectedRow = null;
            _lastRowsWidth = -1;
            var all = GetProperties();
            var search = _search.Text.Trim();
            var visible = all.Where(property => MatchesSearch(property, search)).ToArray();
            _summary.Text = search.Length == 0
                ? T(PropertyGridTextKeys.SummaryCount, new Dictionary<string, object?> { ["count"] = all.Length })
                : T(PropertyGridTextKeys.SummaryFiltered, new Dictionary<string, object?> { ["visible"] = visible.Length, ["total"] = all.Length });
            if (_selectedObject is null)
            {
                _content.Controls.Add(CreateHint(T(PropertyGridTextKeys.EmptyNoObject)));
                return;
            }
            if (visible.Length == 0)
            {
                _content.Controls.Add(CreateHint(T(PropertyGridTextKeys.EmptyNoMatch)));
                return;
            }
            foreach (var category in visible.GroupBy(property => Presentation(property).CategoryKey, StringComparer.Ordinal))
            {
                var properties = category.ToArray();
                var categoryText = Presentation(properties[0]).CategoryText;
                var body = CreateCategoryBody(category.Key, properties);
                _content.Controls.Add(CreateCategory(category.Key, categoryText, properties.Length, body));
                _content.Controls.Add(body);
            }
            if (restorePropertyKey is not null && _rows.TryGetValue(restorePropertyKey, out var restoredRow)
                && restoredRow.Tag is PropertyDescriptor restoredProperty) SelectRow(restoredRow, restoredProperty);
            PostLayout(() =>
            {
                if (rebuildVersion != _rebuildVersion) return;
                _scrollViewport.ScrollOffset = restoreScrollOffset;
                if (!string.IsNullOrEmpty(restoreEditorFocus) && _editors.TryGetValue(restoreEditorFocus, out var restoredEditor) && restoredEditor.Visible)
                    restoredEditor.Focus();
            });
        }
        finally
        {
            _content.ResumeLayout();
            ResizeRows();
            _building = false;
        }
    }

    private bool MatchesSearch(PropertyDescriptor property, string search)
    {
        if (search.Length == 0) return true;
        var presentation = Presentation(property);
        var comparison = _localizationContext.Current.Culture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return comparison.IndexOf(presentation.DisplayName, search, options) >= 0
            || property.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || comparison.IndexOf(presentation.CategoryText, search, options) >= 0
            || comparison.IndexOf(presentation.Description, search, options) >= 0;
    }

    private Control CreateCategory(string category, string categoryText, int count, Control body)
    {
        var state = (CategoryBodyState)body.Tag!;
        var collapsedInitially = _collapsedCategories.Contains(category);
        var button = new ModernButton
        {
            Text = T(PropertyGridTextKeys.CategoryHeader, new Dictionary<string, object?> { ["category"] = categoryText, ["count"] = count }),
            Icon = collapsedInitially ? ModernIconKind.ChevronRight : ModernIconKind.ChevronDown,
            ButtonType = ModernButtonType.Text,
            StrongHoverFeedback = true,
            Height = ScaleLogical(32),
            TextAlign = ContentAlignment.MiddleLeft,
            Theme = _theme,
            Margin = new Padding(0, ScaleLogical(6), 0, ScaleLogical(2)),
            TabStop = true
        };
        button.Click += (_, _) =>
        {
            var collapsed = _collapsedCategories.Add(category);
            if (!collapsed) _collapsedCategories.Remove(category);
            button.Icon = collapsed ? ModernIconKind.ChevronRight : ModernIconKind.ChevronDown;
            state.Animation?.Dispose();
            if (!collapsed) body.Visible = true;
            var from = body.Height;
            var target = collapsed ? 0 : state.ExpandedHeight;
            state.Animation = ModernAnimation.Start(body, from, target,
                AnimateCategoryExpansion ? Math.Max(0, CategoryAnimationDuration) : 0,
                value =>
                {
                    var height = Math.Max(0, (int)Math.Round(value));
                    // Height 变化本身会触发 FlowLayout；避免每帧再 PerformLayout 一次导致滚动条重复测量。
                    if (body.Height != height) body.Height = height;
                },
                () =>
                {
                    body.Height = target;
                    if (collapsed) body.Visible = false;
                    state.Animation = null;
                    ResizeRows();
                });
        };
        return button;
    }

    private Control CreateCategoryBody(string category, IReadOnlyList<PropertyDescriptor> properties)
    {
        var body = new BufferedFlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = _theme.Background
        };
        foreach (var property in properties) body.Controls.Add(CreateRow(property));
        var expandedHeight = body.Controls.Cast<Control>().Sum(control => control.Height + control.Margin.Vertical);
        var collapsed = _collapsedCategories.Contains(category);
        body.Tag = new CategoryBodyState(category, expandedHeight);
        body.Height = collapsed ? 0 : expandedHeight;
        body.Visible = !collapsed;
        body.SizeChanged += (_, _) => ResizeCategoryRows(body);
        return body;
    }

    private void ResizeCategoryRows(Control body)
    {
        var width = Math.Max(0, body.ClientSize.Width - body.Padding.Horizontal);
        if (body.Tag is CategoryBodyState state && state.LastWidth == width) return;
        if (body.Tag is CategoryBodyState currentState) currentState.LastWidth = width;
        foreach (Control row in body.Controls)
        {
            if (row.Width != width) row.Width = width;
            if (row is TableLayoutPanel table && table.ColumnStyles.Count >= 3)
            {
                var availableWidth = Math.Max(0, table.ClientSize.Width - table.Padding.Horizontal);
                table.ColumnStyles[0].Width = Math.Max(ScaleLogical(72), (float)Math.Round(availableWidth * .36));
            }
        }
    }

    private Control CreateRow(PropertyDescriptor property)
    {
        var row = new PropertyRowPanel
        {
            Height = ScaleLogical(_theme.PropertyRowHeight),
            MinimumSize = new Size(0, ScaleLogical(_theme.PropertyRowHeight)),
            Margin = new Padding(0, 0, 0, ScaleLogical(2)),
            Padding = new Padding(ScaleLogical(8), ScaleLogical(5), ScaleLogical(6), ScaleLogical(5)),
            ColumnCount = 3,
            RowCount = 1,
            OutsideColor = _theme.Background,
            NormalColor = _theme.Container,
            HoverColor = _theme.ControlHover,
            SelectedColor = _theme.PrimaryBackground,
            CornerRadius = 7,
            Tag = property
        };
        // 名称列使用统一宽度，保证各行编辑器左边缘对齐；单位列按文本内容自动占用空间。
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(120)));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var presentation = Presentation(property);
        var unit = presentation.Unit ?? string.Empty;
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var name = new Label
        {
            Text = presentation.DisplayName,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _theme.TextSecondary
        };
        var editor = CreateEditor(property);
        _editors[property.Name] = editor;
        _rows[presentation.PropertyKey] = row;
        editor.Dock = DockStyle.Fill;
        editor.Margin = Padding.Empty;
        row.Controls.Add(name, 0, 0);
        row.Controls.Add(editor, 1, 0);
        if (!string.IsNullOrEmpty(unit))
        {
            row.Controls.Add(new Label
            {
                Text = unit,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(ScaleLogical(6), 0, 0, 0),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = _theme.TextSecondary
            }, 2, 0);
        }
        WireSelection(row, row, property);
        return row;
    }
}
