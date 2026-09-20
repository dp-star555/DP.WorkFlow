param(
    [ValidateSet('net48', 'net8.0-windows')]
    [string]$TargetFramework = 'net48',
    [ValidateSet(96, 144)]
    [int]$TargetDpi = 96,
    [string]$OutputPath = '.artifacts/date-range-fonts.txt'
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DateRangeFontNative {
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
}
'@
[DateRangeFontNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$repoRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repoRoot "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/Release/$TargetFramework/ModernUI.WinForms.Gallery.exe"
$output = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
New-Item -ItemType Directory -Force (Split-Path $output -Parent) | Out-Null
Remove-Item $output -Force -ErrorAction SilentlyContinue

function Wait-Window([Diagnostics.Process]$process) {
    for($i=0;$i -lt 120;$i++) {
        if($process.HasExited){throw "Gallery exited with $($process.ExitCode)."}
        $process.Refresh(); if($process.MainWindowHandle -ne 0){return $process.MainWindowHandle}
        Start-Sleep -Milliseconds 40
    }
    throw 'Gallery window did not appear.'
}

$targetScreen=$null
foreach($screen in [Windows.Forms.Screen]::AllScreens) {
    $probe=Start-Process $exe -ArgumentList @('--demo=DateAndTime',"--location=$($screen.WorkingArea.Left+40),$($screen.WorkingArea.Top+40)") -PassThru
    try { $window=Wait-Window $probe; if([DateRangeFontNative]::GetDpiForWindow($window)-eq $TargetDpi){$targetScreen=$screen;break} }
    finally { if(-not $probe.HasExited){$probe.Kill();$probe.WaitForExit()} }
}
if($null -eq $targetScreen){throw "No screen reports $TargetDpi DPI."}
$location="$($targetScreen.WorkingArea.Left+40),$($targetScreen.WorkingArea.Top+40)"
$encodedOutput=[Uri]::EscapeDataString($output)
$process=Start-Process $exe -ArgumentList @('--demo=DateAndTime',"--location=$location","--date-range-font-probe=$encodedOutput") -PassThru
try {
    $window=Wait-Window $process
    if([DateRangeFontNative]::GetDpiForWindow($window)-ne $TargetDpi){throw 'Gallery did not start at target DPI.'}
    for($i=0;$i -lt 160 -and -not (Test-Path $output);$i++){Start-Sleep -Milliseconds 50}
    if(-not (Test-Path $output)){throw 'Date range font probe did not complete.'}
    $lines=Get-Content $output
    $lines | ForEach-Object { Write-Host $_ }
    $calendarFonts=@()
    $nativeFonts=@()
    foreach($line in $lines) {
        if($line -match ' native=(?<native>.*?) calendar=(?<calendar>.*?) dpi=') {
            $nativeFonts += $matches['native']
            $calendarFonts += $matches['calendar']
        }
    }
    if($calendarFonts.Count -ne 6){throw 'Probe did not return six calendar samples.'}
    for($i=0;$i -lt 6;$i++) {
        if($calendarFonts[$i] -ne $nativeFonts[$i]){throw "Font mismatch sample ${i}: native=$($nativeFonts[$i]) calendar=$($calendarFonts[$i])."}
    }
    if(@($calendarFonts | Select-Object -Unique).Count -ne 1){throw "Order-dependent calendar fonts: $($calendarFonts -join ', ')."}
    Write-Host "DATE_RANGE_FONT_PROBE_OK framework=$TargetFramework dpi=$TargetDpi font=$($calendarFonts[0])"
}
finally { if(-not $process.HasExited){$process.Kill();$process.WaitForExit()} }
