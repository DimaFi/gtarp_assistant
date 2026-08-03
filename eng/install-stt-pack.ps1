[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,
    [string]$Destination,
    [string]$ExpectedSha256
)

$ErrorActionPreference = 'Stop'
$Package = [System.IO.Path]::GetFullPath($Package)
if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) { throw "STT package not found: $Package" }
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'GtaRpAssistant\model-packs\stt'
}
$Destination = [System.IO.Path]::GetFullPath($Destination)
$destinationRoot = [System.IO.Path]::GetPathRoot($Destination)
if ($Destination.TrimEnd('\') -eq $destinationRoot.TrimEnd('\') -or $Destination.Length -lt $destinationRoot.Length + 8) {
    throw "Unsafe STT installation destination: $Destination"
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
    $actualPackageHash = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualPackageHash -ne $ExpectedSha256.Trim().ToLowerInvariant()) {
        throw "STT package SHA-256 mismatch. Expected $ExpectedSha256, got $actualPackageHash."
    }
}

$parent = Split-Path -Parent $Destination
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$staging = Join-Path $parent ".stt-staging-$([Guid]::NewGuid().ToString('N'))"
$backup = Join-Path $parent ".stt-backup-$([Guid]::NewGuid().ToString('N'))"
$installed = $false
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    $stagingRoot = [System.IO.Path]::GetFullPath($staging).TrimEnd('\') + '\'
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Package)
    try {
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('/', '\')
            if ([string]::IsNullOrWhiteSpace($relative)) { continue }
            if ([System.IO.Path]::IsPathRooted($relative)) { throw "Rooted path in STT archive: $relative" }
            if ($relative.Split('\') | Where-Object { $_ -eq '..' -or $_ -eq '.' }) { throw "Unsafe path in STT archive: $relative" }
            $target = [System.IO.Path]::GetFullPath((Join-Path $staging $relative))
            if (-not $target.StartsWith($stagingRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Traversal path in STT archive: $relative" }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                continue
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            $input = $entry.Open()
            $output = [System.IO.File]::Open($target, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $input.CopyTo($output) }
            finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $manifestPath = Join-Path $staging 'stt-pack.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'STT archive does not contain stt-pack.json at its root.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.runtime -ne 'whisper.cpp' -or $manifest.architecture -ne 'win-x64') {
        throw 'Unsupported STT pack manifest.'
    }
    $declared = @{}
    foreach ($file in $manifest.files) {
        $relative = ([string]$file.path).Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($relative) -or [System.IO.Path]::IsPathRooted($relative)) { throw "Unsafe file path in STT manifest: $relative" }
        if ($relative.Split('\') | Where-Object { $_ -eq '..' -or $_ -eq '.' }) { throw "Unsafe file path in STT manifest: $relative" }
        $target = [System.IO.Path]::GetFullPath((Join-Path $staging $relative))
        if (-not $target.StartsWith($stagingRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Traversal path in STT manifest: $relative" }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing STT pack file: $relative" }
        $info = Get-Item -LiteralPath $target
        if ([long]$file.sizeBytes -gt 0 -and $info.Length -ne [long]$file.sizeBytes) { throw "Size mismatch for STT pack file: $relative" }
        $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne ([string]$file.sha256).ToLowerInvariant()) { throw "SHA-256 mismatch for STT pack file: $relative" }
        $declared[$relative.ToLowerInvariant()] = $true
    }
    foreach ($required in @($manifest.entryPoint, $manifest.modelFile, $manifest.licenseFile)) {
        $key = ([string]$required).Replace('/', '\').ToLowerInvariant()
        if (-not $declared.ContainsKey($key)) { throw "Required STT file is not integrity-protected: $required" }
    }

    if (Test-Path -LiteralPath $Destination) { Move-Item -LiteralPath $Destination -Destination $backup }
    try {
        Move-Item -LiteralPath $staging -Destination $Destination
        $installed = $true
    }
    catch {
        if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $Destination }
        throw
    }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    Write-Host "STT pack installed: $Destination"
    Write-Host 'Select this folder on the Audio page, or leave the path empty when using the default destination.'
}
finally {
    if (-not $installed -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
