[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Comparison,
    [Parameter(Mandatory)] [string]$PackDirectory,
    [Parameter(Mandatory)] [string[]]$LifecycleReports,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'artifacts\stt\final' }
$Comparison = [System.IO.Path]::GetFullPath($Comparison)
$PackDirectory = [System.IO.Path]::GetFullPath($PackDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$LifecycleReports = @($LifecycleReports | ForEach-Object { [System.IO.Path]::GetFullPath($_) })

foreach ($path in @($Comparison) + $LifecycleReports) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "STT evidence file not found: $path" }
}
if (-not (Test-Path -LiteralPath (Join-Path $PackDirectory 'stt-pack.json') -PathType Leaf)) {
    throw "STT pack manifest not found: $PackDirectory"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$project = Join-Path $root 'tools\GtaRpAssistant.SttBenchmark\GtaRpAssistant.SttBenchmark.csproj'
$archive = Join-Path $OutputDirectory 'GtaRpAssistant-Local-VoicePack-win-x64.zip'
$attestation = Join-Path $OutputDirectory 'production-attestation.json'

& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "STT benchmark build failed with exit code $LASTEXITCODE." }
& dotnet run --project $project -c Release --no-build -- finalize $Comparison $PackDirectory $archive $attestation @LifecycleReports
if ($LASTEXITCODE -ne 0) { throw "STT production gate rejected the final voice pack. See $attestation" }

$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([System.IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ASCII
Write-Host "Final local voice pack: $archive"
Write-Host "Production attestation: $attestation"
