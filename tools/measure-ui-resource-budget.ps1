param(
    [ValidateSet('net48', 'net8.0-windows')][string]$TargetFramework = 'net8.0-windows',
    [ValidateRange(1, 5000)][int]$IdleSampleMilliseconds = 1200,
    [ValidateRange(0, 5000)][int]$MaximumHandleGrowth = 120,
    [ValidateRange(0, 100)][double]$MaximumIdleCpuPercent = 1.0
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class UiResourceBudgetNative {
 [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
}
'@
$root = Split-Path $PSScriptRoot -Parent
$exe = (Resolve-Path (Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/Release/$TargetFramework/ModernUI.WinForms.Gallery.exe")).Path
$process = Start-Process $exe -ArgumentList '--demo=FeedbackAndOverlays','--no-animation' -PassThru
try {
    for ($i = 0; $i -lt 100 -and $process.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 50; $process.Refresh() }
    if ($process.MainWindowHandle -eq 0) { throw 'Gallery main window did not appear.' }
    Start-Sleep -Milliseconds 600
    $process.Refresh()
    $handlesBefore = $process.HandleCount
    $gdiBefore = [UiResourceBudgetNative]::GetGuiResources($process.Handle, 0)
    $userBefore = [UiResourceBudgetNative]::GetGuiResources($process.Handle, 1)
    $cpuBefore = $process.TotalProcessorTime
    $sampleStart = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds $IdleSampleMilliseconds
    $sampleStart.Stop(); $process.Refresh()
    $cpuUsed = ($process.TotalProcessorTime - $cpuBefore).TotalMilliseconds
    $idleCpu = 100d * $cpuUsed / ($sampleStart.Elapsed.TotalMilliseconds * [Environment]::ProcessorCount)
    $handleGrowth = $process.HandleCount - $handlesBefore
    $gdiGrowth = [int][UiResourceBudgetNative]::GetGuiResources($process.Handle, 0) - [int]$gdiBefore
    $userGrowth = [int][UiResourceBudgetNative]::GetGuiResources($process.Handle, 1) - [int]$userBefore
    Write-Host ("RESOURCE_RESULT framework={0} idleCpu={1:N3}% handles={2} gdi={3} user={4}" -f $TargetFramework,$idleCpu,$handleGrowth,$gdiGrowth,$userGrowth)
    if ($idleCpu -gt $MaximumIdleCpuPercent) { throw "Idle CPU $idleCpu% exceeds $MaximumIdleCpuPercent%." }
    if ($handleGrowth -gt $MaximumHandleGrowth) { throw "Handle growth $handleGrowth exceeds $MaximumHandleGrowth." }
    if ($gdiGrowth -gt 8 -or $userGrowth -gt 8) { throw "GUI resource growth exceeded budget: GDI=$gdiGrowth USER=$userGrowth." }
    Write-Host "UI_RESOURCE_BUDGET_OK framework=$TargetFramework"
}
finally { if ($null -ne $process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() } }
