[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$RuntimeTag = 'b10016',
    [ValidateSet('all', 'qwen3-0.6b', 'smollm2-360m-instruct')]
    [string]$Candidate = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\model-benchmarks\assets'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$workspace = [IO.Path]::GetFullPath($root)
if (-not $output.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must stay inside the workspace: $output"
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

$runtimeName = "llama-$RuntimeTag-bin-win-cpu-x64.zip"
$runtimeUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$RuntimeTag/$runtimeName"
$runtimeZip = Join-Path $output $runtimeName
$runtimeDirectory = Join-Path $output "llama-$RuntimeTag-win-cpu-x64"
$models = @(
    [ordered]@{
        candidateId = 'qwen3-0.6b'
        fileName = 'Qwen3-0.6B-Q4_0.gguf'
        revision = 'ggml-org/Qwen3-0.6B-GGUF@a41486f'
        sourceUrl = 'https://huggingface.co/ggml-org/Qwen3-0.6B-GGUF/resolve/a41486f/Qwen3-0.6B-Q4_0.gguf?download=true'
        sha256 = 'da2572f16c06133561ce56accaa822216f2391ef4d37fba427801cd6736417d4'
        license = 'Apache-2.0'
        licenseUrl = 'https://huggingface.co/Qwen/Qwen3-0.6B/blob/main/LICENSE'
    },
    [ordered]@{
        candidateId = 'smollm2-360m-instruct'
        fileName = 'smollm2-360m-instruct-q8_0.gguf'
        revision = 'HuggingFaceTB/SmolLM2-360M-Instruct-GGUF@593b5a2e04c8f3e4ee880263f93e0bd2901ad47f'
        sourceUrl = 'https://huggingface.co/HuggingFaceTB/SmolLM2-360M-Instruct-GGUF/resolve/593b5a2e04c8f3e4ee880263f93e0bd2901ad47f/smollm2-360m-instruct-q8_0.gguf?download=true'
        sha256 = '48ab3034d0dd401fbc721eb1df3217902fee7dab9078992d66431f09b7750201'
        license = 'Apache-2.0'
        licenseUrl = 'https://huggingface.co/HuggingFaceTB/SmolLM2-360M-Instruct/blob/main/LICENSE'
    }
)
if ($Candidate -ne 'all') { $models = @($models | Where-Object { $_.candidateId -eq $Candidate }) }

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Receive-VerifiedFile([string]$Url, [string]$Destination, [string]$ExpectedSha256) {
    if (Test-Path -LiteralPath $Destination) {
        $actual = Get-Sha256 $Destination
        if ($actual -ne $ExpectedSha256) { throw "Existing file has wrong SHA-256: $Destination ($actual)" }
        return
    }
    $temporary = "$Destination.$([Guid]::NewGuid().ToString('N')).download"
    try {
        Invoke-WebRequest -Uri $Url -OutFile $temporary -UseBasicParsing
        $actual = Get-Sha256 $temporary
        if ($actual -ne $ExpectedSha256) { throw "Downloaded file has wrong SHA-256: $Destination ($actual)" }
        Move-Item -LiteralPath $temporary -Destination $Destination
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

$release = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$RuntimeTag" -Headers @{ 'User-Agent' = 'GtaRpAssistant-ModelBenchmark' }
$asset = $release.assets | Where-Object { $_.name -eq $runtimeName } | Select-Object -First 1
if ($null -eq $asset) { throw "Runtime asset was not found in official release ${RuntimeTag}: $runtimeName" }
$runtimeSha256 = [string]$asset.digest
if (-not $runtimeSha256.StartsWith('sha256:', [StringComparison]::OrdinalIgnoreCase)) { throw 'Official runtime asset has no SHA-256 digest.' }
$runtimeSha256 = $runtimeSha256.Substring(7).ToLowerInvariant()

Receive-VerifiedFile -Url $runtimeUrl -Destination $runtimeZip -ExpectedSha256 $runtimeSha256
foreach ($model in $models) {
    $model['path'] = Join-Path $output $model.fileName
    Receive-VerifiedFile -Url $model.sourceUrl -Destination $model.path -ExpectedSha256 $model.sha256
}

$llamaCompletion = Join-Path $runtimeDirectory 'llama-completion.exe'
if (-not (Test-Path -LiteralPath $runtimeDirectory)) {
    New-Item -ItemType Directory -Path $runtimeDirectory | Out-Null
    Expand-Archive -LiteralPath $runtimeZip -DestinationPath $runtimeDirectory
}
if (-not (Test-Path -LiteralPath $llamaCompletion)) { throw "Extracted runtime does not contain llama-completion.exe: $runtimeDirectory" }

$provenance = [ordered]@{
    schemaVersion = 1
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    runtime = [ordered]@{
        tag = $RuntimeTag
        sourceUrl = $runtimeUrl
        sha256 = $runtimeSha256
        executable = $llamaCompletion
    }
    models = $models
}
$provenancePath = Join-Path $output 'provenance.json'
$provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $provenancePath -Encoding UTF8

Write-Host "Runtime: $llamaCompletion"
foreach ($model in $models) { Write-Host "Model:   $($model.path)" }
Write-Host "Record:  $provenancePath"
