[CmdletBinding()]
param(
    [string]$Destination,
    [string]$DownloadDirectory,
    [switch]$SkipArchive,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $root 'artifacts\stt\pack-base-q8_0' }
if ([string]::IsNullOrWhiteSpace($DownloadDirectory)) { $DownloadDirectory = Join-Path $root 'artifacts\stt\downloads' }
$Destination = [System.IO.Path]::GetFullPath($Destination)
$DownloadDirectory = [System.IO.Path]::GetFullPath($DownloadDirectory)
$destinationRoot = [System.IO.Path]::GetPathRoot($Destination)
if ($Destination.TrimEnd('\') -eq $destinationRoot.TrimEnd('\') -or $Destination.Length -lt $destinationRoot.Length + 8) {
    throw "Unsafe STT pack destination: $Destination"
}
$runtimeVersion = '1.9.1'
$runtimeUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v$runtimeVersion/whisper-bin-x64.zip"
$runtimeSha256 = '7d8be46ecd31828e1eb7a2ecdd0d6b314feafd82163038ab6092594b0a063539'
$modelRevision = '5359861c739e955e79d9a303bcbc70fb988958b1'
$modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/$modelRevision/ggml-base-q8_0.bin?download=true"
$modelSha256 = 'c577b9a86e7e048a0b7eada054f4dd79a56bbfa911fbdacf900ac5b567cbb7d9'
$licenseUrl = "https://raw.githubusercontent.com/ggml-org/whisper.cpp/v$runtimeVersion/LICENSE"
$licenseSha256 = '94f29bbed6a22c35b992c5c6ebf0e7c92f13b836b90f36f461c9cf2f0f1d010d'

function Get-VerifiedDownload([string]$Uri, [string]$Path, [string]$ExpectedSha256) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $Path)) {
        $partial = "$Path.partial"
        Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $partial -UseBasicParsing
            Move-Item -LiteralPath $partial -Destination $Path -Force
        }
        finally { Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue }
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) { throw "SHA-256 mismatch for $Path. Expected $ExpectedSha256, got $actual." }
}

$runtimeArchive = Join-Path $DownloadDirectory "whisper-bin-x64-v$runtimeVersion.zip"
$modelDownload = Join-Path $DownloadDirectory 'ggml-base-q8_0.bin'
$licenseDownload = Join-Path $DownloadDirectory 'LICENSE-whisper.cpp.txt'
Get-VerifiedDownload $runtimeUrl $runtimeArchive $runtimeSha256
Get-VerifiedDownload $modelUrl $modelDownload $modelSha256
Get-VerifiedDownload $licenseUrl $licenseDownload $licenseSha256

$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "GtaRpAssistant-stt-$([Guid]::NewGuid().ToString('N'))"
try {
    Expand-Archive -LiteralPath $runtimeArchive -DestinationPath $temporary
    $release = Join-Path $temporary 'Release'
    if (-not (Test-Path -LiteralPath (Join-Path $release 'whisper-server.exe'))) { throw 'Official runtime archive does not contain whisper-server.exe.' }
    if (Test-Path -LiteralPath $Destination) {
        if (-not $Force) { throw "Destination already exists. Use -Force to replace it: $Destination" }
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    $runtimeDestination = Join-Path $Destination 'runtime'
    $modelDestination = Join-Path $Destination 'models'
    New-Item -ItemType Directory -Path $runtimeDestination -Force | Out-Null
    New-Item -ItemType Directory -Path $modelDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $release 'whisper-server.exe') -Destination $runtimeDestination
    Copy-Item -LiteralPath (Join-Path $release 'whisper.dll') -Destination $runtimeDestination
    Get-ChildItem -LiteralPath $release -Filter 'ggml*.dll' -File | Copy-Item -Destination $runtimeDestination
    Copy-Item -LiteralPath $modelDownload -Destination (Join-Path $modelDestination 'ggml-base-q8_0.bin')
    Copy-Item -LiteralPath $licenseDownload -Destination (Join-Path $Destination 'LICENSE-whisper.cpp.txt')

    $files = Get-ChildItem -LiteralPath $Destination -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($Destination.Length).TrimStart('\').Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            sizeBytes = $_.Length
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        id = 'gta-rp-assistant-stt-base-q8_0'
        version = '1.0.0'
        runtime = 'whisper.cpp'
        runtimeVersion = $runtimeVersion
        architecture = 'win-x64'
        entryPoint = 'runtime/whisper-server.exe'
        modelId = 'whisper-base-q8_0-multilingual'
        modelFile = 'models/ggml-base-q8_0.bin'
        language = 'ru'
        inferencePath = '/inference'
        licenseFile = 'LICENSE-whisper.cpp.txt'
        runtimeSource = $runtimeUrl
        modelSource = $modelUrl
        files = @($files)
        limits = [ordered]@{
            threads = 2
            startupTimeoutSeconds = 90
            requestTimeoutSeconds = 45
            idleTtlSeconds = 120
            hardMemoryLimitBytes = 1153433600
        }
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Destination 'stt-pack.json') -Encoding UTF8

    if (-not $SkipArchive) {
        $releaseDirectory = Join-Path $root 'artifacts\stt\release'
        New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
        $archive = Join-Path $releaseDirectory 'GtaRpAssistant-STT-base-q8_0-v1.9.1-win-x64.zip'
        Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath $archive -CompressionLevel Optimal -Force
        $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        "$archiveHash  $([System.IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ASCII
        Write-Host "STT archive: $archive"
        Write-Host "Archive SHA-256: $archiveHash"
    }
    Write-Host "STT pack: $Destination"
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
