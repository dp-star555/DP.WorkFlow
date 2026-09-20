param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipWindowsTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Invoke-ReleaseStep([string]$Name, [scriptblock]$Action, [int]$Attempts = 1) {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        & $Action
        if ($LASTEXITCODE -eq 0) { return }
        if ($attempt -eq $Attempts) { throw "$Name failed after $Attempts attempt(s)." }
        Write-Warning "$Name failed on attempt $attempt; retrying in a fresh process."
        Start-Sleep -Milliseconds 500
    }
}

Push-Location $root
try {
    & (Join-Path $root 'tools/verify-solution-projects.ps1') -FailOnMissing
    dotnet build 'src/Platform/Desktop/ModernUI.WinForms.Gallery/ModernUI.WinForms.Gallery.csproj' -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Dual-target Gallery build failed.' }
    & (Join-Path $root 'tools/compare-ui-public-surface.ps1') -Configuration $Configuration

    if (-not $SkipWindowsTests) {
        # WinForms behavior tests intentionally use a stable 96-DPI process baseline. Real PMv2
        # transitions are covered by compare/measure scripts that assert GetDpiForWindow.
        # Public-surface comparison loads WinForms assemblies into this PowerShell process, after
        # which Windows no longer permits changing its process DPI context. Run behavior tests in
        # a fresh process so their 96-DPI baseline is established before any WinForms load.
        $previousCompatibilityLayer = $env:__COMPAT_LAYER
        try {
            # A child process inherits the parent's DPI awareness once PowerShell has loaded the
            # reflected WinForms assembly. Force the child executable's context before startup;
            # SetProcessDpiAwarenessContext inside the child would already be too late.
            $env:__COMPAT_LAYER = 'DPIUNAWARE'
            Invoke-ReleaseStep 'Windows UI behavior tests' {
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-tests-96dpi.ps1') -Configuration Debug
            } -Attempts 2
        }
        finally { $env:__COMPAT_LAYER = $previousCompatibilityLayer }
    }

    dotnet run --project 'tests/Platform/ModernUI.WinForms.Net48.Smoke/ModernUI.WinForms.Net48.Smoke.csproj' -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw '.NET Framework smoke failed.' }
    dotnet run --project 'tests/Platform/ModernUI.WinForms.DesignerSmoke/ModernUI.WinForms.DesignerSmoke.csproj' -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw '.NET Framework Designer host smoke failed.' }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-native-stress.ps1') `
        -Configuration $Configuration -Iterations 120
    if ($LASTEXITCODE -ne 0) { throw 'Dual-framework native HWND stress failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-data-stress.ps1') `
        -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Dual-framework data control stress failed.' }
    Invoke-ReleaseStep 'Dual-framework 96-DPI popup continuous-frame tests' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-continuous-frame-tests.ps1') `
            -Configuration $Configuration -TargetFramework All -Probe All -TargetDpi 96 -FrameCount 18 -NoBuild
    } -Attempts 2
    Invoke-ReleaseStep 'Dual-framework 144-DPI popup continuous-frame tests' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-continuous-frame-tests.ps1') `
            -Configuration $Configuration -TargetFramework All -Probe All -TargetDpi 144 -FrameCount 18 -NoBuild -SkipIfDpiUnavailable
    } -Attempts 2
    Invoke-ReleaseStep 'Dual-framework RTL 96-DPI Select continuous-frame tests' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-continuous-frame-tests.ps1') `
            -Configuration $Configuration -TargetFramework All -Probe Select -TargetDpi 96 -FrameCount 18 -RightToLeft -NoBuild
    } -Attempts 2
    Invoke-ReleaseStep 'Dual-framework RTL 144-DPI Select continuous-frame tests' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-continuous-frame-tests.ps1') `
            -Configuration $Configuration -TargetFramework All -Probe Select -TargetDpi 144 -FrameCount 18 -RightToLeft -NoBuild -SkipIfDpiUnavailable
    } -Attempts 2
    Invoke-ReleaseStep 'Dual-framework 96-DPI interaction-frame tests' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-interaction-frame-tests.ps1') `
            -Configuration $Configuration -TargetFramework All -Probe All -TargetDpi 96 `
            -ThemeDirection Both -Motion Both -NoBuild
    } -Attempts 2
    Invoke-ReleaseStep 'Dual-framework 144-DPI interaction-frame tests' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-interaction-frame-tests.ps1') `
            -Configuration $Configuration -TargetFramework All -Probe All -TargetDpi 144 `
            -ThemeDirection Both -Motion Both -NoBuild -SkipIfDpiUnavailable
    } -Attempts 2
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-settings-matrix.ps1') `
        -Configuration $Configuration -TargetFramework All -Theme All -Direction Both -Motion Both `
        -TargetDpi 96 -NoBuild
    if ($LASTEXITCODE -ne 0) { throw 'Dual-framework 96-DPI settings matrix failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/run-ui-settings-matrix.ps1') `
        -Configuration $Configuration -TargetFramework All -Theme All -Direction Both -Motion Both `
        -TargetDpi 144 -NoBuild -SkipIfDpiUnavailable
    if ($LASTEXITCODE -ne 0) { throw 'Dual-framework 144-DPI settings matrix failed.' }

    # A previous build launched above the repository can use another installed SDK and leave an
    # otherwise up-to-date assets file whose net8.0-windows group lacks the normalized Windows
    # platform version. Force restore under this repository's global.json before packing; ordinary
    # implicit restore may reuse the incompatible assets file and fail intermittently with NU1012.
    dotnet restore 'src/Platform/Desktop/ModernUI.WinForms/ModernUI.WinForms.csproj' --force
    if ($LASTEXITCODE -ne 0) { throw 'Package assets restore failed.' }
    dotnet pack 'src/Platform/Localization/ModernUI.Localization/ModernUI.Localization.csproj' -c $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Localization package creation failed.' }
    dotnet pack 'src/Platform/Desktop/ModernUI.WinForms/ModernUI.WinForms.csproj' -c $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'WinForms package creation failed.' }

    $package = Get-ChildItem '.artifacts/packages/ModernUI.WinForms.*.nupkg' |
        Where-Object { $_.Name -notlike '*.symbols.nupkg' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $package) { throw 'ModernUI.WinForms package was not created.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        foreach ($required in @('lib/net48/ModernUI.WinForms.dll', 'lib/net8.0-windows7.0/ModernUI.WinForms.dll')) {
            if ($entries -notcontains $required) { throw "Package is missing $required." }
        }
    }
    finally { $archive.Dispose() }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/verify-ui-package-consumers.ps1') `
        -Configuration $Configuration -PackageDirectory (Join-Path $root '.artifacts/packages')
    if ($LASTEXITCODE -ne 0) { throw 'Fresh NuGet package consumer smoke failed.' }

    Write-Host "UI_RELEASE_OK configuration=$Configuration package=$($package.FullName)"
}
finally { Pop-Location }
