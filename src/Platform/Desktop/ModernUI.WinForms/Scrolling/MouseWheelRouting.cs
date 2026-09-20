namespace ModernUI.WinForms;

/// <summary>在嵌套原生滚动控件与页面容器之间建立按方向传递的滚轮链路。</summary>
internal static class MouseWheelRouting
{
    private const int WindowMouseWheel = 0x020A;

    public static bool TryRouteToAncestor(Control source, ref Message message)
    {
        if (message.Msg != WindowMouseWheel) return false;
        var delta = unchecked((short)((long)message.WParam >> 16));
        if (delta == 0 || CanScrollVertically(source, delta)) return false;

        for (var ancestor = source.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is ModernScrollView modernScroll && modernScroll.TryScrollWheel(delta)) return true;
            if (ancestor is ScrollableControl { AutoScroll: true } scrollable &&
                TryScrollAutoScrollControl(scrollable, delta)) return true;
        }
        return false;
    }

    internal static bool CanScrollVertically(Control control, int delta)
    {
        var towardTop = delta > 0;
        switch (control)
        {
            case ModernTextArea textArea:
                return textArea.CanConsumeWheel(delta);
            case ModernScrollView scroll:
                return scroll.CanScrollDirection(delta);
            case ListBox list when list.Items.Count > 0:
                if (towardTop) return list.TopIndex > 0;
                var visibleItems = Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight));
                return list.TopIndex + visibleItems < list.Items.Count;
            case ListView listView when listView.Items.Count > 0:
                if (towardTop) return (listView.TopItem?.Index ?? 0) > 0;
                return listView.GetItemRect(listView.Items.Count - 1).Bottom > listView.ClientSize.Height;
            case TreeView tree when tree.Nodes.Count > 0:
                if (towardTop) return tree.TopNode?.PrevVisibleNode is not null;
                var node = tree.TopNode;
                for (var index = 1; index < tree.VisibleCount && node?.NextVisibleNode is { } next; index++)
                    node = next;
                return node?.NextVisibleNode is not null;
            case DataGridView grid when grid.RowCount > 0:
                if (towardTop) return grid.FirstDisplayedScrollingRowIndex > 0;
                return grid.FirstDisplayedScrollingRowIndex + grid.DisplayedRowCount(false) < grid.RowCount;
            case ScrollableControl { AutoScroll: true } scrollable:
                return CanAutoScroll(scrollable, delta);
        }

        return ModernNativeScrollProtocol.CanScroll(control, vertical: true, delta);
    }

    private static bool TryScrollAutoScrollControl(ScrollableControl control, int delta)
    {
        if (!CanAutoScroll(control, delta)) return false;
        var currentX = -control.AutoScrollPosition.X;
        var currentY = -control.AutoScrollPosition.Y;
        var maximum = Math.Max(0, control.DisplayRectangle.Height - control.ClientSize.Height);
        var wheelLines = SystemInformation.MouseWheelScrollLines;
        var logicalStep = wheelLines < 0 ? control.ClientSize.Height : Math.Max(48, wheelLines * 16);
        var notches = Math.Max(1, Math.Abs(delta) / 120);
        var next = ModernCompatibility.Clamp(currentY - Math.Sign(delta) * logicalStep * notches, 0, maximum);
        if (next == currentY) return false;
        control.AutoScrollPosition = new Point(currentX, next);
        return true;
    }

    private static bool CanAutoScroll(ScrollableControl control, int delta)
    {
        var position = -control.AutoScrollPosition.Y;
        var maximum = Math.Max(0, control.DisplayRectangle.Height - control.ClientSize.Height);
        return delta > 0 ? position > 0 : position < maximum;
    }

}
