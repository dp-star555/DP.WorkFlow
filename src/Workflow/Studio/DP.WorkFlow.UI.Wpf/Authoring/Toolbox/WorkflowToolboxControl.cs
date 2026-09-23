using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>按分类树展示节点，并支持双击或拖放到画布。</summary>
public sealed class WorkflowToolboxControl : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(WorkflowDesignerSession),
        typeof(WorkflowToolboxControl),
        new PropertyMetadata(null, OnSessionChanged));

    private readonly TreeView _tree = new();
    private Point _dragStart;

    /// <summary>初始化工作流节点工具箱。</summary>
    public WorkflowToolboxControl()
    {
        Background = Brush(15, 23, 42);
        Foreground = Brush(226, 232, 240);
        _tree.Background = Background;
        _tree.Foreground = Foreground;
        _tree.BorderThickness = new Thickness(0);
        _tree.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(_tree);
        _tree.PreviewMouseMove += OnTreeMouseMove;
        _tree.MouseDoubleClick += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem { Tag: WorkflowToolboxItem item })
                NodeTypeActivated?.Invoke(this, item);
        };
        Content = _tree;
    }

    public WorkflowDesignerSession? Session
    {
        get => (WorkflowDesignerSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>获取或设置 Node Type Activated 成员。</summary>
    public event EventHandler<WorkflowToolboxItem>? NodeTypeActivated;

    /// <summary>处理设计会话变化并刷新当前控件。</summary>
    /// <param name="source">源路径点集合。</param>
    /// <param name="args">“args”参数。</param>
    private static void OnSessionChanged(DependencyObject source, DependencyPropertyChangedEventArgs args) =>
        ((WorkflowToolboxControl)source).RebuildTree();

    /// <summary>执行 Rebuild Tree 相关处理。</summary>
    private void RebuildTree()
    {
        _tree.Items.Clear();
        if (Session is null) return;
        foreach (var item in Session.GetToolboxItems()) AddItem(item);
    }

    /// <summary>添加Item。</summary>
    /// <param name="item">目标数据项。</param>
    private void AddItem(WorkflowToolboxItem item)
    {
        var segments = (string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ItemCollection nodes = _tree.Items;
        foreach (var segment in segments)
        {
            var category = nodes.Cast<TreeViewItem>()
                .FirstOrDefault(node => node.Tag is null && Equals(node.Header, segment));
            if (category is null)
            {
                category = new TreeViewItem
                {
                    Header = segment,
                    IsExpanded = true,
                    Foreground = Brush(125, 211, 252),
                    FontWeight = FontWeights.SemiBold
                };
                nodes.Add(category);
            }
            nodes = category.Items;
        }
        nodes.Add(new TreeViewItem
        {
            Header = item.DisplayName,
            Tag = item,
            ToolTip = item.Description ?? item.NodeType,
            Foreground = Foreground
        });
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || _tree.SelectedItem is not TreeViewItem { Tag: WorkflowToolboxItem item })
            return;
        var current = e.GetPosition(_tree);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _ = DragDrop.DoDragDrop(_tree, item, DragDropEffects.Copy);
    }

    /// <summary>创建并冻结指定 RGB 颜色的 WPF 画刷。</summary>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
