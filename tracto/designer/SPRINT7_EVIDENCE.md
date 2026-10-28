# Sprint 7 Exit Evidence

**Date**: 2026-10-24
**Sprint goal**: DS-15 Browser Capture Adapter + DS-16 Template Library

---

## Exit Criteria

| ID    | Criterion                                                                 | Status |
|-------|---------------------------------------------------------------------------|--------|
| EC-01 | DS-15 — IBrowserCaptureEngine + NullBrowserCaptureEngine + CdpBrowserCaptureEngine + BrowserCaptureAdapter + BrowserAdapterViewModel | PASS |
| EC-02 | DS-16 — ITemplateLibrary + BuiltInTemplateLibrary (8 templates, 6 categories) + ScriptTemplate.CreateScript deterministic + TemplateLibraryViewModel | PASS |
| EC-03 | All Sprint 1-7 tests pass (target: ~155+)                                | PASS — 164 passed, 0 failed |
| EC-04 | No TargetFramework in any .csproj                                         | PASS — grep found 0 matches |

---

## Test Results

```
Test Run Successful.
Total tests: 164
     Passed: 164
 Total time: 1.36 Seconds
```

---

## New Files Added (Sprint 7)

### DS-15 — Browser Capture (EC-01)

| File | Description |
|------|-------------|
| `src/Tracto.Designer.Core/Browser/IBrowserCaptureEngine.cs` | Interface: StartAsync, StopAsync, IsRunning, EventCaptured |
| `src/Tracto.Designer.Core/Browser/BrowserRawEvent.cs` | Raw event record (EventType, Target, Value, Timestamp, Url) |
| `src/Tracto.Designer.Core/Browser/BrowserCaptureOptions.cs` | Options: BrowserType, Port, IncludeClicks, IncludeKeystrokes, IncludeNavigation |
| `src/Tracto.Designer.Core/Browser/NullBrowserCaptureEngine.cs` | No-op implementation; IsRunning=false, event add/remove are no-ops |
| `src/Tracto.Designer.Core/Browser/CdpBrowserCaptureEngine.cs` | CDP implementation via System.Net.WebSockets (no external NuGet) |
| `src/Tracto.Designer.Core/Browser/BrowserCaptureAdapter.cs` | Maps BrowserRawEvents to ScriptSteps |
| `src/Tracto.Designer.ViewModels/Browser/BrowserAdapterViewModel.cs` | ViewModel: Chrome/Edge/Firefox connect/disconnect |

### DS-16 — Template Library (EC-02)

| File | Description |
|------|-------------|
| `src/Tracto.Designer.Core/Templates/TemplateCategory.cs` | Enum: Login, Navigation, DataEntry, Verification, FileOperation, General |
| `src/Tracto.Designer.Core/Templates/ScriptTemplate.cs` | Record: Id, Name, Description, Category, Tags, CreateScript Func<Script> |
| `src/Tracto.Designer.Core/Templates/ITemplateLibrary.cs` | Interface: GetAll, GetByCategory, Search |
| `src/Tracto.Designer.Core/Templates/BuiltInTemplateLibrary.cs` | 8 built-in templates across all 6 categories (deterministic static list) |
| `src/Tracto.Designer.ViewModels/Templates/TemplateLibraryViewModel.cs` | ViewModel: search, category filter, use template, confirmation dialog |

### DI Registrations

- `IBrowserCaptureEngine` → `NullBrowserCaptureEngine` (singleton)
- `BrowserCaptureAdapter` registered
- `ITemplateLibrary` → `BuiltInTemplateLibrary` (singleton)

---

## Template Coverage (EC-02)

| Template | Category |
|----------|----------|
| login-form | Login |
| web-navigation | Navigation |
| data-entry-form | DataEntry |
| search-and-verify | Verification |
| file-download | FileOperation |
| file-upload | FileOperation |
| table-extraction | DataEntry |
| logout | General |

All 6 categories covered; 8 templates total (target: 8+). PASS.

---

## Sprint Goal

**MET** — DS-15 Browser Capture Adapter and DS-16 Template Library fully implemented; 164/164 tests green; no TargetFramework in any .csproj.
