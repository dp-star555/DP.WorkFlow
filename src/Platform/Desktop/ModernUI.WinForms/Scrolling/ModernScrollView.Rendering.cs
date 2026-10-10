using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

public sealed partial class ModernScrollView
{
    private void ResumeCompositedRendering()
    {
        if (_compositedSuspensionCount <= 0) return;
        _compositedSuspensionCount--;
        UpdateCompositedRenderingStyle();
    }

    private void UpdateCompositedRenderingStyle()
    {
        if (!IsHandleCreated) return;
        var shouldBeActive = UseCompositedScrolling && LiveScrollDuringThumbDrag && _dragging &&
                             _compositedSuspensionCount == 0;
        if (_compositedRenderingActive == shouldBeActive) return;
        var style = GetWindowLongPtr(Handle, ExtendedWindowStyleIndex).ToInt64();
        var nextStyle = shouldBeActive
            ? style | ExtendedStyleComposited
            : style & ~ExtendedStyleComposited;
        SetWindowLongPtr(Handle, ExtendedWindowStyleIndex, new IntPtr(nextStyle));
        _compositedRenderingActive = shouldBeActive;
        if (!shouldBeActive) Invalidate(true);
    }

    internal bool CanScrollDirection(int delta) => delta > 0 ? _offset > 0 : _offset < MaximumOffset;

    internal bool TryScrollWheel(int delta)
    {
        if (delta == 0 || !CanScrollDirection(delta)) return false;
        var previous = _offset;
        SetOffset(_offset - Math.Sign(delta) * Math.Max(1, ScaleLogical(WheelStep)));
        return _offset != previous;
    }

    private void SetOffset(int value)
    {
        var next = ModernCompatibility.Clamp(value, 0, MaximumOffset);
        // 内容重建、焦点自动滚动等会直接改动显示区位置；只有缓存偏移与实际位置都已一致才跳过，
        // 否则会出现“内容显示在顶端、滚动条仍在中间、一滚动又跳回原处”的错位。
        if (_offset == next && -DisplayRectangle.Top == next) return;
        SetDisplayRectLocation(0, -next);
        _offset = ModernCompatibility.Clamp(-DisplayRectangle.Top, 0, MaximumOffset);
        HideNativeScrollBars();
        InvalidateScrollBarGutter();
    }

    private void CompleteDeferredScrollFrame()
    {
        if (!IsHandleCreated) return;
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
            RedrawInvalidate | RedrawErase | RedrawAllChildren | RedrawUpdateNow);
    }

    private void InvalidateScrollBarGutter()
    {
        var gutter = Math.Max(0, ScaleLogical(ScrollBarGutter));
        Invalidate(new Rectangle(Math.Max(0, ClientSize.Width - gutter), 0,
            Math.Min(ClientSize.Width, gutter), ClientSize.Height));
    }

    private void HideNativeScrollBars() =>
        HideNativeScrollBars(IsHandleCreated ? Handle : IntPtr.Zero);

    private static void HideNativeScrollBars(IntPtr handle)
    {
        if (handle != IntPtr.Zero) ShowScrollBar(handle, BothScrollBars, false);
    }

    private RectangleF GetThumbRectangle()
    {
        var trackTop = ScaleLogical(4);
        var trackHeight = Math.Max(1f, ClientSize.Height - ScaleLogical(8));
        var contentHeight = Math.Max(1, _content?.Height ?? 1);
        var thumbHeight = Math.Max(ScaleLogical(MinimumThumbLength), trackHeight * ClientSize.Height / contentHeight);
        thumbHeight = Math.Min(trackHeight, thumbHeight);
        var travel = Math.Max(0, trackHeight - thumbHeight);
        var displayOffset = _dragging && !LiveScrollDuringThumbDrag ? _dragPreviewOffset : _offset;
        var y = trackTop + (MaximumOffset == 0 ? 0 : travel * displayOffset / MaximumOffset);
        var width = ScaleLogical(_hovered || _dragging ? ScrollBarHoverWidth : ScrollBarWidth);
        var x = ClientSize.Width - (ScaleLogical(ScrollBarGutter) + width) / 2f;
        return new RectangleF(x, y, width, thumbHeight);
    }

    private void InvalidateContentMeasure()
    {
        _contentMeasureInvalid = true;
        _measuredContentWidth = -1;
    }

    private int ScaleLogical(int value) => ModernDpi.ScaleToInt(value, DeviceDpi);

    private const int WindowSize = 0x0005;
    private const int WindowNcCalcSize = 0x0083;
    private const int WindowMouseWheel = 0x020A;
    private const int BothScrollBars = 3;
    private const int ExtendedWindowStyleIndex = -20;
    private const int ExtendedStyleComposited = 0x02000000;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawErase = 0x0004;
    private const uint RedrawAllChildren = 0x0080;
    private const uint RedrawUpdateNow = 0x0100;
}
