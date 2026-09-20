param(
    [ValidateSet('Release','Debug')][string]$Configuration='Release',
    [ValidateSet('net48','net8.0-windows')][string]$TargetFramework='net8.0-windows',
    [ValidateSet(96,144)][int]$TargetDpi=144,
    [string]$OutputDirectory='.artifacts/listbox-vertical-scroll-diagnostic'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=if([IO.Path]::IsPathRooted($OutputDirectory)){$OutputDirectory}else{Join-Path $root $OutputDirectory}
if(Test-Path $output){Remove-Item $output -Recurse -Force};New-Item -ItemType Directory -Force $output|Out-Null
$exe=Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/$TargetFramework/ModernUI.WinForms.Gallery.exe"
if(-not(Test-Path $exe)){throw "Gallery missing: $exe"}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies 'System.Drawing.dll' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
public static class ListScrollProbeNative {
 [StructLayout(LayoutKind.Sequential)] public struct RECT{public int Left,Top,Right,Bottom;public int Width{get{return Right-Left;}}public int Height{get{return Bottom-Top;}}}
 public delegate bool EnumProc(IntPtr h,IntPtr s);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p,IntPtr s);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h,EnumProc p,IntPtr s);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out RECT r);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 public static string Class(IntPtr h){var b=new StringBuilder(128);GetClassName(h,b,b.Capacity);return b.ToString();}
 public static IntPtr[] ProcessWindows(uint pid){var a=new List<IntPtr>();EnumWindows(delegate(IntPtr h,IntPtr s){uint p;GetWindowThreadProcessId(h,out p);if(p==pid&&IsWindowVisible(h))a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static IntPtr[] Descendants(IntPtr root){var a=new List<IntPtr>();EnumChildWindows(root,delegate(IntPtr h,IntPtr s){a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static IntPtr[] DirectChildren(IntPtr root){var a=new List<IntPtr>();EnumChildWindows(root,delegate(IntPtr h,IntPtr s){if(GetParent(h)==root)a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static Bitmap Capture(IntPtr h){RECT r;GetClientRect(h,out r);var b=new Bitmap(Math.Max(1,r.Width),Math.Max(1,r.Height),PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(b)){var dc=g.GetHdc();try{PrintWindow(h,dc,3);}finally{g.ReleaseHdc(dc);}}return b;}
}
'@
[ListScrollProbeNative]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
function Wait-Main($p){for($i=0;$i-lt 150;$i++){if($p.HasExited){throw "Gallery exited $($p.ExitCode)"};$w=@([ListScrollProbeNative]::ProcessWindows([uint32]$p.Id))|Sort-Object{ $r=New-Object ListScrollProbeNative+RECT;[ListScrollProbeNative]::GetWindowRect($_,[ref]$r)|Out-Null;-($r.Width*$r.Height)}|Select-Object -First 1;if($null-ne$w){return $w};Start-Sleep -Milliseconds 40};throw 'Main window unavailable'}
function Move-ToDpi([IntPtr]$h){foreach($s in [Windows.Forms.Screen]::AllScreens){[ListScrollProbeNative]::SetWindowPos($h,[IntPtr]::Zero,$s.WorkingArea.Left+40,$s.WorkingArea.Top+40,0,0,0x0001)|Out-Null;Start-Sleep -Milliseconds 700;if([ListScrollProbeNative]::GetDpiForWindow($h)-eq$TargetDpi){return $s}};throw "No connected display reports $TargetDpi DPI"}
function Rect([IntPtr]$h){$r=New-Object ListScrollProbeNative+RECT;[ListScrollProbeNative]::GetWindowRect($h,[ref]$r)|Out-Null;return $r}
function ClientRect([IntPtr]$h){$r=New-Object ListScrollProbeNative+RECT;[ListScrollProbeNative]::GetClientRect($h,[ref]$r)|Out-Null;return $r}
function Capture([IntPtr]$h,[string]$name){$b=[ListScrollProbeNative]::Capture($h);try{$b.Save((Join-Path $output $name),[Drawing.Imaging.ImageFormat]::Png)}finally{$b.Dispose()}}
$process=Start-Process $exe -ArgumentList @('--no-animation','--demo=DataDisplay','--scroll=300') -PassThru
try{
 $main=Wait-Main $process;$screen=Move-ToDpi $main;Start-Sleep -Milliseconds 1800
 $all=@([ListScrollProbeNative]::Descendants($main));$candidates=@($all|?{[ListScrollProbeNative]::Class($_)-like '*ListBox*'})
 $list=$null;$horizontal=$null;$vertical=$null
 foreach($candidate in $candidates){$children=@([ListScrollProbeNative]::DirectChildren($candidate)|?{[ListScrollProbeNative]::IsWindowVisible($_)});$wide=$null;$tall=$null;foreach($child in $children){$r=Rect $child;if($r.Width-gt$r.Height*2){$wide=$child};if($r.Height-gt$r.Width*2){$tall=$child}};if($null-ne$wide-and$null-ne$tall){$list=$candidate;$horizontal=$wide;$vertical=$tall;break}}
 if($null-eq$list){throw 'Modern ListBox with both overlay HWNDs not found.'}
 $records=@()
 function Record([string]$phase,[int]$frame){
  $lr=Rect $list;$cr=ClientRect $list;$hr=Rect $horizontal;$vr=Rect $vertical;$top=[ListScrollProbeNative]::SendMessage($list,0x018E,[IntPtr]::Zero,[IntPtr]::Zero).ToInt32();$style=[ListScrollProbeNative]::GetWindowLong($list,-16);$nativeHorizontal=($style-band 0x00100000)-ne 0
  $script:records += [pscustomobject]@{Phase=$phase;Frame=$frame;TopIndex=$top;Dpi=[ListScrollProbeNative]::GetDpiForWindow($list);List="$($lr.Left),$($lr.Top),$($lr.Width)x$($lr.Height)";Client="$($cr.Width)x$($cr.Height)";Style=('0x'+$style.ToString('X8'));NativeHorizontalVisible=$nativeHorizontal;Horizontal="$($hr.Left-$lr.Left),$($hr.Top-$lr.Top),$($hr.Width)x$($hr.Height)";HorizontalBottom=$hr.Bottom-$lr.Top;ExpectedBottom=$lr.Height;Vertical="$($vr.Left-$lr.Left),$($vr.Top-$lr.Top),$($vr.Width)x$($vr.Height)";HorizontalAtBottom=(($hr.Bottom-$lr.Top)-eq$lr.Height)}
  Capture $list ("{0:D2}-{1}-list.png"-f$frame,$phase);Capture $main ("{0:D2}-{1}-main.png"-f$frame,$phase)
 }
 Record 'before' 0
 [ListScrollProbeNative]::SendMessage($list,0x0194,[IntPtr]720,[IntPtr]::Zero)|Out-Null
 Record 'extent' 1
 $vr=Rect $vertical;$x=[Math]::Max(1,[int]($vr.Width/2));$start=[Math]::Max(8,[int]($vr.Height*0.18));$end=[Math]::Min($vr.Height-8,$start+[int]($vr.Height*0.36))
 $pack={param($px,$py)[IntPtr](($py-shl 16)-bor($px-band 0xffff))}
 [ListScrollProbeNative]::SendMessage($vertical,0x0201,[IntPtr]1,(&$pack $x $start))|Out-Null
 for($i=1;$i-le 8;$i++){$y=$start+[int](($end-$start)*$i/8);[ListScrollProbeNative]::SendMessage($vertical,0x0200,[IntPtr]1,(&$pack $x $y))|Out-Null;Start-Sleep -Milliseconds 20;Record 'drag' ($i+1)}
 [ListScrollProbeNative]::SendMessage($vertical,0x0202,[IntPtr]::Zero,(&$pack $x $end))|Out-Null;Start-Sleep -Milliseconds 100;Record 'after' 10
 $records|Export-Csv (Join-Path $output 'geometry.csv') -NoTypeInformation -Encoding UTF8
 $records|Format-Table -AutoSize
 $bad=@($records|?{-not $_.HorizontalAtBottom});$duplicates=@($records|?{$_.NativeHorizontalVisible});$tops=@($records.TopIndex|Select-Object -Unique)
 if($bad.Count-or$duplicates.Count){throw "LISTBOX_SCROLL_REPRODUCED geometryFailures=$($bad.Count) nativeHorizontalFrames=$($duplicates.Count) tops=$($tops-join',') output=$output"}
 Write-Host "LISTBOX_SCROLL_CHROME_OK frames=$($records.Count) tops=$($tops-join',') dpi=$TargetDpi screen=$($screen.DeviceName) output=$output"
}
finally{if(-not$process.HasExited){$process.Kill();$process.WaitForExit()}}
