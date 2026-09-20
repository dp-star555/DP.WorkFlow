param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('All', 'net48', 'net8.0-windows')]
    [string]$TargetFramework = 'All',
    [ValidateSet('All', 'Light', 'Dark', 'HighContrast')]
    [string]$Theme = 'All',
    [ValidateSet('Both', 'LTR', 'RTL')]
    [string]$Direction = 'Both',
    [ValidateSet('Both', 'Animations', 'ReducedMotion')]
    [string]$Motion = 'Both',
    [ValidateSet(96, 144)]
    [int]$TargetDpi = 96,
    [string]$OutputDirectory = '.artifacts/settings-matrix',
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

public static class SettingsMatrixNative
{
    public const int GwlExStyle = -20;
    public const long WsExNoActivate = 0x08000000L;
    public const uint GwOwner = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

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
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr parameter);

    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
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

    public static string WindowText(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
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
}
'@

[SettingsMatrixNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$monitor = [SettingsMatrixNative]::Monitors() |
    Where-Object { $_.Dpi -eq $TargetDpi } |
    Sort-Object { - (($_.WorkRight - $_.WorkLeft) * ($_.WorkBottom - $_.WorkTop)) } |
    Select-Object -First 1
if ($null -eq $monitor) {
    if ($SkipIfDpiUnavailable) {
        Write-Host "UI_SETTINGS_MATRIX_SKIPPED dpi=$TargetDpi reason=NoPhysicalMonitor"
        exit 0
    }
    throw "No connected display reports $TargetDpi DPI before HWND creation."
}

$frameworks = if ($TargetFramework -eq 'All') { @('net48', 'net8.0-windows') } else { @($TargetFramework) }
$themes = if ($Theme -eq 'All') { @('Light', 'Dark', 'HighContrast') } else { @($Theme) }
$directions = if ($Direction -eq 'Both') { @('LTR', 'RTL') } else { @($Direction) }
$motions = if ($Motion -eq 'Both') { @('Animations', 'ReducedMotion') } else { @($Motion) }
New-Item -ItemType Directory -Force $output | Out-Null

function Wait-MainWindow([Diagnostics.Process]$Process) {
    for ($index = 0; $index -lt 200; $index++) {
        if ($Process.HasExited) { throw "Gallery exited with code $($Process.ExitCode)." }
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero -and
            [SettingsMatrixNative]::IsWindowVisible($Process.MainWindowHandle)) {
            return $Process.MainWindowHandle
        }
        Start-Sleep -Milliseconds 15
    }
    throw 'Gallery main window did not appear.'
}

function Assert-ForegroundStable([IntPtr]$Expected, [string]$Context) {
    $current = [SettingsMatrixNative]::GetForegroundWindow()
    for ($attempt = 0; $current -eq [IntPtr]::Zero -and $attempt -lt 10; $attempt++) {
        Start-Sleep -Milliseconds 2
        $current = [SettingsMatrixNative]::GetForegroundWindow()
    }
    if ($current -ne $Expected) {
        throw "SETTINGS_MATRIX_ENVIRONMENT_INTERFERENCE context=$Context foregroundChanged expected=0x$($Expected.ToInt64().ToString('X')) actual=0x$($current.ToInt64().ToString('X'))"
    }
}

function Capture-Window([IntPtr]$Window, [string]$Path, [bool]$ClientOnly) {
    $rectangle = New-Object SettingsMatrixNative+RECT
    if ($ClientOnly) {
        [SettingsMatrixNative]::GetClientRect($Window, [ref]$rectangle) | Out-Null
    } else {
        [SettingsMatrixNative]::GetWindowRect($Window, [ref]$rectangle) | Out-Null
    }
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    if ($width -le 1 -or $height -le 1) { throw "Window 0x$($Window.ToInt64().ToString('X')) has invalid geometry ${width}x${height}." }
    $bitmap = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc = $graphics.GetHdc()
        try {
            $flags = if ($ClientOnly) { 3 } else { 2 }
            if (-not [SettingsMatrixNative]::PrintWindow($Window, $dc, $flags)) {
                throw "PrintWindow failed for 0x$($Window.ToInt64().ToString('X'))."
            }
        }
        finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        $pixels = [SettingsMatrixNative]::Analyze($bitmap)
        return [pscustomobject]@{
            Width=$width; Height=$height; Hash=$pixels.Hash; ColorBuckets=$pixels.ColorBuckets
            MinimumLuminance=$pixels.MinimumLuminance; MaximumLuminance=$pixels.MaximumLuminance
            BlackRatio=$pixels.BlackRatio
        }
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Parse-ProbeResult([string]$Text, [string]$Scenario) {
    $pattern = '^SETTINGS_MATRIX_PROBE_OK theme=(\w+) direction=(LTR|RTL) motion=(Animations|ReducedMotion) dpi=(\d+) anchor=(-?\d+),(-?\d+),(\d+),(\d+) popup=(-?\d+),(-?\d+),(\d+),(\d+) popupDpi=(\d+) value=(\w+) values=([\w,]+) modern=(\d+) systemHighContrast=(True|False) colors=(-?\d+),(-?\d+),(-?\d+),(-?\d+)\s*$'
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) { throw "$Scenario returned an invalid probe result: $Text" }
    return [pscustomobject]@{
        Theme=$match.Groups[1].Value; Direction=$match.Groups[2].Value; Motion=$match.Groups[3].Value
        Dpi=[int]$match.Groups[4].Value
        AnchorLeft=[int]$match.Groups[5].Value; AnchorTop=[int]$match.Groups[6].Value
        AnchorWidth=[int]$match.Groups[7].Value; AnchorHeight=[int]$match.Groups[8].Value
        PopupLeft=[int]$match.Groups[9].Value; PopupTop=[int]$match.Groups[10].Value
        PopupWidth=[int]$match.Groups[11].Value; PopupHeight=[int]$match.Groups[12].Value
        PopupDpi=[int]$match.Groups[13].Value; Value=$match.Groups[14].Value
        Values=$match.Groups[15].Value; ModernCount=[int]$match.Groups[16].Value
        SystemHighContrast=[bool]::Parse($match.Groups[17].Value)
        Colors=@([int]$match.Groups[18].Value, [int]$match.Groups[19].Value,
            [int]$match.Groups[20].Value, [int]$match.Groups[21].Value)
    }
}

function Expected-ThemeColors([string]$ThemeName) {
    switch ($ThemeName) {
        'Light' {
            return @([Drawing.Color]::FromArgb(245,247,250).ToArgb(), [Drawing.Color]::White.ToArgb(),
                [Drawing.Color]::FromArgb(31,31,31).ToArgb(), [Drawing.Color]::FromArgb(22,119,255).ToArgb())
        }
        'Dark' {
            return @([Drawing.Color]::FromArgb(20,20,20).ToArgb(), [Drawing.Color]::FromArgb(31,31,31).ToArgb(),
                [Drawing.Color]::FromArgb(217,217,217).ToArgb(), [Drawing.Color]::FromArgb(22,104,220).ToArgb())
        }
        'HighContrast' {
            return @([Drawing.SystemColors]::Window.ToArgb(), [Drawing.SystemColors]::Window.ToArgb(),
                [Drawing.SystemColors]::WindowText.ToArgb(), [Drawing.SystemColors]::Highlight.ToArgb())
        }
    }
}

$results = @()
foreach ($framework in $frameworks) {
    $exe = Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/$framework/ModernUI.WinForms.Gallery.exe"
    if (-not (Test-Path $exe)) { throw "Gallery output missing: $exe" }
    foreach ($themeName in $themes) {
        foreach ($directionName in $directions) {
            foreach ($motionName in $motions) {
                $scenario = "$framework-$themeName-$directionName-$motionName-$TargetDpi"
                $scenarioOutput = Join-Path $output $scenario
                if (Test-Path $scenarioOutput) { Remove-Item $scenarioOutput -Recurse -Force }
                New-Item -ItemType Directory -Force $scenarioOutput | Out-Null
                $resultPath = Join-Path $scenarioOutput 'result.txt'
                $x = $monitor.WorkLeft + 24
                $y = $monitor.WorkTop + 24
                $arguments = @(
                    '--deterministic-settings-probe', '--no-activate-probe', '--demo=Selection',
                    "--location=$x,$y", "--settings-matrix-probe=$([Uri]::EscapeDataString($resultPath))"
                )
                if ($themeName -eq 'Dark') { $arguments += '--dark' }
                elseif ($themeName -eq 'HighContrast') { $arguments += '--high-contrast' }
                if ($directionName -eq 'RTL') { $arguments += '--rtl' }
                if ($motionName -eq 'ReducedMotion') { $arguments += '--no-animation' }

                $foreground = [SettingsMatrixNative]::GetForegroundWindow()
                $process = Start-Process $exe -ArgumentList $arguments -PassThru
                try {
                    $main = Wait-MainWindow $process
                    if ([SettingsMatrixNative]::GetDpiForWindow($main) -ne $TargetDpi) {
                        throw "$scenario main HWND was not created at $TargetDpi DPI."
                    }
                    Assert-ForegroundStable $foreground "$scenario-startup"
                    for ($wait = 0; $wait -lt 300 -and -not (Test-Path $resultPath); $wait++) {
                        Assert-ForegroundStable $foreground "$scenario-wait-$wait"
                        Start-Sleep -Milliseconds 10
                    }
                    if (-not (Test-Path $resultPath)) { throw "$scenario did not write its result." }
                    $probe = Parse-ProbeResult (Get-Content $resultPath -Raw) $scenario
                    if ($probe.Theme -ne $themeName -or $probe.Direction -ne $directionName -or
                        $probe.Motion -ne $motionName -or $probe.Dpi -ne $TargetDpi -or $probe.PopupDpi -ne $TargetDpi) {
                        throw "$scenario reported settings that differ from its launch contract."
                    }
                    if ($probe.Value -ne 'continuous' -or $probe.Values -ne 'raw,result,log,thumbnail' -or
                        $probe.ModernCount -lt 10) {
                        throw "$scenario did not preserve representative selection values or its control tree."
                    }
                    $expectedColors = @(Expected-ThemeColors $themeName)
                    if (($probe.Colors -join ',') -ne ($expectedColors -join ',')) {
                        throw "$scenario theme tokens are $($probe.Colors -join ','), expected $($expectedColors -join ',')."
                    }
                    $expectedClient = if ($TargetDpi -eq 144) { '1440x1020' } else { '960x680' }
                    $mainPath = Join-Path $scenarioOutput 'main.png'
                    $mainPixels = Capture-Window $main $mainPath $true
                    if ("$($mainPixels.Width)x$($mainPixels.Height)" -ne $expectedClient) {
                        throw "$scenario client is $($mainPixels.Width)x$($mainPixels.Height), expected $expectedClient."
                    }
                    if ($mainPixels.ColorBuckets -lt 12 -or
                        ($mainPixels.MaximumLuminance - $mainPixels.MinimumLuminance) -lt 32 -or
                        $mainPixels.BlackRatio -gt 0.30) {
                        throw "$scenario produced an incomplete main frame."
                    }

                    $popup = [SettingsMatrixNative]::ProcessWindows([uint32]$process.Id, $true) |
                        Where-Object { $_ -ne $main -and [SettingsMatrixNative]::GetWindow($_, [SettingsMatrixNative]::GwOwner) -eq $main } |
                        Select-Object -First 1
                    if ($null -eq $popup -or $popup -eq [IntPtr]::Zero) { throw "$scenario Select popup is not visible." }
                    if (([SettingsMatrixNative]::ExtendedStyle($popup) -band [SettingsMatrixNative]::WsExNoActivate) -eq 0) {
                        throw "$scenario Select popup can activate."
                    }
                    if ([SettingsMatrixNative]::GetDpiForWindow($popup) -ne $TargetDpi) {
                        throw "$scenario Select popup has incorrect physical DPI."
                    }
                    $popupRect = New-Object SettingsMatrixNative+RECT
                    [SettingsMatrixNative]::GetWindowRect($popup, [ref]$popupRect) | Out-Null
                    if ($popupRect.Left -ne $probe.PopupLeft -or $popupRect.Top -ne $probe.PopupTop -or
                        $popupRect.Right - $popupRect.Left -ne $probe.PopupWidth -or
                        $popupRect.Bottom - $popupRect.Top -ne $probe.PopupHeight) {
                        throw "$scenario native popup geometry differs from the in-process observation."
                    }
                    $expectedPopup = if ($TargetDpi -eq 144) { '450x169' } else { '300x112' }
                    if ("$($probe.PopupWidth)x$($probe.PopupHeight)" -ne $expectedPopup) {
                        throw "$scenario Select popup is $($probe.PopupWidth)x$($probe.PopupHeight), expected $expectedPopup."
                    }
                    $aligned = if ($directionName -eq 'RTL') {
                        $probe.PopupLeft + $probe.PopupWidth -eq $probe.AnchorLeft + $probe.AnchorWidth
                    } else { $probe.PopupLeft -eq $probe.AnchorLeft }
                    if (-not $aligned) { throw "$scenario Select popup is not aligned to its $directionName anchor edge." }
                    $popupPath = Join-Path $scenarioOutput 'select-popup.png'
                    $popupPixels = Capture-Window $popup $popupPath $false
                    if ($popupPixels.ColorBuckets -lt 8 -or
                        ($popupPixels.MaximumLuminance - $popupPixels.MinimumLuminance) -lt 24) {
                        throw "$scenario produced an incomplete Select popup frame."
                    }
                    Assert-ForegroundStable $foreground "$scenario-final"
                    if (-not [SettingsMatrixNative]::IsWindowVisible($main) -or
                        [SettingsMatrixNative]::IsIconic($main) -or $process.MainWindowHandle -ne $main) {
                        throw "$scenario destabilized the Gallery main HWND."
                    }

                    $results += [pscustomobject]@{
                        Framework=$framework; Theme=$themeName; Direction=$directionName; Motion=$motionName
                        Dpi=$TargetDpi; Client=$expectedClient; Popup=$expectedPopup
                        MainHash=$mainPixels.Hash.ToString('X16'); PopupHash=$popupPixels.Hash.ToString('X16')
                        SystemHighContrast=$probe.SystemHighContrast
                    }
                    Write-Host "SETTINGS_MATRIX_OK framework=$framework theme=$themeName direction=$directionName motion=$motionName dpi=$TargetDpi client=$expectedClient popup=$expectedPopup systemHighContrast=$($probe.SystemHighContrast)"
                }
                finally {
                    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
                }
            }
        }
    }
}

if ($frameworks.Count -gt 1) {
    foreach ($themeName in $themes) {
        foreach ($directionName in $directions) {
            foreach ($motionName in $motions) {
                $scenarioResults = @($results | Where-Object {
                    $_.Theme -eq $themeName -and $_.Direction -eq $directionName -and $_.Motion -eq $motionName
                })
                if (@($scenarioResults | ForEach-Object Popup | Select-Object -Unique).Count -ne 1) {
                    throw "$themeName/$directionName/$motionName popup geometry differs across frameworks."
                }
                if (@($scenarioResults | ForEach-Object PopupHash | Select-Object -Unique).Count -ne 1) {
                    throw "$themeName/$directionName/$motionName Select popup pixels differ across frameworks."
                }
            }
        }
    }
}
foreach ($framework in $frameworks) {
    foreach ($themeName in $themes) {
        foreach ($directionName in $directions) {
            $motionResults = @($results | Where-Object {
                $_.Framework -eq $framework -and $_.Theme -eq $themeName -and $_.Direction -eq $directionName
            })
            if ($motions.Count -gt 1 -and
                (@($motionResults | ForEach-Object MainHash | Select-Object -Unique).Count -ne 1 -or
                 @($motionResults | ForEach-Object PopupHash | Select-Object -Unique).Count -ne 1)) {
                throw "$framework/$themeName/$directionName final pixels differ between Animations and ReducedMotion."
            }
        }
        if ($directions.Count -gt 1) {
            $ltrHash = ($results | Where-Object {
                $_.Framework -eq $framework -and $_.Theme -eq $themeName -and $_.Direction -eq 'LTR'
            } | Select-Object -First 1).PopupHash
            $rtlHash = ($results | Where-Object {
                $_.Framework -eq $framework -and $_.Theme -eq $themeName -and $_.Direction -eq 'RTL'
            } | Select-Object -First 1).PopupHash
            if ($ltrHash -eq $rtlHash) {
                throw "$framework/$themeName Select popup did not mirror its internal RTL row layout."
            }
        }
    }
}

$results | Format-Table -AutoSize
Write-Host "UI_SETTINGS_MATRIX_OK frameworks=$($frameworks -join ',') themes=$($themes -join ',') directions=$($directions -join ',') motions=$($motions -join ',') dpi=$TargetDpi scenarios=$($results.Count)"
