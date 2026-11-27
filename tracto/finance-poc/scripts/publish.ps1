# Publishes the two finance main jobs to the Orchestrator, points "latest" at them, and creates the
# A1 schedules (08:00 and 15:00, disabled: the demo uses Run). Safe to run again: a version that
# is already published is left as it is, and a schedule that already exists is not added twice.
#
#   powershell -ExecutionPolicy Bypass -File tracto\finance-poc\scripts\publish.ps1
#   ... -Orchestrator http://127.0.0.1:5080 -TimeZone Asia/Kolkata
param(
    [string]$Orchestrator = 'http://127.0.0.1:5080',
    [string]$TimeZone = 'Asia/Kolkata'
)
$ErrorActionPreference = 'Stop'
$base = $Orchestrator.TrimEnd('/')

$scripts = @(
    'finance-a1-claims-to-bills_1.0.0.json',
    'finance-a2-payment-file_1.0.0.json'
)

foreach ($file in $scripts) {
    $path = Join-Path $PSScriptRoot $file
    $script = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $envelope = @{ schemaVersion = '1'; script = $script } | ConvertTo-Json -Depth 20 -Compress
    $payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($envelope))
    $body = @{ schemaVersion = '1'; payload = $payload } | ConvertTo-Json
    $uri = "$base/artifacts/$($script.id)/versions/$($script.version)"
    try {
        Invoke-RestMethod -Uri $uri -Method PUT -Body $body -ContentType 'application/json' | Out-Null
        Write-Host "Published $($script.id) $($script.version)"
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        if ($code -eq 409) { Write-Host "$($script.id) $($script.version) is already published" }
        else { throw }
    }
    Invoke-RestMethod -Method PUT -ContentType 'application/json' -Uri "$base/artifacts/$($script.id)/aliases/latest" `
        -Body (@{ version = $script.version; updatedBy = 'finance-poc publish' } | ConvertTo-Json) | Out-Null
    Write-Host "  latest -> $($script.version)"
}

# A1 runs at 08:00 and 15:00 every day once enabled.
$existing = @(Invoke-RestMethod "$base/admin/schedules")
foreach ($cron in @('0 8 * * *', '0 15 * * *')) {
    $same = $existing | Where-Object { $_.artifactId -eq 'finance-a1-claims-to-bills' -and $_.cronExpression -eq $cron }
    if ($same) { Write-Host "Schedule '$cron' for A1 already exists"; continue }
    $s = @{
        cronExpression = $cron; timeZone = $TimeZone
        artifactId = 'finance-a1-claims-to-bills'; version = 'latest'
        enabled = $false; overlapPolicy = 'skip'
    } | ConvertTo-Json
    Invoke-RestMethod -Method POST -ContentType 'application/json' -Uri "$base/admin/schedules" -Body $s | Out-Null
    Write-Host "Created schedule '$cron' ($TimeZone) for A1, disabled"
}
