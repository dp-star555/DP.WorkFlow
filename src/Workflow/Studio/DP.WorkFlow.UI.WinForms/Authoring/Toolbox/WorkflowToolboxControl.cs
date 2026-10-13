using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 按分类树展示节点，支持搜索过滤以及双击或拖放到画布。
/// 搜索框与 ModernTreeView 的静态外观位于同名 Designer.cs，分类和节点项由目录动态生成。
/// </summary>
public sealed partial class WorkflowToolboxControl : UserControl
{
    private WorkflowDesignerSession? _session;
    private ImageList? _icons;

    /// <summary>初始化工作流节点工具箱。</summary>
    public WorkflowToolboxControl()
    {
        InitializeComponent();
        ModernUI.WinForms.ModernUiSettings.ApplyTheme(this, ModernUI.WinForms.ModernTheme.Dark);
        searchInput.TextChanged += (_, _) => RebuildTree();
        toolboxTreeView.NodeMouseDoubleClick += (_, e) => Activate(e.Node);
        toolboxTreeView.ItemDrag += (_, e) =>
        {
            if (e.Item is TreeNode { Tag: WorkflowToolboxItem item })
                toolboxTreeView.DoDragDrop(item, DragDropEffects.Copy);
        };
    }

    /// <summary>获取或设置设计会话。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            RebuildTree();
        }
    }

    /// <summary>激活工具箱项目时发生。</summary>
    public event EventHandler<WorkflowToolboxItem>? NodeTypeActivated;

    /// <summary>执行 Activate 相关处理。</summary>
    /// <param name="node">目标画布节点。</param>
    private void Activate(TreeNode node)
    {
        if (node.Tag is WorkflowToolboxItem item)
            NodeTypeActivated?.Invoke(this, item);
    }

    /// <summary>执行 Rebuild Tree 相关处理。</summary>
    private void RebuildTree()
    {
        toolboxTreeView.BeginUpdate();
        toolboxTreeView.Nodes.Clear();
        EnsureIconList();
        if (_session is not null)
        {
            var filter = searchInput.Text.Trim();
            foreach (var item in _session.GetToolboxItems().Where(item => Matches(item, filter)))
                AddItem(item);
            toolboxTreeView.ExpandAll();
        }
        toolboxTreeView.EndUpdate();
    }

    /// <summary>按显示名、节点类型、分类或说明做不区分大小写的包含匹配；空过滤条件匹配全部。</summary>
    private static bool Matches(WorkflowToolboxItem item, string filter) =>
        filter.Length == 0 || new[] { item.DisplayName, item.NodeType, item.Category, item.Description }
            .Any(text => text?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>添加Item。</summary>
    /// <param name="item">目标数据项。</param>
    private void AddItem(WorkflowToolboxItem item)
    {
        var segments = Polyfill.SplitTrimmed(string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category, '/');
        var nodes = toolboxTreeView.Nodes;
        foreach (var segment in segments)
        {
            var category = nodes.Cast<TreeNode>()
                .FirstOrDefault(node => node.Tag is null && node.Text == segment);
            if (category is null)
            {
                category = new TreeNode(segment)
                {
                    ForeColor = toolboxTreeView.Theme.TextSecondary,
                    NodeFont = new Font(Font, FontStyle.Bold)
                };
                nodes.Add(category);
            }
            nodes = category.Nodes;
        }
        var icon = IconKey(item.ResolveIcon());
        nodes.Add(new TreeNode(item.DisplayName)
        {
            Tag = item,
            ToolTipText = item.Description ?? item.NodeType,
            ImageKey = icon,
            SelectedImageKey = icon
        });
    }

    /// <summary>按当前 DPI 建立图标列表：节点名称左侧显示与画布节点相同的线框图标；DPI 变化时重建。</summary>
    private void EnsureIconList()
    {
        var size = Math.Max(12, (int)Math.Round(16 * DeviceDpi / 96d));
        if (_icons?.ImageSize.Width == size) return;
        var previous = _icons;
        _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(size, size) };
        toolboxTreeView.ImageList = _icons;
        previous?.Dispose();
    }

    /// <summary>把图标绘制进图标列表（每个图标只绘制一次），返回其键。</summary>
    private string IconKey(WorkflowNodeIcon icon)
    {
        if (_icons is null) return icon.Key;
        if (!_icons.Images.ContainsKey(icon.Key))
        {
            var size = _icons.ImageSize.Width;
            var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var inset = size / 16f;
                WorkflowDesignerControl.DrawIcon(graphics, icon, new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset), WorkflowDesignerPalette.Icon);
            }
            _icons.Images.Add(icon.Key, bitmap);
        }
        return icon.Key;
    }

    /// <inheritdoc />
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RebuildTree();
    }
}
