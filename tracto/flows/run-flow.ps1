param(
    [Parameter(Mandatory = $true)][string]$Flow,
    [string]$Orchestrator = 'http://localhost:5080',
    [int]$WaitSeconds = 300
)

# Runs a flow file: publishes each listed script, then asks the Orchestrator to run them in
# order. Every run receives the flow params plus the outputs of the runs before it, as
# ${param.name}. Scripts may target different runtimes (desktop, browser).
#
# A flow is one JSON file:
#   { "name": "...", "params": { "query": "..." }, "scripts": [ { <script 1> }, { <script 2> } ] }
# Each script is embedded in full; an entry may instead be a path, relative to the flow file,
# to a script file.
$ErrorActionPreference = 'Stop'

$flowPath = Resolve-Path -LiteralPath $Flow
$flowDir = Split-Path -Parent $flowPath
$def = Get-Content -LiteralPath $flowPath -Raw | ConvertFrom-Json

$steps = @()
foreach ($item in $def.scripts) {
    if ($item -is [string]) {
        $script = Get-Content -LiteralPath (Join-Path $flowDir $item) -Raw | ConvertFrom-Json
    } else {
        $script = $item
    }
    $version = '1.0.' + ([int]([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() % 1000000))
    $script.version = $version

    $envelope = @{ schemaVersion = '1'; script = $script } | ConvertTo-Json -Depth 12 -Compress
    $payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($envelope))
    $body = @{ schemaVersion = '1'; payload = $payload } | ConvertTo-Json
    Invoke-RestMethod -Uri "$Orchestrator/artifacts/$($script.id)/versions/$version" -Method Put `
        -Body $body -ContentType 'application/json' | Out-Null

    "published $($script.id) $version ($($script.targetRuntime))"
    $steps += @{ scriptArtifactId = $script.id; scriptVersion = $version }
    Start-Sleep -Milliseconds 1100   # versions are derived from the clock
}

$params = @{}
if ($def.params) { $def.params.PSObject.Properties | ForEach-Object { $params[$_.Name] = [string]$_.Value } }

$request = @{ name = $def.name; steps = $steps; params = $params } | ConvertTo-Json -Depth 6
$created = Invoke-RestMethod -Uri "$Orchestrator/flows" -Method Post -Body $request -ContentType 'application/json'
"flow $($created.flowId) started ($($created.steps) steps)"

$end = (Get-Date).AddSeconds($WaitSeconds)
do {
    Start-Sleep -Seconds 3
    $state = Invoke-RestMethod "$Orchestrator/flows/$($created.flowId)"
    $progress = ($state.runs | ForEach-Object { "$($_.stepIndex + 1):$($_.state)" }) -join ' '
    "  $($state.state)  $progress"
} while ((Get-Date) -lt $end -and $state.state -eq 'RUNNING')

"flow state: $($state.state)"
foreach ($run in $state.runs) {
    if ($run.outputs.PSObject.Properties.Count -gt 0) {
        "outputs of step $($run.stepIndex + 1) ($($run.script)):"
        $run.outputs.PSObject.Properties | ForEach-Object {
            $v = [string]$_.Value
            if ($v.Length -gt 200) { $v = $v.Substring(0, 200) + '…' }
            "  $($_.Name) = $($v -replace "`r?`n", ' | ')"
        }
    }
}
