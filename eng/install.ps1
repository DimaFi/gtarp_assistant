[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,
    [string]$InstallRoot = (Join-Path $env:LocalAppData 'Programs\GtaRpAssistant'),
    [switch]$NoShortcuts,
    [switch]$NoRegistration,
    [switch]$StartWithWindows,
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

function Assert-EmbeddedSttPack([string]$ApplicationRoot) {
    $pack = Join-Path $ApplicationRoot 'model-packs\stt'
    $manifestPath = Join-Path $pack 'stt-pack.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Package does not contain the required offline Russian speech-recognition pack.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.architecture -ne 'win-x64' -or $manifest.language -ne 'ru') { throw 'Unsupported embedded speech-recognition pack.' }
    if ($manifest.runtime -notin @('sherpa-onnx', 'whisper.cpp')) { throw 'Unsupported embedded STT runtime.' }
    $root = [IO.Path]::GetFullPath($pack).TrimEnd('\') + '\'
    $declared = @{}
    foreach ($file in $manifest.files) {
        $relative = ([string]$file.path).Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or ($relative.Split('\') | Where-Object { $_ -in @('.', '..') })) { throw "Unsafe embedded STT path: $relative" }
        $target = [IO.Path]::GetFullPath((Join-Path $pack $relative))
        if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Embedded STT path traversal: $relative" }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing embedded STT file: $relative" }
        $info = Get-Item -LiteralPath $target
        if ([long]$file.sizeBytes -gt 0 -and $info.Length -ne [long]$file.sizeBytes) { throw "Embedded STT size mismatch: $relative" }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne ([string]$file.sha256).ToLowerInvariant()) { throw "Embedded STT checksum mismatch: $relative" }
        $declared[$relative.ToLowerInvariant()] = $true
    }
    foreach ($required in @($manifest.entryPoint, $manifest.modelFile, $manifest.tokenFile, $manifest.licenseFile)) {
        if ([string]::IsNullOrWhiteSpace([string]$required)) { continue }
        if (-not $declared.ContainsKey(([string]$required).Replace('/', '\').ToLowerInvariant())) { throw "Embedded STT required file is not integrity-protected: $required" }
    }
}

$packagePath = (Resolve-Path -LiteralPath $Package).Path
$installPath = Get-SafeFullPath $InstallRoot
$parent = [System.IO.Path]::GetDirectoryName($installPath)
$nativeArchitecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
if (-not [Environment]::Is64BitOperatingSystem -or $nativeArchitecture -notin @('AMD64', 'ARM64')) {
    throw "GTA RP Assistant requires 64-bit Windows; detected architecture: $nativeArchitecture"
}
$installDrive = New-Object System.IO.DriveInfo ([IO.Path]::GetPathRoot($installPath))
$minimumFreeBytes = [Math]::Max(1GB, (Get-Item -LiteralPath $packagePath).Length * 3)
if ($installDrive.AvailableFreeSpace -lt $minimumFreeBytes) {
    throw "Not enough free space on $($installDrive.Name): at least $([Math]::Ceiling($minimumFreeBytes / 1MB)) MiB is required."
}
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
    Assert-EmbeddedSttPack $stagingPath

    Remove-SiblingTree $backupPath $parent
    if (Test-Path -LiteralPath $installPath) { Move-Item -LiteralPath $installPath -Destination $backupPath }
    Move-Item -LiteralPath $stagingPath -Destination $installPath

    $state = [ordered]@{
        installedAt = [DateTimeOffset]::UtcNow.ToString('O')
        packageSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        previousVersionAvailable = (Test-Path -LiteralPath $backupPath)
    }
    $state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installPath 'install-state.json') -Encoding UTF8

    $bundledUninstaller = Join-Path $PSScriptRoot 'uninstall.ps1'
    if (Test-Path -LiteralPath $bundledUninstaller) {
        Copy-Item -LiteralPath $bundledUninstaller -Destination (Join-Path $installPath 'uninstall.ps1') -Force
    }

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
            $shortcut.IconLocation = (Join-Path $installPath 'GtaRpAssistant.App.exe') + ',0'
            $shortcut.Description = 'Локальный AI-компаньон для GTA 5 RP'
            $shortcut.Save()
        }
    }

    if (-not $NoRegistration) {
    $uninstallKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GtaRpAssistant'
    New-Item -Path $uninstallKeyPath -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name DisplayName -Value 'GTA RP Assistant' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name DisplayIcon -Value ((Join-Path $installPath 'GtaRpAssistant.App.exe') + ',0') -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name InstallLocation -Value $installPath -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name Publisher -Value 'LAB AI' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installPath 'uninstall.ps1') + '" -InstallRoot "' + $installPath + '"') -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name QuietUninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installPath 'uninstall.ps1') + '" -InstallRoot "' + $installPath + '"') -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKeyPath -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    }

    if ($StartWithWindows) {
        $runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        New-ItemProperty -Path $runKeyPath -Name GtaRpAssistant -Value ('"' + (Join-Path $installPath 'GtaRpAssistant.App.exe') + '"') -PropertyType String -Force | Out-Null
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
