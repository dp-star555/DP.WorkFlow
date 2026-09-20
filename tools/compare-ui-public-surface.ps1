param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$BaselinePath = '',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $root 'src/Platform/Desktop/ModernUI.WinForms/PublicApi.Shipped.txt'
}
$base = Join-Path $root "src/Platform/Desktop/ModernUI.WinForms/bin/$Configuration"
$net48 = [Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $base 'net48/ModernUI.WinForms.dll')))
$net8 = [Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $base 'net8.0-windows/ModernUI.WinForms.dll')))
function Get-PublicSurface([Reflection.Assembly]$assembly) {
    @($assembly.GetExportedTypes() | ForEach-Object {
        $type = $_
        $type.FullName
        $type.GetMembers([Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly') |
            Where-Object { $_.MemberType -in @('Method','Property','Event','Field','NestedType') } |
            ForEach-Object { "  $($_.MemberType):$($_.ToString())" }
    } | Sort-Object)
}
$framework = @(Get-PublicSurface $net48)
$modern = @(Get-PublicSurface $net8)
$difference = @(Compare-Object $framework $modern)
if ($difference.Count -gt 0) {
    $difference | Format-Table -AutoSize
    throw "net48 and net8 public surfaces differ in $($difference.Count) entries."
}

if ($UpdateBaseline) {
    $directory = Split-Path $BaselinePath -Parent
    if (-not (Test-Path $directory)) { New-Item $directory -ItemType Directory -Force | Out-Null }
    $framework | Set-Content $BaselinePath -Encoding UTF8
    Write-Host "PUBLIC_SURFACE_BASELINE_UPDATED path=$BaselinePath entries=$($framework.Count)"
}
elseif (-not (Test-Path $BaselinePath)) {
    throw "Public interface baseline is missing: $BaselinePath. Use -UpdateBaseline for an intentional release change."
}
else {
    $baseline = @(Get-Content $BaselinePath)
    $baselineDifference = @(Compare-Object $baseline $framework)
    if ($baselineDifference.Count -gt 0) {
        $baselineDifference | Format-Table -AutoSize
        throw "Current public interface differs from the shipped baseline in $($baselineDifference.Count) entries."
    }
}
Write-Host "PUBLIC_SURFACE_OK entries=$($framework.Count) baseline=$BaselinePath"
