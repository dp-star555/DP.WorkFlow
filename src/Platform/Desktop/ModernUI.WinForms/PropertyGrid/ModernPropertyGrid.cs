using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

/// <summary>
/// 一个基于 <see cref="TypeDescriptor"/>、支持主题和编辑器扩展的现代 WinForms 属性表格。
/// </summary>
[Description("ModernPropertyGrid 现代属性编辑器")]
[DisplayName("现代属性编辑器")]
[ToolboxBitmap(typeof(ModernPropertyGrid), "Toolbox.Icons.PropertyGrid.bmp")]
[ToolboxItem(true)]
public sealed partial class ModernPropertyGrid : UserControl
{
    private readonly TableLayoutPanel _layout = new BufferedTableLayoutPanel();
    private readonly TableLayoutPanel _searchBar = new BufferedTableLayoutPanel();
    private readonly ModernInput _search = new();
    private readonly Label _summary = new();
    private readonly ModernButton _clearSearch = new();
    private readonly ModernScrollView _scrollViewport = new();
    private readonly FlowLayoutPanel _content = new BufferedFlowLayoutPanel();
    private readonly ModernSplitter _detailsSplitter = new();
    private readonly Label _details = new();
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 160 };
    private readonly List<IPropertyEditorProvider> _providers = [];
    private readonly HashSet<string> _collapsedCategories = new(StringComparer.Ordinal);
    private object? _selectedObject;
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private Control? _selectedRow;
    private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PropertyPresentation> _presentations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PropertyRowPanel> _rows = new(StringComparer.Ordinal);
    private ILocalizationContext _localizationContext = PropertyGridLocalization.CreateContext(CultureInfo.CurrentUICulture);
    private IPropertyPresentationProvider _presentationProvider = DefaultPropertyPresentationProvider.Instance;
    private string? _selectedPropertyKey;
    private bool _building;
    private bool _refreshingEditor;
    private bool _showNumericStepButtons = true;
    private bool _showSearchBar = true;
    private int _lastRowsWidth = -1;
    private int _rebuildVersion;
    private FinalLayoutTransaction? _dpiLayoutTransaction;

    private FinalLayoutTransaction DpiLayoutTransaction =>
        _dpiLayoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalDpiLayout);

    /// <summary>初始化属性表格。</summary>
    public ModernPropertyGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        // Use the same scaling axis as the top-level per-monitor DPI container. Font auto-scaling
        // applies a second, non-uniform transform here (for example 760x450 became 725x478 after a
        // 144 -> 96 round trip), while None prevents this nested ContainerControl from scaling at all.
        AutoScaleMode = AutoScaleMode.Dpi;
        TabStop = true;

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(48)));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _searchBar.Dock = DockStyle.Fill;
        _searchBar.Padding = new Padding(ScaleLogical(8), ScaleLogical(8), ScaleLogical(6), ScaleLogical(7));
        _searchBar.ColumnCount = 3;
        _searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(72)));
        _searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(36)));
        _search.Dock = DockStyle.Fill;
        _search.Margin = Padding.Empty;
        _search.PlaceholderText = null;
        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleRight;
        _clearSearch.Dock = DockStyle.Fill;
        _clearSearch.Margin = new Padding(ScaleLogical(6), 0, 0, 0);
        _clearSearch.ButtonType = ModernButtonType.Text;
        _clearSearch.StrongHoverFeedback = true;
        _clearSearch.Icon = ModernIconKind.Close;
        _clearSearch.AccessibleName = null;
        _searchBar.Controls.Add(_search, 0, 0);
        _searchBar.Controls.Add(_summary, 1, 0);
        _searchBar.Controls.Add(_clearSearch, 2, 0);

        _scrollViewport.Dock = DockStyle.Fill;
        _scrollViewport.ScrollBarGutter = 14;
        _scrollViewport.ScrollBarWidth = 5;
        _scrollViewport.ScrollBarHoverWidth = 7;
        _scrollViewport.MinimumThumbLength = 28;
        _content.AutoScroll = false;
        _content.FlowDirection = FlowDirection.TopDown;
        _content.WrapContents = false;
        _content.Padding = new Padding(ScaleLogical(8));
        _scrollViewport.Content = _content;
        _detailsSplitter.Dock = DockStyle.Fill;
        _detailsSplitter.Margin = Padding.Empty;
        _detailsSplitter.Orientation = Orientation.Horizontal;
        _detailsSplitter.FixedPanel = FixedPanel.Panel2;
        _detailsSplitter.Panel1MinSize = ScaleLogical(64);
        _detailsSplitter.Panel2MinSize = ScaleLogical(44);
        _detailsSplitter.InitialPanel2Size = 82;
        _detailsSplitter.AccessibleName = null;
        _details.Dock = DockStyle.Fill;
        _details.MinimumSize = new Size(0, ScaleLogical(44));
        _details.Padding = new Padding(ScaleLogical(10), ScaleLogical(8), ScaleLogical(10), ScaleLogical(8));
        _details.Text = string.Empty;
        _detailsSplitter.Panel1.Controls.Add(_scrollViewport);
        _detailsSplitter.Panel2.Controls.Add(_details);

        _layout.Controls.Add(_searchBar, 0, 0);
        _layout.Controls.Add(_detailsSplitter, 0, 1);
        Controls.Add(_layout);

        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Rebuild(); };
        _search.TextChanged += (_, _) =>
        {
            _clearSearch.Visible = _search.Text.Length > 0;
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _search.InnerTextBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Escape || _search.Text.Length == 0) return;
            _search.Text = string.Empty;
            eventArgs.SuppressKeyPress = true;
        };
        _clearSearch.Click += (_, _) =>
        {
            _search.Text = string.Empty;
            _search.InnerTextBox.Focus();
        };
        _content.SizeChanged += (_, _) => ResizeRows();
        _clearSearch.Visible = false;
        _localizationContext.Changed += OnLocalizationChanged;
        ApplyLocalization();
        ApplyTheme();
        Rebuild();
    }

    // The parent scales this ContainerControl's outer bounds; its own DPI autoscaler scales the
    // internal tree. Allowing the parent to recurse into that tree applies the same DPI factor twice.
    protected override bool ScaleChildren => false;

    /// <summary>获取或设置要检查的对象；属性通过 <see cref="TypeDescriptor"/> 发现。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (ReferenceEquals(_selectedObject, value)) return;
            if (_selectedObject is INotifyPropertyChanged oldNotifier) oldNotifier.PropertyChanged -= OnObjectPropertyChanged;
            _selectedObject = value;
            _selectedPropertyKey = null;
            if (_selectedObject is INotifyPropertyChanged newNotifier) newNotifier.PropertyChanged += OnObjectPropertyChanged;
            Rebuild();
        }
    }

    /// <summary>获取或设置属性表格主题。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set
        {
            var nextTheme = value ?? throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(_theme, nextTheme)) return;
            var geometryChanged = _theme.PropertyRowHeight != nextTheme.PropertyRowHeight ||
                                  _theme.ControlHeight != nextTheme.ControlHeight ||
                                  _theme.Radius != nextTheme.Radius;
            _theme = nextTheme;
            ApplyTheme();
            if (geometryChanged) Rebuild();
            else RefreshVisualTheme();
        }
    }

    /// <summary>获取或设置当前本地化上下文；切换快照后会按稳定身份重建并恢复页面状态。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ILocalizationContext LocalizationContext
    {
        get => _localizationContext;
        set
        {
            var next = value ?? throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(_localizationContext, next)) return;
            _localizationContext.Changed -= OnLocalizationChanged;
            _localizationContext = next;
            _localizationContext.Changed += OnLocalizationChanged;
            ApplyLocalization();
            Rebuild();
        }
    }

    /// <summary>获取或设置业务属性呈现 Provider。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IPropertyPresentationProvider PresentationProvider
    {
        get => _presentationProvider;
        set
        {
            var next = value ?? throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(_presentationProvider, next)) return;
            _presentationProvider = next;
            Rebuild();
        }
    }

    /// <summary>获取或设置是否显示顶部属性搜索栏。</summary>
    [Category("Appearance"), DefaultValue(true)]
    [Description("属性数量较少时可隐藏搜索栏，减少重复标题和计数信息。")]
    public bool ShowSearchBar
    {
        get => _showSearchBar;
        set
        {
            if (_showSearchBar == value) return;
            _showSearchBar = value;
            _searchBar.Visible = value;
            _layout.RowStyles[0].Height = value ? ScaleLogical(48) : 0;
            PerformLayout();
        }
    }

    /// <summary>获取或设置是否启用分类展开/收起动画。</summary>
    [Category("Behavior"), DefaultValue(true)]
    [Description("展开或收起属性分类时是否播放过渡动画。")]
    public bool AnimateCategoryExpansion { get; set; } = true;

    /// <summary>获取或设置分类展开/收起动画时长（毫秒）。</summary>
    [Category("Behavior"), DefaultValue(180)]
    [Description("属性分类展开或收起动画的持续时间（毫秒）。")]
    public int CategoryAnimationDuration { get; set; } = 180;

    /// <summary>获取或设置数值编辑器是否显示右侧增减按钮。</summary>
    [Category("Appearance"), DefaultValue(true)]
    [Description("隐藏后数值仍可直接输入，也可使用上下方向键步进。")]
    public bool ShowNumericStepButtons
    {
        get => _showNumericStepButtons;
        set
        {
            if (_showNumericStepButtons == value) return;
            _showNumericStepButtons = value;
            Rebuild();
        }
    }

    /// <summary>属性值成功修改后发生。</summary>
    [Description("属性编辑器成功提交属性值后引发。")]
    public event EventHandler<PropertyValueChangedEventArgs>? PropertyValueChanged;

    /// <summary>属性值转换、校验或提交失败时发生。</summary>
    [Description("用户输入未通过属性转换、验证或提交时引发。")]
    public event EventHandler<PropertyValidationFailedEventArgs>? ValidationFailed;

    /// <summary>注册一个自定义编辑器 Provider。</summary>
    public void RegisterEditor(IPropertyEditorProvider provider)
    {
        ModernCompatibility.ThrowIfNull(provider, nameof(provider));
        _providers.Add(provider);
        _providers.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        RefreshProperties();
    }

    /// <summary>重新发现属性并刷新表格结构。</summary>
    public void RefreshProperties() => Rebuild();

    /// <summary>只刷新指定属性的编辑器和值；属性结构变化时返回 false。</summary>
    public bool RefreshProperty(string propertyName)
    {
        ModernCompatibility.ThrowIfNullOrWhiteSpace(propertyName, nameof(propertyName));
        return RefreshEditorValue(propertyName);
    }

    /// <inheritdoc />
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData != (Keys.Control | Keys.F)) return base.ProcessCmdKey(ref message, keyData);
        _search.InnerTextBox.Focus();
        _search.InnerTextBox.SelectAll();
        return true;
    }

    private sealed record PropertyChoice(object Value, string Text);

    private sealed class CategoryBodyState(string category, int expandedHeight)
    {
        public string Category { get; } = category;
        public int ExpandedHeight { get; set; } = expandedHeight;
        public Func<int>? MeasureExpandedHeight { get; init; }
        public int LastWidth { get; set; } = -1;
        public IDisposable? Animation { get; set; }

        public void CancelAnimation()
        {
            Animation?.Dispose();
            Animation = null;
        }
    }

    private static bool IsNumber(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
        or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static bool IsIntegral(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
        or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;

    private static object ConvertNumber(decimal value, Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.Byte => decimal.ToByte(value),
        TypeCode.SByte => decimal.ToSByte(value),
        TypeCode.Int16 => decimal.ToInt16(value),
        TypeCode.UInt16 => decimal.ToUInt16(value),
        TypeCode.Int32 => decimal.ToInt32(value),
        TypeCode.UInt32 => decimal.ToUInt32(value),
        TypeCode.Int64 => decimal.ToInt64(value),
        TypeCode.UInt64 => decimal.ToUInt64(value),
        TypeCode.Single => decimal.ToSingle(value),
        TypeCode.Double => decimal.ToDouble(value),
        _ => value
    };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Control existing in _content.Controls)
            {
                if (existing.Tag is CategoryBodyState state) state.CancelAnimation();
            }
            if (_selectedObject is INotifyPropertyChanged notifier) notifier.PropertyChanged -= OnObjectPropertyChanged;
            _localizationContext.Changed -= OnLocalizationChanged;
            _searchTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
