# Publishes HomeWindow and builds the Windows installer with Inno Setup.
#   powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 [-Version 0.2.0] [-SelfContained]
# Without -SelfContained the installer is small and the PC needs the .NET 10 Desktop Runtime
# (setup checks for it). -SelfContained puts .NET in the installer, so it runs on any PC; that
# needs NuGet access to the runtime packages, which the GitHub workflow has.
# The GitHub workflow signs the app between the two steps: -PublishOnly publishes it to publish\,
# -InstallerOnly builds the installer from what is in publish\ at that moment.
param(
  [string]$Version,
  [switch]$SelfContained,
  [string]$Iscc,
  [switch]$PublishOnly,
  [switch]$InstallerOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'HomeWindow\HomeWindow.csproj'

if (-not $Version) {
  $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Iscc -and -not $PublishOnly) {
  $Iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
  ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $Iscc -and -not $PublishOnly) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

$publish = Join-Path $root 'publish'
if (-not $InstallerOnly) {
  if (Test-Path $publish) { Get-ChildItem $publish -Recurse | Remove-Item -Recurse -Force }

  $publishArgs = @('publish', $project, '-c', 'Release', '-o', $publish, "-p:Version=$Version", '-nologo')
  if ($SelfContained) { $publishArgs += @('-r', 'win-x64', '--self-contained', 'true') }
  Write-Host "Publishing HomeWindow $Version$(if ($SelfContained) { ' (self-contained)' })"
  & dotnet @publishArgs
  if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
  if ($PublishOnly) { return }
}

Write-Host 'Building the installer'
& $Iscc "/DAppVersion=$Version" "/DSourceDir=$publish" "/DSelfContained=$(if ($SelfContained) { 1 } else { 0 })" /Q (Join-Path $root 'installer\HomeWindow.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

$setup = Join-Path $root "installer\Output\HomeWindow-Setup-$Version.exe"
Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
