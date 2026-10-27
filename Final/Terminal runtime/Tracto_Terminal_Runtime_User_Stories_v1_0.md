# Tracto Terminal Runtime — User Stories v1.0

**Product:** Tracto Terminal Runtime  
**Version:** 1.0  
**Date:** 24 October 2026  
**Status:** Ready for sprint planning — pending baseline approval of functional spec  
**Source spec:** `Tracto_Terminal_Runtime_Functional_Spec_v1_1_Aligned.md`  
**Contract authority:** Common Execution & Orchestrator Interface Contract v1.0.1  

---

## How to Read This Document

Each story has:
- **Story ID** — `TER-US-NNN` (unique, stable, never reused)
- **Epic** — the feature group this story belongs to
- **Requirement refs** — one or more functional requirement IDs from the spec (e.g. `ENV-001`, `CMD-003`)
- **Acceptance tests** — the spec acceptance-test IDs that must pass (e.g. `TER-AT-01`)
- **Acceptance criteria** — testable conditions written in plain language; each maps to the Given/When/Then in the spec requirement block
- **Definition of Done** — conditions that apply to every story in this document (listed once below)

### Universal Definition of Done

All stories in this document are Done when:

1. The implementation passes every listed acceptance test with both positive and negative outcomes documented.
2. No secret value (credential, environment key, token, password prompt response) appears in logs, evidence artifacts, job messages, audit records, crash dumps, or Orchestrator payloads.
3. Business-comparison predicates and dynamically supplied shell strings are rejected before any process is launched.
4. The CEC v1.0.1 conformance suite (CF-01–CF-10) passes against the mock Orchestrator fixture.
5. Code review confirms no cross-engine script dependency (no `browser.*` or `desktop.*` action references).
6. The requirement-to-test mapping in Appendix E of the spec is updated to include any new test IDs.

---

## Epic 1 — Shell Environment and Process Identity

**Goal:** The Terminal Runtime verifies shell availability and execution identity before admitting any job, enforces working-directory policy, manages environment variables safely, and creates processes with typed argument vectors.

---

### TER-US-001 — Shell Availability and Version Validation

**As a** Tracto platform operator,  
**I want** the Terminal Runtime to verify that configured CMD, PowerShell, and WSL Bash executables are present and at approved versions before accepting any job,  
**so that** a job never attempts to execute a command in an unsupported or misconfigured shell.

**Requirement refs:** ENV-001  
**Acceptance tests:** TER-AT-02, TER-AT-03

**Acceptance criteria:**

1. When the runtime starts, it detects the available CMD, PowerShell, and WSL Bash executables and records their versions.
2. When a job's declared `shell` is not in the supported version list, the run is rejected before any command is executed with `errorCategory=ENVIRONMENT`, `detailCode=terminal.shellUnavailable`.
3. When a job declares `shell=wsl-bash` and WSL Bash is not installed or not at an approved version, the run is rejected with `detailCode=terminal.wslUnavailable` before any command executes.
4. The detected shell versions are included in the capability advertisement reported to the Orchestrator.

---

### TER-US-002 — Execution Identity and Privilege Enforcement

**As a** enterprise security officer,  
**I want** every command to run under the authorized Windows or Linux identity specified in the deployment policy,  
**so that** no automation script can silently elevate privileges or run as a different user.

**Requirement refs:** ENV-002, ENV-006  
**Acceptance tests:** TER-AT-02, TER-AT-03, TER-AT-09, TER-AT-23

**Acceptance criteria:**

1. Before executing any command, the runtime verifies the effective Windows or Linux identity against the policy-specified identity; a mismatch is rejected with `errorCategory=AUTHENTICATION`, `detailCode=terminal.identityMismatch`.
2. A script cannot silently execute a command as a higher-privilege user than the authorized identity.
3. For `wsl-bash` commands, the approved distro name and Linux user identity are resolved explicitly; a job referencing an unapproved or unavailable distro is rejected before any command launches with `errorCategory=ENVIRONMENT` or `errorCategory=POLICY_DENIED`.
4. WSL Bash customer commands run in a separately provisioned distro/container that is not the WSL environment hosting the Tracto Orchestrator; a `commandRef` that would run in the Orchestrator's WSL environment is rejected.

---

### TER-US-003 — Working Directory Policy and Environment Variable Handling

**As a** enterprise security officer,  
**I want** the runtime to validate working-directory paths against the approved root allowlist and pass only explicitly declared environment variables to processes,  
**so that** commands cannot traverse to unauthorized directories or inherit ambient secrets from the host environment.

**Requirement refs:** ENV-003, ENV-004, ENV-005  
**Acceptance tests:** TER-AT-01, TER-AT-11, TER-AT-16, TER-AT-19

**Acceptance criteria:**

1. A `cwdRef` that resolves to a path outside the approved root allowlist is rejected before any command launches with `errorCategory=POLICY_DENIED`, `detailCode=terminal.pathTraversalDenied`.
2. A `cwdRef` containing any `../` path segment is rejected before resolution regardless of whether the canonicalized path would remain within an authorized root. This strict pre-resolution rejection is the approved policy boundary; any relaxation to a canonicalization-only check requires an explicit architecture decision record.
3. Only environment variables explicitly declared in the step's `environmentRefs` are passed to the child process; the host's ambient environment is not forwarded.
4. Secret values are passed via approved Windows-side `secretRef` resolution; they never appear as literals in command arguments, audit records, logs, or Orchestrator payloads.
5. A secret environment variable is absent from all collected evidence and telemetry.

---

### TER-US-004 — Process Creation with Typed Argument Binding

**As a** Tracto security architect,  
**I want** the runtime to launch approved commands using typed argument-vector APIs and fixed shell templates,  
**so that** no user-supplied value can inject a second OS command regardless of its content.

**Requirement refs:** ENV-005, CMD-002, CMD-003  
**Acceptance tests:** TER-AT-01, TER-AT-22

**Acceptance criteria:**

1. Arguments are passed to `cmd.exe`, PowerShell, and WSL Bash via typed argument-vector APIs wherever the platform supports it; shell concatenation of user-supplied strings is not used.
2. An argument value containing shell metacharacters (`&`, `|`, `;`, backticks, `$(...)`) is passed as literal data to the target command — it does not trigger a second OS command.
3. A test where a malicious argument value attempts to execute a secondary command produces no additional process and no unintended output.
4. For CMD/PowerShell/Bash templates that require shell syntax, the template is fixed, policy-certified, and rejects unsafe dynamic interpolation at validation time.
5. An argument value that exceeds the declared maximum parameter length for the command template is rejected at schema validation before any process launches, with `errorCategory=VALIDATION`, `detailCode=terminal.argumentTooLong`.
6. An argument value that encodes a URL or filesystem path targeting a non-allowlisted host or directory is rejected at schema validation; the runtime does not attempt to connect to or stat the path before rejection.

---

### TER-US-005 — Headless Terminal Operation and Remote Context Rejection

**As a** Tracto platform engineer,  
**I want** the Terminal Runtime to operate without a foreground Windows desktop session and to reject unconfigured remote contexts,  
**so that** Terminal workers can run alongside Desktop workers without competing for a GUI session.

**Requirement refs:** ENV-007, ENV-008  
**Acceptance tests:** TER-AT-12, TER-AT-15, TER-AT-28

**Acceptance criteria:**

1. A Terminal worker runs CMD and PowerShell commands successfully while the Windows desktop is logged out, provided the execution identity allows headless operation.
2. The Terminal worker never requests or holds a foreground Windows interactive input lease.
3. A job that targets an SSH remote host not configured in the deployment policy is rejected before any connection attempt with `errorCategory=POLICY_DENIED`.
4. Two Terminal workers share a host with capacity isolation; neither interferes with the other's process tree.

---

## Epic 2 — Approved Command and Parameter Model

**Goal:** Every command executed by the Terminal Runtime is preapproved, has verified integrity, uses typed arguments, and contains no business logic or network-escape capabilities.

---

### TER-US-006 — Command Allowlist Enforcement and Integrity Verification

**As a** enterprise security officer,  
**I want** the runtime to execute only preapproved, integrity-verified command templates,  
**so that** no unapproved, tampered, or dynamically assembled command can be launched.

**Requirement refs:** CMD-001, CMD-009  
**Acceptance tests:** TER-AT-01, TER-AT-02, TER-AT-21

**Acceptance criteria:**

1. A `commandRef` that does not resolve to a preapproved command in the signed artifact and deployment command policy is rejected with `errorCategory=POLICY_DENIED`, `detailCode=terminal.commandNotAllowed`.
2. A binary or script whose digest does not match the approved, hash-pinned identity is rejected before launch.
3. An unsigned `.bat` template that has been edited fails the integrity check; execution does not proceed.
4. An artifact containing a wrong-runtime, unsigned, or mixed-engine `commandRef` fails validation before any process is launched.

---

### TER-US-007 — PowerShell Safe Execution Policy

**As a** enterprise security officer,  
**I want** the runtime to honor PowerShell execution policy, signed script requirements, module allowlists, and language mode,  
**so that** unsigned, unapproved, or unrestricted PowerShell code cannot execute.

**Requirement refs:** CMD-004, CMD-005  
**Acceptance tests:** TER-AT-02

**Acceptance criteria:**

1. An attempt to use `-ExecutionPolicy Bypass` is rejected by policy before the PowerShell process launches.
2. An attempt to call `Invoke-Expression` with an arbitrary string is rejected; only preapproved script references with typed arguments are allowed.
3. A PowerShell module not in the approved module allowlist cannot be imported.
4. The PowerShell language mode is set to `ConstrainedLanguage` or stricter as declared in the deployment policy.
5. An approved versioned batch script with declared inputs and an output contract executes correctly; an edited unsigned version of the same script fails the integrity check.

---

### TER-US-008 — WSL Bash Script Invocation and Distro Isolation

**As a** enterprise security officer,  
**I want** WSL Bash commands to run in an explicitly provisioned customer distro with approved identity, and never in the Orchestrator's WSL environment,  
**so that** customer command execution is isolated from the Tracto control plane.

**Requirement refs:** CMD-006, ENV-006  
**Acceptance tests:** TER-AT-03, TER-AT-09, TER-AT-18, TER-AT-23

**Acceptance criteria:**

1. A WSL Bash command executes only in the approved customer command distro, with the declared Linux user identity and working directory.
2. A `commandRef` targeting the WSL distribution that hosts the Tracto Orchestrator is rejected with `errorCategory=POLICY_DENIED`.
3. A mismatch between the declared distro and the provisioned distro is rejected before execution, not silently redirected to another distro.
4. A script path outside the approved directory within the customer distro fails before execution.

---

### TER-US-009 — No Business Logic in Commands and Network Bypass Prevention

**As a** Tracto architect,  
**I want** Terminal command templates to be unable to implement domain calculations, make API calls to third-party systems, or use SQL clients,  
**so that** the Terminal cannot be used as an alternative business integration channel in place of `service.invoke`.

**Requirement refs:** CMD-007, STR-006, §9 (network boundary)  
**Acceptance tests:** TER-AT-13, TER-AT-16, TER-AT-17, TER-AT-20, TER-AT-26

**Acceptance criteria:**

1. A command template that encodes a business threshold comparison (e.g., `if ($amount -gt 10000)`) is rejected by the schema validator before any process launches.
2. A command template that invokes `curl`, `wget`, `Invoke-WebRequest`, `Invoke-RestMethod`, or a SQL client targeting a third-party business API or database is rejected before any process launches; no HTTP request is emitted.
3. A `service.invoke` call to a registered custom service succeeds; the same operation attempted via a shell-based HTTP command fails.
4. An unapproved arbitrary shell string derived from an API response is rejected at validation as a dynamic command — it is never passed to a shell.
5. Structured command output (JSON, exit code) is passed to `service.invoke` unchanged; the runtime makes no domain calculation on the output.

---

### TER-US-010 — Idempotency Classification and Command Audit

**As a** Tracto platform engineer,  
**I want** every command to declare its idempotency class and for the runtime to record a full audit trace without revealing secrets,  
**so that** irreversible commands are never retried after a lost acknowledgement, and auditors can reconstruct which command definition ran.

**Requirement refs:** CMD-008, CMD-010  
**Acceptance tests:** TER-AT-08, TER-AT-11, TER-AT-14, TER-AT-21, TER-AT-27

**Acceptance criteria:**

1. A command classified `irreversible` (e.g., a financial export) is not automatically replayed after an ambiguous timeout or lost acknowledgement.
2. A command classified `read-only` may be retried up to the configured retry limit on a transient failure.
3. The audit record for each command contains: template ID, argument names, hashes of approved artifacts, `runId`, `attemptId`, and the effective identity reference — no raw secret parameter values.
4. The audit record is sufficient to reconstruct which command definition was invoked without exposing any secret input.

---

## Epic 3 — Streams, Prompts, and Output Management

**Goal:** The Terminal Runtime captures stdout and stderr safely within defined buffer limits, handles interactive prompts using allowlisted patterns, redacts secrets from output, and stores result files in approved locations.

---

### TER-US-011 — Bounded stdout/stderr Capture and Deadlock Prevention

**As a** Tracto platform engineer,  
**I want** stdout and stderr to be captured separately with enforced buffer limits and truncation markers,  
**so that** a large output burst never deadlocks the worker or causes unbounded memory growth.

**Requirement refs:** STR-001, STR-002, STR-003  
**Acceptance tests:** TER-AT-03, TER-AT-04, TER-AT-06

**Acceptance criteria:**

1. `terminal.stdout.read` and `terminal.stderr.read`: Each action returns the next available buffered segment from the respective stream, or blocks until data arrives or the process ends; calling either action after the process has exited returns any remaining buffered data then a stream-end marker.
2. stdout and stderr are captured as separate, labelled, sequenced streams; a large stderr burst does not deadlock the owning process.
3. When stdout exceeds the configured limit (default 10 MB), output capture stops, a `terminal.outputTruncated` marker is written to the step's evidence record, and the run continues — it does not hang.
4. When stderr exceeds the configured limit (default 1 MB), a `terminal.stderrTruncated` marker is written and the run continues.
5. Each individual output chunk delivered from the process pipe does not exceed 64 KB; a process that writes a single chunk larger than 64 KB is handled by splitting or truncating at the chunk boundary rather than passing the oversized buffer in one read.
6. Line endings in captured stdout and stderr are normalized to LF before storage; a process that writes CRLF-terminated lines produces LF-only output in the evidence record and step bindings.
7. Exit code and process completion are captured independently of stdout content; exit code 0 with warning output is distinguishable from nonzero with partial output.
8. Unicode filenames and non-ASCII console output round-trip correctly using the declared console encoding.

---

### TER-US-012 — Bounded Stdin and Allowlisted Prompt Matching

**As a** workflow author,  
**I want** the runtime to respond to interactive CLI prompts using only predeclared allowlisted patterns and bounded inputs,  
**so that** an unknown or credential-seeking prompt never receives a guessed response.

**Requirement refs:** STR-004, STR-005  
**Acceptance tests:** TER-AT-05

**Acceptance criteria:**

1. `terminal.stdin.write`: The action writes the declared allowlisted string to the process's stdin pipe within the configured `stdinPromptTimeoutMs`; a write to a process that has already exited produces `errorCategory=ENVIRONMENT`, `detailCode=terminal.processExited`; the written value is never echoed to evidence or logs.
2. An expected interactive prompt that matches an allowlisted pattern receives the declared approved response within the configured `stdinPromptTimeoutMs`.
3. An interactive prompt that does not match any allowlisted pattern causes the step to time out with `errorCategory=TIMEOUT`, `detailCode=terminal.promptTimeout`; no guessed input is sent.
4. A prompt that appears to be requesting a credential (password, token) but is not in the allowlist never receives any input from the runtime.
5. The stdin payload is bounded by the configured limit (default 64 KB); an oversized input is rejected before write.

---

### TER-US-013 — Output Redaction and Secret Protection

**As a** enterprise security officer,  
**I want** secrets injected into a process (via environment variables, arguments, or stdin) to be automatically redacted from all collected output evidence,  
**so that** credentials never appear in telemetry, Orchestrator payloads, or diagnostic artifacts.

**Requirement refs:** STR-007, ENV-004  
**Acceptance tests:** TER-AT-11, TER-AT-19

**Acceptance criteria:**

1. A secret value passed to a process via an approved `secretRef` does not appear in collected stdout, stderr, evidence, telemetry, or Orchestrator job messages.
2. The `outputRedaction` field is required on all Terminal steps that produce output; `mask-all` is the default; scripts may narrow redaction only where deployment policy permits — disabling redaction entirely is not permitted.
3. A step-level audit record does not contain the raw secret value even when the secret is used as a command argument.
4. A sanitized evidence artifact from a failed run contains no live session token or API key.

---

### TER-US-014 — Output Bindings and Approved Result Artifacts

**As a** workflow author,  
**I want** to bind command output (exit code, stdout segments, JSON) to named step outputs and store result files in approved locations,  
**so that** structured output can be passed to a downstream `service.invoke` call without the runtime making any domain interpretation.

**Requirement refs:** STR-006, STR-008  
**Acceptance tests:** TER-AT-04, TER-AT-07, TER-AT-10, TER-AT-24

**Acceptance criteria:**

1. A declared JSON output from a command is bound to a named step output and passed unchanged to a `service.invoke` call; the runtime performs no numeric comparison or domain decision on it.
2. An output file stored at a path outside the approved storage root is rejected; oversized output is truncated or summarized safely rather than silently accepted.
3. Structured command output references (file paths, artifact hashes) are evidenced with signed references in the run journal.
4. `terminal.file.exists`: Given a path within the approved storage root, the action returns a boolean existence result; the boolean is the only output — no file content is read. A path outside the approved root is rejected with `errorCategory=POLICY_DENIED` before any stat call. A path containing a secret segment must not expose the secret in the error detail.
5. `terminal.env.inspect`: Given an allowlisted environment variable name, the action returns the variable's current value as a step output; a variable name not in the deployment policy allowlist is rejected with `errorCategory=POLICY_DENIED`. If the returned value matches an active `secretRef`, it is automatically redacted from evidence and Orchestrator payloads by the same mechanism as `outputRedaction`.

---

## Epic 4 — Lifecycle, Deadlines, and Recovery

**Goal:** The Terminal Runtime enforces per-step and run-level deadlines, cleans up owned process trees on cancellation, handles WSL outages, classifies unknown side effects correctly, and sends heartbeats during long-running commands.

---

### TER-US-015 — Process Deadline Enforcement and Owned Process-Tree Cleanup

**As a** Tracto platform engineer,  
**I want** every command step to have a bounded deadline and for all owned child processes to be terminated cleanly on cancel or timeout,  
**so that** no shell session waits indefinitely and no unrelated host process is affected.

**Requirement refs:** TER-001, TER-002, TER-003  
**Acceptance tests:** TER-AT-05, TER-AT-07, TER-AT-20, TER-AT-27

**Acceptance criteria:**

1. `terminal.process.start`: A command is launched using the typed argument-vector API with the declared shell, cwdRef, and environment; the process ID is recorded in the run journal before any output is read.
2. `terminal.process.await`: The action blocks until the process terminates or `timeoutMs` elapses; exit code and stream-end markers are returned when the process ends normally.
3. `terminal.process.cancel`: A cancel signal terminates the full owned child process tree; the action confirms no owned process remains after the `processTreeCleanupMs` grace period; the run state becomes CANCELLED.
4. A command that exceeds its declared `timeoutMs` is terminated at the deadline; the runtime never waits indefinitely for `Enter` or additional input; the run state becomes TIMED_OUT.
5. On cancellation or timeout, the runtime terminates the full owned child process tree using the OS-appropriate method; unrelated host processes are unaffected.
6. After owned child processes are terminated, a verification check confirms no owned process remains; a `processTreeCleanupMs` grace period is applied before forced kill.
7. A script that uses a background process (`&` in Bash, background jobs in PowerShell) is blocked unless the approved command template expressly supports that lifecycle.

---

### TER-US-016 — Unknown Side Effect Classification and No Duplicate Replay

**As a** Tracto platform engineer,  
**I want** the runtime to classify a command as UNKNOWN when it kills or loses connection to a process after an effectful write, and to never replay irreversible commands after that,  
**so that** data integrity is preserved even when the process outcome cannot be verified.

**Requirement refs:** TER-005, TER-006  
**Acceptance tests:** TER-AT-08, TER-AT-27

**Acceptance criteria:**

1. When a read-only command fails with a known transient error, the runtime may retry up to the configured limit.
2. When an `irreversible` command's process is killed or the transport breaks after stdout indicates a write has occurred but the exit code is not received, the run terminates with run state UNKNOWN, `errorCategory=TRANSPORT` or `INTERNAL`, and `unknownReason` populated with the C3-approved code.
3. The runtime does not automatically issue a second attempt of an `irreversible` command after a lost exit receipt.
4. An `UNKNOWN` terminal state is reported to the Orchestrator with `needsAttention` set; the attempt stops.

---

### TER-US-017 — WSL Failure Detection and Reconciliation

**As a** Tracto platform engineer,  
**I want** the runtime to detect WSL distro unavailability or unexpected termination distinctly and reconcile safely,  
**so that** a WSL infrastructure failure produces a structured, classified error rather than a hung worker.

**Requirement refs:** TER-004  
**Acceptance tests:** TER-AT-09, TER-AT-23

**Acceptance criteria:**

1. When the WSL distro is unavailable before any effectful command starts, the run reports `errorCategory=ENVIRONMENT`, `detailCode=terminal.wslUnavailable`.
2. When the WSL distro terminates or restarts during an effectful command, the runtime classifies the result: if the command's side effect cannot be reconciled as committed or not-committed, the run terminates as UNKNOWN; if the command had not yet committed any side effect, it terminates as FAILED.
3. After a WSL distro outage, the worker's journal state allows the Orchestrator to reconcile the attempt without launching a duplicate command.

---

### TER-US-018 — Heartbeat During Long-Running Commands and Control-Plane Restart

**As a** Tracto platform engineer,  
**I want** the runtime to send periodic heartbeats and bounded progress summaries while a long command runs,  
**so that** the Orchestrator can detect a hung worker and so that a control-plane restart does not cause a duplicate command launch.

**Requirement refs:** TER-007  
**Acceptance tests:** TER-AT-14, TER-AT-28

**Acceptance criteria:**

1. While a long-running command is executing, the runtime sends outbound heartbeats at the configured interval (default 15 seconds) with a bounded progress summary.
2. After an Orchestrator restart, the worker reconnects and the journal state prevents a duplicate irreversible command from being launched.
3. A heartbeat that fails due to a temporary Orchestrator outage does not kill the running command; the worker retries the heartbeat and continues the command within the bounds of the approved lease duration and the run's maximum deadline. If heartbeat failures accumulate until the lease expires or the run exceeds its approved maximum runtime, the command is terminated safely and the run state is set to TIMED_OUT or UNKNOWN depending on whether an effectful action has committed a side effect.

---

### TER-US-019 — Filesystem Cleanup and Async Service Handoff

**As a** Tracto platform engineer,  
**I want** the runtime to remove owned temporary files on cancel and to hand off async business operations correctly to `service.invoke`,  
**so that** cancelled runs leave no secret artefacts on disk and async operations integrate cleanly with the Orchestrator's continuation model.

**Requirement refs:** TER-008, TER-009  
**Acceptance tests:** TER-AT-07, TER-AT-10, TER-AT-19, TER-AT-24, TER-AT-25

**Acceptance criteria:**

1. When a run is cancelled, owned temporary files are removed per policy; approved result files and diagnostics are preserved; no leaked temporary secrets remain.
2. Owned temporary files are not deleted outside the authorized filesystem roots.
3. A `service.invoke` call that results in a confirmed asynchronous acknowledgement ends the Terminal run with state `HANDED_OFF`; the Terminal worker is released; the Orchestrator creates exactly one statically configured continuation run only when an eligible authenticated deduplicated callback or approved continuation trigger is received.
4. A continuation run (Run B) is created only when an eligible authenticated deduplicated callback or approved continuation trigger is received after the source run's `HANDED_OFF` state is persisted; the continuation is not created speculatively at `HANDED_OFF` time. A dynamic `scriptRef` in a service callback event is refused; only statically configured continuation is allowed.

---

## Epic 5 — CEC Contract and Service Invocation

**Goal:** The Terminal Runtime is fully conformant with CEC v1.0.1 — correct wire protocol, all five service outcomes, all run states, signed artifact validation, and independent release gates.

---

### TER-US-020 — Signed Artifact Validation and Closed Behavior

**As a** Tracto security architect,  
**I want** every Terminal job artifact to be validated for signature, runtime type, schema major, and policy references before any process launches,  
**so that** tampered, unsigned, mixed-engine, or wrong-runtime artifacts are rejected before any side effect.

**Requirement refs:** Appendix A (CEC-001, CEC-002, CEC-012)  
**Acceptance tests:** TER-AT-21, TER-AT-26, TER-AT-28

**Acceptance criteria:**

1. An unsigned artifact is rejected before any process launches; the run reports `errorCategory=VALIDATION`.
2. An artifact with `runtimeType=browser` submitted to a Terminal worker is rejected before any shell interaction.
3. An artifact containing mixed `terminal.*` + `browser.*` action references is rejected at schema validation time.
4. An artifact referencing an unsupported script schema major version is rejected; only the supported schema major (v1) is accepted.
5. Business-arithmetic predicates and dynamically supplied shell strings from API results are rejected at schema validation before any process launches.

---

### TER-US-021 — All Five Service Outcomes and Run State Handling

**As a** workflow author,  
**I want** the Terminal Runtime to handle all five `service.invoke` outcomes correctly and to use the exact CEC run states,  
**so that** operators can rely on consistent terminal state semantics across all Tracto Runtimes.

**Requirement refs:** Appendix A (CEC-005, CEC-013, CEC-016), STR-006  
**Acceptance tests:** TER-AT-10, TER-AT-24, TER-AT-25

**Acceptance criteria:**

1. A fake registered service that returns each of the five `serviceOutcome` values is handled correctly; the runtime branches only on category or approved `actionCode`, never on domain fields.
2. `serviceOutcome=BUSINESS_REJECTION` results in run state `SUCCEEDED` (on successful completion of any configured CLI handling step, such as displaying the rejection status) with `businessOutcome=REJECTED`; no retry is attempted. If the post-rejection handling step fails for an independent technical reason, the run terminates as FAILED for that technical failure — the service business outcome does not override the run's technical completion state.
3. `serviceOutcome=UNKNOWN_OUTCOME` terminates the attempt as UNKNOWN; no continuation executes until external reconciliation establishes a safe outcome.
4. A confirmed asynchronous acknowledgement results in run state `HANDED_OFF`; the Terminal worker is released; exactly one statically configured continuation run is created only when an eligible authenticated deduplicated callback or approved continuation trigger is received; dynamic `scriptRef` from service is refused.
5. Terminal run states used are exactly: `SUCCEEDED`, `FAILED`, `CANCELLED`, `TIMED_OUT`, `UNKNOWN`, `HANDED_OFF`; `UNKNOWN` is terminal and stops the attempt.

---

### TER-US-022 — Lease Fencing, Heartbeat, and Attempt Reconciliation

**As a** Tracto platform engineer,  
**I want** the Terminal Runtime to enforce one active job per worker, maintain heartbeats, and reconcile after disconnect,  
**so that** no duplicate irreversible command is issued after Orchestrator or worker restart.

**Requirement refs:** Appendix A (CEC-004, CEC-HOST-002, CEC-HOST-003)  
**Acceptance tests:** TER-AT-14, TER-AT-28

**Acceptance criteria:**

1. At most one active job lease is held by a registered Terminal worker at any time.
2. The runtime persists `runId`, `attemptId`, `leaseId`, heartbeat sequence number, checkpoints, and final results to the local run journal.
3. When a lease expires, the runtime performs reconciliation — it does not assume failure and does not replay irreversible commands.
4. After an Orchestrator restart, the worker reconnects and uses the journal to avoid launching a duplicate irreversible command.

---

### TER-US-023 — CEC Conformance Suite and Independent Release

**As a** Tracto release manager,  
**I want** the Terminal Runtime to pass the full Stage 1 CEC conformance suite against both mock and real Orchestrator environments before release,  
**so that** it can be shipped independently of the Desktop and Browser tracks.

**Requirement refs:** Appendix A (CEC-007, CEC-010), Appendix F  
**Acceptance tests:** TER-AT-28

**Acceptance criteria:**

1. All applicable CF-01–CF-10 conformance fixtures pass against the mock Orchestrator fixture.
2. CF-01–CF-10 pass against a real, compatible Orchestrator in connected mode.
3. The engineering build gate confirms Terminal Runtime binaries build under the approved .NET baseline independently of the Desktop and Browser source trees.
4. The security gate confirms no secrets appear in script, events, logs, evidence, traces, stream dumps, or Orchestrator payloads.
5. The mode certification gate confirms headless Terminal operation — no `interactiveSessionId` or foreground input lease is required.
6. The WSL mode certification gate confirms customer commands run in the approved customer distro, not the Orchestrator's WSL environment. This gate applies only when the WSL Bash capability is advertised in the worker's capability advertisement; a Terminal worker that does not advertise WSL Bash does not need to pass this gate and may achieve independent release without it.

---

## Appendix — Story Summary Table

| Story ID | Title | Requirement refs | Acceptance tests | Epic |
|---|---|---|---|---|
| TER-US-001 | Shell Availability and Version Validation | ENV-001 | TER-AT-02, 03 | 1 — Environment |
| TER-US-002 | Execution Identity and Privilege Enforcement | ENV-002, ENV-006 | TER-AT-02, 03, 09, 23 | 1 — Environment |
| TER-US-003 | Working Directory Policy and Env Var Handling | ENV-003, ENV-004, ENV-005 | TER-AT-01, 11, 16, 19 | 1 — Environment |
| TER-US-004 | Process Creation with Typed Argument Binding | ENV-005, CMD-002, CMD-003 | TER-AT-01, 22 | 1 — Environment |
| TER-US-005 | Headless Operation and Remote Context Rejection | ENV-007, ENV-008 | TER-AT-12, 15, 28 | 1 — Environment |
| TER-US-006 | Command Allowlist Enforcement and Integrity | CMD-001, CMD-009 | TER-AT-01, 02, 21 | 2 — Command Policy |
| TER-US-007 | PowerShell Safe Execution Policy | CMD-004, CMD-005 | TER-AT-02 | 2 — Command Policy |
| TER-US-008 | WSL Bash Invocation and Distro Isolation | CMD-006, ENV-006 | TER-AT-03, 09, 18, 23 | 2 — Command Policy |
| TER-US-009 | No Business Logic and No Network Bypass | CMD-007, STR-006, §9 | TER-AT-13, 16, 17, 20, 26 | 2 — Command Policy |
| TER-US-010 | Idempotency Classification and Command Audit | CMD-008, CMD-010 | TER-AT-08, 11, 14, 21, 27 | 2 — Command Policy |
| TER-US-011 | Bounded stdout/stderr and Deadlock Prevention | STR-001, STR-002, STR-003 | TER-AT-03, 04, 06 | 3 — Streams |
| TER-US-012 | Bounded Stdin and Allowlisted Prompt Matching | STR-004, STR-005 | TER-AT-05 | 3 — Streams |
| TER-US-013 | Output Redaction and Secret Protection | STR-007, ENV-004 | TER-AT-11, 19 | 3 — Streams |
| TER-US-014 | Output Bindings and Approved Result Artifacts | STR-006, STR-008 | TER-AT-04, 07, 10, 24 | 3 — Streams |
| TER-US-015 | Process Deadline and Process-Tree Cleanup | TER-001, TER-002, TER-003 | TER-AT-05, 07, 20, 27 | 4 — Recovery |
| TER-US-016 | Unknown Side Effect and No Duplicate Replay | TER-005, TER-006 | TER-AT-08, 27 | 4 — Recovery |
| TER-US-017 | WSL Failure Detection and Reconciliation | TER-004 | TER-AT-09, 23 | 4 — Recovery |
| TER-US-018 | Heartbeat and Control-Plane Restart Safety | TER-007 | TER-AT-14, 28 | 4 — Recovery |
| TER-US-019 | Filesystem Cleanup and Async Service Handoff | TER-008, TER-009 | TER-AT-07, 10, 19, 24, 25 | 4 — Recovery |
| TER-US-020 | Signed Artifact Validation and Closed Behavior | Appendix A (CEC-001, 002, 012) | TER-AT-21, 26, 28 | 5 — CEC Contract |
| TER-US-021 | All Five Service Outcomes and Run States | Appendix A (CEC-005, 013, 016) | TER-AT-10, 24, 25 | 5 — CEC Contract |
| TER-US-022 | Lease Fencing, Heartbeat, Reconciliation | Appendix A (CEC-004, HOST-002, 003) | TER-AT-14, 28 | 5 — CEC Contract |
| TER-US-023 | CEC Conformance Suite and Independent Release | Appendix A (CEC-007, 010), Appendix F | TER-AT-28 | 5 — CEC Contract |

**Total stories: 23**  
**Functional requirements covered: ENV-001–008, CMD-001–010, STR-001–008, TER-001–009, Appendix A (all)**  
**Acceptance tests covered: TER-AT-01 through TER-AT-28 (all)**
