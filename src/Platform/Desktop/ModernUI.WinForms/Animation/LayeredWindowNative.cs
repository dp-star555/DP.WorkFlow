using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>Encapsulates Win32 operations required by the per-pixel-alpha theme overlay.</summary>
internal static class LayeredWindowNative
{
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    public const int OverlayExtendedStyles = WsExLayered | WsExNoActivate | WsExToolWindow;

    public static Point GetClientScreenOrigin(IntPtr windowHandle)
    {
        var point = new NativePoint();
        if (!ClientToScreen(windowHandle, ref point))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to locate the control client area.");
        return new Point(point.X, point.Y);
    }

    public static void FlushDesktopComposition()
    {
        if (ModernCompatibility.IsWindowsVersionAtLeast(6)) _ = DwmFlush();
    }

    public static void Present(IntPtr windowHandle, Point location, Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

        var memoryDc = IntPtr.Zero;
        var bitmapHandle = IntPtr.Zero;
        var previousObject = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

            bitmapHandle = CreatePArgbDib(memoryDc, bitmap);
            previousObject = SelectObject(memoryDc, bitmapHandle);
            if (previousObject == IntPtr.Zero || previousObject == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());

            var destination = new NativePoint { X = location.X, Y = location.Y };
            var source = new NativePoint();
            var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            if (!UpdateLayeredWindow(windowHandle, screenDc, ref destination, ref size, memoryDc,
                    ref source, 0, ref blend, UlwAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousObject != IntPtr.Zero && previousObject != new IntPtr(-1))
                SelectObject(memoryDc, previousObject);
            if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static IntPtr CreatePArgbDib(IntPtr deviceContext, Bitmap bitmap)
    {
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bitmap.Width,
                Height = -bitmap.Height,
                Planes = 1,
                BitCount = 32
            }
        };
        var handle = CreateDIBSection(deviceContext, ref info, 0, out var pixels, IntPtr.Zero, 0);
        if (handle == IntPtr.Zero || pixels == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        System.Drawing.Imaging.BitmapData? data = null;
        try
        {
            var bounds = new Rectangle(Point.Empty, bitmap.Size);
            data = bitmap.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var rowLength = bitmap.Width * 4;
            var row = new byte[rowLength];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowLength);
                Marshal.Copy(row, 0, pixels + y * rowLength, rowLength);
            }
            return handle;
        }
        catch
        {
            DeleteObject(handle);
            throw;
        }
        finally
        {
            if (data is not null) bitmap.UnlockBits(data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr windowHandle, IntPtr destinationDc,
        ref NativePoint destination, ref NativeSize size, IntPtr sourceDc, ref NativePoint source,
        int colorKey, ref BlendFunction blend, int flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapInfo bitmapInfo,
        uint usage, out IntPtr pixels, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr drawingObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr drawingObject);
}
