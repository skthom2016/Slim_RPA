# Tracto Terminal Runtime — Functional Requirements

**TRACTO / ARCHITECTURE-ALIGNED SPECIFICATION**  
Terminal Runtime — Functional Requirements  
Part A shared host responsibilities + Part B Controlled Command Execution Engine

**Version:** REVISED v1.1  
**Date:** 24 October 2026  
**Status:** Proposed independent Runtime functional baseline; architecture v1.2A unchanged.  
Implemented Stage 1 CEC v1.0.1 governs shared behavior; runtime and deployment certification are pending.

> **REVIEW NOTES (incorporated 2026-10-24):** This document has been updated to address gaps identified during alignment review against the Desktop Runtime Functional Spec v1.1 and Architecture Amendment v1.2A. All changes are marked with `[ADDED]` or `[FIXED]` annotations inline. The original DOCX is preserved at `Tracto_Terminal_Runtime_Functional_Spec_v1_1_Aligned.docx`.

---

## 1. Scope and Relationship to the Architecture

The Terminal Runtime is an independently packaged .NET worker for approved CMD, PowerShell and Bash technical interactions. It may invoke registered custom business services only through the common `service.invoke` action. It cannot serve as an unrestricted command shell, domain-processing engine, or bypass to third-party APIs/databases. It can run headlessly where certified.

This is one independently delivered .NET application in the Tracto Runtime family. The product still has three logical components: Designer, Runtime family and Orchestrator.

This document is Part A + Part B: Part A references mandatory shared host behavior; Part B defines only this runtime's own actions, environment, failure conditions and acceptance tests.

| Included in this document | Outside this Runtime |
|---|---|
| Controlled Windows process launching, CMD/PowerShell, approved WSL Bash, stdout/stderr streaming, stdin prompts and process recovery | Arbitrary shell scripting outside policy, unrestricted arbitrary commands, business calculations/branching, privilege escalation, SSH/remote execution unless separately approved |
| Runtime-specific technical actions, waits, recovery and certified fixtures | Business rules, calculations, enrichment, reconciliation, persistence, transaction compensation |
| Conformance to completed Common Execution Contract v1.0.1 | Designer AI generation, Orchestrator scheduling logic, other runtimes' action catalogues |

---

## 2. Part A — Shared Runtime Host Obligations

The following host obligations are required for every worker; the canonical wire, result and condition semantics belong to the Common Execution & Orchestrator Interface Contract. A runtime team may share .NET libraries or implement these separately, but must pass the same conformance fixtures.

### 2.0 Shared Host Contract Reference

This Runtime conforms to CEC-HOST-001 through CEC-HOST-009 in the Common Execution & Orchestrator Interface Contract v1.0.1. Those normative host requirements are defined once in the CEC rather than duplicated as HOST-001–HOST-009 here. Only engine-specific obligations are specified below.

All programmatic API invocations use `service.invoke` to a registered independently developed custom business service; no direct third-party business API or database integration is implemented in Tracto Runtime. Normal UI/website/shell interactions with authorized target applications remain in scope.

### 2.1 Invocation and Execution Model

1. A configured worker advertises its `runtimeType`, capabilities, worker version and supported schema majors.
2. Worker initiates authenticated outbound pull; receives a run/attempt-bound lease and immutable artifact reference.
3. Before side effects, verify signatures, input types, environment permissions, policy refs and workload compatibility.
4. Execute runtime-specific steps with bounded deadlines, technical pre/postconditions and action status evidence.
5. Handle a synchronous registered custom-service response or an acknowledged asynchronous handoff; never call third-party business APIs directly or make business decisions.
6. Write local run journal and send final result. Reconcile after disconnect; release job capacity and runtime-specific resources.

### 2.2 Worker/Session and Deployment Assumptions

One job per registered bot worker in v1. Worker processes are separate applications, and multiple workers may share a host only where capacity and session/input isolation allow. Isolated deployment uses the single Windows Runtime Host Supervisor to supervise WSL2/Docker/Orchestrator; it is shared infrastructure within the Runtime family, not an additional product component. Host/interactive-session POC-01–POC-10 gates remain those of architecture v1.2A.

The Terminal Runtime does not require a foreground interactive Windows desktop session. It operates headlessly in all supported deployment modes.

---

## 3. Part B — Terminal Contexts and Action Catalogue

| Context | v1 target behavior | Restriction |
|---|---|---|
| CMD | Run signed/allowlisted executable or fixed command template under approved Windows user | No raw unquoted user-string concatenation into `cmd.exe` |
| PowerShell | Run approved script/file/function with typed arguments, execution policy and constrained account | No policy bypass or unrestricted `Invoke-Expression` |
| WSL Bash | Run approved command/script under provisioned WSL distro and Linux identity | Explicit distro/user mapping; no assumed shared Windows/Linux secret store |
| Interactive prompts | Provide predeclared prompt patterns and bounded responses where safe | No open-ended LLM-guided terminal interaction |
| Long process | Stream bounded stdout/stderr; heartbeat and cancellation | No indefinite unmanaged background process |
| Registered custom business services | Use common `service.invoke` action rather than embed business processing in CLI | No calculating domain result in bash/powershell workflow |

### 3.1 Required Terminal Actions

| Action family | Required capabilities | Deferred / excluded |
|---|---|---|
| Environment | discover-shell, working-dir, version, user identity, executable capabilities | Remote cloud SSH fleet orchestration |
| Process | start-approved-command, wait, read-exit-code, terminate-owned-tree | Uncontrolled shell exec / privilege escalation |
| Stream | stdin bounded input, stdout/stderr capture, decode, output-capped streaming | Unbounded raw output dump to Orchestrator |
| Filesystem | list approved directory, verify file exists, read approved output artifact refs | Arbitrary exfiltration or unapproved deletion |
| Command policy | command template ID + typed arguments + allowed paths | Direct C# or unreviewed shell scripts as free-form steps |
| Control | technical `exitCodeEquals` / `fileExists` / `processExited` checks | Numeric business thresholds or application decisions |

### 3.2 Action Gap Classification Table

> **[ADDED — Terminal equivalent of Desktop §3.2. This section was missing from the original document and is required to align with the Desktop Runtime Functional Spec v1.1 pattern.]**

Every capability required to execute the §3.1 catalogue is classified below. "Existing" = already delivered in Stage 1 CEC packages. "Composition" = assembled from existing capabilities without new engine logic. "Internal" = new engine-specific behavior that does not appear on the CEC wire. "New" = new capability that requires Tracto-level design and acceptance testing.

| Capability | Classification | Notes |
|---|---|---|
| CEC lease/heartbeat/result/cancel wire | Existing | Stage 1 CEC-HOST-001–009; shared with all runtimes |
| `service.invoke` to registered service | Existing | Stage 1 CEC-005; no changes |
| Signed artifact validation + runtimeType check | Existing | Stage 1 CEC-001/CEC-012 |
| Approved shell detection (CMD/PS/WSL versions) | New | Must validate before admitting job (ENV-001) |
| Execution-identity verification (Windows/Linux) | New | Per-shell effective identity check before command (ENV-002) |
| Typed argument binding + injection prevention | New | Argument-vector API or fixed shell template (CMD-002, CMD-003) |
| PowerShell execution-policy enforcement | New | Module allowlist, language mode, signed script refs (CMD-004) |
| WSL distro selection and isolation | New | Explicit distro/user/container mapping; separate from Orchestrator WSL (ENV-006) |
| CMD batch script integrity check | New | Signed/hash-pinned batch references (CMD-005, CMD-009) |
| Bash script invocation with approved distro | New | Explicit distro/user/cwd, approved path (CMD-006) |
| stdout/stderr bounded stream capture | New | Per-chunk sequencing, buffer limits, truncation markers (STR-001) |
| Stdin bounded write | New | Approved input only; timeout on unrecognized prompts (STR-004) |
| Prompt pattern matching | Internal | Allowlisted patterns only; no open-ended AI decision (STR-005) |
| Exit-code capture and semantics | New | Independent of stdout wording (STR-002) |
| Output redaction of secrets | New | Mask tokens, env vars, password prompt responses (STR-007) |
| Owned process-tree cancel | New | OS-appropriate termination of job-owned children (TER-002) |
| Heartbeat during long command | Composition | Re-uses CEC heartbeat wire; adds progress summary (TER-007) |
| UNKNOWN classification after effectful command | Internal | Kill/disconnect after write → UNKNOWN terminal state; no replay (TER-006) |
| `service.invoke` handoff → HANDED_OFF run state | Existing | Stage 1 CEC-013 |
| Filesystem existence check | New | Technical only; no domain decision (CMD-007) |
| Audit record (template ID, arg hashes, identity ref) | New | No raw secret values in audit (CMD-010) |

---

## 4. Shell Environment and Process Identity

### ENV-001 Approved Shell Availability

**Requirement:** Detect configured `cmd.exe`, PowerShell and WSL Bash executables and versions before admission.

**Acceptance:** Unsupported shell/version returns `errorCategory=ENVIRONMENT` and `detailCode=terminal.shellUnavailable` without executing a command.

### ENV-002 Execution Identity

**Requirement:** Run commands under authorized Windows/Linux identities with policy-specified privileges and per-shell effective identity checks.

**Acceptance:** A script cannot silently elevate or run as another user.

### ENV-003 Working Directory

**Requirement:** Resolve and validate path roots before command execution; prevent traversal outside policy where feasible.

**Acceptance:** `../` or absolute path outside allowlist is rejected.

### ENV-004 Environment Variables

**Requirement:** Pass explicit approved variables and secret references only in transient scoped process context; avoid dumping environment.

**Acceptance:** Secret environment variable absent from logs and command audit records.

### ENV-005 Process Creation

**Requirement:** Launch command with typed argument vector where supported and configurable stdin/out/err redirection.

**Acceptance:** Arguments containing spaces/metacharacters are passed literally and cannot inject a second command.

### ENV-006 WSL Distro Selection

**Requirement:** Resolve configured, provisioned distro and Linux user explicitly; distinguish WSL control-plane distro if shared.

**Acceptance:** Wrong/missing distro is rejected rather than starting an unintended one.

> **[NOTED — WSL control-plane isolation]** The Architecture v1.2A requires that the Orchestrator control-plane WSL2/Docker environment is managed by the shared Windows Runtime Host Supervisor and must not be used for customer command execution. The precise customer distro/container arrangement — including filesystem and network isolation details — is an implementation decision to be approved and tested against the Mode Certification gate (Appendix F), not frozen here. The required outcome is: customer WSL Bash commands must not run inside the Orchestrator's WSL environment (see TER-AT-23); the specific provisioning mechanism follows the approved deployment policy.

### ENV-007 Host Isolation

**Requirement:** Honor one active job per worker and host resource limits; interactive Windows GUI session not required.

**Acceptance:** Terminal worker operates while desktop is logged out if identity/context supports it; no GUI input attempted.

### ENV-008 Remote Contexts

**Requirement:** Remote SSH or cloud shell contexts are not implicitly supported by Windows Bash feature.

**Acceptance:** Unconfigured remote target is rejected, not silently connected.

---

## 5. Approved Command and Parameter Model

### CMD-001 Allowlisted Command References

**Requirement:** Execute only preapproved executable and argument templates referenced by signed automation definitions and deployment policy. No separate signed-command-template artifact framework is mandatory in v1.

**Acceptance:** Unregistered command ID yields `errorCategory=POLICY_DENIED` and `detailCode=terminal.commandNotAllowed`.

### CMD-002 Typed Argument Binding

**Requirement:** Validate argument schema, quoting, path/URL policies and maximum size before process start.

**Acceptance:** Input with `&`, `|`, `;` or backticks remains literal or is rejected according to shell policy.

### CMD-003 Shell Injection Prevention

**Requirement:** Use argument-vector APIs where possible and carefully configured fixed shell templates otherwise; no dynamic eval or expression interpolation.

**Acceptance:** Malicious user value cannot trigger an additional OS command.

### CMD-004 PowerShell Safe Operation

**Requirement:** Honor execution policy, signed script references where required, module allowlist, profile isolation and explicit language mode.

**Acceptance:** Attempted `-ExecutionPolicy Bypass` or arbitrary `Invoke-Expression` rejected by policy.

### CMD-005 CMD Batch Scripts

**Requirement:** Allow only approved versioned batch scripts with declared inputs and output contracts.

**Acceptance:** Edited unsigned `.bat` template fails integrity check.

### CMD-006 Bash Script Invocation

**Requirement:** Allow approved Bash command/script references with fixed distro/user/cwd, stdin/stdout policy and duration.

**Acceptance:** WSL user mismatch or script outside approved path fails before execution.

### CMD-007 No Business Logic in Commands

**Requirement:** Tracto-owned command workflows may inspect technical exit codes and files but must not implement domain calculations or approvals.

**Acceptance:** Sample `if invoiceAmount > 10000` logic in Terminal script is rejected by common schema.

### CMD-008 Idempotency Classification

**Requirement:** Mark whether command is read-only, idempotent or potentially irreversible and restrict automatic retry accordingly.

**Acceptance:** A financial export command is not replayed after ambiguous timeout.

### CMD-009 Executable and Script Integrity

**Requirement:** Verify signed/hash-pinned command artifact or approved executable identity before launch.

**Acceptance:** Binary/script digest mismatch returns policy error.

### CMD-010 Command Audit Reference

**Requirement:** Record template ID, argument names, hashes of approved artifacts, run/attempt and effective identity ref without raw sensitive values.

**Acceptance:** Audit reconstructs which command definition ran without revealing secret parameters.

---

## 6. Streams, Prompts and Output Management

### STR-001 stdout/stderr Separation

**Requirement:** Stream and label stdout vs stderr with bounded buffering, chunk ordering/sequence and truncated-output indicators.

**Acceptance:** Large stderr burst does not deadlock process; output limit is enforced.

> **[ADDED — Stream Buffer Limits]** The runtime must enforce normative buffer limits to prevent deadlock and memory exhaustion. Default limits (overridable in deployment policy):
> - Per-chunk read size: 65,536 bytes (64 KB)
> - Total stdout capture per step: 10,485,760 bytes (10 MB) — truncated with `terminal.outputTruncated` marker
> - Total stderr capture per step: 1,048,576 bytes (1 MB) — truncated with `terminal.stderrTruncated` marker
> - Stdin write per invocation: 65,536 bytes (64 KB)
>
> When output exceeds the limit, the runtime must write a truncation marker to the step's evidence record and continue (not deadlock). Output beyond the limit is discarded, not sent to the Orchestrator. See §12 Defaults table for configurable overrides.

### STR-002 Exit-Code Semantics

**Requirement:** Capture exit code and process completion independently from stdout wording.

**Acceptance:** Exit code 0 with warning output is distinguishable from nonzero with partial output.

### STR-003 Encoding and Line Endings

**Requirement:** Support declared console encodings including UTF-8, Unicode Windows command output and newline normalization.

**Acceptance:** Unicode filename and non-ASCII output round-trip in certified shells.

### STR-004 Bounded Stdin

**Requirement:** Send explicitly provided standard input or approved prompt responses, with limits and timeouts.

**Acceptance:** Interactive yes/no prompt receives approved response; unknown prompt causes safe timeout/attention.

### STR-005 Prompt Matching

**Requirement:** Match only allowlisted stable terminal prompt patterns, not open-ended freeform AI decisions.

**Acceptance:** Unrecognized interactive credential prompt never receives guessed input.

### STR-006 Output Mappings

**Requirement:** Bind declared JSON/text/exit code or artifact references to step outputs; avoid parsing business rules inside Tracto.

**Acceptance:** Structured output is passed to API unchanged; no numeric approval comparison performed.

### STR-007 Output Redaction

**Requirement:** Mask secret arguments, tokens, environment keys, password prompts and sensitive stdout/stderr segments by policy.

**Acceptance:** Secret injected into process not present in collected evidence or telemetry.

### STR-008 Output Artifacts

**Requirement:** Store only approved result files/metadata under bounded storage policy with signed/evidenced references.

**Acceptance:** Output path outside approved root or oversize data is rejected, summarized or truncated safely.

---

## 7. Lifecycle, Deadlines and Recovery

### TER-001 Process Deadline

**Requirement:** Terminate or report timeout at declared step/run deadline, including blocked prompts and child processes.

**Acceptance:** Never leaves a shell waiting indefinitely for Enter or additional input.

### TER-002 Owned Process-Tree Cleanup

**Requirement:** On cancellation/timeout terminate job-owned child process tree safely according to OS/context policy.

**Acceptance:** All owned test child processes terminate; unrelated processes remain untouched.

### TER-003 Detached Process Policy

**Requirement:** Disallow detached/background tasks unless separately declared and reconciled through approved process handle/service pattern.

**Acceptance:** Script using background `&` is blocked unless approved template expressly supports lifecycle.

### TER-004 WSL Failure Handling

**Requirement:** Detect WSL distro unavailable, terminated/restarted or Linux command failure distinctly.

**Acceptance:** Injected WSL stop reports ENVIRONMENT with `terminal.wslUnavailable` detail, or terminal UNKNOWN if an effectful command cannot be reconciled where irreversible work was pending.

### TER-005 Transient Read-Only Retry

**Requirement:** Retry only approved safe commands and known transient shell failures within configured limits.

**Acceptance:** Network read-only command can retry; destructive command after lost response never replays blindly.

### TER-006 Command Side-Effect Uncertainty

**Requirement:** Classify process kill/transport break after write as UNKNOWN if durable effect cannot be established.

**Acceptance:** Lost exit acknowledgement after file write yields UNKNOWN without duplicate write.

> **[FIXED — UNKNOWN_OUTCOME clarification]** When the runtime cannot establish whether an effectful command completed (e.g., process killed mid-write, transport break after stdout flush), the run terminates with run **state** = UNKNOWN (CEC-016 terminal state). The `errorCategory` on the terminal step is set to `TRANSPORT` (if a network/process-communication failure caused the uncertainty) or `INTERNAL` (if state is indeterminate due to an engine error); `unknownReason` is populated with the C3-approved code. `UNKNOWN_OUTCOME` is a `serviceOutcome` value returned by `service.invoke` and must never appear as an `errorCategory` or run state override.

### TER-007 Progress and Heartbeat

**Requirement:** Report bounded progress/stream summaries via outbound heartbeats while a long command runs.

**Acceptance:** Orchestrator restart reconnects and reconciles attempt without launching duplicate command.

### TER-008 Filesystem Cleanup

**Requirement:** Remove owned temporary files per policy, preserve required results and never delete files beyond authorized roots.

**Acceptance:** Cancelled command leaves no leaked temp secrets while preserving approved diagnostics.

### TER-009 Business API Handoff

**Requirement:** Use `service.invoke` action to submit opaque request and finish HANDED_OFF on accepted async processing.

**Acceptance:** Independent Terminal job ends and later callback triggers separate run by Orchestrator configuration.

---

## 8. Terminal Reference Environment Matrix

| Fixture | Test intent | Release status |
|---|---|---|
| Windows CMD on certified build | Argument quoting, exit code, Unicode echo, stderr, timeout | Required |
| PowerShell version in certified matrix | Signed script, policy denial, JSON output, cancellation | Required |
| WSL Bash inside approved distro | Effective identity, cwd, stdout/stderr, distro unavailable | Required if WSL Bash advertised |
| Long-running process fixture | Heartbeat, bounded output, cancel and tree cleanup | Required |
| Prompt-driven CLI fixture | Approved response, unexpected prompt fail | Required |
| Injection/unauthorized command fixture | Untrusted shell metacharacters, path traversal, escalation | Required negative test |
| Irreversible file-write fixture | Unknown completion and no duplicate retry | Required |

### Acceptance Scenario Catalogue

| Test ID | Scenario | Observable pass criterion |
|---|---|---|
| TER-AT-01 | CMD safe argument | Metacharacters supplied as data cannot run secondary command. |
| TER-AT-02 | PowerShell policy | Unsigned/unapproved command fails before launch. |
| TER-AT-03 | Bash provision | Certified distro/user executes approved command and returns exit code. |
| TER-AT-04 | Output flood | Large stdout/stderr does not deadlock; output limit and truncation recorded. |
| TER-AT-05 | Interactive prompt | Approved prompt answered; unknown prompt times out. |
| TER-AT-06 | Encoding | Unicode CLI output round-trips using declared encoding. |
| TER-AT-07 | Long-running cancel | Owned child tree terminated; no unrelated process affected. |
| TER-AT-08 | Unknown side effect | After file-write acknowledgement loss, run is UNKNOWN and action not repeated. |
| TER-AT-09 | WSL outage | Worker reports WSL unavailable and reconciles safely. |
| TER-AT-10 | API business rejection | Technical run SUCCEEDED with businessOutcome REJECTED where downstream action handled. |
| TER-AT-11 | Secret resolution | Credential passed only to approved process/API; absent from logs and Orchestrator. |
| TER-AT-12 | Host concurrency | Headless command works beside Desktop worker with capacity isolation; no interactive input lease needed. |
| TER-AT-13 | Business expression | Script author cannot encode `response.amount > threshold` to branch. |
| TER-AT-14 | Control-plane restart | Worker journal prevents duplicate irreversible command. |
| TER-AT-15 | Unauthorized remote connection | SSH target not configured is refused. |

### Negative Tests and Unsupported-Scope Behavior

- No unrestricted arbitrary shell execution, free-form evaluation or domain-processing scripts in Tracto-owned templates.
- No privilege escalation, policy bypass, unapproved system modification or credential harvesting.
- No dependency on an interactive Windows desktop for CLI worker; do not claim Linux/remote host support beyond certified adapters.
- No mixed-engine Desktop/Browser/Terminal scripts in one run.
- No unbounded terminal session that outlives its run without explicitly provisioned lifecycle management.

### Reference Environments and Certification

Certify CMD, selected PowerShell versions and declared WSL distro/Bash variants with pinned Windows/WSL builds. External services may themselves execute complex shell processing as their own business implementation; that does not expand the Tracto Terminal Runtime responsibility. Recorder spike is not mandatory for Terminal v1; command references must be manually authorable and deterministic.

Each requirement and scenario is subject to a runtime-specific support matrix: OS/build, engine/runtime version, application/site/shell version, session class, script schema major, policy profile, result and evidence reference. Tests that pass only under special prerequisites are marked conditional rather than universally supported.

---

## 9. Terminal Command Catalogue and Normative Command Schema

Terminal v1 is a controlled technical command runner, not an API-integration fallback. Invoking `curl`, `wget`, PowerShell `Invoke-WebRequest`/`Invoke-RestMethod`, SQL clients, or embedded scripts to access third-party business APIs or databases is prohibited. There is no exception. Business-service integration must use `service.invoke`. Approved CLI application interactions (driving a local command-line tool as a UI target) remain in scope; using Terminal commands as an alternative programmatic integration channel to a third-party business API or database does not.

**[ADDED — Normative Command Schema]**  
The following schema fields apply to Terminal command-launch steps (`terminal.process.start`). They do not apply to steps that act on an already-running process (`terminal.process.await`, `terminal.stdout.read`, `terminal.stderr.read`, `terminal.stdin.write`, `terminal.process.cancel`) or to inspection actions (`terminal.file.exists`, `terminal.env.inspect`). The CEC step envelope remains authoritative for all steps; these fields extend it only for command-launch.

**Command-launch step fields (`terminal.process.start` only):**

| Field | Type | Required/Conditional | Description |
|---|---|---|---|
| `commandRef` | `string` (artifact-internal ID) | Required | References the preapproved command template in the signed artifact; resolved by the deployment command policy. No free-form shell string. |
| `shell` | enum: `cmd`, `powershell`, `wsl-bash` | Required | Declares the shell interpreter; must match the distro/version in the worker's support matrix. |
| `argumentBindings` | `object` (named typed args) | Required if template has parameters | Typed key-value pairs validated against the argument schema before process start. No dynamic interpolation. |
| `cwdRef` | `string` (approved-path reference) | Required | Policy-approved working directory reference; resolved and validated before launch. |
| `environmentRefs` | `object` (named secretRef/literalRef) | Conditional | Named approved environment variables; secret values passed via approved Windows-side secretRef, never as literals. |
| `timeoutMs` | `integer` | Required | Per-step deadline; must be ≤ the run-level deadline; must be > 0. |
| `idempotencyClass` | enum: `read-only`, `idempotent`, `irreversible` | Required | Governs automatic retry eligibility. `irreversible` steps are never retried after lost acknowledgement. |
| `wslDistroRef` | `string` | Conditional (wsl-bash only) | Approved distro name; must not be the Orchestrator's WSL environment. |
| `wslUserRef` | `string` | Conditional (wsl-bash only) | Approved Linux user identity in the target distro. |

**All Terminal steps (including post-launch and inspection steps) must declare:**

| Field | Type | Required | Description |
|---|---|---|---|
| `stdinRef` | `string` (approved-input reference) | Conditional (stdin.write only) | Approved stdin payload; must match an allowlisted prompt pattern. |
| `outputBindings` | `object` | Conditional | Bind exit code, stdout/stderr segments, or artifact refs to step outputs; no domain calculations. |
| `outputRedaction` | enum: `mask-stdout`, `mask-stderr`, `mask-all` | Required on all steps that produce output | Declares mandatory redaction policy for this step's evidence. There is no `none` option — output redaction cannot be disabled by script; the minimum policy is enforced by the deployment policy baseline. |

| Terminal action | Input parameters | Outputs / postcondition |
|---|---|---|
| `terminal.process.start` | `commandRef`, `argumentBindings`, `cwdRef`, `environmentRefs`, `timeout` | `processRef`, `pid`; approved process started. |
| `terminal.process.await` | `processRef`, `timeoutMs` | `exitCode`, `terminated`; deadline observed. |
| `terminal.stdin.write` | `processRef`, `approvedInputRef`, `maxBytes` | `writeCount`; authorized process receives input. |
| `terminal.stdout.read` | `processRef`, `maxBytes`, `encodingRef` | bounded `dataRef`; sanitized evidence. |
| `terminal.stderr.read` | `processRef`, `maxBytes`, `encodingRef` | bounded `dataRef`; error classification. |
| `terminal.process.cancel` | `processRef`, `policyRef` | `terminated`/`stillRunning`; owned process tree handled. |
| `terminal.file.exists` | `approvedPathRef` | boolean; technical existence only. |
| `terminal.env.inspect` | `allowedEnvRef` | safe `valueRef`; no secret exposure. |

Command references resolve to signed automation artifact contents plus centrally managed allowlists; no new independent signed-command-template artifact type is introduced. Customer commands in WSL must run in a separate distribution/container from the WSL distribution hosting the Tracto Orchestrator, with explicit filesystem/network isolation. Technical commands may inspect files, drive an approved CLI application, and copy opaque outputs to a custom service. They may not perform domain calculations, direct external API integration, authorization decisions or unrestricted shell interpretation. Isolated `secretRef` resolution must occur through approved Windows-side providers; do not assume WSL Bash can decrypt Windows CurrentUser DPAPI directly.

---

## 10. Additional Terminal Acceptance Tests

| Test ID | Input / scenario | Pass criteria |
|---|---|---|
| TER-AT-16 | Command template attempts `curl` third-party finance API | Denied before process launch; no HTTP request emitted. |
| TER-AT-17 | Approved custom service invocation | `service.invoke` uses registered `serviceRef`; no shell-based HTTP bypass. |
| TER-AT-18 | Command attempts launch in Orchestrator WSL distro | Denied; customer shell runs in isolated approved environment. |
| TER-AT-19 | Command output contains API access token | Bounded output redacted, no token in telemetry. |
| TER-AT-20 | Unapproved arbitrary shell string from API result | Rejected at validation as dynamic command. |

---

## 11. Definition of Done and Release Traceability

All requirements have uniquely named automated or reproducible tests with positive and negative outcomes; requirement-to-test mapping is recorded. The complete CEC v1.0.1 conformance suite passes against the mock Orchestrator fixture and a real Orchestrator environment. Business-comparison predicates and dynamically selected scripts/commands are rejected before executing any side effect. Sensitive values are absent from logs, evidence, job messages, local crash dumps and container-mounted Tracto state. Supported capability claims are backed by named environment fixtures; unsupported apps/targets are published as known limits. Crash, timeout, focus/session (if applicable) and control-plane restart tests demonstrate correct cleanup, journal reconciliation and UNKNOWN semantics. The worker may be released independently of the other two Runtime applications only within declared script/Orchestrator compatibility.

---

## 12. Script Schema Defaults

> **[ADDED — This section was missing from the original document. It is required to match the Desktop Runtime Functional Spec v1.1 §12 pattern.]**

The following defaults apply unless the signed automation artifact explicitly overrides them. All overrides must be within the bounds specified in the deployment policy.

| Parameter | Default value | Notes |
|---|---|---|
| `shell` | (required; no default) | Must be explicitly declared: `cmd`, `powershell`, or `wsl-bash` |
| `powershellExecutionPolicy` | `AllSigned` | Deployment policy may tighten to `Restricted`; never `Bypass` |
| `powershellLanguageMode` | `ConstrainedLanguage` | Deployment policy may further restrict; never `FullLanguage` for untrusted scripts |
| `wslDistroRef` | (required for wsl-bash; no default) | Must reference an explicitly approved distro name |
| `idempotencyClass` | `irreversible` | Conservative default; opt into `read-only` or `idempotent` explicitly |
| `timeoutMs` (per step) | `30000` | Maximum configurable: bounded by run-level deadline |
| `maxStdoutBytes` | `10485760` | 10 MB; excess triggers `terminal.outputTruncated` marker |
| `maxStderrBytes` | `1048576` | 1 MB; excess triggers `terminal.stderrTruncated` marker |
| `maxStdinBytes` | `65536` | 64 KB; excess rejected before write |
| `stdinPromptTimeoutMs` | `10000` | Unrecognized prompt causes `needsAttention` and timeout after this interval |
| `retryCount` | `0` for `irreversible`; `3` for `read-only` | `idempotent` commands may retry up to 3 times at implementation discretion |
| `outputRedaction` | `mask-all` | Default is maximum redaction. Scripts may narrow to `mask-stdout` or `mask-stderr` where deployment policy permits; `none` is not a valid option — redaction cannot be disabled by script |
| `heartbeatIntervalMs` | `15000` | Worker heartbeat to Orchestrator during long-running commands |
| `processTreeCleanupMs` | `5000` | Grace period before forced kill of child process tree on cancel/timeout |
| `allowedPathRoots` | (required in deployment policy; no default) | Explicit approved roots for cwd, file operations and output artifacts |
| `envVarExposurePolicy` | `approved-list-only` | Only explicitly declared `environmentRefs` pass to process; no ambient env forwarding |

---

## Appendix A. Normative v1.1 Alignment with Completed Stage 1

The shared Stage 1 contract v1.0.1 is authoritative for all script envelopes, service outcomes, run states, wire semantics, cancellation, security and conformance. Engine-specific content in this specification must not override it.

| Common boundary | Required implementation / acceptance | Authority |
|---|---|---|
| Published artifact | Validate signed immutable artifact digest, runtimeType, schema major, typed references, declared targets and policy refs before any action. | CEC-001; CEC-HOST-001; CEC-012 |
| Closed behavior | Accept only the CEC technical-condition grammar, forward-only branching, bounded iteration, approved actionCode equality and typed pass-through; never business arithmetic or dynamically selected scripts. | CEC-002; CEC-011; CEC-HOST-009 |
| Registration/pull | Use the implemented Stage 1 protocolVersion, capability advertisement, supported schema majors, authenticated outbound register/pull, lastSeenAttemptId and lease grant inputs. No Runtime listener for routine dispatch. | CEC-HOST-003; CEC-015 |
| Leases/fencing | One active job per registered worker. Persist runId, attemptId, leaseId, heartbeat sequence, checkpoints and final results. Expired leases require reconciliation; never assume failure or replay irreversible work. | CEC-004; CEC-HOST-002 |
| Run states | Use CREATED/QUEUED/LEASED/RUNNING and terminal SUCCEEDED/HANDED_OFF/FAILED/CANCELLED/TIMED_OUT/UNKNOWN. **UNKNOWN is a terminal run state that stops the attempt; it is not an error code.** `needsAttention` is a flag, not a new state. | CEC-016; CEC state model |
| Service invocation | Use `service.invoke` only for registered custom business-service operations. Service outcome values: `SUCCESS`, `BUSINESS_REJECTION`, `TECHNICAL_FAILURE`, `TRANSIENT_FAILURE`, `UNKNOWN_OUTCOME`; `businessOutcome` separately. `HANDED_OFF` is a **run state** after confirmed asynchronous acknowledgment — it is not a service outcome. | CEC-005; CEC-013; CEC-HOST-004/005 |
| Technical continuation | A SUCCEEDED run or authenticated deduplicated callback may create one Orchestrator-configured successor run; neither script, Runtime, nor business service dynamically chooses next automation. | CEC-008; CEC §9 |
| Error/evidence | Use the 11 canonical `errorCategory` names and namespaced `terminal.*` detail codes. Redact secrets in job messages, diagnostic evidence, streams, dumps and offline packages. | CEC §6.2; CEC-HOST-008 |
| Version and testing | Initial script schema major is v1; test v1 and rejection of unsupported majors. N/N-1 tests apply once a real previous major exists. Pass completed Stage 1 conformance fixtures against mock, then real compatible Orchestrator before release. | CEC-007; CEC-010; CEC §12 |
| Modes and ownership | Connected: central Linux-container Orchestrator; isolated: same image in Docker Engine/WSL2 under single shared Windows Host Supervisor. These are not additional Tracto product components. | Architecture v1.2A §§5,9 |

---

## Appendix B. Engine-Specific Safeguards and Design Limits

| Subject | Mandatory v1.1 rule |
|---|---|
| Command reference integrity | `commandRef` resolves to a preapproved command in the signed script and deployment command policy; do not add a fourth artifact type, dynamic executable selection, or an arbitrary string shell interface. |
| Argument security | Prefer process argument-list APIs and typed bindings. For CMD/PowerShell/Bash templates that require shell syntax, templates are fixed, policy-certified and reject unsafe dynamic interpolation. Inputs cannot become another command. |
| WSL separation | In isolated mode, host Supervisor manages WSL2/Docker Orchestrator. Terminal WSL Bash work belongs in an independently approved distro/container and must not run customer commands inside the control-plane Orchestrator environment. |
| Network boundary | Shell commands and scripts must not use `curl`, `wget`, `Invoke-WebRequest`, `Invoke-RestMethod`, drivers or database clients as business integration escape. Only `service.invoke` calls registered custom business service operations; approved target CLI interactions are allowed. |
| Output and secrets | stdout/stderr remain bounded and separately sequenced per §12 defaults. Raw secret or business data must not enter telemetry; approved opaque output refs may pass unchanged to `service.invoke`. Secret material is Windows-side only; WSL Linux does not directly decrypt Windows CurrentUser DPAPI. |
| Noninteractive and safety | Terminal is noninteractive and never requires a foreground desktop input lease. One active job per worker. Unknown side-effect on killed command or lost exit receipt produces terminal UNKNOWN; cancel only owned process tree. |
| Certification | Independent Terminal fixture cases TER-AT-01–TER-AT-28 retain their IDs. Architecture deployment POC-01–POC-10 are separate and only apply to supported deployment claims. |

---

## Appendix C. Error Taxonomy

> **[ADDED — Error taxonomy corrected to use the exact 11 canonical CEC errorCategory values from Common Execution & Orchestrator Interface Contract v1.0.1 §6.2. Terminal-specific diagnostics are carried in the namespaced `terminal.*` detailCode only. The Runtime implementation must not introduce new shared enum values.]**

All 11 canonical `errorCategory` values are defined in CEC §6.2 and are reproduced here with terminal-specific `detailCode` examples. These values are fixed by Stage 1 and shared across all Runtimes; they cannot be changed independently by a Runtime specification.

| `errorCategory` (CEC §6.2, exact) | Terminal-specific `detailCode` examples | When used |
|---|---|---|
| `VALIDATION` | `terminal.commandRefInvalid`, `terminal.argumentSchemaViolation`, `terminal.mixedEngineScript`, `terminal.businessExpressionRejected` | Artifact fails schema check before process launch |
| `POLICY_DENIED` | `terminal.commandNotAllowed`, `terminal.pathTraversalDenied`, `terminal.shellEscapeAttempt`, `terminal.wslDistroNotApproved`, `terminal.networkBypassAttempt` | Command blocked by deployment policy |
| `TARGET_NOT_FOUND` | `terminal.fileNotFound`, `terminal.processNotFound` | Expected file or process handle not available |
| `TARGET_AMBIGUOUS` | `terminal.ambiguousPromptMatch` | Multiple allowlisted prompt patterns match simultaneously |
| `TIMEOUT` | `terminal.stepDeadlineExceeded`, `terminal.promptTimeout`, `terminal.processHangTimeout` | Step exceeded bounded wait |
| `SESSION_UNAVAILABLE` | `terminal.identityUnavailable`, `terminal.wslSessionUnavailable` | Required execution identity or session context not available |
| `ENVIRONMENT` | `terminal.shellUnavailable`, `terminal.wslUnavailable`, `terminal.executableNotFound`, `terminal.shellVersionUnsupported` | Shell/distro/executable missing or misconfigured |
| `TRANSPORT` | `terminal.shellStartFailure`, `terminal.streamReadError`, `terminal.processStartFailed` | Transport or process-communication failure |
| `AUTHENTICATION` | `terminal.identityMismatch`, `terminal.secretRefUnavailable` | Execution identity or secret reference invalid |
| `CANCELLED` | `terminal.runCancelled` | Run cancelled by Orchestrator |
| `INTERNAL` | `terminal.internalRuntimeError` | Unexpected internal engine error |

> **Note on UNKNOWN run state:** UNKNOWN is a **terminal run state** (CEC-016), not an `errorCategory`. When an effectful command (classified `irreversible` or `idempotent`) produces uncertain results due to process kill or transport break, the run terminates with run state = UNKNOWN. The step-level `errorCategory` is set to `TRANSPORT` or `INTERNAL` as appropriate; `unknownReason` is populated with the C3-approved code.

> **Note on UNKNOWN_OUTCOME:** `UNKNOWN_OUTCOME` is a `serviceOutcome` value returned by `service.invoke` only. It must never be used as an `errorCategory`. When a service returns `UNKNOWN_OUTCOME`, the attempt terminates as UNKNOWN; further execution requires authorized external reconciliation.

> **Note on business rejection:** `BUSINESS_REJECTION` is not a CEC `errorCategory`. A service returning `serviceOutcome=BUSINESS_REJECTION` produces run state `SUCCEEDED` with `businessOutcome` set.

---

## Appendix D. Additional Independent Runtime Acceptance Tests

The following cases supplement the existing application/shell acceptance catalogue without reusing or changing any previous test identifiers. They are functional test obligations, not new platform capabilities.

| Test ID | Test scenario | Acceptance evidence required |
|---|---|---|
| TER-AT-21 | Signed artifact, runtime namespace | Wrong-runtime, unsigned or mixed `terminal.*` + `browser.*` artifacts fail validation before process launch. `commandRef` resolves inside approved signed artifact and policy, never from dynamic service result. |
| TER-AT-22 | Safe process admission | Typed args containing shell operators are treated as literal data or rejected; no second command starts, no unapproved effective identity or path traversal is possible. |
| TER-AT-23 | WSL execution isolation | WSL Bash command runs only in an approved customer command distro, never in the WSL distro/container hosting Tracto Orchestrator; unapproved or unavailable distro returns structured ENVIRONMENT/POLICY_DENIED detail. |
| TER-AT-24 | All five service outcomes | Fake registered service returns each of `SUCCESS`, `BUSINESS_REJECTION`, `TECHNICAL_FAILURE`, `TRANSIENT_FAILURE`, `UNKNOWN_OUTCOME`; branch only on category or approved `actionCode`, and never on domain fields. |
| TER-AT-25 | Async callback + separate run | Confirmed async service acknowledgment ends source run HANDED_OFF; authenticated deduplicated callback causes one statically configured new run; dynamic `scriptRef` in event is refused. |
| TER-AT-26 | No shell integration escape | Command templates invoking `curl`/`wget`/`Invoke-WebRequest`/`Invoke-RestMethod`/SQL client to third-party business API/database are denied; registered custom service via `service.invoke` succeeds. |
| TER-AT-27 | Unknown and cleanup | Kill or communication loss after effectful write with unknown commitment produces terminal UNKNOWN and `needsAttention`; no replay; terminate owned processes and protect unrelated ones. |
| TER-AT-28 | Stage 1 and deployment conformance | Run relevant CF-01–CF-10 and mock fault tests; real Orchestrator integration before release; headless Terminal never requires `interactiveSessionId` or foreground input lease. |

---

## Appendix E. Requirement-to-Test Traceability

Each original Part B requirement retains its identity. Test references below name the primary certification evidence; testers must also execute the Given/When/Then acceptance in each requirement block and add positive/negative boundaries where applicable.

| Requirement | Mapped primary fixture tests | Capability group |
|---|---|---|
| ENV-001 | TER-AT-02, 03 | Environment |
| ENV-002 | TER-AT-02, 03 | Environment |
| ENV-003 | TER-AT-01, 16 | Environment |
| ENV-004 | TER-AT-11, 19 | Environment |
| ENV-005 | TER-AT-01, 22 | Environment |
| ENV-006 | TER-AT-03, 09, 23 | Environment |
| ENV-007 | TER-AT-12, 28 | Environment |
| ENV-008 | TER-AT-15 | Environment |
| CMD-001 | TER-AT-01, 21 | Target/DOM |
| CMD-002 | TER-AT-01, 22 | Target/DOM |
| CMD-003 | TER-AT-01, 22 | Target/DOM |
| CMD-004 | TER-AT-02 | Target/DOM |
| CMD-005 | TER-AT-02 | Target/DOM |
| CMD-006 | TER-AT-03, 23 | Target/DOM |
| CMD-007 | TER-AT-13, 20 | Target/DOM |
| CMD-008 | TER-AT-08, 27 | Target/DOM |
| CMD-009 | TER-AT-02, 21 | Target/DOM |
| CMD-010 | TER-AT-11, 21 | Target/DOM |
| STR-001 | TER-AT-04 | Interaction/streams |
| STR-002 | TER-AT-03, 06 | Interaction/streams |
| STR-003 | TER-AT-06 | Interaction/streams |
| STR-004 | TER-AT-05 | Interaction/streams |
| STR-005 | TER-AT-05 | Interaction/streams |
| STR-006 | TER-AT-10, 24 | Interaction/streams |
| STR-007 | TER-AT-11, 19 | Interaction/streams |
| STR-008 | TER-AT-04, 07 | Interaction/streams |
| TER-001 | TER-AT-05, 07 | Recovery |
| TER-002 | TER-AT-07, 27 | Recovery |
| TER-003 | TER-AT-07, 20 | Recovery |
| TER-004 | TER-AT-09, 23 | Recovery |
| TER-005 | TER-AT-08, 27 | Recovery |
| TER-006 | TER-AT-08, 27 | Recovery |
| TER-007 | TER-AT-14, 28 | Recovery |
| TER-008 | TER-AT-07, 19 | Recovery |
| TER-009 | TER-AT-10, 24, 25 | Recovery |

---

## Appendix F. Independent Release and Architecture Gates

| Gate | Exit evidence / restriction |
|---|---|
| Engineering build gate | Engine binaries build under approved .NET baseline; behavior is deterministic and isolated from other Runtime source trees. No Runtime application needs Desktop or the other parallel track to be completed. |
| Contract gate | Applicable Stage 1 fixtures (CF-01–CF-10) pass against mock and fake registered service; all five outcomes, cancellation, attempt fencing, unknown handling and signed artifact validation are exercised. |
| Security gate | Tests show no secrets in script, events, logs, evidence, traces, stream dumps or Orchestrator. Reject unapproved business expression and third-party API/database integration bypass before effectful launch. |
| Mode certification | Terminal: supported CMD/PowerShell; WSL Bash only in certified separate distro (verified by TER-AT-23); no interactive-session claim or SSH unless separately approved. WSL distro/container isolation from Orchestrator WSL is verified here. |
| Real integration gate | Before production Runtime release, test against an actual compatible Orchestrator using unchanged Stage 1 logical protocol, including restart/reconcile, cancellation and job handoff. |
| Deployment PoC gate | Preserve architecture POC-01–POC-10 meanings and applicability. An engine fixture test does not certify unattended host boot, session provisioning, secret recovery or WSL restoration. Conditional features remain conditional. |
| Scope gate | No added cross-engine action, LLM runtime reasoning, business rule engine, direct business API escape, general-purpose scripting or fourth Tracto component. |

### Source Hierarchy and Unresolved Implementation Choices

1. Frozen Tracto Architecture v1.2A (AD-01–AD-14)
2. Completed Common Execution & Orchestrator Interface Contract v1.0.1 and published packages
3. This independent Runtime action and target specification
4. Approved implementation decisions, deployment policies and named certification fixtures

This document does not assert which PowerShell or WSL builds have passed real-world certification. Resolve exact transport bindings, signed package availability, library versions and configured thresholds against the implemented Stage 1 packages and approved deployment policies. Do not treat illustrative namespaced detail codes or proposed test fixtures as new shared protocol fields.
