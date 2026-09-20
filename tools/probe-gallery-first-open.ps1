param(
    [ValidateSet('net48', 'net8.0-windows')]
    [string]$TargetFramework = 'net8.0-windows',
    [ValidateSet('Main', 'DataDisplay', 'DateAndTime')]
    [string]$Page = 'DataDisplay',
    [ValidateSet(96, 144)]
    [int]$TargetDpi = 96,
    [string]$OutputDirectory = '.artifacts/gallery-first-open'
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GalleryFirstOpenNative {
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
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int capacity);
}
'@

[GalleryFirstOpenNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$repoRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repoRoot "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/Release/$TargetFramework/ModernUI.WinForms.Gallery.exe"
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }
New-Item -ItemType Directory -Force $output | Out-Null

function Wait-MainWindow([Diagnostics.Process]$process) {
    for ($index = 0; $index -lt 100; $index++) {
        if ($process.HasExited) { throw "Gallery exited with code $($process.ExitCode)." }
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { return $process.MainWindowHandle }
        Start-Sleep -Milliseconds 50
    }
    throw 'Gallery window did not appear.'
}

function Resolve-ScreenForDpi([int]$dpi) {
    foreach ($screen in [Windows.Forms.Screen]::AllScreens) {
        $probeArguments = @('--no-animation', "--location=$($screen.WorkingArea.Left+40),$($screen.WorkingArea.Top+40)")
        if ($Page -ne 'Main') { $probeArguments += "--demo=$Page" }
        $probe = Start-Process $exe -ArgumentList $probeArguments -PassThru
        try {
            $window = Wait-MainWindow $probe
            if ([GalleryFirstOpenNative]::GetDpiForWindow($window) -eq $dpi) { return $screen }
        }
        finally { if (-not $probe.HasExited) { $probe.Kill(); $probe.WaitForExit() } }
    }
    throw "No display reports $dpi DPI."
}

function Capture([IntPtr]$window, [string]$path) {
    $rect = New-Object GalleryFirstOpenNative+RECT
    [GalleryFirstOpenNative]::GetClientRect($window, [ref]$rect) | Out-Null
    $bitmap = New-Object Drawing.Bitmap ($rect.Right-$rect.Left), ($rect.Bottom-$rect.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { [GalleryFirstOpenNative]::PrintWindow($window, $dc, 1) | Out-Null }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    $bitmap.Save($path)
    $size = $bitmap.Size
    $bitmap.Dispose()
    return $size
}

if (-not (Test-Path $exe)) { throw "Build output not found: $exe" }
$screen = Resolve-ScreenForDpi $TargetDpi
$initialLocation = "$($screen.WorkingArea.Left+40),$($screen.WorkingArea.Top+40)"
$arguments = @('--no-animation', "--location=$initialLocation")
if ($Page -ne 'Main') { $arguments += "--demo=$Page" }
$process = Start-Process $exe -ArgumentList $arguments -PassThru
try {
    $window = Wait-MainWindow $process
    $actualDpi = [GalleryFirstOpenNative]::GetDpiForWindow($window)
    if ($actualDpi -ne $TargetDpi) { throw "Gallery was first created at $actualDpi DPI, expected $TargetDpi." }
    Start-Sleep -Milliseconds 1000
    $path = Join-Path $output "$TargetFramework-$Page-$TargetDpi.png"
    $size = Capture $window $path
    $expected = if ($Page -eq 'Main') {
        if ($TargetDpi -eq 96) { [Drawing.Size]::new(1180,720) } else { [Drawing.Size]::new(1770,1080) }
    } else {
        if ($TargetDpi -eq 96) { [Drawing.Size]::new(960,680) } else { [Drawing.Size]::new(1440,1020) }
    }
    if ($size -ne $expected) { throw "First-open client is $($size.Width)x$($size.Height), expected $($expected.Width)x$($expected.Height)." }
    Write-Host "FIRST_OPEN_OK framework=$TargetFramework page=$Page dpi=$TargetDpi client=$($size.Width)x$($size.Height) screen=$($screen.DeviceName) image=$path"
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
