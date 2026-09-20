param(
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release',
    [ValidateSet('net48','net8.0-windows')][string]$TargetFramework = 'net8.0-windows',
    [ValidateRange(10,100)][int]$DragSteps = 30,
    [ValidateRange(1,50)][int]$FrameDelayMilliseconds = 4,
    [string]$OutputDirectory = '.artifacts/tree-flicker-diagnostic'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force $output | Out-Null
$exe = if ($TargetFramework -eq 'net48') {
    Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/net48/ModernUI.WinForms.Gallery.exe"
} else {
    Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/net8.0-windows/ModernUI.WinForms.Gallery.exe"
}

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

public static class TreeFlickerNative
{
    public const int WM_LBUTTONDOWN=0x0201, WM_LBUTTONUP=0x0202, WM_MOUSEMOVE=0x0200;
    public const int TV_FIRST=0x1100, TVM_GETEXTENDEDSTYLE=TV_FIRST+45;
    public const int TVS_EX_DOUBLEBUFFER=0x0004;
    public delegate bool EnumChildProc(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr state);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest,int x,int y,int width,int height,IntPtr source,int sx,int sy,int operation);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    public static IntPtr FindClass(IntPtr root, string className)
    {
        IntPtr result=IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr hwnd, IntPtr state) {
            var text=new StringBuilder(128); GetClassName(hwnd,text,text.Capacity);
            if (String.Equals(text.ToString(),className,StringComparison.Ordinal)) { result=hwnd; return false; }
            return true;
        },IntPtr.Zero);
        return result;
    }
    public static IntPtr[] Children(IntPtr root)
    {
        var result=new List<IntPtr>();
        EnumChildWindows(root, delegate(IntPtr hwnd, IntPtr state) { result.Add(hwnd); return true; }, IntPtr.Zero);
        return result.ToArray();
    }
    public static string ClassName(IntPtr hwnd) { var s=new StringBuilder(128);GetClassName(hwnd,s,s.Capacity);return s.ToString(); }
    public static Bitmap Capture(IntPtr hwnd)
    {
        RECT r; GetClientRect(hwnd,out r); var w=Math.Max(1,r.Right-r.Left);var h=Math.Max(1,r.Bottom-r.Top);
        var bitmap=new Bitmap(w,h,PixelFormat.Format32bppArgb); using(var g=Graphics.FromImage(bitmap)) { var dc=g.GetHdc(); try { PrintWindow(hwnd,dc,3); } finally { g.ReleaseHdc(dc); } } return bitmap;
    }
    public static Bitmap CaptureVisibleSurface(IntPtr hwnd)
    {
        RECT r; GetClientRect(hwnd,out r); var w=Math.Max(1,r.Right-r.Left);var h=Math.Max(1,r.Bottom-r.Top);
        var bitmap=new Bitmap(w,h,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(bitmap)){var dest=g.GetHdc();var source=GetDC(hwnd);try{BitBlt(dest,0,0,w,h,source,0,0,0x00CC0020);}finally{ReleaseDC(hwnd,source);g.ReleaseHdc(dest);}}return bitmap;
    }
    public static Task DragAsync(IntPtr hwnd,int startX,int endX,int y,int steps,int delay)
    {
        return Task.Factory.StartNew(delegate {
            SendMessage(hwnd,WM_LBUTTONDOWN,(IntPtr)1,Pack(startX,y));
            for(int step=0;step<=steps;step++){int x=(int)Math.Round(startX+(endX-startX)*step/(double)steps);SendMessage(hwnd,WM_MOUSEMOVE,(IntPtr)1,Pack(x,y));if(delay>0)Thread.Sleep(delay);}
            SendMessage(hwnd,WM_LBUTTONUP,IntPtr.Zero,Pack(endX,y));
        });
    }
    public static Rectangle InkBounds(Bitmap bitmap)
    {
        int left=bitmap.Width,top=bitmap.Height,right=-1,bottom=-1;var bg=bitmap.GetPixel(0,0);
        for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) { var c=bitmap.GetPixel(x,y);if(Math.Abs(c.R-bg.R)+Math.Abs(c.G-bg.G)+Math.Abs(c.B-bg.B)<=12)continue;left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y); }
        return right<left?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    public static Snapshot Analyze(Bitmap bitmap)
    {
        long dark=0, nearBackground=0; int min=255,max=0; ulong hash=14695981039346656037UL;
        for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) { var c=bitmap.GetPixel(x,y);int l=(c.R*299+c.G*587+c.B*114)/1000;min=Math.Min(min,l);max=Math.Max(max,l);if(l<130)dark++;if(l>238)nearBackground++;hash=(hash^(byte)c.R)*1099511628211UL;hash=(hash^(byte)c.G)*1099511628211UL;hash=(hash^(byte)c.B)*1099511628211UL; }
        return new Snapshot { Dark=dark, Background=nearBackground, Min=min, Max=max, Hash=hash };
    }
    public sealed class Snapshot { public long Dark,Background;public int Min,Max;public ulong Hash; }
    public static IntPtr Pack(int x,int y) { return (IntPtr)((y<<16)|(x&0xffff)); }
}
'@

$process = Start-Process $exe -ArgumentList @('--demo=DataDisplay','--scroll=300','--location=80,80','--no-animation') -PassThru
try {
    for ($wait=0;$wait -lt 200;$wait++) {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        if ($process.HasExited) { throw "Gallery exited $($process.ExitCode)." }
        Start-Sleep -Milliseconds 20
    }
    $main=$process.MainWindowHandle
    if ($main -eq [IntPtr]::Zero) { throw 'Main HWND not found.' }
    Start-Sleep -Milliseconds 1200
    $tree=@([TreeFlickerNative]::Children($main) | Where-Object { [TreeFlickerNative]::ClassName($_) -like '*SysTreeView32*' } | Select-Object -First 1)[0]
    if ($tree -eq [IntPtr]::Zero) {
        $classes=@([TreeFlickerNative]::Children($main)|%{[TreeFlickerNative]::ClassName($_)})|group|sort Count -Descending|%{"$($_.Count)x $($_.Name)"}
        throw "SysTreeView32 not found. classes=$($classes -join '; ')"
    }
    $styles=[TreeFlickerNative]::SendMessage($tree,[TreeFlickerNative]::TVM_GETEXTENDEDSTYLE,[IntPtr]::Zero,[IntPtr]::Zero).ToInt64()
    $children=@([TreeFlickerNative]::Children($tree))
    $horizontal=$null;$thumb=[Drawing.Rectangle]::Empty
    foreach($candidate in $children | Where-Object { [TreeFlickerNative]::IsWindowVisible($_) }) {
        $candidateRect=New-Object TreeFlickerNative+RECT;[TreeFlickerNative]::GetClientRect($candidate,[ref]$candidateRect)|Out-Null
        $candidateWidth=$candidateRect.Right-$candidateRect.Left;$candidateHeight=$candidateRect.Bottom-$candidateRect.Top
        if($candidateWidth -le $candidateHeight){continue}
        $scrollBitmap=[TreeFlickerNative]::Capture($candidate)
        try { $candidateThumb=[TreeFlickerNative]::InkBounds($scrollBitmap) } finally { $scrollBitmap.Dispose() }
        if(-not $candidateThumb.IsEmpty){$horizontal=$candidate;$thumb=$candidateThumb;break}
    }
    if ($null -eq $horizontal -or $thumb.IsEmpty) {
        $childText=$children|%{$r=New-Object TreeFlickerNative+RECT;[TreeFlickerNative]::GetClientRect($_,[ref]$r)|Out-Null;"$([TreeFlickerNative]::ClassName($_)) $($r.Right)x$($r.Bottom) visible=$([TreeFlickerNative]::IsWindowVisible($_))"}
        throw "Horizontal thumb not found. children=$($childText -join '; ')"
    }
    $hr=New-Object TreeFlickerNative+RECT;[TreeFlickerNative]::GetClientRect($horizontal,[ref]$hr)|Out-Null
    $startX=$thumb.Left+[int]($thumb.Width/2);$endX=[Math]::Min($hr.Right-8,$startX+[Math]::Max(60,[int](($hr.Right-$hr.Left)*0.35)));$y=$thumb.Top+[int]($thumb.Height/2)
    $rows=@();$drag=[TreeFlickerNative]::DragAsync($horizontal,$startX,$endX,$y,$DragSteps,$FrameDelayMilliseconds);$frame=0
    while(-not $drag.IsCompleted -or $frame -lt 5) {
        $bitmap=[TreeFlickerNative]::CaptureVisibleSurface($tree)
        try {
            $snapshot=[TreeFlickerNative]::Analyze($bitmap)
            $path=Join-Path $output ('{0:D3}.png' -f $frame);$bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png)
            $rows += [pscustomobject]@{Step=$frame;Dark=$snapshot.Dark;Background=$snapshot.Background;Min=$snapshot.Min;Max=$snapshot.Max;Hash=$snapshot.Hash.ToString('X16')}
        } finally { $bitmap.Dispose() }
        $frame++
    }
    $drag.Wait()
    $rows | Export-Csv (Join-Path $output 'frames.csv') -NoTypeInformation -Encoding UTF8
    $positive=@($rows|?{$_.Dark -gt 0}|% Dark);$median=if($positive.Count){($positive|sort)[[int][Math]::Floor(($positive.Count-1)/2)]}else{0};$minimum=if($positive.Count){($positive|Measure-Object -Minimum).Minimum}else{0}
    "framework=$TargetFramework tree=0x$($tree.ToInt64().ToString('X')) extendedStyle=0x$($styles.ToString('X')) doubleBuffer=$(($styles -band [TreeFlickerNative]::TVS_EX_DOUBLEBUFFER)-ne 0) frames=$($rows.Count) darkMedian=$median darkMinimum=$minimum" | Set-Content (Join-Path $output 'result.txt')
    Get-Content (Join-Path $output 'result.txt')
    $rows | Format-Table -AutoSize
}
finally {
    if (-not $process.HasExited) { $process.Kill();$process.WaitForExit() }
}
