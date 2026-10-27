# Tracto Desktop Runtime — Sprint Plan v1.0

**Document ID:** TRACTO-DT-SP-001  
**Version:** 1.0 — 23 Oct 2026  
**Basis:** Desktop Runtime User Stories v1.1 (TRACTO-DT-US-001); Functional Specification v1.1; Architecture v1.2A (frozen)  
**Audience:** Desktop Runtime team, lead architect, QA lead, security reviewer  

---

Each sprint has a single outcome-focused goal whose pass/fail can be verified by anyone with access to the test machine and the CI pipeline. No sprint ends at an arbitrary code milestone. Parallel tracks within a sprint are explicitly noted.

**Estimated duration:** 8 sprints for a team of 3–4 engineers. A solo engineer on the critical path requires approximately 9–10 sprints.

**Critical path:** DT-01 → DT-02 → DT-05 → DT-07 → DT-15 → DT-16 → DT-18 → DT-19 → DT-20

---

## Pre-conditions before Sprint 1 starts

- Repository created; internal NuGet feed access confirmed
- Stage 1 NuGet packages (`Tracto.Contract.*`, `Tracto.Mock.Orchestrator`, `Tracto.Mock.BusinessService`) resolvable in the new project
- CI pipeline target confirmed (GitHub Actions or Azure DevOps); Windows-hosted runner available
- Dedicated Windows test machine with an active interactive logon session confirmed; cannot be a headless agent
- Java Access Bridge enabled on the test machine (required from Sprint 3 onward; confirm now)
- Architecture v1.2A confirmed frozen; no fourth component authorized

---

## Capability Groups (for cross-reference)

| Group | Stories | Theme |
|-------|---------|-------|
| A | DT-01, DT-02, DT-03, DT-04 | Session and Process Management |
| B | DT-05, DT-06, DT-07 | Element Addressing |
| C | DT-08, DT-09, DT-10 | Mouse, Keyboard and Clipboard Input |
| D | DT-11, DT-12, DT-13, DT-14 | Secrets and Control Surface |
| E | DT-15, DT-16, DT-17 | Recovery, Safety and Bounds |
| F | DT-18, DT-19 | Runtime Conformance and Observability |
| G | DT-20 | Certification |

---

## Sprint 1 — Session Boundary and IPC

**Goal:** The Desktop Runtime process starts on a Windows machine, refuses to accept any job from a locked, disconnected or noninteractive session, and establishes the Supervisor/Executor named-pipe channel with correct access controls. No GUI input is possible without passing both checks.

**Stories:** DT-01, DT-04  
**Capability group:** A (partial)  
**Parallel tracks:** DT-01 and DT-04 share a model dependency; DT-04 implementation begins once DT-01-AC01 passes. The IPC plumbing in DT-04 is staffable in parallel with the session pre-check test cases once the session-state model is stable.

### Pre-conditions before Sprint 1 starts

- Stage 1 NuGet packages referenced; `Tracto.Mock.Orchestrator` resolvable
- Windows CI runner with interactive session confirmed
- Named-pipe security model reviewed with security engineer (DT-04-AC04)

### Key Deliverables

- `SessionEligibilityChecker` — verifies logon state, active input desktop, process identity and lock state before any GUI input is permitted
- `InputLeaseManager` — exclusive claim gate; second concurrent request blocked until first releases
- Windows Runtime Host Supervisor stub — boot-time identity, named-pipe listener with correct DACL
- Interactive Executor stub — connects to Supervisor pipe, registers worker with mock Orchestrator
- `ISessionStateProvider` abstraction allowing deterministic unit testing without a real Windows session
- CI pipeline skeleton: build green on Windows runner; DT-01 and DT-04 unit tests passing

### Sprint Exit Criteria

- [ ] DT-01-AC01: locked session → no job admitted; clean `errorCategory=SESSION_UNAVAILABLE` returned
- [ ] DT-01-AC02: disconnected/noninteractive session → same result; evidence confirms no GUI input injected
- [ ] DT-01-AC03: display profile drift detected before coordinate action → `errorCategory=ENVIRONMENT, detailCode=desktop.displayProfileChanged`
- [ ] DT-01-AC04: concurrent second lock attempt → blocked until first release; no race condition
- [ ] DT-04-AC01: Supervisor boots without interactive login; Executor connects via named pipe in the interactive session
- [ ] DT-04-AC02: isolated-mode WSL2/Docker Orchestrator endpoint received from Supervisor; not overridable from Executor environment
- [ ] DT-04-AC03: Supervisor binary contains no `SendInput`/`mouse_event`/`keybd_event` call; test confirms zero GUI injection via Supervisor path
- [ ] DT-04-AC04: foreign process connecting to Supervisor pipe → OS access check refuses it
- [ ] All tests deterministic (no wall-clock sleep; mock session state injected via `ISessionStateProvider`)

---

## Sprint 2 — Application Lifecycle, Window Management, and Element Find

**Goal:** The Runtime can launch a Windows application, attach to a running one, detect and block unexpected modal popups, and locate any visible control by stable UIA properties. A selector survives window movement and control re-rendering without re-running the discovery step.

**Stories:** DT-02, DT-03, DT-05  
**Capability group:** A (completion), B (partial)  
**Prerequisite:** Sprint 1 complete.  
**Parallel tracks:** DT-03 (window/modal) and DT-05 (UIA element find) can be staffed concurrently once DT-02-AC01 passes. DT-02 itself is sequential after Sprint 1.

### Key Deliverables

- `AppLauncher` — verified launch via process start; verified attach by process name or PID; retry class NEVER for launch; `desktop.app.launch`, `desktop.app.attach`
- `ProcessExitMonitor` — detects unexpected exit; emits `errorCategory=ENVIRONMENT, detailCode=desktop.appCrash`
- `WindowEnumerator` — lists top-level windows by title, class and process; handles duplicate titles
- `ModalBlocker` — detects unexpected overlay before submission; blocks action; generates sanitized evidence
- `UiaElementFinder` — stable property query (AutomationId, ClassName, Name, ControlType, ancestry chain); no RuntimeId; scroll-to-element is internal behavior
- Stale re-resolution path: selector re-runs when cached element is no longer alive
- WPF and WinForms reference harness projects scaffolded (used from this sprint onward in all `[W]` tests)

### Sprint Exit Criteria

- [ ] DT-02-AC01: launch Notepad → process confirmed running; `desktop.app.launch` returns SUCCEEDED
- [ ] DT-02-AC02: attach by process name to a running WinForms app → correct window handle returned
- [ ] DT-02-AC03: targeted process exits unexpectedly → step returns `errorCategory=ENVIRONMENT, detailCode=desktop.appCrash`; run does not continue
- [ ] DT-02-AC04: `desktop.app.launch` retry class confirmed NEVER; second launch attempt rejected without explicit script step
- [ ] DT-03-AC01: modal popup appears before form submit → submission blocked; `errorCategory=TARGET_AMBIGUOUS` or environment error returned; evidence names the popup
- [ ] DT-03-AC02: two windows with identical titles → `errorCategory=TARGET_AMBIGUOUS` without action unless unique ancestry supplied
- [ ] DT-05-AC01: WPF button located by AutomationId; window moved; same selector finds same button without re-running discovery
- [ ] DT-05-AC02: two controls with the same AutomationId in different containers → `errorCategory=TARGET_AMBIGUOUS` unless scoped selector used
- [ ] DT-05-AC03: element found then removed from UIA tree → re-resolution returns `errorCategory=TARGET_NOT_FOUND`; no stale-handle use
- [ ] DT-05-AC04: absolute screen coordinate in selector → rejected by validator before execution; no UIA query issued
- [ ] WPF and WinForms harness projects build and run in CI

---

## Sprint 3 — Element State, Java Bridge, and Bounded Waits

**Goal:** Every action is gated by a verified element state check — disabled, stale and unsupported controls produce a single predictable diagnostic rather than a silent failure or an incorrect action. Java Swing application controls are addressable via Access Bridge for semantic actions and keyboard fallback. Bounded waits resolve on state change or return a defined timeout outcome.

**Stories:** DT-06, DT-07, DT-15  
**Capability group:** B (completion), E (partial)  
**Prerequisite:** Sprint 2 complete.  
**Parallel tracks:** DT-06 (Java Bridge) and DT-07 (element state) can be staffed concurrently — both depend only on DT-05. DT-15 (bounded waits) is sequential after DT-07 since its wait logic gates on element state.

### Key Deliverables

- `JavaAccessBridgeAdapter` — locates Java Swing controls by accessible name/role; semantic actions (click, type) via JAB; keyboard fallback for controls without coordinate permission; no coordinate input via JAB anchor alone
- `ElementStateChecker` — visibility (`IsOffscreen`), enabled (`IsEnabled`), unsupported-provider detection; result: single deterministic errorCategory per failure mode
- `ElementStateWaiter` — polls state with `FakeTimeProvider`-controlled timeout; resolves when state matches or returns `errorCategory=TIMEOUT` on expiry
- `UiaElementFinder` extended with `desktop.element.wait` action binding
- Java Swing reference harness project scaffolded (button, text field, tree node)

### Sprint Exit Criteria

- [ ] DT-07-AC01: button present in UIA tree but `IsEnabled=false` → `errorCategory=ENVIRONMENT, detailCode=desktop.elementDisabled`; no click sent; single predictable outcome (no `TARGET_NOT_FOUND` alternative)
- [ ] DT-07-AC02: element found, then scrolled out of viewport → `IsOffscreen=true` detected; step waits for visibility or returns `errorCategory=ENVIRONMENT` — not a silent miss
- [ ] DT-07-AC06: custom canvas control with no UIA provider → `errorCategory=ENVIRONMENT, detailCode=desktop.unsupportedControl`; no OCR fallback attempted
- [ ] DT-07-AC07: UIA element in elevated-integrity process → `errorCategory=POLICY_DENIED, detailCode=desktop.accessDenied`; no elevation bypass attempted
- [ ] DT-07-AC04: element not visible → becomes visible within 5 s → `desktop.element.wait` resolves; step SUCCEEDED
- [ ] DT-07-AC05: element never transitions to expected state within timeout → `errorCategory=TIMEOUT`; last-observed state in evidence
- [ ] DT-06-AC01: Java Swing button located by accessible name via JAB; click action succeeds; postcondition verifies state change
- [ ] DT-06-AC02: JAB anchor used for semantic action only; attempt to use JAB anchor for control-relative pointer coordinate → rejected; error confirms policy
- [ ] DT-15-AC01: `desktop.element.wait` with `state=visible` and 5 s timeout — element appears at 1 s → resolves without timeout
- [ ] DT-15-AC02: `desktop.element.wait` expires → `errorCategory=TIMEOUT`; last observed state in evidence; no retry beyond configured timeout
- [ ] DT-15-AC03: postcondition assertion failure after action → step is FAILED not SUCCEEDED; assertion outcome recorded in evidence
- [ ] All `[W]` tests run on the dedicated Windows machine with Java Access Bridge enabled; Java Swing harness build confirmed in CI

---

## Sprint 4 — Mouse, Keyboard, and Clipboard Input

**Goal:** The Runtime executes the full mouse input surface (verified click, drag, dwell hover, scroll), types text and shortcuts via all three approved strategies (ValuePattern, keystroke, clipboard-paste), and transfers clipboard content across process boundaries with contention handling. Every effectful input action correctly produces UNKNOWN with `unknownReason: action.commitUncertain` when the postcondition cannot be confirmed.

**Stories:** DT-08, DT-09, DT-10  
**Capability group:** C  
**Prerequisite:** Sprint 3 complete.  
**Parallel tracks:** DT-08 (mouse), DT-09 (keyboard) and DT-10 (clipboard) can be staffed concurrently — all depend only on DT-05 (done in Sprint 2). DT-11 (secrets) follows in Sprint 5 once DT-09 and DT-10 are both accepted.

### Key Deliverables

- `MouseInputEngine` — `desktop.mouse.click` (verified focus pre-check); `desktop.mouse.drag` (move/down/up); `desktop.mouse.move` with optional `dwellMs`; `desktop.mouse.scroll` (retry class NEVER); control-relative coordinates only
- `KeyboardInputEngine` — `desktop.keyboard.type` (ValuePattern preferred, keystroke fallback, clipboard-paste declared); `desktop.keyboard.press` (combinations); `desktop.keyboard.keyDownUp` with `holdMs` and cancellation-safe modifier release
- `ClipboardEngine` — `desktop.clipboard.copy` (copy from UI control into named ref); `desktop.clipboard.paste` (paste from ref into target control; retry class NEVER); contention retry bounded and TRANSIENT_SAFE; OS clipboard save/restore as internal policy-controlled behavior (not a public composition step)
- Focus verification before every keystroke (`UIA HasKeyboardFocus` check)
- Modifier-release guarantee on every exit path (cancellation, exception, timeout)
- UNKNOWN postcondition handling: paste and drag without confirmed postcondition → `unknownReason: action.commitUncertain`

### Sprint Exit Criteria

- [ ] DT-08-AC01: WPF button click — window moved after selector resolution → click lands on button at new position; SUCCEEDED
- [ ] DT-08-AC02: `desktop.mouse.scroll` confirmed retry class NEVER (not TRANSIENT_SAFE)
- [ ] DT-08-AC03: drag-and-drop cancelled mid-drag → mouse button released; desktop usable; step CANCELLED or UNKNOWN per observed side effect
- [ ] DT-08-AC04: `desktop.mouse.move` with `dwellMs=1500` → hover menu appears; evidence confirms dwell duration
- [ ] DT-09-AC01: WPF TextBox `ValuePattern` type → field matches exactly; strategy recorded as `ValuePattern` in evidence
- [ ] DT-09-AC02: Win32 Edit control → keystroke strategy; text matches; strategy recorded
- [ ] DT-09-AC03: Java text field → scancode/layout fallback; text matches; no `UnicodeChar` injection
- [ ] DT-09-AC04: `desktop.keyboard.keyDownUp` Shift held 800 ms then cancelled → Shift released; subsequent typing produces lowercase; no stuck modifier
- [ ] DT-09-AC05: typing step resolving from `secretRef` → no portion of plaintext in step result, evidence or log line
- [ ] DT-10-AC01: WinForms text field copy → `clipboardRef` holds exact text
- [ ] DT-10-AC02: paste into WPF field across processes → field matches; `desktop.clipboard.paste` retry class NEVER; uncertain paste → UNKNOWN with `unknownReason: action.commitUncertain`
- [ ] DT-10-AC03: competing clipboard handle held for 1 s → copy retries within bounded window; succeeds; does not fail on first attempt
- [ ] DT-10-AC04: policy=on → OS clipboard text captured before paste, restored after commit; test confirms prior clipboard text present after step; policy=off → no restore attempt
- [ ] All `[W]` tests confirmed on Windows machine; no `sleep` in any test (mock timers used for contention window)

---

## Sprint 5 — Secret Handling and Core Control Surface

**Goal:** DPAPI secret references are resolved and applied to controls without plaintext appearing in any log, telemetry entry, named pipe or evidence file. Text fields, checkboxes, radio groups, combo boxes and date/numeric/masked controls are settable and verifiable by accessible pattern. A correct but already-set control is not toggled again.

**Stories:** DT-11, DT-12  
**Capability group:** D (partial)  
**Prerequisite:** Sprint 4 complete (DT-09 and DT-10 both accepted before DT-11 begins).  
**Parallel tracks:** DT-11 (security) and DT-12 (controls) can be staffed concurrently once the Sprint 4 keyboard/clipboard models are stable. DT-12 only needs DT-05 and DT-07 (both done in Sprint 2/3).

### Key Deliverables

- `DpapiSecretResolver` — resolves `{"ref":"secrets.keyName"}` inside the interactive Windows session; byte-array cleared after use; no resolution outside session (Supervisor, pipe, network)
- `SecretApplicationBridge` — wires resolved value into `desktop.keyboard.type` and `desktop.clipboard.paste` paths; resolution never re-exposed after use
- Evidence-scan automation — known test-secret pattern confirmed absent from all logs, step results, Orchestrator messages and journal entries
- `ControlInteractionEngine` — `desktop.control.setValue` (TextBox via ValuePattern or keystroke); checkbox idempotent toggle; RadioGroup select; ComboBox select with `optionText` and index disambiguation; DateTimePicker via accessible value interface; masked-edit certified keyboard procedure
- `TARGET_AMBIGUOUS` guard in ComboBox/ListBox when no unique option identified

### Sprint Exit Criteria

- [ ] DT-11-AC01: DPAPI-protected blob resolved in active Windows session → plaintext to in-process buffer only; no file write or network call during resolution
- [ ] DT-11-AC02: script provides `{"ref":"secrets.passwordKey"}` for Password field → field receives correct value; login proceeds; no credential in step output or evidence
- [ ] DT-11-AC03: automated evidence scan against known test secret → zero occurrences in logs, step results, Orchestrator messages, journal entries
- [ ] DT-11-AC04: isolated deployment → plaintext never written to named pipe, WSL-mounted path or network socket accessible from Linux side
- [ ] DT-11-AC05: `secretRef` resolution attempted outside Windows interactive session → `errorCategory=POLICY_DENIED` or `ENVIRONMENT` returned; no resolution attempted
- [ ] DT-11-AC06: evidence screenshot containing password field → field region masked; screenshot withheld entirely if masking cannot be guaranteed
- [ ] DT-11-AC07: transient byte array cleared with zeroes after typing step completes (or fails/is cancelled)
- [ ] DT-12-AC01: WinForms TextBox set via ValuePattern → readback matches; strategy `ValuePattern` recorded
- [ ] DT-12-AC02: checkbox already in target state → no toggle event sent; state unchanged; step SUCCEEDED (idempotent)
- [ ] DT-12-AC03: checkbox not in target state → toggled once; readback confirms new state
- [ ] DT-12-AC04: RadioGroup selection → correct radio becomes checked; previously checked radio unchecked
- [ ] DT-12-AC05: ComboBox item selected by `optionText` → selection confirmed by accessible readback
- [ ] DT-12-AC06: two ComboBox items both labelled "Unknown", no index supplied → `errorCategory=TARGET_AMBIGUOUS`; no selection made
- [ ] DT-12-AC07: DateTimePicker set via accessible value interface → displayed date matches intended date on certified OS locale

---

## Sprint 6 — Advanced Controls, Recovery, and Cancellation

**Goal:** Lists, grids, trees, tab strips, menus and file dialogs are navigable and editable. Crash, session lock and focus theft produce defined outcomes rather than stuck or falsely succeeded runs. Job cancellation releases every held resource and leaves the interactive desktop in a clean, usable state.

**Stories:** DT-13, DT-14, DT-16, DT-17  
**Capability group:** D (completion), E (partial)  
**Prerequisite:** DT-12 accepted (for DT-14); DT-15 accepted (for DT-16); DT-03 accepted (for DT-16).  
**Parallel tracks:** DT-13 (grids/trees) and DT-14 (tabs/dialogs) can be staffed concurrently. DT-16 (crash/lock/theft) begins once DT-15 and DT-03 are both accepted. DT-17 (cancellation) is sequential after DT-16 since it depends on the uncertainty model DT-16 establishes.

### Key Deliverables

- `GridInteractionEngine` — `desktop.grid.selectRow` (content-based stable identity, survives sort); `desktop.grid.editCell` (enter edit mode, set value, confirm; not composable from read/select); scroll-to-realize via VirtualizedItemPattern with bounded realization timeout
- `TreeInteractionEngine` — `desktop.tree.expand` with `state: expanded|collapsed`; idempotent if already in target state
- `TabStripNavigator` — `desktop.control.select` targeting named tab in TabControl via UIA Selection pattern
- `MenuNavigator` / `FileDialogHandler` — menu path navigation; file picker type and confirm; confirm dialog interaction
- `CrashDetector` — `TerminateProcess` injection test; WER dialog detection; `errorCategory=ENVIRONMENT, detailCode=desktop.appCrash`
- `SessionLockHandler` — REC-008 internal bounded wait; heartbeats continue during lock; no GUI input while locked; deadline expiry → UNKNOWN or TIMED_OUT; fenced immediately on deadline
- `FocusTheftDetector` — foreground-change detection before pointer/keystroke; action withheld; evidence names stealing window
- `CancellationEngine` — modifier key release, mouse button release, clipboard handle close, input lease release, process handle release; all on every cancellation/timeout/exception path

### Sprint Exit Criteria

- [ ] DT-13-AC01: DataGrid row selected by cell content → correct row selected; readback matches
- [ ] DT-13-AC02: DataGrid sorted between selection and action → content-based identity finds correct row after sort
- [ ] DT-13-AC06: DataGrid row outside viewport → VirtualizedItemPattern scroll realizes row within bounded timeout; if realization impossible within timeout → `errorCategory=TIMEOUT` or `errorCategory=ENVIRONMENT, detailCode=desktop.unsupportedControl` returned; timeout not suppressed
- [ ] DT-13-AC04: WPF TreeView collapsed node → `desktop.tree.expand(state=expanded)` → child nodes accessible; idempotent if already expanded
- [ ] DT-13-AC08: read-only DataGrid cell → `desktop.grid.editCell` → `errorCategory=ENVIRONMENT`; no edit mode entered; value unchanged
- [ ] DT-14-AC01: tab selected by name → correct tab active; `desktop.control.select` confirmed for tab strips
- [ ] DT-14-AC02: file picker opened, valid path typed and confirmed → target field shows selected path
- [ ] DT-14-AC03: confirm dialog → approved dismiss → dialog closed; step SUCCEEDED; unapproved dismiss → step FAILED
- [ ] DT-16-AC01: `TerminateProcess` injected during script → crash detected; `errorCategory=ENVIRONMENT, detailCode=desktop.appCrash`; subsequent steps do not execute
- [ ] DT-16-AC03: session locked during job within 300 s policy limit → no GUI input injected; heartbeats continue; on unlock, job resumes or exits UNKNOWN — not SUCCEEDED
- [ ] DT-16-AC04: session lock exceeds policy deadline → run terminated; `errorCategory=TIMEOUT`; lease released
- [ ] DT-16-AC05: foreground window stolen by external process before click → click withheld; evidence names stealing window; step FAILED
- [ ] DT-16-AC06: consequential step produces UNKNOWN with `needsAttention=true` → run terminates immediately; no subsequent step executes; Orchestrator receives single final UNKNOWN result
- [ ] DT-17-AC01: Ctrl+Shift held during cancelled action → both modifier keys released; OS key-state query confirms unpressed
- [ ] DT-17-AC02: drag cancelled mid-operation → left mouse button released; no drag cursor persists
- [ ] DT-17-AC03: clipboard open during timeout → CloseClipboard called; subsequent clipboard operation from another process succeeds
- [ ] DT-17-AC04: input lease released after cancellation → new job can acquire lease without stuck-lease error

---

## Sprint 7 — Protocol Conformance and Observability

**Goal:** The Desktop Runtime is a fully conformant CEC v1.0.1 worker: registration, leasing, heartbeat, reconciliation, service invocation, async handoff, cancellation acknowledgement and capability matching are all mock-verified. Telemetry is emitted for every step result without exposing any secret, control plaintext or sensitive evidence inline.

**Stories:** DT-18, DT-19  
**Capability group:** F  
**Prerequisite:** DT-16 accepted (UNKNOWN/fence model established); DT-04 accepted (IPC established); DT-01 accepted (session model). Security review of DT-04 and DT-11 must be complete before DT-18 is accepted.  
**Parallel tracks:** DT-18 (protocol) and DT-19 (telemetry/observability) can be staffed concurrently after Sprint 6 — DT-19 depends on DT-18's result envelope structure but shares no implementation dependency that blocks parallel start.

### Key Deliverables

- `OrchestratorProtocolClient` — `RegisterWorker`, `PullJob` / `AcceptLease`, `ProcessHeartbeat`, `ProcessFinalResult`, `Reconcile`; all using `Tracto.Mock.Orchestrator` NuGet
- `JobDispatcher` — `PullJobResponse.JobOffered(LeaseGranted)` → `AcceptLease` → execution; `PullJobResponse.NoPendingJob(NoJobAvailable)` → idle poll; cancellation acknowledgement within configured deadline
- `ServiceInvokeRouter` — `service.invoke` to registered endpoints only; all five `serviceOutcome` values handled; `HANDED_OFF` → run state HANDED_OFF, lease released; no direct third-party API calls
- Schema/policy validation confirmed before first side effect; signed-artifact trust reused from Stage 1 NuGet (not rebuilt)
- `TelemetryEmitter` — structured step record: `stepId`, `actionType`, `selectorStrategy`, `durationMs`, `resultCategory`, `errorCategory`, `unknownReason`, `evidenceRef`; no inline secret, credential or sensitive control value
- `EvidenceRecorder` — screenshot with sensitive-region masking; screenshot withheld entirely if masking cannot be guaranteed; `evidenceRef=null` + `screenshotOmittedReason` recorded when withheld
- `SelectorDiagnosticBuilder` — strategy, scope, candidate count, sanitized window title; no `secretRef`-sourced parameter value in diagnostic
- Journal entries for UNKNOWN steps: last confirmed action, `unknownReason`, `attemptId`, timestamp; persists across Runtime restart

### Sprint Exit Criteria

- [ ] DT-18-AC01: `RegisterWorker` with `runtimeType=desktop` → accepted; `capabilityDigest` treated as integrity hint only, not authentication proof
- [ ] DT-18-AC02: `PullJobResponse.JobOffered(LeaseGranted)` → `AcceptLease` called; `leaseId` and `attemptId` recorded; execution begins. `PullJobResponse.NoPendingJob(NoJobAvailable)` → idle poll with configured interval; no error
- [ ] DT-18-AC03: periodic heartbeats within interval → mock Orchestrator records without expiry; gap beyond timeout → Orchestrator marks lease expired
- [ ] DT-18-AC04: `ProcessFinalResult` with UNKNOWN state → `unknownReason` and `needsAttention=true` present; mock Orchestrator accepts; lease released
- [ ] DT-18-AC05: network disconnect during job → reconnect → `Reconcile` called with `attemptId`; no duplicate result submitted
- [ ] DT-18-AC06: duplicate lease offer for already-finalized `attemptId` → no re-execution of any side-effectful action
- [ ] DT-18-AC07: Orchestrator cancellation → current action cancelled; release sequence runs; CANCELLED or UNKNOWN result submitted within cancellation timeout
- [ ] DT-18-AC08: job containing `browser.navigate` → rejected; worker does not attempt browser action
- [ ] DT-18-AC09: `service.invoke` → synchronous result routed to `serviceOutcome`; `businessOutcome` populated; no direct third-party API call
- [ ] DT-18-AC10: acknowledged async handoff → run state HANDED_OFF; lease released; worker does not poll for downstream result
- [ ] DT-18-AC11: schema/policy validation failure → FAILED returned before any side effect executes; zero actions performed
- [ ] DT-18-AC12: `service.invoke` to unregistered URL → rejected at validation; never reaches network layer
- [ ] DT-19-AC01: completed step telemetry record contains all required fields; no inline secret or control plaintext
- [ ] DT-19-AC02: password field in screenshot → field region masked; image file referenced by URI, not embedded inline
- [ ] DT-19-AC03: masking unsafe → screenshot withheld; `evidenceRef=null` + `screenshotOmittedReason` recorded
- [ ] DT-19-AC04: selector diagnostic after failure → no `secretRef`-sourced parameter in diagnostic; strategy, scope and candidate count present
- [ ] DT-19-AC05: UNKNOWN step → journal entry survives Runtime restart; `attemptId`, `unknownReason` and timestamp present
- [ ] Integration tests use `Tracto.Mock.Orchestrator` and `Tracto.Mock.BusinessService` NuGet packages (not rebuilt)
- [ ] Automated evidence scan: zero occurrences of known test-secret pattern across all telemetry, logs and journal entries

---

## Sprint 8 — POC Gate Certification

**Goal:** All 23 application certification scenarios (DES-AT-01..DES-AT-23) are executed as defined in Spec §9/§13 and pass on the reference application matrix with recorded evidence. POC-01..POC-09 architecture deployment gates are satisfied against their Architecture v1.2A acceptance conditions. The unconditional implementation freeze is declared.

**Story:** DT-20  
**Capability group:** G  
**Prerequisite:** All 19 previous stories (DT-01..DT-19) accepted. Security review complete. Real Windows test infrastructure confirmed for POC gates requiring it.

**Note:** POC-10 (vault-backed connected unattended session) may remain an open conditional release gate per Architecture v1.2A when connected unattended provisioning is not in the initial release scope. Its completion is tracked separately and does not block the unconditional freeze when POC-01..POC-09 are all satisfied.

### Key Deliverables

- Evidence package for each DES-AT-01..DES-AT-23: step journal (all step IDs, outcomes, `errorCategory`/`detailCode` where applicable) and sanitized evidence (masked screenshot where safe; sanitized non-image evidence where screenshot would expose unmasked sensitive content)
- Each scenario confirmed repeatable across three successive isolated runs with no setup change between runs
- POC-01..POC-09 gate evidence per Architecture v1.2A: real infrastructure components used for gates requiring durable reconciliation, DPAPI post-reboot, WSL/Docker startup, or vault-backed provisioning; mock Orchestrator only where Architecture v1.2A explicitly permits it
- Automated evidence scan report: zero occurrences of any credential plaintext pattern in any log, telemetry or evidence file across all 23 scenarios
- Unconditional freeze sign-off document: signed by Lead Architect; references this sprint's evidence package and confirms all 27 DT-20 ACs passed

### Sprint Exit Criteria — Unconditional Freeze when All Pass

- [ ] DT-20-AC01..DT-20-AC23: all 23 DES-AT application certification scenarios pass with evidence per each AC definition (see user stories v1.1 for full Given/When/Then per scenario)
- [ ] DT-20-AC24: each DES-AT-01..DES-AT-23 is repeatable across three successive isolated runs; identical pass/fail evidence each run
- [ ] DT-20-AC25: POC-01..POC-09 gates each pass against Architecture v1.2A acceptance conditions; real infrastructure used where required; mock not substituted for a gate that requires deployment evidence
- [ ] DT-20-AC27: unconditional freeze declared — all 23 DES-AT scenarios passing and all POC-01..POC-09 gates satisfied; no gate substituted or deferred
- [ ] Automated evidence scan: zero credential-plaintext occurrences across all scenario logs and evidence files (DES-AT-08, DES-AT-09, DES-AT-19 especially)
- [ ] All 20 stories accepted; all ~165 ACs have automated evidence or explicit human-review record in the traceability appendix

**Not required for freeze:** POC-10 (conditional gate), Browser Runtime, Terminal Runtime, Designer, AI Recorder, production Orchestrator, real vault (except as required by POC gates), any per-Business-Service team conformance report.

---

## Approval Gates

The following decisions or confirmations must be obtained before the named sprint work begins. Implementation must not start before the gate is cleared.

| Gate | Required before | Sprint |
|------|----------------|--------|
| Named-pipe security model reviewed with security engineer | DT-04 implementation | Sprint 1 |
| Java Access Bridge confirmed enabled on test machine | DT-06 implementation | Sprint 3 |
| DPAPI secret resolution design reviewed by security engineer | DT-11 implementation | Sprint 5 |
| Security review of DT-04 and DT-11 complete | DT-18 acceptance | Sprint 7 |
| Real infrastructure plan confirmed for POC-01..POC-09 gates | DT-20 begins | Sprint 8 |
| Architecture v1.2A conditional-gate status for POC-10 confirmed | DT-20-AC26 scope | Sprint 8 |

---

## Story-to-Sprint Mapping

| Story | Sprint | Parallel with |
|-------|--------|---------------|
| DT-01 | 1 | DT-04 (after DT-01-AC01 passes) |
| DT-04 | 1 | DT-01 (IPC plumbing) |
| DT-02 | 2 | — (sequential after Sprint 1) |
| DT-03 | 2 | DT-05 |
| DT-05 | 2 | DT-03 |
| DT-06 | 3 | DT-07 |
| DT-07 | 3 | DT-06 |
| DT-15 | 3 | Sequential after DT-07 |
| DT-08 | 4 | DT-09, DT-10 |
| DT-09 | 4 | DT-08, DT-10 |
| DT-10 | 4 | DT-08, DT-09 |
| DT-11 | 5 | DT-12 |
| DT-12 | 5 | DT-11 |
| DT-13 | 6 | DT-14 |
| DT-14 | 6 | DT-13 |
| DT-16 | 6 | DT-13, DT-14 (sequential within sprint after DT-15, DT-03) |
| DT-17 | 6 | Sequential after DT-16 |
| DT-18 | 7 | DT-19 |
| DT-19 | 7 | DT-18 |
| DT-20 | 8 | — |
