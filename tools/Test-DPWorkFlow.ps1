param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projects = @(
    'tests/Workflow/DP.WorkFlow.Nodes.Vision.Tests/DP.WorkFlow.Nodes.Vision.Tests.csproj',
    'tests/Platform/ScriptEngine.Tests/ScriptEngine.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Core.Tests/DP.WorkFlow.Core.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Composite.Tests/DP.WorkFlow.Nodes.Composite.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Motion.Tests/DP.WorkFlow.Nodes.Motion.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Process.Tests/DP.WorkFlow.Nodes.Process.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Standard.Tests/DP.WorkFlow.Nodes.Standard.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Persistence.Tests/DP.WorkFlow.Persistence.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Runtime.Tests/DP.WorkFlow.Runtime.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.UI.Shared.Tests/DP.WorkFlow.UI.Shared.Tests.csproj',
    # Keep the WinForms suite in its own testhost so its module initializer can establish 96-DPI behavior tests.
    'tests/Workflow/DP.WorkFlow.UI.Windows.Tests/DP.WorkFlow.UI.Windows.Tests.csproj'
)

Push-Location $root
try {
    [string[]]$restoreArguments = if ($NoRestore) { @('--no-restore') } else { @() }
    & dotnet build DP.WorkFlow.sln -c $Configuration --nologo @restoreArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    foreach ($project in $projects) {
        Write-Host "`n=== $project ===" -ForegroundColor Cyan
        & dotnet test $project -c $Configuration --no-build --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
finally {
    Pop-Location
}
