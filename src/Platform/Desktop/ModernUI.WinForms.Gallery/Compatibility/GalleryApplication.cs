using System.Runtime.InteropServices;

namespace ModernUI.WinForms.Gallery;

internal static class GalleryApplication
{
    internal static bool ShowWithoutActivationForProbe { get; set; }

    public static void ConfigureLogicalSize(Form form, Size logicalClientSize, Size logicalMinimumSize)
    {
        var initialized = false;
        var dpiTransition = false;
        var transitionVersion = 0;
        var currentLogicalClientSize = logicalClientSize;
        form.Load += (_, _) =>
        {
            var managedDpi = form.DeviceDpi > 0 ? form.DeviceDpi : 96;
            var nativeDpi = GetNativeWindowDpi(form);
            var currentDpi = managedDpi;
            var scale = currentDpi / 96d;
            form.SuspendLayout();
            if (Math.Abs(scale - 1d) > .001d)
                form.Scale(new SizeF((float)scale, (float)scale));
            form.ClientSize = Scale(currentLogicalClientSize, scale);
            form.MinimumSize = Scale(logicalMinimumSize, scale);
            // Native auto-complete HWND creation is deferred by ModernComboBox, so it is now safe
            // to establish WinForms' optimized DPI baseline after the complete tree has loaded.
            form.AutoScaleDimensions = new SizeF(currentDpi, currentDpi);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ResumeLayout(true);
            initialized = true;

            // .NET Framework initializes Control.DeviceDpi from the system-DPI monitor. If the
            // first HWND is created directly on another monitor, the native window already has the
            // correct DPI but the managed cache remains stale until the first WM_DPICHANGED. The
            // old cache made the initial Gallery 1.5x too large at 96 DPI. Reproduce the missing
            // transition after all child handles are stable; GalleryDpiForm keeps it off screen
            // until the corrected complete frame is ready.
            if (nativeDpi != managedDpi)
            {
                dpiTransition = true;
                var previousOpacity = form.Opacity;
                // No-activate frame probes must not use a transparent startup staging window:
                // net48 can leave that HWND without a visible DWM surface until activation. The
                // probe waits for this same DPI transaction to finish before it captures frames.
                if (!ShowWithoutActivationForProbe) form.Opacity = 0;
                form.BeginInvoke((Action)(() => CorrectInitialManagedDpi(form, nativeDpi,
                    currentLogicalClientSize, logicalMinimumSize, previousOpacity,
                    () => dpiTransition = false)));
            }
        };
        // Only persist an explicit user resize. Resize also fires for the intermediate bounds that
        // WinForms applies inside WM_DPICHANGED; treating those as a new logical size feeds the
        // scaled bounds back into the next transition and progressively enlarges the window.
        form.ResizeEnd += (_, _) =>
        {
            if (!initialized || dpiTransition || form.WindowState != FormWindowState.Normal) return;
            var dpi = form.DeviceDpi > 0 ? form.DeviceDpi : 96;
            currentLogicalClientSize = new Size(
                (int)Math.Round(form.ClientSize.Width * 96d / dpi),
                (int)Math.Round(form.ClientSize.Height * 96d / dpi));
        };
        form.DpiChanged += (_, eventArgs) =>
        {
            if (!initialized || eventArgs.DeviceDpiNew <= 0) return;
            dpiTransition = true;
            var version = ++transitionVersion;
            form.BeginInvoke((Action)(() =>
            {
                if (form.IsDisposed || version != transitionVersion) return;
                var scale = eventArgs.DeviceDpiNew / 96d;
                form.ClientSize = Scale(currentLogicalClientSize, scale);
                form.MinimumSize = Scale(logicalMinimumSize, scale);
                dpiTransition = false;
            }));
        };
    }

    private static Size Scale(Size logicalSize, double scale) => new(
        (int)Math.Round(logicalSize.Width * scale),
        (int)Math.Round(logicalSize.Height * scale));

    private static int GetNativeWindowDpi(Form form)
    {
        if (!form.IsHandleCreated) return form.DeviceDpi > 0 ? form.DeviceDpi : 96;
        var dpi = GetDpiForWindow(form.Handle);
        return dpi > 0 ? (int)dpi : form.DeviceDpi > 0 ? form.DeviceDpi : 96;
    }

    private static void CorrectInitialManagedDpi(Form form, int nativeDpi, Size logicalClientSize,
        Size logicalMinimumSize, double previousOpacity, Action completed)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
        {
            completed();
            return;
        }

        var managedDpi = form.DeviceDpi > 0 ? form.DeviceDpi : 96;
        if (managedDpi != nativeDpi)
        {
            var ratio = nativeDpi / (double)managedDpi;
            var bounds = form.Bounds;
            var suggested = new NativeRect
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Right = bounds.Left + Math.Max(1, (int)Math.Round(bounds.Width * ratio)),
                Bottom = bounds.Top + Math.Max(1, (int)Math.Round(bounds.Height * ratio))
            };
            var rectangle = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
            try
            {
                Marshal.StructureToPtr(suggested, rectangle, false);
                var packedDpi = new IntPtr(nativeDpi | nativeDpi << 16);
                SendMessage(form.Handle, 0x02E0, packedDpi, rectangle);
            }
            finally { Marshal.FreeHGlobal(rectangle); }
        }

        form.BeginInvoke((Action)(() =>
        {
            if (!form.IsDisposed)
            {
                var scale = nativeDpi / 96d;
                form.ClientSize = Scale(logicalClientSize, scale);
                form.MinimumSize = Scale(logicalMinimumSize, scale);
                form.Opacity = previousOpacity;
            }
            completed();
        }));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    public static void Initialize()
    {
#if NET8_0_OR_GREATER
        ApplicationConfiguration.Initialize();
#else
        // On .NET Framework the App.config DpiAwareness entry must own process DPI setup.
        // Calling SetProcessDpiAwarenessContext first leaves WinForms' managed DeviceDpi cache
        // at the system DPI while the native HWND is already PerMonitorV2-aware.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#endif
    }
}
