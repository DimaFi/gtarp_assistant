[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$testDataDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("GtaRpAssistant-smoke-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDataDirectory -Force | Out-Null
$previousDataDirectory = $env:GTA_RP_ASSISTANT_DATA_DIR
$previousAutomationMode = $env:GTA_RP_AUTOMATION_MODE
$env:GTA_RP_ASSISTANT_DATA_DIR = $testDataDirectory
$env:GTA_RP_AUTOMATION_MODE = '1'
try {
    $process = Start-Process -FilePath $resolvedExecutable -ArgumentList '--smoke-test' -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Smoke test timed out after $TimeoutSeconds seconds."
        }
        if ($process.ExitCode -ne 0) {
            $errorReport = Join-Path $testDataDirectory 'smoke-error.txt'
            if (Test-Path -LiteralPath $errorReport) {
                throw "Smoke test exited with code $($process.ExitCode).`n$(Get-Content -LiteralPath $errorReport -Raw)"
            }
            throw "Smoke test exited with code $($process.ExitCode)."
        }
        Write-Host "WPF smoke test passed (exit code 0, isolated profile)."
    }
    finally { $process.Dispose() }
}
finally {
    $env:GTA_RP_ASSISTANT_DATA_DIR = $previousDataDirectory
    $env:GTA_RP_AUTOMATION_MODE = $previousAutomationMode
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTestData = [System.IO.Path]::GetFullPath($testDataDirectory)
    if ($resolvedTestData.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTestData -Recurse -Force -ErrorAction SilentlyContinue
    }
}
