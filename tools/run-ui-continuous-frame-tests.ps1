param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('All', 'net48', 'net8.0-windows')]
    [string]$TargetFramework = 'All',
    [ValidateSet('All', 'ComboBox', 'Select', 'DatePicker', 'ToolTip')]
    [string]$Probe = 'All',
    [ValidateSet(96, 144)]
    [int]$TargetDpi = 96,
    [ValidateRange(6, 120)]
    [int]$FrameCount = 18,
    [ValidateRange(8, 100)]
    [int]$FrameIntervalMilliseconds = 12,
    [string]$OutputDirectory = '.artifacts/continuous-frame-tests',
    [switch]$RightToLeft,
    [switch]$NoBuild,
    [switch]$SkipIfDpiUnavailable
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj'
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $root $OutputDirectory
}

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

public static class ContinuousFrameNative
{
    public const uint GwOwner = 4;
    public const uint GaRoot = 2;
    public const uint GaRootOwner = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int Size;
        public RECT Item;
        public RECT Button;
        public int ButtonState;
        public IntPtr Combo;
        public IntPtr ItemWindow;
        public IntPtr List;
    }

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
        public IntPtr Handle;
        public int Left, Top, Right, Bottom;
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
    }

    public sealed class ComboSnapshot
    {
        public IntPtr Combo;
        public IntPtr List;
        public int Width;
        public int Height;
        public long DroppedWidth;
        public uint Dpi;
        public int Awareness;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    private delegate bool EnumChildProc(IntPtr window, IntPtr parameter);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr parameter);

    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool PtVisible(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr combo, ref COMBOBOXINFO info);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    public static IntPtr[] ProcessWindows(uint targetProcessId, bool visibleOnly)
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, parameter) =>
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == targetProcessId && (!visibleOnly || IsWindowVisible(window))) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    public static string WindowClass(IntPtr window)
    {
        var text = new StringBuilder(128);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    public static bool IsComboListForRoot(IntPtr list, IntPtr root)
    {
        return FindComboListForRoot(list, root) != null;
    }

    public static ComboSnapshot FindComboListForRoot(IntPtr list, IntPtr root)
    {
        ComboSnapshot matched = null;
        EnumChildWindows(root, (window, parameter) =>
        {
            if (WindowClass(window).IndexOf("COMBOBOX", StringComparison.OrdinalIgnoreCase) < 0) return true;
            var info = new COMBOBOXINFO { Size = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
            if (!GetComboBoxInfo(window, ref info) || info.List != list) return true;
            RECT rectangle;
            GetWindowRect(window, out rectangle);
            var awareness = -1;
            try { awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(window)); }
            catch (EntryPointNotFoundException) { }
            matched = new ComboSnapshot
            {
                Combo = window,
                List = info.List,
                Width = rectangle.Right - rectangle.Left,
                Height = rectangle.Bottom - rectangle.Top,
                DroppedWidth = SendMessage(window, 0x015f, IntPtr.Zero, IntPtr.Zero).ToInt64(),
                Dpi = GetDpiForWindow(window),
                Awareness = awareness
            };
            return false;
        }, IntPtr.Zero);
        return matched;
    }

    public static MonitorSnapshot[] Monitors()
    {
        var result = new List<MonitorSnapshot>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, parameter) =>
        {
            var info = new MONITORINFOEX { Size = Marshal.SizeOf(typeof(MONITORINFOEX)), DeviceName = String.Empty };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            uint dpiX = 96, dpiY = 96;
            try { if (GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) != 0) dpiX = 96; }
            catch (DllNotFoundException) { dpiX = 96; }
            catch (EntryPointNotFoundException) { dpiX = 96; }
            result.Add(new MonitorSnapshot
            {
                Handle = monitor,
                Left = info.Monitor.Left, Top = info.Monitor.Top,
                Right = info.Monitor.Right, Bottom = info.Monitor.Bottom,
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
            var length = Math.Abs(data.Stride) * data.Height;
            var bytes = new byte[length];
            Marshal.Copy(data.Scan0, bytes, 0, length);
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            var buckets = new HashSet<int>();
            var minimum = 255;
            var maximum = 0;
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
                    buckets.Add(((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4));
                }
            }
            return new PixelSnapshot
            {
                Hash = hash,
                ColorBuckets = buckets.Count,
                MinimumLuminance = minimum,
                MaximumLuminance = maximum
            };
        }
        finally { bitmap.UnlockBits(data); }
    }
}
'@

# GetWindowRect and CopyFromScreen must remain in physical coordinates.
[ContinuousFrameNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

$monitor = [ContinuousFrameNative]::Monitors() |
    Where-Object { $_.Dpi -eq $TargetDpi } |
    Sort-Object { - (($_.WorkRight - $_.WorkLeft) * ($_.WorkBottom - $_.WorkTop)) } |
    Select-Object -First 1
if ($null -eq $monitor) {
    if ($SkipIfDpiUnavailable) {
        Write-Host "UI_CONTINUOUS_FRAMES_SKIPPED dpi=$TargetDpi reason=NoPhysicalMonitor"
        exit 0
    }
    throw "No connected display reports $TargetDpi DPI before HWND creation."
}

$frameworks = if ($TargetFramework -eq 'All') { @('net48', 'net8.0-windows') } else { @($TargetFramework) }
$probes = if ($Probe -eq 'All') { @('ComboBox', 'Select', 'DatePicker', 'ToolTip') } else { @($Probe) }
$scenarios = @{
    ComboBox = @{ Page = 'Inputs'; Scroll = 800; Argument = 'combobox' }
    Select = @{ Page = 'Selection'; Scroll = 0; Argument = 'select' }
    DatePicker = @{ Page = 'DateAndTime'; Scroll = 0; Argument = 'datepicker' }
    ToolTip = @{ Page = 'FeedbackAndOverlays'; Scroll = 800; Argument = 'tooltip' }
}
New-Item -ItemType Directory -Force $output | Out-Null

function Wait-MainWindow([Diagnostics.Process]$Process) {
    for ($index = 0; $index -lt 160; $index++) {
        if ($Process.HasExited) { throw "Gallery exited with code $($Process.ExitCode)." }
        $Process.Refresh()
        $window = $Process.MainWindowHandle
        if ($window -ne [IntPtr]::Zero -and [ContinuousFrameNative]::IsWindowVisible($window)) { return $window }
        Start-Sleep -Milliseconds 20
    }
    throw 'Gallery main window did not appear.'
}

function Find-Popup([Diagnostics.Process]$Process, [IntPtr]$MainWindow, [string]$ProbeName, [bool]$VisibleOnly) {
    foreach ($window in [ContinuousFrameNative]::ProcessWindows([uint32]$Process.Id, $VisibleOnly)) {
        if ($window -eq $MainWindow) { continue }
        $className = [ContinuousFrameNative]::WindowClass($window)
        if ($ProbeName -eq 'ComboBox' -and $className -eq 'ComboLBox') { continue }
        if ($className -ne 'SysMonthCal32') { return $window }
    }
    return [IntPtr]::Zero
}

function Assert-RoundedPopup([IntPtr]$Window, [string]$Scenario) {
    $region = [ContinuousFrameNative]::CreateRectRgn(0, 0, 1, 1)
    if ($region -eq [IntPtr]::Zero) { throw "$Scenario could not allocate a region probe." }
    try {
        if ([ContinuousFrameNative]::GetWindowRgn($Window, $region) -le 0) {
            throw "$Scenario popup has no rounded window region."
        }
        $dc = [ContinuousFrameNative]::GetDC($Window)
        if ($dc -eq [IntPtr]::Zero) { throw "$Scenario could not inspect its popup region." }
        try {
            if ([ContinuousFrameNative]::PtVisible($dc, 0, 0)) {
                throw "$Scenario popup still exposes its rectangular top-left corner."
            }
        }
        finally { [ContinuousFrameNative]::ReleaseDC($Window, $dc) | Out-Null }
    }
    finally { [ContinuousFrameNative]::DeleteObject($region) | Out-Null }
}

function Capture-Popup([IntPtr]$Window, [string]$Path) {
    $rectangle = New-Object ContinuousFrameNative+RECT
    if (-not [ContinuousFrameNative]::GetWindowRect($Window, [ref]$rectangle)) {
        throw "GetWindowRect failed for 0x$($Window.ToInt64().ToString('X'))."
    }
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    if ($width -le 1 -or $height -le 1) { throw "Popup has invalid geometry ${width}x${height}." }
    $bitmap = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc = $graphics.GetHdc()
        try {
            if (-not [ContinuousFrameNative]::PrintWindow($Window, $dc, 2)) {
                throw "PrintWindow failed for 0x$($Window.ToInt64().ToString('X'))."
            }
        }
        finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        $pixels = [ContinuousFrameNative]::Analyze($bitmap)
        return [pscustomobject]@{
            Left = $rectangle.Left; Top = $rectangle.Top; Width = $width; Height = $height
            Hash = $pixels.Hash; ColorBuckets = $pixels.ColorBuckets
            MinimumLuminance = $pixels.MinimumLuminance; MaximumLuminance = $pixels.MaximumLuminance
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$results = @()
foreach ($framework in $frameworks) {
    $exe = Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/$framework/ModernUI.WinForms.Gallery.exe"
    if (-not (Test-Path $exe)) { throw "Gallery output missing: $exe" }
    foreach ($probeName in $probes) {
        $scenario = $scenarios[$probeName]
        $directionSuffix = if ($RightToLeft) { '-RTL' } else { '' }
        $scenarioOutput = Join-Path $output "$framework-$probeName-$TargetDpi$directionSuffix"
        if (Test-Path $scenarioOutput) { Remove-Item $scenarioOutput -Recurse -Force }
        New-Item -ItemType Directory -Force $scenarioOutput | Out-Null
        $x = $monitor.WorkLeft + 24
        $y = $monitor.WorkTop + 24
        $arguments = @(
            '--no-animation',
            "--demo=$($scenario.Page)",
            "--scroll=$($scenario.Scroll)",
            "--location=$x,$y",
            "--frame-probe=$($scenario.Argument)"
        )
        if ($RightToLeft) { $arguments += '--rtl' }
        $process = Start-Process $exe -ArgumentList $arguments -PassThru
        try {
            $main = Wait-MainWindow $process
            $mainDpi = [ContinuousFrameNative]::GetDpiForWindow($main)
            if ($mainDpi -ne $TargetDpi) {
                throw "$framework $probeName main HWND was created at $mainDpi DPI, expected $TargetDpi."
            }
            $popup = [IntPtr]::Zero
            for ($wait = 0; $wait -lt 500 -and $popup -eq [IntPtr]::Zero; $wait++) {
                $popup = Find-Popup $process $main $probeName $true
                if ($popup -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 4 }
            }
            if ($popup -eq [IntPtr]::Zero) {
                $created = Find-Popup $process $main $probeName $false
                $foreground = [ContinuousFrameNative]::GetForegroundWindow()
                if ($probeName -eq 'ComboBox' -and $created -ne [IntPtr]::Zero -and $foreground -ne $main) {
                    throw "CONTINUOUS_FRAME_ENVIRONMENT_INTERFERENCE framework=$framework probe=$probeName foreground=0x$($foreground.ToInt64().ToString('X')) main=0x$($main.ToInt64().ToString('X'))"
                }
                throw "$framework $probeName popup did not become visible."
            }

            # Startup DPI/layout transactions may finish between the first owner-HWND discovery
            # and the delayed probe action. The continuous-frame contract begins at the first
            # visible popup frame, so record owner geometry at that exact boundary.
            $mainRectangle = New-Object ContinuousFrameNative+RECT
            [ContinuousFrameNative]::GetWindowRect($main, [ref]$mainRectangle) | Out-Null
            $frames = @()
            for ($frame = 0; $frame -lt $FrameCount; $frame++) {
                $process.Refresh()
                $currentPopup = Find-Popup $process $main $probeName $true
                if ($currentPopup -eq [IntPtr]::Zero) {
                    $foreground = [ContinuousFrameNative]::GetForegroundWindow()
                    if ($probeName -eq 'ComboBox' -and $foreground -ne $main) {
                        throw "CONTINUOUS_FRAME_ENVIRONMENT_INTERFERENCE framework=$framework probe=$probeName frame=$frame foreground=0x$($foreground.ToInt64().ToString('X'))"
                    }
                    throw "$framework $probeName popup disappeared at frame $frame."
                }
                if ($currentPopup -ne $popup) { throw "$framework $probeName replaced its popup HWND at frame $frame." }
                $currentMainRectangle = New-Object ContinuousFrameNative+RECT
                [ContinuousFrameNative]::GetWindowRect($main, [ref]$currentMainRectangle) | Out-Null
                $mainGeometryChanged = $currentMainRectangle.Left -ne $mainRectangle.Left -or
                    $currentMainRectangle.Top -ne $mainRectangle.Top -or
                    $currentMainRectangle.Right -ne $mainRectangle.Right -or
                    $currentMainRectangle.Bottom -ne $mainRectangle.Bottom
                # Process.MainWindowHandle is recalculated from top-level windows and legitimately
                # resolves to native ComboLBox while it is open. The recorded owner HWND is the
                # stable identity; managed overlays must not change Process.MainWindowHandle.
                $managedMainChanged = $probeName -ne 'ComboBox' -and $process.MainWindowHandle -ne $main
                if ($managedMainChanged -or -not [ContinuousFrameNative]::IsWindowVisible($main) -or
                    [ContinuousFrameNative]::IsIconic($main) -or $mainGeometryChanged) {
                    throw "$framework $probeName destabilized the Gallery main HWND at frame ${frame}: initial=0x$($main.ToInt64().ToString('X')) processMain=0x$($process.MainWindowHandle.ToInt64().ToString('X')) visible=$([ContinuousFrameNative]::IsWindowVisible($main)) iconic=$([ContinuousFrameNative]::IsIconic($main)) geometryChanged=$mainGeometryChanged popup=0x$($popup.ToInt64().ToString('X'))."
                }
                if ([ContinuousFrameNative]::GetDpiForWindow($popup) -ne $TargetDpi) {
                    throw "$framework $probeName popup has incorrect DPI at frame $frame."
                }
                $owner = [ContinuousFrameNative]::GetWindow($popup, [ContinuousFrameNative]::GwOwner)
                $rootOwner = [ContinuousFrameNative]::GetAncestor($popup, [ContinuousFrameNative]::GaRootOwner)
                $parent = [ContinuousFrameNative]::GetParent($popup)
                $parentRoot = [ContinuousFrameNative]::GetAncestor($parent, [ContinuousFrameNative]::GaRoot)
                $validOwner = if ($probeName -eq 'ComboBox') {
                    $owner -eq $main -or $rootOwner -eq $main
                } else {
                    $owner -eq $main -or $rootOwner -eq $main
                }
                if (-not $validOwner) {
                    throw "$framework $probeName popup owner is invalid: owner=0x$($owner.ToInt64().ToString('X')) rootOwner=0x$($rootOwner.ToInt64().ToString('X')) parent=0x$($parent.ToInt64().ToString('X')) parentRoot=0x$($parentRoot.ToInt64().ToString('X')) main=0x$($main.ToInt64().ToString('X'))."
                }
                $path = Join-Path $scenarioOutput ("{0:D2}.png" -f $frame)
                $capture = Capture-Popup $popup $path
                $capture | Add-Member -NotePropertyName Frame -NotePropertyValue $frame
                $capture | Add-Member -NotePropertyName Complete -NotePropertyValue ($capture.ColorBuckets -ge 4 -and
                    ($capture.MaximumLuminance - $capture.MinimumLuminance) -ge 24)
                $frames += $capture
                if ($frame + 1 -lt $FrameCount) { Start-Sleep -Milliseconds $FrameIntervalMilliseconds }
            }

            if ($probeName -in @('ComboBox', 'Select', 'DatePicker')) {
                Assert-RoundedPopup $popup "$framework $probeName"
            }
            $incompleteFrames = @($frames | Where-Object { -not $_.Complete })
            if ($incompleteFrames.Count -gt 0) {
                $details = $incompleteFrames | ForEach-Object {
                    "$($_.Frame):colors=$($_.ColorBuckets),luminance=$($_.MinimumLuminance)-$($_.MaximumLuminance)"
                }
                throw "$framework $probeName exposed blank/incomplete frames: $($details -join '; ')."
            }
            $geometries = @($frames | ForEach-Object { "$($_.Left),$($_.Top),$($_.Width)x$($_.Height)" } | Select-Object -Unique)
            if ($geometries.Count -ne 1) {
                throw "$framework $probeName exposed changing frame geometry: $($geometries -join '; ')."
            }
            $hashes = @($frames | ForEach-Object { $_.Hash.ToString('X16') } | Select-Object -Unique)
            if ($hashes.Count -ne 1) {
                throw "$framework $probeName first/final visual frames differ: $($hashes.Count) content signatures."
            }
            if ($probeName -eq 'Select') {
                $expected = if ($TargetDpi -eq 144) { '450x169' } else { '300x112' }
                $actual = "$($frames[0].Width)x$($frames[0].Height)"
                if ($actual -ne $expected) { throw "$framework Select popup is $actual, expected $expected." }
            }
            if ($probeName -eq 'DatePicker') {
                $expected = if ($TargetDpi -eq 144) { '450x433' } else { '300x288' }
                $actual = "$($frames[0].Width)x$($frames[0].Height)"
                if ($actual -ne $expected) { throw "$framework DatePicker popup is $actual, expected $expected." }
            }
            if ($probeName -eq 'ComboBox') {
                $expected = if ($TargetDpi -eq 144) { '510x169' } else { '340x112' }
                $actual = "$($frames[0].Width)x$($frames[0].Height)"
                if ($actual -ne $expected) {
                    throw "$framework ComboBox popup is $actual, expected $expected physical pixels at $TargetDpi DPI."
                }
            }
            if ($probeName -eq 'ToolTip') {
                $expected = if ($TargetDpi -eq 144) { '463x111' } else { '312x77' }
                $actual = "$($frames[0].Width)x$($frames[0].Height)"
                if ($actual -ne $expected) { throw "$framework ToolTip popup is $actual, expected $expected." }
            }
            $comboDiagnostic = if ($probeName -eq 'ComboBox') {
                [ContinuousFrameNative]::FindComboListForRoot($popup, $main)
            } else { $null }
            $diagnosticText = if ($null -ne $comboDiagnostic) {
                " combo=0x$($comboDiagnostic.Combo.ToInt64().ToString('X')) comboSize=$($comboDiagnostic.Width)x$($comboDiagnostic.Height) requestedWidth=$($comboDiagnostic.DroppedWidth) comboDpi=$($comboDiagnostic.Dpi) awareness=$($comboDiagnostic.Awareness)"
            } else { '' }
            $results += [pscustomobject]@{
                Framework = $framework
                Probe = $probeName
                Dpi = $TargetDpi
                Frames = $FrameCount
                Geometry = $geometries[0]
                Width = $frames[0].Width
                Height = $frames[0].Height
                ColorBuckets = $frames[0].ColorBuckets
                Hash = $hashes[0]
                Diagnostic = $diagnosticText.Trim()
            }
            $directionName = if ($RightToLeft) { 'RTL' } else { 'LTR' }
            Write-Host "CONTINUOUS_FRAME_OK framework=$framework probe=$probeName direction=$directionName dpi=$TargetDpi frames=$FrameCount geometry=$($geometries[0])$diagnosticText"
        }
        finally {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        }
    }
}

if ($frameworks.Count -gt 1) {
    foreach ($probeName in $probes) {
        $probeResults = @($results | Where-Object { $_.Probe -eq $probeName })
        $sizes = @($probeResults | ForEach-Object { "$($_.Width)x$($_.Height)" } | Select-Object -Unique)
        if ($sizes.Count -ne 1) {
            throw "$probeName physical geometry differs across frameworks at $TargetDpi DPI: $($sizes -join ', ')."
        }
    }
}

$results | Format-Table -AutoSize
$directionName = if ($RightToLeft) { 'RTL' } else { 'LTR' }
Write-Host "UI_CONTINUOUS_FRAMES_OK frameworks=$($frameworks -join ',') probes=$($probes -join ',') direction=$directionName dpi=$TargetDpi frames=$FrameCount"
