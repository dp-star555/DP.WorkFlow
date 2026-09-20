using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

public sealed partial class ModernScrollView
{
    private sealed class CompositedRenderingScope(ModernScrollView owner) : IDisposable
    {
        private ModernScrollView? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ResumeCompositedRendering();
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) => IntPtr.Size == 8
        ? GetWindowLongPtr64(window, index)
        : new IntPtr(GetWindowLong32(window, index));

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue) => IntPtr.Size == 8
        ? SetWindowLongPtr64(window, index, newValue)
        : new IntPtr(SetWindowLong32(window, index, newValue.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr window, int index, int newValue);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle,
        IntPtr updateRegion, uint flags);
}
