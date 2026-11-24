<#
.SYNOPSIS
  Starts the Orchestrator and Hello World services, runs the E2E tests, then stops the services.

.DESCRIPTION
  Usage:  .\run-e2e.ps1 [-OrchestratorPort 5100] [-HelloWorldPort 5200] [-Filter <dotnet test filter>]
  Example: .\run-e2e.ps1 -Filter "FullyQualifiedName!~Notepad"   (skips the test that drives a real Notepad window)
  Requires: dotnet 10, Go, and a PostgreSQL server. The connection string is DATABASE_URL, taken from
  the environment or from orchestrator-go\.env. The run uses a schema of its own (e2e_<number>) that
  is created for it and dropped at the end, so it never touches the orchestrator's real data.
  The services are stopped and the schema dropped however the run ends.
  Set TRACTO_SKIP_BUILD=1 to skip dotnet build.
#>
param(
    [int] $OrchestratorPort = 5100,
    [int] $HelloWorldPort   = 5200,
    [string] $Filter        = ""      # a dotnet test filter, for example "FullyQualifiedName!~Notepad"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$OrchDir = Join-Path $Root "orchestrator-go"

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

# A value from the environment, else from a KEY=VALUE .env file.
function Get-Setting([string]$Name, [string]$EnvFile) {
    $v = [Environment]::GetEnvironmentVariable($Name)
    if ($v) { return $v }
    if (Test-Path $EnvFile) {
        foreach ($line in Get-Content $EnvFile) {
            if ($line -match "^\s*$Name\s*=\s*(.+?)\s*$") { return $Matches[1].Trim('"', "'") }
        }
    }
    return $null
}

# Something already answering on a port would be mistaken for the service this run starts.
function Assert-PortFree([int]$Port, [string]$What) {
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $Port ($What) is already in use: stop what is listening there, or pass another port."
    }
}

$databaseUrl = Get-Setting "DATABASE_URL" (Join-Path $OrchDir ".env")
if (-not $databaseUrl) { throw "DATABASE_URL is not set: put it in the environment or in orchestrator-go\.env" }
Assert-PortFree $OrchestratorPort "Orchestrator"
Assert-PortFree $HelloWorldPort "Hello World"

New-Item -ItemType Directory -Force -Path "$Root\logs" | Out-Null

# ── Build ────────────────────────────────────────────────────────────────────
if ($env:TRACTO_SKIP_BUILD -ne "1") {
    Write-Host "Building solution..." -ForegroundColor Cyan
    & dotnet build "$Root\tracto.slnx" -c Release --no-restore -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}
Write-Host "Building the orchestrator..." -ForegroundColor Cyan
$orchExe = Join-Path $env:TEMP "tracto-orchestrator-e2e.exe"
Push-Location $OrchDir
try { & go build -o $orchExe . } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Orchestrator build failed" }

$schema = "e2e_" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$orchProc = $null
$hwProc = $null
$testExitCode = 1
$saved = @{}
foreach ($k in "DATABASE_URL", "ORCHESTRATOR_SCHEMA", "ORCHESTRATOR_PORT", "TRACTO_MODE", "ORCHESTRATOR_ENV_FILE", "ASPNETCORE_URLS", "ASPNETCORE_ENVIRONMENT", "TRACTO_ORCHESTRATOR_URL", "TRACTO_HELLO_WORLD_URL") {
    $saved[$k] = [Environment]::GetEnvironmentVariable($k)
}

try {
    # ── Orchestrator ─────────────────────────────────────────────────────────
    Write-Host "Starting Orchestrator on port $OrchestratorPort..." -ForegroundColor Cyan
    $env:DATABASE_URL          = $databaseUrl
    $env:ORCHESTRATOR_SCHEMA   = $schema
    $env:ORCHESTRATOR_PORT     = "$OrchestratorPort"
    $env:TRACTO_MODE           = "connected"
    $env:ORCHESTRATOR_ENV_FILE = Join-Path $Root "nonexistent.env"   # use only the settings above
    $orchProc = Start-Process $orchExe -WorkingDirectory $OrchDir -NoNewWindow -PassThru `
        -RedirectStandardOutput "$Root\logs\orchestrator.log" `
        -RedirectStandardError "$Root\logs\orchestrator-err.log"

    # ── Hello World ──────────────────────────────────────────────────────────
    Write-Host "Starting Hello World on port $HelloWorldPort..." -ForegroundColor Cyan
    $env:ASPNETCORE_URLS        = "http://127.0.0.1:$HelloWorldPort"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $hwProc = Start-Process dotnet -ArgumentList @(
        "run",
        "--project", "$Root\hello-world\src\Tracto.HelloWorld.Api\Tracto.HelloWorld.Api.csproj",
        "--no-build",
        "-c", "Release"
    ) -NoNewWindow -PassThru -RedirectStandardOutput "$Root\logs\hello-world.log" `
      -RedirectStandardError "$Root\logs\hello-world-err.log"

    # ── Wait for readiness ───────────────────────────────────────────────────
    Write-Host "Waiting for services to be ready..." -ForegroundColor Yellow
    $orchReady = Wait-ForUrl "http://127.0.0.1:$OrchestratorPort/health"
    $hwReady   = Wait-ForUrl "http://127.0.0.1:$HelloWorldPort/health"
    if (-not $orchReady) { throw "Orchestrator did not become ready in time (see logs\orchestrator-err.log)" }
    if (-not $hwReady)   { throw "Hello World did not become ready in time (see logs\hello-world-err.log)" }

    # ── Run E2E tests ────────────────────────────────────────────────────────
    Write-Host "Running E2E tests..." -ForegroundColor Cyan
    $env:TRACTO_ORCHESTRATOR_URL = "http://127.0.0.1:$OrchestratorPort"
    $env:TRACTO_HELLO_WORLD_URL  = "http://127.0.0.1:$HelloWorldPort"

    # dotnet test reports failures on stderr; that must not abort the script before cleanup.
    $ErrorActionPreference = "Continue"
    $testArgs = @()
    if ($Filter) { $testArgs += @("--filter", $Filter) }
    & dotnet test "$Root\e2e\Tracto.E2E.Tests\Tracto.E2E.Tests.csproj" `
        --no-build -c Release @testArgs `
        --logger "console;verbosity=detailed" `
        --logger "trx;LogFileName=$Root\logs\e2e-results.trx" `
        --results-directory "$Root\logs"
    $testExitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
}
finally {
    $ErrorActionPreference = "Continue"
    Write-Host "Stopping services..." -ForegroundColor Cyan
    foreach ($proc in $orchProc, $hwProc) {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    }
    foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) }

    # Drop the schema this run used.
    $container = (& docker ps --filter "publish=5432" --format "{{.Names}}" 2>$null | Select-Object -First 1)
    if ($container) { & docker exec -i $container psql -q -U tracto -d tracto -c "SET client_min_messages TO warning; DROP SCHEMA IF EXISTS $schema CASCADE;" | Out-Null }
    else { Write-Warning "Could not drop schema $schema automatically; run: DROP SCHEMA $schema CASCADE;" }
}

exit $testExitCode
