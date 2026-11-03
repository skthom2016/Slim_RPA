param(
    [string]$Orchestrator = 'http://localhost:5080',
    [string]$WorkerId = 'browser-runtime-headless-001',
    [string]$Channel = ''
)

# Starts the Browser Runtime host with no visible window. Scripts are unchanged: headless is a
# worker setting, so every browser script runs the same way in either mode.
# Set TRACTO_SECRET_<NAME> variables in this window first for scripts that use ${secret:NAME}.
$env:ORCHESTRATOR_URL = $Orchestrator
$env:WORKER_ID = $WorkerId
$env:BROWSER_MODE = 'headless'
$env:BROWSER_CHANNEL = $Channel

Set-Location (Join-Path $PSScriptRoot 'src\TractoRuntime.Browser.Host')
dotnet run
