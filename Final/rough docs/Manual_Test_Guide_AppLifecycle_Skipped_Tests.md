# Manual Test Guide — AppLifecycle Skipped Tests

**Document type:** Manual test execution guide  
**Date:** 2026-10-23  
**Applies to:** `Tracto.Desktop.AppLifecycle.Tests` — two tests skipped in CI  
**Test file:** `tracto-desktop-runtime\tests\Tracto.Desktop.AppLifecycle.Tests\AppLifecycleTests.cs`  
**Class under test:** `Tracto.Desktop.AppLifecycle.WindowsAppLifecycleManager`

---

## Why these tests were skipped

Both tests call `WindowsAppLifecycleManager` directly, which uses `System.Diagnostics.Process` to launch and attach to real Windows processes. This requires:

- An **interactive Windows desktop session** (not a CI service account or locked workstation)
- The **Windows interactive desktop** to be active (not logged out, not RDP disconnected)
- **Notepad.exe** to be present on the machine (ships with every Windows edition)

These conditions cannot be guaranteed in a headless CI environment, so both tests carry `[Fact(Skip = "Requires interactive Windows session")]`. They must be executed manually on a developer workstation.

---

## Pre-conditions (verify before starting)

| # | Pre-condition | How to verify |
|---|---|---|
| 1 | Logged into an interactive Windows desktop session | You can see the taskbar and Start menu |
| 2 | The desktop is not locked | Press any key to confirm you are not on the lock screen |
| 3 | No existing Notepad windows are open | Close any open Notepad windows before starting |
| 4 | .NET 10 SDK is installed | Run `dotnet --version` in a terminal — must be `10.x.x` |
| 5 | Solution builds cleanly | See Step 1 in each test below |

---

## Test 1 — LaunchAsync_RealProcess_ReturnsHandle

**Story:** DT-02-AC01  
**What it proves:** `WindowsAppLifecycleManager.LaunchAsync` can start a real GUI application, wait for it to become idle, and return a valid `AppHandle` — then close it cleanly.

### Step-by-step execution

**Step 1 — Build the solution**

Open a terminal (PowerShell or Command Prompt) in the solution root:

```
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto-desktop-runtime
dotnet build --configuration Release
```

Expected output: `Build succeeded` with 0 errors.

**Step 2 — Remove the Skip attribute temporarily**

Open this file in any text editor:

```
tests\Tracto.Desktop.AppLifecycle.Tests\AppLifecycleTests.cs
```

Find **line 99** (currently reads):

```csharp
[Fact(Skip = "Requires interactive Windows session")]
public async Task LaunchAsync_RealProcess_ReturnsHandle()
```

Change it to:

```csharp
[Fact]
public async Task LaunchAsync_RealProcess_ReturnsHandle()
```

Do **not** change any other line.

**Step 3 — Confirm no Notepad window is open**

Look at the Windows taskbar. Close every Notepad window if any are visible.

**Step 4 — Run only this test**

In the same terminal:

```
dotnet test tests\Tracto.Desktop.AppLifecycle.Tests ^
  --configuration Release ^
  --filter "FullyQualifiedName~LaunchAsync_RealProcess_ReturnsHandle" ^
  --logger "console;verbosity=detailed"
```

*(PowerShell users: replace `^` line continuation with a backtick `` ` ``)*

**Step 5 — Observe while the test runs**

- A blank Notepad window will appear on screen (created by the test).
- The test waits for Notepad to become idle (up to 10 seconds).
- Notepad closes automatically once the test completes.
- Total elapsed time: typically 2–4 seconds.

**Step 6 — Read the result**

The test output will print each assertion. Confirm all three pass:

```
✓ handle should not be <null>
✓ handle.ProcessId should be greater than 0
✓ handle.ProcessName should contain equivalent of "notepad"
```

**Expected console output (last lines):**

```
Passed LaunchAsync_RealProcess_ReturnsHandle [2.3s]

Test Run Successful.
Total tests: 1
     Passed: 1
     Failed: 0
```

**Step 7 — Restore the Skip attribute**

Undo the change made in Step 2 — put `[Fact(Skip = "Requires interactive Windows session")]` back on line 99.

### What a PASS looks like

| Assertion | Expected value | Meaning |
|---|---|---|
| `handle` is not null | Not null | LaunchAsync returned an object |
| `handle.ProcessId` | Any integer > 0 | A real process was started |
| `handle.ProcessName` | Contains "notepad" (case-insensitive) | The correct executable was launched |
| Notepad window closed | Window disappears from taskbar | `CloseAsync` worked correctly |

### What a FAIL looks like and how to diagnose

| Symptom | Likely cause | Fix |
|---|---|---|
| `DesktopActionError` with `desktop.launchFailed` | `notepad.exe` not found on PATH | Confirm `where notepad` in terminal returns a path |
| `DesktopActionError` with `desktop.launchTimeout` | Machine is very slow; Notepad did not idle within 10 s | Run again; if persistent, temporarily raise `LaunchTimeoutMs` to 30000 in the test |
| Test runner error about interactive desktop | Running inside a Windows Service account | Switch to a normal user account and run from the desktop |
| Notepad window remains open after test | Fixed in v1.1 of `WindowsAppLifecycleManager` — `Refresh()` + forced kill fallback added. If still seen, confirm you rebuilt after the fix. |

---

## Test 2 — AttachAsync_ByName_SingleProcess_ReturnsHandle

**Story:** DT-02 (AttachAsync by name)  
**What it proves:** `WindowsAppLifecycleManager.AttachAsync` can find a single already-running process by name and return a valid `AppHandle`.

> **Important:** This test requires **exactly one Notepad window** to be open before it runs. The recommended approach is to run Test 1 first (which launches and closes Notepad), then manually open one Notepad window, and then run this test.

### Step-by-step execution

**Step 1 — Build the solution** (skip if already done for Test 1)

```
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto-desktop-runtime
dotnet build --configuration Release
```

Expected: `Build succeeded` with 0 errors.

**Step 2 — Open exactly one Notepad window**

Press `Win + R`, type `notepad`, press Enter.  
Confirm: one Notepad window is visible in the taskbar. No other Notepad windows open.

**Step 3 — Remove the Skip attribute temporarily**

In the same test file (`AppLifecycleTests.cs`), find **line 114**:

```csharp
[Fact(Skip = "Requires interactive Windows session")]
public async Task AttachAsync_ByName_SingleProcess_ReturnsHandle()
```

Change it to:

```csharp
[Fact]
public async Task AttachAsync_ByName_SingleProcess_ReturnsHandle()
```

Do **not** change any other line.

**Step 4 — Run only this test**

```
dotnet test tests\Tracto.Desktop.AppLifecycle.Tests ^
  --configuration Release ^
  --filter "FullyQualifiedName~AttachAsync_ByName_SingleProcess_ReturnsHandle" ^
  --logger "console;verbosity=detailed"
```

**Step 5 — Observe**

- No new window is created (the test attaches to the existing Notepad).
- Notepad remains open after the test (the test only attaches; it does not close).
- Total elapsed time: under 1 second.

**Step 6 — Read the result**

```
✓ handle should not be <null>
```

**Expected console output (last lines):**

```
Passed AttachAsync_ByName_SingleProcess_ReturnsHandle [0.6s]

Test Run Successful.
Total tests: 1
     Passed: 1
     Failed: 0
```

**Step 7 — Close the Notepad window manually**

After the test finishes, close the Notepad window you opened in Step 2.

**Step 8 — Restore the Skip attribute**

Put `[Fact(Skip = "Requires interactive Windows session")]` back on line 114.

### What a PASS looks like

| Assertion | Expected value | Meaning |
|---|---|---|
| `handle` is not null | Not null | AttachAsync found the running Notepad and returned a handle |

### What a FAIL looks like and how to diagnose

| Symptom | Likely cause | Fix |
|---|---|---|
| `DesktopActionError` with `TARGET_NOT_FOUND` | Notepad was closed before the test ran | Re-open Notepad and re-run |
| `DesktopActionError` with `TARGET_AMBIGUOUS` | Two or more Notepad windows are open | Close all Notepad windows, open exactly one, and re-run |
| Test runner error about interactive desktop | Running as service account | Switch to normal user interactive session |

---

## Running both tests in sequence (recommended)

The cleanest way to execute both tests is in a single session:

1. Close all Notepad windows.
2. Remove both `Skip` attributes (lines 99 and 114).
3. Run Test 1 first (it opens and closes Notepad automatically).
4. Open one Notepad window manually.
5. Run Test 2.
6. Close Notepad.
7. Restore both `Skip` attributes.

Or run them together — but note that Test 2 will fail if Test 1's Notepad has not yet closed when Test 2 starts (two processes → TARGET_AMBIGUOUS). To avoid this, add a short delay or run them sequentially:

```
dotnet test tests\Tracto.Desktop.AppLifecycle.Tests ^
  --configuration Release ^
  --filter "FullyQualifiedName~LaunchAsync_RealProcess OR FullyQualifiedName~AttachAsync_ByName" ^
  --logger "console;verbosity=detailed"
```

If running together, verify Notepad is closed after Test 1 before Test 2 begins.

---

## Recording the result

After execution, record the outcome in the `CERTIFICATION.md` file at:

```
C:\Users\santh\Downloads\New-NeeliRPA\tracto-desktop-runtime\CERTIFICATION.md
```

Find the **Open Items** section and update it as follows:

**PASS:**
```
- [x] LaunchAsync_RealProcess_ReturnsHandle — PASSED manually on <date> by <tester name>
- [x] AttachAsync_ByName_SingleProcess_ReturnsHandle — PASSED manually on <date> by <tester name>
```

**FAIL:**
```
- [ ] <TestName> — FAILED on <date>; symptom: <exact error message>; investigated by <name>
```

Once both tests are marked PASS, the Desktop Runtime Sprint 8 certification is fully complete with 161/161 tests passed.

---

*End of guide*
