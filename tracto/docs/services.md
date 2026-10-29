# Tracto — Services Reference

## Start-Up Order

Always start services in this order. The Desktop Runtime must be last.

| # | Service | Command | Port |
|---|---------|---------|------|
| 1 | Go Orchestrator | `cd orchestrator-go && go run .` | 5080 |
| 2 | Orchestrator UI | `cd orchestrator-ui && node server.js` | 4000 |
| 3 | Hello World API | `dotnet run --project hello-world/src/Tracto.HelloWorld.Api` | 3000 |
| 4 | Desktop Runtime | `dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64` | — |
| 5 | Designer *(optional)* | `dotnet run --project designer/src/Tracto.Designer.App -p:Platform=x64` | — |

Open the dashboard at **http://localhost:4000** after step 2.

Full kill/restart commands: `tracto/README.md`

---

## Health Checks

```powershell
Invoke-RestMethod "http://localhost:5080/health"   # Orchestrator
Invoke-RestMethod "http://localhost:3000/health"   # Hello World API
# Browser: http://localhost:4000                   # Orchestrator UI
```

---

## Orchestrator REST API

Base URL: `http://localhost:5080`

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `GET` | `/health` | Health check |
| `PUT` | `/artifacts/{id}/versions/{ver}` | Upload a script |
| `GET` | `/artifacts` | List all uploaded scripts |
| `POST` | `/jobs` | Submit a job |
| `GET` | `/jobs/{runId}` | Get job status |
| `GET` | `/jobs/{runId}/telemetry` | Get per-step execution telemetry |
| `POST` | `/jobs/pull` | (Runtime use) Claim next pending job |

---

## Upload a Script Manually

Use this when the Designer's Publish button fails, or when uploading a hand-authored script.

```powershell
$content = Get-Content "path\to\script.json" -Raw
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($content))
$body = @{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json
Invoke-RestMethod `
    -Uri "http://localhost:5080/artifacts/<script-id>/versions/<version>" `
    -Method PUT `
    -Body $body `
    -ContentType "application/json"
```

Replace `<script-id>` with the script's `id` field and `<version>` with its `version` field.

---

## Submit a Job

```powershell
$body = @{
    schemaVersion    = "1"
    scriptArtifactId = "<script-id>"
    scriptVersion    = "<version>"
    triggerType      = "Manual"
} | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/jobs" -Method POST -Body $body -ContentType "application/json"
```

---

## Check Job Results

```powershell
# Overall status (Pending / Running / Completed / Failed)
Invoke-RestMethod "http://localhost:5080/jobs/<runId>"

# Step-by-step execution telemetry
Invoke-RestMethod "http://localhost:5080/jobs/<runId>/telemetry"
```

---

## Troubleshooting

**Orchestrator stops responding / endpoints time out**  
SQLite lock. Kill and restart:
```powershell
$p = (Get-NetTCPConnection -LocalPort 5080 -State Listen -EA SilentlyContinue).OwningProcess
if ($p) { Stop-Process -Id $p -Force }
# Then restart Terminal 1
```

**Desktop Runtime fails to connect on startup**  
The Orchestrator is not running yet. Start service #1 first.

**Job stays Pending and never runs**  
The Desktop Runtime is not started. Start service #4 after submitting the job.
