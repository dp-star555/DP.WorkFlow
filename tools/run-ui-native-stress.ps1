param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateRange(1, 5000)][int]$Iterations = 120,
    [ValidateSet('All', 'net48', 'net8.0-windows')][string]$TargetFramework = 'All',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'tests/Platform/ModernUI.WinForms.NativeStress/ModernUI.WinForms.NativeStress.csproj'
if (-not $NoBuild) {
    & dotnet build $project -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Native stress project build failed.' }
}

$frameworks = if ($TargetFramework -eq 'All') { @('net48', 'net8.0-windows') } else { @($TargetFramework) }
foreach ($framework in $frameworks) {
    $executable = Join-Path $root "tests/Platform/ModernUI.WinForms.NativeStress/bin/$Configuration/$framework/ModernUI.WinForms.NativeStress.exe"
    if (-not (Test-Path $executable)) { throw "Native stress executable was not found: $executable" }
    $output = & $executable "--iterations=$Iterations" 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "Native stress failed for $framework with exit code $exitCode." }
    if (-not ($output -match "NATIVE_STRESS_OK iterations=$Iterations")) {
        throw "Native stress success marker was not produced for $framework."
    }
}

Write-Host "UI_NATIVE_STRESS_OK frameworks=$($frameworks -join ',') iterations=$Iterations"
