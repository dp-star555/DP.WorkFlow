param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$PackageDirectory = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $root '.artifacts/packages' }
$packageDirectoryPath = (Resolve-Path $PackageDirectory).Path
$work = Join-Path $root '.artifacts/package-consumers'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $work -ItemType Directory -Force | Out-Null

$source = @'
using System;
using System.Drawing;
using System.Windows.Forms;
using ModernUI.WinForms;
internal static class Program {
 [STAThread] private static int Main() {
  Application.EnableVisualStyles();
  Application.SetCompatibleTextRenderingDefault(false);
  using (var host = new Form { ClientSize = new Size(620, 300) })
  using (var combo = new ModernComboBox { Bounds = new Rectangle(20, 20, 240, 34), ReadOnly = true })
  using (var masked = new ModernMaskedInput { Bounds = new Rectangle(20, 64, 240, 34), Mask = "000-000", Text = "123456" })
  using (var rich = new ModernRichTextBox { Bounds = new Rectangle(280, 20, 300, 100), Text = "ModernUI 1.1" })
  using (var checkedList = new ModernCheckedListBox { Bounds = new Rectangle(20, 112, 240, 120) })
  using (var group = new ModernGroupBox { Bounds = new Rectangle(280, 134, 300, 98), Text = "Package" }) {
   combo.Items.AddRange(new object[] { "Camera A", "Camera B" });
   checkedList.Items.AddRange(new object[] { "Native binding", "RTL scroll" });
   checkedList.SetItemChecked(0, true);
   host.Controls.AddRange(new Control[] { combo, masked, rich, checkedList, group });
   host.CreateControl(); combo.CreateControl();
   using (var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height)) host.DrawToBitmap(bitmap, host.ClientRectangle);
   Console.WriteLine("PACKAGE_CONSUMER_OK framework=" + AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName +
    " version=" + typeof(ModernMaskedInput).Assembly.GetName().Version);
  }
  return 0;
 }
}
'@

foreach ($framework in @('net48', 'net8.0-windows')) {
    $directory = Join-Path $work $framework
    New-Item $directory -ItemType Directory -Force | Out-Null
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$framework</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="ModernUI.WinForms" Version="1.1.0" /></ItemGroup>
</Project>
"@
    Set-Content (Join-Path $directory 'Consumer.csproj') $project -Encoding UTF8
    Set-Content (Join-Path $directory 'Program.cs') $source -Encoding UTF8
    $config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="modern-ui-local" value="$packageDirectoryPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    Set-Content (Join-Path $directory 'NuGet.config') $config -Encoding UTF8
    & dotnet restore (Join-Path $directory 'Consumer.csproj') --configfile (Join-Path $directory 'NuGet.config')
    if ($LASTEXITCODE -ne 0) { throw "Package consumer restore failed for $framework." }
    & dotnet build (Join-Path $directory 'Consumer.csproj') -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Package consumer build failed for $framework." }
    $executable = Join-Path $directory "bin/$Configuration/$framework/Consumer.exe"
    $output = & $executable 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or -not ($output -match 'PACKAGE_CONSUMER_OK')) {
        throw "Package consumer execution failed for $framework."
    }
}
Write-Host 'UI_PACKAGE_CONSUMERS_OK frameworks=net48,net8.0-windows'
