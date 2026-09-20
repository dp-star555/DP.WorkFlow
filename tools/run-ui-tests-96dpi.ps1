param(
    [string]$Filter = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class UiTestDpiBaselineNative {
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@

# Behavior tests that assert 96-DPI logical metrics must not inherit the invoking terminal's monitor
# context. Real PMv2 behavior is tested separately by the Gallery probes using GetDpiForWindow.
[UiTestDpiBaselineNative]::SetProcessDpiAwarenessContext([IntPtr](-1)) | Out-Null
[UiTestDpiBaselineNative]::SetThreadDpiAwarenessContext([IntPtr](-1)) | Out-Null

$root = Split-Path $PSScriptRoot -Parent
$arguments = @('test', (Join-Path $root 'tests/Workflow/DP.WorkFlow.UI.Windows.Tests/DP.WorkFlow.UI.Windows.Tests.csproj'),
    '-c', $Configuration, '--no-restore', '--disable-build-servers')
if ($NoBuild) { $arguments += '--no-build' }
if (-not [string]::IsNullOrWhiteSpace($Filter)) { $arguments += @('--filter', $Filter) }
& dotnet @arguments
exit $LASTEXITCODE
