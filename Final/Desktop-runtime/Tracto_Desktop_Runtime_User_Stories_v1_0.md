# Tracto Desktop Runtime — Functional User Stories and Testable Acceptance Criteria

**Document ID:** TRACTO-DT-US-001  
**Version:** 1.1 — corrections applied per architect review (23 Oct 2026)  
**Architecture:** Tracto v1.2A — frozen; no architectural changes authorized  
**Basis:** Tracto Desktop Runtime Functional Specification v1.1 (23 October 2026); Common Execution & Orchestrator Interface Contract v1.0.1  
**Audience:** Lead architect, Desktop Runtime team, QA and security reviewers, Orchestrator integration team  
**Status:** Functional stories ready for sprint planning; POC-01–POC-10 gates remain the conditional-freeze precondition

---

## 1. Desktop Runtime objective and non-negotiable boundaries

The Desktop Runtime reproduces authorized human mouse, keyboard, clipboard and control interactions with Windows applications using deterministic scripts. It is one independently delivered .NET application in the Tracto Runtime family; it has no awareness of Browser or Terminal Runtime internals.

**Immovable constraints for every story:**

- **UIA-first targeting:** Windows UI Automation is the primary element-addressing mechanism. Java Access Bridge addresses supported Java applications for semantic actions and keyboard fallback only — it does not authorize coordinate-based input. Arbitrary absolute screen coordinates are not permitted in any story.
- **No OCR/image recognition in v1:** pixel-only, screenshot-confidence or image-template selectors are unsupported; validation rejects them before any side effect.
- **Control-relative coordinates only:** pointer offsets are permitted only relative to a uniquely UIA-located control with a verified bounding rectangle. A Java Access Bridge anchor alone is insufficient.
- **No UAC bypass or elevated desktop interaction in v1:** the executor runs at standard Windows integrity; elevated application UI, the Consent desktop and protected windows are unsupported unless separately certified outside this baseline.
- **No OCR/image/domain computation:** the Runtime performs no business arithmetic, no relational domain comparisons and no dynamic expression evaluation beyond the CEC-approved technical condition grammar.
- **Deterministic execution:** published scripts execute without LLM interpretation at run time. Every action is bounded, observable and has a postcondition or explicit uncertainty declaration.
- **CEC v1.0.1 protocol:** all wire messages, run states, step outcomes, errorCategory values and unknownReason codes are those of the Common Execution & Orchestrator Interface Contract v1.0.1. Desktop-specific error identifiers map to contract errorCategory values as defined in Spec Appendix A. `UNKNOWN_OUTCOME` is not a valid error code; the correct pattern is step/run state UNKNOWN with `unknownReason`.
- **Supervisor/Executor split:** the Windows Runtime Host Supervisor owns boot-time identity and WSL/Orchestrator connectivity. The interactive Executor owns the Windows desktop session, job pull, heartbeat and GUI input. They communicate only via authorized local named-pipe IPC. The Supervisor cannot inject GUI events.
- **DPAPI secret boundary:** `secretRef` values are resolved inside the Windows interactive session only. No secret crosses to Linux/WSL containers, Orchestrator messages or log files.
- **Mock Orchestrator conformance:** implementation testing for Orchestrator-protocol stories uses `Tracto.Mock.Orchestrator` (Stage 1 NuGet package). Real Orchestrator testing is a release-gate requirement, not a sprint-acceptance criterion.
- **Scope of this backlog:** deliver a functional Desktop Runtime capable of repeatable, selector-based Windows application automation. Browser/Terminal actions, Orchestrator scheduling, Designer UI generation and business-service logic belong to separate tracks.

---

## 2. Story sizing and definition of done

Each story represents an implementable capability: one session/environment boundary, one element-addressing mechanism, one input or control-family surface, one recovery class or one conformance/certification gate — rather than one API method or one framework file.

For a Desktop Runtime story to be accepted:
(a) The normative behavior is documented in Functional Spec v1.1 §§ cited in the story's **Traceability** line.  
(b) A working .NET implementation and automated tests exist for all acceptance criteria below.  
(c) Positive, negative and boundary cases pass on the required acceptance fixture application.  
(d) Error outputs are stable, error codes map to CEC v1.0.1 `errorCategory` and sensitive data is redacted from evidence.  
(e) Any irreversible action that lacks a verified postcondition produces UNKNOWN with the correct `unknownReason`, never a blind retry or false SUCCEEDED.  
(f) At least one tested POC scenario on the relevant reference application is recorded and reproducible.

**Test labels:** `U` = deterministic unit/schema/model test; `C` = CEC contract conformance test against mock Orchestrator; `W` = Windows UI/integration test using reference application fixture; `S` = security/policy/secret boundary test; `R` = compatibility/regression test across supported fixture apps.

Every criterion carries a persistent AC identifier suitable for linking to automated tests.

---

## 3. Story catalogue and recommended build order

| Seq | Story ID | Capability | Primary role | Depends on |
|-----|----------|-----------|--------------|-----------|
| 1 | DT-01 | Validate interactive session eligibility and input ownership | Desktop engineer | None |
| 2 | DT-02 | Launch, attach and manage application lifecycle | Desktop engineer | DT-01 |
| 3 | DT-03 | Enumerate windows, control state and unexpected modal handling | Desktop engineer | DT-02 |
| 4 | DT-04 | Supervisor/Executor IPC and session isolation | Platform engineer | DT-01 |
| 5 | DT-05 | Locate elements by stable UIA properties and ancestry | Desktop engineer | DT-02 |
| 6 | DT-06 | Java Access Bridge adapter and keyboard-fallback targeting | Desktop engineer | DT-05 |
| 7 | DT-07 | Verify element state, handle stale and unsupported controls | Desktop engineer | DT-05 |
| 8 | DT-08 | Execute verified mouse movement, click, drag and scroll | Desktop engineer | DT-05 |
| 9 | DT-09 | Enter text, keyboard shortcuts and hold-key sequences | Desktop engineer | DT-05 |
| 10 | DT-10 | Clipboard read, write, contention recovery and restore pattern | Desktop engineer | DT-05 |
| 11 | DT-11 | Resolve and apply secretRef values without secret leakage | Security engineer | DT-09, DT-10 |
| 12 | DT-12 | Set and verify text fields, checkboxes, radio and value controls | Desktop engineer | DT-05, DT-07 |
| 13 | DT-13 | Navigate and edit lists, grids and tree hierarchies | Desktop engineer | DT-07, DT-08 |
| 14 | DT-14 | Select tabs, navigate menus and handle file/confirm dialogs | Desktop engineer | DT-07, DT-12 |
| 15 | DT-15 | Bounded technical waits, postcondition assertions and prechecks | Desktop engineer | DT-05, DT-07 |
| 16 | DT-16 | Detect and handle crash, session lock, focus theft and uncertainty | Desktop engineer | DT-15, DT-03 |
| 17 | DT-17 | Cancel safely and release all held input and session resources | Desktop engineer | DT-08, DT-09, DT-16 |
| 18 | DT-18 | CEC v1.0.1 Orchestrator protocol conformance | Orchestrator engineer | DT-01, DT-04, DT-16 |
| 19 | DT-19 | Emit safe telemetry, evidence references and sanitized diagnostics | Support engineer | DT-15, DT-16, DT-18 |
| 20 | DT-20 | POC gate certification across reference application matrix | QA lead | DT-01–DT-19 |

Stories DT-01–DT-04 establish the session and deployment foundation; DT-05–DT-07 build the element-addressing layer that all input stories depend on; DT-08–DT-14 cover the complete input/control surface; DT-15–DT-17 add recovery and safety guarantees; DT-18–DT-19 wire the Orchestrator protocol and observability; DT-20 certifies the full stack. Security review can begin as early as DT-04 and DT-11 are drafted; it must complete before DT-18 is accepted.

---

# Capability Group A — Session and Process Management

## DT-01 — Validate interactive session eligibility and input ownership

**User story:** As a **Desktop Engineer**, I want the Runtime to verify that an eligible interactive Windows session and exclusive input lease exist before any GUI input, so that automation never injects keystrokes or clicks into a locked, noninteractive or already-claimed session.

**Functional scope:** Verify active interactive logon, input desktop identity (not the secure/consent desktop), process integrity level, session lock state and session class before any mouse/keyboard/clipboard operation. Acquire an exclusive per-session input lease. Validate that the display profile (resolution, DPI) matches the certified environment baseline before control-relative coordinate actions. Re-verify foreground ownership before each input phase. Detect human physical input or foreground theft during unattended execution.

**Acceptance criteria**

- **DT-01-AC01 [W] Session eligibility pass.** Given an active interactive Windows logon in a standard-integrity session at the certified display profile, when the Runtime initialises, then it acquires an input lease and proceeds to application launch/attach.
- **DT-01-AC02 [W] Locked/disconnected session.** Given a locked, logged-off, disconnected or noninteractive WinStation session, when the Runtime attempts to begin GUI input, then it returns `errorCategory=SESSION_UNAVAILABLE`, `detailCode=desktop.sessionUnavailable`, with no click or keystroke injected. The run state is FAILED (or UNKNOWN if a consequential action had already begun — per REC-008 / C3 rules).
- **DT-01-AC03 [W] Display profile mismatch.** Given a resolution or DPI value that differs from the certified environment baseline, when a coordinate-dependent action is requested, then the action is blocked with `errorCategory=ENVIRONMENT`, `detailCode=desktop.displayProfileChanged`, and no mouse movement occurs. UIA-semantic-only actions may proceed according to explicit policy.
- **DT-01-AC04 [W] Exclusive input lease.** Given two worker processes targeting the same interactive desktop session, when both attempt to acquire the input lease, then exactly one succeeds; the second receives a controlled failure and does not interleave input.
- **DT-01-AC05 [W] Foreground re-verification.** Given a foreground application change between two steps (e.g. a toast notification taking focus), when the next input action pre-checks foreground ownership, then it detects the discrepancy, halts input and produces evidence identifying the unexpected window.
- **DT-01-AC06 [W] Human interference detection.** Given physical mouse movement or keyboard input from a local user during an unattended session, when the Runtime detects the intrusion per configured policy, then it applies the declared policy (pause/fail) and does not intermix bot and human keystrokes.
- **DT-01-AC07 [S] Secure/consent desktop rejection.** Given the Windows session is at the UAC consent prompt or secure desktop, when the executor checks the current input desktop name, then it refuses to inject any input and returns an appropriate environment error.
- **DT-01-AC08 [R] Lease release on all exit paths.** Given the Runtime exits normally, crashes or is cancelled, when a new Run begins in the same session, then the input lease is available; no orphaned lease prevents the next execution.

**Tests and delivery evidence:** WinStation and desktop API unit tests with mocked Win32 session state; integration test with injected display-profile mismatch; lease contention test with two concurrent workers; confirmation that zero keystrokes appear in a locked-session scenario.

**Traceability:** DES-001, DES-002, DES-003, DES-009; Spec §4, §8 REC-003, REC-008; CEC-HOST-001.

---

## DT-02 — Launch, attach and manage application lifecycle

**User story:** As a **Desktop Engineer**, I want the Runtime to launch and attach to Windows applications from a policy allowlist, so that automation can open target processes and react to unexpected exit, crash or freeze.

**Functional scope:** Launch a policy-allowlisted executable or shortcut with arguments; attach using process identity and window ownership with disambiguation when multiple matching processes exist. Detect unexpected process exit, crash, hang and owned child-process changes. Apply configured close/relaunch policy without duplicating committed side effects.

**Acceptance criteria**

- **DT-02-AC01 [W] Allowlisted launch.** Given an approved executable path or shortcut with arguments, when `desktop.app.launch` executes, then a unique process is created, its PID and main-window reference are returned, and the postcondition verifies process/window existence. Retry class is NEVER — uncertain launch yields step state UNKNOWN with `unknownReason: action.commitUncertain`.
- **DT-02-AC02 [S] Unauthorized executable denied.** Given an executable path or command not on the policy allowlist, when validation occurs, then `errorCategory=POLICY_DENIED` is returned before any process is started.
- **DT-02-AC03 [W] Unambiguous attach.** Given a running process matching a provided identity and window ownership discriminator, when `desktop.app.attach` executes, then it resolves exactly one process/window pair. Given two processes with identical window title but different class or PID, then without explicit disambiguation the result is `errorCategory=TARGET_AMBIGUOUS` (no attachment).
- **DT-02-AC04 [W] Unexpected exit before postcondition.** Given an application crash or forced kill occurring after an action begins but before its postcondition is confirmed, when the Runtime detects the exit, then the step result is UNKNOWN with `unknownReason: action.commitUncertain` and `needsAttention=true`; SUCCEEDED is never returned without evidence.
- **DT-02-AC05 [W] Crash detection and evidence.** Given an application process that terminates with a non-zero exit code or triggers a Windows Error Reporting dialog, when detected, then `errorCategory=ENVIRONMENT`, `detailCode=desktop.appCrash` is returned with sanitized evidence (module name, exit code); no secret input is included in evidence.
- **DT-02-AC06 [W] Graceful close.** Given `desktop.app.close` with a configured `gracefulTimeoutMs`, when the application closes normally within the timeout, then `closed=true` is returned. If the process remains alive after the timeout, the result is FAILED or UNKNOWN according to whether a close signal was sent.
- **DT-02-AC07 [W] Policy-governed relaunch.** Given an approved relaunch policy and confirmed evidence that no committed side effect duplicates the prior attempt, when a relaunch is executed, then it follows the same allowlisted path and does not submit a second business transaction.
- **DT-02-AC08 [R] Multiple instances.** Given two allowed instances of the same application running simultaneously, when an attach targets one by PID or explicit window ownership, then only that instance is controlled; the other remains unaffected.

**Tests and delivery evidence:** Win32 process launch/attach integration tests against WinForms reference fixture; crash injection and exit-code detection test; dual-instance disambiguation test. Policy allowlist enforcement confirmed by attempting an unauthorized executable in a negative-path test.

**Traceability:** DES-004, DES-007, DES-008; Spec §4, §10 catalogue (desktop.app.launch, desktop.app.attach, desktop.app.close); CEC-HOST-001.

---

## DT-03 — Enumerate windows, control state and unexpected modal handling

**User story:** As a **Desktop Engineer**, I want the Runtime to discover, inspect and control the state of top-level and owned windows, so that automation can activate the intended target, detect unexpected modal popups and avoid input into the wrong surface.

**Functional scope:** Discover top-level and child windows by title pattern, class name, process ownership and modal ownership. Get visibility, bounding rectangle, enabled/focus state. Activate, restore, maximize, minimize, move and resize where OS permits. Detect configured expected dialogs; stop on unrecognised modals rather than blindly dismiss. Detect out-of-session window changes (update prompts, security dialogs, toasts).

**Acceptance criteria**

- **DT-03-AC01 [W] Window enumeration.** Given a process with a main window and a nested child/modal dialog, when `desktop.window.find` runs with an appropriate selector, then it resolves the correct window reference with title, class and bounding rectangle. Ambiguous matches (no discriminator) return `errorCategory=TARGET_AMBIGUOUS`.
- **DT-03-AC02 [W] State control.** Given `desktop.window.state` with `state=maximize`, when executed, then the resulting window state is verified within the configured timeout; an OS-denied request returns `errorCategory=ENVIRONMENT`, `detailCode=desktop.windowControlFailed`, not SUCCEEDED.
- **DT-03-AC03 [W] Foreground activation.** Given an application window in the background, when `desktop.window.activate` runs, then the window is brought to the foreground and focus is verified before the action returns.
- **DT-03-AC04 [W] Expected dialog handling.** Given a modal dialog that matches a configured expected title/class pattern, when it appears during a step, then the declared handler executes; a dialog not in the configured list stops the current step with sanitized window metadata evidence.
- **DT-03-AC05 [W] Security/update prompt detection.** Given a Windows security prompt, UAC dialog or auto-update modal appearing during automation, when detected, then no click or keystroke is sent to it; the active step fails with the dialog identity evidence and `errorCategory=ENVIRONMENT`.
- **DT-03-AC06 [W] Window overlap and occlusion check.** Given a modal window covering the target control before a pointer action, when the REC-003 foreground/overlay recheck runs, then the covered action is withheld and the blocking window is identified in evidence.
- **DT-03-AC07 [R] Cross-process window ownership.** Given windows owned by different processes with identical visible titles, when `desktop.window.find` is scoped to the correct process owner, then it returns only that process's windows.

**Tests and delivery evidence:** WinForms fixture with main window, nested panel and injected modal; window-state change tests for each allowed transition; security-prompt injection test confirming zero input delivery.

**Traceability:** DES-005, DES-006, DES-008; Spec §4, §10 catalogue (desktop.window.find, desktop.window.activate, desktop.window.state, desktop.window.moveResize); §8 REC-003.

---

## DT-04 — Supervisor/Executor split and session isolation

**User story:** As a **Platform Engineer**, I want the Supervisor and interactive Executor to be separate processes communicating over authorized local IPC, so that the background Supervisor cannot inject GUI events into the desktop session and the Executor exclusively owns the interactive job lifecycle.

**Functional scope:** Windows Runtime Host Supervisor handles boot-time bot identity, WSL2 control (isolated mode), named-pipe IPC endpoint creation. Interactive Executor handles desktop session ownership, job pull, heartbeat, GUI input and final result submission. Supervisor cannot reach the input desktop or act as an Orchestrator client. Named-pipe IPC uses OS-enforced pipe permissions; no network socket between the two processes.

**Acceptance criteria**

- **DT-04-AC01 [W] Separate process identities.** Given the Supervisor and Executor launched as distinct OS processes, when inspected at runtime, then their PIDs differ; the Supervisor process has no active window handle on the interactive desktop and cannot call SendInput or SetForegroundWindow.
- **DT-04-AC02 [W] IPC readiness handshake (isolated mode).** Given an **isolated mode** deployment where the Supervisor controls the WSL2/Docker Orchestrator container, when the Supervisor has completed WSL2 start and environment readiness check and sends the readiness signal over the authorized named pipe, then the Executor proceeds to worker registration and job pull; it does not attempt job pull before receiving readiness. In **connected mode**, the Executor connects directly to the central Orchestrator without a WSL2 dependency; the IPC handshake is limited to the Supervisor signalling local session availability.
- **DT-04-AC03 [S] Supervisor cannot deliver GUI events.** Given a test harness that asks the Supervisor process to invoke a GUI input API (SendInput, mouse_event, keybd_event), then the call is either structurally absent from the Supervisor binary or blocked by its integrity/policy; the interactive desktop receives no injected event via the Supervisor.
- **DT-04-AC04 [S] Pipe permission enforcement.** Given a process other than the authorized Executor attempting to connect to the Supervisor named pipe, when the OS access check runs, then the connection is refused; no foreign process can impersonate the Executor on the pipe.
- **DT-04-AC05 [W] Executor owns Orchestrator protocol.** Given a running Executor that holds a job lease and a Supervisor that has network access, when the Supervisor attempts to pull a job or heartbeat from the Orchestrator independently, then no such call exists in the Supervisor; job pull/heartbeat is an Executor-only responsibility.
- **DT-04-AC06 [W] Restart isolation.** Given the Supervisor restarts (host reboot scenario), when the Executor re-establishes IPC, then it resumes the reconciliation flow (DT-18) rather than starting a new uncoordinated job pull.
- **DT-04-AC07 [R] Isolated mode wiring.** Given an isolated deployment using Supervisor-controlled WSL2 and Orchestrator container, when the IPC handshake completes, then the Executor correctly treats the Supervisor-reported WSL2 endpoint as its Orchestrator address without the Orchestrator endpoint being configurable from within the Executor's environment.

**Tests and delivery evidence:** Two-process integration test with named-pipe harness; GUI-injection attempt test confirming no keystrokes from Supervisor; pipe ACL unit test; Orchestrator-endpoint ownership verification. Architecture reference: AD-13 in Tracto Architecture v1.2A.

**Traceability:** DES-010; Spec §4 DES-010; Architecture AD-13; CEC-HOST-001.

---

# Capability Group B — Element Identification and State

## DT-05 — Locate elements by stable UIA properties and ancestry

**User story:** As a **Desktop Engineer**, I want the Runtime to locate Windows controls using stable UIA properties and control ancestry, so that selectors survive window movement, control re-rendering and minor layout changes without relying on volatile runtime identifiers or captured pixel coordinates.

**Functional scope:** Resolve selectors by AutomationId, ControlType, ClassName, Name, supported UIA patterns and ancestor control chain from process → top-level window → ancestors → leaf. Require REQUIRE_UNIQUE match by default; allow indexWithinContainer only with stable parent and explicit evidence. Reject selectors using RuntimeId, absolute screen coordinates, OCR-confidence or image-template matching. Use UIA ScrollPattern to realize virtualized elements before targeting — this is internal Runtime behavior, not a script step. Re-resolve stale element references after provider invalidation — also internal behavior.

**Acceptance criteria**

- **DT-05-AC01 [W] AutomationId-based selection.** Given a WPF button with a stable AutomationId in a window that has been moved and resized, when `desktop.element.find` runs with that AutomationId, then it returns a valid element reference with the correct ControlType and bounding rectangle.
- **DT-05-AC02 [W] Ancestry chain.** Given a Pane ancestor scoping a button (both with stable AutomationIds), when the selector includes the ancestor chain, then the resolver finds the button within that scope only and not an identically named button in a sibling pane.
- **DT-05-AC03 [W] Ambiguity enforcement.** Given two buttons with identical Name and ControlType in the same window without AutomationId disambiguation, when `desktop.element.find` runs with REQUIRE_UNIQUE policy, then `errorCategory=TARGET_AMBIGUOUS` is returned and no action proceeds.
- **DT-05-AC04 [W] Virtualized row realization.** Given a WPF DataGrid with a row outside the visible viewport, when a step targets a cell in that row, then the Runtime internally scrolls via UIA ScrollPattern and/or VirtualizedItemPattern to realize the element before returning the reference; the script does not supply a scroll action.
- **DT-05-AC05 [W] Stale reference re-resolution.** Given a list control that refreshes its provider (e.g. sort/reload), when the cached element reference becomes stale, then the Runtime automatically re-resolves using the original selector before the action proceeds; no stale COM exception propagates to the step result.
- **DT-05-AC06 [S] Selector type rejection.** Given a selector containing an `absoluteCoordinate`, `imageTemplate`, `ocrPattern`, or `runtimeId` field, when schema validation runs before job execution, then the artifact is rejected with `errorCategory=VALIDATION` and the unsupported selector class name; no action executes.
- **DT-05-AC07 [U] RuntimeId exclusion.** Given a selector authored with a Windows UIA RuntimeId captured at design time, when the Runtime attempts to use it after an application restart, then the failure path returns `TARGET_NOT_FOUND` rather than corrupting another element's RuntimeId-mapped reference; published selectors must not persist RuntimeId.
- **DT-05-AC08 [R] Selector stability across window move/resize.** Given a published script that targets five distinct WPF controls with AutomationId-based selectors, when the window is moved to a different screen position and resized, then all five selectors resolve identically to before the move.

**Tests and delivery evidence:** WPF reference-harness integration tests with stable AutomationId and ancestry; virtual DataGrid row-realization test; ambiguity test with two same-named buttons; stale-reference injection test using a list that refreshes its UIA provider; schema-validation rejection test for each disallowed selector type.

**Traceability:** UIA-001, UIA-002, UIA-003, UIA-009, UIA-010; Spec §5, §11 selector schema; §3.2 gap classification (scroll-to-element, stale re-resolution = internal behaviors); DES-AT-01, DES-AT-17, DES-AT-23.

---

## DT-06 — Java Access Bridge adapter and keyboard-fallback targeting

**User story:** As a **Desktop Engineer**, I want the Runtime to address certified Java Swing/JavaFX controls via the Java Access Bridge and to navigate controls through documented keyboard paths when UIA patterns are unavailable, so that Java and legacy Win32 applications can be automated without requiring OCR or absolute coordinates.

**Functional scope:** Java Access Bridge adapter for certified JDK/JAB configurations — accessible role, name, indexInParent within a stable parent. Java Bridge enables semantic actions (click by accessible invoke, text via accessible text interface, tree by accessible hierarchy) and keyboard-fallback for Java apps. Java location does not authorize coordinate input — a UIA anchor is always required for pointer actions. Keyboard fallback: tab order, accelerators and arrow-key navigation within a focused container.

**Acceptance criteria**

- **DT-06-AC01 [W] Java button invoke.** Given a Swing JButton with an accessible name in a certified JAB-enabled JRE fixture, when `desktop.element.find` with a `javaBridge` selector resolves it and an action invokes it, then the accessible action fires and the postcondition observes the expected UI change.
- **DT-06-AC02 [W] Java text field read/write.** Given a Swing JTextField with accessible text, when the Runtime sets its value via the accessible text interface and reads it back, then the observed text exactly matches the value provided.
- **DT-06-AC03 [W] Java tree navigation.** Given a Swing JTree with accessible hierarchy, when a tree node is located by accessible name within its accessible parent chain, then the node reference resolves and its accessible expanded state is observable.
- **DT-06-AC04 [W] Java Bridge does not authorize coordinates.** Given a Java control resolved via the Access Bridge adapter, when a script requests a `relativePoint` offset from that Java reference without a UIA anchor, then the action is rejected — `errorCategory=ENVIRONMENT` — before any mouse movement occurs.
- **DT-06-AC05 [W] Keyboard fallback — tab order.** Given a WinForms form where a control has no stable AutomationId but is reachable by a known tab-stop index from a reliably identified container, when the keyboard-fallback strategy executes, then the control receives focus via the documented traversal path and the expected text is entered.
- **DT-06-AC06 [W] Accelerator/hotkey path.** Given a menu item reachable only by keyboard accelerator (Alt+F, O), when `desktop.keyboard.press` is used with the accelerator sequence and a postcondition observes the resulting dialog, then the sequence completes correctly and the dialog appears.
- **DT-06-AC07 [S] Unsupported Java control.** Given a Java control that has no accessible name, role or meaningful state in the accessibility tree, when the Runtime attempts to address it, then `errorCategory=ENVIRONMENT`, `detailCode=desktop.unsupportedControl` is returned; no attempt is made to use absolute coordinates as a fallback.
- **DT-06-AC08 [R] JAB certification boundary.** Given a Java application running on a JRE version outside the certified JAB support matrix, when the adapter attempts to connect, then a structured error is returned naming the unsupported JRE; the Runtime does not silently degrade to pixel-based targeting.

**Tests and delivery evidence:** Java Swing reference fixture (JButton, JTextField, JTree, JList) covering AC01–AC03; negative coordinate-from-Java test for AC04; WinForms keyboard-fallback test for AC05–AC06; unsupported-control negative test for AC07.

**Traceability:** UIA-006, UIA-007, UIA-008, UIA-009, UIA-011; Spec §5, §3.1, §11 selector schema (javaBridge, resolutionFallback, relativePoint); DES-AT-03, DES-AT-04.

---

## DT-07 — Verify element state and handle stale and unsupported targets

**User story:** As a **Desktop Engineer**, I want the Runtime to confirm that a targeted element is visible, enabled and not occluded before input, and to return structured diagnostics when a selector fails, so that automation never fires a click into a disabled or covered control and engineers can identify failing selectors without inspecting secrets.

**Functional scope:** Pre-input checks: control visible, enabled, foreground owner, not occluded by a modal or overlay, DPI/bounds integrity. Structured diagnostic codes for missing, ambiguous, stale, unsupported and access-denied targets. Read element property values including UIA patterns, text, toggle state, selection and expanded state. Wait for element state transitions.

**Acceptance criteria**

- **DT-07-AC01 [W] Visibility/enabled pre-check.** Given a button that is present in the tree but disabled (`IsEnabled=false`), when a click action is requested, then the pre-check returns `errorCategory=ENVIRONMENT`, `detailCode=desktop.elementDisabled`, with no click sent and no polling retry beyond configured timeout. This is the single predictable diagnostic outcome for a located-but-disabled control; callers must not expect `TARGET_NOT_FOUND` for this case.
- **DT-07-AC02 [W] Occlusion detection.** Given a modal dialog covering a target button, when the Runtime's foreground/occlusion check runs before a pointer action, then it detects the covering window, withholds the click and returns evidence naming the occlusion.
- **DT-07-AC03 [W] Property read.** Given a WinForms text box with a known value, when `desktop.element.read` retrieves the `Value` property via the UIA ValuePattern, then the returned value matches the text box content exactly.
- **DT-07-AC04 [W] Wait for state transition.** Given an element that is initially not visible and becomes visible after a 1-second async load, when `desktop.element.wait` is called with `state=visible` and a 5-second timeout, then it resolves once the element appears; the step does not time out.
- **DT-07-AC05 [W] Timeout on state wait.** Given an element that never transitions to the expected state within the configured timeout, when `desktop.element.wait` expires, then the step returns `TIMED_OUT` (errorCategory=TIMEOUT) with the last-observed element state in evidence.
- **DT-07-AC06 [W] Unsupported provider.** Given a custom non-accessible canvas control with no UIA provider, when the Runtime attempts element find, then `errorCategory=ENVIRONMENT`, `detailCode=desktop.unsupportedControl` is returned; no fallback to OCR or absolute coordinates occurs.
- **DT-07-AC07 [S] Access denied — elevated target.** Given a UIA element belonging to an elevated-integrity process, when a standard-integrity Executor attempts to read or interact with it, then `errorCategory=POLICY_DENIED`, `detailCode=desktop.accessDenied` is returned without crashing and without attempting elevation bypass.
- **DT-07-AC08 [R] Diagnostic clarity.** Given a failing selector, when the error returns, then the diagnostic includes the attempted selector strategy (AutomationId, ClassName, ancestry), the searched process/window scope and the sanitized bounds of candidates found — without exposing any secret input or step variable plaintext.

**Tests and delivery evidence:** WinForms/WPF test harness with disabled, hidden, occluded and normal state buttons; async-load element visible test; UIA ValuePattern read test; elevated-process access-denied test; diagnostic sanitization review.

**Traceability:** UIA-004, UIA-005, UIA-009, UIA-011; Spec §5, §8 REC-003, REC-007; §10 catalogue (desktop.element.find, desktop.element.read, desktop.element.wait).

---

# Capability Group C — Mouse, Keyboard and Clipboard Input

## DT-08 — Execute verified mouse movement, click, drag and scroll

**User story:** As a **Desktop Engineer**, I want the Runtime to deliver mouse input only to verified, uniquely located controls with confirmed postconditions, so that clicks never reach wrong targets and the automation never claims a potentially-effectful click succeeded unless the UI state confirms it.

**Functional scope:** Move (with optional dwell for contextual UI), left/right/middle click, double click, drag/drop, horizontal/vertical scroll. All coordinate inputs are relative to a UIA-located control bounding rectangle. Revalidate bounds, foreground, DPI and hit region immediately before each input. Release held mouse button on all cancellation/timeout paths.

**Acceptance criteria**

- **DT-08-AC01 [W] Control-relative click.** Given a WPF button uniquely located by UIA selector, when `desktop.mouse.click` executes with zero relative offset, then the click lands on the button's center (verified by system hit-test), the postcondition confirms UI state change, and the step returns SUCCEEDED.
- **DT-08-AC02 [W] Offset stays in control.** Given a UIA control with a known bounding rectangle, when a relative offset is specified, then the Runtime verifies the computed absolute coordinate falls within the control rectangle before sending the event; an offset that would exit the rectangle is rejected.
- **DT-08-AC03 [W] Window move before click.** Given a window moved to a new screen position between selector resolution and the click, when the Runtime re-resolves the control rectangle immediately before the input, then the click lands at the new correct position.
- **DT-08-AC04 [W] Click without confirmed postcondition.** Given a `desktop.mouse.click` on a Submit button where the application takes an action but no accessible state change is observable, when no postcondition predicate is provided or the provided one is unverifiable after the click, then the step state is UNKNOWN with `unknownReason: postcondition.irrevocableUncertain` and `needsAttention=true`.
- **DT-08-AC05 [W] Hover with dwell.** Given `desktop.mouse.move` with `dwellMs=1500` on a toolbar button that shows a contextual menu after hover, when executed, then the cursor dwells for 1500 ms on the UIA-located control and the contextual UI appears before the action returns.
- **DT-08-AC06 [W] Drag and drop.** Given two WPF list items as UIA-located source and target, when `desktop.mouse.drag` completes, then the item appears in the target location per its postcondition. If the drag is cancelled mid-action, the mouse button is released and the step is UNKNOWN or CANCELLED based on whether the drop target can be observed.
- **DT-08-AC07 [W] Scroll — NEVER retry.** Given `desktop.mouse.scroll` directing a vertical wheel event in a list, when the list scrolls, then a new viewport state is observable. Retry class is NEVER; a repeat scroll call from the orchestrator is not automatically issued even if the first does not reach the target row.
- **DT-08-AC08 [W] Mouse button cleanup on cancellation.** Given a drag operation cancelled via the job cancellation signal, when the Runtime handles cancellation, then no mouse button remains logically pressed; the input desktop is left in a usable state and a subsequent click in the same session succeeds.

**Tests and delivery evidence:** WPF reference harness with clickable button, draggable list items and a scrollable virtualized grid; window-move-before-click test; dwell-hover contextual-menu test; cancellation-during-drag cleanup test.

**Traceability:** INPUT-001, INPUT-002, INPUT-003; Spec §6, §10 catalogue (desktop.mouse.move with dwellMs, desktop.mouse.click, desktop.mouse.drag, desktop.mouse.scroll); §8 REC-004, REC-005; DES-AT-08, DES-AT-12.

---

## DT-09 — Enter text, keyboard shortcuts and hold-key sequences

**User story:** As a **Desktop Engineer**, I want the Runtime to type Unicode text and keyboard shortcuts into verified, focused controls, so that all three typing strategies (UIA ValuePattern, controlled keystroke, clipboard paste) produce the exact intended characters and modifier keys are always released.

**Functional scope:** `desktop.keyboard.type` with declared strategy (ValuePattern preferred, keystroke fallback, clipboard-paste declared); `desktop.keyboard.press` for single keys and combinations; `desktop.keyboard.keyDownUp` with `holdMs` and cancellation-safe release. Verify target focus before typing. Release all modifier keys on any exit path. Handle scancode/layout fallback for Java and Win32 apps that reject Unicode packet injection.

**Acceptance criteria**

- **DT-09-AC01 [W] UIA ValuePattern entry.** Given a WPF TextBox that exposes the UIA ValuePattern, when `desktop.keyboard.type` uses the ValuePattern strategy, then the exact Unicode string (including accented and non-Latin characters) appears in the control and is confirmed by ValuePattern readback.
- **DT-09-AC02 [W] Keystroke fallback.** Given a Win32 Edit control that does not expose ValuePattern, when the keystroke fallback strategy types a mixed-case ASCII string with a configured keyboard layout, then the text field contains the exact expected characters.
- **DT-09-AC03 [W] Non-Latin characters via scancode fallback.** Given a Java Swing text field that rejects Unicode WM_CHAR injection, when the scancode/layout fallback strategy is used, then the expected Unicode text (including accented Latin or RTL characters) is entered correctly on the certified OS locale.
- **DT-09-AC04 [W] Keyboard shortcut.** Given a WPF application with a Ctrl+S save shortcut, when `desktop.keyboard.press` sends the combination and the postcondition observes the save dialog or success indicator, then the step returns SUCCEEDED.
- **DT-09-AC05 [W] Hold-key sequence with cleanup.** Given `desktop.keyboard.keyDownUp` holding Shift for 800 ms while other keys are pressed, when the hold completes or the job is cancelled mid-hold, then the Shift modifier is released; a subsequent typing test confirms no stuck modifier.
- **DT-09-AC06 [W] Focus verification.** Given a text field identified by UIA selector, when `desktop.keyboard.type` is called, then the Runtime verifies the control has focus (via UIA HasKeyboardFocus) before injecting keystrokes; if it cannot obtain focus within the configured timeout, the step returns an environment error.
- **DT-09-AC07 [W] Strategy reporting.** Given any `desktop.keyboard.type` execution, when the step completes (success or failure), then the evidence or result metadata declares which strategy was used (ValuePattern, keystroke, clipboard-paste) so engineers can diagnose encoding failures.
- **DT-09-AC08 [R] No secret characters in evidence.** Given a typing step where the text reference resolves from a `secretRef`, when the step evidence is recorded, then no portion of the resolved plaintext appears in the step result, journal entry or log line.

**Tests and delivery evidence:** WPF TextBox ValuePattern test; Win32 Edit keystroke test; Java text field scancode fallback test; Ctrl+S shortcut test; hold-key-stuck-modifier test; focus verification failure test.

**Traceability:** INPUT-004, INPUT-005, INPUT-008, INPUT-009; Spec §6, §10 catalogue (desktop.keyboard.type, desktop.keyboard.press, desktop.keyboard.keyDownUp); DES-AT-18.

---

## DT-10 — Clipboard read, write, contention recovery and restore pattern

**User story:** As a **Desktop Engineer**, I want the Runtime to read and write clipboard text with contention handling and a documented restoration pattern, so that automation can transfer values between application fields without permanently clobbering unrelated clipboard content.

**Functional scope:** `desktop.clipboard.copy` reads the current clipboard or copies from a UI control into a named reference; `desktop.clipboard.paste` pastes approved content from a named reference into a target UI control. Clipboard lock contention retries are bounded and transient-safe. OS clipboard save/restore around paste operations is internal Runtime behavior governed by deployment policy — it is not a public script action that callers compose. Unsupported proprietary binary clipboard formats are disclosed, not silently dropped.

**Acceptance criteria**

- **DT-10-AC01 [W] Copy from control.** Given a WinForms text field with a known value, when `desktop.clipboard.copy` targets that field (via select-all + copy), then the resulting `clipboardRef` holds the exact text for use by subsequent steps.
- **DT-10-AC02 [W] Paste into control.** Given an approved text value in a `clipboardRef`, when `desktop.clipboard.paste` targets a text field, then the field contains the expected text confirmed by accessible readback or ValuePattern. Retry class is NEVER; an uncertain paste yields UNKNOWN with `unknownReason: action.commitUncertain`.
- **DT-10-AC03 [W] Clipboard lock contention.** Given a second Windows process that holds an open clipboard handle for 1 second, when `desktop.clipboard.copy` attempts to open the clipboard, then it retries within the bounded contention window (TRANSIENT_SAFE), succeeds once the handle is released and does not fail on the first attempt.
- **DT-10-AC04 [W] Internal clipboard restore.** Given a deployment policy that enables OS clipboard preservation, when `desktop.clipboard.paste` executes, then the Runtime internally captures the pre-paste OS clipboard text (best-effort), pastes the approved content into the target control, and restores the captured text to the OS clipboard after the paste commits; this behavior is transparent to the script and does not require callers to compose a save/restore sequence using public actions.
- **DT-10-AC05 [W] Unsupported format disclosure.** Given a third-party application that has written a proprietary binary clipboard format, when `desktop.clipboard.copy` runs with format=text, then the result acknowledges the plain-text portion (if any) and discloses that proprietary formats were present but not captured; no crash or silent truncation.
- **DT-10-AC06 [S] No secret in clipboard evidence.** Given a clipboard paste step where the clipboardRef was resolved from a `secretRef`, when the step evidence is recorded, then no portion of the secret plaintext appears in logs or telemetry. Clipboard handles to secret content are cleared after use.
- **DT-10-AC07 [R] Cross-process paste.** Given a source WinForms field and a target WPF field in a different process, when the copy-paste sequence runs across the process boundary, then the correct value appears in the WPF target and no intermediate window receives stray clipboard events.

**Tests and delivery evidence:** WinForms-to-WPF cross-process clipboard test; contention test using a competing clipboard handle with a 1-second hold; internal-restore policy test (policy=on: confirms OS clipboard holds prior text after paste; policy=off: confirms no restore attempt); proprietary-format disclosure test; secret-leakage review (automated evidence scan).

**Traceability:** INPUT-006, INPUT-007; Spec §6, §10 catalogue (desktop.clipboard.copy, desktop.clipboard.paste); §3.2 gap classification (clipboard restoration = internal Runtime behavior); DES-AT-05.

---

## DT-11 — Resolve and apply secretRef values without secret leakage

**User story:** As a **Security Engineer**, I want secret values to be resolved via Windows DPAPI in the interactive session and applied to controls without the plaintext appearing in logs, telemetry, evidence screenshots or Orchestrator messages, so that credentials never leave the authorized Windows session boundary.

**Functional scope:** `secretRef` resolved by the Executor inside the interactive Windows session using DPAPI CurrentUser scope. Resolved plaintext used directly for ValuePattern set, controlled keystroke or clipboard paste per the step's declared strategy. Zero plaintext in step result, journal, evidence reference, Orchestrator heartbeat/result message or WSL-side container. Sensitive control regions masked in any permitted screenshots. DPAPI not available in WSL/Linux container — resolution must fail with a structured error if attempted there.

**Acceptance criteria**

- **DT-11-AC01 [W] DPAPI resolution in session.** Given a `secretRef` pointing to a DPAPI-protected credential blob, when the Executor resolves it in the active interactive Windows session, then the plaintext is returned to the in-process buffer only; no file write or network call is made during resolution.
- **DT-11-AC02 [W] Credential applied to auth control.** Given a Windows login dialog with Username and Password fields, when the script provides `textRef={"ref":"inputs.username"}` and `textRef={"ref":"secrets.passwordKey"}` respectively, then the Username and Password fields receive the correct values and the login proceeds; no credential appears in evidence or step output.
- **DT-11-AC03 [S] No plaintext in logs or results.** Given any step that uses a `secretRef`, after it completes (success or failure), when logs, step results, Orchestrator messages and journal entries are inspected, then no portion of the resolved secret plaintext is present. This is verified by an automated evidence scan against a known test secret pattern.
- **DT-11-AC04 [S] No secret crosses the Windows/Linux boundary.** Given an isolated deployment where the Supervisor runs adjacent to a Linux/WSL container, when the Executor resolves a `secretRef`, then the plaintext is never written to the named pipe, the container-mounted path or any network socket accessible from the Linux side.
- **DT-11-AC05 [S] DPAPI unavailable outside Windows session.** Given any code path that attempts `secretRef` resolution outside the active interactive Windows user session (e.g. in a background service, a network call handler or the Supervisor), then the operation returns a structured error (`POLICY_DENIED`/`ENVIRONMENT`) and no resolution is attempted.
- **DT-11-AC06 [W] Screenshot masking.** Given an approved evidence screenshot that includes a password field, when the screenshot is captured, then the password field region is masked (blacked out or blurred) such that no character is legible; the screenshot is withheld entirely if masking cannot be guaranteed.
- **DT-11-AC07 [R] Cleared transient buffer.** Given a `secretRef` resolved to a byte array for a typing step, after the keystroke sequence completes (or the step fails/is cancelled), when the process memory is inspected at the .NET GC level, then the byte array backing the secret has been cleared with zeroes before the next GC collection point.

**Tests and delivery evidence:** DPAPI resolution unit test with mock credential store; evidence-scan automation confirming zero secret leakage across logs/results (AC03); Windows/Linux boundary test in isolated deployment (AC04); screenshot masking visual review.

**Traceability:** INPUT-009; Spec §6, §12 (screenshots, DPAPI); Architecture AD-14; DES-AT-09; CEC-HOST-001.

---

# Capability Group D — Standard Control Families

## DT-12 — Set and verify text fields, checkboxes, radio buttons and value controls

**User story:** As a **Desktop Engineer**, I want the Runtime to set the values of standard editable controls to their intended final state via UIA patterns, so that a checkbox is not toggled if it is already in the target state and a text field receives the exact expected content.

**Functional scope:** `desktop.control.setValue` using UIA ValuePattern or controlled keystroke/paste for text fields; `desktop.control.setCheck` using UIA TogglePattern to reach the target boolean state without spurious toggles; `desktop.keyboard.type` for numeric, date and masked edit controls via certified keyboard procedure. Verify final observed state as postcondition.

**Acceptance criteria**

- **DT-12-AC01 [W] Text field set via ValuePattern.** Given a WinForms TextBox exposing ValuePattern, when `desktop.control.setValue` sets a Unicode string, then the field contains exactly that string confirmed by immediate ValuePattern readback; the step is IDEMPOTENT.
- **DT-12-AC02 [W] Already-correct state not re-toggled.** Given a WinForms CheckBox already in the `checked=true` state, when `desktop.control.setCheck` is called with `checked=true`, then no toggle event is sent; the UIA TogglePattern is read first and the action is a no-op when already correct.
- **DT-12-AC03 [W] Radio button selection.** Given a WinForms RadioGroup, when `desktop.control.setCheck` targets one radio button with `checked=true`, then that button is selected and confirmed by TogglePattern or SelectionItemPattern; the previously selected radio reflects the change.
- **DT-12-AC04 [W] Numeric masked edit.** Given a Win32 masked-edit control that does not expose ValuePattern, when the certified keyboard procedure enters a locale-formatted number, then the control displays the expected formatted value and a readback via accessible text confirms it.
- **DT-12-AC05 [W] ComboBox selection.** Given a WPF ComboBox, when `desktop.control.select` sets `optionText="Option B"`, then Option B is selected using the UIA Selection pattern and the displayed value reflects Option B after the action.
- **DT-12-AC06 [W] Two identical options require disambiguation.** Given a ComboBox with two items both labelled "Unknown", when `desktop.control.select` specifies `optionText="Unknown"` without an index, then `errorCategory=TARGET_AMBIGUOUS` is returned and no selection is made.
- **DT-12-AC07 [W] Date picker via accessible interface.** Given a Windows Forms DateTimePicker, when the target date is set via its accessible value interface (or certified keyboard procedure for controls without it), then the displayed date matches the intended date on the certified OS locale.
- **DT-12-AC08 [R] Strategy reporting.** Given any value-setting step, when evidence is recorded, then the step metadata declares the strategy used (ValuePattern, keystroke, clipboard) so engineers can diagnose discrepancies between nominal and observed values.

**Tests and delivery evidence:** WinForms harness with TextBox (ValuePattern), CheckBox, RadioGroup, ComboBox (two identical items), DateTimePicker and a Win32 masked-edit control; idempotent-checkbox test confirming zero toggle when already correct.

**Traceability:** CTRL-001, CTRL-002, CTRL-003, CTRL-008; Spec §7, §10 catalogue (desktop.control.setValue, desktop.control.setCheck, desktop.control.select).

---

## DT-13 — Navigate and edit lists, grids and tree hierarchies

**User story:** As a **Desktop Engineer**, I want the Runtime to select rows and cells in lists and grids, read accessible cell values and enter edit mode for editable grid cells, and to expand and collapse tree nodes by state, so that tabular and hierarchical data can be reliably read and updated.

**Functional scope:** `desktop.grid.readCell` — read cell value via UIA Grid/Table/TableItem patterns; `desktop.grid.selectRow` — select by stable row identity; `desktop.grid.editCell` — enter edit mode, set value, confirm via postcondition (retry class NEVER); `desktop.tree.expand` with `state: expanded|collapsed`(IDEMPOTENT in both directions via UIA ExpandCollapse pattern). Virtualized grid rows realized by internal scroll before targeting.

**Acceptance criteria**

- **DT-13-AC01 [W] Grid cell read.** Given a WPF DataGrid with stable row identifiers and column headers, when `desktop.grid.readCell` targets a specific row/column intersection, then the returned value matches the accessible cell text without requiring the row to be pre-scrolled into view (the Runtime scrolls internally).
- **DT-13-AC02 [W] Row selection by identity.** Given a sorted or filtered DataGrid where ordinal row position changes, when `desktop.grid.selectRow` targets a row by its stable accessible identifier (e.g. a unique cell value in a key column), then that row is selected regardless of its current visual position.
- **DT-13-AC03 [W] Grid cell edit and confirm.** Given an editable WinForms DataGridView cell, when `desktop.grid.editCell` enters edit mode, sets a new value and confirms, then the cell displays the new value after the postcondition check. Retry class is NEVER; uncertain commit → UNKNOWN with `unknownReason: action.commitUncertain`.
- **DT-13-AC04 [W] Tree expand.** Given a WPF TreeView node that is collapsed, when `desktop.tree.expand` runs with `state=expanded`, then the node's accessible ExpandCollapse state becomes Expanded and child nodes become accessible. If already Expanded, the action is idempotent (no second ExpandCollapse event sent).
- **DT-13-AC05 [W] Tree collapse.** Given a WPF TreeView node that is expanded, when `desktop.tree.expand` runs with `state=collapsed`, then the node's accessible ExpandCollapse state becomes Collapsed. If already Collapsed, the action is idempotent.
- **DT-13-AC06 [W] Virtualized grid — scrolled-to-realize.** Given a DataGrid with 10 000 rows where the target row is outside the viewport, when any targeting action is issued for that row, then the Runtime internally scrolls via VirtualizedItemPattern or ScrollPattern within a bounded realization timeout before returning the cell reference. If the row cannot be realized within those bounds (e.g. the grid blocks programmatic scroll), the step returns `errorCategory=TIMEOUT` or `errorCategory=ENVIRONMENT, detailCode=desktop.unsupportedControl` as appropriate; it does not suppress the timeout or silently succeed.
- **DT-13-AC07 [W] Stale row identity after sort.** Given a DataGrid whose rows are re-sorted between selector resolution and the action, when `desktop.grid.selectRow` uses a stable content-based row identity (not row index), then the correct row is still selected after the sort.
- **DT-13-AC08 [R] Read-only cell rejection.** Given a DataGrid cell that is read-only, when `desktop.grid.editCell` is called on it, then `errorCategory=ENVIRONMENT` is returned and no edit mode is entered; the cell value is unchanged.

**Tests and delivery evidence:** WPF DataGrid with sortable rows and an editable column; tree expand/collapse idempotency test; virtual-row realization test with 500-row list; read-only-cell rejection test.

**Traceability:** CTRL-004, CTRL-005; Spec §7, §10 catalogue (desktop.grid.readCell, desktop.grid.selectRow, desktop.grid.editCell ★, desktop.tree.expand ★); §3.2 gap classification.

---

## DT-14 — Select tabs, navigate menus and handle file and confirm dialogs

**User story:** As a **Desktop Engineer**, I want the Runtime to select named tabs, execute menu/ribbon commands and handle file picker and confirmation dialogs according to declared policy, so that multi-page forms, command-line menus and save/open flows complete predictably without dismissing unexpected dialogs.

**Functional scope:** `desktop.control.select` for tab strip (UIA Selection pattern); `desktop.menu.select` via path array (retry class NEVER); `desktop.dialog.fileChoose` with approved path (retry class NEVER); `desktop.dialog.confirm` for declared expected button choices (retry class NEVER). All dialog actions: uncertain completion → UNKNOWN with `unknownReason: action.commitUncertain`. Undeclared dialogs are blocked (DT-03-AC04).

**Acceptance criteria**

- **DT-14-AC01 [W] Named tab selection.** Given a WinForms TabControl with three named tabs, when `desktop.control.select` targets the tab named "Settings" by optionText, then the "Settings" tab becomes active and its accessible selected state is verified. This is IDEMPOTENT — calling again when already on "Settings" produces no event.
- **DT-14-AC02 [W] Menu command via path.** Given a Win32 application menu bar with File → Save As, when `desktop.menu.select` is given the path array `["File", "Save As"]`, then the Save As dialog opens. Retry class is NEVER; if the save action is uncertain, step state is UNKNOWN.
- **DT-14-AC03 [W] Ribbon command via overflow.** Given an Office-style ribbon with a command that has moved to the overflow area due to window resize, when `desktop.menu.select` targets it by its accessible name path, then the command is found and invoked via accessible invoke or keyboard shortcut, or the step returns a documented unsupported result.
- **DT-14-AC04 [W] File picker with approved path.** Given Windows Open File dialog, when `desktop.dialog.fileChoose` is called with an approved path reference, then the path is typed into the filename field using the approved keyboard procedure and the dialog is confirmed; the accessible dialog state then closes.
- **DT-14-AC05 [S] Unauthorized file path denied.** Given `desktop.dialog.fileChoose` with a path that is not on the approved file-path policy allowlist (e.g., a system directory or network UNC outside the allowlist), when validation runs, then `POLICY_DENIED` is returned before the dialog receives any input.
- **DT-14-AC06 [W] Confirm dialog.** Given a "Save changes?" dialog with "Save" and "Discard" buttons, when `desktop.dialog.confirm` is called with the declared choice "Save", then the "Save" button is clicked and its accessible state disappears, confirming the dialog closed. Uncertain outcome → UNKNOWN.
- **DT-14-AC07 [W] Undeclared dialog blocks step.** Given an unexpected Windows Update or cloud-sync dialog appearing during a file-picker flow, when the Runtime's modal-detection check fires, then no further input is sent, the current step fails with the dialog identity in evidence, and the flow halts for human review.
- **DT-14-AC08 [R] Overwrite confirmation policy.** Given a file save dialog prompting to overwrite an existing file, when the declared policy is "deny overwrite", then the Runtime does not click OK and returns the step as FAILED with the appropriate evidence; it never bypasses the overwrite check by pre-deleting the file.

**Tests and delivery evidence:** WinForms TabControl three-tab selection idempotency test; Win32 menu path test; file picker with approved and unapproved path tests; confirm-dialog test with both declared choices; overwrite-deny policy test.

**Traceability:** CTRL-006, CTRL-007; Spec §7, §10 catalogue (desktop.control.select, desktop.menu.select, desktop.dialog.fileChoose, desktop.dialog.confirm); DES-AT-06, DES-AT-14.

---

# Capability Group E — Recovery, Waits and Safe Failure

## DT-15 — Bounded technical waits, postcondition assertions and prechecks

**User story:** As a **Desktop Engineer**, I want every potentially-consequential step to carry a verified technical postcondition and every wait to use a state predicate rather than a fixed sleep, so that automation neither claims success without evidence nor hangs indefinitely.

**Functional scope:** `desktop.element.wait` with state predicates (visible, enabled, focused, text-equals, window-created/destroyed, app-ready); all action types have postcondition assertions; `elementWaitMs` polling at 250 ms; `inputSettleMs` as a minimum settle time after which state-based postconditions must confirm; deadline inheritance from job policy. Non-confirmed postcondition on an irreversible action → UNKNOWN, not SUCCEEDED.

**Acceptance criteria**

- **DT-15-AC01 [W] Wait resolves on transition.** Given an application button that becomes enabled after a 2-second async operation, when `desktop.element.wait` is called with `state=enabled` and a 5-second timeout, then it resolves at ~2 seconds without the 5-second timeout elapsing; no fixed sleep is used.
- **DT-15-AC02 [W] Timeout on missing transition.** Given a wait for `state=visible` where the element never appears, when the configured `elementWaitMs` expires, then the step returns `TIMED_OUT` (errorCategory=TIMEOUT) with the last-observed state; no action fires after the timeout.
- **DT-15-AC03 [W] Postcondition gates SUCCEEDED.** Given a Submit click whose application response (a dialog or status label change) appears 500 ms later, when `desktop.control.click` is configured with a postcondition predicate that checks for the expected UI change, then SUCCEEDED is returned only after the predicate resolves true; if the predicate times out, the step is FAILED or UNKNOWN.
- **DT-15-AC04 [W] Irreversible-action uncertainty.** Given a Save click on an ERP form where the application responds with a timeout (network issue), when the postcondition is unverifiable and the action was potentially effectful, then the step state is UNKNOWN with `unknownReason: postcondition.irrevocableUncertain` and `needsAttention=true`; the step is not retried automatically.
- **DT-15-AC05 [W] Precheck blocks blind click.** Given a button that is present in the UIA tree but reports IsEnabled=false at the moment a click would fire, when the pre-check runs as part of the action sequence, then the click is withheld and the step fails with a diagnostic; the click does not go to the disabled control even if it becomes enabled a millisecond later.
- **DT-15-AC06 [W] inputSettleMs is not a substitute for postconditions.** Given a step with `inputSettleMs=200` and no postcondition, when the action fires, then only the 200 ms settle delay is applied; no assertion claims SUCCEEDED without observable state evidence. The settle time is distinct from the wait — it is a minimum delay, not a confirmation mechanism.
- **DT-15-AC07 [R] Deadline inheritance.** Given a job lease with 30 seconds remaining and a step `timeoutMs=60000`, when the step begins, then the effective timeout is capped at the remaining job deadline (≤30 s); the step does not run past the lease expiry.

**Tests and delivery evidence:** WPF async-load button wait test; postcondition-gates-success test with simulated 500 ms UI response; ERP-save timeout → UNKNOWN test; disabled-button precheck test; deadline-capping unit test.

**Traceability:** REC-001, REC-002, REC-003, REC-004; Spec §8, §10 catalogue (desktop.element.wait); §12 defaults; DES-AT-01.

---

## DT-16 — Detect and handle crash, session lock, focus theft and uncertain completion

**User story:** As a **Desktop Engineer**, I want the Runtime to detect unexpected application crashes, session lock/disconnect events, focus theft and uncertain-completion states, so that automation exits with a structured, accurate terminal state rather than a false success or an indefinite block.

**Functional scope:** Detect process crash (non-zero exit, WER dialog), hang (unresponsive message loop), session lock/disconnect (WM_WTSSESSION_CHANGE), focus theft (foreground window changes unexpectedly). Bounded session-lock wait (default 300 000 ms, configurable) is internal behavior under RUNNING — no PAUSED run state emitted; heartbeat continues during wait; exit through FAILED/TIMED_OUT/UNKNOWN on deadline per C3. Map all Desktop error identifiers to CEC v1.0.1 `errorCategory` (Spec Appendix A).

**Acceptance criteria**

- **DT-16-AC01 [W] Application crash detection.** Given an application process that terminates unexpectedly (injected TerminateProcess) during a multi-step flow, when the Runtime detects the exit, then `APP_CRASH` (errorCategory=ENVIRONMENT) is returned; if the crash occurred after a potentially-effectful action, UNKNOWN with `unknownReason: action.commitUncertain` is used; the step result never claims SUCCEEDED without process existence confirmed.
- **DT-16-AC02 [W] Application hang detection.** Given an application whose message loop stops responding within the configured unresponsive threshold, when detected, then the Runtime stops sending input, collects sanitized evidence (process name, last action) and returns an environment error; it does not continue clicking into a frozen window.
- **DT-16-AC03 [W] Session lock — RUNNING throughout.** Given the Windows session is locked mid-job (injected Ctrl+Alt+Del lock) within the session-lock timeout, when the Runtime enters its internal bounded wait, then the run state remains RUNNING; no PAUSED state is emitted; the Orchestrator continues receiving heartbeats during the wait period.
- **DT-16-AC04 [W] Session-lock deadline expiry.** Given the session lock persists past the configured `pauseMaxMs` deadline (default 300 000 ms), when the deadline expires, then the run exits as FAILED, TIMED_OUT or UNKNOWN according to whether the last consequential action's side effect can be established — not as a false SUCCEEDED.
- **DT-16-AC05 [W] Focus theft detection and halt.** Given a foreground window change caused by an external process (injected background notification taking focus), when detected before a pointer/keystroke action, then the action is withheld, evidence names the stealing window and the step fails rather than sending input to the wrong target.
- **DT-16-AC06 [W] UNKNOWN terminates execution attempt.** Given any step producing UNKNOWN with `needsAttention=true`, when the result is detected, then the execution attempt is fenced immediately: the run terminates as UNKNOWN with `needsAttention=true` and no subsequent automation steps execute. The Orchestrator receives a single final result with `needsAttention=true`; continuation requires authorized reconciliation via `Reconcile` and a new valid execution decision with a fresh `attemptId`. Read-only or diagnostic steps that cannot confirm their outcome are classified FAILED/TIMED_OUT — they do not produce UNKNOWN; only steps with an irrevocable side effect produce UNKNOWN.
- **DT-16-AC07 [R] Error code mapping completeness.** Given each Desktop-specific error condition, when it appears in a step result, then the `errorCategory` field carries the CEC v1.0.1 contract value and any Desktop-specific supplemental information is in `detailCode`, per Spec Appendix A: `desktop.sessionUnavailable` → `errorCategory=SESSION_UNAVAILABLE`; `desktop.displayProfileChanged` → `errorCategory=ENVIRONMENT`; `desktop.windowControlFailed` → `errorCategory=ENVIRONMENT`; `desktop.appCrash` → `errorCategory=ENVIRONMENT`; target absent → `errorCategory=TARGET_NOT_FOUND`; multiple matches → `errorCategory=TARGET_AMBIGUOUS`; `desktop.unsupportedControl` → `errorCategory=ENVIRONMENT`; `desktop.accessDenied` → `errorCategory=POLICY_DENIED`; timeout expired → `errorCategory=TIMEOUT`. No unmapped Desktop error code reaches the Orchestrator message.

**Tests and delivery evidence:** TerminateProcess injection test during click; WER-dialog detection test; session-lock simulation with heartbeat-continuity verification; lock-deadline expiry test; focus-theft injection test confirming zero post-theft input.

**Traceability:** REC-004, REC-005, REC-006, REC-008; Spec §8, §12; Spec Appendix A (error taxonomy); DES-AT-07, DES-AT-11, DES-AT-22; CEC C3 rules.

---

## DT-17 — Cancel safely and release all held input and session resources

**User story:** As a **Desktop Engineer**, I want every cancellation, timeout or exception path to release held mouse buttons, modifier keys, clipboard handles, the input lease and owned process handles, so that the interactive desktop is left in a usable state after any form of job termination.

**Functional scope:** Job cancellation signal; action-deadline timeout; unhandled exception during input; Orchestrator-initiated cancel (CEC-HOST-006). Release sequence: keyboard modifier state, mouse button state, clipboard handle, input lease, session-level handles, application process handles. Evidence of what was released and what was uncertain. Confirm desktop usability after release.

**Acceptance criteria**

- **DT-17-AC01 [W] Keyboard modifier release on cancellation.** Given Ctrl+Shift held during a keyboard action that is cancelled, when the cancellation signal arrives, then the Runtime sends the corresponding key-up events; a subsequent OS-level key-state query shows Ctrl and Shift unpressed.
- **DT-17-AC02 [W] Mouse button release on drag cancellation.** Given a drag-and-drop operation in progress when the job is cancelled, when the Runtime handles cancellation, then the left mouse button is released before any cleanup step; no drag cursor persists.
- **DT-17-AC03 [W] Clipboard handle released.** Given the clipboard is open (between OpenClipboard and CloseClipboard) when a timeout fires, when the Runtime's exception handler runs, then CloseClipboard is called; a subsequent clipboard operation from another process succeeds without a clipboard-still-open error.
- **DT-17-AC04 [W] Input lease released.** Given a job that is cancelled while holding the exclusive input lease, when cleanup completes, when a new Run starts in the same session, then it can acquire the input lease without a stuck-lease error.
- **DT-17-AC05 [W] Step result on cancellation.** Given a mouse click that is in-flight when the job cancellation signal arrives, when the action is aborted, then the step state is CANCELLED (if no input was sent) or UNKNOWN with `unknownReason: action.commitUncertain` (if input was sent but postcondition was not confirmed).
- **DT-17-AC06 [W] Application process handles.** Given the Runtime has open handles to the target application's process (for exit monitoring), when the Runtime exits via any path (normal, crash, cancellation), then those handles are released; the target application is not killed unless the script explicitly calls `desktop.app.close`.
- **DT-17-AC07 [R] Desktop usability after cancel.** Given a complete job-cancel cycle that includes keyboard input, mouse drag and clipboard operations, when a new automated or manual session begins on the same desktop, then all input modalities function normally: typing produces the expected characters, the mouse cursor moves freely and the clipboard is accessible.

**Tests and delivery evidence:** Cancellation-during-held-key test; cancellation-during-drag test; clipboard-handle-leak prevention test; lease-availability-after-cancel test; full-cycle cancel then new-session input test.

**Traceability:** REC-005; Spec §8 REC-005, §12 (retry/release defaults); §10 catalogue (desktop.keyboard.keyDownUp, desktop.mouse.drag, desktop.clipboard.copy); DES-AT-12; CEC-HOST-006.

---

# Capability Group F — Runtime Conformance and Observability

## DT-18 — CEC v1.0.1 Orchestrator protocol conformance

**User story:** As an **Orchestrator Engineer**, I want the Desktop Runtime to fully implement the Common Execution & Orchestrator Interface Contract v1.0.1 worker protocol, so that it can be registered, leased, heartbeated, reconciled and cancelled by any compatible Orchestrator.

**Functional scope:** Worker registration with `runtimeType=desktop`, capability advertisement and `capabilityDigest` (integrity hint, not auth proof); job pull with prefix-namespace matching for `desktop.*` actions; lease accept; heartbeat during RUNNING (including during session-lock bounded wait); final result submission with CEC v1.0.1 run/step state envelope; reconcile after disconnect; attempt fencing to prevent duplicate irreversible side effects; cancellation acknowledgement. Use `Tracto.Mock.Orchestrator` (Stage 1 NuGet) for sprint acceptance; real Orchestrator for release gate.

**Acceptance criteria**

- **DT-18-AC01 [C] Worker registration.** Given a Desktop Runtime worker with correct `runtimeType=desktop`, capability list and `capabilityDigest`, when `RegisterWorker` is called against the mock Orchestrator, then registration is accepted and the worker is listed as available. `capabilityDigest` is an integrity hint only — the Orchestrator does not treat it as an authentication proof (C4 constraint).
- **DT-18-AC02 [C] Job pull and lease.** Given a queued job with `desktop.*` action namespaces, when the Desktop worker calls `PullJob` and the Orchestrator returns `PullJobResponse.JobOffered(LeaseGranted)`, then the worker calls `AcceptLease`, records the `leaseId` and `attemptId` and begins execution. A `PullJobResponse.NoPendingJob(NoJobAvailable)` response is handled gracefully with a configured idle poll interval before the next `PullJob` call.
- **DT-18-AC03 [C] Heartbeat during RUNNING.** Given a running Desktop job, when the executor sends periodic heartbeats within the configured interval, then the mock Orchestrator records them without lease expiry; a gap exceeding the configured heartbeat timeout causes the Orchestrator to mark the lease expired.
- **DT-18-AC04 [C] Final result submission.** Given a completed Desktop job, when `ProcessFinalResult` is called with the CEC v1.0.1 result envelope (run state, step results, errorCategory, unknownReason, needsAttention), then the mock Orchestrator accepts it and the lease is released. UNKNOWN state results carry `unknownReason` and `needsAttention=true`.
- **DT-18-AC05 [C] Reconciliation after disconnect.** Given a Desktop worker that lost network connectivity during a job, when it reconnects, then it calls `Reconcile` with the `attemptId` and journal evidence; the Orchestrator determines whether the attempt is still live or was superseded; no duplicate result is submitted.
- **DT-18-AC06 [C] Attempt fencing.** Given a reconnected Desktop worker that receives a duplicate lease offer for an `attemptId` it has already finalized, when it processes the duplicate, then it does not re-execute any side-effectful action; it either resumes at the journal-confirmed point or reports a duplicate-attempt error.
- **DT-18-AC07 [C] Cancellation acknowledgement.** Given the Orchestrator sending a cancellation request, when the Desktop worker receives it, then the current action is cancelled per DT-17, the release sequence runs and a CANCELLED or UNKNOWN final result is submitted within the configured cancellation timeout.
- **DT-18-AC08 [C] Namespace capability matching.** Given a job containing a `browser.navigate` action, when the Desktop worker evaluates it against its declared `desktop.*` capability namespace, then it rejects the job as incompatible and does not attempt to execute a browser action.
- **DT-18-AC09 [C] Synchronous service invocation.** Given a `service.invoke` action in a Desktop job script targeting a registered custom-service endpoint, when the step executes, then the Runtime calls the service synchronously, maps the response to a `serviceOutcome` (Success, BusinessRejection, TechnicalFailure, TransientFailure), and routes the result to `businessOutcome`; no direct third-party API call is made outside the registered service registry. The Runtime does not rebuild this routing — it reuses the Stage 1 implementation via the NuGet reference.
- **DT-18-AC10 [C] Asynchronous handoff.** Given a `service.invoke` that returns an acknowledged asynchronous handoff, when the Orchestrator receives the step result, then the run state is HANDED_OFF and the lease is released; the worker does not poll or wait for the downstream service result.
- **DT-18-AC11 [C] Schema and policy validation before first side effect.** Given a Desktop job script submitted for execution, when the Runtime begins processing, then signed-artifact trust verification and script schema/policy validation complete successfully before any `desktop.*` or `service.invoke` action executes a side effect; validation failure yields FAILED without executing any action.
- **DT-18-AC12 [C] No direct third-party API calls.** Given any `service.invoke` step, when the Runtime executes it, then the only outbound network call is to the registered service endpoint; calls to arbitrary URLs, external REST APIs or cloud services not in the approved service registry are rejected during script validation and never reach the network layer.

**Tests and delivery evidence:** Integration tests using `Tracto.Mock.Orchestrator` and `Tracto.Mock.BusinessService` NuGet packages covering all twelve ACs; heartbeat-gap expiry test; reconcile-after-disconnect test with simulated network failure; service-invocation tests for all five ServiceOutcome values (including HANDED_OFF); validation-before-side-effect test confirming no action executes on schema rejection; real-Orchestrator test is a release-gate prerequisite (not sprint acceptance).

**Traceability:** CEC-HOST-001 through CEC-HOST-009; Spec §2 Part A, §7 (service.invoke); Architecture AD-13; C3 approval (FAILED/TIMED_OUT/UNKNOWN); C4 approval (capabilityDigest); ST1-09 through ST1-15 contract (shared with Desktop by NuGet reference); DES-AT-10, DES-AT-21.

---

## DT-19 — Emit safe telemetry, evidence references and sanitized diagnostics

**User story:** As a **Support Engineer**, I want the Desktop Runtime to emit structured telemetry and sanitized diagnostic evidence without exposing secrets, secret-adjacent control content or user-identifiable data, so that engineers can reproduce and diagnose failures using the emitted evidence alone.

**Functional scope:** Structured step telemetry with action type, selector strategy used, timing, result category and evidence reference (not inline payload); screenshot capture with sensitive-region masking and central retention-policy control; selector diagnostic (strategy, scope, candidate count) without secret input; journal entries for crash/lock/uncertain events; all Desktop error codes map to CEC errorCategory (Spec Appendix A).

**Acceptance criteria**

- **DT-19-AC01 [W] Structured step telemetry.** Given a completed step (any result), when the telemetry record is emitted, then it includes `stepId`, `actionType`, `selectorStrategy`, `durationMs`, `resultCategory`, `errorCategory` (when applicable), `unknownReason` (when applicable) and an `evidenceRef` pointer — no inline secret, credential or control plaintext beyond the declared output fields.
- **DT-19-AC02 [W] Screenshot with masking.** Given an approved evidence checkpoint during a step that involves a password field, when the screenshot is captured, then the password field region is masked; the resulting image file is referenced by URI in the telemetry record, not embedded inline.
- **DT-19-AC03 [W] Screenshot withheld if masking unsafe.** Given a scenario where masking cannot be guaranteed (e.g. multi-monitor screenshot or modal covering the masking boundary), when the screenshot capture runs, then it is withheld entirely and the telemetry records `evidenceRef=null` with a `screenshotOmittedReason` field.
- **DT-19-AC04 [W] Selector diagnostic — no secret exposure.** Given a selector that fails to resolve, when the diagnostic is emitted, then it includes the attempted strategy (AutomationId, ClassName, ancestry path), the number of candidates found, and the sanitized window title — without including any parameter value that was sourced from a `secretRef` or a prior step's sensitive output.
- **DT-19-AC05 [S] Journal entries for uncertainty.** Given a step that produces UNKNOWN with `needsAttention=true`, when the journal entry is written, then it records the last confirmed action, the reason for uncertainty, the attempt fence ID and a timestamp; this entry survives a Runtime restart for reconciliation.
- **DT-19-AC06 [R] Error code mapping in telemetry.** Given each Desktop-specific error identifier appearing in telemetry, when the telemetry record is inspected, then `errorCategory` carries the CEC v1.0.1 contract value per Spec Appendix A and the Desktop-specific code is in a `detailCode` field; no unmapped code reaches the Orchestrator or log aggregator.
- **DT-19-AC07 [S] Telemetry retention controlled centrally.** Given any evidence screenshot, log file or telemetry record produced by the Desktop Runtime, when the retention policy is applied, then the decision to keep, redact or delete is made by the centrally configured policy, not by the individual worker; workers do not self-delete evidence.

**Tests and delivery evidence:** Telemetry schema test confirming all required fields present; screenshot-masking visual review; screenshot-withheld-if-unsafe test; selector-diagnostic sanitization review (automated scan for known secret pattern); journal-persistence-after-restart test.

**Traceability:** DES-007; Spec §8 REC-006, REC-007; §12 (evidence, screenshots); Spec Appendix A; CEC-HOST-007; ST1-13 (shared telemetry contract).

---

## DT-20 — Application certification and architecture POC gate completion

**User story:** As a **QA Lead**, I want all 23 application certification scenarios (DES-AT-01..DES-AT-23) executed as defined in Spec §9/§13, and all architecture deployment feasibility gates (POC-01..POC-10) satisfied per Architecture v1.2A, so that the Desktop Runtime exits its conditional freeze with both application coverage and infrastructure readiness confirmed.

**Functional scope:** This story covers two distinct tracks. Track A tests Desktop application interaction behaviour; Track B certifies deployment infrastructure. They share no acceptance criteria.

**Track A — Application certification (DES-AT-01..DES-AT-23):** Scenarios are those defined in Spec §9 and §13 exactly as written; their identifiers and descriptions are not redefined here. Each runs in isolated mode (Supervisor + Executor + mock Orchestrator) with recorded step journal and telemetry. Evidence is a sanitized screenshot where masking can be guaranteed, or sanitized non-image evidence (step journal, control state readback) where a screenshot would expose unmasked sensitive content.

**Track B — Architecture POC gates (POC-01..POC-10):** Infrastructure and deployment feasibility gates defined in Architecture v1.2A. These test deployment-level concerns (cold boot, DPAPI persistence across reboot, WSL/Docker startup, durable reconciliation, vault-backed session provisioning) that a mock Orchestrator cannot certify. Real infrastructure components are required. The pass/fail criteria for each gate are those in Architecture v1.2A — they are not redefined here. Per Architecture v1.2A, POC-10 (vault-backed connected unattended session creation) may remain a separate conditional release gate when connected unattended provisioning is not part of the initial release scope; the unconditional freeze requires only POC-01..POC-09.

**Acceptance criteria — Track A (Application certification)**

- **DT-20-AC01 [W] DES-AT-01 — UIA stable click.** Given a WPF app that is moved and resized during the test, when a selector identifies the same button by stable UIA properties and the click action executes, then the postcondition passes and the run is SUCCEEDED. The selector must not rely on screen coordinates or `RuntimeId`.
- **DT-20-AC02 [W] DES-AT-02 — Focus stolen.** Given an external process that steals foreground focus before an input action, when the Runtime detects the focus theft, then the action is withheld, no input reaches the wrong window, and a controlled fail/evidence record is produced.
- **DT-20-AC03 [W] DES-AT-03 — Java accessibility.** Given a Java Swing application with an accessible button, text field and tree node, when the script targets each by accessible name via Java Access Bridge, then all three actions succeed and their postconditions verify the expected state changes.
- **DT-20-AC04 [W] DES-AT-04 — Custom opaque UI.** Given a custom or opaque control with no UIA accessible path, when the Runtime evaluates it, then it returns the approved keyboard path if one exists, or an explicit unsupported result; no OCR or image-recognition attempt is made.
- **DT-20-AC05 [W] DES-AT-05 — Multi-step copy/paste.** Given two application fields in separate processes, when a script performs a full copy-paste workflow, then the exact Unicode value transfers correctly; clipboard contention from a competing handle is handled within the bounded retry window without failing on first attempt.
- **DT-20-AC06 [W] DES-AT-06 — Modal surprise.** Given an unknown popup that appears mid-script before a form submission, when the Runtime detects it, then the submission is blocked, no blind dismiss occurs, and sanitized evidence names the popup and records why the step did not proceed.
- **DT-20-AC07 [W] DES-AT-07 — Locked desktop.** Given a session that is locked during a running job within the configured policy limit, when the lock is active, then no GUI input is injected, heartbeats continue to the Orchestrator, and on unlock the job resumes or exits with a defined non-SUCCEEDED state — not falsely SUCCEEDED.
- **DT-20-AC08 [W] DES-AT-08 — Control-relative coordinates.** Given a UIA-anchored control that moves to a new screen position between selector resolution and the click, when the click action executes, then the UIA anchor is re-located at its new position and the bounded offset stays inside the control bounds.
- **DT-20-AC09 [S] DES-AT-09 — DPAPI secret handling.** Given a `{"ref":"secrets.passwordKey"}` reference in a script, when the Executor resolves it in the interactive Windows session, then the plaintext is applied to the target control; it does not appear in any log, telemetry entry, named pipe message or Linux-accessible path.
- **DT-20-AC10 [C] DES-AT-10 — Async API handoff.** Given a `service.invoke` step that returns an acknowledged asynchronous handoff, when the Orchestrator receives the result, then the run state is HANDED_OFF and the lease is released; the worker does not poll for the downstream result.
- **DT-20-AC11 [W] DES-AT-11 — Orchestrator restart during irreversible action.** Given the Orchestrator restarting while a Desktop worker is mid-click on a submit button, when the worker reconnects and calls `Reconcile`, then journal-based attempt fencing prevents a duplicate click; the step is not re-executed.
- **DT-20-AC12 [W] DES-AT-12 — Cancelled drag.** Given a drag-and-drop operation in progress when the job is cancelled, when cancellation completes, then the mouse button is released, no drag cursor persists, the desktop is usable and the run result is CANCELLED or UNKNOWN with `unknownReason: action.commitUncertain` as appropriate to the observed side effect.
- **DT-20-AC13 [W] DES-AT-13 — Wrong display profile.** Given a coordinate-dependent action submitted after the display resolution or DPI has changed from the certified baseline, when the Runtime detects the mismatch, then the action is rejected and `errorCategory=ENVIRONMENT, detailCode=desktop.displayProfileChanged` is returned without executing the action.
- **DT-20-AC14 [W] DES-AT-14 — File overwrite policy.** Given a script step that would overwrite an existing file, when the Runtime evaluates the policy, then the deny or confirm outcome is respected as configured; no file is overwritten contrary to policy.
- **DT-20-AC15 [W] DES-AT-15 — Reference application fixture coverage.** Given the full certified application fixture set (Win32/Notepad, WinForms, WPF, Java Swing), when each fixture is run with a recorded OS and application version, then all fixture scenarios produce reproducible evidence that can be re-executed by a second engineer on the same configuration.
- **DT-20-AC16 [W] DES-AT-16 — Two same-title windows.** Given two top-level windows with identical titles, when a selector that uniquely identifies one by process identity or ancestry is used, then only the correct window is targeted; if no unique disambiguation is possible, `errorCategory=TARGET_AMBIGUOUS` is returned.
- **DT-20-AC17 [W] DES-AT-17 — Volatile UIA RuntimeId across restart.** Given a target application that has restarted between selector authoring and execution, when the script runs, then the Runtime re-resolves the selector using stable properties and either locates the target or returns a clean `errorCategory=TARGET_NOT_FOUND`; it never uses a stale `RuntimeId`.
- **DT-20-AC18 [W] DES-AT-18 — Java Unicode packet rejection fallback.** Given a Java application that rejects Unicode WM_CHAR packet injection, when `desktop.keyboard.type` runs with the approved scancode/layout fallback strategy, then the expected text appears in the field without corruption or silent failure.
- **DT-20-AC19 [S] DES-AT-19 — Elevated target at standard identity.** Given a UIA element belonging to an elevated-integrity process, when the standard-integrity Executor attempts to interact with it, then the OS integrity boundary is detected, `errorCategory=POLICY_DENIED, detailCode=desktop.accessDenied` is returned, and no unsafe input injection is attempted.
- **DT-20-AC20 [W] DES-AT-20 — Overlay detection.** Given an unexpected update modal that occludes the intended target after selector resolution but before the click, when the focus/occlusion check runs, then the click is withheld, the overlay is named in sanitized evidence and the step fails with a defined error.
- **DT-20-AC21 [C] DES-AT-21 — Disallowed third-party API rejected.** Given a script that encodes a direct call to a disallowed third-party API URL, when script validation runs, then the script is rejected before any side effect; a `service.invoke` to a registered custom-service endpoint in the same script succeeds as expected.
- **DT-20-AC22 [W] DES-AT-22 — Pause exceeds policy limit.** Given a session-lock or other suspension that exceeds the configured pause policy limit, when the deadline fires, then the run closes with a classified outcome (`TIMED_OUT` or UNKNOWN), the lease is released and no indefinite pause occurs.
- **DT-20-AC23 [W] DES-AT-23 — Image-only control without UIA anchor.** Given a custom control that presents only as a rendered image with no UIA-accessible anchor and no keyboard path, when the Runtime evaluates it, then `errorCategory=ENVIRONMENT, detailCode=desktop.unsupportedControl` or `errorCategory=TARGET_NOT_FOUND` is returned per the published exclusion; no OCR or image-click attempt is made.
- **DT-20-AC24 [R] DES-AT repeatability.** Each of DES-AT-01..DES-AT-23 is repeatable across three successive isolated runs with no setup change; each run produces identical pass/fail evidence. Scenarios involving sensitive data (DES-AT-09, DES-AT-19) use a sanitized test credential with a known pattern; the automated evidence scan confirms no plaintext of that pattern appears in logs or telemetry.

**Acceptance criteria — Track B (Architecture POC gates)**

- **DT-20-AC25 [W] POC-01..POC-09 gates satisfied.** Given the real deployment stack (Supervisor + Executor + real or isolated Orchestrator as required by each gate's definition in Architecture v1.2A), when each of POC-01 through POC-09 is executed, then all nine gates pass against their v1.2A acceptance conditions. A mock Orchestrator may be used only where Architecture v1.2A explicitly permits it for that gate; gates that require durable reconciliation, DPAPI post-reboot, or WSL/Docker startup evidence require real infrastructure. Evidence for each gate is recorded and attached to the freeze decision. The pass criteria are those in Architecture v1.2A and are not redefined here.
- **DT-20-AC26 [W] POC-10 — Vault-backed connected session (conditional gate).** Given a compatible real Orchestrator and vault-backed credential provisioning, when a Desktop session is created on demand, a job is executed, and the session is cleaned up, then the Orchestrator records a consistent outcome and the provisioned credential is not retained beyond the session lifetime. Per Architecture v1.2A, this gate may remain a separate conditional release gate when connected unattended provisioning is not in the initial release scope; it does not block the unconditional freeze when POC-01..POC-09 are all satisfied.
- **DT-20-AC27 [W] Unconditional freeze declaration.** Given all 23 DES-AT Track A scenarios passing (DT-20-AC01..DT-20-AC23) and all POC-01..POC-09 Track B gates satisfied (DT-20-AC25), when the evidence package is assembled and signed off, then the unconditional implementation freeze is declared. POC-10 may remain open as a conditional gate per Architecture v1.2A; no other gate may be substituted or deferred.

**Tests and delivery evidence:** Per-scenario step journal and sanitized evidence (screenshot where masking is safe, otherwise sanitized non-image evidence) for DES-AT-01..DES-AT-23; real infrastructure evidence for POC-01..POC-09 per Architecture v1.2A requirements; POC-10 vault-backed provisioning evidence when in scope; automated evidence scan confirming no credential plaintext in any log, telemetry or evidence file.

**Traceability:** All Spec §§4–13 requirements; Spec §9 and §13 acceptance application matrix (DES-AT-01..DES-AT-23, definitions authoritative from spec); Architecture v1.2A POC-01..POC-10 gate criteria (authoritative, unchanged); AD-04 (Desktop Runtime as engineering critical path).

---

## Appendix — Requirement-to-Story Traceability

| Spec requirement | Story |
|----------------|-------|
| DES-001 Session eligibility | DT-01 |
| DES-002 Display baseline | DT-01 |
| DES-003 Foreground ownership | DT-01 |
| DES-004 Launch/attach | DT-02 |
| DES-005 Window enumeration | DT-03 |
| DES-006 Window state | DT-03 |
| DES-007 Exit handling | DT-02, DT-16 |
| DES-008 Unexpected modal | DT-03 |
| DES-009 User interference | DT-01, DT-16 |
| DES-010 Supervisor integration | DT-04 |
| UIA-001 Stable UIA queries | DT-05 |
| UIA-002 Unique target | DT-05 |
| UIA-003 Re-resolution | DT-05 |
| UIA-004 Visibility/enabled | DT-07 |
| UIA-005 UIA pattern preference | DT-07, DT-12 |
| UIA-006 Java Access Bridge | DT-06 |
| UIA-007 Keyboard fallback | DT-06 |
| UIA-008 Control-relative pointer | DT-06, DT-08 |
| UIA-009 No visual targeting | DT-05, DT-06 |
| UIA-010 Virtualized controls | DT-05, DT-13 |
| UIA-011 Provider failure | DT-07 |
| INPUT-001 Mouse | DT-08 |
| INPUT-002 Drag | DT-08 |
| INPUT-003 Hover/scroll | DT-08 |
| INPUT-004 Unicode typing | DT-09 |
| INPUT-005 Text entry strategy | DT-09 |
| INPUT-006 Clipboard isolation | DT-10 |
| INPUT-007 Copy-paste workflow | DT-10 |
| INPUT-008 Key hold cleanup | DT-09, DT-17 |
| INPUT-009 Sensitive text | DT-11 |
| CTRL-001 Text/rich text | DT-12 |
| CTRL-002 Buttons/toggles/radio | DT-12 |
| CTRL-003 Combobox/dropdown | DT-12 |
| CTRL-004 Lists/grids | DT-13 |
| CTRL-005 Trees/menus/ribbons | DT-13, DT-14 |
| CTRL-006 Tabs/modals | DT-14 |
| CTRL-007 File pickers | DT-14 |
| CTRL-008 Date/numeric/masked | DT-12 |
| CTRL-009 Office controls | DT-14 |
| REC-001 Explicit state waits | DT-15 |
| REC-002 Postcondition verification | DT-15 |
| REC-003 Focus theft/overlay | DT-01, DT-16 |
| REC-004 Irreversible uncertainty | DT-08, DT-15 |
| REC-005 Cancellation/cleanup | DT-17 |
| REC-006 App crash | DT-16 |
| REC-007 Selector diagnostics | DT-07, DT-19 |
| REC-008 Session lock recovery | DT-16 |
| REC-009 Service outcome routing | DT-18 |
| DES-AT-01–DES-AT-23 | DT-20 |
| Architecture POC-01–POC-10 | DT-20 |
