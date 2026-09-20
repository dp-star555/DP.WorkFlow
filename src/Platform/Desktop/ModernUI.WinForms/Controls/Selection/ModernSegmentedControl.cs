using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModernUI.WinForms;

/// <summary>表示现代分段选择控件中的一个文本、图标和值项目。</summary>
public sealed class ModernSegmentedItem : INotifyPropertyChanged
{
    private string _text;
    private object? _value;
    private ModernIconKind _icon;
    private bool _enabled = true;

    /// <summary>初始化一个分段选择项目。</summary>
    /// <param name="text">项目显示文本。</param>
    /// <param name="value">项目对应的业务值；省略时使用显示文本。</param>
    public ModernSegmentedItem(string text, object? value = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _value = value ?? text;
    }

    /// <summary>获取或设置项目显示文本。</summary>
    public string Text
    {
        get => _text;
        set { value ??= string.Empty; if (_text == value) return; _text = value; OnPropertyChanged(); }
    }

    /// <summary>获取或设置项目对应的业务值。</summary>
    public object? Value
    {
        get => _value;
        set { if (Equals(_value, value)) return; _value = value; OnPropertyChanged(); }
    }

    /// <summary>获取或设置显示在文本左侧的矢量图标。</summary>
    public ModernIconKind Icon
    {
        get => _icon;
        set { if (_icon == value) return; _icon = value; OnPropertyChanged(); }
    }

    /// <summary>获取或设置用户能否选择该项目。</summary>
    [DefaultValue(true)]
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; OnPropertyChanged(); }
    }

    /// <summary>项目显示或可用状态发生变化时发生。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>返回项目的显示文本。</summary>
    public override string ToString() => Text;
}

/// <summary>以单画布提供互斥项目、滑动选择背景、键盘导航和无障碍子项的现代分段选择控件。</summary>
[DefaultProperty(nameof(Items))]
[DefaultEvent(nameof(SelectedIndexChanged))]
[Description("ModernSegmentedControl 现代分段选择控件")]
[DisplayName("现代分段选择器")]
[ToolboxBitmap(typeof(ModernSegmentedControl), "Toolbox.Icons.Selection.bmp")]
[ToolboxItem(true)]
public sealed class ModernSegmentedControl : ModernControl
{
    private readonly BindingList<ModernSegmentedItem> _items = [];
    private int _selectedIndex = -1;
    private int _hoveredIndex = -1;
    private RectangleF _selectionBounds = RectangleF.Empty;
    private IDisposable? _selectionAnimation;

    /// <summary>初始化分段选择控件的默认尺寸和项目跟踪。</summary>
    public ModernSegmentedControl()
    {
        AccessibleRole = AccessibleRole.Grouping;
        Height = 36;
        MinimumSize = new Size(80, 30);
        Cursor = Cursors.Hand;
        _items.ListChanged += ItemsChanged;
    }

    /// <summary>获取分段选择项目集合。</summary>
    [Category("Data")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public BindingList<ModernSegmentedItem> Items => _items;

    /// <summary>获取或设置当前选中项目索引；-1 表示未选择。</summary>
    [Category("Data"), DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value >= 0 && !Items[value].Enabled) return;
            if (_selectedIndex == value) return;
            var previousBounds = GetItemBounds(_selectedIndex);
            _selectedIndex = value;
            AnimateSelection(previousBounds, GetItemBounds(value));
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.Selection, value);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>获取或设置当前选中项目。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernSegmentedItem? SelectedItem
    {
        get => (uint)SelectedIndex < (uint)Items.Count ? Items[SelectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    /// <summary>获取或设置当前选中项目对应的业务值；null 清除选择，找不到或项目禁用时保留原选择。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedValue
    {
        get => SelectedItem?.Value;
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }
            var index = Items.ToList().FindIndex(item => item.Enabled && Equals(item.Value, value));
            if (index >= 0) SelectedIndex = index;
        }
    }

    /// <summary>获取或设置选择背景滑动动画时长；设置为 0 可关闭动画。</summary>
    [Category("Behavior"), DefaultValue(160)]
    public int AnimationDuration { get; set; } = 160;

    /// <summary>当前选中项目发生变化时发生。</summary>
    public event EventHandler? SelectedIndexChanged;

    protected override AccessibleObject CreateAccessibilityInstance() => new SegmentedAccessibleObject(this);

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var inset = ScaleLogical(1f);
        var track = RectangleF.Inflate(bounds, -inset, -inset);
        canvas.Fill(Enabled ? Theme.Control : Theme.Background, track, ScaleLogical(Theme.Radius));
        canvas.Draw(Theme.Border, ScaleLogical(1f), track, ScaleLogical(Theme.Radius));

        if (!_selectionBounds.IsEmpty)
            canvas.Fill(Enabled ? Theme.PrimaryBackground : Theme.Control, _selectionBounds,
                Math.Max(2, ScaleLogical(Theme.Radius) - ScaleLogical(2)));

        for (var index = 0; index < Items.Count; index++)
            DrawItem(canvas, index, GetItemBounds(index));

        if (ShouldShowFocusCue)
        {
            var focusBounds = RectangleF.Inflate(bounds, -ScaleLogical(1.5f), -ScaleLogical(1.5f));
            ModernFocusVisual.Draw(canvas, Theme, focusBounds, ScaleLogical(Theme.Radius), DpiScale);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHoveredIndex(IndexFromPoint(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        SetHoveredIndex(-1);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        var index = IndexFromPoint(e.Location);
        if (index >= 0 && Items[index].Enabled) SelectedIndex = index;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Left or Keys.Up) return MoveSelection(-1);
        if (keyData is Keys.Right or Keys.Down) return MoveSelection(1);
        if (keyData == Keys.Home) return SelectEdge(first: true);
        if (keyData == Keys.End) return SelectEdge(first: false);
        if (keyData is Keys.Space or Keys.Enter && _hoveredIndex >= 0 && Items[_hoveredIndex].Enabled)
        {
            SelectedIndex = _hoveredIndex;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _selectionAnimation?.Dispose();
        _selectionBounds = GetItemBounds(SelectedIndex);
        Invalidate();
    }

    private void DrawItem(GdiCanvas canvas, int index, RectangleF bounds)
    {
        var item = Items[index];
        var selected = index == SelectedIndex;
        if (index == _hoveredIndex && item.Enabled && !selected)
            canvas.Fill(Theme.ControlHover, bounds, Math.Max(2, ScaleLogical(Theme.Radius) - ScaleLogical(2)));

        var foreground = !Enabled || !item.Enabled ? Theme.TextDisabled : selected ? Theme.Primary : Theme.Text;
        if (item.Icon == ModernIconKind.None)
        {
            canvas.DrawText(item.Text, Font, foreground, Rectangle.Round(bounds), ContentAlignment.MiddleCenter);
            return;
        }

        var iconSize = Math.Min(ScaleLogical(14), Math.Max(0, (int)bounds.Height - ScaleLogical(10)));
        var gap = ScaleLogical(6);
        var textSize = TextRenderer.MeasureText(item.Text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var contentWidth = iconSize + gap + textSize.Width;
        var left = bounds.Left + Math.Max(ScaleLogical(5), (bounds.Width - contentWidth) / 2f);
        var iconBounds = new RectangleF(left, bounds.Top + (bounds.Height - iconSize) / 2f, iconSize, iconSize);
        canvas.DrawIcon(item.Icon, foreground, iconBounds, ScaleLogical(1.5f));
        canvas.DrawText(item.Text, Font, foreground,
            Rectangle.Round(new RectangleF(iconBounds.Right + gap, bounds.Top,
                Math.Max(0, bounds.Right - iconBounds.Right - gap - ScaleLogical(4)), bounds.Height)),
            ContentAlignment.MiddleLeft);
    }

    private RectangleF GetItemBounds(int index)
    {
        if (index < 0 || index >= Items.Count || Items.Count == 0) return RectangleF.Empty;
        var inset = ScaleLogical(3);
        var availableWidth = Math.Max(0, Width - inset * 2);
        var itemWidth = availableWidth / (float)Items.Count;
        return new RectangleF(inset + itemWidth * index, inset,
            index == Items.Count - 1 ? Width - inset - (inset + itemWidth * index) : itemWidth,
            Math.Max(0, Height - inset * 2));
    }

    private int IndexFromPoint(Point point)
    {
        for (var index = 0; index < Items.Count; index++)
            if (GetItemBounds(index).Contains(point)) return index;
        return -1;
    }

    private bool MoveSelection(int delta)
    {
        if (Items.Count == 0) return false;
        var start = SelectedIndex >= 0 ? SelectedIndex : delta > 0 ? -1 : 0;
        for (var offset = 1; offset <= Items.Count; offset++)
        {
            var index = (start + delta * offset) % Items.Count;
            if (index < 0) index += Items.Count;
            if (!Items[index].Enabled) continue;
            SelectedIndex = index;
            SetHoveredIndex(index);
            return true;
        }
        return false;
    }

    private bool SelectEdge(bool first)
    {
        var indices = first ? Enumerable.Range(0, Items.Count) : Enumerable.Range(0, Items.Count).Reverse();
        foreach (var index in indices)
        {
            if (!Items[index].Enabled) continue;
            SelectedIndex = index;
            SetHoveredIndex(index);
            return true;
        }
        return false;
    }

    private void SetHoveredIndex(int index)
    {
        if (_hoveredIndex == index) return;
        var previous = _hoveredIndex;
        _hoveredIndex = index;
        if (previous >= 0) Invalidate(Rectangle.Ceiling(GetItemBounds(previous)));
        if (index >= 0) Invalidate(Rectangle.Ceiling(GetItemBounds(index)));
    }

    private void AnimateSelection(RectangleF from, RectangleF target)
    {
        _selectionAnimation?.Dispose();
        if (target.IsEmpty)
        {
            _selectionBounds = RectangleF.Empty;
            return;
        }
        if (from.IsEmpty) from = target;
        var start = from;
        _selectionAnimation = ModernAnimation.Start(this, 0, 1, Math.Max(0, AnimationDuration),
            progress =>
            {
                _selectionBounds = new RectangleF(
                    start.X + (target.X - start.X) * progress,
                    start.Y + (target.Y - start.Y) * progress,
                    start.Width + (target.Width - start.Width) * progress,
                    start.Height + (target.Height - start.Height) * progress);
                Invalidate();
            }, () => _selectionAnimation = null);
    }

    private void ItemsChanged(object? sender, ListChangedEventArgs e)
    {
        if (_selectedIndex >= Items.Count || _selectedIndex >= 0 && !Items[_selectedIndex].Enabled)
            SelectedIndex = -1;
        else
            _selectionBounds = GetItemBounds(SelectedIndex);
        if (_hoveredIndex >= Items.Count) _hoveredIndex = -1;
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _selectionAnimation?.Dispose();
            _items.ListChanged -= ItemsChanged;
        }
        base.Dispose(disposing);
    }

    private sealed class SegmentedAccessibleObject(ModernSegmentedControl owner) : ControlAccessibleObject(owner)
    {
        public override int GetChildCount() => owner.Items.Count;
        public override AccessibleObject? GetChild(int index) =>
            (uint)index < (uint)owner.Items.Count ? new SegmentAccessibleObject(owner, index) : null;
    }

    private sealed class SegmentAccessibleObject(ModernSegmentedControl owner, int index) : AccessibleObject
    {
        public override string? Name => (uint)index < (uint)owner.Items.Count ? owner.Items[index].Text : string.Empty;
        public override AccessibleRole Role => AccessibleRole.RadioButton;
        public override string? DefaultAction => "选择";
        public override Rectangle Bounds
        {
            get
            {
                var rectangle = Rectangle.Ceiling(owner.GetItemBounds(index));
                return owner.RectangleToScreen(rectangle);
            }
        }
        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Focusable | AccessibleStates.Selectable;
                if (index == owner.SelectedIndex) state |= AccessibleStates.Checked | AccessibleStates.Selected;
                if (!owner.Enabled || (uint)index >= (uint)owner.Items.Count || !owner.Items[index].Enabled)
                    state |= AccessibleStates.Unavailable;
                return state;
            }
        }

        public override void DoDefaultAction()
        {
            if (owner.Enabled && (uint)index < (uint)owner.Items.Count && owner.Items[index].Enabled)
                owner.SelectedIndex = index;
        }
    }
}
