# 06 — Architectural and Sequence Diagrams

All diagrams are in Mermaid source. Render with any Mermaid-compatible viewer.

---

## A. System Context

```mermaid
graph TB
    subgraph External["External Systems (not Tracto)"]
        BIZ["Custom Business Services\nGo / Node.js / .NET / Python\nOwned by customer"]
        TAPP["Target Applications\nERP, CRM, Web Apps, CLI tools"]
        VAULT["Secret Provider\nEnterprise Vault (connected)\nor Windows DPAPI (isolated)"]
        MON["Customer Monitoring Stack\n(connected mode only)"]
    end

    subgraph Tracto["Tracto Product Boundary (3 components)"]
        DES["Designer\nWindows workstation\nAI-assisted authoring\nSigned artifact publishing"]

        subgraph RTF["Runtime Family (1 logical component, 3 .NET apps)"]
            DRT["Desktop Runtime\n.NET / Windows / UIA"]
            BRT["Browser Runtime\n.NET / Playwright / Windows"]
            TRT["Terminal Runtime\n.NET / CMD+PS+Bash"]
        end

        ORC["Orchestrator\n.NET Linux container\nConnected: Docker/K8s\nIsolated: Docker in WSL2"]
    end

    PUB(["Automation Publisher"])
    OPS(["Ops / Support"])

    PUB -->|records and approves| DES
    DES -->|publishes signed artifact| ORC
    DRT -->|outbound pull/lease/heartbeat/result| ORC
    BRT -->|outbound pull/lease/heartbeat/result| ORC
    TRT -->|outbound pull/lease/heartbeat/result| ORC
    DRT -->|authorized UI actions / UIA| TAPP
    BRT -->|authorized Playwright navigation| TAPP
    TRT -->|approved CMD/PS/Bash commands| TAPP
    DRT -->|service.invoke SYNC/ASYNC| BIZ
    BRT -->|service.invoke SYNC/ASYNC| BIZ
    TRT -->|service.invoke SYNC/ASYNC| BIZ
    BIZ -->|authenticated callback event| ORC
    DRT -->|secretRef resolution at use time| VAULT
    BRT -->|secretRef resolution at use time| VAULT
    TRT -->|secretRef resolution at use time| VAULT
    ORC -->|telemetry export| MON
    OPS -->|monitors and resolves UNKNOWN| ORC

    style External fill:#f5f5f5,stroke:#aaa
    style Tracto fill:#e8f0fe,stroke:#4a86e8
    style RTF fill:#d2e3fc,stroke:#4a86e8
```

**Key boundaries:**
- Runtimes only make outbound connections (pull/heartbeat direction). No inbound listener.
- Business services are never inside the Tracto product boundary.
- Secret plaintext never crosses to Linux Orchestrator (Windows-side resolution only).
- Authenticated business callbacks reach Orchestrator; they cannot name continuation scripts.

---

## B. Stage 1 Component Design

```mermaid
graph TB
    subgraph Prod["Production Deliverables"]
        SCH["Tracto.Contract.Schemas\nJSON Schema files v1 (current major)"]
        MOD["Tracto.Contract.Models\nC# DTOs, enums"]
        VAL["Tracto.Contract.Validation\nEnvelopeValidator\nRefResolver\nConditionGrammarParser\nServiceInvokeValidator"]
        EXE["Tracto.Contract.Execution\nRunStateMachine\nStepOutcomeNormalizer\nIStepExecutor\nResultEnvelopeBuilder"]
        ORP["Tracto.Contract.Orchestrator\nIOrchestratorClient\nIOrchestratorServer\nCapabilityDigestCalculator"]
        TEL["Tracto.Contract.Telemetry\nITelemetryEmitter\nSecretRedactor\nEvidenceManifestBuilder"]
    end

    subgraph Ref["Reference Implementation"]
        REF["Tracto.Reference.Adapter\nSimulated step execution\nFull contract round-trip proof\nClearly labelled — NOT production"]
    end

    subgraph Test["Test Infrastructure / Conformance Tools"]
        CTT["Tracto.Contract.Testing\nFakeTimeProvider\nArtifactBuilder\nFixtureValidator"]
        MKORC["Tracto.Mock.Orchestrator\nIn-memory run/lease/attempt store\nFault injection\nConformance report generator"]
        MKBIZ["Tracto.Mock.BusinessService\nAll 5 serviceOutcome values\nCallback simulator"]
        FIX["Tracto.Conformance.Fixtures\nCF-01..CF-10 + extensions\nconformance-manifest.json"]
        RUN["Tracto.Conformance.Runner\nCIRunner — per-runtime independent\nPass/fail report per adapter version"]
    end

    subgraph Deferred["Deferred — Subsequent Tracks"]
        DESK["Desktop Runtime\n.NET UIA engine"]
        BROW["Browser Runtime\n.NET Playwright engine"]
        TERM["Terminal Runtime\n.NET CMD/PS/Bash engine"]
        PRORC["Production Orchestrator\nScheduling store, dispatch, persistence"]
        DES2["Designer + AI Recorder"]
    end

    SCH --> MOD --> VAL --> EXE --> ORP --> TEL
    CTT --> MKORC & MKBIZ & FIX & RUN
    REF --> ORP & EXE & VAL & MOD
    MKORC --> ORP
    MKBIZ --> MOD
    FIX --> SCH
    RUN --> MKORC & MKBIZ & FIX

    Deferred -.->|will consume contract packages| Prod

    style Prod fill:#e8f4e8,stroke:#2a7a2a
    style Ref fill:#fff8e1,stroke:#f9a825
    style Test fill:#fce4ec,stroke:#c62828
    style Deferred fill:#f5f5f5,stroke:#999,stroke-dasharray:4
```

---

## C. Deployment Boundaries

```mermaid
graph TB
    subgraph Win["Windows Bot Host"]
        subgraph BotUser["Bot User Identity (DPAPI CurrentUser scope)"]
            SUP["Host Supervisor\n(boots at Windows start\nvia Task Scheduler)"]
            DexE["Desktop Executor\n(interactive session)"]
            HBR["Headless Browser Worker"]
            HTR["Terminal Worker"]
            DPAPI["Windows DPAPI\nCurrentUser scope\nSecretRef → Plaintext at use time"]
        end

        subgraph WSL2["WSL2 (Docker Engine — no Docker Desktop)"]
            subgraph OrcCont["Orchestrator Container (.NET Linux)"]
                ORC2["Orchestrator\nRun/attempt/lease state\nCallback dedup\nNo plaintext secrets"]
            end
            subgraph BizCont["Optional: Customer Business Service Containers"]
                BIZ2["Custom Business Services\nOwn their own credentials\nNot Tracto vault tenants"]
            end
        end
    end

    subgraph AuthNet["Authorized Network Destinations"]
        EXT_BIZ["Registered Custom Business Services\n(external or containerized)"]
        EXT_APP["Target Applications\nERP, CRM, Web apps, CLI"]
    end

    SUP -->|supervises WSL lifecycle| WSL2
    DexE -->|outbound pull/heartbeat| ORC2
    HBR  -->|outbound pull/heartbeat| ORC2
    HTR  -->|outbound pull/heartbeat| ORC2
    DexE -->|secretRef resolve| DPAPI
    DexE -->|service.invoke — credential injected| EXT_BIZ
    DexE -->|UIA actions| EXT_APP
    HBR  -->|Playwright navigation| EXT_APP
    HTR  -->|approved CLI commands| EXT_APP

    note1["⚠ Linux Orchestrator CANNOT access\nWindows DPAPI directly.\nsecretRef only in Orchestrator payload."]
    note2["⚠ WSL/Linux worker cannot inherit\nCurrentUser DPAPI (ST1-12-AC07).\nWindows-side resolution boundary enforced."]

    style Win fill:#ddeeff,stroke:#336699
    style WSL2 fill:#e8f0fe,stroke:#4a86e8
    style BotUser fill:#f0fff0,stroke:#2a7a2a
    style note1 fill:#fff3e0,stroke:#e65100
    style note2 fill:#fff3e0,stroke:#e65100
```

**Deployment questions left open by source documents (as noted at ST1-12-AC07):**
- The mechanism by which a Windows-side authorized Runtime process mediates secret resolution for any Linux-hosted worker component is not defined in v1.2A beyond the principle ("approved Windows-side resolution/execution boundary"). This is a deployment PoC concern (POC-05), not a Stage 1 contract concern. The shared contract simply enforces that the Linux Orchestrator receives only `secretRef`.

---

## D1. Script Loading, Trust Verification and Validation

```mermaid
sequenceDiagram
    participant P as Publisher
    participant R as Artifact Registry (Orchestrator)
    participant W as Worker / Reference Adapter
    participant V as EnvelopeValidator
    participant T as ArtifactTrustVerifier

    P->>R: Publish signed automation artifact (schemaVersion, automationId, runtimeType, steps, digest, signer)
    R->>T: Verify signer trust + digest
    T-->>R: Trust result (trusted | rejected)
    alt Untrusted or tampered
        R-->>P: Reject atomically — no artifact stored (ST1-01-AC04)
    end
    R->>V: Validate envelope schema, runtimeType, policyRefs, automationId format
    V-->>R: VALID or VALIDATION failure with field path + detail code
    alt VALIDATION failure
        R-->>P: Reject with errorCategory=VALIDATION (ST1-01-AC02, ST1-01-AC08)
    end
    R-->>P: Artifact accepted; immutable version pinned

    Note over W: On lease acquisition
    W->>R: Fetch artifact by immutable ref
    R-->>W: Artifact + digest
    W->>T: Re-verify digest before execution
    T-->>W: Verified or POLICY_DENIED
    W->>V: Validate against current or previous major schema (N/N-1)
    V-->>W: VALID or VALIDATION
    alt Incompatible schema major
        W-->>W: Reject: republish diagnostic (ST1-01-AC05)
    end
    Note over W: Validation complete — execution may begin
```

---

## D2. Worker Registration and Capability Compatibility

```mermaid
sequenceDiagram
    participant W as Worker
    participant ORC as Mock Orchestrator
    participant CM as CompatibilityMatrix
    participant IL as InteractiveLeaseEnforcer

    W->>ORC: register/capability {workerId, runtimeType, workerVersion, protocolVersion, supportedSchemaMajors, capabilityDigest, hostClass, interactiveSessionId?, advertisedCapabilities, health}
    ORC->>CM: Check protocolVersion (N/N-1)
    CM-->>ORC: compatible | incompatible
    alt Incompatible protocol
        ORC-->>W: Reject with explicit incompatibility result (ST1-09-AC02)
    end
    ORC->>ORC: Validate runtimeType matches advertised action/predicate namespaces
    alt Mismatch
        ORC-->>W: Reject or quarantine with mismatch diagnostic (ST1-09-AC03)
    end
    alt hostClass = interactive
        ORC->>IL: Register interactiveSessionId
        IL-->>ORC: OK or session conflict
    end
    ORC-->>W: Registration acknowledged — contract capabilities exposed (ST1-09-AC01)
```

---

## D3. Job Acquisition, Lease, Input Resolution and Execution Reporting

```mermaid
sequenceDiagram
    participant W as Worker / Reference Adapter
    participant ORC as Mock Orchestrator
    participant J as AttemptJournal
    participant E as IStepExecutor
    participant TEL as ITelemetryEmitter

    W->>ORC: pullJob {workerId, availability, lastSeenAttemptId}
    ORC-->>W: leaseGranted {runId, attemptId, leaseId, artifactRef, digest, deadline, inputRefs}
    W->>W: Validate inputRefs types (ST1-10-AC02)
    W->>ORC: leaseAccepted {runId, attemptId, leaseId}
    W->>J: Journal leaseId, attemptId, start state
    TEL->>TEL: Emit run.leased event

    loop Each step
        W->>E: Execute step (actionType, parameters, preconditions, postconditions, timeoutMs)
        E-->>W: StepResult {stepState, errorCategory?, observedPostcondition, attemptCount}
        W->>J: Journal step result + checkpoint
        W->>ORC: heartbeat {runId, attemptId, leaseId, progress, sequence}
        ORC-->>W: (ack or cancelRequest if cancellation pending)
    end

    W->>ORC: status/finalResult {runId, attemptId, state, outcome, stepResults, evidenceRefs}
    W->>J: Journal final state
    TEL->>TEL: Emit run.finalized event
    ORC-->>W: Acknowledged and fenced
```

---

## D4. Synchronous Custom-Service Invocation

```mermaid
sequenceDiagram
    participant W as Worker
    participant SV as ServiceInvokeValidator
    participant SR as IServiceRegistry
    participant DP as Windows-side SecretProvider
    participant BIZ as Fake/Real Business Service

    W->>SV: Validate service.invoke {serviceRef, operationRef, mode=SYNC, inputBindings, authSecretRef?, idempotencyKeyRef?}
    SV->>SR: Lookup serviceRef + operationRef
    SR-->>SV: Approved route, TLS/auth policy, optional response schema version
    alt Not in registry
        SV-->>W: Reject POLICY_DENIED — no network connection (ST1-06-AC02)
    end
    SV-->>W: Validated

    opt authSecretRef present
        W->>DP: Resolve secretRef (Windows side only)
        DP-->>W: Plaintext credential (never to Orchestrator or logs)
    end

    W->>BIZ: HTTP POST to registered endpoint (with credential, idempotencyKey, correlationId)
    BIZ-->>W: Response {serviceOutcome, businessOutcome?, actionCode?, data?, businessJobId?, correlationId}

    alt serviceOutcome = SUCCESS
        W->>W: Bind approved fields; no domain decision inferred (ST1-06-AC04)
    else serviceOutcome = BUSINESS_REJECTION
        W->>W: Record businessOutcome=REJECTED; follow allowlisted UI branch (ST1-06-AC05)
    else serviceOutcome = TRANSIENT_FAILURE and idempotent
        W->>W: Bounded retry with same idempotencyKey (ST1-06-AC07)
    else serviceOutcome = UNKNOWN_OUTCOME
        W->>W: Do NOT retry; run → UNKNOWN with needsAttention (C3 / ST1-06-AC06)
    end

    Note over W: Response schema mismatch → serviceOutcome=TECHNICAL_FAILURE\nerrorCategory=VALIDATION, detail=service.responseSchemaMismatch
```

---

## D5. Asynchronous Handoff and Callback-Driven Continuation

```mermaid
sequenceDiagram
    participant W as Worker (Run A)
    participant ORC as Mock Orchestrator
    participant BIZ as Business Service
    participant CB as Callback Boundary

    W->>BIZ: service.invoke mode=ASYNC {correlationId, idempotencyKey}
    BIZ-->>W: Accepted + businessJobId
    W->>W: Record HANDED_OFF + businessJobId
    W->>ORC: finalResult {state=HANDED_OFF, businessJobId, correlationId}
    ORC-->>W: Acknowledged; worker lease released (ST1-07-AC01)

    Note over BIZ: Business processing proceeds independently
    BIZ->>CB: Authenticated callback {eventId, eventType, businessJobId, correlationId, resultRef, signature, keyId, timestamp, nonce}

    CB->>CB: Verify signature + timestamp + nonce (not self-asserted authenticated:true) (ST1-07-AC04)
    alt Invalid/stale/replayed
        CB->>CB: Reject event
    end

    CB->>ORC: Validated callback
    ORC->>ORC: Dedup by eventId (ST1-07-AC05)
    alt Duplicate eventId
        ORC->>ORC: Log; emit zero new runs
    end

    ORC->>ORC: Lookup correlation → internal continuation rule (no script named by service) (ST1-07-AC06)
    ORC->>ORC: Create Run B (target automation from Orchestrator config; dedup by sourceRunId + ruleId) (ST1-07-AC08)

    Note over ORC: Run B dispatched independently to next available worker
```

---

## D6. Successful Technical Completion and Static Continuation

```mermaid
sequenceDiagram
    participant W as Worker (Run A)
    participant ORC as Mock Orchestrator

    W->>ORC: finalResult {state=SUCCEEDED, businessOutcome?, stepResults, evidenceRefs}
    ORC->>ORC: Verify attempt identity; fence duplicates
    ORC->>ORC: Check for Orchestrator-owned on-SUCCEEDED static continuation rule
    Note over ORC: Continuation target is internal config — not from script or service
    alt Rule found and not already triggered for this runId + ruleId
        ORC->>ORC: Create exactly one Run B
        Note over ORC: Run B has its own runId, attemptId; tracked independently (ST1-07-AC08)
    end
    ORC-->>W: Acknowledged
```

---

## D7. Cancellation, Lease Expiry, Uncertain Commitment and Reconciliation

```mermaid
sequenceDiagram
    participant W as Worker
    participant ORC as Mock Orchestrator
    participant J as AttemptJournal

    Note over W,ORC: Scenario 1 — Cancellation during active work

    W->>ORC: heartbeat {sequence N}
    ORC-->>W: cancelRequest {runId, attemptId, reason}
    W->>W: Observe cancel at next safe boundary (wait/repeat)
    W->>W: Bound cleanup; determine if irreversible action committed
    alt No irreversible action committed
        W->>ORC: finalResult {state=CANCELLED} (ST1-11-AC02)
    else Irreversible action committed but unconfirmed
        W->>ORC: finalResult {state=UNKNOWN, needsAttention=true, unknownReason} (ST1-11-AC03)
    end
    W->>J: Journal final state + step evidence

    Note over W,ORC: Scenario 2 — Lease expiry

    ORC->>ORC: Lease deadline exceeded; no heartbeat received
    ORC->>ORC: Flag attempt for reconciliation; do NOT dispatch replacement blindly (ST1-10-AC05)

    W->>ORC: reconcile {runId, attemptId, lastSeenAttemptId, journalCheckpoint}
    ORC->>J: Compare persisted identity with reconcile request
    alt Proof of non-start or safe non-commit
        ORC-->>W: Safe requeue decision
    else Cannot rule out commitment
        ORC->>ORC: Finalize UNKNOWN + needsAttention; fence attempt
        ORC-->>W: UNKNOWN resolution (ST1-14-AC03)
    end

    Note over W,ORC: Scenario 3 — Stale attempt reporting

    W->>ORC: finalResult from OLD attempt after replacement assigned
    ORC->>ORC: Fencing check: attemptId is stale (newer attemptId exists)
    ORC->>ORC: Discard stale result; do NOT overwrite authoritative state (ST1-10-AC06)
```
