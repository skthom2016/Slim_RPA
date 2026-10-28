# Sprint 2 Evidence — Tracto Designer

**Date:** 2026-10-24
**Sprint Goal:** Script Editor (DS-03) + Draft Generation (DS-04) + Screenshots (DS-05) + Voice Narration (DS-06)

## Exit Criteria

| ID | Criterion | Status | Notes |
|----|-----------|--------|-------|
| EC-01 | DS-03 Script Editor | PASS | `Script`/`ScriptStep` models with `INotifyPropertyChanged` + `ObservableCollection<ScriptStep>`; `ScriptEditorViewModel` with Add/Delete/MoveUp/MoveDown/Renumber commands; `ScriptEditorControl.xaml` UI |
| EC-02 | DS-04 Draft Gen | PASS | `IScriptDraftGenerator` interface + `ScriptDraftGenerator` implementation; deterministic transcript→Script conversion; action mapping (click/type/key/scroll/navigate/drag); password-field value suppression |
| EC-03 | DS-05 Screenshots | PASS | `WindowsScreenshotAdapter` — GDI BitBlt screen capture; UIA password masking; saves PNG to `<sessionRootDir>/<sessionId>/<stepNumber:D3>.png`; graceful if no WPF app |
| EC-04 | DS-06 Voice Narration | PASS | `WindowsVoiceAdapter` — System.Speech `DictationGrammar` offline STT; `IsAvailable` probe; `StartAsync` swallows missing-mic exceptions; async enumerable transcript stream |
| EC-05 | All tests pass | PASS | 44 passed / 0 failed (Sprint 1: 24 + Sprint 2: 20) |
| EC-06 | No TargetFramework in .csproj | PASS | Grep across all 5 .csproj files: 0 matches (framework set via `Directory.Build.props`) |

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:02.09
```

Projects built:
- `Tracto.Designer.Core` → net10.0-windows
- `Tracto.Designer.Infrastructure` → net10.0-windows
- `Tracto.Designer.App` → net10.0-windows
- `Tracto.Designer.Unit.Tests` → net10.0-windows
- `Tracto.Designer.Integration.Tests` → net10.0-windows

## Tests

Total: **44 passed / 0 failed**

Sprint 1 carried forward (24 tests):
- `RecordingSessionServiceTests` (11 tests): state machine transitions
- `DesktopCaptureEngineTests` (5 tests): capture engine lifecycle
- `VoiceAdapterTests` (4 tests — NullVoiceAdapter only): null-adapter contract
- `WindowsScreenshotAdapterTests` (4 tests — null adapter + availability): adapter contract

Sprint 2 new (20 tests):
- `ScriptEditorViewModelTests` (6 tests): AddStep, DeleteStep, MoveUp, MoveDown, Renumber, LoadScript
- `ScriptDraftGeneratorTests` (8 tests): EmptyTranscript, BrowserEngine, NarrationText, TextInput, PasswordField, KeyPress, Click, StepNumbers
- `VoiceAdapterTests` (2 new): WindowsVoiceAdapter.IsAvailable, StartAsync/StopAsync no-throw
- `WindowsScreenshotAdapterTests` (2 new): IsAvailable=true, CaptureAsync non-throwing

## New Files Delivered (Sprint 2)

**Core — Scripting:**
- `src/Tracto.Designer.Core/Scripting/Script.cs` — Script model (INotifyPropertyChanged, ObservableCollection<ScriptStep>)
- `src/Tracto.Designer.Core/Scripting/ScriptStep.cs` — ScriptStep model with StepNumber, Description, Action, Parameters
- `src/Tracto.Designer.Core/Scripting/IScriptDraftGenerator.cs` — interface
- `src/Tracto.Designer.Core/Scripting/ScriptDraftGenerator.cs` — deterministic transcript→Script implementation

**Core — Adapters:**
- `src/Tracto.Designer.Core/Adapters/IScreenshotAdapter.cs`
- `src/Tracto.Designer.Core/Adapters/IVoiceTranscriptionAdapter.cs`
- `src/Tracto.Designer.Core/Adapters/NullScreenshotAdapter.cs`
- `src/Tracto.Designer.Core/Adapters/NullVoiceTranscriptionAdapter.cs`

**Infrastructure — Adapters:**
- `src/Tracto.Designer.Infrastructure/Adapters/WindowsScreenshotAdapter.cs` — GDI capture + UIA password masking
- `src/Tracto.Designer.Infrastructure/Adapters/WindowsVoiceAdapter.cs` — System.Speech offline STT

**App — ViewModels / UI:**
- `src/Tracto.Designer.App/ViewModels/Scripting/ScriptEditorViewModel.cs` — commands: AddStep, DeleteStep, MoveUp, MoveDown
- `src/Tracto.Designer.App/Views/Scripting/ScriptEditorControl.xaml` — step list, toolbar buttons
- `src/Tracto.Designer.App/Views/Scripting/ScriptEditorControl.xaml.cs`
- `RecordingViewModel` updated: GenerateDraftCommand wired to ScriptDraftGenerator; voice/screenshot adapters injected

**Tests:**
- `tests/Tracto.Designer.Unit.Tests/Scripting/ScriptEditorViewModelTests.cs`
- `tests/Tracto.Designer.Unit.Tests/Scripting/ScriptDraftGeneratorTests.cs`
- `tests/Tracto.Designer.Unit.Tests/Adapters/WindowsScreenshotAdapterTests.cs` (extended)
- `tests/Tracto.Designer.Unit.Tests/Adapters/VoiceAdapterTests.cs` (extended)

## Sprint Goal Assessment

**Yes** — Sprint 2 goal fully met. All four feature stories (DS-03, DS-04, DS-05, DS-06) are implemented and tested. Build is clean (0 warnings, 0 errors). All 44 unit tests pass. No `TargetFramework` in any .csproj. The Designer is now capable of recording via voice narration, capturing screenshots with password masking, generating a structured Script from transcript data, and editing that script step-by-step in the UI.
