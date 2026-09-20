using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF 可搜索分层绑定选择器。</summary>
public sealed class WorkflowBindingSelectorWindow : Window
{
    private readonly WorkflowBindingTreeModel _model = new();
    private readonly TreeView _tree = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(6) };
    private readonly WorkflowBindingKey? _current;

    /// <summary>初始化工作流数据绑定选择窗口。</summary>
    /// <param name="candidates">“candidates”参数。</param>
    /// <param name="current">“current”参数。</param>
    public WorkflowBindingSelectorWindow(
        IEnumerable<WorkflowBindingCandidate> candidates,
        WorkflowBindingKey? current = null)
    {
        _current = current;
        Title = "选择工作流绑定";
        Width = 560;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var search = new TextBox { Margin = new Thickness(6) };
        search.ToolTip = "搜索节点、成员、路径或类型";
        search.TextChanged += (_, _) => _model.Search(search.Text);
        _tree.Margin = new Thickness(6);
        _tree.MouseDoubleClick += (_, _) => AcceptSelection();
        var ok = new Button { Content = "确定", Width = 88, Margin = new Thickness(4) };
        var cancel = new Button { Content = "取消", Width = 88, Margin = new Thickness(4) };
        ok.Click += (_, _) => AcceptSelection();
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(search, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(search);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        root.Children.Add(_tree);
        Content = root;
        _model.Changed += (_, _) => Rebuild();
        _model.SetCandidates(candidates);
        Loaded += (_, _) => Keyboard.Focus(search);
    }

    /// <summary>获取或设置 Selected Candidate 成员。</summary>
    /// <summary>执行 Rebuild 相关处理。</summary>
    public WorkflowBindingCandidate? SelectedCandidate { get; private set; }

    private void Rebuild()
    {
        _tree.Items.Clear();
        TreeViewItem? selected = null;
        foreach (var root in _model.Roots)
            _tree.Items.Add(CreateItem(root, ref selected));
        if (selected is not null)
        {
            selected.IsSelected = true;
            selected.BringIntoView();
        }
        _status.Text = $"{_model.MatchCount} 个兼容候选";
    }

    /// <summary>创建Item。</summary>
    /// <param name="source">源路径点集合。</param>
    /// <param name="selected">“selected”参数。</param>
    /// <returns>返回处理结果。</returns>
    private TreeViewItem CreateItem(WorkflowBindingTreeNode source, ref TreeViewItem? selected)
    {
        var item = new TreeViewItem
        {
            Header = source.Label,
            Tag = source.Candidate,
            IsExpanded = source.Kind is WorkflowBindingTreeNodeKind.SourceNode or WorkflowBindingTreeNodeKind.PublicDataGroup
        };
        var containsSelection = false;
        foreach (var child in source.Children)
        {
            var previous = selected;
            item.Items.Add(CreateItem(child, ref selected));
            if (!ReferenceEquals(previous, selected) && selected is not null) containsSelection = true;
        }
        if (source.Candidate?.ToBindingKey() == _current)
            selected = item;
        if (containsSelection) item.IsExpanded = true;
        return item;
    }

    /// <summary>执行 Accept Selection 相关处理。</summary>
    private void AcceptSelection()
    {
        if (_tree.SelectedItem is not TreeViewItem { Tag: WorkflowBindingCandidate candidate })
            return;
        SelectedCandidate = candidate;
        DialogResult = true;
        Close();
    }
}
