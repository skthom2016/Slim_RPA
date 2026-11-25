# Runs one Tracto service inside a Windows Terminal tab. Started by start-demo.sh at the repository root:
#   powershell -NoExit -File tracto\scripts\demo-tab.ps1 <name>
# Names: database, orchestrator, sheet-service, orchestrator-ui, desktop-runtime, browser-runtime, designer
#
# -Fresh (database tab only): clear the demo data first, with demo-tools\clear-demo-database.ps1 at the
# repository root. That tool is separate from the application.
param([Parameter(Mandatory = $true)][string]$Name, [switch]$Fresh)

$ErrorActionPreference = 'Continue'
$tracto = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $tracto

# The database tab creates this file when the database is ready (and cleared, if asked). The other
# tabs wait for it. start-demo.sh removes it before it opens the tabs.
$readyMarker = Join-Path $env:TEMP 'tracto-demo-database-ready'

function Wait-Until([string]$What, [scriptblock]$Ready, [int]$Seconds = 180) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    Write-Host "Waiting for $What ..." -ForegroundColor DarkGray
    while ((Get-Date) -lt $deadline) {
        $ok = $false
        try { $ok = [bool](& $Ready) } catch { $ok = $false }
        if ($ok) { Write-Host "$What is ready." -ForegroundColor Green; return $true }
        Start-Sleep -Seconds 1
    }
    Write-Host "$What did not become ready in $Seconds s; continuing anyway." -ForegroundColor Yellow
    return $false
}

function Wait-Orchestrator {
    # 127.0.0.1, not localhost: the orchestrator listens on IPv4 only, and "localhost" tries IPv6 first.
    Wait-Until 'the orchestrator (http://127.0.0.1:5080/health)' {
        (Invoke-WebRequest -Uri 'http://127.0.0.1:5080/health' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200
    } | Out-Null
}

function Test-Database {
    # Ask the server itself, then make sure the port is reachable from this machine.
    # (Not the container's health status: a container created without a healthcheck has none.)
    docker exec tracto-postgres pg_isready -U tracto -d tracto 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { return $false }
    $client = New-Object System.Net.Sockets.TcpClient
    try { $client.Connect('127.0.0.1', 5432); return $client.Connected } catch { return $false } finally { $client.Close() }
}

function Wait-Database {
    return (Wait-Until 'the database (started, and cleared if you chose a fresh one)' {
        (Test-Path $readyMarker) -and (Test-Database)
    })
}

function Start-Database {
    docker info 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $desktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
        if (Test-Path $desktop) {
            Write-Host 'Docker is not running. Starting Docker Desktop ...' -ForegroundColor Yellow
            Start-Process $desktop
        } else {
            Write-Host 'Docker is not running and Docker Desktop was not found. Start Docker, then run this tab again.' -ForegroundColor Red
            return
        }
        Wait-Until 'Docker' { docker info 2>$null | Out-Null; $LASTEXITCODE -eq 0 } 240 | Out-Null
    }
    docker start tracto-postgres 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'There is no tracto-postgres container yet. Create it once (see tracto\README.md):' -ForegroundColor Red
        Write-Host '  cd tracto ; $env:TRACTO_DB_PASSWORD = "<password>" ; docker compose up -d postgres'
        return
    }
    if (-not (Wait-Until 'PostgreSQL on localhost:5432' { Test-Database })) {
        Write-Host 'The database did not come up. The other tabs will not start.' -ForegroundColor Red
        return
    }

    if ($Fresh) {
        $clear = Join-Path $repoRoot 'demo-tools\clear-demo-database.ps1'
        Write-Host 'Fresh database: clearing the demo data ...' -ForegroundColor Yellow
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $clear -Yes
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'The database was NOT cleared. The other tabs will not start. Fix the problem above and run ./start-demo.sh again.' -ForegroundColor Red
            return
        }
    } else {
        Write-Host 'Keeping the existing data.' -ForegroundColor Green
    }

    Set-Content -Path $readyMarker -Value (Get-Date -Format o)
    Write-Host 'Database is ready. This tab can stay open.' -ForegroundColor Green
}

# Waits for the database tab; returns $false (and says so) when it never finished.
function Wait-DatabaseOrStop {
    if (Wait-Database) { return $true }
    Write-Host 'The database tab did not finish, so this service was not started.' -ForegroundColor Red
    return $false
}

switch ($Name) {
    'database' {
        Start-Database
    }
    'orchestrator' {
        if (-not (Wait-DatabaseOrStop)) { return }
        Set-Location (Join-Path $tracto 'orchestrator-go')
        go run .
    }
    'sheet-service' {
        if (-not (Wait-DatabaseOrStop)) { return }
        Wait-Orchestrator
        Set-Location (Join-Path $tracto 'sheet-service')
        go run .
    }
    'orchestrator-ui' {
        Wait-Orchestrator
        Set-Location (Join-Path $tracto 'orchestrator-ui')
        node server.js
    }
    'desktop-runtime' {
        Wait-Orchestrator
        Set-Location $tracto
        dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64
    }
    'browser-runtime' {
        Wait-Orchestrator
        Set-Location (Join-Path $tracto 'browser-runtime\src\TractoRuntime.Browser.Host')
        dotnet run
    }
    'designer' {
        Wait-Orchestrator
        Set-Location $tracto
        dotnet run --project designer/src/Tracto.Designer.App -p:Platform=x64
    }
    default {
        Write-Host "Unknown service '$Name'." -ForegroundColor Red
    }
}
