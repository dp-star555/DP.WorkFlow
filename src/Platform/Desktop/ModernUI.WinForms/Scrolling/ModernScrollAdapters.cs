using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>Immutable position model shared by native and managed scrolling implementations.</summary>
internal readonly record struct ModernScrollState(int Minimum, int MaximumPosition, int Position, int PageSize, int Range)
{
    public bool HasRange => MaximumPosition > Minimum;
    public bool CanScroll(int delta) => delta > 0 ? Position > Minimum : delta < 0 && Position < MaximumPosition;
}

/// <summary>Internal seam used by the modern scroll chrome; callers never need to know the HWND protocol.</summary>
internal interface IModernScrollAdapter
{
    Control Target { get; }
    bool IsDirectionReversed { get; }
    bool TryGetState(out ModernScrollState state);
    void SetPosition(int position);
    void EndScroll();
    void SynchronizeChrome();
}

/// <summary>Adapts a Win32 scrollbar exposed through GetScrollInfo/WM_VSCROLL/WM_HSCROLL.</summary>
internal sealed class NativeWindowScrollAdapter : IModernScrollAdapter
{
    private readonly bool _vertical;
    private readonly bool _hideNativeChrome;
    private readonly Action<int>? _setPosition;
    private readonly bool _reverseInRightToLeft;

    public NativeWindowScrollAdapter(Control target, bool vertical, bool hideNativeChrome = true,
        Action<int>? setPosition = null, bool reverseInRightToLeft = false)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        _vertical = vertical;
        _hideNativeChrome = hideNativeChrome;
        _setPosition = setPosition;
        _reverseInRightToLeft = reverseInRightToLeft;
    }

    public Control Target { get; }
    public bool IsDirectionReversed => !_vertical && _reverseInRightToLeft && Target.RightToLeft == RightToLeft.Yes;

    public bool TryGetState(out ModernScrollState state) =>
        ModernNativeScrollProtocol.TryGetState(Target, _vertical, out state);

    public void SetPosition(int position)
    {
        if (_setPosition is not null)
        {
            _setPosition(position);
            return;
        }
        ModernNativeScrollProtocol.SendScroll(Target, _vertical, ModernNativeScrollProtocol.ThumbTrack, position);
    }

    public void EndScroll() =>
        ModernNativeScrollProtocol.SendScroll(Target, _vertical, ModernNativeScrollProtocol.EndScroll, 0);

    public void SynchronizeChrome()
    {
        if (_hideNativeChrome) ModernNativeScrollProtocol.HideNativeBar(Target, _vertical);
    }
}

/// <summary>Adapts controls whose real position is exposed by managed state or nonstandard Win32 messages.</summary>
internal sealed class ManagedControlScrollAdapter : IModernScrollAdapter
{
    private readonly Func<int> _position;
    private readonly Func<int> _maximumPosition;
    private readonly Func<int> _pageSize;
    private readonly Action<int> _setPosition;
    private readonly bool _hideNativeChrome;
    private readonly bool _reverseInRightToLeft;

    public ManagedControlScrollAdapter(Control target, Func<int> position, Func<int> maximumPosition,
        Func<int> pageSize, Action<int> setPosition, bool hideNativeChrome = false,
        bool reverseInRightToLeft = false)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        _position = position ?? throw new ArgumentNullException(nameof(position));
        _maximumPosition = maximumPosition ?? throw new ArgumentNullException(nameof(maximumPosition));
        _pageSize = pageSize ?? throw new ArgumentNullException(nameof(pageSize));
        _setPosition = setPosition ?? throw new ArgumentNullException(nameof(setPosition));
        _hideNativeChrome = hideNativeChrome;
        _reverseInRightToLeft = reverseInRightToLeft;
    }

    public Control Target { get; }
    public bool IsDirectionReversed => _reverseInRightToLeft && Target.RightToLeft == RightToLeft.Yes;

    public bool TryGetState(out ModernScrollState state)
    {
        if (!Target.IsHandleCreated)
        {
            state = default;
            return false;
        }
        const int minimum = 0;
        var maximum = Math.Max(minimum, _maximumPosition());
        var page = Math.Max(1, _pageSize());
        var position = ModernCompatibility.Clamp(_position(), minimum, maximum);
        state = new ModernScrollState(minimum, maximum, position, page, Math.Max(1, maximum + page));
        return state.HasRange;
    }

    public void SetPosition(int position) => _setPosition(position);
    public void EndScroll() { }
    public void SynchronizeChrome()
    {
        if (_hideNativeChrome) ModernNativeScrollProtocol.HideNativeBar(Target, vertical: true);
    }
}

/// <summary>Contains the only GetScrollInfo and native scroll-message implementation used by the framework.</summary>
internal static class ModernNativeScrollProtocol
{
    internal const int ThumbTrack = 5;
    internal const int First = 6;
    internal const int EndScroll = 8;
    private const int HorizontalBar = 0;
    private const int VerticalBar = 1;
    private const int WindowStyle = -16;
    private const int HorizontalScrollStyle = 0x00100000;
    private const int VerticalScrollStyle = 0x00200000;
    private const uint ScrollInfoAll = 0x17;
    private const int WindowHorizontalScroll = 0x0114;
    private const int WindowVerticalScroll = 0x0115;

    public static bool TryGetState(Control target, bool vertical, out ModernScrollState state)
    {
        state = default;
        if (!target.IsHandleCreated) return false;
        var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = ScrollInfoAll };
        if (!GetScrollInfo(target.Handle, vertical ? VerticalBar : HorizontalBar, ref info) || info.Page == 0)
            return false;
        var maximumPosition = Math.Max(info.Minimum, info.Maximum - Math.Max(0, (int)info.Page - 1));
        state = new ModernScrollState(info.Minimum, maximumPosition,
            ModernCompatibility.Clamp(info.Position, info.Minimum, maximumPosition),
            Math.Max(1, (int)info.Page), Math.Max(1, info.Maximum - info.Minimum + 1));
        return state.HasRange;
    }

    public static bool CanScroll(Control target, bool vertical, int delta) =>
        TryGetState(target, vertical, out var state) && state.CanScroll(delta);

    public static void SendScroll(Control target, bool vertical, int command, int position)
    {
        if (!target.IsHandleCreated) return;
        var parameter = command | ((position & 0xFFFF) << 16);
        SendMessage(target.Handle, vertical ? WindowVerticalScroll : WindowHorizontalScroll,
            (IntPtr)parameter, IntPtr.Zero);
    }

    public static bool HasNativeBar(Control target, bool vertical)
    {
        if (!target.IsHandleCreated) return false;
        int styleBit = vertical ? VerticalScrollStyle : HorizontalScrollStyle;
        return (GetWindowLong(target.Handle, WindowStyle) & styleBit) != 0;
    }

    public static void HideNativeBar(Control target, bool vertical)
    {
        if (!target.IsHandleCreated) return;

        const int scrollStyles = HorizontalScrollStyle | VerticalScrollStyle;
        int style = GetWindowLong(target.Handle, WindowStyle);
        if ((style & scrollStyles) != 0)
        {
            _ = SetWindowLong(target.Handle, WindowStyle, style & ~scrollStyles);
            const uint frameChanged = 0x0020;
            const uint noMove = 0x0002;
            const uint noSize = 0x0001;
            const uint noActivate = 0x0010;
            _ = SetWindowPos(target.Handle, IntPtr.Zero, 0, 0, 0, 0,
                frameChanged | noMove | noSize | noActivate);

            int restoredStyle = GetWindowLong(target.Handle, WindowStyle);
            if ((restoredStyle & scrollStyles) != 0)
                _ = SetWindowLong(target.Handle, WindowStyle, restoredStyle & ~scrollStyles);
        }

        const int bothBars = 3;
        _ = ShowScrollBar(target.Handle, bothBars, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint Size;
        public uint Mask;
        public int Minimum;
        public int Maximum;
        public uint Page;
        public int Position;
        public int TrackPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);
}
