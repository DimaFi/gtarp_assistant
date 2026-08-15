[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$FrameworkDependent,
    [switch]$SkipPackage,
    [switch]$SkipSmoke,
    [ValidateRange(0, 100)]
    [int]$SoakIterations = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'GtaRpAssistant.sln'
$pack = Join-Path $root 'knowledge\packs\gta5rp'
$tool = Join-Path $root 'tools\GtaRpAssistant.KnowledgePackTool\GtaRpAssistant.KnowledgePackTool.csproj'
$modelBenchmarkTool = Join-Path $root 'tools\GtaRpAssistant.ModelBenchmark\GtaRpAssistant.ModelBenchmark.csproj'
$modelCandidates = Join-Path $root 'ml\configs\micro-model-candidates.json'
$modelEvaluation = Join-Path $root 'ml\evaluation\micro-model-eval.json'
$conversationModelEvaluation = Join-Path $root 'ml\evaluation\conversation-model-eval.json'
$productBenchmarkTool = Join-Path $root 'tools\GtaRpAssistant.ProductBenchmark\GtaRpAssistant.ProductBenchmark.csproj'
$productEvaluation = Join-Path $root 'ml\evaluation\product-pipeline-eval.json'
$productBenchmarkOutput = Join-Path $root 'artifacts\product-benchmark'
$community = Join-Path $root 'knowledge\reference\community'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    Invoke-DotNet @('restore', $solution, '-r', $Runtime)
    Invoke-DotNet @('build', $solution, '-c', $Configuration, '--no-restore')
    Invoke-DotNet @('test', $solution, '-c', $Configuration, '--no-build', '--no-restore')
    Invoke-DotNet @('run', '--project', $tool, '-c', $Configuration, '--no-build', '--', 'validate', $pack, '--strict')
    Invoke-DotNet @('run', '--project', $modelBenchmarkTool, '-c', $Configuration, '--no-build', '--', 'validate', $modelCandidates, $modelEvaluation)
    Invoke-DotNet @('run', '--project', $modelBenchmarkTool, '-c', $Configuration, '--no-build', '--', 'validate', $modelCandidates, $conversationModelEvaluation)
    Invoke-DotNet @('run', '--project', $productBenchmarkTool, '-c', $Configuration, '--no-build', '--', 'evaluate', $productEvaluation, $pack, $community, $productBenchmarkOutput)
    if (-not $SkipPackage) {
        & (Join-Path $PSScriptRoot 'package.ps1') -Configuration $Configuration -Runtime $Runtime -SelfContained:$SelfContained -FrameworkDependent:$FrameworkDependent -SkipSmoke:$SkipSmoke
        if ($SoakIterations -gt 0) {
            $executable = Join-Path $root "artifacts\publish\$Runtime\GtaRpAssistant.App.exe"
            & (Join-Path $PSScriptRoot 'soak.ps1') -Executable $executable -Iterations $SoakIterations
        }
    }
}
finally {
    Pop-Location
}
