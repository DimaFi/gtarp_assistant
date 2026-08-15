[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,
    [string]$InstallRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packagePath = (Resolve-Path -LiteralPath $Package).Path
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $root 'artifacts\install smoke\Custom GTA RP Assistant'
}
$installPath = [System.IO.Path]::GetFullPath($InstallRoot)
$artifactsPath = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts')).TrimEnd('\') + '\'
if (-not $installPath.StartsWith($artifactsPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Install smoke path must stay inside the artifacts directory: $installPath"
}

try {
    & (Join-Path $PSScriptRoot 'install.ps1') -Package $packagePath -InstallRoot $installPath -NoShortcuts -NoRegistration
    $executable = Join-Path $installPath 'GtaRpAssistant.App.exe'
    if (-not (Test-Path -LiteralPath (Join-Path $installPath 'install-state.json'))) {
        throw 'Install smoke did not create install-state.json.'
    }
    & (Join-Path $PSScriptRoot 'smoke.ps1') -Executable $executable
    Write-Host "Custom-path install smoke passed: $installPath"
}
finally {
    & (Join-Path $PSScriptRoot 'uninstall.ps1') -InstallRoot $installPath -NoShortcuts -NoRegistration
}
