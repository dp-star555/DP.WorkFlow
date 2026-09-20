using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

public sealed partial class ModernScrollView
{
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AutoScroll = true;
        HideNativeScrollBars();
        if (_messageFilterRegistered) return;
        Application.AddMessageFilter(this);
        _messageFilterRegistered = true;
    }

    protected override void OnDpiChangedBeforeParent(EventArgs e)
    {
        _dpiLayoutActive = true;
        InvalidateContentMeasure();
        base.OnDpiChangedBeforeParent(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        InvalidateContentMeasure();
        _dpiLayoutActive = false;
        ContentLayoutTransaction.Request();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is WindowMouseWheel or WindowSize or WindowNcCalcSize)
            HideNativeScrollBars(message.HWnd);
        base.WndProc(ref message);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        _offset = ModernCompatibility.Clamp(-DisplayRectangle.Top, 0, MaximumOffset);
        InvalidateScrollBarGutter();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _compositedRenderingActive = false;
        _dpiLayoutActive = false;
        if (_messageFilterRegistered)
        {
            Application.RemoveMessageFilter(this);
            _messageFilterRegistered = false;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (_changingContent || e.Control is null || ReferenceEquals(_content, e.Control)) return;
        if (_content is null)
        {
            _content = e.Control;
            _offset = 0;
            TrackContent(e.Control);
            PerformLayout();
        }
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (!_changingContent && e.Control is not null && ReferenceEquals(_content, e.Control))
        {
            UntrackContent(e.Control);
            _content = null;
            _offset = 0;
        }
        base.OnControlRemoved(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // During a DPI transaction, WinForms is still scaling descendants. Measuring that partial
        // tree makes every intermediate SizeChanged recursively run GetPreferredSize again.
        if (!_dpiLayoutActive && !ContentLayoutTransaction.IsPending) LayoutContent();
    }

    private void LayoutContent()
    {
        if (_layingOut || _content is null) return;
        try
        {
            _layingOut = true;
            // Native AutoScroll can temporarily reserve the system scrollbar width even after its
            // non-client bar is hidden. Base content width on the stable outer viewport instead.
            var width = Math.Max(0, Width - Math.Max(0, ScaleLogical(ScrollBarGutter)));
            if (_contentMeasureInvalid || _measuredContentWidth != width)
            {
                _preferredContentHeight = _content.GetPreferredSize(new Size(width, 0)).Height;
                _measuredContentWidth = width;
                _contentMeasureInvalid = false;
            }
            var height = Math.Max(ClientSize.Height, _preferredContentHeight);
            _offset = ModernCompatibility.Clamp(_offset, 0, Math.Max(0, height - ClientSize.Height));
            AutoScrollMinSize = new Size(0, height);
            SetDisplayRectLocation(0, -_offset);
            _offset = ModernCompatibility.Clamp(-DisplayRectangle.Top, 0, Math.Max(0, height - ClientSize.Height));
            _content.SetBounds(DisplayRectangle.Left, DisplayRectangle.Top, width, height);
            HideNativeScrollBars();
        }
        finally { _layingOut = false; }
        Invalidate();
    }

    private void TrackContent(Control content)
    {
        content.SizeChanged += Content_SizeChanged;
        content.Layout += Content_Layout;
    }

    private void UntrackContent(Control content)
    {
        content.SizeChanged -= Content_SizeChanged;
        content.Layout -= Content_Layout;
    }

    private void Content_SizeChanged(object? sender, EventArgs e)
    {
        if (_layingOut) return;
        InvalidateContentMeasure();
        QueueContentLayout();
    }

    private void Content_Layout(object? sender, LayoutEventArgs e)
    {
        if (_layingOut) return;
        InvalidateContentMeasure();
        QueueContentLayout();
    }

    private void QueueContentLayout() => ContentLayoutTransaction.Request();

}
