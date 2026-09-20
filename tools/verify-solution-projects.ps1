param([switch]$FailOnMissing)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'DP.WorkFlow.sln'
$searchRoots = @(
    'src',
    'tests',
    'samples/ModernUI.WinForms.Sample',
    'samples/DP.WorkFlow.WinForms.Sample',
    'samples/ScriptEngine.WinForms.Demo'
) |
    ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ }
$discovered = @($searchRoots | ForEach-Object { Get-ChildItem $_ -Recurse -Filter *.csproj } |
    ForEach-Object FullName | Sort-Object -Unique)
$listed = @(& dotnet sln $solution list | Where-Object { $_ -match '\.csproj$' } |
    ForEach-Object { [IO.Path]::GetFullPath((Join-Path $root ($_ -replace '\\', '/'))) } | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw 'Unable to list solution projects.' }
$difference = @(Compare-Object $discovered $listed)
if ($difference.Count -gt 0) {
    $difference | Format-Table -AutoSize
    if ($FailOnMissing) { throw "Solution project set differs from discovered projects in $($difference.Count) entries." }
}
Write-Host "SOLUTION_PROJECTS_OK listed=$($listed.Count) discovered=$($discovered.Count) differences=$($difference.Count)"
