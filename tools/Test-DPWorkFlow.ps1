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
    [string]$SinceRef = '',
    # 失败归因（AR-28）：某个工程失败后，把失败用例逐个单独重跑若干次，判定它是"可复现"
    # 还是"瞬态"。0 = 关闭。默认 2 —— 代价只在真的失败时才付。
    [ValidateRange(0, 10)]
    [int]$FlakeTriage = 2,
    # 瞬态容忍：仅当归因**证明所有失败都不可复现**时，退出码才允许为 0。
    # 默认关闭——门禁的红不会因为"看起来像抖动"而自动转绿；要转绿必须显式要求，且日志里留证据。
    [switch]$TolerateTransientFlakes,
    # 重复探测：把混合的 Windows 工程连跑 N 轮并报告每轮失败数，用来把"抖动"量化成比率。
    [ValidateRange(1, 20)]
    [int]$RepeatWindowsSuite = 1
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
# 这个工程也是 AR-28 记录的"结果不确定"的套件，所以失败归因与重复探测主要针对它。
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

# 从 vstest 输出里取出失败用例的全名。中文输出的失败行形如：
#   "  失败 DP.WorkFlow.Tests.Xxx.Yyy [3 s]"
# 注意不要命中汇总行 "失败!  - 失败:     1，…"（失败 后面是 '!' 而不是空白）。
function Get-FailedTestNames {
    param([string[]]$Lines)
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines) {
        if ($line -match '^\s*(?:失败|Failed)\s+(\S+)') { $names.Add($Matches[1]) }
    }
    return $names
}

# 失败归因：把每个失败用例单独重跑 $Attempts 次。
# 返回对象数组，每项含 Name / Passed / Attempts / Reproducible。
function Invoke-FlakeTriage {
    param([string]$Project, [string[]]$FailedNames, [int]$Attempts)
    $results = New-Object System.Collections.Generic.List[object]
    if ($Attempts -le 0 -or $FailedNames.Count -eq 0) { return $results }
    foreach ($name in $FailedNames) {
        $passed = 0
        for ($i = 0; $i -lt $Attempts; $i++) {
            # Windows PowerShell 5.1 会把原生进程 stderr 包装成 ErrorRecord；在脚本级
            # Stop 策略下，vstest 的正常失败诊断会提前终止本脚本。这里只把原生输出
            # 当作待分析数据，真正的通过/失败始终以进程退出码为准。
            $previousErrorActionPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $triageOutput = & dotnet test $Project -c $Configuration --no-build --nologo --filter "FullyQualifiedName=$name" 2>&1
                $triageExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
            if ($triageExitCode -eq 0) { $passed++ }
        }
        $results.Add([pscustomobject]@{
            Name         = $name
            Passed       = $passed
            Attempts     = $Attempts
            Reproducible = ($passed -eq 0)
        })
    }
    return $results
}

# 运行一个测试工程，并把结论写进 $script:LastVerdict。
# 刻意**不用 return**：本函数同时把测试输出转发到输出流（保证日志完整、且实时可见），
# 若再 return 一个对象，调用方拿到的是"输出行 + 结论"的混合数组。
# 失败时（若开启归因）先做归因再返回——因为"失败"与"改动引入回归"并不是同一件事（AR-28）。
function Invoke-TestProject {
    param([string]$Project, [string]$Filter)
    $suffix = if ($Filter) { "  [$Filter]" } else { '' }
    Write-SuiteNote -Message "`n=== $Project$suffix ===" -Color Cyan
    [string[]]$testArguments = @($Project, '-c', $Configuration, '--no-build', '--nologo')
    if ($Filter) { $testArguments += @('--filter', $Filter) }
    $buffer = New-Object System.Collections.Generic.List[string]
    # Windows PowerShell 5.1 会把原生进程 stderr 包装成 ErrorRecord；测试失败本应进入
    # 下方归因流程，而不是被脚本级 Stop 策略截断。捕获期间临时放宽策略，并继续以
    # dotnet 的退出码作为唯一判定依据。
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & dotnet test @testArguments 2>&1 | ForEach-Object { $buffer.Add([string]$_); $_ }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $triage = @()
    $transient = $false
    if ($exitCode -ne 0 -and $FlakeTriage -gt 0) {
        $failed = Get-FailedTestNames -Lines $buffer.ToArray()
        if ($failed.Count -gt 0) {
            Write-SuiteNote -Message "`n[flake-triage] 该工程有 $($failed.Count) 个失败用例，逐个重跑 $FlakeTriage 次以判定是否可复现…" -Color Yellow
            $triage = @(Invoke-FlakeTriage -Project $Project -FailedNames $failed -Attempts $FlakeTriage)
            foreach ($item in $triage) {
                if ($item.Reproducible) {
                    Write-SuiteNote -Message "  [flake-triage] $($item.Name): 0/$($item.Attempts) 通过 → **可复现**，按真实回归处理。" -Color Red
                }
                else {
                    Write-SuiteNote -Message "  [flake-triage] $($item.Name): $($item.Passed)/$($item.Attempts) 通过 → 不可复现（疑似瞬态，仍计入失败）。" -Color DarkYellow
                }
            }
            $transient = (@($triage | Where-Object { $_.Reproducible }).Count -eq 0)
        }
        else {
            Write-SuiteNote -Message "  [flake-triage] 未能从输出里解析出失败用例名，跳过归因。" -Color DarkYellow
        }
    }
    $script:LastVerdict = [pscustomobject]@{
        Project   = $Project
        Filter    = $Filter
        ExitCode  = $exitCode
        Triage    = $triage
        Transient = $transient
    }
}

Push-Location $root
$verdicts = New-Object System.Collections.Generic.List[object]
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
        foreach ($project in $controlLibraryProjects) {
            Invoke-TestProject -Project $project -Filter $null
            $verdicts.Add($script:LastVerdict)
        }
        Invoke-TestProject -Project $windowsProject -Filter "Category=$uiControlsCategory"
        $verdicts.Add($script:LastVerdict)
    }
    else {
        foreach ($project in $projects) {
            Invoke-TestProject -Project $project -Filter $null
            $verdicts.Add($script:LastVerdict)
        }

        if ($runUiControls) {
            foreach ($project in $controlLibraryProjects) {
                Invoke-TestProject -Project $project -Filter $null
                $verdicts.Add($script:LastVerdict)
            }
        }
        $windowsFilter = if ($runUiControls) { $null } else { "Category!=$uiControlsCategory" }

        if ($RepeatWindowsSuite -gt 1) {
            # 重复探测：不改变判定，只把"抖动"变成可比较的比率。
            Write-SuiteNote -Message "`n[repeat-probe] 将 $windowsProject 连跑 $RepeatWindowsSuite 轮，用于量化抖动率。" -Color Yellow
            for ($round = 1; $round -le $RepeatWindowsSuite; $round++) {
                Invoke-TestProject -Project $windowsProject -Filter $windowsFilter
                $roundVerdict = $script:LastVerdict
                $roundState = if ($roundVerdict.ExitCode -eq 0) { '通过' } else { '失败' }
                Write-SuiteNote -Message "  [repeat-probe] 第 $round/$RepeatWindowsSuite 轮：$roundState" -Color Gray
                $verdicts.Add($roundVerdict)
            }
        }
        else {
            Invoke-TestProject -Project $windowsProject -Filter $windowsFilter
            $verdicts.Add($script:LastVerdict)
        }
    }

    $failedVerdicts = @($verdicts | Where-Object { $_.ExitCode -ne 0 })
    if ($failedVerdicts.Count -eq 0) {
        Write-SuiteNote -Message "`n[verdict] 全部工程通过（$($verdicts.Count) 次运行）。" -Color Green
    }
    else {
        $reproducible = @($failedVerdicts | Where-Object { -not $_.Transient })
        $transientOnly = ($reproducible.Count -eq 0)
        Write-SuiteNote -Message "`n[verdict] $($failedVerdicts.Count)/$($verdicts.Count) 次运行失败；其中可复现的 $($reproducible.Count) 个。" -Color Red
        if ($transientOnly -and $TolerateTransientFlakes) {
            Write-SuiteNote -Message "[verdict] 所有失败都被归因证明为不可复现的瞬态，且已显式要求容忍 → 退出码 0。注意：这一轮的红并不代表代码正确，只是不代表回归。" -Color Yellow
        }
        else {
            if ($transientOnly) {
                Write-SuiteNote -Message "[verdict] 失败全部疑似瞬态，但未指定 -TolerateTransientFlakes → 仍按失败退出（默认不把抖动当通过）。" -Color Red
            }
            exit 1
        }
    }
}
finally {
    Pop-Location
}
