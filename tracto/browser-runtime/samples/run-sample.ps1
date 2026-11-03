param(
    [Parameter(Mandatory = $true)][string]$File,
    [string]$Orchestrator = 'http://localhost:5080',
    [int]$WaitSeconds = 120
)

# Publishes a flat browser script to the Orchestrator and submits one job for it.
# The script is wrapped in the {"schemaVersion":"1","script":{...}} envelope; versions are
# immutable, so a unique patch number is appended to the version in the file.
$ErrorActionPreference = 'Stop'

$script = Get-Content -LiteralPath $File -Raw | ConvertFrom-Json
$version = '1.0.' + ([int]([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() % 1000000))
$script.version = $version

$envelope = @{ schemaVersion = '1'; script = $script } | ConvertTo-Json -Depth 12 -Compress
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($envelope))
$body = @{ schemaVersion = '1'; payload = $payload } | ConvertTo-Json

Invoke-RestMethod -Uri "$Orchestrator/artifacts/$($script.id)/versions/$version" -Method Put `
    -Body $body -ContentType 'application/json' | Out-Null

$job = @{ schemaVersion = '1'; scriptArtifactId = $script.id; scriptVersion = $version; triggerType = 'Manual' } |
    ConvertTo-Json
$run = Invoke-RestMethod -Uri "$Orchestrator/jobs" -Method Post -Body $job -ContentType 'application/json'
"submitted $($script.id) $version  run=$($run.runId)"

$end = (Get-Date).AddSeconds($WaitSeconds)
do {
    Start-Sleep -Seconds 2
    $state = (Invoke-RestMethod "$Orchestrator/jobs/$($run.runId)").state
} while ((Get-Date) -lt $end -and $state -notmatch 'Succeeded|Failed|Cancelled|TimedOut|Unknown|HandedOff')
"state: $state"
