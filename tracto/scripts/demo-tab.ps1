# Runs one Tracto service inside a Windows Terminal tab. Started by start-demo.sh at the repository root:
#   powershell -NoExit -File tracto\scripts\demo-tab.ps1 <name>
# Names: database, orchestrator, sheet-service, orchestrator-ui, desktop-runtime, browser-runtime, designer
param([Parameter(Mandatory = $true)][string]$Name)

$ErrorActionPreference = 'Continue'
$tracto = Split-Path -Parent $PSScriptRoot

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
    Wait-Until 'PostgreSQL on localhost:5432' { Test-Database } | Out-Null
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
    Wait-Database
    Write-Host 'Database is up. This tab can stay open.' -ForegroundColor Green
}

switch ($Name) {
    'database' {
        Start-Database
    }
    'orchestrator' {
        Wait-Database
        Set-Location (Join-Path $tracto 'orchestrator-go')
        go run .
    }
    'sheet-service' {
        Wait-Database
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
