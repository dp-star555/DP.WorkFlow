param(
    [ValidateSet('Main', 'Actions', 'Inputs', 'Selection', 'DataDisplay', 'NavigationAndLayout', 'FeedbackAndOverlays', 'DateAndTime', 'PropertyGrid', 'BusinessScenario')]
    [string]$Page = 'Main',
    [ValidateSet(96, 144)]
    [int]$TargetDpi = 96,
    [ValidateSet('None', 'Message', 'Notification', 'ToolTip', 'ToolTip-Short')]
    [string]$FeedbackProbe = 'None',
    [ValidateRange(0, 20000)]
    [int]$ScrollOffset = 0,
    [switch]$Dark,
    [string]$OutputDirectory = '.artifacts/gallery-framework-comparison'
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GalleryFrameworkComparisonNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@

[GalleryFrameworkComparisonNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$repoRoot = Split-Path $PSScriptRoot -Parent
$targets = [ordered]@{
    net48 = Join-Path $repoRoot 'src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/Release/net48/ModernUI.WinForms.Gallery.exe'
    net8 = Join-Path $repoRoot 'src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/Release/net8.0-windows/ModernUI.WinForms.Gallery.exe'
}
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }
New-Item -ItemType Directory -Force $output | Out-Null

function Wait-MainWindow([Diagnostics.Process]$process) {
    for ($index = 0; $index -lt 100; $index++) {
        if ($process.HasExited) { throw "Gallery exited with code $($process.ExitCode)." }
        $windows = @(Find-ProcessWindows $process.Id)
        $main = $windows | Sort-Object {
            $rect = New-Object GalleryFrameworkComparisonNative+RECT
            [GalleryFrameworkComparisonNative]::GetClientRect($_, [ref]$rect) | Out-Null
            -(($rect.Right - $rect.Left) * ($rect.Bottom - $rect.Top))
        } | Select-Object -First 1
        if ($null -ne $main) {
            $rect = New-Object GalleryFrameworkComparisonNative+RECT
            [GalleryFrameworkComparisonNative]::GetClientRect($main, [ref]$rect) | Out-Null
            if ((($rect.Right - $rect.Left) * ($rect.Bottom - $rect.Top)) -gt 100000) { return $main }
        }
        Start-Sleep -Milliseconds 80
    }
    throw 'Gallery main window did not appear.'
}

function Move-ToDpi([IntPtr]$window, [int]$dpi) {
    foreach ($screen in [Windows.Forms.Screen]::AllScreens) {
        [GalleryFrameworkComparisonNative]::SetWindowPos($window, [IntPtr]::Zero,
            $screen.WorkingArea.Left + 40, $screen.WorkingArea.Top + 40, 0, 0, 0x0001) | Out-Null
        Start-Sleep -Milliseconds 500
        if ([GalleryFrameworkComparisonNative]::GetDpiForWindow($window) -eq $dpi) { return $screen }
    }
    throw "No connected display reports $dpi DPI."
}

function Find-ProcessWindows([int]$processId) {
    $windows = New-Object Collections.Generic.List[IntPtr]
    $parameter = [Runtime.InteropServices.GCHandle]::Alloc($windows)
    try {
        $callback = [GalleryFrameworkComparisonNative+EnumWindowsProc]{
            param($window, $state)
            $handle = [Runtime.InteropServices.GCHandle]::FromIntPtr($state)
            $list = [Collections.Generic.List[IntPtr]]$handle.Target
            if ([GalleryFrameworkComparisonNative]::IsWindowVisible($window)) { $list.Add($window) }
            return $true
        }
        [GalleryFrameworkComparisonNative]::EnumWindows($callback, [Runtime.InteropServices.GCHandle]::ToIntPtr($parameter)) | Out-Null
    }
    finally { $parameter.Free() }
    $result = @()
    foreach ($window in $windows) {
        [uint32]$ownerProcessId = 0
        [GalleryFrameworkComparisonNative]::GetWindowThreadProcessId($window, [ref]$ownerProcessId) | Out-Null
        if ($ownerProcessId -eq $processId) { $result += $window }
    }
    return $result
}

function Capture-Client([IntPtr]$window, [string]$path) {
    $rect = New-Object GalleryFrameworkComparisonNative+RECT
    [GalleryFrameworkComparisonNative]::GetClientRect($window, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object Drawing.Bitmap $width, $height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { [GalleryFrameworkComparisonNative]::PrintWindow($window, $dc, 1) | Out-Null }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    $bitmap.Save($path)
    $bitmap.Dispose()
    return [Drawing.Size]::new($width, $height)
}

$results = @()
foreach ($target in $targets.GetEnumerator()) {
    if (-not (Test-Path $target.Value)) { throw "Build output not found: $($target.Value)" }
    $arguments = @('--no-animation')
    if ($Dark) { $arguments += '--dark' }
    if ($Page -ne 'Main') { $arguments += "--demo=$Page" }
    if ($ScrollOffset -gt 0) { $arguments += "--scroll=$ScrollOffset" }
    if ($FeedbackProbe -ne 'None') {
        if ($Page -ne 'FeedbackAndOverlays') { throw 'Feedback probes require -Page FeedbackAndOverlays.' }
        $arguments += "--feedback-probe=$($FeedbackProbe.ToLowerInvariant())"
    }
    $process = Start-Process $target.Value -ArgumentList $arguments -PassThru
    try {
        $window = Wait-MainWindow $process
        $screen = Move-ToDpi $window $TargetDpi
        Start-Sleep -Milliseconds 1600
        $themeSuffix = if ($Dark) { '-dark' } else { '' }
        $path = Join-Path $output "$($target.Key)-$Page-$TargetDpi$themeSuffix-scroll$ScrollOffset.png"
        $clientSize = Capture-Client $window $path
        $probeWidth = 0
        $probeHeight = 0
        if ($FeedbackProbe -ne 'None') {
            $foundWindows = @(Find-ProcessWindows $process.Id)
            $probeWindow = $foundWindows | Where-Object { $_ -ne $window } | Select-Object -First 1
            if ($null -eq $probeWindow) { throw "$FeedbackProbe window did not appear." }
            $probePath = Join-Path $output "$($target.Key)-$FeedbackProbe-$TargetDpi.png"
            $probeSize = Capture-Client $probeWindow $probePath
            $probeWidth = $probeSize.Width
            $probeHeight = $probeSize.Height
            if ([GalleryFrameworkComparisonNative]::GetDpiForWindow($probeWindow) -ne $TargetDpi) {
                throw "$($target.Key) $FeedbackProbe window did not inherit $TargetDpi DPI."
            }
        }
        $results += [pscustomobject]@{
            Framework = $target.Key
            Dpi = [GalleryFrameworkComparisonNative]::GetDpiForWindow($window)
            ClientWidth = $clientSize.Width
            ClientHeight = $clientSize.Height
            ProbeWidth = $probeWidth
            ProbeHeight = $probeHeight
            Screen = $screen.DeviceName
            Image = $path
        }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    }
}

$expected = if ($Page -eq 'Main') {
    if ($TargetDpi -eq 96) { [Drawing.Size]::new(1180, 720) } else { [Drawing.Size]::new(1770, 1080) }
} else {
    if ($TargetDpi -eq 96) { [Drawing.Size]::new(960, 680) } else { [Drawing.Size]::new(1440, 1020) }
}

$results | Format-Table -AutoSize
foreach ($result in $results) {
    if ($result.Dpi -ne $TargetDpi) { throw "$($result.Framework) reported DPI $($result.Dpi), expected $TargetDpi." }
    if ($result.ClientWidth -ne $expected.Width -or $result.ClientHeight -ne $expected.Height) {
        throw "$($result.Framework) client size is $($result.ClientWidth)x$($result.ClientHeight), expected $($expected.Width)x$($expected.Height)."
    }
}
if ($results[0].ClientWidth -ne $results[1].ClientWidth -or $results[0].ClientHeight -ne $results[1].ClientHeight) {
    throw 'net48 and net8 client sizes do not match.'
}
if ($FeedbackProbe -ne 'None' -and
    ($results[0].ProbeWidth -ne $results[1].ProbeWidth -or $results[0].ProbeHeight -ne $results[1].ProbeHeight)) {
    throw "net48 and net8 $FeedbackProbe sizes do not match."
}
$themeName = if ($Dark) { 'dark' } else { 'light' }
Write-Host "FRAMEWORK_COMPARISON_OK page=$Page dpi=$TargetDpi theme=$themeName scroll=$ScrollOffset client=$($expected.Width)x$($expected.Height) probe=$FeedbackProbe"
