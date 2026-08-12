[CmdletBinding()]
param(
    [ValidateSet('start', 'status', 'restart', 'stop')]
    [string]$Action = 'status'
)

$ErrorActionPreference = 'Stop'
$compose = Join-Path $PSScriptRoot '..\.codex\qdrant\compose.yaml'
$dockerCandidates = @(
    (Get-Command docker -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin\docker.exe",
    'E:\Apps\DockerDesktop\resources\bin\docker.exe',
    'C:\Program Files\Docker\Docker\resources\bin\docker.exe'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if (-not $dockerCandidates) {
    throw 'Docker CLI was not found. Install or start Docker Desktop, then retry.'
}

$docker = $dockerCandidates
switch ($Action) {
    'start'   { & $docker compose -f $compose up -d }
    'restart' { & $docker compose -f $compose restart }
    'stop'    { & $docker compose -f $compose down }
    'status'  { & $docker compose -f $compose ps }
}
if ($LASTEXITCODE -ne 0) { throw "Docker compose завершился с кодом $LASTEXITCODE." }

if ($Action -in @('start', 'restart', 'status')) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        try {
            $health = Invoke-RestMethod -Uri 'http://127.0.0.1:6333/healthz' -TimeoutSec 3
            if ($health -eq 'healthz check passed') {
                Write-Host 'Qdrant health: OK (loopback only)'
                exit 0
            }
        } catch { Start-Sleep -Milliseconds 750 }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Qdrant did not pass its health check within 45 seconds.'
}
