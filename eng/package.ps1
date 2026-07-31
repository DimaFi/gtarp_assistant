[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '0.2.0',
    [switch]$SelfContained,
    [switch]$FrameworkDependent,
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publishDirectory = Join-Path $artifacts "publish\$Runtime"
$releaseDirectory = Join-Path $artifacts 'release'
$applicationProject = Join-Path $root 'src\GtaRpAssistant.App\GtaRpAssistant.App.csproj'
$microModelHostProject = Join-Path $root 'src\GtaRpAssistant.MicroModelHost\GtaRpAssistant.MicroModelHost.csproj'
$microModelHostPublishDirectory = Join-Path $publishDirectory 'micro-model-host'
$archiveName = "GtaRpAssistant-$Version-$Runtime"
$archivePath = Join-Path $releaseDirectory "$archiveName.zip"
$manifestPath = Join-Path $releaseDirectory "$archiveName.manifest.json"
$checksumPath = Join-Path $releaseDirectory "$archiveName.zip.sha256"
$isSelfContained = $SelfContained.IsPresent -or -not $FrameworkDependent.IsPresent

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $publishDirectory -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

$publishArguments = @(
    'publish', $applicationProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $isSelfContained.ToString().ToLowerInvariant(),
    '--no-restore',
    ("-p:Version=$Version"),
    ("-p:PublishDir=$publishDirectory\")
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$hostPublishArguments = @(
    'publish', $microModelHostProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $isSelfContained.ToString().ToLowerInvariant(),
    '--no-restore',
    ("-p:Version=$Version"),
    ("-p:PublishDir=$microModelHostPublishDirectory\")
)
& dotnet @hostPublishArguments
if ($LASTEXITCODE -ne 0) { throw "MicroModelHost publish failed with exit code $LASTEXITCODE." }

if ($isSelfContained) {
    foreach ($requiredRuntimeFile in @('hostfxr.dll', 'coreclr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredRuntimeFile))) {
            throw "Self-contained publish is missing required runtime file: $requiredRuntimeFile"
        }
    }
}

$executable = Join-Path $publishDirectory 'GtaRpAssistant.App.exe'
if (-not $SkipSmoke) {
    & (Join-Path $PSScriptRoot 'smoke.ps1') -Executable $executable
    & (Join-Path $PSScriptRoot 'capture-ui.ps1') -Executable $executable
}

$files = Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($publishDirectory.Length).TrimStart('\').Replace('\', '/')
        size = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$installerScripts = @('install.ps1', 'rollback.ps1', 'uninstall.ps1') | ForEach-Object {
    $source = Join-Path $PSScriptRoot $_
    $destination = Join-Path $releaseDirectory $_
    Copy-Item -LiteralPath $source -Destination $destination -Force
    [ordered]@{
        path = $_
        sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    schemaVersion = 1
    product = 'GTA RP Assistant'
    version = $Version
    runtime = $Runtime
    selfContained = $isSelfContained
    entryPoint = 'GtaRpAssistant.App.exe'
    installerScripts = @($installerScripts)
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Sort-Object FullName)) {
        $relativePath = $file.FullName.Substring($publishDirectory.Length).TrimStart('\').Replace('\', '/')
        $entry = $archive.CreateEntry($relativePath, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $inputStream = $file.OpenRead()
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
}
finally {
    $archive.Dispose()
}
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $archiveName.zip" | Set-Content -LiteralPath $checksumPath -Encoding ASCII
if (-not $SkipSmoke) {
    & (Join-Path $PSScriptRoot 'install-smoke.ps1') -Package $archivePath
}
Write-Host "Package: $archivePath"
Write-Host "Manifest: $manifestPath"
Write-Host "Checksum: $checksumPath"
Write-Host "Archive SHA-256: $archiveHash"
