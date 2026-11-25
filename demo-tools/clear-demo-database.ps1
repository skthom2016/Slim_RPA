<#
Clears the data in the local Tracto demo database, so a demo starts from nothing.

This is a stand-alone tool. It is not part of the Tracto application and nothing in the application
calls it. It works only on the local Docker container "tracto-postgres" (through "docker exec"), so it
cannot reach any other database, whatever DATABASE_URL a service uses elsewhere.

Cleared:  every table in the "orchestrator" schema (artifacts, schedules, runs, logs, workers, leases,
          callbacks, journal, telemetry, security events ...) and in the "sheets" schema
          (batches and their items).
Kept:     schema_migrations in both schemas (the services need it to start), and ms_tokens (the saved
          Microsoft sign-in, so the demo does not need "sheet-service login" again).
          The tables themselves stay; only their rows are removed.

Run from anywhere:
  powershell -File demo-tools\clear-demo-database.ps1            asks for confirmation
  powershell -File demo-tools\clear-demo-database.ps1 -Yes       no question
  powershell -File demo-tools\clear-demo-database.ps1 -WhatIf    only shows what would be removed
  -Keep name1,name2                                               also keep these tables (any schema)
  -Database name                                                  another database in the same container
  -NoServiceCheck                                                 skip the "services are running" check;
                                                                  only for a scratch database that no
                                                                  service is using

Stop the orchestrator and the sheet service first. The tool refuses to run while they are listening.
#>
param(
    [switch]$Yes,
    [switch]$WhatIf,
    [switch]$NoServiceCheck,
    [string[]]$Keep = @(),
    [string]$Container = 'tracto-postgres',
    [string]$Database = 'tracto',
    [string]$User = 'tracto'
)

$ErrorActionPreference = 'Continue'

# Tables that always stay.
$keepAlways = @('schema_migrations', 'ms_tokens')
$keepList = @($keepAlways + $Keep | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)
$keepSql = ($keepList | ForEach-Object { "'" + ($_ -replace "'", "''") + "'" }) -join ','

function Fail([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

function Invoke-Sql([string]$Sql) {
    $out = $Sql | docker exec -i $Container psql -U $User -d $Database -v ON_ERROR_STOP=1 -q -At -F '|'
    if ($LASTEXITCODE -ne 0) { Fail "The database command failed (exit $LASTEXITCODE)." }
    return $out
}

# --- Checks ---------------------------------------------------------------------------------------
docker info 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'Docker is not running.' }

docker exec $Container pg_isready -U $User -d $Database 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "The container '$Container' is not running or the database is not ready." }

if (-not $NoServiceCheck) {
    foreach ($svc in @(@{ Port = 5080; Name = 'orchestrator' }, @{ Port = 5090; Name = 'sheet service' })) {
        if (Get-NetTCPConnection -LocalPort $svc.Port -State Listen -ErrorAction SilentlyContinue) {
            Fail "The $($svc.Name) is running (port $($svc.Port)). Stop it first, then run this again."
        }
    }
}

# --- What is there --------------------------------------------------------------------------------
$countSql = @"
SELECT table_schema || '.' || table_name,
       (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text::int
FROM information_schema.tables
WHERE table_schema IN ('orchestrator', 'sheets') AND table_type = 'BASE TABLE'
ORDER BY 1;
"@

function Get-Counts {
    $rows = @()
    foreach ($line in @(Invoke-Sql $countSql)) {
        if (-not $line) { continue }
        $name, $n = $line -split '\|'
        $table = ($name -split '\.')[1].ToLowerInvariant()
        $rows += [pscustomobject]@{ Table = $name; Rows = [int]$n; Kept = ($keepList -contains $table) }
    }
    return $rows
}

$before = Get-Counts
if ($before.Count -eq 0) { Fail "No tables found in the 'orchestrator' or 'sheets' schema. Start the services once so they create them." }

Write-Host ''
Write-Host "Database '$Database' in container '$Container':" -ForegroundColor Cyan
foreach ($r in $before) {
    $note = if ($r.Kept) { 'kept' } else { 'cleared' }
    Write-Host ('  {0,-42} {1,8} rows   {2}' -f $r.Table, $r.Rows, $note)
}
$toClear = @($before | Where-Object { -not $_.Kept })
$rowsToClear = ($toClear | Measure-Object -Property Rows -Sum).Sum
Write-Host ''
Write-Host "$($toClear.Count) tables, $rowsToClear rows will be removed." -ForegroundColor Yellow

if ($WhatIf) { Write-Host 'Nothing was changed (-WhatIf).'; exit 0 }

if (-not $Yes) {
    $answer = Read-Host 'This cannot be undone. Type YES to clear them'
    if ($answer -cne 'YES') { Write-Host 'Cancelled. Nothing was changed.'; exit 2 }
}

# --- Clear ----------------------------------------------------------------------------------------
$clearSql = @"
DO `$`$
DECLARE t record;
BEGIN
  FOR t IN
    SELECT table_schema, table_name FROM information_schema.tables
    WHERE table_schema IN ('orchestrator', 'sheets') AND table_type = 'BASE TABLE'
      AND lower(table_name) NOT IN ($keepSql)
  LOOP
    EXECUTE format('TRUNCATE TABLE %I.%I RESTART IDENTITY CASCADE', t.table_schema, t.table_name);
  END LOOP;
END
`$`$;
"@
Invoke-Sql $clearSql | Out-Null

# --- Check ----------------------------------------------------------------------------------------
$after = Get-Counts
$left = @($after | Where-Object { -not $_.Kept -and $_.Rows -gt 0 })
$keptRows = ($after | Where-Object { $_.Kept } | Measure-Object -Property Rows -Sum).Sum
if ($left.Count -gt 0) {
    Write-Host 'Some tables still have rows:' -ForegroundColor Red
    $left | ForEach-Object { Write-Host ("  {0} {1}" -f $_.Table, $_.Rows) }
    exit 1
}
Write-Host "Database cleared: $($toClear.Count) tables are empty. Kept tables hold $keptRows rows." -ForegroundColor Green
exit 0
