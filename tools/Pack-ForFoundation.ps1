<#
.SYNOPSIS
    把 DP.Foundation 的 Workflow 功能包要用的库打成 NuGet 包，复制到 DP.Foundation\vendor\packages。
.DESCRIPTION
    版本取根目录 Directory.Build.props 的 <Version>。改了这些库后：先把版本加一，再运行本脚本，
    然后在 DP.Foundation 里同步引用版本、vendor\README.md 的哈希，并 dotnet restore --force-evaluate 刷新锁文件。
    DP.Plugins.Runtime 来自同级的 DP.Vision 仓库，一起打包（版本与 Workflow 相同）。
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Foundation = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'DP.Foundation')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/foundation-pack'
$projects = @(
    'src/Workflow/Kernel/DP.WorkFlow.Abstractions/DP.WorkFlow.Abstractions.csproj',
    'src/Workflow/Kernel/DP.WorkFlow.Core/DP.WorkFlow.Core.csproj',
    'src/Workflow/Kernel/DP.WorkFlow.Runtime/DP.WorkFlow.Runtime.csproj',
    'src/Workflow/Persistence/DP.WorkFlow.Persistence.Json/DP.WorkFlow.Persistence.Json.csproj',
    'src/Workflow/Nodes/DP.WorkFlow.Nodes.Standard/DP.WorkFlow.Nodes.Standard.csproj',
    'src/Workflow/Nodes/DP.WorkFlow.Nodes.Composite/DP.WorkFlow.Nodes.Composite.csproj',
    'src/Workflow/Nodes/DP.WorkFlow.Nodes.Process/DP.WorkFlow.Nodes.Process.csproj',
    'src/Workflow/Nodes/DP.WorkFlow.Nodes.Motion/DP.WorkFlow.Nodes.Motion.csproj',
    'src/Platform/Scripting/ScriptEngine/ScriptEngine.csproj',
    'src/Platform/Scripting/ScriptEngine.WinForms/ScriptEngine.WinForms.csproj',
    'src/Workflow/Studio/DP.WorkFlow.UI.Shared/DP.WorkFlow.UI.Shared.csproj',
    'src/Workflow/Studio/DP.WorkFlow.UI.WinForms/DP.WorkFlow.UI.WinForms.csproj',
    '../DP.Vision/src/DP.Plugins.Runtime/DP.Plugins.Runtime.csproj'
)
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props')), '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw 'No <Version> in Directory.Build.props' }
foreach ($project in $projects) {
    # DP.Plugins.Runtime 的版本在 DP.Vision 里另有定义，这里统一成 Workflow 的版本，依赖关系才对得上。
    & dotnet pack (Join-Path $root $project) -c $Configuration -o $output --nologo "-p:Version=$version"
    if ($LASTEXITCODE -ne 0) { throw "Pack failed: $project" }
}
$target = Join-Path $Foundation 'vendor/packages'
if (Test-Path $target) {
    Get-ChildItem $target -Filter '*.nupkg' | Where-Object { $_.Name -match '^(DP\.WorkFlow\.|DP\.Plugins\.Runtime\.)' } | Remove-Item
    Get-ChildItem $output -Filter "*.$version.nupkg" | ForEach-Object {
        Copy-Item $_.FullName $target -Force
        Write-Host ("{0} {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
    }
}
Write-Host "PACK_OK $version"
