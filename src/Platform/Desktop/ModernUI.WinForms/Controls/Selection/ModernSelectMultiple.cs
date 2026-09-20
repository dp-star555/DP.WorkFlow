using System.ComponentModel;

namespace ModernUI.WinForms;

public enum MultipleSelectionDisplayMode { Summary, Tags }

/// <summary>使用完全自绘的复选下拉列表选择多个值，并在输入区域显示选择摘要。</summary>
[DefaultProperty(nameof(Items))]
[DefaultEvent(nameof(SelectionChanged))]
[Description("ModernSelectMultiple 现代多选下拉框")]
[DisplayName("现代多选选择器")]
[ToolboxBitmap(typeof(ModernSelectMultiple), "Toolbox.Icons.Selection.bmp")]
[ToolboxItem(true)]
public sealed partial class ModernSelectMultiple : ModernValidatedControl
{
    // 必须使用普通 ListBox。CheckedListBox 会在 OwnerDraw 之前强制绘制原生方形 Checkbox。
    // ListBox 仅作为兼容的 Items 数据模型；弹出层由单一双缓冲画布绘制，避免原生 ListBox 在逐帧更新时擦除被点击行。
    private readonly ListBox _list = new() { BindingContext = new BindingContext() };
    private readonly MultiSelectSurface _surface = new();
    private readonly ModernPopupController _dropDown;
    private readonly ModernItemImageSource _itemImages;
    private readonly ModernListDataView _dataView;
    private readonly HashSet<object> _checkedItems = new(ModernReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, float> _checkProgress = [];
    private readonly Dictionary<int, IDisposable> _checkAnimations = [];
    private string _displayMember = string.Empty;
    private string _valueMember = string.Empty;
    private int _hoveredIndex = -1;
    private bool _openedFromKeyboard;
    private MultipleSelectionDisplayMode _displayMode;
    private int _maxVisibleTags = 3;
    private string? _placeholderText;
    private bool _readOnly;

    public ModernSelectMultiple()
    {
        AccessibleRole = AccessibleRole.ComboBox;
        Height = 34;
        MinimumSize = new Size(120, 30);
        Cursor = Cursors.Hand;
        _dropDown = new ModernPopupController(_surface);
        _itemImages = new ModernItemImageSource(() => { _surface.Invalidate(); Invalidate(); });
        _dataView = new ModernListDataView(BoundListChanged);
        _surface.PaintItem += DrawItem;
        _surface.HoveredIndexChanged += (_, index) => SetHoveredIndex(index);
        _surface.ItemClicked += (_, index) =>
        {
            if (ReadOnly) return;
            ToggleItem(index);
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
        };
        _surface.CloseRequested += (_, _) => _dropDown.Close();
        _dropDown.Closed += (_, _) =>
        {
            _openedFromKeyboard = false;
            Focus();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
        };
        RightToLeftChanged += (_, _) => ApplyTextDirection();
        OnThemeChanged();
    }

    /// <summary>获取下拉列表项目。</summary>
    [Category("Data")]
    [Description("下拉列表中可供用户多选的项目集合。")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ListBox.ObjectCollection Items => _list.Items;

    /// <summary>获取或设置下拉列表的数据源；更换数据源会清空当前选择。</summary>
    [Category("Data"), DefaultValue(null)]
    [Description("为下拉列表提供项目的数据源；更换数据源会清空当前选择。")]
    [AttributeProvider(typeof(IListSource))]
    public object? DataSource
    {
        get => _list.DataSource;
        set
        {
            if (ReferenceEquals(_list.DataSource, value)) return;
            var hadSelection = _checkedItems.Count > 0;
            _list.DataSource = value;
            _dataView.Observe(value);
            ApplyCheckedItems(new HashSet<object>(ModernReferenceEqualityComparer.Instance));
            if (hadSelection) SelectionChanged?.Invoke(this, EventArgs.Empty);
            _surface.ItemCount = _list.Items.Count;
            _surface.Invalidate();
            Invalidate();
        }
    }

    /// <summary>获取或设置用于显示的成员路径，支持点分隔的嵌套属性。</summary>
    [Category("Data"), DefaultValue("")]
    [Description("数据项中用于生成显示文本的属性名称。")]
    public string DisplayMember
    {
        get => _displayMember;
        set
        {
            value ??= string.Empty;
            if (_displayMember == value) return;
            _displayMember = value;
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            _surface.Invalidate();
            Invalidate();
        }
    }

    /// <summary>获取或设置作为选中值的成员路径，支持点分隔的嵌套属性。</summary>
    [Category("Data"), DefaultValue("")]
    [Description("数据项中由 SelectedValues 返回的属性名称。")]
    public string ValueMember
    {
        get => _valueMember;
        set
        {
            value ??= string.Empty;
            if (_valueMember == value) return;
            _valueMember = value;
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
    }

    /// <summary>获取或设置选择项图标列表。</summary>
    [Category("Appearance"), DefaultValue(null)]
    [Description("提供选择项图标的图像列表。")]
    public ImageList? ImageList { get => _itemImages.ImageList; set => _itemImages.ImageList = value; }

    /// <summary>获取或设置项目图标键的成员路径，优先于 ImageIndexMember。</summary>
    [Category("Data"), DefaultValue("")]
    [Description("数据项中用于解析 ImageList 图标键的成员路径。")]
    public string ImageKeyMember { get => _itemImages.ImageKeyMember; set => _itemImages.ImageKeyMember = value; }

    /// <summary>获取或设置项目图标索引的成员路径。</summary>
    [Category("Data"), DefaultValue("")]
    [Description("数据项中用于解析 ImageList 图标索引的成员路径。")]
    public string ImageIndexMember { get => _itemImages.ImageIndexMember; set => _itemImages.ImageIndexMember = value; }

    /// <summary>获取当前选中的项目。</summary>
    [Browsable(false)]
    public IReadOnlyList<object> SelectedItems => _list.Items.Cast<object>()
        .Where(item => _checkedItems.Contains(item)).ToArray();

    /// <summary>获取当前选中项目按照 ValueMember 解析后的值。</summary>
    [Browsable(false)]
    public IReadOnlyList<object?> SelectedValues => SelectedItems.Select(GetItemValue).ToArray();

    /// <summary>获取或设置下拉层是否展开。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DroppedDown
    {
        get => _dropDown.Visible;
        set
        {
            if (value == _dropDown.Visible) return;
            if (value) ShowDropDown(); else _dropDown.Close();
        }
    }

    /// <summary>获取或设置无选择时显示的提示文本。</summary>
    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许查看和展开候选项但禁止用户改变选择；程序仍可设置选择值。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(null)]
    [Description("未选择项目时显示的显式提示文字；null 使用当前语言的框架默认文本。")]
    public string? PlaceholderText { get => _placeholderText; set { _placeholderText = value; Invalidate(); } }

    [Browsable(false)] public string EffectivePlaceholderText => _placeholderText ?? FrameworkText(ModernUiTextKeys.SelectPlaceholder);

    /// <summary>获取或设置输入面使用摘要或标签展示选择项。</summary>
    [Category("Appearance"), DefaultValue(MultipleSelectionDisplayMode.Summary)]
    [Description("输入面使用逗号摘要或可移除标签展示选择项。")]
    public MultipleSelectionDisplayMode DisplayMode { get => _displayMode; set { if (_displayMode == value) return; _displayMode = value; Invalidate(); } }

    /// <summary>Tag 模式下最多显示的标签数量。</summary>
    [Category("Appearance"), DefaultValue(3)]
    [Description("Tag 模式下直接显示的最大标签数，剩余项合并为数量摘要。")]
    public int MaxVisibleTags { get => _maxVisibleTags; set { var next = Math.Max(1, value); if (_maxVisibleTags == next) return; _maxVisibleTags = next; Invalidate(); } }

    /// <summary>下拉列表最多显示的项目数。</summary>
    [Category("Behavior"), DefaultValue(8)]
    [Description("下拉层无需滚动时最多显示的项目行数。")]
    public int MaxDropDownItems { get; set; } = 8;

    /// <summary>下拉展开动画时长（毫秒），设为 0 可关闭动画。</summary>
    [Category("Behavior"), DefaultValue(160)]
    [Description("下拉层展开动画的持续时间（毫秒）；设置为 0 可关闭动画。")]
    public int DropDownAnimationDuration { get; set; } = 160;

    /// <summary>下拉项复选框的选中动画时长（毫秒）。</summary>
    [Category("Behavior"), DefaultValue(160)]
    [Description("下拉项目复选标记切换动画的持续时间（毫秒）。")]
    public int CheckBoxAnimationDuration { get; set; } = 160;

    /// <summary>获取或设置是否绘制键盘焦点描边。</summary>
    [Category("Appearance"), DefaultValue(true)]
    [Description("控件获得键盘焦点时是否显示焦点描边。")]
    public bool ShowFocusBorder { get; set; } = true;

    /// <summary>选择集合改变后发生。</summary>
    [Description("多选项目集合发生更改时引发。")]
    public event EventHandler? SelectionChanged;

    [Description("用户从 Popup 提交一次勾选变化时引发；程序赋值不会引发。")]
    public event EventHandler? SelectionCommitted;

    private void ApplyTextDirection()
    {
        _surface.RightToLeft = RightToLeft;
        _surface.Invalidate();
        Invalidate();
    }

    /// <summary>切换下拉层的展开状态。</summary>
    public void ToggleDropDown()
    {
        _openedFromKeyboard = false;
        if (_dropDown.Visible) _dropDown.Close();
        else ShowDropDown();
    }

    /// <summary>设置指定项目的选中状态。</summary>
    public void SetItemChecked(int index, bool value)
    {
        if ((uint)index >= (uint)_list.Items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        SetItemCheckedCore(index, value, IsHandleCreated);
    }

    /// <summary>整体设置当前选中项，不触发 SelectionChanged。</summary>
    /// <param name="selectedItems">要匹配的项目；每个元素最多匹配一个尚未选中的列表项。</param>
    public void SetSelectedItems(IEnumerable<object?> selectedItems)
    {
        ModernCompatibility.ThrowIfNull(selectedItems, nameof(selectedItems));
        ApplyCheckedItems(MatchItems(selectedItems, static item => item));
    }

    /// <summary>按照 ValueMember 整体设置当前选中值，不触发 SelectionChanged。</summary>
    /// <param name="selectedValues">要匹配的值；每个值最多匹配一个尚未选中的列表项。</param>
    public void SetSelectedValues(IEnumerable<object?> selectedValues)
    {
        ModernCompatibility.ThrowIfNull(selectedValues, nameof(selectedValues));
        ApplyCheckedItems(MatchItems(selectedValues, GetItemValue));
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new MultiSelectAccessibleObject(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataView.Dispose();
            CancelCheckAnimations();
            _itemImages.Dispose();
            _list.Dispose();
            _dropDown.Dispose();
        }
        base.Dispose(disposing);
    }

}
