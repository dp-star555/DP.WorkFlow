using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

/// <summary>具有统一主题、动画弹出层和完全自绘选项的单选控件。</summary>
[DefaultProperty(nameof(Items))]
[DefaultEvent(nameof(SelectedIndexChanged))]
[DefaultBindingProperty(nameof(SelectedValue))]
[Description("ModernSelect 现代单选下拉框")]
[DisplayName("现代单选选择器")]
[ToolboxBitmap(typeof(ModernSelect), "Toolbox.Icons.Selection.bmp")]
[ToolboxItem(true)]
public partial class ModernSelect : ModernValidatedControl
{
    private readonly ComboBox _model = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        BindingContext = new BindingContext()
    };
    private readonly SelectSurface _surface = new();
    private readonly ModernPopupController _dropDown;
    private readonly ModernItemImageSource _itemImages;
    private readonly ModernListDataView _dataView;
    private string _displayMember = string.Empty;
    private string _valueMember = string.Empty;
    private int _hoveredIndex = -1;
    private string? _placeholderText;
    private bool _readOnly;

    internal bool EmbeddedCellAppearance { get; set; }
    internal Color EmbeddedBackColor { get; set; }
    internal Color EmbeddedForeColor { get; set; }

    public ModernSelect()
    {
        AccessibleRole = AccessibleRole.ComboBox;
        Height = 34;
        MinimumSize = new Size(120, 30);
        Cursor = Cursors.Hand;
        _dropDown = new ModernPopupController(_surface);
        _itemImages = new ModernItemImageSource(() => { _surface.Invalidate(); Invalidate(); });
        _dataView = new ModernListDataView(BoundListChanged);
        _surface.PaintItem += DrawItem;
        _surface.HoveredIndexChanged += (_, index) => _hoveredIndex = index;
        _surface.ItemClicked += (_, index) =>
        {
            if (ReadOnly || (uint)index >= (uint)_model.Items.Count) return;
            _model.SelectedIndex = index;
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
            _dropDown.Close();
        };
        _surface.CloseRequested += (_, _) => _dropDown.Close();
        _model.SelectedIndexChanged += (_, _) =>
        {
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            SelectedValueChanged?.Invoke(this, EventArgs.Empty);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            _surface.Invalidate();
            Invalidate();
        };
        _dropDown.Closed += (_, _) =>
        {
            _hoveredIndex = -1;
            _surface.ResetHover();
            Focus();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
        };
        RightToLeftChanged += (_, _) => ApplyTextDirection();
        OnThemeChanged();
    }

    [Category("Data")]
    [Description("下拉列表中可供用户选择的项目集合。")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ComboBox.ObjectCollection Items => _model.Items;

    /// <summary>获取或设置下拉列表的数据源。</summary>
    [Category("Data"), DefaultValue(null)]
    [Description("为下拉列表提供项目的数据源。")]
    [AttributeProvider(typeof(IListSource))]
    public object? DataSource
    {
        get => _model.DataSource;
        set
        {
            if (ReferenceEquals(_model.DataSource, value)) return;
            _model.DataSource = value;
            _dataView.Observe(value);
            _surface.ItemCount = _model.Items.Count;
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
    [Description("数据项中由 SelectedValue 返回的属性名称。")]
    public string ValueMember
    {
        get => _valueMember;
        set => _valueMember = value ?? string.Empty;
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

    /// <summary>获取或设置当前选中项目对应的值；设置为 null 会清除选择。</summary>
    [Browsable(false)]
    [Bindable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedValue
    {
        get => SelectedItem is { } selected ? DataMemberResolver.Resolve(selected, ValueMember) : null;
        set
        {
            if (value is null)
            {
                _model.SelectedIndex = -1;
                return;
            }

            var items = _model.Items.Cast<object>().ToArray();
            var index = Array.FindIndex(items,
                item => ReferenceEquals(DataMemberResolver.Resolve(item, ValueMember), value));
            if (index < 0)
                index = Array.FindIndex(items, item => Equals(DataMemberResolver.Resolve(item, ValueMember), value));
            _model.SelectedIndex = index;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem { get => _model.SelectedItem; set => _model.SelectedItem = value; }

    [Category("Data"), DefaultValue(-1)]
    [Description("当前选中项目的索引；-1 表示未选择任何项目。")]
    public int SelectedIndex { get => _model.SelectedIndex; set => _model.SelectedIndex = value; }

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

    [Category("Behavior"), DefaultValue(8)]
    [Description("下拉层无需滚动时最多显示的项目行数。")]
    public int MaxDropDownItems { get; set; } = 8;

    [Category("Behavior"), DefaultValue(160)]
    [Description("下拉层展开动画的持续时间（毫秒）；设置为 0 可关闭动画。")]
    public int DropDownAnimationDuration { get; set; } = 160;

    [Category("Appearance"), DefaultValue(true)]
    [Description("控件获得键盘焦点时是否显示焦点描边。")]
    public bool ShowFocusBorder { get; set; } = true;
    [Description("当前选中项目发生更改时引发。")]
    public event EventHandler? SelectedIndexChanged;

    [Description("当前选中项目对应的 SelectedValue 发生变化时引发。")]
    public event EventHandler? SelectedValueChanged;

    [Description("用户从 Popup 提交选择时引发；程序赋值不会引发。")]
    public event EventHandler? SelectionCommitted;

    protected override AccessibleObject CreateAccessibilityInstance() => new SelectAccessibleObject(this);

    private void ApplyTextDirection()
    {
        _surface.RightToLeft = RightToLeft;
        _surface.Invalidate();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataView.Dispose();
            _itemImages.Dispose();
            _dropDown.Dispose();
            _model.Dispose();
        }
        base.Dispose(disposing);
    }

}
