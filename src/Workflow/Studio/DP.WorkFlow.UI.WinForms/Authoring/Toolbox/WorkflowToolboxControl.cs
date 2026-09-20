using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 按分类树展示节点，并支持双击或拖放到画布。
/// TreeView 的静态外观位于同名 Designer.cs，分类和节点项由目录动态生成。
/// </summary>
public sealed partial class WorkflowToolboxControl : UserControl
{
    private WorkflowDesignerSession? _session;

    /// <summary>初始化工作流节点工具箱。</summary>
    public WorkflowToolboxControl()
    {
        InitializeComponent();
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
        if (_session is not null)
        {
            foreach (var item in _session.GetToolboxItems())
                AddItem(item);
            toolboxTreeView.ExpandAll();
        }
        toolboxTreeView.EndUpdate();
    }

    /// <summary>添加Item。</summary>
    /// <param name="item">目标数据项。</param>
    private void AddItem(WorkflowToolboxItem item)
    {
        var segments = (string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nodes = toolboxTreeView.Nodes;
        foreach (var segment in segments)
        {
            var category = nodes.Cast<TreeNode>()
                .FirstOrDefault(node => node.Tag is null && node.Text == segment);
            if (category is null)
            {
                category = new TreeNode(segment)
                {
                    ForeColor = Color.FromArgb(55, 148, 255),
                    NodeFont = new Font(Font, FontStyle.Bold)
                };
                nodes.Add(category);
            }
            nodes = category.Nodes;
        }
        nodes.Add(new TreeNode(item.DisplayName)
        {
            Tag = item,
            ToolTipText = item.Description ?? item.NodeType,
            ForeColor = ForeColor
        });
    }
}
