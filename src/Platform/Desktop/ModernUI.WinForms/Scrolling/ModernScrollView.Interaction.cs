using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

public sealed partial class ModernScrollView
{
    /// <summary>
    /// 在原生子控件处理滚轮前执行滚动链路：子控件能沿当前方向滚动时由其处理，
    /// 否则把消息交给当前页面。这样无滚动范围的列表不会吞掉页面滚轮。
    /// </summary>
    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg != WindowMouseWheel || !CanScroll || !IsHandleCreated) return false;
        var target = ResolveWheelTarget(message);
        if (target is null || ReferenceEquals(target, this) || !ContainsDescendant(target)) return false;

        var delta = unchecked((short)((long)message.WParam >> 16));
        if (delta == 0 || DescendantCanScroll(target, delta)) return false;
        var previous = _offset;
        SetOffset(_offset - Math.Sign(delta) * Math.Max(1, ScaleLogical(WheelStep)));
        return _offset != previous;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!CanScroll) return;
        var thumb = GetThumbRectangle();
        ModernScrollThumbRenderer.Draw(e.Graphics, thumb,
            _hovered || _dragging ? ThumbHoverColor : ThumbColor);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!CanScroll)
        {
            base.OnMouseWheel(e);
            return;
        }
        SetOffset(_offset - Math.Sign(e.Delta) * Math.Max(1, ScaleLogical(WheelStep)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = CanScroll && e.X >= ClientSize.Width - ScaleLogical(ScrollBarGutter);
        if (_hovered != hover) { _hovered = hover; Invalidate(); }
        if (!_dragging) return;
        var trackTravel = Math.Max(1f, ClientSize.Height - GetThumbRectangle().Height - ScaleLogical(8));
        var contentTravel = Math.Max(1, MaximumOffset);
        var next = ModernCompatibility.Clamp(_dragStartOffset +
            (int)Math.Round((e.Y - _dragStartY) * contentTravel / trackTravel), 0, MaximumOffset);
        if (LiveScrollDuringThumbDrag) SetOffset(next);
        else
        {
            _dragPreviewOffset = next;
            InvalidateScrollBarGutter();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_dragging && _hovered) { _hovered = false; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!_dragging || Capture) return;
        _dragging = false;
        UpdateCompositedRenderingStyle();
        InvalidateScrollBarGutter();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !CanScroll || e.X < ClientSize.Width - ScaleLogical(ScrollBarGutter)) return;
        Focus();
        var thumb = GetThumbRectangle();
        var startOffset = _offset;
        if (!thumb.Contains(e.Location))
        {
            startOffset = ModernCompatibility.Clamp(_offset +
                (e.Y < thumb.Top ? -ClientSize.Height : ClientSize.Height), 0, MaximumOffset);
            if (LiveScrollDuringThumbDrag) SetOffset(startOffset);
        }
        _dragging = true;
        _dragStartY = e.Y;
        _dragStartOffset = startOffset;
        _dragPreviewOffset = startOffset;
        Capture = true;
        UpdateCompositedRenderingStyle();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            var pendingOffset = _dragPreviewOffset;
            _dragging = false;
            Capture = false;
            UpdateCompositedRenderingStyle();
            if (!LiveScrollDuringThumbDrag)
            {
                SetOffset(pendingOffset);
                CompleteDeferredScrollFrame();
            }
            InvalidateScrollBarGutter();
        }
        base.OnMouseUp(e);
    }

    private Control? ResolveWheelTarget(Message message)
    {
        var direct = Control.FromHandle(message.HWnd) ?? Control.FromChildHandle(message.HWnd);
        if (direct is not null && ContainsDescendant(direct)) return direct;

        // WM_MOUSEWHEEL 常被系统投递给顶层/焦点窗口，而不是光标下的原生子 HWND。
        // lParam 携带屏幕坐标，必须据此命中真正位于光标下方的控件。
        var packedPoint = (long)message.LParam;
        var point = new NativePoint
        {
            X = unchecked((short)(packedPoint & 0xFFFF)),
            Y = unchecked((short)((packedPoint >> 16) & 0xFFFF))
        };
        var pointedWindow = WindowFromPoint(point);
        return pointedWindow == IntPtr.Zero
            ? direct
            : Control.FromHandle(pointedWindow) ?? Control.FromChildHandle(pointedWindow) ?? direct;
    }

    private bool ContainsDescendant(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, this)) return true;
        }
        return false;
    }

    private bool DescendantCanScroll(Control target, int delta)
    {
        for (var current = target; current is not null && !ReferenceEquals(current, this); current = current.Parent)
        {
            if (MouseWheelRouting.CanScrollVertically(current, delta)) return true;
        }
        return false;
    }

    internal IDisposable SuspendCompositedRendering()
    {
        if (!UseCompositedScrolling || !IsHandleCreated) return EmptyScope.Instance;
        _compositedSuspensionCount++;
        UpdateCompositedRenderingStyle();
        return new CompositedRenderingScope(this);
    }
}
