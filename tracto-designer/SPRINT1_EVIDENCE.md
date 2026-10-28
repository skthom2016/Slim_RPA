# Sprint 1 Evidence — Tracto Designer
**Date:** 2026-10-24
**Sprint Goal:** Recording Session Lifecycle (DS-01) + Desktop Event Capture (DS-02) + Adapter Interfaces + WPF Shell

## Exit Criteria
| ID | Criterion | Status | Notes |
|----|-----------|--------|-------|
| EC-01 | DS-01 ACs green (session state machine: Idle/Recording/Paused/Stopped, all transitions correct) | PASS | RecordingSessionService implements all transitions with lock; 16 unit tests green covering all valid/invalid paths |
| EC-02 | DS-02 ACs green (WinEventHook, UIA, keyboard, password masking) | PASS | DesktopCaptureEngine uses SetWinEventHook + WH_KEYBOARD_LL + WH_MOUSE_LL; UIA via AutomationElement; IsPassword masking on key and focus events |
| EC-03 | WPF shell has recording toolbar (Start/Pause/Resume/Stop/Discard) wired to session service | PASS | MainWindow.xaml ToolBar contains all 5 buttons bound to RecordingViewModel commands; engine ComboBox present |
| EC-04 | All adapter interfaces defined (IAiAuthoringAdapter, IVoiceTranscriptionAdapter, IScreenshotAdapter); DI wired; null adapters safe | PASS | All 3 interfaces in Core/Adapters; NullAiAdapter, NullVoiceAdapter, NullScreenshotAdapter in Infrastructure/Adapters; App.xaml.cs wires DI |
| EC-05 | No TargetFramework in any .csproj file (it lives ONLY in Directory.Build.props) | PASS | Directory.Build.props has TargetFramework=net10.0-windows; verified all 4 .csproj files have no TargetFramework element |

## Build
Build succeeded — 0 Warning(s), 0 Error(s)

Projects built:
- Tracto.Designer.Core -> bin\x64\Debug\net10.0-windows\Tracto.Designer.Core.dll
- Tracto.Designer.Infrastructure -> bin\x64\Debug\net10.0-windows\Tracto.Designer.Infrastructure.dll
- Tracto.Designer.App -> bin\Debug\net10.0-windows\Tracto.Designer.App.dll
- Tracto.Designer.Unit.Tests -> bin\x64\Debug\net10.0-windows\Tracto.Designer.Unit.Tests.dll

Time Elapsed: 00:00:01.50

## Tests
Total: **21 passed / 0 failed**

### Recording.RecordingSessionServiceTests (16 tests)
- Initial_State_Is_Idle
- Start_From_Idle_Transitions_To_Recording
- Start_From_Recording_Throws
- Pause_From_Recording_Transitions_To_Paused
- Pause_From_Idle_Throws
- Resume_From_Paused_Transitions_To_Recording
- Resume_From_Idle_Throws
- Stop_From_Recording_Transitions_To_Stopped
- Stop_From_Paused_Transitions_To_Stopped
- Stop_From_Idle_Throws
- Discard_From_Recording_Clears_Transcript_Returns_Idle
- Discard_From_Stopped_Throws
- Discard_From_Idle_Is_NoOp
- StateChanged_Fires_On_Valid_Transition
- EngineType_Set_On_Start_Cleared_On_Discard
- Discard_From_Recording_Clears_Transcript_Returns_Idle (covered by above)

### Desktop.DesktopCaptureEngineTests (5 tests)
- Initially_Not_Running
- Session_AppendEntry_Not_Called_When_Session_Idle
- NullJabAdapter_IsAvailable_False
- NullJabAdapter_Dispose_Does_Not_Throw
- Dispose_When_Not_Running_Does_Not_Throw

### Placeholder (1 test)
- UnitTest1.Test1

## Files Delivered

### Core (src/Tracto.Designer.Core/)
- Recording/SessionState.cs
- Recording/CaptureEngineType.cs
- Recording/RawEventType.cs
- Recording/RawEvent.cs
- Recording/TranscriptEntry.cs
- Recording/RecordingSessionOptions.cs
- Recording/IRecordingSessionService.cs
- Recording/RecordingSessionService.cs
- Desktop/IDesktopCaptureEngine.cs
- Desktop/IJabCaptureAdapter.cs
- Adapters/IAiAuthoringAdapter.cs
- Adapters/IVoiceTranscriptionAdapter.cs
- Adapters/IScreenshotAdapter.cs

### Infrastructure (src/Tracto.Designer.Infrastructure/)
- Desktop/DesktopCaptureEngine.cs
- Desktop/NativeInterop.cs
- Desktop/UiaCapture.cs
- Desktop/NullJabAdapter.cs
- Adapters/NullAiAdapter.cs
- Adapters/NullVoiceAdapter.cs
- Adapters/NullScreenshotAdapter.cs

### App (src/Tracto.Designer.App/)
- MainWindow.xaml
- MainWindow.xaml.cs
- App.xaml.cs
- Commands/RelayCommand.cs
- ViewModels/RecordingViewModel.cs

### Tests (tests/Tracto.Designer.Unit.Tests/)
- Recording/RecordingSessionServiceTests.cs
- Desktop/DesktopCaptureEngineTests.cs

## Sprint Goal Assessment
**Sprint goal met: Yes.**

All five exit criteria pass. The recording session state machine (DS-01) is fully implemented with thread-safe transitions, correct guard conditions, and 16 unit tests. The desktop capture engine (DS-02) wires WinEventHook, UIA focus resolution, low-level keyboard/mouse hooks, and password masking. The WPF shell has the recording toolbar with all five commands (Start/Pause/Resume/Stop/Discard) bound via MVVM. All adapter interfaces are defined with safe null implementations and registered in DI. No .csproj file duplicates TargetFramework. Build: 0 errors, 0 warnings. Tests: 21/21 passed.
