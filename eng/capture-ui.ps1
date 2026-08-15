[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [string]$OutputDirectory,
    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\ui-snapshots'
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$previousOutput = $env:GTA_RP_UI_SNAPSHOT_DIR
$previousDataDirectory = $env:GTA_RP_ASSISTANT_DATA_DIR
$previousAutomationMode = $env:GTA_RP_AUTOMATION_MODE
$testDataDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("GtaRpAssistant-capture-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDataDirectory -Force | Out-Null
$env:GTA_RP_UI_SNAPSHOT_DIR = $resolvedOutput
$env:GTA_RP_ASSISTANT_DATA_DIR = $testDataDirectory
$env:GTA_RP_AUTOMATION_MODE = '1'
try {
    $process = Start-Process -FilePath $resolvedExecutable -ArgumentList '--capture-ui' -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "UI snapshot capture timed out after $TimeoutSeconds seconds."
        }
        if ($process.ExitCode -ne 0) {
            $errorReport = Join-Path $resolvedOutput 'capture-error.txt'
            if (Test-Path -LiteralPath $errorReport) {
                throw "UI snapshot capture exited with code $($process.ExitCode).`n$(Get-Content -LiteralPath $errorReport -Raw)"
            }
            throw "UI snapshot capture exited with code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }

    foreach ($feature in @('assistant', 'audio', 'providers', 'behavior', 'privacy', 'memory', 'knowledge', 'about')) {
        $process = Start-Process -FilePath $resolvedExecutable -ArgumentList @('--capture-ui', '--capture-feature', $feature) -PassThru -WindowStyle Hidden
        try {
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                throw "UI snapshot capture for '$feature' timed out after $TimeoutSeconds seconds."
            }
            if ($process.ExitCode -ne 0) {
                $errorReport = Join-Path $resolvedOutput 'capture-error.txt'
                if (Test-Path -LiteralPath $errorReport) {
                    throw "UI snapshot capture for '$feature' exited with code $($process.ExitCode).`n$(Get-Content -LiteralPath $errorReport -Raw)"
                }
                throw "UI snapshot capture for '$feature' exited with code $($process.ExitCode)."
            }
        }
        finally {
            $process.Dispose()
        }
    }
}
finally {
    $env:GTA_RP_UI_SNAPSHOT_DIR = $previousOutput
    $env:GTA_RP_ASSISTANT_DATA_DIR = $previousDataDirectory
    $env:GTA_RP_AUTOMATION_MODE = $previousAutomationMode
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTestData = [System.IO.Path]::GetFullPath($testDataDirectory)
    if ($resolvedTestData.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTestData -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$expectedSnapshots = @('assistant', 'audio', 'providers', 'behavior', 'privacy', 'memory', 'knowledge', 'about', 'overlay-compact', 'overlay-expanded', 'voice-preview', 'vision-preview')
foreach ($name in $expectedSnapshots) {
    $snapshot = Get-Item -LiteralPath (Join-Path $resolvedOutput "$name.png") -ErrorAction SilentlyContinue
    if ($null -eq $snapshot) { throw "Missing UI snapshot: $name.png." }
    if ($snapshot.Length -lt 5000) { throw "UI snapshot is unexpectedly small: $($snapshot.FullName)." }
}
Write-Host "UI snapshots captured: $($expectedSnapshots.Count) in $resolvedOutput"
