# Tracto — Quick Start & Restart Guide

## Kill Everything

```powershell
# Kill Orchestrator (port 5080)
$p = (Get-NetTCPConnection -LocalPort 5080 -State Listen -EA SilentlyContinue).OwningProcess
if ($p) { Stop-Process -Id $p -Force; Write-Host "Orchestrator killed" }

# Kill Orchestrator UI (port 4000)
$p = (Get-NetTCPConnection -LocalPort 4000 -State Listen -EA SilentlyContinue).OwningProcess
if ($p) { Stop-Process -Id $p -Force; Write-Host "Orchestrator UI killed" }

# Kill Hello World API (port 3000)
$p = (Get-NetTCPConnection -LocalPort 3000 -State Listen -EA SilentlyContinue).OwningProcess
if ($p) { Stop-Process -Id $p -Force; Write-Host "Hello World API killed" }
```

---

## Start Everything (4 separate terminals)

**Terminal 1 — Go Orchestrator**
```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-go
go run .
# Listens on http://localhost:5080
```

**Terminal 2 — Hello World API**
```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto
dotnet run --project hello-world/src/Tracto.HelloWorld.Api
# Listens on http://localhost:3000
```

**Terminal 3 — Orchestrator UI**
```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-ui
node server.js
# Open http://localhost:4000
```

**Terminal 4 — Desktop Runtime** *(start AFTER submitting a job from the UI)*
```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto
dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64
```

**Terminal 5 — Designer** *(optional, for recording/publishing scripts)*
```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto
dotnet run --project designer/src/Tracto.Designer.App -p:Platform=x64
```

---

## Run a Script (UI-driven, no terminal needed)

1. **Designer** → Import script → Sign → Publish *(uploads to Orchestrator)*
2. **Orchestrator UI** → Artifacts tab → **▶ Run** *(queues a job)*
3. **Desktop Runtime** picks up the job automatically and executes it

---

## Upload a Script Manually (if Designer publish fails)

```powershell
$content = Get-Content "C:\Users\santh\Downloads\notepad_hello_script_v2.json" -Raw
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($content))
$body = @{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/artifacts/notepad-hello-world-001/versions/1.0.3" -Method PUT -Body $body -ContentType "application/json"
```

---

## Health Checks

```powershell
Invoke-RestMethod "http://localhost:5080/health"   # Orchestrator
Invoke-RestMethod "http://localhost:3000/health"   # Hello World API
Invoke-RestMethod "http://localhost:4000"          # Orchestrator UI
```

---

## SQLite Lock / Orchestrator Hangs

If Orchestrator endpoints time out (UI shows no data), restart it:
```powershell
$p = (Get-NetTCPConnection -LocalPort 5080 -State Listen -EA SilentlyContinue).OwningProcess
if ($p) { Stop-Process -Id $p -Force }
# Then start Terminal 1 again
```
