<#
.SYNOPSIS
  Starts Orchestrator and Hello World services, runs E2E tests, then stops services.

.DESCRIPTION
  Usage:  .\run-e2e.ps1 [-OrchestratorPort 5100] [-HelloWorldPort 5200] [-DbPath tracto-e2e.db]
  Requires: dotnet 10, SQLite (no Docker needed).
  Set TRACTO_SKIP_BUILD=1 to skip dotnet build.
#>
param(
    [int]    $OrchestratorPort = 5100,
    [int]    $HelloWorldPort   = 5200,
    [string] $DbPath           = "tracto-e2e.db"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

function Wait-ForUrl([string]$Url, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { return $true }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# ── Build ────────────────────────────────────────────────────────────────────
if ($env:TRACTO_SKIP_BUILD -ne "1") {
    Write-Host "Building solution..." -ForegroundColor Cyan
    & dotnet build "$Root\tracto.slnx" -c Release --no-restore -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

# ── Orchestrator ─────────────────────────────────────────────────────────────
Write-Host "Starting Orchestrator on port $OrchestratorPort..." -ForegroundColor Cyan

$dbAbsPath = Join-Path $Root $DbPath
if (Test-Path $dbAbsPath) { Remove-Item $dbAbsPath -Force }   # fresh DB each run

$orchEnv = @{
    ASPNETCORE_URLS       = "http://127.0.0.1:$OrchestratorPort"
    TRACTO_DB_PATH        = $dbAbsPath
    TRACTO_MODE           = "Connected"
    ASPNETCORE_ENVIRONMENT= "Development"
}

$orchProc = Start-Process dotnet -ArgumentList @(
    "run",
    "--project", "$Root\orchestrator\src\Tracto.Orchestrator.Api\Tracto.Orchestrator.Api.csproj",
    "--no-build",
    "-c", "Release"
) -NoNewWindow -PassThru -RedirectStandardOutput "$Root\logs\orchestrator.log" `
  -RedirectStandardError "$Root\logs\orchestrator-err.log" `
  -Environment $orchEnv 2>$null

# Ensure log dir
New-Item -ItemType Directory -Force -Path "$Root\logs" | Out-Null

# ── Hello World ──────────────────────────────────────────────────────────────
Write-Host "Starting Hello World on port $HelloWorldPort..." -ForegroundColor Cyan

$hwEnv = @{
    ASPNETCORE_URLS        = "http://127.0.0.1:$HelloWorldPort"
    ASPNETCORE_ENVIRONMENT = "Development"
}

$hwProc = Start-Process dotnet -ArgumentList @(
    "run",
    "--project", "$Root\hello-world\src\Tracto.HelloWorld.Api\Tracto.HelloWorld.Api.csproj",
    "--no-build",
    "-c", "Release"
) -NoNewWindow -PassThru -RedirectStandardOutput "$Root\logs\hello-world.log" `
  -RedirectStandardError "$Root\logs\hello-world-err.log" `
  -Environment $hwEnv 2>$null

# ── Wait for readiness ────────────────────────────────────────────────────────
Write-Host "Waiting for services to be ready..." -ForegroundColor Yellow

$orchReady = Wait-ForUrl "http://127.0.0.1:$OrchestratorPort/health"
$hwReady   = Wait-ForUrl "http://127.0.0.1:$HelloWorldPort/health"

if (-not $orchReady) { Write-Warning "Orchestrator did not become ready in time" }
if (-not $hwReady)   { Write-Warning "Hello World did not become ready in time" }

# ── Run E2E tests ─────────────────────────────────────────────────────────────
Write-Host "Running E2E tests..." -ForegroundColor Cyan

$env:TRACTO_ORCHESTRATOR_URL = "http://127.0.0.1:$OrchestratorPort"
$env:TRACTO_HELLO_WORLD_URL  = "http://127.0.0.1:$HelloWorldPort"

& dotnet test "$Root\e2e\Tracto.E2E.Tests\Tracto.E2E.Tests.csproj" `
    --no-build -c Release `
    --logger "console;verbosity=detailed" `
    --logger "trx;LogFileName=$Root\logs\e2e-results.trx" `
    --results-directory "$Root\logs"

$testExitCode = $LASTEXITCODE

# ── Stop services ─────────────────────────────────────────────────────────────
Write-Host "Stopping services..." -ForegroundColor Cyan
try { $orchProc | Stop-Process -Force -ErrorAction SilentlyContinue } catch { }
try { $hwProc   | Stop-Process -Force -ErrorAction SilentlyContinue } catch { }

# Clean up test DB
if (Test-Path $dbAbsPath) { Remove-Item $dbAbsPath -Force }

exit $testExitCode
