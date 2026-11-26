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
    private bool _releasingPages, _pagesReleased;

    // 在窗口仍有UI消息循环时等待页面停止；Designer.Dispose的同步兜底不能等待仍需UI线程的原生试运行。
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _model is null || _pagesReleased) return;
        e.Cancel = true;
        if (_releasingPages) return;
        _releasingPages = true;
        var result = DialogResult;
        DialogResult = DialogResult.None;
        Enabled = false;
        try { await _model.DisposeAsync(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
        finally
        {
            _pagesReleased = true;
            if (!IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed) { DialogResult = result; Close(); } }));
        }
    }

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
        IEnumerable<IWorkflowWinFormsNodeEditorPageRenderer>? renderers = null)
        : this()
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _renderers = CreateRendererIndex(renderers);
        ValidatePageRenderers(model.Pages, _renderers);
        Text = model.PropertyEditorKey == null ? $"节点信息 - {model.Node.Title}"
            : $"{model.Pages.Single(p => p.PropertyEditorKey == model.PropertyEditorKey).Title} - {model.Node.Title}";

        var hasSpecialContent = model.Pages.Any(page => !IsSidePage(page));
        // 默认尺寸应能完整放入常见的 1366×768 工作区，并给宿主任务栏保留空间。
        ClientSize = hasSpecialContent ? new Size(1280, 720) : new Size(1040, 680);
        MinimumSize = hasSpecialContent ? new Size(900, 560) : new Size(760, 480);

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
    /// 构建节点窗口主体（按设计稿“节点编辑页”）：顶部节点标题卡（图标、标题、类型 · 标识、上次运行状态）；
    /// 左边分页卡（参数、输入输出、渲染器贡献的列表页如视觉 ROI），中间按页面顺序放子流程、脚本、图像等主体卡，
    /// 右边是运行结果和本节点提示。没有主体页面时左边分页卡填满中间。
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
        var specialPages = visible.Where(page => !IsSidePage(page)).ToArray();

        // 先创建主体页面：其页面模型可能在控件连接后才为参数页贡献操作按钮（IWorkflowNodeEditorPropertyContributor）。
        var sidePanels = new List<WorkflowWinFormsNodeEditorSidePanel>();
        var specialControls = specialPages.Select(page =>
        {
            var control = CreatePageControl(page);
            if (ResolveRenderer(page) is IWorkflowWinFormsNodeEditorSidePanelRenderer sides)
                sidePanels.AddRange(sides.CreateSidePanels(page));
            return control;
        }).ToArray();
        var panels = new List<WorkflowWinFormsNodeEditorSidePanel>
        {
            Panel(visible.Single(page => page.Kind == WorkflowNodeEditorPageKind.Properties))
        };
        // 复合节点的“输入输出”页紧跟参数页。
        if (visible.FirstOrDefault(page => page.Kind == WorkflowNodeEditorPageKind.SubflowInterface) is { } subflowInterface)
            panels.Add(Panel(subflowInterface));
        panels.AddRange(sidePanels);

        var root = new Panel { Dock = DockStyle.Fill };
        var middle = new Panel { Dock = DockStyle.Fill };
        var sideCard = new WorkflowCard { Dock = specialPages.Length == 0 ? DockStyle.Fill : DockStyle.Left, LogicalExtent = specialPages.Length == 0 ? 0 : 380 };
        sideCard.Controls.Add(CreateSidePanels(panels));

        // 右栏：运行结果（有运行会话时）和本节点提示。
        var right = new Panel { Dock = DockStyle.Right, Width = LogicalToDeviceUnits(320) };
        if (visible.FirstOrDefault(page => page.Kind == WorkflowNodeEditorPageKind.Results) is { } results)
            right.Controls.Add(WorkflowPage.Section("运行结果", null, CreatePageControl(results)));
        var notices = new WorkflowFactList();
        var noticeCard = WorkflowPage.Section("本节点提示", null, notices, right.Controls.Count == 0 ? DockStyle.Fill : DockStyle.Bottom, 200);
        ShowNotices(notices, noticeCard);
        if (right.Controls.Count > 0) right.Controls.Add(WorkflowPage.Gap(DockStyle.Bottom));
        right.Controls.Add(noticeCard);

        if (specialPages.Length > 0)
        {
            var specialHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = specialPages.Length };
            for (var index = 0; index < specialPages.Length; index++)
            {
                specialHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / specialPages.Length));
                var card = WorkflowPage.Section(specialPages[index].Title, null, specialControls[index]);
                card.Margin = new Padding(0, 0, 0, index == specialPages.Length - 1 ? 0 : LogicalToDeviceUnits(10));
                specialHost.Controls.Add(card, 0, index);
            }
            middle.Controls.Add(specialHost);
            middle.Controls.Add(WorkflowPage.Gap(DockStyle.Left));
        }
        middle.Controls.Add(sideCard);
        root.Controls.Add(middle);
        root.Controls.Add(WorkflowPage.Gap(DockStyle.Right));
        root.Controls.Add(right);

        var header = new WorkflowCard { Dock = DockStyle.Top, LogicalExtent = 64 };
        header.Controls.Add(new WorkflowNodeHeader { Dock = DockStyle.Fill, Session = Model.Session, NodeId = Model.Node.Id });
        root.Controls.Add(WorkflowPage.Gap(DockStyle.Top));
        root.Controls.Add(header);
        return root;

        WorkflowWinFormsNodeEditorSidePanel Panel(WorkflowNodeEditorPageDescriptor page) =>
            new(page.PageId, page.Title, CreatePageControl(page));
    }

    /// <summary>本节点在运行会话里累计的提示（只读，确认在画布角标或节点提示页上做）。</summary>
    private void ShowNotices(WorkflowFactList list, WorkflowCard card)
    {
        var notices = Model.Session.GetNodeNotices(Model.Node.Id);
        WorkflowPage.HeaderOf(card).Subtitle = notices.Count == 0 ? null : notices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        list.Show(notices.Count == 0
            ? new[] { new WorkflowFact("本节点没有提示") }
            : notices.Reverse().Select(notice => new WorkflowFact(notice.Category + "  " + notice.Message, "× " + notice.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>显示在左侧分页中的页面；其余页面（子画布、脚本、图像等）放在右侧主体区。</summary>
    private static bool IsSidePage(WorkflowNodeEditorPageDescriptor page) => page.Kind is WorkflowNodeEditorPageKind.Properties
        or WorkflowNodeEditorPageKind.Diagnostics or WorkflowNodeEditorPageKind.Results or WorkflowNodeEditorPageKind.SubflowInterface;

    /// <summary>左边分页卡：参数、输入输出、列表页（只有参数一页时也用页签，和设计稿一致）。</summary>
    private static Control CreateSidePanels(IReadOnlyList<WorkflowWinFormsNodeEditorSidePanel> panels)
    {
        var tabs = new ModernUI.WinForms.ModernTabControl { Dock = DockStyle.Fill, Theme = WorkflowWinFormsTheme.Modern };
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
            WorkflowNodeEditorPageKind.SubflowInterface => new WorkflowSubflowInterfaceControl((WorkflowSubflowInterfacePageModel)page.Model)
            {
                Dock = DockStyle.Fill
            },
            _ => throw new InvalidOperationException($"WinForms 不支持节点详情页类型 {page.Kind}：{page.PageId}。")
        };
    }

    /// <summary>创建共享属性模型对应的 WinForms 参数面板。</summary>
    private Control CreateProperties(WorkflowPropertyEditorPageModel page)
    {
        var panel = new WorkflowPropertyPanel
        {
            ChoiceProvider = page.ChoiceProvider,
            Services = page.Services,
            AdditionalProperties = WorkflowNodeEditorPropertyContributors.Compose(page.AdditionalProperties, Model.Pages),
            Session = page.Session,
            EntryNodeId = page.EntryNodeId,
            Dock = DockStyle.Fill,
            HideScriptProperty = Model.EditingNode is IWorkflowScriptNode
        };
        panel.EditError += (_, message) => MessageBox.Show(this, message, "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        panel.PropertyActionRequested += (_, request) =>
        {
            WorkflowNodeEditorModel? child = null;
            try
            {
                child = Model.CreatePropertyEditor(request.EditorKey);
                using var dialog = new WorkflowNodeEditorDialog(child, _renderers.Values);
                dialog.ShowDialog(this);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "属性编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { if (child != null) child.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        };
        return panel;
    }

    /// <summary>
    /// 子画布页与工作台主画布共用画布工作区：同一套工具栏、工具箱和诊断。
    /// “运行”在最近一次运行的基础上单独再执行这个节点，过程记在运行日志里。
    /// </summary>
    private Control CreateSubWorkflow(WorkflowSubWorkflowEditorPageModel page)
    {
        var workspace = new WorkflowCanvasWorkspaceControl { Dock = DockStyle.Fill };
        // 子画布自己的诊断：检查正在编辑的草稿，可能稍后才放入启动节点。
        workspace.Diagnostics.FollowsDocumentEntry = true;
        workspace.Session = page.Session;
        var commands = new WorkflowSubWorkflowRunCommands(page);
        workspace.RunCommands = commands;
        workspace.Disposed += (_, _) =>
        {
            workspace.RunCommands = null;
            commands.Dispose();
        };
        // 子画布与主工作台一致：双击节点打开其节点窗口。
        workspace.Designer.NodeEditRequested += (_, node) => OpenSubWorkflowNode(page, node);
        workspace.InteractionError += (_, message) =>
            MessageBox.Show(this, message, "子画布", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return workspace;
    }

    /// <summary>在本窗口之上打开子画布节点的节点窗口；应用写回子画布草稿，随本窗口确认或取消。</summary>
    private void OpenSubWorkflowNode(WorkflowSubWorkflowEditorPageModel page, IWorkflowNodeModel node)
    {
        WorkflowNodeEditorModel? child = null;
        try
        {
            child = Model.CreateSubWorkflowNodeEditor(page, node.Id);
            using var dialog = new WorkflowNodeEditorDialog(child, _renderers.Values);
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开节点详情", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { if (child != null) child.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    /// <summary>创建 Roslyn 脚本编辑器、命令栏和编译诊断列表。</summary>
    private Control CreateScript(WorkflowScriptEditorPageModel page) => new WorkflowCSharpScriptEditorControl
    {
        Dock = DockStyle.Fill,
        Page = page
    };

    /// <summary>创建Diagnostics。</summary>
    private static Control CreateDiagnostics(WorkflowScriptEditorPageModel page)
    {
        var list = new ModernUI.WinForms.ModernListBox { Dock = DockStyle.Fill };
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
