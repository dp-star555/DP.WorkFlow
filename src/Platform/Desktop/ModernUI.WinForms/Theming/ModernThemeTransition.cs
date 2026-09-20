using System.Drawing.Imaging;

namespace ModernUI.WinForms;

/// <summary>Coordinates animated theme changes for an existing WinForms control tree.</summary>
public static class ModernThemeTransition
{
    private static readonly Dictionary<Control, ThemeRevealOverlay> ActiveTransitions = [];

    /// <summary>Applies a theme change while revealing the live new theme from a circular origin.</summary>
    /// <param name="root">The root control whose client area is transitioned.</param>
    /// <param name="origin">The reveal origin in the root control's client coordinates.</param>
    /// <param name="applyTheme">The synchronous action that applies the new theme.</param>
    /// <param name="duration">The transition duration in milliseconds.</param>
    public static void Reveal(Control root, Point origin, Action applyTheme, int duration = 320)
    {
        ModernCompatibility.ThrowIfNull(root, nameof(root));
        ModernCompatibility.ThrowIfNull(applyTheme, nameof(applyTheme));

        CancelActiveTransition(root);
        if (root.IsHandleCreated) root.Update();

        if (!CanAnimate(root, duration))
        {
            applyTheme();
            return;
        }

        using var oldFrame = TryCaptureClient(root);
        if (oldFrame is null)
        {
            applyTheme();
            return;
        }

        var overlay = new ThemeRevealOverlay(root, oldFrame, origin)
        {
            Location = LayeredWindowNative.GetClientScreenOrigin(root.Handle),
            ClientSize = root.ClientSize
        };
        overlay.FormClosed += (_, _) => ReleaseTransition(root, overlay);
        RegisterTransition(root, overlay);

        try
        {
            overlay.Show(root.FindForm());
            overlay.PresentInitialFrame();
            applyTheme();
            root.Update();
            overlay.Start(duration);
        }
        catch
        {
            ReleaseTransition(root, overlay);
            overlay.Cancel();
            overlay.Dispose();
            throw;
        }
    }

    private static bool CanAnimate(Control root, int duration) =>
        ModernUiSettings.EffectiveAnimationsEnabled && duration > 0 && root.IsHandleCreated &&
        root.ClientSize.Width > 0 && root.ClientSize.Height > 0;

    private static Bitmap? TryCaptureClient(Control root)
    {
        var size = root.ClientSize;
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(LayeredWindowNative.GetClientScreenOrigin(root.Handle), Point.Empty, bitmap.Size,
                CopyPixelOperation.SourceCopy);
            EnsureOpaque(bitmap);
            return bitmap;
        }
        catch
        {
            try
            {
                root.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
                EnsureOpaque(bitmap);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                return null;
            }
        }
    }

    private static void EnsureOpaque(Bitmap bitmap)
    {
        var bounds = new Rectangle(Point.Empty, bitmap.Size);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var address = data.Scan0 + y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(address, row, 0, row.Length);
                for (var x = 3; x < bitmap.Width * 4; x += 4) row[x] = byte.MaxValue;
                System.Runtime.InteropServices.Marshal.Copy(row, 0, address, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void RegisterTransition(Control root, ThemeRevealOverlay overlay)
    {
        lock (ActiveTransitions) ActiveTransitions[root] = overlay;
    }

    private static void CancelActiveTransition(Control root)
    {
        ThemeRevealOverlay? overlay;
        lock (ActiveTransitions) ActiveTransitions.Remove(root, out overlay);
        overlay?.Cancel();
    }

    private static void ReleaseTransition(Control root, ThemeRevealOverlay overlay)
    {
        lock (ActiveTransitions)
        {
            if (ActiveTransitions.TryGetValue(root, out var current) && ReferenceEquals(current, overlay))
                ActiveTransitions.Remove(root);
        }
    }
}
