using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// WinForms 工作流绑定选择窗口。
/// <para>
/// 该窗口只负责把 <see cref="WorkflowBindingTreeModel"/> 转换为可搜索、可折叠的
/// <see cref="TreeView"/>；候选项的控制流可见性和类型兼容性已经由共享模型完成。
/// </para>
/// <para>静态布局位于同名 Designer.cs，便于在 Visual Studio 设计器中人工调整。</para>
/// </summary>
public sealed partial class WorkflowBindingSelectorDialog : Form
{
    /// <summary>UI 无关的树形候选模型，负责搜索过滤和层级组织。</summary>
    private readonly WorkflowBindingTreeModel _model = new();

    /// <summary>打开窗口时已有的绑定，用于重建树后定位当前叶子。</summary>
    private WorkflowBindingKey? _currentBinding;

    /// <summary>
    /// 供 Visual Studio WinForms 设计器使用的无参构造函数。
    /// 运行时通常应调用包含候选集合的构造函数。
    /// </summary>
    public WorkflowBindingSelectorDialog()
    {
        InitializeComponent();
        WireInteractionEvents();
    }

    /// <summary>使用强类型候选集合初始化绑定选择窗口。</summary>
    /// <returns>返回处理结果。</returns>
    public WorkflowBindingSelectorDialog(
        IEnumerable<WorkflowBindingCandidate> candidates,
        WorkflowBindingKey? current = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _currentBinding = current;

        // SetCandidates 会触发 Changed，Changed 最终调用 RebuildBindingTree。
        _model.SetCandidates(candidates);
        WorkflowWinFormsStyle.Apply(this);
    }

    /// <summary>用户确认后的候选；取消窗口时保持为空。</summary>
    public WorkflowBindingCandidate? SelectedCandidate { get; private set; }

    /// <summary>
    /// 集中连接行为事件。Designer.cs 只保存控件层级与尺寸，避免设计器序列化业务 Lambda。
    /// </summary>
    private void WireInteractionEvents()
    {
        searchTextBox.TextChanged += (_, _) => _model.Search(searchTextBox.Text);
        bindingTreeView.NodeMouseDoubleClick += (_, _) => AcceptSelection();
        okButton.Click += (_, _) => AcceptSelection();
        _model.Changed += (_, _) => RebuildBindingTree();
    }

    /// <summary>根据共享树模型重新生成 WinForms TreeNode。</summary>
    private void RebuildBindingTree()
    {
        bindingTreeView.BeginUpdate();
        try
        {
            bindingTreeView.Nodes.Clear();
            TreeNode? selected = null;

            foreach (var root in _model.Roots)
            {
                var node = CreateTreeNode(root, _currentBinding, ref selected);
                bindingTreeView.Nodes.Add(node);

                // 默认只展开来源层。成员层保持折叠，避免复杂结果对象打开时树过长。
                node.Expand();
                if (root.Kind == WorkflowBindingTreeNodeKind.PublicDataGroup)
                    foreach (TreeNode category in node.Nodes)
                        category.Expand();
            }

            // 搜索结果数量通常较少，此时全部展开可以直接看到命中的叶子。
            if (!string.IsNullOrWhiteSpace(_model.SearchText))
                bindingTreeView.ExpandAll();

            bindingTreeView.SelectedNode = selected;
            selected?.EnsureVisible();
            statusLabel.Text = $" {_model.MatchCount} 个兼容候选";
        }
        finally
        {
            bindingTreeView.EndUpdate();
        }
    }

    /// <summary>递归地把平台无关树节点转换为 WinForms 树节点。</summary>
    /// <param name="source">源路径点集合。</param>
    /// <param name="current">“current”参数。</param>
    /// <param name="selected">“selected”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static TreeNode CreateTreeNode(
        WorkflowBindingTreeNode source,
        WorkflowBindingKey? current,
        ref TreeNode? selected)
    {
        var node = new TreeNode(source.Label) { Tag = source.Candidate };
        foreach (var child in source.Children)
            node.Nodes.Add(CreateTreeNode(child, current, ref selected));

        if (source.Candidate?.ToBindingKey() == current)
            selected = node;
        return node;
    }

    /// <summary>校验当前选择必须是候选叶子，然后关闭窗口并返回结果。</summary>
    private void AcceptSelection()
    {
        if (bindingTreeView.SelectedNode?.Tag is not WorkflowBindingCandidate candidate)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        SelectedCandidate = candidate;
        DialogResult = DialogResult.OK;
        Close();
    }
}
