[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [ValidateRange(1, 100)]
    [int]$Iterations = 10,
    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 20,
    [string]$ReportPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\reports\lifecycle-soak.json')
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$results = @()
$previousDataDirectory = $env:GTA_RP_ASSISTANT_DATA_DIR
$previousAutomationMode = $env:GTA_RP_AUTOMATION_MODE
$env:GTA_RP_AUTOMATION_MODE = '1'
try {
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $testDataDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("GtaRpAssistant-soak-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $testDataDirectory -Force | Out-Null
        $env:GTA_RP_ASSISTANT_DATA_DIR = $testDataDirectory
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $resolvedExecutable -ArgumentList '--smoke-test' -PassThru -WindowStyle Hidden
        $peakWorkingSet = 0L
        $timedOut = $false
        try {
            while (-not $process.HasExited -and $stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
                $process.Refresh()
                $peakWorkingSet = [Math]::Max($peakWorkingSet, $process.WorkingSet64)
                Start-Sleep -Milliseconds 100
            }
            if (-not $process.HasExited) {
                $timedOut = $true
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                $process.WaitForExit()
            }
            $errorPath = Join-Path $testDataDirectory 'smoke-error.txt'
            $errorDetail = if (Test-Path -LiteralPath $errorPath) { (Get-Content -LiteralPath $errorPath -Raw).Trim() } else { $null }
            $results += [pscustomobject][ordered]@{
                iteration = $iteration
                exitCode = if ($timedOut) { $null } else { $process.ExitCode }
                timedOut = $timedOut
                durationMs = $stopwatch.ElapsedMilliseconds
                peakWorkingSetMb = [Math]::Round($peakWorkingSet / 1MB, 2)
                error = $errorDetail
            }
        }
        finally {
            $stopwatch.Stop()
            $process.Dispose()
            $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
            $resolvedTestData = [System.IO.Path]::GetFullPath($testDataDirectory)
            if ($resolvedTestData.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $resolvedTestData -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
finally {
    $env:GTA_RP_ASSISTANT_DATA_DIR = $previousDataDirectory
    $env:GTA_RP_AUTOMATION_MODE = $previousAutomationMode
}

$reportDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($ReportPath))
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$report = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    executable = $resolvedExecutable
    operatingSystem = [Environment]::OSVersion.VersionString
    iterations = $Iterations
    failures = @($results | Where-Object { $_.timedOut -or $_.exitCode -ne 0 }).Count
    maxPeakWorkingSetMb = ($results | Measure-Object -Property peakWorkingSetMb -Maximum).Maximum
    runs = $results
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Host "Lifecycle soak complete. Failures: $($report.failures)/$Iterations; max peak working set: $($report.maxPeakWorkingSetMb) MB"
Write-Host "Report: $ReportPath"
if ($report.failures -gt 0) { throw 'Lifecycle soak detected one or more failures.' }
