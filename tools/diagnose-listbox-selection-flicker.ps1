param(
    [ValidateSet('Release','Debug')][string]$Configuration='Release',
    [ValidateSet('net48','net8.0-windows')][string]$TargetFramework='net8.0-windows',
    [ValidateSet(96,144)][int]$TargetDpi=144,
    [ValidateRange(12,100)][int]$DragSteps=36,
    [ValidateRange(2,40)][int]$DragDelayMilliseconds=8,
    [string]$OutputDirectory='.artifacts/listbox-selection-flicker-diagnostic'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=if([IO.Path]::IsPathRooted($OutputDirectory)){$OutputDirectory}else{Join-Path $root $OutputDirectory}
if(Test-Path $output){Remove-Item $output -Recurse -Force};New-Item -ItemType Directory -Force $output|Out-Null
$exe=Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/$TargetFramework/ModernUI.WinForms.Gallery.exe"
if(-not(Test-Path $exe)){throw "Gallery missing: $exe"}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies 'System.Drawing.dll','System.Windows.Forms.dll' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
public static class ListSelectionFlickerNative {
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
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,ref RECT l);
 [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetScrollPos(IntPtr h,int bar);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
 [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h,IntPtr dc);
 [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d,int x,int y,int w,int h,IntPtr s,int sx,int sy,int op);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 public static string Class(IntPtr h){var b=new StringBuilder(128);GetClassName(h,b,b.Capacity);return b.ToString();}
 public static IntPtr[] ProcessWindows(uint pid){var a=new List<IntPtr>();EnumWindows(delegate(IntPtr h,IntPtr s){uint p;GetWindowThreadProcessId(h,out p);if(p==pid&&IsWindowVisible(h))a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static IntPtr[] Descendants(IntPtr root){var a=new List<IntPtr>();EnumChildWindows(root,delegate(IntPtr h,IntPtr s){a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static IntPtr[] DirectChildren(IntPtr root){var a=new List<IntPtr>();EnumChildWindows(root,delegate(IntPtr h,IntPtr s){if(GetParent(h)==root)a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
 public static RECT ItemRect(IntPtr list,int index){var r=new RECT();SendMessage(list,0x0198,(IntPtr)index,ref r);return r;}
 public static Bitmap Capture(IntPtr h,bool visible){RECT r;GetClientRect(h,out r);var b=new Bitmap(Math.Max(1,r.Width),Math.Max(1,r.Height),PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(b)){var d=g.GetHdc();try{if(visible){var s=GetDC(h);try{BitBlt(d,0,0,b.Width,b.Height,s,0,0,0x00CC0020);}finally{ReleaseDC(h,s);}}else PrintWindow(h,d,3);}finally{g.ReleaseHdc(d);}}return b;}
 public static Rectangle InkBounds(Bitmap b){int l=b.Width,t=b.Height,r=-1,bt=-1;var bg=b.GetPixel(0,0);for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++){var c=b.GetPixel(x,y);if(Math.Abs(c.R-bg.R)+Math.Abs(c.G-bg.G)+Math.Abs(c.B-bg.B)<=12)continue;l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x);bt=Math.Max(bt,y);}return r<l?Rectangle.Empty:Rectangle.FromLTRB(l,t,r+1,bt+1);}
 public static RowSnapshot AnalyzeRow(Bitmap bitmap,RECT source,Color expected){int left=Math.Max(2,source.Left+2),top=Math.Max(0,source.Top+2),right=Math.Min(bitmap.Width-30,source.Right-2),bottom=Math.Min(bitmap.Height,source.Bottom-2);long expectedCount=0,white=0,dark=0,darkX=0,total=0;ulong hash=14695981039346656037UL;var colors=new Dictionary<int,int>();for(int y=top;y<bottom;y++)for(int x=left;x<right;x++){var c=bitmap.GetPixel(x,y);var distance=Math.Abs(c.R-expected.R)+Math.Abs(c.G-expected.G)+Math.Abs(c.B-expected.B);if(distance<=9)expectedCount++;if(c.R>245&&c.G>245&&c.B>245)white++;if((c.R*299+c.G*587+c.B*114)/1000<150){dark++;darkX+=x;}hash=(hash^(byte)c.R)*1099511628211UL;hash=(hash^(byte)c.G)*1099511628211UL;hash=(hash^(byte)c.B)*1099511628211UL;int key=c.ToArgb();int count;colors.TryGetValue(key,out count);colors[key]=count+1;total++;}int scanY=Math.Min(bitmap.Height-1,Math.Max(0,source.Top+4)),runs=0,longest=0,current=0;bool inside=false;for(int x=0;x<bitmap.Width;x++){var c=bitmap.GetPixel(x,scanY);bool match=Math.Abs(c.R-expected.R)+Math.Abs(c.G-expected.G)+Math.Abs(c.B-expected.B)<=9;if(match){if(!inside){runs++;inside=true;}current++;longest=Math.Max(longest,current);}else{inside=false;current=0;}}int dominant=0,dominantColor=0;foreach(var pair in colors)if(pair.Value>dominant){dominant=pair.Value;dominantColor=pair.Key;}return new RowSnapshot{Expected=expectedCount,White=white,Dark=dark,DarkX=darkX,Total=total,Hash=hash,SelectedRuns=runs,LongestSelectedRun=longest,Dominant=dominant,DominantColor=unchecked((uint)dominantColor)};}
 public static Task DragAsync(IntPtr h,int start,int end,int y,int steps,int delay){return Task.Factory.StartNew(delegate{SendMessage(h,0x0201,(IntPtr)1,Pack(start,y));for(int i=0;i<=steps;i++){int x=(int)Math.Round(start+(end-start)*i/(double)steps);SendMessage(h,0x0200,(IntPtr)1,Pack(x,y));if(delay>0)Thread.Sleep(delay);}SendMessage(h,0x0202,IntPtr.Zero,Pack(end,y));});}
 public static IntPtr Pack(int x,int y){return(IntPtr)((y<<16)|(x&0xffff));}
 public sealed class RowSnapshot{public long Expected,White,Dark,DarkX,Total;public ulong Hash;public int SelectedRuns,LongestSelectedRun,Dominant;public uint DominantColor;}
}
'@
[ListSelectionFlickerNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
function Wait-Main($process) {
    for ($index = 0; $index -lt 150; $index++) {
        if ($process.HasExited) { throw "Gallery exited $($process.ExitCode)" }
        $window = @([ListSelectionFlickerNative]::ProcessWindows([uint32]$process.Id)) |
            Sort-Object {
                $rectangle = New-Object ListSelectionFlickerNative+RECT
                [ListSelectionFlickerNative]::GetWindowRect($_, [ref]$rectangle) | Out-Null
                -($rectangle.Width * $rectangle.Height)
            } | Select-Object -First 1
        if ($null -ne $window) { return $window }
        Start-Sleep -Milliseconds 40
    }
    throw 'Main HWND unavailable'
}
function Move-ToDpi([IntPtr]$window) {
    foreach ($screen in [Windows.Forms.Screen]::AllScreens) {
        [ListSelectionFlickerNative]::SetWindowPos($window, [IntPtr]::Zero,
            $screen.WorkingArea.Left + 40, $screen.WorkingArea.Top + 40, 0, 0, 0x0001) | Out-Null
        Start-Sleep -Milliseconds 700
        if ([ListSelectionFlickerNative]::GetDpiForWindow($window) -eq $TargetDpi) { return $screen }
    }
    throw "No connected display reports $TargetDpi DPI"
}
function Get-WindowRectangle([IntPtr]$window) {
    $rectangle = New-Object ListSelectionFlickerNative+RECT
    [ListSelectionFlickerNative]::GetWindowRect($window, [ref]$rectangle) | Out-Null
    return $rectangle
}

$process = Start-Process $exe -ArgumentList @('--no-animation', '--demo=DataDisplay', '--scroll=300') -PassThru
try {
    $main = Wait-Main $process
    $screen = Move-ToDpi $main
    Start-Sleep -Milliseconds 1800
    $all = @([ListSelectionFlickerNative]::Descendants($main))
    $list = $null
    $horizontal = $null
    foreach ($candidate in $all | Where-Object { [ListSelectionFlickerNative]::Class($_) -like '*ListBox*' }) {
        $children = @([ListSelectionFlickerNative]::DirectChildren($candidate) |
            Where-Object { [ListSelectionFlickerNative]::IsWindowVisible($_) })
        foreach ($child in $children) {
            $rectangle = Get-WindowRectangle $child
            if ($rectangle.Width -gt $rectangle.Height * 2) { $horizontal = $child }
        }
        if ($null -ne $horizontal) { $list = $candidate; break }
    }
    if ($null -eq $list -or $null -eq $horizontal) { throw 'Modern ListBox horizontal overlay not found.' }

    [ListSelectionFlickerNative]::SendMessage($list, 0x0197, [IntPtr]3, [IntPtr]::Zero) | Out-Null
    [ListSelectionFlickerNative]::SendMessage($list, 0x0186, [IntPtr]8, [IntPtr]::Zero) | Out-Null
    [ListSelectionFlickerNative]::SetFocus($list) | Out-Null
    Start-Sleep -Milliseconds 100
    $selected = [ListSelectionFlickerNative]::ItemRect($list, 8)
    if ($selected.Height -le 0) { throw 'Selected item rectangle is empty.' }
    $before = [ListSelectionFlickerNative]::Capture($list, $true)
    try {
        $sampleX = [Math]::Min($before.Width - 40, 4)
        $sampleY = [Math]::Min($before.Height - 1, $selected.Top + [int]($selected.Height / 2))
        $baseline = $before.GetPixel($sampleX, $sampleY)
        $before.Save((Join-Path $output 'baseline.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $before.Dispose() }

    $scroll = [ListSelectionFlickerNative]::Capture($horizontal, $false)
    try { $thumb = [ListSelectionFlickerNative]::InkBounds($scroll) }
    finally { $scroll.Dispose() }
    if ($thumb.IsEmpty) { throw 'Horizontal thumb not found.' }
    $horizontalRect = New-Object ListSelectionFlickerNative+RECT
    [ListSelectionFlickerNative]::GetClientRect($horizontal, [ref]$horizontalRect) | Out-Null
    $start = $thumb.Left + [int]($thumb.Width / 2)
    $end = [Math]::Min($horizontalRect.Right - 8,
        $start + [Math]::Max(70, [int]($horizontalRect.Right * 0.38)))
    $y = $thumb.Top + [int]($thumb.Height / 2)

    $rows = @()
    $drag = [ListSelectionFlickerNative]::DragAsync(
        $horizontal, $start, $end, $y, $DragSteps, $DragDelayMilliseconds)
    $frame = 0
    while (-not $drag.IsCompleted -or $frame -lt 6) {
        $bitmap = [ListSelectionFlickerNative]::Capture($list, $true)
        try {
            $rectangle = [ListSelectionFlickerNative]::ItemRect($list, 8)
            $snapshot = [ListSelectionFlickerNative]::AnalyzeRow($bitmap, $rectangle, $baseline)
            $path = Join-Path $output ('{0:D3}.png' -f $frame)
            $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            $ratio = if ($snapshot.Total) { $snapshot.Expected / [double]$snapshot.Total } else { 0 }
            $darkCenter = if ($snapshot.Dark) { $snapshot.DarkX / [double]$snapshot.Dark } else { -1 }
            $rows += [pscustomobject]@{
                Frame = $frame
                Position = [ListSelectionFlickerNative]::GetScrollPos($list, 0)
                Item = "$($rectangle.Left),$($rectangle.Top),$($rectangle.Width)x$($rectangle.Height)"
                SelectedRatio = [Math]::Round($ratio, 4)
                DarkCenter = [Math]::Round($darkCenter, 2)
                White = $snapshot.White
                Dark = $snapshot.Dark
                Total = $snapshot.Total
                RowHash = $snapshot.Hash.ToString('X16')
                SelectedRuns = $snapshot.SelectedRuns
                LongestSelectedRun = $snapshot.LongestSelectedRun
                Dominant = $snapshot.Dominant
                DominantColor = '0x' + $snapshot.DominantColor.ToString('X8')
            }
        }
        finally { $bitmap.Dispose() }
        $frame++
    }
    $drag.Wait()
    $rows | Export-Csv (Join-Path $output 'frames.csv') -NoTypeInformation -Encoding UTF8
    $rows | Format-Table -AutoSize
    $minimum = ($rows.SelectedRatio | Measure-Object -Minimum).Minimum
    $maximum = ($rows.SelectedRatio | Measure-Object -Maximum).Maximum
    $positions = @($rows.Position | Select-Object -Unique)
    $darkCenters = @($rows.DarkCenter | Where-Object { $_ -ge 0 })
    $darkCenterRange = ($darkCenters | Measure-Object -Maximum).Maximum -
        ($darkCenters | Measure-Object -Minimum).Minimum
    $rowHashes = @($rows.RowHash | Select-Object -Unique)
    $maximumSelectedRuns = ($rows.SelectedRuns | Measure-Object -Maximum).Maximum
    $result = "framework=$TargetFramework dpi=$TargetDpi frames=$($rows.Count) " +
        "baseline=$($baseline.ToArgb().ToString('X8')) selectedRatioMin=$minimum " +
        "selectedRatioMax=$maximum positions=$($positions.Count) rowHashes=$($rowHashes.Count) maxSelectedRuns=$maximumSelectedRuns darkCenterRange=$darkCenterRange " +
        "screen=$($screen.DeviceName)"
    $result | Set-Content (Join-Path $output 'result.txt')
    $result
    if ($minimum -lt 0.75) {
        throw "LISTBOX_SELECTION_FLICKER selectedRatioMin=$minimum expectedAtLeast=0.75 output=$output"
    }
    if ($positions.Count -lt 5 -or $rowHashes.Count -lt 5) {
        throw "LISTBOX_HORIZONTAL_CONTENT_FROZEN positions=$($positions.Count) rowHashes=$($rowHashes.Count) output=$output"
    }
    if ($maximumSelectedRuns -gt 1) {
        throw "LISTBOX_SELECTION_FRAGMENTED maxSelectedRuns=$maximumSelectedRuns output=$output"
    }
    Write-Host "LISTBOX_SELECTION_FRAMES_OK frames=$($rows.Count) selectedRatioMin=$minimum positions=$($positions.Count) rowHashes=$($rowHashes.Count) maxSelectedRuns=$maximumSelectedRuns"
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
