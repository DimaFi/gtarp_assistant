[CmdletBinding()]
param(
    [string]$Destination,
    [string]$DownloadDirectory,
    [switch]$SkipArchive,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $root 'artifacts\stt\pack-gigaam-v2' }
if ([string]::IsNullOrWhiteSpace($DownloadDirectory)) { $DownloadDirectory = Join-Path $root 'artifacts\stt\downloads' }
$Destination = [IO.Path]::GetFullPath($Destination)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\stt'))
if (-not $Destination.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "STT pack must stay inside $artifactRoot"
}

$modelName = 'sherpa-onnx-nemo-ctc-giga-am-v2-russian-2025-04-19'
$modelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$modelName.tar.bz2"
$archive = Join-Path $DownloadDirectory "$modelName.tar.bz2"
$archiveHash = '777be8717d8aaf04861823671290f7687f7579fd9ac63a2124955573f920caf5'
$archiveSize = 166917722L
$runtimeVersion = '1.13.4'
$runtimeSource = "https://github.com/k2-fsa/sherpa-onnx/tree/v$runtimeVersion"
$runtimeLicense = Join-Path $DownloadDirectory "LICENSE-sherpa-onnx-$runtimeVersion.txt"
$runtimeLicenseHash = 'cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30'

function Assert-File([string]$Path, [string]$Hash, [long]$Size = 0) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing verified input: $Path" }
    $info = Get-Item -LiteralPath $Path
    if ($Size -gt 0 -and $info.Length -ne $Size) { throw "Size mismatch: $Path" }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Hash) { throw "SHA-256 mismatch: $Path" }
}

Assert-File $archive $archiveHash $archiveSize
Assert-File $runtimeLicense $runtimeLicenseHash
$temporary = Join-Path $artifactRoot "temp\gigaam-pack-$([Guid]::NewGuid().ToString('N'))"
$publish = Join-Path $temporary 'runtime'
try {
    New-Item -ItemType Directory -Path $temporary -Force | Out-Null
    tar -xjf $archive -C $temporary
    $modelRoot = Join-Path $temporary $modelName
    $env:NUGET_PACKAGES = Join-Path $artifactRoot 'nuget-packages'
    $env:TEMP = Join-Path $artifactRoot 'temp'
    $env:TMP = $env:TEMP
    dotnet publish (Join-Path $root 'src\GtaRpAssistant.SttHost\GtaRpAssistant.SttHost.csproj') `
        -c Release -r win-x64 --self-contained true --no-restore -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'STT host publish failed.' }

    if (Test-Path -LiteralPath $Destination) {
        if (-not $Force) { throw "Destination already exists. Use -Force: $Destination" }
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $Destination 'runtime') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Destination 'models') -Force | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination (Join-Path $Destination 'runtime') -Recurse
    Copy-Item -LiteralPath (Join-Path $modelRoot 'model.int8.onnx') -Destination (Join-Path $Destination 'models\model.int8.onnx')
    Copy-Item -LiteralPath (Join-Path $modelRoot 'tokens.txt') -Destination (Join-Path $Destination 'models\tokens.txt')
    Copy-Item -LiteralPath (Join-Path $modelRoot 'LICENSE') -Destination (Join-Path $Destination 'LICENSE-GigaAM.txt')
    Copy-Item -LiteralPath $runtimeLicense -Destination (Join-Path $Destination 'LICENSE-sherpa-onnx.txt')

    $files = Get-ChildItem -LiteralPath $Destination -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($Destination.Length).TrimStart('\').Replace('\', '/'); sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); sizeBytes = $_.Length }
    }
    $manifest = [ordered]@{
        schemaVersion = 1; id = 'gta-rp-assistant-stt-gigaam-v2'; version = '1.0.0'
        runtime = 'sherpa-onnx'; runtimeVersion = $runtimeVersion; architecture = 'win-x64'
        entryPoint = 'runtime/GtaRpAssistant.SttHost.exe'; modelId = 'gigaam-v2-russian-int8'
        modelFile = 'models/model.int8.onnx'; tokenFile = 'models/tokens.txt'; language = 'ru'
        inferencePath = '/stdio-v1'; licenseFile = 'LICENSE-GigaAM.txt'
        runtimeSource = $runtimeSource; modelSource = $modelUrl; files = @($files)
        limits = [ordered]@{ threads = 4; startupTimeoutSeconds = 90; requestTimeoutSeconds = 45; idleTtlSeconds = 120; hardMemoryLimitBytes = 1153433600L }
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Destination 'stt-pack.json') -Encoding UTF8
    if (-not $SkipArchive) {
        $release = Join-Path $artifactRoot 'release'; New-Item -ItemType Directory -Path $release -Force | Out-Null
        $zip = Join-Path $release 'GtaRpAssistant-STT-gigaam-v2-win-x64.zip'
        Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath $zip -CompressionLevel Optimal -Force
        $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding ASCII
    }
    Write-Host "STT pack: $Destination"
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
