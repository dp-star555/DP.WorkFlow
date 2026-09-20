param(
    [ValidateSet('net48', 'net8.0-windows')]
    [string]$TargetFramework = 'net48',
    [int]$FrameCount = 18,
    [string]$OutputDirectory = '.artifacts/date-popup-frames'
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DatePopupProbeNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
}
'@

[DatePopupProbeNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$repoRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repoRoot "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/Release/$TargetFramework/ModernUI.WinForms.Gallery.exe"
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }
New-Item -ItemType Directory -Force $output | Out-Null

function Wait-MainWindow([Diagnostics.Process]$process) {
    for ($index=0; $index -lt 120; $index++) {
        if ($process.HasExited) { throw "Gallery exited with code $($process.ExitCode)." }
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { return $process.MainWindowHandle }
        Start-Sleep -Milliseconds 25
    }
    throw 'Gallery window did not appear.'
}

function Process-Windows([int]$processId) {
    $result = New-Object Collections.Generic.List[IntPtr]
    $callback = [DatePopupProbeNative+EnumWindowsProc]{
        param($window, $parameter)
        [uint32]$windowProcessId = 0
        [DatePopupProbeNative]::GetWindowThreadProcessId($window, [ref]$windowProcessId) | Out-Null
        if ($windowProcessId -eq $processId -and [DatePopupProbeNative]::IsWindowVisible($window)) { $result.Add($window) }
        return $true
    }
    [DatePopupProbeNative]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    return @($result)
}

function Class-Name([IntPtr]$window) {
    $text = New-Object Text.StringBuilder 128
    [DatePopupProbeNative]::GetClassName($window, $text, $text.Capacity) | Out-Null
    return $text.ToString()
}

function Capture-Window([IntPtr]$window, [string]$path) {
    $rect = New-Object DatePopupProbeNative+RECT
    [DatePopupProbeNative]::GetWindowRect($window, [ref]$rect) | Out-Null
    $width=$rect.Right-$rect.Left; $height=$rect.Bottom-$rect.Top
    if ($width -le 0 -or $height -le 0) { return $null }
    $bitmap=New-Object Drawing.Bitmap $width,$height
    $graphics=[Drawing.Graphics]::FromImage($bitmap); $dc=$graphics.GetHdc()
    try { [DatePopupProbeNative]::PrintWindow($window,$dc,0) | Out-Null }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    $bitmap.Save($path); $bitmap.Dispose()
    return [Drawing.Size]::new($width,$height)
}

if (-not (Test-Path $exe)) { throw "Build output missing: $exe" }
$process=Start-Process $exe -ArgumentList @('--demo=DateAndTime','--date-probe=open','--location=-1880,430') -PassThru
try {
    $main=Wait-MainWindow $process
    $observations=@()
    for($frame=0;$frame -lt $FrameCount;$frame++) {
        $windows=@(Process-Windows $process.Id)
        foreach($window in $windows) {
            if($window -eq $main){continue}
            $class=Class-Name $window
            $path=Join-Path $output ("{0:D2}-{1}.png" -f $frame, ($class -replace '[^A-Za-z0-9_-]','_'))
            $size=Capture-Window $window $path
            if($null -ne $size){$observations += [pscustomobject]@{Frame=$frame;Class=$class;Width=$size.Width;Height=$size.Height;Path=$path}}
        }
        Start-Sleep -Milliseconds 12
    }
    if($observations | Where-Object {$_.Class -eq 'SysMonthCal32'}) { throw 'A visible native SysMonthCal32 frame was observed.' }
    $popupFrames=@($observations | Where-Object {$_.Class -ne 'SysMonthCal32'})
    if($popupFrames.Count -eq 0){throw 'Managed calendar popup was not observed.'}
    $sizes=@($popupFrames | ForEach-Object {"$($_.Width)x$($_.Height)"} | Select-Object -Unique)
    if($sizes.Count -ne 1){throw "Popup exposed changing/blank-frame geometry: $($sizes -join ', ')."}
    $expected = if([DatePopupProbeNative]::GetDpiForWindow($main) -eq 144){'450x433'}else{'300x288'}
    if($sizes[0] -ne $expected){throw "Popup geometry $($sizes[0]), expected $expected."}
    Write-Host "DATE_POPUP_FRAMES_OK framework=$TargetFramework frames=$($popupFrames.Count) size=$($sizes[0])"
}
finally { if(-not $process.HasExited){$process.Kill();$process.WaitForExit()} }
