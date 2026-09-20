param(
 [string[]]$GalleryArguments = @(),
 [ValidateSet('net48', 'net8.0-windows')][string]$TargetFramework = 'net8.0-windows',
 [ValidateRange(2, 100)][int]$Iterations = 12,
 [double]$MaximumMedianMs = 0,
 [switch]$AssertLogicalClientSize
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class DpiTransitionMeasure {
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out RECT r);
}
'@
[DpiTransitionMeasure]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$repoRoot = Split-Path $PSScriptRoot -Parent
$exe=(Resolve-Path (Join-Path $repoRoot "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/Release/$TargetFramework/ModernUI.WinForms.Gallery.exe")).Path
$screens=[Windows.Forms.Screen]::AllScreens
if($screens.Count-lt2){throw 'two monitors required'}
$targets=@($screens|%{$_.WorkingArea})
$p = if ($GalleryArguments.Count -eq 0) {
 Start-Process $exe -PassThru
} else {
 Start-Process $exe -ArgumentList $GalleryArguments -PassThru
}
$times=@()
$observations=@()
try{
 for($i=0;$i-lt100-and$p.MainWindowHandle-eq0;$i++){Start-Sleep -Milliseconds 10;$p.Refresh()}
 Start-Sleep -Milliseconds 500
 # Warm both targets before measuring. This proves that the monitors have different real DPI
 # values and ensures every timed SetWindowPos below performs an actual DPI transition.
 $first=$targets[0];$second=$targets[1]
 [DpiTransitionMeasure]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,$first.Left+30,$first.Top+30,0,0,0x15)|Out-Null
 Start-Sleep -Milliseconds 250
 $firstDpi=[DpiTransitionMeasure]::GetDpiForWindow($p.MainWindowHandle)
 [DpiTransitionMeasure]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,$second.Left+30,$second.Top+30,0,0,0x15)|Out-Null
 Start-Sleep -Milliseconds 250
 $secondDpi=[DpiTransitionMeasure]::GetDpiForWindow($p.MainWindowHandle)
 if($firstDpi-eq$secondDpi){throw "two monitors with different real DPI values required; both reported $firstDpi"}
 for($i=0;$i-lt$Iterations;$i++){
  $r=$targets[$i%2]
  $sw=[Diagnostics.Stopwatch]::StartNew()
  [DpiTransitionMeasure]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,$r.Left+30,$r.Top+30,0,0,0x15)|Out-Null
  $sw.Stop();Start-Sleep -Milliseconds 150
  $rect=New-Object DpiTransitionMeasure+RECT;[DpiTransitionMeasure]::GetClientRect($p.MainWindowHandle,[ref]$rect)|Out-Null
  $dpi=[DpiTransitionMeasure]::GetDpiForWindow($p.MainWindowHandle)
  $ms=$sw.Elapsed.TotalMilliseconds;$times+=$ms
  $width=$rect.Right-$rect.Left;$height=$rect.Bottom-$rect.Top
  $observations += [pscustomobject]@{Dpi=[int]$dpi;Width=$width;Height=$height}
  Write-Host ("run={0} dpi={1} setWindowPosMs={2:N1} client={3}x{4}" -f ($i+1),$dpi,$ms,$width,$height)
 }
 $sorted=@($times|sort)
 $middle=[Math]::Floor($sorted.Count/2)
 $median=if(($sorted.Count%2)-eq0){($sorted[$middle-1]+$sorted[$middle])/2}else{$sorted[$middle]}
 $p95=$sorted[[Math]::Min($sorted.Count-1,[Math]::Floor($sorted.Count*.95))]
 $maximum=($sorted|measure -Maximum).Maximum
 Write-Host ("RESULT framework={0} median={1:N1}ms p95={2:N1}ms max={3:N1}ms" -f $TargetFramework,$median,$p95,$maximum)
 if($MaximumMedianMs -gt 0 -and $median -gt $MaximumMedianMs){
  throw ("DPI transition median {0:N1}ms exceeded limit {1:N1}ms" -f $median,$MaximumMedianMs)
 }
 if($AssertLogicalClientSize){
  $isCategory=$GalleryArguments | Where-Object { $_ -like '--demo=*' }
  $logicalWidth=if($isCategory){960}else{1180};$logicalHeight=if($isCategory){680}else{720}
  foreach($observation in $observations){
   $expectedWidth=[int][Math]::Round($logicalWidth*$observation.Dpi/96d)
   $expectedHeight=[int][Math]::Round($logicalHeight*$observation.Dpi/96d)
   if($observation.Width-ne$expectedWidth -or $observation.Height-ne$expectedHeight){
    throw "DPI round-trip client mismatch at $($observation.Dpi) DPI: $($observation.Width)x$($observation.Height), expected ${expectedWidth}x${expectedHeight}."
   }
  }
  $groups=$observations|Group-Object Dpi
  foreach($group in $groups){
   if((@($group.Group|Select-Object Width,Height -Unique)).Count-ne1){throw "Client size drift detected after repeated $($group.Name) DPI visits."}
  }
  Write-Host "DPI_ROUNDTRIP_OK logical=${logicalWidth}x${logicalHeight} dpi=$($groups.Name -join ',')"
 }
}finally{if($null -ne $p -and !$p.HasExited){$p.Kill();$p.WaitForExit()}}
