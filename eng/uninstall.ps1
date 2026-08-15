[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LocalAppData 'Programs\GtaRpAssistant'),
    [switch]$KeepRollback,
    [switch]$NoShortcuts,
    [switch]$NoRegistration
)

$ErrorActionPreference = 'Stop'

if (-not $NoRegistration) {
    Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GtaRpAssistant' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name GtaRpAssistant -Force -ErrorAction SilentlyContinue
}
$installPath = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$parent = [System.IO.Path]::GetDirectoryName($installPath)
if ([string]::IsNullOrWhiteSpace($parent) -or $installPath -eq [System.IO.Path]::GetPathRoot($installPath).TrimEnd('\')) { throw "Unsafe install path: $InstallRoot" }

$targets = @($installPath)
if (-not $KeepRollback) { $targets += "$installPath.rollback" }
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $resolved = (Resolve-Path -LiteralPath $target).Path.TrimEnd('\')
    if ([System.IO.Path]::GetDirectoryName($resolved) -ne $parent) { throw "Refusing to remove unexpected path: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

if (-not $NoShortcuts) {
    @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'GTA RP Assistant.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'GTA RP Assistant.lnk')
    ) | ForEach-Object { if (Test-Path -LiteralPath $_) { Remove-Item -LiteralPath $_ -Force } }
}
Write-Host "Uninstalled from $installPath"
