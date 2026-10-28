# Sprint 4 Evidence — Tracto Designer

**Date:** 2026-10-24
**Sprint Goal:** Runtime-based Testing (DS-09) + Script Signing (DS-10)

## Exit Criteria

| ID | Criterion | Status | Notes |
|----|-----------|--------|-------|
| EC-01 | DS-09 Runtime Testing | PASS | IScriptTestRunner + LocalSimulationTestRunner; validates before run; cancellable; TestPanelViewModel |
| EC-02 | DS-10 Script Signing | PASS | IScriptSigner + HmacScriptSigner (HMAC-SHA256); ScriptCanonicalizer (deterministic); Sign throws on invalid; SigningViewModel |
| EC-03 | All tests pass | PASS | 91/91 (S1+S2+S3: 76 + S4 Testing: 6 + S4 Signing: 9) |
| EC-04 | No TargetFramework in .csproj | PASS | All 5 .csproj files verified clean |

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:02.17
```

## Tests

Total: **91 passed / 0 failed**

- Sprint 1+2+3 (Core, MCP, Desktop, Scripting): 76
- Sprint 4 Testing (ScriptTestRunnerTests): 6
- Sprint 4 Signing (ScriptSigningTests): 9

All 91 tests green.

## New Files (Sprint 4)

### src/Tracto.Designer.Core/Testing/
- `IScriptTestRunner.cs`
- `LocalSimulationTestRunner.cs`
- `ScriptTestResult.cs`
- `StepTestResult.cs`

### src/Tracto.Designer.Core/Signing/
- `IScriptSigner.cs`
- `HmacScriptSigner.cs`
- `NullScriptSigner.cs`
- `ScriptCanonicalizer.cs`
- `ScriptSignature.cs`

### src/Tracto.Designer.App/ViewModels/
- `TestPanelViewModel.cs`
- `SigningViewModel.cs`

### tests/Tracto.Designer.Unit.Tests/
- `Testing/ScriptTestRunnerTests.cs`
- `Signing/ScriptSigningTests.cs`

## Sprint Goal Assessment

**Sprint Goal Met: YES**

DS-09 (Runtime-based Testing) and DS-10 (Script Signing) are fully implemented and verified.
All 91 tests across Sprints 1–4 pass. Build is clean with 0 warnings and 0 errors.
No TargetFramework entries in any .csproj (handled via Directory.Build.props).
