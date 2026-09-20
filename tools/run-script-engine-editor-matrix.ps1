[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$StressIterations = 3,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$IncludeX86,
    [switch]$IncludeSingleFilePublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Invoke-DotNet([string[]]$Arguments) {
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet 命令失败，退出码: $LASTEXITCODE"
    }
}

$testMatrix = @(
    @{ Project = 'tests/Platform/ScriptEngine.Tests/ScriptEngine.Tests.csproj'; Framework = 'net8.0' },
    @{ Project = 'tests/Platform/ScriptEngine.Tests/ScriptEngine.Tests.csproj'; Framework = 'net48' },
    @{ Project = 'tests/Platform/ScriptEngine.Workspaces.Tests/ScriptEngine.Workspaces.Tests.csproj'; Framework = 'net8.0' },
    @{ Project = 'tests/Platform/ScriptEngine.Workspaces.Tests/ScriptEngine.Workspaces.Tests.csproj'; Framework = 'net48' },
    @{ Project = 'tests/Platform/ScriptEngine.Windows.Tests/ScriptEngine.Windows.Tests.csproj'; Framework = 'net8.0-windows' },
    @{ Project = 'tests/Platform/ScriptEngine.Windows.Tests/ScriptEngine.Windows.Tests.csproj'; Framework = 'net48' }
)

Push-Location $root
try {
    Invoke-DotNet @('restore', 'DP.WorkFlow.sln', '--force-evaluate')
    foreach ($entry in $testMatrix) {
        Invoke-DotNet @(
            'test', $entry.Project,
            '--framework', $entry.Framework,
            '--configuration', $Configuration,
            '--no-restore',
            '-m:1'
        )
    }

    for ($iteration = 1; $iteration -le $StressIterations; $iteration++) {
        Write-Host "大型源码/缓存压力轮次 $iteration/$StressIterations" -ForegroundColor Yellow
        Invoke-DotNet @(
            'test', 'tests/Platform/ScriptEngine.Tests/ScriptEngine.Tests.csproj',
            '--framework', 'net8.0',
            '--configuration', $Configuration,
            '--no-restore',
            '--filter', 'FullyQualifiedName~Analysis_RapidLargeSnapshotsKeepsCacheBoundedAndLatestSnapshotValid',
            '-m:1'
        )
    }

    if ($IncludeX86) {
        try {
            Invoke-DotNet @(
                'test', 'tests/Platform/ScriptEngine.Windows.Tests/ScriptEngine.Windows.Tests.csproj',
                '--framework', 'net48',
                '--configuration', $Configuration,
                '--no-restore',
                '-p:PlatformTarget=x86',
                '-p:IntermediateOutputPath=obj/script-engine-x86/',
                '-p:OutputPath=bin/script-engine-x86/',
                '-m:1'
            )
        }
        finally {
            Get-ChildItem -Path $root -Directory -Recurse -Filter 'script-engine-x86' |
                Remove-Item -Recurse -Force
        }
    }

    if ($IncludeSingleFilePublish) {
        $singleFileOutput = Join-Path $root 'artifacts/script-engine-single-file/'
        Invoke-DotNet @(
            'publish', 'samples/ScriptEngine.WinForms.Demo/ScriptEngine.WinForms.Demo.csproj',
            '--configuration', $Configuration,
            '--runtime', 'win-x64',
            '--self-contained', 'false',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:IncludeAllContentForSelfExtract=true',
            "-p:PublishDir=$singleFileOutput"
        )
        & (Join-Path $singleFileOutput 'ScriptEngine.WinForms.Demo.exe') --single-file-smoke
        if ($LASTEXITCODE -ne 0) { throw "单文件 UI/编译冒烟失败，退出码: $LASTEXITCODE" }
    }
}
finally {
    Pop-Location
}

Write-Host 'ScriptEngine 编辑器验证矩阵全部通过。' -ForegroundColor Green
