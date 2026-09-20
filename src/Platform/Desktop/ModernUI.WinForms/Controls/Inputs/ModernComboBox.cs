using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>允许自由输入并提供候选项、自动完成和数据绑定的现代组合框。</summary>
[DefaultProperty(nameof(Items))]
[DefaultEvent(nameof(SelectedIndexChanged))]
[DefaultBindingProperty(nameof(Text))]
[Description("ModernComboBox 现代可编辑组合框")]
[DisplayName("现代组合框")]
[ToolboxBitmap(typeof(ModernComboBox), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed partial class ModernComboBox : ModernValidatedControl
{
    private readonly ModernNativeComboBox _comboBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDown,
        FlatStyle = FlatStyle.Flat,
        DrawMode = DrawMode.OwnerDrawFixed,
        IntegralHeight = false,
        FormattingEnabled = true,
        BindingContext = new BindingContext()
    };
    private readonly ComboArrowSurface _arrow = new();
    private readonly DisabledComboSurface _disabledSurface = new();
    private readonly ComboSelectedImageSurface _selectedImageSurface = new();
    private readonly ComboBottomChromeSurface _topChrome;
    private readonly ComboBottomChromeSurface _bottomChrome;
    private readonly ComboDropDownSurface _dropDownSurface = new();
    private readonly ModernPopupController _dropDown;
    private readonly ModernItemImageSource _itemImages;
    private readonly ModernListDataView _dataView;
    private object? _dataSource;
    private IList? _sourceList;
    private string _displayMember = string.Empty;
    private string _valueMember = string.Empty;
    private int _dropDownWidth;
    private bool _readOnly;
    private bool _restoringReadOnlyValue;
    private bool _settingText;
    private bool _userEditPending;
    private bool _hasCommittedText;
    private string _lastCommittedText = string.Empty;
    private object? _readOnlySelectedItem;
    private string _readOnlyText = string.Empty;
    private bool _autoCompleteConfigured;
    private bool _autoCompleteInitialized;
    private AutoCompleteMode _autoCompleteMode = AutoCompleteMode.SuggestAppend;
    private AutoCompleteSource _autoCompleteSource = AutoCompleteSource.ListItems;
    private bool _dpiMetricsInvalid;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalLayout);

    /// <summary>初始化可编辑组合框及其原生输入和候选列表行为。</summary>
    public ModernComboBox()
    {
        AccessibleRole = AccessibleRole.ComboBox;
        _topChrome = new ComboBottomChromeSurface(this);
        _bottomChrome = new ComboBottomChromeSurface(this);
        _dropDown = new ModernPopupController(_dropDownSurface);
        _itemImages = new ModernItemImageSource(UpdateItemImages);
        _dataView = new ModernListDataView(BoundListChanged);
        Height = 34;
        MinimumSize = new Size(120, 30);
        Padding = new Padding(3);
        Controls.Add(_comboBox);
        Controls.Add(_selectedImageSurface);
        Controls.Add(_arrow);
        Controls.Add(_disabledSurface);
        Controls.Add(_topChrome);
        Controls.Add(_bottomChrome);
        _topChrome.BringToFront();
        _bottomChrome.BringToFront();
        _dropDownSurface.PaintItem += DrawManagedDropDownItem;
        _dropDownSurface.ItemClicked += CommitManagedSelection;
        _dropDownSurface.CloseRequested += (_, _) => CloseManagedDropDown();
        _dropDown.Closed += (_, _) =>
        {
            _dropDownSurface.ResetHover();
            DropDownClosed?.Invoke(this, EventArgs.Empty);
            _arrow.Expanded = false;
            InvalidateChrome();
        };
        _arrow.Click += (_, _) =>
        {
            if (!Enabled) return;
            if (_dropDown.Visible) CloseManagedDropDown(); else ShowManagedDropDown();
        };
        _comboBox.TextChanged += (_, _) =>
        {
            if (!_settingText && _comboBox.Focused && _comboBox.SelectedIndex < 0)
                _userEditPending = true;
            base.Text = _comboBox.Text;
            _disabledSurface.Text = _comboBox.Text;
            if (!ReadOnly || !_dropDown.Visible) RememberReadOnlyValue();
            OnTextChanged(EventArgs.Empty);
        };
        _comboBox.SelectedIndexChanged += (_, _) =>
        {
            if (!ReadOnly || !_dropDown.Visible) RememberReadOnlyValue();
            UpdateItemImages();
            SelectedValueChanged?.Invoke(this, EventArgs.Empty);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        };
        _comboBox.GotFocus += (_, _) => InvalidateChrome();
        _comboBox.LostFocus += (_, _) => InvalidateChrome();
        _comboBox.Validated += (_, _) => CommitText();
        _comboBox.ManagedKeyHandler = keyData =>
        {
            if (_dropDown.Visible) return _dropDownSurface.HandleKey(keyData);
            if (keyData == (Keys.Alt | Keys.Down) || keyData == Keys.F4)
            {
                ShowManagedDropDown();
                return true;
            }
            return false;
        };
        _comboBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None || _dropDown.Visible) return;
            CommitText();
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
        _comboBox.SelectionChangeCommitted += (_, _) =>
        {
            _userEditPending = false;
            if (ReadOnly) RestoreReadOnlyValue();
            else SelectionChangeCommitted?.Invoke(this, EventArgs.Empty);
        };
        _comboBox.DrawItem += DrawItem;
        _comboBox.Format += (_, eventArgs) => eventArgs.Value = GetItemText(eventArgs.ListItem);
        _comboBox.HandleCreated += (_, _) =>
        {
            NativeControlTheme.ApplyComboBox(_comboBox, Theme.IsDark);
            CommitDropDownWidth();
        };
        OnThemeChanged();
        LayoutEditor();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        // The outer HWND exists at this point, but on first creation the native child ComboBox has
        // not yet entered CreateControl. Configure auto-complete in that narrow window so WinForms
        // does not need to recreate its edit child. Keep the managed configuration for the whole
        // control lifetime: outer Handle recreation parks the native child and does not erase it.
        if (!_autoCompleteInitialized)
        {
            ConfigureAutoComplete();
            _autoCompleteInitialized = _autoCompleteConfigured;
        }
        base.OnHandleCreated(e);
    }

    private void ConfigureAutoComplete()
    {
        if (_autoCompleteConfigured || IsDisposed || Disposing || !IsHandleCreated ||
            _comboBox.IsDisposed || Parent is { IsHandleCreated: false }) return;
        _comboBox.AutoCompleteSource = _autoCompleteSource;
        _comboBox.AutoCompleteMode = _autoCompleteMode;
        _autoCompleteConfigured = true;
    }

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        var restoreAutoComplete = _autoCompleteConfigured && !_comboBox.IsDisposed;
        var childIndex = -1;
        if (restoreAutoComplete)
        {
            // Shell AutoComplete subclasses the native edit HWND. Remove that subclass under the
            // current stable parent, then detach the ComboBox so the outer RTL Handle transaction
            // cannot park/destroy the edit HWND while Shell still owns its window procedure.
            _comboBox.AutoCompleteMode = AutoCompleteMode.None;
            _autoCompleteConfigured = false;
            childIndex = Controls.GetChildIndex(_comboBox);
            Controls.Remove(_comboBox);
        }

        try { base.OnRightToLeftChanged(e); }
        finally
        {
            if (restoreAutoComplete && !IsDisposed && !Disposing)
            {
                _comboBox.RightToLeft = RightToLeft;
                Controls.Add(_comboBox);
                Controls.SetChildIndex(_comboBox, Math.Min(childIndex, Controls.Count - 1));
                ConfigureAutoComplete();
                LayoutEditor();
            }
        }
    }

    /// <summary>获取或设置编辑区域中的文本；文本不必匹配候选项。</summary>
    [Browsable(true)]
    [Bindable(true)]
    [AllowNull]
    public override string Text
    {
        get => _comboBox.Text;
        set
        {
            var next = value ?? string.Empty;
            if (_comboBox.Text == next) return;
            _userEditPending = false;
            _settingText = true;
            try { _comboBox.Text = next; }
            finally { _settingText = false; }
        }
    }

    /// <summary>获取或设置可输入的最大字符数。</summary>
    [Category("Behavior"), DefaultValue(0)]
    public int MaxLength { get => _comboBox.MaxLength; set => _comboBox.MaxLength = value; }

    /// <summary>获取或设置当前选择文本的起始位置。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionStart { get => _comboBox.SelectionStart; set => _comboBox.SelectionStart = value; }

    /// <summary>获取或设置当前选择的字符数。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionLength { get => _comboBox.SelectionLength; set => _comboBox.SelectionLength = value; }

    /// <summary>获取或替换当前选择的文本。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public string SelectedText { get => _comboBox.SelectedText; set => _comboBox.SelectedText = value ?? string.Empty; }

    /// <summary>选择指定范围的编辑文本。</summary>
    /// <param name="start">从零开始的选择起始位置。</param>
    /// <param name="length">要选择的字符数。</param>
    public void Select(int start, int length) => _comboBox.Select(start, length);

    /// <summary>选择编辑区域中的全部文本。</summary>
    public void SelectAll() => _comboBox.SelectAll();

    /// <summary>获取可供用户选择和自动完成的候选项集合。</summary>
    [Category("Data")]
    [Description("组合框中可供用户选择和自动完成的项目集合。")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ComboBox.ObjectCollection Items => _comboBox.Items;

    /// <summary>获取或设置候选项数据源。</summary>
    [Category("Data"), DefaultValue(null)]
    [AttributeProvider(typeof(IListSource))]
    public object? DataSource
    {
        get => _dataSource;
        set
        {
            if (ReferenceEquals(_dataSource, value)) return;
            _dataSource = value;
            _sourceList = _dataView.Bind(value);
            SynchronizeSourceItems();
        }
    }

    /// <summary>获取或设置候选项用于显示的成员，支持点分隔的嵌套路径。</summary>
    [Category("Data"), DefaultValue("")]
    public string DisplayMember
    {
        get => _displayMember;
        set
        {
            _displayMember = value ?? string.Empty;
            var selectedIndex = _comboBox.SelectedIndex;
            if (selectedIndex >= 0)
            {
                var selected = _comboBox.SelectedItem;
                _comboBox.SelectedIndex = -1;
                _comboBox.SelectedItem = selected;
            }
            _comboBox.Invalidate();
        }
    }

    /// <summary>获取或设置候选项作为选中值的成员。</summary>
    [Category("Data"), DefaultValue("")]
    public string ValueMember { get => _valueMember; set => _valueMember = value ?? string.Empty; }

    /// <summary>获取或设置候选项图标列表。</summary>
    [Category("Appearance"), DefaultValue(null)]
    [Description("提供候选项图标的图像列表。")]
    public ImageList? ImageList { get => _itemImages.ImageList; set => _itemImages.ImageList = value; }

    /// <summary>获取或设置候选项图标键的成员路径，优先于 ImageIndexMember。</summary>
    [Category("Data"), DefaultValue("")]
    [Description("候选项中用于解析 ImageList 图标键的成员路径。")]
    public string ImageKeyMember { get => _itemImages.ImageKeyMember; set => _itemImages.ImageKeyMember = value; }

    /// <summary>获取或设置候选项图标索引的成员路径。</summary>
    [Category("Data"), DefaultValue("")]
    [Description("候选项中用于解析 ImageList 图标索引的成员路径。")]
    public string ImageIndexMember { get => _itemImages.ImageIndexMember; set => _itemImages.ImageIndexMember = value; }

    /// <summary>获取或设置当前选中的候选项。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem { get => _comboBox.SelectedItem; set => _comboBox.SelectedItem = value; }

    /// <summary>获取或设置当前选中候选项的索引。</summary>
    [Category("Data"), DefaultValue(-1)]
    public int SelectedIndex { get => _comboBox.SelectedIndex; set => _comboBox.SelectedIndex = value; }

    /// <summary>获取或设置当前选中候选项对应的值；设置为 null 会清除选择。</summary>
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
                _comboBox.SelectedIndex = -1;
                return;
            }

            var items = _comboBox.Items.Cast<object>().ToArray();
            var index = Array.FindIndex(items,
                item => ReferenceEquals(DataMemberResolver.Resolve(item, ValueMember), value));
            if (index < 0)
                index = Array.FindIndex(items, item => Equals(DataMemberResolver.Resolve(item, ValueMember), value));
            _comboBox.SelectedIndex = index;
        }
    }

    /// <summary>获取或设置是否允许查看、聚焦和复制但禁止用户编辑或选择新值。</summary>
    [Category("Behavior"), DefaultValue(false)]
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value) return;
            _readOnly = value;
            _comboBox.ReadOnly = value;
            if (value) _userEditPending = false;
            RememberReadOnlyValue();
            Cursor = value ? Cursors.Default : Cursors.Hand;
        }
    }

    /// <summary>获取或设置自动完成的显示方式。</summary>
    [Category("Behavior"), DefaultValue(AutoCompleteMode.SuggestAppend)]
    public AutoCompleteMode AutoCompleteMode
    {
        get => _autoCompleteMode;
        set
        {
            _autoCompleteMode = value;
            if (_autoCompleteConfigured) _comboBox.AutoCompleteMode = value;
        }
    }

    /// <summary>获取或设置自动完成候选项的来源。</summary>
    [Category("Behavior"), DefaultValue(AutoCompleteSource.ListItems)]
    public AutoCompleteSource AutoCompleteSource
    {
        get => _autoCompleteSource;
        set
        {
            _autoCompleteSource = value;
            if (_autoCompleteConfigured) _comboBox.AutoCompleteSource = value;
        }
    }

    /// <summary>获取用于 CustomSource 自动完成的字符串集合。</summary>
    [Category("Data")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public AutoCompleteStringCollection AutoCompleteCustomSource => _comboBox.AutoCompleteCustomSource;

    /// <summary>获取或设置下拉列表最多显示的项目数。</summary>
    [Category("Behavior"), DefaultValue(8)]
    public int MaxDropDownItems { get => _comboBox.MaxDropDownItems; set => _comboBox.MaxDropDownItems = value; }

    /// <summary>获取或设置下拉列表的宽度。</summary>
    [Category("Layout"), DefaultValue(0)]
    public int DropDownWidth
    {
        get => _dropDownWidth;
        set
        {
            _dropDownWidth = Math.Max(0, value);
            CommitDropDownWidth();
        }
    }

    /// <summary>获取或设置下拉列表是否已展开。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DroppedDown
    {
        get => _dropDown.Visible;
        set { if (value) ShowManagedDropDown(); else CloseManagedDropDown(); }
    }

    /// <summary>获取或设置组合框是否以错误状态显示。</summary>
    [Category("Appearance"), DefaultValue(false)]
    public bool HasError
    {
        get => ValidationState == ModernValidationState.Error;
        set { if (value) ValidationState = ModernValidationState.Error; else if (HasError) ValidationState = ModernValidationState.None; }
    }

    /// <summary>当前选中的候选项发生变化时发生。</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>用户完成一次有效的自由文本编辑时发生；程序赋值不会引发。</summary>
    public event EventHandler? TextCommitted;

    /// <summary>当前选中候选项对应的 SelectedValue 发生变化时发生。</summary>
    public event EventHandler? SelectedValueChanged;

    /// <summary>用户从下拉列表提交一个候选项时发生。</summary>
    public event EventHandler? SelectionChangeCommitted;

    /// <summary>下拉列表展开时发生。</summary>
    public event EventHandler? DropDown;

    /// <summary>下拉列表关闭时发生。</summary>
    public event EventHandler? DropDownClosed;

    /// <summary>获取内部原生组合框，以便高级 WinForms 绑定场景使用。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ComboBox InnerComboBox => _comboBox;

    protected override AccessibleObject CreateAccessibilityInstance()
    {
        _comboBox.AccessibleName = AccessibleName;
        _comboBox.AccessibleDescription = AccessibleDescription;
        return _comboBox.AccessibilityObject;
    }

    protected override void SynchronizeValidationAccessibility()
    {
        base.SynchronizeValidationAccessibility();
        if (_comboBox is not null) _comboBox.AccessibleDescription = AccessibleDescription;
    }

    private void CommitText()
    {
        if (ReadOnly || !_userEditPending) return;
        _userEditPending = false;
        if (_hasCommittedText && string.Equals(_lastCommittedText, Text, StringComparison.Ordinal)) return;
        _hasCommittedText = true;
        _lastCommittedText = Text;
        TextCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void RememberReadOnlyValue()
    {
        if (_restoringReadOnlyValue) return;
        _readOnlySelectedItem = _comboBox.SelectedItem;
        _readOnlyText = _comboBox.Text;
    }

    private void RestoreReadOnlyValue()
    {
        if (!ReadOnly || _restoringReadOnlyValue) return;
        _restoringReadOnlyValue = true;
        try
        {
            _comboBox.SelectedItem = _readOnlySelectedItem;
            _comboBox.Text = _readOnlyText;
            CloseManagedDropDown();
        }
        finally { _restoringReadOnlyValue = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataView.Dispose();
            _itemImages.Dispose();
            _comboBox.ManagedKeyHandler = null;
            _dropDown.Dispose();
        }
        base.Dispose(disposing);
    }

}
