param(
    [ValidateRange(1, 100)][int]$Rounds = 10,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateRange(1, 5000)][int]$NativeIterations = 120,
    [switch]$SkipBehaviorTests,
    [switch]$SkipNativeStress
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testProject = Join-Path $root 'tests/Workflow/DP.WorkFlow.UI.Windows.Tests/DP.WorkFlow.UI.Windows.Tests.csproj'
$stressProject = Join-Path $root 'tests/Platform/ModernUI.WinForms.NativeStress/ModernUI.WinForms.NativeStress.csproj'

# Shut down persistent MSBuild/VBCS servers once before the soak. They can preserve a DPI context
# from an earlier PMv2 build and launch later TestHosts outside the requested 96-DPI compatibility
# layer even though each visible PowerShell process is fresh.
& dotnet build-server shutdown | Out-Null

if (-not $SkipBehaviorTests) {
    & dotnet build $testProject -c $Configuration --no-restore --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Behavior test build failed before soak.' }
}
if (-not $SkipNativeStress) {
    & dotnet build $stressProject -c $Configuration --no-restore --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Native stress build failed before soak.' }
}

$watch = [Diagnostics.Stopwatch]::StartNew()
for ($round = 1; $round -le $Rounds; $round++) {
    Write-Host "UI_SOAK_ROUND_START round=$round/$Rounds"
    if (-not $SkipBehaviorTests) {
        # VSTest may leave a dotnet/testhost process alive after a run. A later round can otherwise
        # inherit that process's PMv2/monitor context even though its PowerShell launcher is fresh.
        & dotnet build-server shutdown | Out-Null
        $previousCompatibilityLayer = $env:__COMPAT_LAYER
        try {
            $env:__COMPAT_LAYER = 'DPIUNAWARE'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-tests-96dpi.ps1') `
                -Configuration $Configuration -NoBuild
            if ($LASTEXITCODE -ne 0) { throw "Behavior tests failed during soak round $round." }
        }
        finally { $env:__COMPAT_LAYER = $previousCompatibilityLayer }
    }
    if (-not $SkipNativeStress) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-native-stress.ps1') `
            -Configuration $Configuration -Iterations $NativeIterations -NoBuild
        if ($LASTEXITCODE -ne 0) { throw "Native stress failed during soak round $round." }
    }
    Write-Host "UI_SOAK_ROUND_OK round=$round/$Rounds"
}
$watch.Stop()
Write-Host "UI_SOAK_OK rounds=$Rounds elapsedSeconds=$([Math]::Round($watch.Elapsed.TotalSeconds, 1))"
