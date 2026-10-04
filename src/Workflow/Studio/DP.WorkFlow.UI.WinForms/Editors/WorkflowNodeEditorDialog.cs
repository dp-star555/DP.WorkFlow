using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>WinForms 节点工作台自定义页面渲染器。</summary>
public interface IWorkflowWinFormsNodeEditorPageRenderer
{
    /// <summary>获取与 UI 无关页面描述匹配的稳定渲染器键。</summary>
    string RendererKey { get; }

    /// <summary>获取 Renderer 接受的强类型页面模型。</summary>
    Type ModelType { get; }

    /// <summary>为页面创建 WinForms 控件。</summary>
    /// <param name="page">UI 无关页面描述。</param>
    /// <returns>平台控件。</returns>
    Control CreateControl(WorkflowNodeEditorPageDescriptor page);
}

/// <summary>
/// 可自动容纳参数、子流程、脚本、图像和宿主扩展区域的统一节点编辑窗口。
/// <para>窗口稳定外壳位于 Designer.cs，页面内容仍由节点能力和页面描述符在运行时生成。</para>
/// </summary>
public sealed partial class WorkflowNodeEditorDialog : Form
{
    /// <summary>节点编辑事务模型；无参设计器实例中为空。</summary>
    private WorkflowNodeEditorModel? _model;
    private readonly ListBox _navigation = new() { Visible = false };
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, IWorkflowWinFormsNodeEditorPageRenderer> _renderers =
        new Dictionary<string, IWorkflowWinFormsNodeEditorPageRenderer>(StringComparer.Ordinal);
    private Action<IWorkflowBlockMappingNode>? _editMappings;

    /// <summary>获取运行时模型，并避免设计器无参实例误触发业务页面构建。</summary>
    private WorkflowNodeEditorModel Model =>
        _model ?? throw new InvalidOperationException("节点编辑窗口尚未绑定 WorkflowNodeEditorModel。");

    /// <summary>供 Visual Studio WinForms 设计器显示窗口外壳。</summary>
    public WorkflowNodeEditorDialog()
    {
        InitializeComponent();
    }

    /// <summary>使用节点编辑模型和平台扩展渲染器初始化运行时窗口。</summary>
    /// <returns>返回处理结果。</returns>
    public WorkflowNodeEditorDialog(
        WorkflowNodeEditorModel model,
        IEnumerable<IWorkflowWinFormsNodeEditorPageRenderer>? renderers = null,
        Action<IWorkflowBlockMappingNode>? editMappings = null)
        : this()
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _renderers = CreateRendererIndex(renderers);
        ValidatePageRenderers(model.Pages, _renderers);
        _editMappings = editMappings;
        Text = model.PropertyEditorKey == null ? $"节点信息 - {model.Node.Title}"
            : $"{model.Pages.Single(p => p.PropertyEditorKey == model.PropertyEditorKey).Title} - {model.Node.Title}";

        var hasSpecialContent = model.Pages.Any(page => page.Kind is not (WorkflowNodeEditorPageKind.Properties or WorkflowNodeEditorPageKind.Diagnostics));
        // 默认尺寸应能完整放入常见的 1366×768 工作区，并给宿主任务栏保留空间。
        ClientSize = hasSpecialContent ? new Size(1040, 680) : new Size(720, 640);
        MinimumSize = hasSpecialContent ? new Size(760, 520) : new Size(620, 480);

        nodeIdTextBox.Text = model.EditingNode.Id;
        nodeTypeTextBox.Text = model.EditingNode.NodeType;
        titleTextBox.Text = model.EditingNode.Title;
        if (model.PropertyEditorKey != null)
        {
            headerLayout.Visible = false;
            rootLayout.RowStyles[0].Height = 0;
        }
        titleTextBox.Validated += (_, _) => CommitEditedTitle();
        applyButton.Click += (_, _) => ApplyChanges();
        okButton.Click += (_, _) =>
        {
            if (!ApplyChanges()) return;
            DialogResult = DialogResult.OK;
            Close();
        };

        workspacePanel.Controls.Add(CreateWorkspace());
        if (model.Pages.Any(p => p.Model is IWorkflowNodeEditorCommitReadiness))
        {
            components ??= new System.ComponentModel.Container();
            var readiness = new System.Windows.Forms.Timer(components) { Interval = 150 };
            void UpdateReadiness() { applyButton.Enabled = okButton.Enabled = model.CanApplyChanges; }
            readiness.Tick += (_, _) => UpdateReadiness(); UpdateReadiness(); readiness.Start();
        }
        ApplyFixedStyle(this);
    }

    /// <summary>显示时按当前显示器工作区收缩窗口，避免低分辨率或高 DPI 下按钮落到屏幕外。</summary>
    protected override void OnShown(EventArgs e)
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var maximumWidth = Math.Max(MinimumSize.Width, workingArea.Width - 24);
        var maximumHeight = Math.Max(MinimumSize.Height, workingArea.Height - 24);
        if (Width > maximumWidth || Height > maximumHeight)
        {
            Size = new Size(Math.Min(Width, maximumWidth), Math.Min(Height, maximumHeight));
            Left = workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2);
            Top = workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2);
        }
        base.OnShown(e);
        FindScriptEditor(this)?.FocusEditor();
    }

    private static WorkflowCSharpScriptEditorControl? FindScriptEditor(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is WorkflowCSharpScriptEditorControl editor) return editor;
            var nested = FindScriptEditor(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>把标题文本框内容写入编辑副本，而不是直接修改正式节点。</summary>
    private void CommitEditedTitle()
    {
        if (_model is null || _model.EditingNode.Title == titleTextBox.Text)
            return;
        _model.EditingSession.ExecuteNodeConfigurationChange(
            _model.EditingNode.Id,
            node => node.Title = titleTextBox.Text);
        Text = $"节点信息 - {_model.EditingNode.Title}";
    }

    /// <summary>
    /// 构建节点窗口主体。普通节点只返回参数面板；存在特殊能力时返回左右分栏，
    /// 左侧固定为参数，右侧按页面顺序纵向放置子流程、脚本、图像或自定义内容。
    /// </summary>
    /// <returns>返回处理结果。</returns>
    private Control CreateWorkspace()
    {
        if (Model.PropertyEditorKey is { } key)
        {
            var editor = CreatePageControl(Model.Pages.Single(p => p.PropertyEditorKey == key));
            editor.Dock = DockStyle.Fill;
            return editor;
        }
        var propertiesPage = Model.Pages.Single(page => page.Kind == WorkflowNodeEditorPageKind.Properties);
        var properties = CreatePageControl(propertiesPage);
        properties.Dock = DockStyle.Fill;
        var specialPages = Model.Pages
            .Where(page => page.PropertyEditorKey == null && page.Kind is not (WorkflowNodeEditorPageKind.Properties or WorkflowNodeEditorPageKind.Diagnostics))
            .ToArray();
        if (specialPages.Length == 0) return properties;

        var specialHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = specialPages.Length };
        for (var index = 0; index < specialPages.Length; index++)
        {
            specialHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / specialPages.Length));
            var special = CreatePageControl(specialPages[index]);
            special.Dock = DockStyle.Fill;
            var group = new GroupBox { Text = specialPages[index].Title, Dock = DockStyle.Fill, Padding = new Padding(6) };
            group.Controls.Add(special);
            specialHost.Controls.Add(group, 0, index);
        }
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1
        };
        var initialDistanceApplied = false;
        split.SizeChanged += (_, _) =>
        {
            if (initialDistanceApplied || split.ClientSize.Width < 640) return;
            var maximum = split.ClientSize.Width - split.SplitterWidth - 320;
            if (maximum < 260) return;
            var preferred = (int)Math.Round(split.ClientSize.Width * 0.32d);
            split.SplitterDistance = Math.Clamp(preferred, 280, Math.Min(360, maximum));
            split.Panel1MinSize = Math.Min(280, split.SplitterDistance);
            split.Panel2MinSize = Math.Min(320, split.ClientSize.Width - split.SplitterDistance - split.SplitterWidth);
            initialDistanceApplied = true;
        };
        split.Panel1.Controls.Add(properties);
        split.Panel2.Controls.Add(specialHost);
        return split;
    }

    /// <summary>校验当前子控件，并将编辑副本作为一次可撤销操作提交到正式节点。</summary>
    /// <returns>返回处理结果。</returns>
    private bool ApplyChanges()
    {
        try
        {
            ValidateChildren();
            Model.ApplyChanges();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            MessageBox.Show(this, exception.Message, "参数校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>
    /// 兼容旧页面导航模式的延迟加载入口。当前自适应布局通常直接平铺特殊页面，
    /// 但保留该方法便于以后在窄屏模式恢复页面导航。
    /// </summary>
    private void ShowSelectedPage()
    {
        if (_navigation.SelectedItem is not EditorPageItem item) return;
        workspacePanel.Controls.Clear();
        if (!_controls.TryGetValue(item.Page.PageId, out var control))
        {
            control = CreatePageControl(item.Page);
            control.Dock = DockStyle.Fill;
            _controls.Add(item.Page.PageId, control);
        }
        workspacePanel.Controls.Add(control);
    }

    /// <summary>
    /// 将 UI 无关页面描述转换为 WinForms 控件。宿主 Renderer 优先于内置 PageKind，
    /// 因而视觉、厂商工具等模块可以替换默认页面而不修改本窗口。
    /// </summary>
    /// <param name="page">“page”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreatePageControl(WorkflowNodeEditorPageDescriptor page)
    {
        var rendererKey = ResolveRendererKey(page);
        if (rendererKey is not null && _renderers.TryGetValue(rendererKey, out var renderer))
        {
            if (!renderer.ModelType.IsInstanceOfType(page.Model))
                throw new InvalidOperationException(
                    $"WinForms Renderer“{rendererKey}”要求 {renderer.ModelType.FullName}，页面“{page.PageId}”提供了 {page.Model.GetType().FullName}。");
            return renderer.CreateControl(page);
        }
        return page.Kind switch
        {
            WorkflowNodeEditorPageKind.Properties => CreateProperties((WorkflowPropertyEditorPageModel)page.Model),
            WorkflowNodeEditorPageKind.SubWorkflow => CreateSubWorkflow((WorkflowSubWorkflowEditorPageModel)page.Model),
            WorkflowNodeEditorPageKind.Script => CreateScript((WorkflowScriptEditorPageModel)page.Model),
            WorkflowNodeEditorPageKind.Diagnostics => CreateDiagnostics((WorkflowScriptEditorPageModel)page.Model),
            _ => throw new InvalidOperationException($"WinForms 不支持节点详情页类型 {page.Kind}：{page.PageId}。")
        };
    }

    /// <summary>创建共享属性模型对应的 WinForms 参数面板。</summary>
    /// <param name="page">“page”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreateProperties(WorkflowPropertyEditorPageModel page)
    {
        var panel = new WorkflowPropertyPanel
        {
            ChoiceProvider = page.ChoiceProvider,
            AdditionalProperties = page.AdditionalProperties,
            Session = page.Session,
            EntryNodeId = page.EntryNodeId,
            Dock = DockStyle.Fill,
            HideScriptProperty = Model.EditingNode is IWorkflowScriptNode,
            HideSpecialActions = Model.Pages.Any(item => item.Kind != WorkflowNodeEditorPageKind.Properties)
        };
        panel.EditError += (_, message) => MessageBox.Show(this, message, "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        panel.PropertyActionRequested += (_, request) =>
        {
            WorkflowNodeEditorModel? child = null;
            try
            {
                child = Model.CreatePropertyEditor(request.EditorKey);
                using var dialog = new WorkflowNodeEditorDialog(child, _renderers.Values, _editMappings);
                dialog.ShowDialog(this);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "属性编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { if (child != null) child.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        };
        if (_editMappings is not null) panel.BlockMappingEditRequested += (_, block) => _editMappings(block);
        return panel;
    }

    /// <summary>创建嵌入式子流程设计器及其局部工具栏。</summary>
    /// <param name="page">“page”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreateSubWorkflow(WorkflowSubWorkflowEditorPageModel page)
    {
        var designer = new WorkflowDesignerControl { Session = page.Session, Dock = DockStyle.Fill };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(5) };
        var fit = new Button { Text = "适合画布", AutoSize = true };
        var layout = new Button { Text = "自动布局", AutoSize = true };
        fit.Click += (_, _) => page.Session.FitToView(designer.ClientSize.Width, designer.ClientSize.Height);
        layout.Click += (_, _) => page.Session.AutoLayout();
        toolbar.Controls.Add(fit);
        toolbar.Controls.Add(layout);
        if (Model.Node is IWorkflowBlockMappingNode block && _editMappings is not null)
        {
            var mappings = new Button { Text = "输入/输出映射", AutoSize = true };
            mappings.Click += (_, _) => _editMappings(block);
            toolbar.Controls.Add(mappings);
        }
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(designer);
        panel.Controls.Add(toolbar);
        return panel;
    }

    /// <summary>创建 Roslyn 脚本编辑器、命令栏和编译诊断列表。</summary>
    /// <param name="page">“page”参数。</param>
    /// <returns>返回处理结果。</returns>
    private Control CreateScript(WorkflowScriptEditorPageModel page) => new WorkflowCSharpScriptEditorControl
    {
        Dock = DockStyle.Fill,
        Page = page
    };

    /// <summary>更新Diagnostics。</summary>
    /// <param name="page">“page”参数。</param>
    /// <param name="text">要显示或处理的文本。</param>
    /// <param name="list">“list”参数。</param>
    private static void UpdateDiagnostics(WorkflowScriptEditorPageModel page, string text, ListBox list)
    {
        list.BeginUpdate();
        list.Items.Clear();
        var diagnostics = page.GetDiagnostics(text);
        if (diagnostics.Count == 0) list.Items.Add("✓ 未发现脚本诊断。");
        else foreach (var diagnostic in diagnostics) list.Items.Add(diagnostic);
        list.EndUpdate();
    }

    /// <summary>创建Diagnostics。</summary>
    /// <param name="page">“page”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static Control CreateDiagnostics(WorkflowScriptEditorPageModel page)
    {
        var list = new ListBox { Dock = DockStyle.Fill };
        var diagnostics = page.GetDiagnostics();
        if (diagnostics.Count == 0) list.Items.Add("✓ 未发现脚本诊断。");
        else foreach (var item in diagnostics) list.Items.Add(item);
        return list;
    }

    private static IReadOnlyDictionary<string, IWorkflowWinFormsNodeEditorPageRenderer> CreateRendererIndex(
        IEnumerable<IWorkflowWinFormsNodeEditorPageRenderer>? renderers)
    {
        var index = new Dictionary<string, IWorkflowWinFormsNodeEditorPageRenderer>(StringComparer.Ordinal);
        foreach (var renderer in renderers ?? Array.Empty<IWorkflowWinFormsNodeEditorPageRenderer>())
        {
            if (string.IsNullOrWhiteSpace(renderer.RendererKey))
                throw new InvalidOperationException("WinForms 节点详情页 RendererKey 不能为空。");
            if (!index.TryAdd(renderer.RendererKey.Trim(), renderer))
                throw new InvalidOperationException($"WinForms 节点详情页渲染器“{renderer.RendererKey}”重复注册。");
        }
        return index;
    }

    private static string? ResolveRendererKey(WorkflowNodeEditorPageDescriptor page) =>
        !string.IsNullOrWhiteSpace(page.RendererKey) ? page.RendererKey.Trim() : null;

    private static void ValidatePageRenderers(
        IEnumerable<WorkflowNodeEditorPageDescriptor> pages,
        IReadOnlyDictionary<string, IWorkflowWinFormsNodeEditorPageRenderer> renderers)
    {
        foreach (var page in pages)
        {
            var rendererKey = ResolveRendererKey(page);
            if (rendererKey is null)
                continue;
            if (!renderers.TryGetValue(rendererKey, out var renderer))
                throw new InvalidOperationException(
                    $"WinForms 节点详情页“{page.PageId}”缺少 Renderer“{rendererKey}”。");
            if (!renderer.ModelType.IsInstanceOfType(page.Model))
            {
                throw new InvalidOperationException(
                    $"WinForms Renderer“{rendererKey}”要求 {renderer.ModelType.FullName}，页面“{page.PageId}”提供了 {page.Model.GetType().FullName}。");
            }
        }
    }

    /// <summary>应用。</summary>
    /// <param name="root">“root”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static void ApplyFixedStyle(Control root) => WorkflowWinFormsStyle.Apply(root);

    /// <summary>定义 EditorPageItem 类型。</summary>
    /// <param name="Page">“Page”参数。</param>
    private sealed record EditorPageItem(WorkflowNodeEditorPageDescriptor Page)
    {
        public string Title => Page.Title;
    }
}
