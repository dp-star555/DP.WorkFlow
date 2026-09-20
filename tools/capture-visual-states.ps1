param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory = '',
    [string]$RepositoryRoot = '',
    [switch]$IncludeEnglish,
    [int]$TargetDpi = 0
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $RepositoryRoot = Split-Path -Parent $PSScriptRoot
    }
    else {
        $candidate = Join-Path (Get-Location) 'DP.WorkFlow'
        $RepositoryRoot = if (Test-Path $candidate) { $candidate } else { Get-Location }
    }
}
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$project = Join-Path $root 'src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts/visual-states'
}

& dotnet build $project -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Gallery build failed with exit code $LASTEXITCODE." }

$exe = Join-Path $root "src/Platform/Desktop/ModernUI.WinForms.Gallery/bin/$Configuration/net8.0-windows/ModernUI.WinForms.Gallery.exe"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class VisualStatesCaptureNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
"@

# PowerShell 默认是 DPI unaware；不切换上下文时 GetWindowRect 会返回虚拟化的 96 DPI 尺寸，
# 导致 125%/150%/200% 截图位图本身就被截断。
[VisualStatesCaptureNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

$offsets = @(0, 520, 1040, 1560, 2080)
$locales = @(@{ Name = 'zh-CN'; Argument = '' })
if ($IncludeEnglish) { $locales += @{ Name = 'en-US'; Argument = ' --english' } }
foreach ($locale in $locales) {
  foreach ($isDark in @($false, $true)) {
    foreach ($offset in $offsets) {
        $arguments = "--visual-states --scroll=$offset$($locale.Argument)"
        if ($isDark) { $arguments += ' --dark' }
        $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 80
                $process.Refresh()
            }
            if ($process.MainWindowHandle -eq 0) { throw "Gallery window did not open for $arguments." }
            if ($TargetDpi -gt 0) {
                $matched = $false
                foreach ($screen in [Windows.Forms.Screen]::AllScreens) {
                    [VisualStatesCaptureNative]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero,
                        $screen.WorkingArea.Left + 30, $screen.WorkingArea.Top + 30, 0, 0, 0x0015) | Out-Null
                    Start-Sleep -Milliseconds 350
                    if ([VisualStatesCaptureNative]::GetDpiForWindow($process.MainWindowHandle) -eq $TargetDpi) {
                        $matched = $true
                        break
                    }
                }
                if (-not $matched) { throw "No monitor with $TargetDpi DPI is available." }
            }
            Start-Sleep -Milliseconds 500

            $rectangle = New-Object VisualStatesCaptureNative+RECT
            [VisualStatesCaptureNative]::GetWindowRect($process.MainWindowHandle, [ref]$rectangle) | Out-Null
            $bitmap = New-Object Drawing.Bitmap($($rectangle.Right - $rectangle.Left), $($rectangle.Bottom - $rectangle.Top))
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $deviceContext = $graphics.GetHdc()
                try { [VisualStatesCaptureNative]::PrintWindow($process.MainWindowHandle, $deviceContext, 2) | Out-Null }
                finally { $graphics.ReleaseHdc($deviceContext) }
                $theme = if ($isDark) { 'dark' } else { 'light' }
                $prefix = if ($locale.Name -eq 'zh-CN') { '' } else { "$($locale.Name)-" }
                $bitmap.Save((Join-Path $OutputDirectory "$prefix$theme-$offset.png"))
            }
            finally {
                $graphics.Dispose()
                $bitmap.Dispose()
            }
        }
        finally {
            if (-not $process.HasExited) {
                $process.CloseMainWindow() | Out-Null
                $process.WaitForExit(2000) | Out-Null
            }
            if (-not $process.HasExited) { $process.Kill() }
        }
    }
  }
}

Write-Host "Visual-state captures written to $OutputDirectory"
