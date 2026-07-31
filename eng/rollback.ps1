[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LocalAppData 'Programs\GtaRpAssistant')
)

$ErrorActionPreference = 'Stop'
$installPath = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$parent = [System.IO.Path]::GetDirectoryName($installPath)
$backupPath = "$installPath.rollback"
$failedPath = "$installPath.failed-$([Guid]::NewGuid().ToString('N'))"
if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $backupPath)) { throw 'No rollback version is available.' }

try {
    if (Test-Path -LiteralPath $installPath) { Move-Item -LiteralPath $installPath -Destination $failedPath }
    Move-Item -LiteralPath $backupPath -Destination $installPath
    if (Test-Path -LiteralPath $failedPath) {
        $resolved = (Resolve-Path -LiteralPath $failedPath).Path
        if ([System.IO.Path]::GetDirectoryName($resolved) -ne $parent) { throw "Refusing to remove unexpected path: $resolved" }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Write-Host "Rollback completed: $installPath"
}
catch {
    if (-not (Test-Path -LiteralPath $installPath) -and (Test-Path -LiteralPath $failedPath)) {
        Move-Item -LiteralPath $failedPath -Destination $installPath
    }
    throw
}
