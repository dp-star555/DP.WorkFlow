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
    private WorkflowDesignerSession? _session;
    private WorkflowPropertyChoiceProvider? _choiceProvider;
    private Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? _additionalProperties;

    /// <summary>领域描述生成的附加属性；与普通属性共用提交及撤销。</summary>
    public Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? AdditionalProperties
    {
        get => _additionalProperties;
        set { _additionalProperties = value; RecreateModel(); }
    }
    private WorkflowPropertyInspectorModel? _model;
    private string? _startNodeId;
    private bool _building;
    private bool _editing;
    private int _inputEditorGeneration;
    private bool _hideScriptProperty;
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
            entry => entry.EditorKind == WorkflowPropertyEditorKind.Action,
            CreateActionEditor));
        _modernGrid.RegisterEditor(new WorkflowEditorProvider(
            entry => entry.EditorKind == WorkflowPropertyEditorKind.Structured,
            CreateStructuredLauncher));
        _modernGrid.RegisterEditor(new WorkflowEditorProvider(
            entry => entry.EditorKind == WorkflowPropertyEditorKind.Choice,
            CreateChoiceEditor));

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

    /// <summary>
    /// 获取或设置候选值提供者。宿主用它把机器配置（例如已发布的逻辑图像源）注入参数面板；
    /// 未设置时候选编辑器退回文本输入，不会因为宿主未装配而无法编辑。
    /// </summary>
    public WorkflowPropertyChoiceProvider? ChoiceProvider
    {
        get => _choiceProvider;
        set
        {
            if (ReferenceEquals(_choiceProvider, value))
                return;
            _choiceProvider = value;
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
            _model = new WorkflowPropertyInspectorModel(_session, _startNodeId, _choiceProvider, _additionalProperties);
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

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || _editing)
            return;
        if (InvokeRequired)
            BeginInvoke(Rebuild);
        else
            Rebuild();
    }

    /// <summary>把共享 DP.WorkFlow 属性模型适配到独立 ModernPropertyGrid。</summary>
    private void Rebuild()
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
                _inputEditorGeneration++;
                _modernGrid.SelectedObject = null;
                _modernActions.Visible = false;
                return;
            }

            if (!HideSpecialActions && _model.SelectedNode is IWorkflowBlockMappingNode block)
            {
                var mappingButton = ActionBarButton("编辑输入/输出映射…");
                mappingButton.Click += (_, _) => BlockMappingEditRequested?.Invoke(this, block);
                _modernActions.Controls.Add(mappingButton);
            }
            AddModernOutputPortEditors();
            _modernActions.Visible = _modernActions.Controls.Count > 0;
            var schemaKey = CreateModernSchemaKey();
            if (_modernAdapter is null)
            {
                _inputEditorGeneration++;
                _modernAdapter = new WorkflowPropertyObjectAdapter(
                    _model,
                    IsModernPropertyVisible,
                    CommitModernValue);
                _modernSchemaKey = schemaKey;
                _modernGrid.SelectedObject = _modernAdapter;
            }
            else if (!string.Equals(_modernSchemaKey, schemaKey, StringComparison.Ordinal))
            {
                _inputEditorGeneration++;
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
            string.Join("\u001a", entry.GroupPath),
            entry.Description,
            entry.EditorKey,
            entry.EditorFilter,
            entry.EditorDialogTitle,
            entry.EditorCheckExists,
            entry.NumberMinimum,
            entry.NumberMaximum,
            entry.IsReadOnly,
            entry.EditorKind == WorkflowPropertyEditorKind.Action ? entry.Value : null,
            entry.EditorKind == WorkflowPropertyEditorKind.Action ? entry.ActionBlockReason : null,
            entry.EditorKind == WorkflowPropertyEditorKind.WorkflowInput ? entry.GetInputSource() : null,
            entry.EditorKind == WorkflowPropertyEditorKind.WorkflowInput ? entry.GetInputBinding() : null,
            entry.EditorKind == WorkflowPropertyEditorKind.WorkflowInput
                ? entry.CanEditInputLiteralAsText ? entry.GetInputLiteral() : entry.GetInputLiteral() is not null
                : null,
            string.Join("\u001c", entry.Choices.Select(choice => choice.Label + "\u001b" + choice.Value)))));

    private void CommitModernValue(WorkflowPropertyEntry entry, object? value)
    {
        if (_model is null) return;
        var previousSchema = CreateModernSchemaKey();
        try
        {
            _editing = true;
            _model.SetValue(entry, value);
        }
        finally
        {
            _editing = false;
        }
        if (string.Equals(previousSchema, CreateModernSchemaKey(), StringComparison.Ordinal))
        {
            // 一次领域操作可能同时改变参数和依赖值，例如清空配置；同步所有值，保留焦点。
            foreach (var current in _model.Entries.Where(IsModernPropertyVisible))
                _modernGrid.RefreshProperty(current.Name);
        }
        else if (IsHandleCreated)
            BeginInvoke(Rebuild);
        else
            Rebuild();
    }

    private void AddModernOutputPortEditors()
    {
        if (_session is null || _model?.SelectedNode is null) return;
        var outputs = _session.GetDeclaredPorts(_model.SelectedNode.Id, WorkflowPortDirection.Output);
        if (outputs.Count <= 1) return;
        var canvasNode = _session.Canvas.Nodes.First(item => item.Node.Id == _model.SelectedNode.Id);
        foreach (var port in outputs)
        {
            var text = $"启用输出 {WorkflowPorts.GetDisplayName(port.Key)}";
            var check = new ModernCheckbox
            {
                Text = text,
                Checked = !canvasNode.IsOutputPortHidden(port),
                Theme = _modernGrid.Theme,
                Size = new Size(TextRenderer.MeasureText(text, Font).Width + 32, 28),
                Margin = new Padding(8, 2, 4, 2)
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
        var button = EditorButton("打开智能脚本编辑器…");
        button.Click += (_, _) => EditScript(entry);
        return button;
    }

    private Control CreateStructuredLauncher(WorkflowPropertyEntry entry)
    {
        var button = EditorButton("编辑集合/对象…");
        button.Click += (_, _) => EditStructuredValue(entry);
        return button;
    }

    /// <summary>领域操作属性：有处理器时直接执行，否则请求宿主打开插件提供的编辑窗口。</summary>
    private Control CreateActionEditor(WorkflowPropertyEntry entry)
    {
        var nodeId = _model?.SelectedNode?.Id;
        var generation = _inputEditorGeneration;
        var button = EditorButton(Convert.ToString(entry.Value) ?? "打开编辑器…");
        button.Enabled = entry.ActionBlockReason.Length == 0;
        button.Click += async (_, _) =>
        {
            if (button.IsDisposed || generation != _inputEditorGeneration || nodeId != _model?.SelectedNode?.Id) return;
            if (entry.HasActionHandler)
            {
                button.Enabled = false;
                try { await entry.ExecuteActionAsync(); }
                catch (Exception ex) { EditError?.Invoke(this, ex.Message); }
                finally { if (!button.IsDisposed) button.Enabled = entry.ActionBlockReason.Length == 0; }
                return;
            }
            if (PropertyActionRequested == null) { EditError?.Invoke(this, "宿主未注册此属性的编辑窗口。"); return; }
            PropertyActionRequested.Invoke(this, new WorkflowPropertyActionRequest(nodeId!, entry.EditorKey!));
        };
        return button;
    }

    /// <summary>创建候选编辑器：只允许从宿主已发布的候选集中选择，避免手写出机器上不存在的标识。</summary>
    private Control CreateChoiceEditor(WorkflowPropertyEntry entry)
    {
        var combo = EditorCombo();
        foreach (var choice in entry.Choices) combo.Items.Add(choice);
        combo.SelectedItem = entry.Choices.FirstOrDefault(choice => Equals(choice.Value, entry.Value));
        combo.SelectedValueChanged += (_, _) =>
        {
            if (combo.SelectedItem is WorkflowPropertyChoice choice)
                TryEdit(() => _model!.SetValue(entry, choice.Value), rebuild: true);
        };
        return combo;
    }

    /// <summary>请求打开由插件页面提供的独立属性编辑窗口。</summary>
    public event EventHandler<WorkflowPropertyActionRequest>? PropertyActionRequested;

    /// <summary>创建Path Editor。</summary>
    private Control CreatePathEditor(WorkflowPropertyEntry entry)
    {
        var text = EditorText(Convert.ToString(entry.Value) ?? string.Empty);
        var browse = EditorButton("…");
        browse.Margin = new Padding(4, 0, 0, 0);
        browse.AccessibleName = $"浏览{entry.DisplayName}";
        // 只提交用户实际改过的路径：校验所有子控件时，未编辑的旧显示值不能覆盖别处已更新的属性。
        var shown = text.Text;
        text.InnerTextBox.Validated += (_, _) =>
        {
            if (text.Text == shown) return;
            shown = text.Text;
            TryEdit(() => _model!.SetValue(entry, text.Text));
        };
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
            shown = text.Text;
            TryEdit(() => _model!.SetValue(entry, text.Text));
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(browse, 1, 0);
        return layout;
    }

    /// <summary>执行 Edit Script 相关处理。</summary>
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
        AddDialogButtons(dialog, buttons);
        dialog.Controls.Add(editor);
        dialog.Controls.Add(buttons);
        ApplyFixedStyle(dialog);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            TryEdit(() => _model!.SetStructuredJson(entry, editor.Text), rebuild: true);
    }

    /// <summary>执行 Edit Collection Table 相关处理。</summary>
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
        AddDialogButtons(dialog, buttons);
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
    private Control CreateWorkflowInputEditor(WorkflowPropertyEntry entry)
    {
        var generation = _inputEditorGeneration;
        var model = _model!;
        var node = model.SelectedNode;
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

        bool IsCurrentEditor() => !panel.IsDisposed && generation == _inputEditorGeneration
            && ReferenceEquals(model, _model) && ReferenceEquals(node, model.SelectedNode);

        Control valueEditor;
        if (entry.GetInputSource() == WorkflowValueSource.Binding)
        {
            var candidates = _model!.GetBindingCandidates(entry);
            var current = entry.GetInputBinding();
            // 绑定选择打开树形窗口，外观与左侧来源下拉框保持一致：左对齐文本 + 右侧展开箭头。
            var button = EditorButton(candidates.FirstOrDefault(item => item.ToBindingKey() == current)?.DisplayPath
                ?? current?.ToString()
                ?? "选择绑定…");
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Icon = ModernIconKind.ChevronDown;
            button.IconPlacement = ModernIconPlacement.Right;
            button.Click += (_, _) =>
            {
                if (!IsCurrentEditor()) return;
                using var dialog = new WorkflowBindingSelectorDialog(candidates, entry.GetInputBinding());
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCandidate is { } candidate && IsCurrentEditor())
                    TryEdit(() => _model.SetWorkflowInput(
                        entry, WorkflowValueSource.Binding, null, candidate.ToBindingKey()), rebuild: true);
            };
            valueEditor = button;
        }
        else
        {
            var editable = entry.CanEditInputLiteralAsText;
            var text = EditorText(editable
                ? Convert.ToString(entry.GetInputLiteral(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                : $"{entry.WorkflowInputType!.Name}：请使用绑定或专用编辑器");
            text.ReadOnly = !editable;
            if (editable)
                text.InnerTextBox.Validated += (_, _) =>
                {
                    if (!IsCurrentEditor() || text.IsDisposed || entry.GetInputSource() != WorkflowValueSource.Literal
                        || source.SelectedItem is not WorkflowValueSource.Literal) return;
                    if (text.Text == Convert.ToString(entry.GetInputLiteral(), System.Globalization.CultureInfo.InvariantCulture)) return;
                    TryEdit(() => _model!.SetWorkflowInput(entry, WorkflowValueSource.Literal, text.Text, null));
                };
            valueEditor = text;
        }
        valueEditor.Margin = Padding.Empty;
        panel.Controls.Add(valueEditor, 1, 0);
        source.SelectedValueChanged += (_, _) =>
        {
            if (!IsCurrentEditor()) return;
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
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCandidate is { } candidate && IsCurrentEditor())
                    TryEdit(() => _model.SetWorkflowInput(entry, selected, null, candidate.ToBindingKey()), rebuild: true);
                else
                    Rebuild();
            }
        };
        return panel;
    }

    /// <summary>统一执行参数转换、错误提示和可选界面重建，避免各编辑器重复异常处理。</summary>
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
                                          or ArgumentException or OverflowException or InvalidCastException or System.Text.Json.JsonException)
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

    private ModernInput EditorText(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        MinimumSize = new Size(0, 24),
        Theme = ModernTheme.Dark,
        LocalizationContext = LocalizationContext,
        Margin = Padding.Empty
    };

    /// <summary>执行 Editor Combo 相关处理。</summary>
    private ModernSelect EditorCombo() => new()
    {
        Dock = DockStyle.Fill,
        Theme = ModernTheme.Dark,
        DropDownAnimationDuration = 0
    };

    /// <summary>参数行内的操作按钮：与输入框、下拉框同一主题和圆角。</summary>
    private ModernButton EditorButton(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Theme = ModernTheme.Dark,
        Margin = Padding.Empty
    };

    /// <summary>参数表上方操作栏按钮，按文本宽度定尺寸。</summary>
    private ModernButton ActionBarButton(string text) => new()
    {
        Text = text,
        Theme = _modernGrid.Theme,
        Size = new Size(TextRenderer.MeasureText(text, Font).Width + 32, 30),
        Margin = new Padding(0, 2, 6, 2)
    };

    /// <summary>为编辑窗口添加统一风格的确定/取消按钮。</summary>
    private static void AddDialogButtons(Form dialog, FlowLayoutPanel buttons)
    {
        var ok = new ModernButton
        {
            Text = "确定",
            ButtonType = ModernButtonType.Primary,
            DialogResult = DialogResult.OK,
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
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        dialog.CancelButton = cancel;
    }

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
            if (entry.NumberMinimum.HasValue || entry.NumberMaximum.HasValue)
                attributes.Add(new PropertyRangeAttribute(entry.NumberMinimum ?? -1_000_000_000d, entry.NumberMaximum ?? 1_000_000_000d,
                    entry.ValueType == typeof(int) || entry.ValueType == typeof(long) ? 1 : 0.1,
                    entry.ValueType == typeof(int) || entry.ValueType == typeof(long) ? 0 : 3));
            if (entry.GroupPath.Count != 0) attributes.Add(new PropertyGroupAttribute(entry.GroupPath.ToArray()));
            if (!string.IsNullOrWhiteSpace(entry.EditorKey))
                attributes.Add(new PropertyEditorKeyAttribute(entry.EditorKey));
            return attributes.ToArray();
        }
    }

    private static void ApplyFixedStyle(Control root) => WorkflowWinFormsStyle.Apply(root);
}
