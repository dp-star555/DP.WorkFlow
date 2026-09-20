using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.Localization;
using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// WinForms 节点参数面板。
/// <para>共享层负责属性发现、元数据和类型转换，本控件只负责生成平台原生编辑器。</para>
/// </summary>
public sealed partial class WorkflowPropertyPanel : UserControl
{
    /// <summary>仅用于维护属性分类和当前选择的逻辑树；实际参数行显示在滚动内容区。</summary>
    private readonly TreeView _propertyTree = new();

    /// <summary>搜索防抖计时器，避免每输入一个字符就立即重建全部动态参数行。</summary>
    private readonly System.Windows.Forms.Timer _searchTimer;
    private readonly HashSet<string> _collapsedCategories = new(StringComparer.Ordinal);
    private WorkflowDesignerSession? _session;
    private WorkflowPropertyInspectorModel? _model;
    private string? _startNodeId;
    private bool _building;
    private bool _editing;
    private bool _hideScriptProperty;
    private Control? _selectedRow;
    private readonly ModernPropertyGrid.WinForms.ModernPropertyGrid _modernGrid;
    private readonly FlowLayoutPanel _modernActions;
    private readonly TableLayoutPanel _modernHost;
    private WorkflowPropertyObjectAdapter? _modernAdapter;
    private string? _modernSchemaKey;

    /// <summary>初始化节点属性编辑面板。</summary>
    public WorkflowPropertyPanel()
    {
        InitializeComponent();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        rootLayout.Visible = false;
        // 属性表必须显式使用宿主深色主题；通用递归配色无法改变其自绘卡片调色板。
        _modernGrid = new ModernPropertyGrid.WinForms.ModernPropertyGrid
        {
            Dock = DockStyle.Fill,
            Theme = ModernTheme.Dark
        };
        _modernActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(8, 5, 8, 3),
            Visible = false
        };
        _modernHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        _modernHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _modernHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _modernHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _modernHost.Controls.Add(_modernActions, 0, 0);
        _modernHost.Controls.Add(_modernGrid, 0, 1);
        Controls.Add(_modernHost);
        _modernHost.BringToFront();
        _modernGrid.ValidationFailed += (_, eventArgs) => EditError?.Invoke(this, eventArgs.Exception.Message);
        _modernGrid.RegisterEditor(new WorkflowEditorProvider(
            entry => entry.EditorKind == WorkflowPropertyEditorKind.WorkflowInput,
            entry => CreateWorkflowInputEditor(entry)));
        _modernGrid.RegisterEditor(new WorkflowEditorProvider(
            entry => string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FilePath, StringComparison.Ordinal)
                || string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FolderPath, StringComparison.Ordinal),
            CreatePathEditor));
        _modernGrid.RegisterEditor(new WorkflowEditorProvider(
            entry => entry.EditorKind == WorkflowPropertyEditorKind.Script,
            CreateScriptLauncher));
        _modernGrid.RegisterEditor(new WorkflowEditorProvider(
            entry => entry.EditorKind == WorkflowPropertyEditorKind.Structured,
            CreateStructuredLauncher));

        _searchTimer = new System.Windows.Forms.Timer { Interval = 180 };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Rebuild();
        };
        _search.TextChanged += (_, _) =>
        {
            _clearSearch.Visible = _search.TextLength > 0;
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _search.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Escape || _search.TextLength == 0) return;
            _search.Clear();
            eventArgs.SuppressKeyPress = true;
        };
        _clearSearch.Click += (_, _) =>
        {
            _search.Clear();
            _search.Focus();
        };
        _clearSearch.Visible = false;
        _propertyTree.AfterSelect += (_, eventArgs) =>
        {
            var selectedNode = eventArgs.Node;
            _details.Text = selectedNode?.Tag is WorkflowPropertyEntry entry
                ? $"{entry.DisplayName}\r\n{entry.Description}"
                : selectedNode?.Text ?? string.Empty;
        };
        _content.SizeChanged += (_, _) => ResizeRows();
    }

    /// <summary>获取或设置现代属性表格使用的本地化上下文。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ILocalizationContext LocalizationContext
    {
        get => _modernGrid.LocalizationContext;
        set => _modernGrid.LocalizationContext = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>获取或设置业务属性名称、分类、说明、单位和选项的呈现 Provider。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IPropertyPresentationProvider PropertyPresentationProvider
    {
        get => _modernGrid.PresentationProvider;
        set => _modernGrid.PresentationProvider = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>在参数面板及其子编辑器中统一提供 Ctrl+F 搜索快捷键。</summary>
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData != (Keys.Control | Keys.F)) return base.ProcessCmdKey(ref message, keyData);
        _search.Focus();
        _search.SelectAll();
        return true;
    }

    /// <summary>获取或设置设计会话。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
                return;
            _session = value;
            RecreateModel();
        }
    }

    /// <summary>获取或设置绑定分析使用的流程入口。</summary>
    public string? EntryNodeId
    {
        get => _startNodeId;
        set
        {
            if (_startNodeId == value)
                return;
            _startNodeId = value;
            RecreateModel();
        }
    }

    /// <summary>在专用脚本页面存在时隐藏参数表中的脚本正文。</summary>
    public bool HideScriptProperty
    {
        get => _hideScriptProperty;
        set
        {
            if (_hideScriptProperty == value) return;
            _hideScriptProperty = value;
            Rebuild();
        }
    }

    /// <summary>专用展示区域已承载 Block 等操作时隐藏重复快捷按钮。</summary>
    public bool HideSpecialActions { get; set; }

    /// <summary>属性转换或绑定提交失败时发生。</summary>
    public event EventHandler<string>? EditError;

    /// <summary>请求打开 Block 映射集合编辑器时发生。</summary>
    public event EventHandler<IWorkflowBlockMappingNode>? BlockMappingEditRequested;

    /// <summary>当 Session 或流程入口变化时释放旧检查器并创建新的共享属性模型。</summary>
    private void RecreateModel()
    {
        DisposeModel();
        _modernAdapter = null;
        _modernSchemaKey = null;
        if (_session is not null && !string.IsNullOrWhiteSpace(_startNodeId))
        {
            _model = new WorkflowPropertyInspectorModel(_session, _startNodeId);
            _model.Changed += OnModelChanged;
        }
        Rebuild();
    }

    /// <summary>执行 Dispose Model 相关处理。</summary>
    private void DisposeModel()
    {
        if (_model is null)
            return;
        _model.Changed -= OnModelChanged;
        _model.Dispose();
        _model = null;
    }

    /// <summary>处理“Model Changed”事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || _editing)
            return;
        if (InvokeRequired)
            BeginInvoke(Rebuild);
        else
            Rebuild();
    }

    /// <summary>
    /// 根据当前节点、搜索文本和条件可见性完整重建动态参数行。
    /// 使用 _building 防止控件初始化事件再次触发递归重建。
    /// </summary>
    private void Rebuild()
    {
        if (_modernGrid is not null)
        {
            RebuildModernGrid();
            return;
        }
        if (_building)
            return;
        _building = true;
        try
        {
            _content.SuspendLayout();
            _content.Controls.Clear();
            _propertyTree.Nodes.Clear();
            _selectedRow = null;
            if (_model?.SelectedNode is null)
            {
                _content.Controls.Add(CreateHint("选择节点以编辑属性"));
                return;
            }
            var availableEntries = _model.Entries
                .Where(entry => !HideScriptProperty || entry.Name != nameof(IWorkflowScriptNode.Script))
                .ToArray();
            var visibleEntries = availableEntries.Where(MatchesSearch).ToArray();
            _searchSummary.Text = _search.TextLength == 0
                ? $"{availableEntries.Length} 项"
                : $"{visibleEntries.Length}/{availableEntries.Length}";
            _content.Controls.Add(CreateHeader(_model.SelectedNode.Title, _model.SelectedNode.NodeType));
            if (visibleEntries.Length == 0)
            {
                _content.Controls.Add(CreateHint("没有匹配的参数\r\n请尝试名称、分类或说明中的关键词"));
                return;
            }
            _content.Controls.Add(CreateColumnHeader());
            if (!HideSpecialActions && _model.SelectedNode is IWorkflowBlockMappingNode block)
            {
                var mappingButton = new Button
                {
                    Text = "编辑输入/输出映射…",
                    Height = 34,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = ForeColor,
                    BackColor = Color.FromArgb(30, 64, 94),
                    Margin = new Padding(0, 0, 0, 6)
                };
                mappingButton.Click += (_, _) => BlockMappingEditRequested?.Invoke(this, block);
                _content.Controls.Add(mappingButton);
            }
            AddOutputPortVisibilityEditors();
            string? category = null;
            foreach (var entry in visibleEntries)
            {
                if (category != entry.Category)
                {
                    category = entry.Category;
                    _content.Controls.Add(CreateCategory(category, visibleEntries.Count(item => item.Category == category)));
                }
                if (!_collapsedCategories.Contains(entry.Category))
                    _content.Controls.Add(CreatePropertyRow(entry));
            }
        }
        finally
        {
            _content.ResumeLayout();
            _building = false;
            ApplyFixedStyle(this);
            ApplyParameterStyles();
            ResizeRows();
        }
    }

    /// <summary>把共享 DP.WorkFlow 属性模型适配到独立 ModernPropertyGrid。</summary>
    private void RebuildModernGrid()
    {
        if (_building) return;
        _building = true;
        try
        {
            _modernActions.SuspendLayout();
            _modernActions.Controls.Clear();
            if (_model?.SelectedNode is null)
            {
                _modernAdapter = null;
                _modernSchemaKey = null;
                _modernGrid.SelectedObject = null;
                _modernActions.Visible = false;
                return;
            }

            if (!HideSpecialActions && _model.SelectedNode is IWorkflowBlockMappingNode block)
            {
                var mappingButton = new Button { Text = "编辑输入/输出映射…", AutoSize = true, FlatStyle = FlatStyle.Flat };
                mappingButton.Click += (_, _) => BlockMappingEditRequested?.Invoke(this, block);
                _modernActions.Controls.Add(mappingButton);
            }
            AddModernOutputPortEditors();
            _modernActions.Visible = _modernActions.Controls.Count > 0;
            var schemaKey = CreateModernSchemaKey();
            if (_modernAdapter is null)
            {
                _modernAdapter = new WorkflowPropertyObjectAdapter(
                    _model,
                    IsModernPropertyVisible,
                    CommitModernValue);
                _modernSchemaKey = schemaKey;
                _modernGrid.SelectedObject = _modernAdapter;
            }
            else if (!string.Equals(_modernSchemaKey, schemaKey, StringComparison.Ordinal))
            {
                _modernSchemaKey = schemaKey;
                _modernGrid.RefreshProperties();
            }
            else
            {
                // 属性结构未改变时只同步值，保留编辑器、焦点、滚动位置和原生句柄。
                foreach (var entry in _model.Entries.Where(IsModernPropertyVisible))
                    _modernGrid.RefreshProperty(entry.Name);
            }
        }
        finally
        {
            _modernActions.ResumeLayout();
            ApplyFixedStyle(_modernActions);
            _building = false;
        }
    }

    private bool IsModernPropertyVisible(WorkflowPropertyEntry entry) =>
        !HideScriptProperty || entry.Name != nameof(IWorkflowScriptNode.Script);

    private string CreateModernSchemaKey() => (_model!.SelectedNode?.Id ?? string.Empty) + "\u001d" + string.Join("\u001f", _model.Entries
        .Where(IsModernPropertyVisible)
        .Select(entry => string.Join("\u001e",
            entry.Name,
            entry.ValueType.AssemblyQualifiedName,
            entry.EditorKind,
            entry.DisplayName,
            entry.Category,
            entry.Description,
            entry.EditorKey,
            entry.EditorFilter,
            entry.EditorDialogTitle,
            entry.EditorCheckExists,
            entry.IsReadOnly)));

    private void CommitModernValue(WorkflowPropertyEntry entry, object? value)
    {
        if (_model is null) return;
        var previousKeys = _model.Entries.Where(IsModernPropertyVisible).Select(item => item.Name).ToArray();
        try
        {
            _editing = true;
            _model.SetValue(entry, value);
        }
        finally
        {
            _editing = false;
        }
        var currentKeys = _model.Entries.Where(IsModernPropertyVisible).Select(item => item.Name).ToArray();
        if (previousKeys.SequenceEqual(currentKeys, StringComparer.Ordinal))
            _modernGrid.RefreshProperty(entry.Name);
        else if (IsHandleCreated)
            BeginInvoke(Rebuild);
    }

    private void AddModernOutputPortEditors()
    {
        if (_session is null || _model?.SelectedNode is null) return;
        var outputs = _session.GetDeclaredPorts(_model.SelectedNode.Id, WorkflowPortDirection.Output);
        if (outputs.Count <= 1) return;
        var canvasNode = _session.Canvas.Nodes.First(item => item.Node.Id == _model.SelectedNode.Id);
        foreach (var port in outputs)
        {
            var check = new CheckBox
            {
                Text = $"启用输出 {port.Key}",
                Checked = !canvasNode.HiddenOutputPorts.Contains(port.Key),
                AutoSize = true,
                Margin = new Padding(8, 6, 4, 4)
            };
            check.CheckedChanged += (_, _) =>
            {
                if (!_building) _session.SetOutputPortVisible(canvasNode.Node.Id, port.Key, check.Checked);
            };
            _modernActions.Controls.Add(check);
        }
    }

    private Control CreateScriptLauncher(WorkflowPropertyEntry entry)
    {
        var button = new Button { Text = "打开智能脚本编辑器…", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        button.Click += (_, _) => EditScript(entry);
        return button;
    }

    private Control CreateStructuredLauncher(WorkflowPropertyEntry entry)
    {
        var button = new Button { Text = "编辑集合/对象…", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        button.Click += (_, _) => EditStructuredValue(entry);
        return button;
    }

    /// <summary>建立仅供分类和说明定位使用的逻辑属性树。</summary>
    /// <param name="entries">“entries”参数。</param>
    private void BuildPropertyTree(IReadOnlyList<WorkflowPropertyEntry> entries)
    {
        foreach (var category in entries.GroupBy(entry => entry.Category, StringComparer.Ordinal))
        {
            var categoryNode = new TreeNode(category.Key);
            foreach (var entry in category)
                categoryNode.Nodes.Add(new TreeNode(entry.DisplayName) { Tag = entry });
            _propertyTree.Nodes.Add(categoryNode);
            categoryNode.Expand();
        }
        if (_propertyTree.Nodes.Count > 0 && _propertyTree.Nodes[0].Nodes.Count > 0)
            _propertyTree.SelectedNode = _propertyTree.Nodes[0].Nodes[0];
    }

    /// <summary>为可隐藏的动态输出端口追加复选框，并在提交时同步清理失效连线。</summary>
    private void AddOutputPortVisibilityEditors()
    {
        if (_session is null || _model?.SelectedNode is null) return;
        var outputs = _session.GetDeclaredPorts(_model.SelectedNode.Id, WorkflowPortDirection.Output);
        if (outputs.Count <= 1) return;
        _content.Controls.Add(CreateCategory("输出端口", outputs.Count));
        var canvasNode = _session.Canvas.Nodes.First(item => item.Node.Id == _model.SelectedNode.Id);
        foreach (var port in outputs)
        {
            var check = new CheckBox
            {
                Text = $"显示并启用 {port.Key}",
                Checked = !canvasNode.HiddenOutputPorts.Contains(port.Key),
                AutoSize = false,
                Height = 30,
                ForeColor = ForeColor,
                BackColor = BackColor,
                Margin = new Padding(6, 1, 0, 1)
            };
            check.CheckedChanged += (_, _) =>
            {
                if (!_building)
                    _session.SetOutputPortVisible(canvasNode.Node.Id, port.Key, check.Checked);
            };
            _content.Controls.Add(check);
        }
    }

    /// <summary>创建Column Header。</summary>
    /// <returns>返回处理结果。</returns>
    private Control CreateColumnHeader()
    {
        var header = new TableLayoutPanel
        {
            Name = "propertyColumnHeader", Height = 30, Margin = new Padding(0, 0, 0, 2), ColumnCount = 2, RowCount = 1,
            BackColor = Color.FromArgb(30, 41, 59), Padding = new Padding(8, 3, 8, 3)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        header.Controls.Add(new Label { Text = "属性", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        header.Controls.Add(new Label { Text = "值", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        return header;
    }

    /// <summary>创建标准“属性名称/值”两列表格行，并递归连接参数说明事件。</summary>
    /// <param name="entry">“entry”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreatePropertyRow(WorkflowPropertyEntry entry)
    {
        var row = new TableLayoutPanel
        {
            Height = 38,
            MinimumSize = new Size(0, 38),
            Margin = new Padding(0, 0, 0, 2),
            BackColor = Color.FromArgb(37, 37, 38),
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8, 5, 6, 5),
            Tag = entry.Category
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        row.Controls.Add(new Label
        {
            Text = entry.DisplayName,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(148, 163, 184)
        }, 0, 0);
        row.Controls.Add(CreateEditor(entry), 1, 0);
        WireDetailsEvents(row, entry);
        return row;
    }

    /// <summary>
    /// 按显式 EditorKey 和共享层推断出的 EditorKind 选择具体 WinForms 编辑器。
    /// 这里是新增普通参数编辑器时最主要的平台扩展入口。
    /// </summary>
    /// <param name="entry">“entry”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreateEditor(WorkflowPropertyEntry entry)
    {
        if (entry.EditorKind == WorkflowPropertyEditorKind.ReadOnly)
            return EditorLabel(Convert.ToString(entry.Value) ?? string.Empty);
        if (entry.EditorKind == WorkflowPropertyEditorKind.Boolean)
        {
            var checkBox = new CheckBox
            {
                Checked = entry.Value is true,
                Dock = DockStyle.Fill,
                Text = entry.Value is true ? "已启用" : "已停用",
                ForeColor = ForeColor
            };
            checkBox.CheckedChanged += (_, _) =>
            {
                checkBox.Text = checkBox.Checked ? "已启用" : "已停用";
                TryEdit(() => _model!.SetValue(entry, checkBox.Checked));
            };
            return checkBox;
        }
        if (entry.EditorKind == WorkflowPropertyEditorKind.Enum)
        {
            var coreType = Nullable.GetUnderlyingType(entry.ValueType) ?? entry.ValueType;
            var combo = EditorCombo();
            combo.Items.AddRange(Enum.GetValues(coreType).Cast<object>().ToArray());
            combo.SelectedItem = entry.Value;
            combo.SelectedValueChanged += (_, _) =>
            {
                if (combo.SelectedItem is not null)
                    TryEdit(() => _model!.SetValue(entry, combo.SelectedItem), rebuild: true);
            };
            return combo;
        }
        if (string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FilePath, StringComparison.Ordinal)
            || string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FolderPath, StringComparison.Ordinal))
            return CreatePathEditor(entry);
        if (entry.EditorKind == WorkflowPropertyEditorKind.WorkflowInput)
            return CreateWorkflowInputEditor(entry);
        if (entry.EditorKind == WorkflowPropertyEditorKind.Script)
        {
            var button = new Button { Text = "打开智能脚本编辑器…", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
            button.Click += (_, _) => EditScript(entry);
            return button;
        }
        if (entry.EditorKind == WorkflowPropertyEditorKind.Structured)
        {
            var button = new Button { Text = "编辑集合/对象…", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
            button.Click += (_, _) => EditStructuredValue(entry);
            return button;
        }

        var text = EditorText(Convert.ToString(entry.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        text.Validated += (_, _) => TryEdit(() => _model!.SetValue(entry, text.Text));
        return text;
    }

    /// <summary>创建Path Editor。</summary>
    /// <param name="entry">“entry”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreatePathEditor(WorkflowPropertyEntry entry)
    {
        var text = EditorText(Convert.ToString(entry.Value) ?? string.Empty);
        var browse = new Button
        {
            Text = "…",
            Dock = DockStyle.Fill,
            Width = 34,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 0, 0, 0),
            AccessibleName = $"浏览{entry.DisplayName}"
        };
        text.Validated += (_, _) => TryEdit(() => _model!.SetValue(entry, text.Text));
        browse.Click += (_, _) =>
        {
            if (string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FilePath, StringComparison.Ordinal))
            {
                using var dialog = new OpenFileDialog
                {
                    Title = string.IsNullOrWhiteSpace(entry.EditorDialogTitle) ? $"选择{entry.DisplayName}" : entry.EditorDialogTitle,
                    Filter = string.IsNullOrWhiteSpace(entry.EditorFilter) ? "所有文件|*.*" : entry.EditorFilter,
                    CheckFileExists = entry.EditorCheckExists,
                    Multiselect = false,
                    FileName = text.Text
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                text.Text = dialog.FileName;
            }
            else
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = string.IsNullOrWhiteSpace(entry.EditorDialogTitle) ? $"选择{entry.DisplayName}" : entry.EditorDialogTitle,
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                    SelectedPath = Directory.Exists(text.Text) ? text.Text : string.Empty
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                text.Text = dialog.SelectedPath;
            }
            TryEdit(() => _model!.SetValue(entry, text.Text));
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(browse, 1, 0);
        return layout;
    }

    /// <summary>执行 Edit Script 相关处理。</summary>
    /// <param name="entry">“entry”参数。</param>
    private void EditScript(WorkflowPropertyEntry entry)
    {
        var page = WorkflowScriptEditorPageModel.CreateBuffer(Convert.ToString(entry.Value));
        using var workspace = new WorkflowCSharpScriptEditorControl
        {
            Dock = DockStyle.Fill,
            Page = page
        };
        using var dialog = new Form
        {
            Text = $"C# 脚本 - {entry.DisplayName}",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(940, 620),
            MinimumSize = new Size(760, 480),
            ShowInTaskbar = false
        };
        var ok = new ModernButton
        {
            Text = "确定",
            ButtonType = ModernButtonType.Primary,
            DialogResult = DialogResult.None,
            Size = new Size(76, 32),
            Margin = new Padding(4)
        };
        var cancel = new ModernButton
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(76, 32),
            Margin = new Padding(4)
        };
        ok.Click += (_, _) =>
        {
            if (workspace.CompilationState != WorkflowScriptCompilationState.Succeeded)
            {
                MessageBox.Show(dialog, "保存前必须点击“编译”，并确保当前 C# 代码编译通过。", "尚未编译", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(4, 2, 8, 2)
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(ok);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        main.Controls.Add(workspace, 0, 0);
        main.Controls.Add(actions, 0, 1);
        dialog.Controls.Add(main);
        dialog.CancelButton = cancel;
        ApplyFixedStyle(dialog);
        dialog.Shown += (_, _) => workspace.FocusEditor();
        if (dialog.ShowDialog(this) == DialogResult.OK)
            TryEdit(() => _model!.SetValue(entry, page.Script), rebuild: true);
    }

    /// <summary>执行 Edit Structured Value 相关处理。</summary>
    /// <param name="entry">“entry”参数。</param>
    private void EditStructuredValue(WorkflowPropertyEntry entry)
    {
        if (WorkflowCollectionTableModel.TryCreate(entry, out var table) && table is not null)
        {
            EditCollectionTable(table, entry.DisplayName);
            return;
        }
        using var editor = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9),
            Text = entry.GetStructuredJson()
        };
        using var dialog = new Form
        {
            Text = $"编辑 {entry.DisplayName}",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(720, 540),
            MinimizeBox = false,
            ShowInTaskbar = false
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true });
        buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true });
        dialog.Controls.Add(editor);
        dialog.Controls.Add(buttons);
        ApplyFixedStyle(dialog);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            TryEdit(() => _model!.SetStructuredJson(entry, editor.Text), rebuild: true);
    }

    /// <summary>执行 Edit Collection Table 相关处理。</summary>
    /// <param name="table">“table”参数。</param>
    /// <param name="displayName">“displayName”参数。</param>
    private void EditCollectionTable(WorkflowCollectionTableModel table, string displayName)
    {
        using var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            RowHeadersVisible = true,
            BackgroundColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(226, 232, 240)
        };
        foreach (var column in table.Columns)
            grid.Columns.Add(column, column);
        foreach (var row in table.Rows)
            grid.Rows.Add(row.Cast<object>().ToArray());
        using var dialog = new Form
        {
            Text = $"表格编辑 - {displayName}",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(820, 560),
            ShowInTaskbar = false
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true });
        buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true });
        dialog.Controls.Add(grid);
        dialog.Controls.Add(buttons);
        ApplyFixedStyle(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var rows = grid.Rows.Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => (IReadOnlyList<string>)row.Cells.Cast<DataGridViewCell>()
                .Select(cell => Convert.ToString(cell.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                .ToArray())
            .ToArray();
        TryEdit(() => _model!.ApplyCollectionTable(table, rows), rebuild: true);
    }

    /// <summary>
    /// 创建 WorkflowInput&lt;T&gt; 编辑器：左侧选择 Literal/Binding，右侧显示固定值编辑器
    /// 或强类型树形绑定按钮。切换到绑定模式时立即打开绑定选择窗口。
    /// </summary>
    /// <param name="entry">“entry”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreateWorkflowInputEditor(WorkflowPropertyEntry entry)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        var source = EditorCombo();
        source.Items.AddRange(Enum.GetValues<WorkflowValueSource>().Cast<object>().ToArray());
        source.SelectedItem = entry.GetInputSource();
        source.Margin = new Padding(0, 0, 3, 0);
        panel.Controls.Add(source, 0, 0);

        Control valueEditor;
        if (entry.GetInputSource() == WorkflowValueSource.Binding)
        {
            var candidates = _model!.GetBindingCandidates(entry);
            var current = entry.GetInputBinding();
            var button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "▼  " + (candidates.FirstOrDefault(item => item.ToBindingKey() == current)?.DisplayPath
                    ?? current?.ToString()
                    ?? "选择绑定…"),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = ForeColor
            };
            button.Click += (_, _) =>
            {
                using var dialog = new WorkflowBindingSelectorDialog(candidates, entry.GetInputBinding());
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                    TryEdit(() => _model.SetWorkflowInput(
                        entry, WorkflowValueSource.Binding, null, candidate.ToBindingKey()), rebuild: true);
            };
            valueEditor = button;
        }
        else
        {
            var text = EditorText(Convert.ToString(
                entry.GetInputLiteral(),
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            text.Validated += (_, _) => TryEdit(() => _model!.SetWorkflowInput(
                entry, WorkflowValueSource.Literal, text.Text, null));
            valueEditor = text;
        }
        valueEditor.Margin = Padding.Empty;
        panel.Controls.Add(valueEditor, 1, 0);
        source.SelectedValueChanged += (_, _) =>
        {
            if (source.SelectedItem is not WorkflowValueSource selected || selected == entry.GetInputSource())
                return;
            if (selected == WorkflowValueSource.Literal)
                TryEdit(() => _model!.SetWorkflowInput(entry, selected, entry.GetInputLiteral(), null), rebuild: true);
            else
            {
                var candidates = _model!.GetBindingCandidates(entry);
                if (candidates.Count == 0)
                {
                    EditError?.Invoke(this, "当前节点没有可用的强类型绑定候选。");
                    Rebuild();
                    return;
                }
                using var dialog = new WorkflowBindingSelectorDialog(candidates, entry.GetInputBinding());
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                    TryEdit(() => _model.SetWorkflowInput(entry, selected, null, candidate.ToBindingKey()), rebuild: true);
                else
                    Rebuild();
            }
        };
        return panel;
    }

    /// <summary>统一执行参数转换、错误提示和可选界面重建，避免各编辑器重复异常处理。</summary>
    /// <param name="action">“action”参数。</param>
    /// <param name="rebuild">“rebuild”参数。</param>
    private void TryEdit(Action action, bool rebuild = false)
    {
        if (_building)
            return;
        try
        {
            _editing = true;
            action();
            _editing = false;
            if (rebuild)
                Rebuild();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException
                                          or ArgumentException or OverflowException or System.Text.Json.JsonException)
        {
            _editing = false;
            EditError?.Invoke(this, exception.Message);
            Rebuild();
        }
        finally
        {
            _editing = false;
        }
    }

    private Label CreateHeader(string title, string nodeType) => new()
    {
        Text = $"{title}\r\n{nodeType}",
        Name = "nodeSummaryHeader",
        Height = 58,
        Padding = new Padding(10, 8, 10, 8),
        Font = new Font(Font, FontStyle.Bold),
        ForeColor = ForeColor,
        BackColor = Color.FromArgb(30, 41, 59),
        Margin = new Padding(0, 0, 0, 10)
    };

    /// <summary>创建Category。</summary>
    /// <param name="category">“category”参数。</param>
    /// <param name="count">分类中当前可见的参数数量。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreateCategory(string category, int count)
    {
        var button = new Button
        {
            Name = "propertyCategory",
            AccessibleDescription = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Text = $"{(_collapsedCategories.Contains(category) ? "▶" : "▼")}  {category}    {count}",
            Height = 27,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(56, 189, 248),
            BackColor = BackColor,
            Margin = new Padding(0, 4, 0, 0),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) =>
        {
            var collapsed = _collapsedCategories.Add(category);
            if (!collapsed) _collapsedCategories.Remove(category);
            button.Text = $"{(collapsed ? "▶" : "▼")}  {category}    {count}";
            _content.SuspendLayout();
            foreach (Control control in _content.Controls)
                if (control.Tag is string rowCategory && rowCategory == category)
                    control.Visible = !collapsed;
            _content.ResumeLayout(true);
        };
        return button;
    }

    /// <summary>执行 Matches Search 相关处理。</summary>
    /// <param name="entry">“entry”参数。</param>
    /// <returns>返回处理结果。</returns>
    private bool MatchesSearch(WorkflowPropertyEntry entry)
    {
        var search = _search.Text.Trim();
        return search.Length == 0
            || entry.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (Convert.ToString(entry.Value)?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>显示Details。</summary>
    /// <param name="entry">“entry”参数。</param>
    private void ShowDetails(WorkflowPropertyEntry entry) =>
        _details.Text = $"{entry.DisplayName}  ·  {entry.ValueType.Name}\r\n{(string.IsNullOrWhiteSpace(entry.Description) ? "暂无参数说明。" : entry.Description)}\r\n内部名称：{entry.Name}";

    /// <summary>递归连接参数行及所有子编辑器，使点击任意位置都能更新底部说明。</summary>
    /// <param name="control">“control”参数。</param>
    /// <param name="entry">“entry”参数。</param>
    private void WireDetailsEvents(Control control, WorkflowPropertyEntry entry)
    {
        void Select()
        {
            ShowDetails(entry);
            var row = FindPropertyRow(control);
            if (ReferenceEquals(row, _selectedRow)) return;
            if (_selectedRow is not null) _selectedRow.BackColor = Color.FromArgb(37, 37, 38);
            _selectedRow = row;
            if (_selectedRow is not null) _selectedRow.BackColor = Color.FromArgb(42, 52, 66);
        }
        control.MouseDown += (_, _) => Select();
        control.Enter += (_, _) => Select();
        foreach (Control child in control.Controls) WireDetailsEvents(child, entry);
    }

    private static Control? FindPropertyRow(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
            if (current is TableLayoutPanel { Tag: string }) return current;
        return null;
    }

    private Label CreateHint(string text) => new()
    {
        Text = text,
        Height = 76,
        Padding = new Padding(12),
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.FromArgb(100, 116, 139)
    };

    private Label EditorLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(100, 116, 139)
    };

    private TextBox EditorText(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(15, 23, 42),
        ForeColor = ForeColor
    };

    /// <summary>执行 Editor Combo 相关处理。</summary>
    /// <returns>返回处理结果。</returns>
    private ModernSelect EditorCombo() => new()
    {
        Dock = DockStyle.Fill,
        Theme = ModernTheme.Dark,
        DropDownAnimationDuration = 0
    };

    /// <summary>把 DP.WorkFlow 专用属性编辑器注册到通用 PropertyGrid。</summary>
    /// <param name="predicate">判断属性是否匹配。</param>
    /// <param name="factory">创建对应 WinForms 编辑器。</param>
    private sealed class WorkflowEditorProvider(
        Func<WorkflowPropertyEntry, bool> predicate,
        Func<WorkflowPropertyEntry, Control> factory) : IPropertyEditorProvider
    {
        public int Priority => 100;

        public bool CanEdit(PropertyDescriptor property) =>
            property is WorkflowPropertyDescriptor descriptor && predicate(descriptor.Entry);

        public Control CreateEditor(PropertyEditorContext context) =>
            factory(((WorkflowPropertyDescriptor)context.Property).Entry);
    }

    private sealed class WorkflowPropertyObjectAdapter(
        WorkflowPropertyInspectorModel model,
        Func<WorkflowPropertyEntry, bool> filter,
        Action<WorkflowPropertyEntry, object?> commit) : ICustomTypeDescriptor
    {
        public AttributeCollection GetAttributes() => AttributeCollection.Empty;
        public string? GetClassName() => model.SelectedNode?.NodeType;
        public string? GetComponentName() => model.SelectedNode?.Title;
        public TypeConverter GetConverter() => new TypeConverter();
        public EventDescriptor? GetDefaultEvent() => null;
        public PropertyDescriptor? GetDefaultProperty() => null;
        public object? GetEditor(Type editorBaseType) => null;
        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
        public EventDescriptorCollection GetEvents(Attribute[]? attributes) => EventDescriptorCollection.Empty;
        public object GetPropertyOwner(PropertyDescriptor? propertyDescriptor) => this;
        public PropertyDescriptorCollection GetProperties() => GetProperties(null);
        public PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => new(
            model.Entries.Where(filter)
                .Select(entry => new WorkflowPropertyDescriptor(this, entry, GetType(), commit))
                .Cast<PropertyDescriptor>()
                .ToArray(),
            readOnly: true);

        public WorkflowPropertyEntry? FindEntry(string name) =>
            model.Entries.FirstOrDefault(entry => filter(entry) && string.Equals(entry.Name, name, StringComparison.Ordinal));
    }

    private sealed class WorkflowPropertyDescriptor : PropertyDescriptor
    {
        private readonly WorkflowPropertyObjectAdapter _adapter;
        private readonly WorkflowPropertyEntry _template;
        private readonly Action<WorkflowPropertyEntry, object?> _commit;

        public WorkflowPropertyDescriptor(
            WorkflowPropertyObjectAdapter adapter,
            WorkflowPropertyEntry entry,
            Type componentType,
            Action<WorkflowPropertyEntry, object?> commit)
            : base(entry.Name, CreateAttributes(entry))
        {
            _adapter = adapter;
            _template = entry;
            ComponentType = componentType;
            _commit = commit;
        }

        // 已创建的编辑器继续持有本 Descriptor，但读写会解析模型中的最新 Entry。
        public WorkflowPropertyEntry Entry => _adapter.FindEntry(Name) ?? _template;
        public override Type ComponentType { get; }
        public override bool IsReadOnly => Entry.IsReadOnly;
        public override Type PropertyType => Entry.ValueType;
        public override bool CanResetValue(object component) => false;
        public override object? GetValue(object? component) => Entry.Value;
        public override void ResetValue(object component) { }
        public override void SetValue(object? component, object? value) => _commit(Entry, value);
        public override bool ShouldSerializeValue(object component) => false;

        private static Attribute[] CreateAttributes(WorkflowPropertyEntry entry)
        {
            var attributes = new List<Attribute>
            {
                new DisplayNameAttribute(entry.DisplayName),
                new CategoryAttribute(entry.Category),
                new DescriptionAttribute(entry.Description)
            };
            if (entry.IsReadOnly) attributes.Add(ReadOnlyAttribute.Yes);
            if (!string.IsNullOrWhiteSpace(entry.EditorKey))
                attributes.Add(new PropertyEditorKeyAttribute(entry.EditorKey));
            return attributes.ToArray();
        }
    }

    private static void ApplyFixedStyle(Control root) => WorkflowWinFormsStyle.Apply(root);

    /// <summary>恢复参数面板的层级色，避免通用主题把分类、表头和可选行抹成同一平面。</summary>
    private void ApplyParameterStyles()
    {
        var palette = WorkflowWinFormsStyle.Get();
        BackColor = palette.Window;
        _content.BackColor = palette.Window;
        _details.BackColor = palette.Surface;
        _details.ForeColor = palette.MutedText;
        _details.BorderStyle = BorderStyle.None;
        _searchSummary.ForeColor = palette.MutedText;
        _clearSearch.FlatAppearance.BorderColor = palette.Border;
        foreach (Control control in _content.Controls)
        {
            if (control.Name == "nodeSummaryHeader") control.BackColor = palette.Surface;
            else if (control.Name == "propertyColumnHeader") control.BackColor = Color.FromArgb(45, 45, 48);
            else if (control.Name == "propertyCategory")
            {
                control.BackColor = palette.Window;
                control.ForeColor = palette.Accent;
            }
            else if (control is TableLayoutPanel { Tag: string }) control.BackColor = Color.FromArgb(37, 37, 38);
        }
    }

    /// <summary>执行 Resize Rows 相关处理。</summary>
    private void ResizeRows()
    {
        var width = Math.Max(80, _content.ClientSize.Width - _content.Padding.Horizontal
            - (_content.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        foreach (Control control in _content.Controls)
            control.Width = width;
    }
}
