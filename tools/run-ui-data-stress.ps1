param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('All', 'net48', 'net8.0-windows')][string]$TargetFramework = 'All',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'tests/Platform/ModernUI.WinForms.DataStress/ModernUI.WinForms.DataStress.csproj'
if (-not $NoBuild) {
    & dotnet build $project -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Data stress project build failed.' }
}
$frameworks = if ($TargetFramework -eq 'All') { @('net48', 'net8.0-windows') } else { @($TargetFramework) }
foreach ($framework in $frameworks) {
    $executable = Join-Path $root "tests/Platform/ModernUI.WinForms.DataStress/bin/$Configuration/$framework/ModernUI.WinForms.DataStress.exe"
    if (-not (Test-Path $executable)) { throw "Data stress executable was not found: $executable" }
    $output = & $executable 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "Data stress failed for $framework with exit code $exitCode." }
    if (-not ($output -match 'DATA_STRESS_OK')) { throw "Data stress success marker was not produced for $framework." }
}
Write-Host "UI_DATA_STRESS_OK frameworks=$($frameworks -join ',')"
