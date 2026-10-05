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

/// <summary>节点窗口左侧分页中的一页。</summary>
/// <param name="PanelId">稳定标识。</param>
/// <param name="Title">标签标题。</param>
/// <param name="Content">页面内容。</param>
public sealed record WorkflowWinFormsNodeEditorSidePanel(string PanelId, string Title, Control Content);

/// <summary>
/// 页面渲染器的可选能力：为节点窗口左侧分页贡献列表页（例如视觉 ROI 列表），
/// 页面主体（例如图像画布）仍显示在右侧工作区。
/// </summary>
public interface IWorkflowWinFormsNodeEditorSidePanelRenderer
{
    /// <summary>为页面创建附加到左侧分页的列表页；不需要时返回空集合。</summary>
    IEnumerable<WorkflowWinFormsNodeEditorSidePanel> CreateSidePanels(WorkflowNodeEditorPageDescriptor page);
}

/// <summary>
/// 可自动容纳参数、子流程、脚本、图像和宿主扩展区域的统一节点编辑窗口。
/// <para>窗口稳定外壳位于 Designer.cs，页面内容仍由节点能力和页面描述符在运行时生成。</para>
/// </summary>
public sealed partial class WorkflowNodeEditorDialog : Form
{
    /// <summary>节点编辑事务模型；无参设计器实例中为空。</summary>
    private WorkflowNodeEditorModel? _model;
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

        var hasSpecialContent = model.Pages.Any(page => page.Kind is not (WorkflowNodeEditorPageKind.Properties
            or WorkflowNodeEditorPageKind.Diagnostics or WorkflowNodeEditorPageKind.Results));
        // 默认尺寸应能完整放入常见的 1366×768 工作区，并给宿主任务栏保留空间。
        ClientSize = hasSpecialContent ? new Size(1040, 680) : new Size(720, 640);
        MinimumSize = hasSpecialContent ? new Size(760, 520) : new Size(620, 480);

        if (model.PropertyEditorKey == null)
        {
            // 标题在参数页“基本信息”中编辑；窗口标题跟随编辑副本同步。
            model.EditingSession.Changed += OnEditingSessionChanged;
            FormClosed += (_, _) => model.EditingSession.Changed -= OnEditingSessionChanged;
        }
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

    private void OnEditingSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind == WorkflowDesignerChangeKind.Document && _model is not null && !IsDisposed)
            Text = $"节点信息 - {_model.EditingNode.Title}";
    }

    /// <summary>
    /// 构建节点窗口主体。左侧为分页列表：参数、渲染器贡献的列表页（如视觉 ROI）和运行结果；
    /// 右侧按页面顺序纵向放置子流程、脚本、图像等主体页面。没有主体页面时左侧分页铺满窗口。
    /// </summary>
    private Control CreateWorkspace()
    {
        if (Model.PropertyEditorKey is { } key)
        {
            var editor = CreatePageControl(Model.Pages.Single(p => p.PropertyEditorKey == key));
            editor.Dock = DockStyle.Fill;
            return editor;
        }
        var visible = Model.Pages.Where(page => page.PropertyEditorKey == null).ToArray();
        var specialPages = visible
            .Where(page => page.Kind is not (WorkflowNodeEditorPageKind.Properties
                or WorkflowNodeEditorPageKind.Diagnostics or WorkflowNodeEditorPageKind.Results))
            .ToArray();

        var panels = new List<WorkflowWinFormsNodeEditorSidePanel>
        {
            Panel(visible.Single(page => page.Kind == WorkflowNodeEditorPageKind.Properties))
        };
        var specialControls = specialPages.Select(page =>
        {
            var control = CreatePageControl(page);
            if (ResolveRenderer(page) is IWorkflowWinFormsNodeEditorSidePanelRenderer sides)
                panels.AddRange(sides.CreateSidePanels(page));
            return control;
        }).ToArray();
        if (visible.FirstOrDefault(page => page.Kind == WorkflowNodeEditorPageKind.Results) is { } results)
            panels.Add(Panel(results));
        var side = CreateSidePanels(panels);
        if (specialPages.Length == 0) return side;

        var specialHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = specialPages.Length };
        for (var index = 0; index < specialPages.Length; index++)
        {
            specialHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / specialPages.Length));
            specialControls[index].Dock = DockStyle.Fill;
            var group = new ModernUI.WinForms.ModernGroupBox { Text = specialPages[index].Title, Dock = DockStyle.Fill, Padding = new Padding(6) };
            group.Controls.Add(specialControls[index]);
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
            var preferred = (int)Math.Round(split.ClientSize.Width * 0.34d);
            split.SplitterDistance = Math.Clamp(preferred, 300, Math.Min(400, maximum));
            split.Panel1MinSize = Math.Min(280, split.SplitterDistance);
            split.Panel2MinSize = Math.Min(320, split.ClientSize.Width - split.SplitterDistance - split.SplitterWidth);
            initialDistanceApplied = true;
        };
        split.Panel1.Controls.Add(side);
        split.Panel2.Controls.Add(specialHost);
        return split;

        WorkflowWinFormsNodeEditorSidePanel Panel(WorkflowNodeEditorPageDescriptor page) =>
            new(page.PageId, page.Title, CreatePageControl(page));
    }

    /// <summary>只有一页时直接显示内容，多页时使用 ModernTabControl 分页。</summary>
    private static Control CreateSidePanels(IReadOnlyList<WorkflowWinFormsNodeEditorSidePanel> panels)
    {
        if (panels.Count == 1)
        {
            panels[0].Content.Dock = DockStyle.Fill;
            return panels[0].Content;
        }
        var tabs = new ModernUI.WinForms.ModernTabControl { Dock = DockStyle.Fill, Theme = ModernUI.WinForms.ModernTheme.Dark };
        foreach (var panel in panels)
        {
            panel.Content.Dock = DockStyle.Fill;
            var tab = new TabPage(panel.Title) { Name = panel.PanelId, Padding = new Padding(2) };
            tab.Controls.Add(panel.Content);
            tabs.TabPages.Add(tab);
        }
        tabs.SelectedIndex = 0;
        return tabs;
    }

    private IWorkflowWinFormsNodeEditorPageRenderer? ResolveRenderer(WorkflowNodeEditorPageDescriptor page) =>
        ResolveRendererKey(page) is { } rendererKey && _renderers.TryGetValue(rendererKey, out var renderer) ? renderer : null;

    /// <summary>校验当前子控件，并将编辑副本作为一次可撤销操作提交到正式节点。</summary>
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
    /// 将 UI 无关页面描述转换为 WinForms 控件。宿主 Renderer 优先于内置 PageKind，
    /// 因而视觉、厂商工具等模块可以替换默认页面而不修改本窗口。
    /// </summary>
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
            WorkflowNodeEditorPageKind.Results => new WorkflowNodeResultsControl((WorkflowNodeResultPageModel)page.Model),
            _ => throw new InvalidOperationException($"WinForms 不支持节点详情页类型 {page.Kind}：{page.PageId}。")
        };
    }

    /// <summary>创建共享属性模型对应的 WinForms 参数面板。</summary>
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
            HideSpecialActions = Model.Pages.Any(item => item.Kind is not (WorkflowNodeEditorPageKind.Properties or WorkflowNodeEditorPageKind.Results))
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

    /// <summary>创建嵌入式子流程设计器、局部工具栏和节点工具箱。</summary>
    private Control CreateSubWorkflow(WorkflowSubWorkflowEditorPageModel page)
    {
        var designer = new WorkflowDesignerControl { Session = page.Session, Dock = DockStyle.Fill };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(5) };
        var fit = ToolbarButton("适合画布");
        var layout = ToolbarButton("自动布局");
        fit.Click += (_, _) => page.Session.FitToView(designer.ClientSize.Width, designer.ClientSize.Height);
        layout.Click += (_, _) => page.Session.AutoLayout();
        toolbar.Controls.Add(fit);
        toolbar.Controls.Add(layout);
        if (Model.Node is IWorkflowBlockMappingNode block && _editMappings is not null)
        {
            var mappings = ToolbarButton("输入/输出映射");
            mappings.Click += (_, _) => _editMappings(block);
            toolbar.Controls.Add(mappings);
        }
        var canvas = new Panel { Dock = DockStyle.Fill };
        canvas.Controls.Add(designer);
        canvas.Controls.Add(toolbar);

        // 子画布与主工作台一致：左侧工具箱双击添加到画布中心，或直接拖放到画布。
        var toolbox = new WorkflowToolboxControl { Session = page.Session, Dock = DockStyle.Fill };
        toolbox.NodeTypeActivated += (_, item) =>
        {
            var center = WorkflowDesignerGeometry.ScreenToCanvas(page.Session, designer.ClientSize.Width / 2d, designer.ClientSize.Height / 2d);
            page.Session.AddNode(item.NodeType, center.X - 90, center.Y - 30);
        };
        var split = new ModernUI.WinForms.ModernSplitter
        {
            // 先给出足够尺寸，避免默认 150px 宽度下设置最小宽度/分隔位置越界。
            Size = new Size(900, 500),
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            InitialPanel2Size = 0,
            Panel1MinSize = 150,
            SplitterDistance = 200
        };
        split.Panel1.Controls.Add(toolbox);
        split.Panel2.Controls.Add(canvas);
        return split;
    }

    private ModernUI.WinForms.ModernButton ToolbarButton(string text) => new()
    {
        Text = text,
        Size = new Size(TextRenderer.MeasureText(text, Font).Width + 32, 30),
        Margin = new Padding(0, 0, 6, 0)
    };

    /// <summary>创建 Roslyn 脚本编辑器、命令栏和编译诊断列表。</summary>
    private Control CreateScript(WorkflowScriptEditorPageModel page) => new WorkflowCSharpScriptEditorControl
    {
        Dock = DockStyle.Fill,
        Page = page
    };

    /// <summary>创建Diagnostics。</summary>
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
    private static void ApplyFixedStyle(Control root) => WorkflowWinFormsStyle.Apply(root);

    /// <summary>定义 EditorPageItem 类型。</summary>
    private sealed record EditorPageItem(WorkflowNodeEditorPageDescriptor Page)
    {
        public string Title => Page.Title;
    }
}
