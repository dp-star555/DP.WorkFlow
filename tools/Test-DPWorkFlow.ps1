param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoRestore,
    # Auto       = 控件库源码有改动才跑控件库用例（默认；检查工作区未提交改动 + 最近一次提交）
    # Core       = 永不跑控件库用例
    # All        = 总是跑全部
    # UiControls = 只跑控件库用例（改控件库时的快速回路）
    [ValidateSet('Auto', 'Core', 'All', 'UiControls')]
    [string]$Suite = 'Auto',
    # Auto 模式下额外比较的基准提交；留空时只比较工作区与 HEAD 本身。
    [string]$SinceRef = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 每次都跑的项目：不依赖任何控件库。
$projects = @(
    'tests/Workflow/DP.WorkFlow.Nodes.Vision.Tests/DP.WorkFlow.Nodes.Vision.Tests.csproj',
    # 跨层端到端：真实采集运行时 + 工作流根运行宿主（V1-C）。
    'tests/Workflow/DP.WorkFlow.Nodes.Vision.Acquisition.Tests/DP.WorkFlow.Nodes.Vision.Acquisition.Tests.csproj',
    'tests/Platform/ScriptEngine.Tests/ScriptEngine.Tests.csproj',
    'tests/Platform/ScriptEngine.Workspaces.Tests/ScriptEngine.Workspaces.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Core.Tests/DP.WorkFlow.Core.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Composite.Tests/DP.WorkFlow.Nodes.Composite.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Motion.Tests/DP.WorkFlow.Nodes.Motion.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Process.Tests/DP.WorkFlow.Nodes.Process.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Nodes.Standard.Tests/DP.WorkFlow.Nodes.Standard.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Persistence.Tests/DP.WorkFlow.Persistence.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.Runtime.Tests/DP.WorkFlow.Runtime.Tests.csproj',
    'tests/Workflow/DP.WorkFlow.UI.Shared.Tests/DP.WorkFlow.UI.Shared.Tests.csproj'
)

# 整个项目都是控件库用例：只在控件库源码改动时跑。判定依据是它引用了 ScriptEngine.WinForms / ScriptEngine.Wpf。
$controlLibraryProjects = @(
    'tests/Platform/ScriptEngine.Windows.Tests/ScriptEngine.Windows.Tests.csproj'
)

# 混合项目：既含控件库用例也含业务流程用例，因此靠 xUnit 分类过滤而不是整项目开关。
# Keep the WinForms suite in its own testhost so its module initializer can establish 96-DPI behavior tests.
$windowsProject = 'tests/Workflow/DP.WorkFlow.UI.Windows.Tests/DP.WorkFlow.UI.Windows.Tests.csproj'

# 现代控件库源码目录。命中其中之一就认为"控件库改过了"。
$controlLibraryPaths = @(
    'src/Platform/Desktop/ModernUI.WinForms/',
    'src/Platform/Localization/ModernUI.Localization/',
    'src/Platform/Scripting/ScriptEngine.WinForms/',
    'src/Platform/Scripting/ScriptEngine.Wpf/'
)

# 控件库用例的分类名，与 TestCategories.UiControls 保持一致。
$uiControlsCategory = 'UiControls'

function Get-ChangedPaths {
    # 同时看工作区（含未跟踪文件）与最近一次提交，覆盖"改完还没提交"和"刚提交完"两种常见时机。
    $paths = New-Object System.Collections.Generic.List[string]
    $status = & git status --porcelain 2>$null
    if ($LASTEXITCODE -eq 0 -and $status) {
        foreach ($line in $status) {
            if ($line.Length -gt 3) {
                # 重命名条目形如 "old -> new"，两侧都算改动。
                foreach ($candidate in ($line.Substring(3).Trim() -split '\s*->\s*')) {
                    if ($candidate) { $paths.Add($candidate) }
                }
            }
        }
    }
    $gitArguments = if ($SinceRef) { @('diff', '--name-only', $SinceRef) } else { @('diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD') }
    $committed = & git $gitArguments 2>$null
    if ($LASTEXITCODE -eq 0 -and $committed) {
        foreach ($line in $committed) { if ($line) { $paths.Add($line) } }
    }
    return $paths
}

function Get-ControlLibraryTrigger {
    param($ChangedPaths)
    foreach ($path in $ChangedPaths) {
        $normalized = $path -replace '\\', '/'
        foreach ($prefix in $controlLibraryPaths) {
            if ($normalized.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $prefix }
        }
    }
    return $null
}

function Write-SuiteNote {
    # Write-Host 在 PowerShell 5.1 里只写宿主、不进重定向流，因此判定依据不会落进日志。
    # 这里同时写输出流，保证 `& script.ps1 *> log` 或 `| Out-File` 能留下可审计的判定记录。
    param([string]$Message, [string]$Color = 'Gray')
    Write-Host $Message -ForegroundColor $Color
    Write-Output $Message
}

function Invoke-TestProject {
    param([string]$Project, [string]$Filter)
    $suffix = if ($Filter) { "  [$Filter]" } else { '' }
    Write-SuiteNote -Message "`n=== $Project$suffix ===" -Color Cyan
    [string[]]$testArguments = @($Project, '-c', $Configuration, '--no-build', '--nologo')
    if ($Filter) { $testArguments += @('--filter', $Filter) }
    & dotnet test @testArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Push-Location $root
try {
    [string[]]$restoreArguments = if ($NoRestore) { @('--no-restore') } else { @() }
    & dotnet build DP.WorkFlow.sln -c $Configuration --nologo @restoreArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $runUiControls = $false
    switch ($Suite) {
        'All' { $runUiControls = $true }
        'Core' { $runUiControls = $false }
        'UiControls' { $runUiControls = $true }
        'Auto' {
            $trigger = Get-ControlLibraryTrigger -ChangedPaths (Get-ChangedPaths)
            if ($trigger) {
                $runUiControls = $true
                Write-SuiteNote -Message "`n[suite] 检测到控件库源码改动（$trigger）→ 一并运行控件库用例。" -Color Yellow
            }
            else {
                Write-SuiteNote -Message "`n[suite] 控件库源码未改动（已检查工作区与 HEAD）→ 跳过控件库用例；用 -Suite All 强制全跑。" -Color DarkGray
            }
        }
    }

    if ($Suite -eq 'UiControls') {
        # 快速回路：只跑控件库用例。
        foreach ($project in $controlLibraryProjects) { Invoke-TestProject -Project $project -Filter $null }
        Invoke-TestProject -Project $windowsProject -Filter "Category=$uiControlsCategory"
        return
    }

    foreach ($project in $projects) { Invoke-TestProject -Project $project -Filter $null }

    if ($runUiControls) {
        foreach ($project in $controlLibraryProjects) { Invoke-TestProject -Project $project -Filter $null }
    }
    $windowsFilter = if ($runUiControls) { $null } else { "Category!=$uiControlsCategory" }
    Invoke-TestProject -Project $windowsProject -Filter $windowsFilter
}
finally {
    Pop-Location
}
