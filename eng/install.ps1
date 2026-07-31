[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,
    [string]$InstallRoot = (Join-Path $env:LocalAppData 'Programs\GtaRpAssistant'),
    [switch]$NoShortcuts,
    [switch]$StartAfterInstall
)

$ErrorActionPreference = 'Stop'

function Get-SafeFullPath([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $driveRoot = [System.IO.Path]::GetPathRoot($full).TrimEnd('\')
    if ([string]::IsNullOrWhiteSpace($full) -or $full -eq $driveRoot -or [string]::IsNullOrWhiteSpace([System.IO.Path]::GetFileName($full))) {
        throw "Unsafe install path: $Path"
    }
    return $full
}

function Remove-SiblingTree([string]$Path, [string]$ExpectedParent) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolved = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')
    if ([System.IO.Path]::GetDirectoryName($resolved) -ne $ExpectedParent) { throw "Refusing to remove unexpected path: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$packagePath = (Resolve-Path -LiteralPath $Package).Path
$installPath = Get-SafeFullPath $InstallRoot
$parent = [System.IO.Path]::GetDirectoryName($installPath)
$backupPath = "$installPath.rollback"
$stagingPath = Join-Path $parent ("GtaRpAssistant.install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $parent -Force | Out-Null

$checksumPath = "$packagePath.sha256"
if (Test-Path -LiteralPath $checksumPath) {
    $expected = ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "Package SHA-256 does not match $checksumPath." }
}

try {
    Expand-Archive -LiteralPath $packagePath -DestinationPath $stagingPath
    if (-not (Test-Path -LiteralPath (Join-Path $stagingPath 'GtaRpAssistant.App.exe'))) {
        throw 'Package does not contain GtaRpAssistant.App.exe.'
    }

    Remove-SiblingTree $backupPath $parent
    if (Test-Path -LiteralPath $installPath) { Move-Item -LiteralPath $installPath -Destination $backupPath }
    Move-Item -LiteralPath $stagingPath -Destination $installPath

    $state = [ordered]@{
        installedAt = [DateTimeOffset]::UtcNow.ToString('O')
        packageSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        previousVersionAvailable = (Test-Path -LiteralPath $backupPath)
    }
    $state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installPath 'install-state.json') -Encoding UTF8

    if (-not $NoShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcutPaths = @(
            (Join-Path ([Environment]::GetFolderPath('Desktop')) 'GTA RP Assistant.lnk'),
            (Join-Path ([Environment]::GetFolderPath('Programs')) 'GTA RP Assistant.lnk')
        )
        foreach ($shortcutPath in $shortcutPaths) {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = Join-Path $installPath 'GtaRpAssistant.App.exe'
            $shortcut.WorkingDirectory = $installPath
            $shortcut.Save()
        }
    }

    if ($StartAfterInstall) { Start-Process -FilePath (Join-Path $installPath 'GtaRpAssistant.App.exe') -WindowStyle Hidden }
    Write-Host "Installed to $installPath"
    if (Test-Path -LiteralPath $backupPath) { Write-Host "Rollback version: $backupPath" }
}
catch {
    Remove-SiblingTree $stagingPath $parent
    if (-not (Test-Path -LiteralPath $installPath) -and (Test-Path -LiteralPath $backupPath)) {
        Move-Item -LiteralPath $backupPath -Destination $installPath
    }
    throw
}
