param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('All', 'net48', 'net8.0-windows')]
    [string]$TargetFramework = 'All',
    [ValidateSet('All', 'Theme', 'Scroll')]
    [string]$Probe = 'All',
    [ValidateSet(96, 144)]
    [int]$TargetDpi = 96,
    [ValidateSet('Both', 'LightToDark', 'DarkToLight')]
    [string]$ThemeDirection = 'Both',
    [ValidateSet('Animations', 'ReducedMotion', 'Both')]
    [string]$Motion = 'Both',
    [ValidateRange(8, 100)]
    [int]$FrameIntervalMilliseconds = 12,
    [string]$OutputDirectory = '.artifacts/interaction-frame-tests',
    [switch]$NoBuild,
    [switch]$SkipIfDpiUnavailable
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj'
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }

if (-not $NoBuild) {
    & dotnet build $project -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Dual-target Gallery build failed.' }
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies 'System.Drawing.dll' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class InteractionFrameNative
{
    public const int GwlExStyle = -20;
    public const long WsExLayered = 0x00080000L;
    public const long WsExComposited = 0x02000000L;
    public const long WsExNoActivate = 0x08000000L;
    public const uint GwOwner = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    public sealed class MonitorSnapshot
    {
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Dpi;
        public string DeviceName = String.Empty;
    }

    public sealed class PixelSnapshot
    {
        public ulong Hash;
        public int ColorBuckets;
        public int MinimumLuminance;
        public int MaximumLuminance;
        public double BlackRatio;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    private delegate bool EnumChildProc(IntPtr window, IntPtr parameter);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr parameter);

    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window, ref POINT point);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    public static IntPtr[] ProcessWindows(uint targetProcessId, bool visibleOnly)
    {
        var windows = new List<IntPtr>();
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == targetProcessId && (!visibleOnly || IsWindowVisible(window))) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    public static long ExtendedStyle(IntPtr window)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr(window, GwlExStyle).ToInt64() : GetWindowLong(window, GwlExStyle);
    }

    public static int CompositedDescendantCount(IntPtr root)
    {
        return CompositedDescendant(root) == IntPtr.Zero ? 0 : 1;
    }

    public static IntPtr CompositedDescendant(IntPtr root)
    {
        var matched = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr window, IntPtr parameter)
        {
            if ((ExtendedStyle(window) & WsExComposited) == 0) return true;
            matched = window;
            return false;
        }, IntPtr.Zero);
        return matched;
    }

    public static string WindowText(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    public static RECT ClientScreenRect(IntPtr window)
    {
        RECT client;
        GetClientRect(window, out client);
        var origin = new POINT();
        ClientToScreen(window, ref origin);
        return new RECT
        {
            Left = origin.X,
            Top = origin.Y,
            Right = origin.X + client.Right - client.Left,
            Bottom = origin.Y + client.Bottom - client.Top
        };
    }

    public static MonitorSnapshot[] Monitors()
    {
        var result = new List<MonitorSnapshot>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr parameter)
        {
            var info = new MONITORINFOEX { Size = Marshal.SizeOf(typeof(MONITORINFOEX)), DeviceName = String.Empty };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            uint dpiX = 96, dpiY = 96;
            try { if (GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) != 0) dpiX = 96; }
            catch (DllNotFoundException) { dpiX = 96; }
            catch (EntryPointNotFoundException) { dpiX = 96; }
            result.Add(new MonitorSnapshot
            {
                WorkLeft = info.Work.Left, WorkTop = info.Work.Top,
                WorkRight = info.Work.Right, WorkBottom = info.Work.Bottom,
                Dpi = dpiX, DeviceName = info.DeviceName ?? String.Empty
            });
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    public static PixelSnapshot Analyze(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            var buckets = new HashSet<int>();
            var minimum = 255;
            var maximum = 0;
            long black = 0;
            for (var y = 0; y < data.Height; y++)
            {
                var row = y * Math.Abs(data.Stride);
                for (var x = 0; x < data.Width; x++)
                {
                    var index = row + x * 4;
                    var blue = bytes[index];
                    var green = bytes[index + 1];
                    var red = bytes[index + 2];
                    hash = (hash ^ blue) * prime;
                    hash = (hash ^ green) * prime;
                    hash = (hash ^ red) * prime;
                    var luminance = (red * 299 + green * 587 + blue * 114) / 1000;
                    minimum = Math.Min(minimum, luminance);
                    maximum = Math.Max(maximum, luminance);
                    if (red < 4 && green < 4 && blue < 4) black++;
                    buckets.Add(((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4));
                }
            }
            return new PixelSnapshot
            {
                Hash = hash,
                ColorBuckets = buckets.Count,
                MinimumLuminance = minimum,
                MaximumLuminance = maximum,
                BlackRatio = black / (double)(bitmap.Width * bitmap.Height)
            };
        }
        finally { bitmap.UnlockBits(data); }
    }

    public static double MeanAbsoluteDistance(Bitmap left, Bitmap right)
    {
        if (left.Width != right.Width || left.Height != right.Height) throw new ArgumentException("Bitmap sizes differ.");
        var bounds = new Rectangle(0, 0, left.Width, left.Height);
        var leftData = left.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var rightData = right.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var leftBytes = new byte[Math.Abs(leftData.Stride) * leftData.Height];
            var rightBytes = new byte[Math.Abs(rightData.Stride) * rightData.Height];
            Marshal.Copy(leftData.Scan0, leftBytes, 0, leftBytes.Length);
            Marshal.Copy(rightData.Scan0, rightBytes, 0, rightBytes.Length);
            long total = 0;
            for (var y = 0; y < left.Height; y++)
            {
                var leftRow = y * Math.Abs(leftData.Stride);
                var rightRow = y * Math.Abs(rightData.Stride);
                for (var x = 0; x < left.Width; x++)
                {
                    var li = leftRow + x * 4;
                    var ri = rightRow + x * 4;
                    total += Math.Abs(leftBytes[li] - rightBytes[ri]);
                    total += Math.Abs(leftBytes[li + 1] - rightBytes[ri + 1]);
                    total += Math.Abs(leftBytes[li + 2] - rightBytes[ri + 2]);
                }
            }
            return total / (double)(left.Width * left.Height * 3);
        }
        finally
        {
            left.UnlockBits(leftData);
            right.UnlockBits(rightData);
        }
    }
}
'@

[InteractionFrameNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$monitor = [InteractionFrameNative]::Monitors() |
    Where-Object { $_.Dpi -eq $TargetDpi } |
    Sort-Object { - (($_.WorkRight - $_.WorkLeft) * ($_.WorkBottom - $_.WorkTop)) } |
    Select-Object -First 1
if ($null -eq $monitor) {
    if ($SkipIfDpiUnavailable) {
        Write-Host "UI_INTERACTION_FRAMES_SKIPPED dpi=$TargetDpi reason=NoPhysicalMonitor"
        exit 0
    }
    throw "No connected display reports $TargetDpi DPI before HWND creation."
}

$frameworks = if ($TargetFramework -eq 'All') { @('net48', 'net8.0-windows') } else { @($TargetFramework) }
$probes = if ($Probe -eq 'All') { @('Theme', 'Scroll') } else { @($Probe) }
$directions = if ($ThemeDirection -eq 'Both') { @('LightToDark', 'DarkToLight') } else { @($ThemeDirection) }
$motions = if ($Motion -eq 'Both') { @('Animations', 'ReducedMotion') } else { @($Motion) }
New-Item -ItemType Directory -Force $output | Out-Null

function Wait-MainWindow([Diagnostics.Process]$Process) {
    for ($index = 0; $index -lt 200; $index++) {
        if ($Process.HasExited) { throw "Gallery exited with code $($Process.ExitCode)." }
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero -and
            [InteractionFrameNative]::IsWindowVisible($Process.MainWindowHandle)) {
            return $Process.MainWindowHandle
        }
        Start-Sleep -Milliseconds 15
    }
    throw 'Gallery main window did not appear.'
}

function Assert-MainStable([Diagnostics.Process]$Process, [IntPtr]$Main, $InitialRect, [string]$Context) {
    $Process.Refresh()
    $current = New-Object InteractionFrameNative+RECT
    [InteractionFrameNative]::GetWindowRect($Main, [ref]$current) | Out-Null
    if ($Process.MainWindowHandle -ne $Main -or -not [InteractionFrameNative]::IsWindowVisible($Main) -or
        [InteractionFrameNative]::IsIconic($Main) -or $current.Left -ne $InitialRect.Left -or
        $current.Top -ne $InitialRect.Top -or $current.Right -ne $InitialRect.Right -or
        $current.Bottom -ne $InitialRect.Bottom) {
        throw "$Context destabilized the Gallery main HWND: initial=0x$($Main.ToInt64().ToString('X')) processMain=0x$($Process.MainWindowHandle.ToInt64().ToString('X')) visible=$([InteractionFrameNative]::IsWindowVisible($Main)) iconic=$([InteractionFrameNative]::IsIconic($Main)) initialRect=$($InitialRect.Left),$($InitialRect.Top),$($InitialRect.Right),$($InitialRect.Bottom) currentRect=$($current.Left),$($current.Top),$($current.Right),$($current.Bottom)."
    }
}

function Capture-Client([IntPtr]$Window, [string]$Path) {
    $rectangle = New-Object InteractionFrameNative+RECT
    [InteractionFrameNative]::GetClientRect($Window, [ref]$rectangle) | Out-Null
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    $bitmap = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc = $graphics.GetHdc()
        try {
            if (-not [InteractionFrameNative]::PrintWindow($Window, $dc, 3)) { throw 'PrintWindow failed.' }
        }
        finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        return [InteractionFrameNative]::Analyze($bitmap)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Assert-ForegroundStable([IntPtr]$Expected, [string]$Context) {
    $current = [InteractionFrameNative]::GetForegroundWindow()
    for ($attempt = 0; $current -eq [IntPtr]::Zero -and $attempt -lt 10; $attempt++) {
        Start-Sleep -Milliseconds 2
        $current = [InteractionFrameNative]::GetForegroundWindow()
    }
    if ($current -ne $Expected) {
        throw "INTERACTION_FRAME_ENVIRONMENT_INTERFERENCE context=$Context foregroundChanged expected=0x$($Expected.ToInt64().ToString('X')) actual=0x$($current.ToInt64().ToString('X'))"
    }
}

function Find-ThemeOverlay([Diagnostics.Process]$Process, [IntPtr]$Main) {
    foreach ($window in [InteractionFrameNative]::ProcessWindows([uint32]$Process.Id, $true)) {
        if ($window -eq $Main) { continue }
        if (([InteractionFrameNative]::ExtendedStyle($window) -band [InteractionFrameNative]::WsExLayered) -ne 0) {
            return $window
        }
    }
    return [IntPtr]::Zero
}

function Invoke-ThemeProbe([string]$Framework, [string]$Direction, [string]$MotionName, [string]$Exe) {
    $scenarioName = "$Framework-Theme-$Direction-$MotionName-$TargetDpi"
    $scenarioOutput = Join-Path $output $scenarioName
    if (Test-Path $scenarioOutput) { Remove-Item $scenarioOutput -Recurse -Force }
    New-Item -ItemType Directory -Force $scenarioOutput | Out-Null
    $x = $monitor.WorkLeft + 24; $y = $monitor.WorkTop + 24
    $arguments = @('--theme-frame-probe', '--no-activate-probe', "--location=$x,$y")
    if ($Direction -eq 'DarkToLight') { $arguments += '--dark' }
    if ($MotionName -eq 'ReducedMotion') { $arguments += '--no-animation' }
    $foreground = [InteractionFrameNative]::GetForegroundWindow()
    $process = Start-Process $Exe -ArgumentList $arguments -PassThru
    try {
        $main = Wait-MainWindow $process
        if ([InteractionFrameNative]::GetDpiForWindow($main) -ne $TargetDpi) {
            throw "$scenarioName main HWND was not created at $TargetDpi DPI."
        }
        Assert-ForegroundStable $foreground "$scenarioName-startup"
        $mainRect = New-Object InteractionFrameNative+RECT
        $previousRect = New-Object InteractionFrameNative+RECT
        $stableGeometryPasses = 0
        for ($geometryWait = 0; $geometryWait -lt 120 -and $stableGeometryPasses -lt 5; $geometryWait++) {
            [InteractionFrameNative]::GetWindowRect($main, [ref]$mainRect) | Out-Null
            if ($mainRect.Left -eq $previousRect.Left -and $mainRect.Top -eq $previousRect.Top -and
                $mainRect.Right -eq $previousRect.Right -and $mainRect.Bottom -eq $previousRect.Bottom) {
                $stableGeometryPasses++
            } else {
                $stableGeometryPasses = 0
                $previousRect.Left = $mainRect.Left; $previousRect.Top = $mainRect.Top
                $previousRect.Right = $mainRect.Right; $previousRect.Bottom = $mainRect.Bottom
            }
            Start-Sleep -Milliseconds 10
        }
        if ($stableGeometryPasses -lt 5) { throw "$scenarioName initial geometry did not stabilize." }
        $clientRect = [InteractionFrameNative]::ClientScreenRect($main)
        $oldPath = Join-Path $scenarioOutput 'old.png'
        $oldPixels = $null
        for ($baselineWait = 0; $baselineWait -lt 100; $baselineWait++) {
            [InteractionFrameNative]::GetWindowRect($main, [ref]$mainRect) | Out-Null
            $clientRect = [InteractionFrameNative]::ClientScreenRect($main)
            $oldPixels = Capture-Client $main $oldPath
            if ($oldPixels.ColorBuckets -ge 12 -and
                ($oldPixels.MaximumLuminance - $oldPixels.MinimumLuminance) -ge 32 -and
                $oldPixels.BlackRatio -le 0.08) { break }
            Start-Sleep -Milliseconds 10
        }
        if ($null -eq $oldPixels -or $oldPixels.ColorBuckets -lt 12 -or
            ($oldPixels.MaximumLuminance - $oldPixels.MinimumLuminance) -lt 32 -or $oldPixels.BlackRatio -gt 0.08) {
            throw "$scenarioName initial DPI frame was not complete before the theme transaction."
        }

        $overlay = [IntPtr]::Zero
        $sawRunning = $false
        for ($wait = 0; $wait -lt 700; $wait++) {
            $title = [InteractionFrameNative]::WindowText($main)
            if ($title -like 'THEME_FRAME_PROBE_RUNNING*') { $sawRunning = $true }
            $overlay = Find-ThemeOverlay $process $main
            if ($overlay -ne [IntPtr]::Zero) { break }
            if ($title -like 'THEME_FRAME_PROBE_OK*') { break }
            Start-Sleep -Milliseconds 3
        }
        if (-not $sawRunning -and $overlay -eq [IntPtr]::Zero) {
            throw "$scenarioName did not enter the transition transaction."
        }

        $frames = @()
        if ($MotionName -eq 'Animations') {
            if ($overlay -eq [IntPtr]::Zero) { throw "$scenarioName did not create a layered reveal overlay." }
            $overlayStyle = [InteractionFrameNative]::ExtendedStyle($overlay)
            if (($overlayStyle -band [InteractionFrameNative]::WsExNoActivate) -eq 0 -or
                [InteractionFrameNative]::GetWindow($overlay, [InteractionFrameNative]::GwOwner) -ne $main) {
                throw "$scenarioName overlay activation/owner contract is invalid."
            }
            if ([InteractionFrameNative]::GetDpiForWindow($overlay) -ne $TargetDpi) {
                throw "$scenarioName overlay DPI is invalid."
            }
            for ($frame = 0; $frame -lt 80; $frame++) {
                if (-not [InteractionFrameNative]::IsWindowVisible($overlay)) { break }
                Assert-MainStable $process $main $mainRect $scenarioName
                Assert-ForegroundStable $foreground "$scenarioName-frame-$frame"
                $overlayRect = New-Object InteractionFrameNative+RECT
                [InteractionFrameNative]::GetWindowRect($overlay, [ref]$overlayRect) | Out-Null
                if ($overlayRect.Right -le $overlayRect.Left -or $overlayRect.Bottom -le $overlayRect.Top) { break }
                if ($overlayRect.Left -ne $clientRect.Left -or $overlayRect.Top -ne $clientRect.Top -or
                    $overlayRect.Right -ne $clientRect.Right -or $overlayRect.Bottom -ne $clientRect.Bottom) {
                    throw "$scenarioName overlay geometry changed or exposed a client edge: overlay=$($overlayRect.Left),$($overlayRect.Top),$($overlayRect.Right),$($overlayRect.Bottom) client=$($clientRect.Left),$($clientRect.Top),$($clientRect.Right),$($clientRect.Bottom)."
                }
                $frames += [pscustomobject]@{ Frame = $frame; Tick = [Diagnostics.Stopwatch]::GetTimestamp() }
                Start-Sleep -Milliseconds $FrameIntervalMilliseconds
            }
            if ($frames.Count -lt 8) { throw "$scenarioName exposed only $($frames.Count) stable overlay observations." }
        }
        else {
            if ($overlay -ne [IntPtr]::Zero) { throw "$scenarioName created an overlay while reduced motion was active." }
        }

        for ($wait = 0; $wait -lt 200; $wait++) {
            if ([InteractionFrameNative]::WindowText($main) -like 'THEME_FRAME_PROBE_OK*') { break }
            Start-Sleep -Milliseconds 10
        }
        if ([InteractionFrameNative]::WindowText($main) -notlike 'THEME_FRAME_PROBE_OK*') {
            throw "$scenarioName did not reach its final theme."
        }
        Assert-MainStable $process $main $mainRect $scenarioName
        Assert-ForegroundStable $foreground "$scenarioName-final"
        if ((Find-ThemeOverlay $process $main) -ne [IntPtr]::Zero) { throw "$scenarioName left a visible overlay." }
        $finalPath = Join-Path $scenarioOutput 'final.png'
        $finalPixels = Capture-Client $main $finalPath
        $oldBitmap = [Drawing.Bitmap]::FromFile($oldPath)
        $finalBitmap = [Drawing.Bitmap]::FromFile($finalPath)
        try {
            $baselineDistance = [InteractionFrameNative]::MeanAbsoluteDistance($oldBitmap, $finalBitmap)
            if ($baselineDistance -lt 24) { throw "$scenarioName did not produce a meaningful theme change ($baselineDistance)." }
            if ($MotionName -eq 'Animations' -and $frames.Count -lt 8) {
                throw "$scenarioName did not keep a stable overlay for the transition lifetime."
            }
        }
        finally { $oldBitmap.Dispose(); $finalBitmap.Dispose() }
        Write-Host "THEME_INTERACTION_FRAMES_OK framework=$Framework direction=$Direction motion=$MotionName dpi=$TargetDpi frames=$($frames.Count)"
        return [pscustomobject]@{ Framework=$Framework; Probe='Theme'; Scenario="$Direction/$MotionName"; Dpi=$TargetDpi; Frames=$frames.Count }
    }
    finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() } }
}

function Invoke-ScrollProbe([string]$Framework, [string]$Exe) {
    $scenarioName = "$Framework-Scroll-$TargetDpi"
    $scenarioOutput = Join-Path $output $scenarioName
    if (Test-Path $scenarioOutput) { Remove-Item $scenarioOutput -Recurse -Force }
    New-Item -ItemType Directory -Force $scenarioOutput | Out-Null
    $resultPath = Join-Path $scenarioOutput 'result.txt'
    $x = $monitor.WorkLeft + 24; $y = $monitor.WorkTop + 24
    $arguments = @('--no-animation', '--no-activate-probe', '--demo=NavigationAndLayout', "--location=$x,$y",
        "--scroll-frame-probe=$([Uri]::EscapeDataString($resultPath))")
    $foreground = [InteractionFrameNative]::GetForegroundWindow()
    $process = Start-Process $Exe -ArgumentList $arguments -PassThru
    try {
        $main = Wait-MainWindow $process
        if ([InteractionFrameNative]::GetDpiForWindow($main) -ne $TargetDpi) {
            throw "$scenarioName main HWND was not created at $TargetDpi DPI."
        }
        Assert-ForegroundStable $foreground "$scenarioName-startup"
        if ([InteractionFrameNative]::CompositedDescendantCount($main) -ne 0) {
            throw "$scenarioName enabled WS_EX_COMPOSITED before thumb drag."
        }
        $frames = @()
        $offsets = @()
        $sawComposited = $false
        for ($wait = 0; $wait -lt 500; $wait++) {
            $title = [InteractionFrameNative]::WindowText($main)
            if ($title -like 'SCROLL_FRAME_PROBE step=*') { break }
            if (Test-Path $resultPath) { break }
            Start-Sleep -Milliseconds 3
        }
        # The Gallery may still be finishing its delayed initial DPI/layout transaction while the
        # probe delay runs. The drag-frame contract begins when the first synthetic thumb message
        # is visible in the title; record stable owner geometry at that exact boundary.
        $mainRect = New-Object InteractionFrameNative+RECT
        [InteractionFrameNative]::GetWindowRect($main, [ref]$mainRect) | Out-Null
        for ($frame = 0; $frame -lt 80; $frame++) {
            $title = [InteractionFrameNative]::WindowText($main)
            if ($title -like 'SCROLL_FRAME_PROBE_OK*' -or $title -eq 'SCROLL_FRAME_PROBE_FAILED') { break }
            if ($title -notmatch '^SCROLL_FRAME_PROBE step=(\d+) offset=(\d+)$') {
                Start-Sleep -Milliseconds 3
                continue
            }
            Assert-MainStable $process $main $mainRect $scenarioName
            Assert-ForegroundStable $foreground "$scenarioName-frame-$frame"
            $offset = [int]$Matches[2]
            $viewport = [InteractionFrameNative]::CompositedDescendant($main)
            if ($viewport -ne [IntPtr]::Zero) { $sawComposited = $true }
            else { throw "$scenarioName lost WS_EX_COMPOSITED during live drag." }
            $path = Join-Path $scenarioOutput ("{0:D2}-offset-{1}.png" -f $frame, $offset)
            $pixels = Capture-Client $viewport $path
            if ($pixels.ColorBuckets -lt 12 -or ($pixels.MaximumLuminance - $pixels.MinimumLuminance) -lt 32 -or
                $pixels.BlackRatio -gt 0.08) {
                throw "$scenarioName exposed an incomplete/black drag frame at offset $offset."
            }
            $frames += [pscustomobject]@{ Offset=$offset; Hash=$pixels.Hash; Path=$path }
            $offsets += $offset
            Start-Sleep -Milliseconds $FrameIntervalMilliseconds
        }
        for ($wait = 0; $wait -lt 200 -and -not (Test-Path $resultPath); $wait++) { Start-Sleep -Milliseconds 10 }
        if (-not (Test-Path $resultPath)) { throw "$scenarioName did not write its result." }
        $result = Get-Content $resultPath -Raw
        if ($result -notlike 'SCROLL_FRAME_PROBE_OK*') { throw "$scenarioName failed: $result" }
        Assert-MainStable $process $main $mainRect $scenarioName
        Assert-ForegroundStable $foreground "$scenarioName-final"
        if (-not $sawComposited) { throw "$scenarioName never enabled WS_EX_COMPOSITED during live drag." }
        if ([InteractionFrameNative]::CompositedDescendantCount($main) -ne 0) {
            throw "$scenarioName left WS_EX_COMPOSITED enabled after thumb release."
        }
        $resultOffsets = @([regex]::Matches($result, '\d+:(\d+)') | ForEach-Object { [int]$_.Groups[1].Value })
        if ($resultOffsets.Count -ne 18) { throw "$scenarioName reported $($resultOffsets.Count) offsets, expected 18." }
        for ($index = 1; $index -lt $resultOffsets.Count; $index++) {
            if ($resultOffsets[$index] -le $resultOffsets[$index - 1]) {
                throw "$scenarioName offsets are not strictly increasing."
            }
        }
        for ($index = 1; $index -lt $offsets.Count; $index++) {
            if ($offsets[$index] -lt $offsets[$index - 1]) { throw "$scenarioName captured a backward scroll frame." }
        }
        $distinctOffsets = @($offsets | Select-Object -Unique)
        $distinctHashes = @($frames | ForEach-Object { $_.Hash.ToString('X16') } | Select-Object -Unique)
        if ($distinctOffsets.Count -lt 5 -or $distinctHashes.Count -lt 5) {
            throw "$scenarioName captured too few distinct drag frames: offsets=$($distinctOffsets.Count) hashes=$($distinctHashes.Count)."
        }
        Write-Host "SCROLL_INTERACTION_FRAMES_OK framework=$Framework dpi=$TargetDpi frames=$($frames.Count) finalOffset=$($resultOffsets[-1])"
        return [pscustomobject]@{ Framework=$Framework; Probe='Scroll'; Scenario='LiveThumbDrag'; Dpi=$TargetDpi; Frames=$frames.Count }
    }
    finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() } }
}

$results = @()
foreach ($framework in $frameworks) {
    $exe = Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/$framework/ModernUI.WinForms.Gallery.exe"
    if (-not (Test-Path $exe)) { throw "Gallery output missing: $exe" }
    if ($probes -contains 'Theme') {
        foreach ($direction in $directions) {
            foreach ($motionName in $motions) {
                $results += Invoke-ThemeProbe $framework $direction $motionName $exe
            }
        }
    }
    if ($probes -contains 'Scroll') { $results += Invoke-ScrollProbe $framework $exe }
}

$results | Format-Table -AutoSize
Write-Host "UI_INTERACTION_FRAMES_OK frameworks=$($frameworks -join ',') probes=$($probes -join ',') dpi=$TargetDpi scenarios=$($results.Count)"
