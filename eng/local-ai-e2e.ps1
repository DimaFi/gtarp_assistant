[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [string]$ModelKey = 'qwen/qwen3-vl-4b',
    [string]$OutputDirectory = '',
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot (Join-Path '..\artifacts\local-ai-e2e' (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$testDataDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('GtaRpAssistant-local-ai-e2e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDataDirectory -Force | Out-Null

$previousDataDirectory = $env:GTA_RP_ASSISTANT_DATA_DIR
$previousAutomationMode = $env:GTA_RP_AUTOMATION_MODE
$previousOutputDirectory = $env:GTA_RP_LOCAL_AI_E2E_DIR
$previousModel = $env:GTA_RP_LOCAL_AI_MODEL
$env:GTA_RP_ASSISTANT_DATA_DIR = $testDataDirectory
$env:GTA_RP_AUTOMATION_MODE = '1'
$env:GTA_RP_LOCAL_AI_E2E_DIR = $resolvedOutput
$env:GTA_RP_LOCAL_AI_MODEL = $ModelKey

try {
    foreach ($phase in @('configure', 'verify')) {
        $process = Start-Process -FilePath $resolvedExecutable -ArgumentList @('--local-ai-e2e', '--phase', $phase) -PassThru -WindowStyle Hidden
        try {
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                throw "Local AI E2E phase '$phase' timed out after $TimeoutSeconds seconds."
            }
            if ($process.ExitCode -ne 0) {
                $errorReport = Join-Path $resolvedOutput "$phase-error.txt"
                if (Test-Path -LiteralPath $errorReport) {
                    throw "Local AI E2E phase '$phase' failed.`n$(Get-Content -LiteralPath $errorReport -Raw)"
                }
                throw "Local AI E2E phase '$phase' exited with code $($process.ExitCode)."
            }
            $report = Join-Path $resolvedOutput "$phase.json"
            if (-not (Test-Path -LiteralPath $report)) {
                throw "Local AI E2E phase '$phase' produced no report."
            }
        }
        finally {
            $process.Dispose()
        }
    }
    Write-Host "Local AI UI E2E passed. Reports: $resolvedOutput"
}
finally {
    $env:GTA_RP_ASSISTANT_DATA_DIR = $previousDataDirectory
    $env:GTA_RP_AUTOMATION_MODE = $previousAutomationMode
    $env:GTA_RP_LOCAL_AI_E2E_DIR = $previousOutputDirectory
    $env:GTA_RP_LOCAL_AI_MODEL = $previousModel
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTestData = [System.IO.Path]::GetFullPath($testDataDirectory)
    if ($resolvedTestData.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTestData -Recurse -Force -ErrorAction SilentlyContinue
    }
}
