[CmdletBinding()]
param(
    [string]$Dataset,
    [string]$BasePack,
    [string]$SmallPack,
    [string]$OutputDirectory,
    [switch]$RunLifecycle,
    [string]$LifecycleWave,
    [ValidateSet('reference', 'weak-pc')]
    [string]$LifecycleHardwareProfile = 'reference',
    [ValidateRange(1, 100)]
    [int]$LifecycleIterations = 100
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Dataset)) { $Dataset = Join-Path $root 'ml\evaluation\stt-russian-gta5rp-v1.json' }
if ([string]::IsNullOrWhiteSpace($BasePack)) { $BasePack = Join-Path $root 'artifacts\stt\pack-base-q8_0' }
if ([string]::IsNullOrWhiteSpace($SmallPack)) { $SmallPack = Join-Path $root 'artifacts\stt\pack-small-q5_1' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'artifacts\stt\comparisons\current' }
$Dataset = [System.IO.Path]::GetFullPath($Dataset)
$BasePack = [System.IO.Path]::GetFullPath($BasePack)
$SmallPack = [System.IO.Path]::GetFullPath($SmallPack)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $Dataset -PathType Leaf)) { throw "STT dataset not found: $Dataset" }
foreach ($pack in @($BasePack, $SmallPack)) {
    if (-not (Test-Path -LiteralPath (Join-Path $pack 'stt-pack.json') -PathType Leaf)) {
        throw "Validated STT pack directory not found: $pack"
    }
}

$datasetDocument = Get-Content -LiteralPath $Dataset -Raw -Encoding UTF8 | ConvertFrom-Json
$cases = @($datasetDocument.cases)
if ($cases.Count -lt [int]$datasetDocument.gate.minimumCases) {
    throw "STT dataset has $($cases.Count) cases, but requires $($datasetDocument.gate.minimumCases)."
}
$datasetRoot = ([System.IO.Path]::GetDirectoryName($Dataset)).TrimEnd('\') + '\'
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($case in $cases) {
    $relative = [string]$case.audioFile
    if ([string]::IsNullOrWhiteSpace($relative) -or [System.IO.Path]::IsPathRooted($relative)) {
        throw "Unsafe STT dataset audio path: $relative"
    }
    $audio = [System.IO.Path]::GetFullPath((Join-Path $datasetRoot $relative))
    if (-not $audio.StartsWith($datasetRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "STT dataset audio path escapes its directory: $relative"
    }
    if (-not (Test-Path -LiteralPath $audio -PathType Leaf)) { $missing.Add($relative) }
}
if ($missing.Count -gt 0) {
    $preview = ($missing | Select-Object -First 5) -join ', '
    throw "STT dataset is incomplete: $($missing.Count) WAV files are missing. First missing files: $preview"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$project = Join-Path $root 'tools\GtaRpAssistant.SttBenchmark\GtaRpAssistant.SttBenchmark.csproj'
$baseReport = Join-Path $OutputDirectory 'base-q8_0.json'
$smallReport = Join-Path $OutputDirectory 'small-q5_1.json'
$comparisonReport = Join-Path $OutputDirectory 'comparison.json'

& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "STT benchmark build failed with exit code $LASTEXITCODE." }

function Invoke-Evaluation([string]$Pack, [string]$Report) {
    & dotnet run --project $project -c Release --no-build -- evaluate $Pack $Dataset $Report
    $code = $LASTEXITCODE
    if ($code -ne 0 -and $code -ne 2) { throw "STT evaluation failed with infrastructure exit code $code for $Pack." }
}

Invoke-Evaluation $BasePack $baseReport
Invoke-Evaluation $SmallPack $smallReport
& dotnet run --project $project -c Release --no-build -- compare $baseReport $smallReport $comparisonReport
$comparisonCode = $LASTEXITCODE
if ($comparisonCode -ne 0 -and $comparisonCode -ne 2) { throw "STT comparison failed with infrastructure exit code $comparisonCode." }

$comparison = Get-Content -LiteralPath $comparisonReport -Raw -Encoding UTF8 | ConvertFrom-Json
if ($RunLifecycle) {
    if ([string]::IsNullOrWhiteSpace($LifecycleWave)) { throw '-LifecycleWave is required with -RunLifecycle.' }
    $LifecycleWave = [System.IO.Path]::GetFullPath($LifecycleWave)
    if (-not (Test-Path -LiteralPath $LifecycleWave -PathType Leaf)) { throw "Lifecycle WAV not found: $LifecycleWave" }
    if ([string]::IsNullOrWhiteSpace([string]$comparison.recommendedPackId)) {
        Write-Warning 'Lifecycle gate was not started because neither candidate passed the quality gate.'
    }
    else {
        $baseId = [string](Get-Content -LiteralPath (Join-Path $BasePack 'stt-pack.json') -Raw -Encoding UTF8 | ConvertFrom-Json).id
        $smallId = [string](Get-Content -LiteralPath (Join-Path $SmallPack 'stt-pack.json') -Raw -Encoding UTF8 | ConvertFrom-Json).id
        if ($comparison.recommendedPackId -eq $baseId) { $selectedPack = $BasePack }
        elseif ($comparison.recommendedPackId -eq $smallId) { $selectedPack = $SmallPack }
        else { throw "Comparison selected an unknown pack: $($comparison.recommendedPackId)" }
        $lifecycleReport = Join-Path $OutputDirectory "winner-lifecycle-$LifecycleHardwareProfile.json"
        & dotnet run --project $project -c Release --no-build -- lifecycle $selectedPack $LifecycleWave $LifecycleIterations $lifecycleReport $LifecycleHardwareProfile
        if ($LASTEXITCODE -ne 0) { throw "Winner lifecycle gate failed with exit code $LASTEXITCODE." }
    }
}

Write-Host "STT comparison report: $comparisonReport"
Write-Host "Decision: $($comparison.decision)"
if ($comparisonCode -eq 2) { exit 2 }
