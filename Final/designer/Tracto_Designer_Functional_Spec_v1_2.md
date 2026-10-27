# Tracto Designer

**Functional Requirements Document  v1.2 — Features Included**

DS-01 — DS-19  |  DESIGN-IT-01 — DESIGN-IT-08  |  Screenshots · Narration · AI Authoring included

---

> **V1 delivery rule:** One Windows .NET Designer with runtime-specific capture adapters, a step-list editor, screenshot evidence, voice narration, and an AI authoring bridge. Core recording, deterministic draft generation, contract validation, Runtime-based testing and approved publication work as a deterministic baseline. Screenshot capture, voice narration and AI assistance are included features — each degrades gracefully if its required adapter or connector is unavailable.

| **Document ID** | TRACTO-DS-FRD-001 |
| --- | --- |
| **Version** | 1.2 — features included REVIEW DRAFT \| 24 October 2026 |
| **Architecture** | Tracto v1.2A — Architecture frozen; no product-boundary changes authorized |
| **Basis** | Tracto Architecture v1.2A §4; CEC v1.0.1; Desktop Runtime Spec v1.1; Architecture §8, §9.5 |
| **Audience** | Lead Architect, Designer team, QA, Security reviewers, Orchestrator integration team |
| **Status** | REVIEW DRAFT — architecture unchanged; deployment PoC gates stay with Architecture v1.2A |

## 1.  Purpose and Scope

The Tracto Designer is a Windows .NET application that bridges human intent and deterministic machine execution. The author records or enters a supported sequence, the Designer creates an editable deterministic draft, and a human reviews and validates it. An AI authoring adapter (MCP-based) can suggest edits; the deterministic no-AI path is always available when no connector is configured. Testing uses a compatible Runtime; approved artifacts are signed and published to the Orchestrator registry.

The Designer is the ONLY component authorized to publish automation artifacts. No Runtime, no business service, and no Orchestrator configuration may substitute for the Designer publication flow. Every publishable script must be author-reviewed, validated, signed by an authorized publisher, and registered as an immutable artifact. The approved offline import path preserves the same trust and approval requirements.

**In scope — v1.0**

- Recording sessions for Desktop, Browser and Terminal runtimes
- Recording screenshots with fail-closed sensitive-content protection (enabled by default; policy-gated per session)
- Voice narration with local offline transcription adapter (degrades gracefully if adapter not installed)
- Light event normalization into a small typed recording representation
- Deterministic (no-AI) script generation as the always-available baseline
- MCP-compatible AI authoring bridge; deterministic no-AI path remains available when no AI connector is configured
- Script editor — human review, step reorder, parameter extraction, control flow
- Live selector test against the running target application
- Script validation — CEC v1.0.1 grammar enforcement and policy checks
- Test execution through a compatible Runtime in an authorized target/test session; no duplicate Designer execution engine
- Artifact signing and publishing to Orchestrator registry (OC-11)
- Script version management — draft and published lifecycle
- Offline sanitized recording export for AI-assisted authoring on a connected workstation
- Signed script package import for isolated environments
**Explicitly out of scope — v1**

- OCR, image-recognition or pixel-only element targeting at runtime
- Cross-engine steps in a single script — one runtimeType per published artifact
- Business rule expressions, domain arithmetic or relational comparisons in scripts
- LLM interpretation at execution time — the Runtime always executes deterministically
- Embedded cloud dependency during isolated execution or offline replay
## 2.  Designer Position in the Architecture

Tracto has exactly three logical components: Designer, Runtime family and Orchestrator. The Designer owns the publish side of the automation lifecycle; it never participates in execution.

| **Owns** | Recording UI activity; AI-assisted and manual script authoring; artifact signing and registry publication; script version management; offline export and signed import. |
| --- | --- |
| **Does NOT own** | Job dispatch, worker leasing or heartbeat (Orchestrator); UI/CLI execution at run time (Runtime); business rules, data extraction or domain decisions (external services). |
| **Produces** | One immutable signed artifact per published version, referenced by schemaVersion + automationId + version; registered with Orchestrator via OC-11. |
| **Depends on** | Orchestrator OC-11 for registration; local Runtime (DS-15) for live test; Approved offline speech-to-text adapter for narration (degrades gracefully if absent); MCP-compatible AI authoring connector (deterministic path if absent). Core validation and publishing work without both. |
| **Security contract** | Signs every artifact with the trusted publisher key; never embeds plaintext secrets; strips sensitive content before any external transmission; sanitizes exports before transfer. |

## 3.  Non-Negotiable Constraints

Every Designer story must honor all of the following. Violations are blocking defects regardless of sprint.

**From Architecture v1.2A §4.2 — Desktop targeting**

- UIA is the primary element-discovery path; Java Access Bridge for eligible Java apps; keyboard fallback; control-relative coordinates ONLY after a UIA-located anchor.
- No OCR targeting, image-recognition targeting or arbitrary absolute-screen clicking in any generated step.
- Screenshots are for recording review and diagnostics. Generated scripts must NOT reference screenshots for element location.
- Playwright for .NET is the browser recording foundation. Terminal requires its own approved command/session capture adapter.
**From Architecture v1.2A §4.3 — AI boundary**

- Claude Code, LLM APIs and MCP-compatible tools are authoring integrations, not Tracto implementation languages. Tracto itself is .NET.
- The authoritative deliverable is the reviewed, signed script artifact — not a chat session or generated Markdown alone.
- Production execution works without the AI authoring provider. The deterministic (no-AI) path is always available.
- Recording and transcript data sent to any external AI service must respect enterprise privacy controls. Sensitive recordings and screenshots must be scrubbed and explicitly authorized before any external transfer. No unverified privacy assurances are permitted.
**From CEC v1.0.1 — Script correctness**

- One published script targets one runtimeType: desktop | browser | terminal. Mixed-engine scripts are rejected at validation.
- No business decision expressions: amount > 10000, response.score <= 0.8, domain arithmetic, relational domain comparisons — all rejected by the validator.
- control.repeatBounded maxIterations must be a literal approved integer within the configured policy limit. The proposed value 100 is not a frozen contract constant.
- automationId is an opaque registry key only — no filesystem paths, executable references or URL-like values.
- No plaintext secrets embedded in any field of the script; secretRef values are opaque strings only.
- Every step output classification must match a recognized deployment policy classification; unclassified outputs are rejected.
**From Architecture v1.2A §9.5 — Isolated / offline mode**

- The Designer must support recording and deterministic script generation without internet connectivity or an AI service.
- Signed script package import must make the executable registry entry visible atomically: the artifact is either admitted after all checks or not admitted.
- Import requires authorized approval before the script is registered for scheduling.
## 4.  Technology Decisions

| **Recording — Desktop** | UIA events and target-scoped Windows event capture first; Java Access Bridge for certified Java apps. Low-level input hooks are a narrowly scoped fallback, not unrestricted global keylogging. |
| --- | --- |
| **Recording — Browser** | Playwright for .NET browser instrumentation; capture approved navigation and user interactions with stable locator candidates. Do not require speculative CDP event names or record network payloads. |
| **Recording — Terminal** | Approved Windows shell-session adapter (ConPTY where appropriate). Capture command boundaries and bounded, sanitized outcome evidence; do not archive unrestricted terminal streams or secrets. |
| **Voice transcription** | Included: local/offline speech-to-text using an approved adapter; no specific product frozen. Narration degrades gracefully if no adapter is installed — recording and publishing are unaffected. |
| **AI authoring integration** | Included: MCP adapter for a configured AI authoring client. Provide sanitized draft/capabilities; accept suggested edits only after human review and CEC validation. Deterministic path available when no AI connector is configured. Local IPC does not imply the AI client never connects externally. |
| **Script signing** | Trusted publisher key pair; artifact hash (SHA-256 of canonical script JSON) signed and embedded in package manifest. |
| **Offline export format** | Optional privacy-checked recording export for offline authoring; a separate signed, CEC-valid script package is used for executable import. Do not confuse the two formats. |
| **Script validation** | Use the published Stage 1 CEC schema/validator/conformance packages as the canonical source. Add only Designer UI diagnostics; do not create a second contract parser. |
| **Registry publishing** | HTTP POST to Orchestrator OC-11 endpoint; receives immutable version reference; stores locally. |
| **Live selector test** | Read-only selector probing in the authoring target session; complete script tests are delegated to the installed compatible Runtime/test harness. Never embed a second execution engine in Designer. |

## 5.  Script Lifecycle Reference

Every script passes through the following states in the Designer before it can be executed by a Runtime worker.

| **State** | **Trigger** | **Description** |
| --- | --- | --- |
| **DRAFT** | DS-09 / DS-08 | Initial script produced by deterministic generation or AI enhancement. Not yet validated. |
| **VALIDATED** | DS-14 passes | All CEC constraints satisfied; no blocking violations. May still contain warnings. |
| **TESTED** | DS-15 passes | Executed at least once against the running target application with SUCCEEDED result. |
| **SIGNED** | DS-16 | Artifact hash computed and signed with trusted publisher key. Immutable from this point. |
| **PUBLISHED** | OC-11 accepts | Registered in the Orchestrator artifact registry; referenced by workers via scriptVersionRef. |
| **SUPERSEDED** | Newer version published | Prior version immutable; may still be pinned by active leases (OC-11 constraint). |

DRAFT → VALIDATED is the minimum path for publishing. TESTED is strongly recommended before the first production publish. Signing follows successful validation and explicit human approval; registry admission is atomic. A locally signed package is not PUBLISHED until the Orchestrator confirms acceptance. TESTED is optional unless deployment policy requires it.

## 6.  Story Catalogue and Build Order

| **Seq** | **ID** | **Capability** | **Group** | **Depends on** |
| --- | --- | --- | --- | --- |
| **1** | **DS-01** | Recording session lifecycle — engine selection, start/stop/pause | **Foundation** | None |
| **2** | **DS-02** | Desktop event capture — UIA, JAB, keyboard, mouse harvest | **Capture** | DS-01 |
| **3** | **DS-03** | Browser event capture — Playwright CDP recording | **Capture** | DS-01 |
| **4** | **DS-04** | Terminal session capture — PTY/ConPTY adapter | **Capture** | DS-01 |
| **5** | **DS-05** | Privacy-safe recording screenshots | **Evidence** | DS-01 |
| **6** | **DS-06** | Voice narration and local transcription | **Evidence** | DS-01 |
| **7** | **DS-07** | Normalize captured events into a minimal typed list | **Pipeline** | One of DS-02/03/04; DS-05 optional |
| **8** | **DS-08** | AI-assisted editing bridge; author approves all patches | **AI** | DS-07, DS-09, DS-14 |
| **9** | **DS-09** | Deterministic draft from captured action sequence | **Generation** | DS-07 |
| **10** | **DS-10** | Script editor — step reorder, name/param editing, human corrections | **Authoring** | DS-09 |
| **11** | **DS-11** | Selector management — live test against running app, fallback chain | **Authoring** | DS-02 or DS-03, DS-10 |
| **12** | **DS-12** | Variable and parameter extraction — input declarations, secretRef | **Authoring** | DS-10 |
| **13** | **DS-13** | Control flow editor — branch conditions and bounded loops | **Authoring** | DS-10, DS-14 |
| **14** | **DS-14** | Script validation — CEC grammar enforcement and policy checks | **Validation** | DS-09 |
| **15** | **DS-15** | Runtime-based test in authorized test target/session | **Testing** | DS-14, DS-11 |
| **16** | **DS-16** | Artifact signing and Orchestrator registry publishing | **Publishing** | DS-14 |
| **17** | **DS-17** | Script version management — draft and published lifecycle | **Publishing** | DS-16 |
| **18** | **DS-18** | Optional sanitized offline recording export | **Isolated** | DS-07; DS-05/06 if enabled |
| **19** | **DS-19** | Verify/approve/import signed scripts for isolated mode | **Isolated** | DS-14, DS-16 |
| **20** | **DS-20** | Audit trail — structured tamper-evident log of Designer lifecycle events | **Observability** | DS-01, DS-14, DS-16 |

## 7.  Functional Stories

### DS-01  —  Recording Session Lifecycle

| **Summary** | The Designer manages the full lifecycle of a recording session: engine selection, start, pause, resume and stop — producing a raw event log and session metadata that feed the normalization pipeline. |
| --- | --- |
| **Context** | A recording session is the entry point for all Designer work. The author chooses which Runtime engine the script will target (Desktop, Browser or Terminal), begins recording while performing the task, and stops when done. The resulting session package contains an action-event list; optional sanitized evidence and narration annotations are included only when enabled. Sessions are local to the Designer; they are never sent anywhere until the author explicitly exports or publishes. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-01-AC01** | Author selects engine and clicks Start | A new recording session begins | sessionId assigned, runtimeType fixed for the session, local event list initialized. Screenshot and narration modules remain disabled unless explicitly enabled. | Engine choice is final for this session; mixed-engine sessions are rejected at normalization. |
| **DS-01-AC02** | Session is RECORDING | Author clicks Pause | RECORDING changes to PAUSED; event recording stops. Any enabled optional screenshot/narration modules pause; disabled modules do not create dependencies. | Resume returns to RECORDING without data loss. |
| **DS-01-AC03** | Session is PAUSED | Author clicks Resume | State=RECORDING; all capture streams resume; elapsed timer continues | Events during pause are absent from the log — a deliberate gap. |
| **DS-01-AC04** | Session is RECORDING or PAUSED | Author clicks Stop | STOPPED seals a minimal local recording: sessionId, runtimeType, timestamps, events and references to any optional evidence actually captured. No mandatory screenshot/narration fields. | No further events accepted after STOPPED. |
| **DS-01-AC05** | Session is STOPPED | Author clicks Discard | Session package deleted from local storage; Designer returns to IDLE | Author must confirm before discard. |
| **DS-01-AC06** | Author tries to start a second session while one is RECORDING | Designer prevents concurrent sessions | Error: "A recording session is already active." No new session created | One active session per Designer instance at a time. |
| **DS-01-AC07** | Session is RECORDING and the target engine process crashes | Designer detects the crash | State=STOPPED; session sealed with crashDetected=true; author notified; partial event log preserved | Partial data may still be usable. |

#### Error Outcomes

- Engine not installed or not licensed → IDLE returns error: engine unavailable.
#### Test Coverage

Unit: session state machine; crash detection; concurrent-session prevention. Integration: start→capture→stop roundtrip with each engine.

#### Traceability

ARCH §4.1; ARCH §15 (Designer after schema slice)

### DS-02  —  Desktop Event Capture — UIA, JAB, Keyboard and Mouse Harvest

| **Summary** | During a Desktop recording session the Designer captures supported, reliably observable target-application interactions using Windows UI Automation as the primary mechanism, Java Access Bridge for eligible Java applications, and keyboard/mouse hooks as supplementary capture. |
| --- | --- |
| **Context** | The Desktop capture engine must collect enough information at the moment of each action to generate a resilient, multi-candidate selector later. Capture only stable target properties and enough context for author review; do not persist the full UIA property tree by default. Optional sanitized screenshots may be captured at selected checkpoints for review, never runtime targeting. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-02-AC01** | Desktop recording session is RECORDING; author clicks a UI element | WinEventHook fires EVENT_OBJECT_FOCUS / EVENT_OBJECT_INVOKED | Record the observed action and the minimum stable UIA identity available (process/window, AutomationId/ControlType, optional parent scope). If event instrumentation cannot prove the click target, mark the entry REVIEW_REQUIRED rather than inventing a selector. | Ancestry chain enables fallback when AutomationId is absent. |
| **DS-02-AC02** | Author types text into a field | WH_KEYBOARD hook fires; element focus confirmed | Create an event for authorized text input using a safe captured value or placeholder. Never persist keys/password plaintext; password and uncertain sensitive fields are replaced with named secret/input placeholders before storage. | Password field value is masked in transcript; replaced with secretRef placeholder. |
| **DS-02-AC03** | Author uses keyboard shortcut (e.g. Ctrl+S) | WH_KEYBOARD hook captures modifier + key | Transcript entry: rawEventType=ui.shortcut; keys=[VK_CONTROL, VK_S]; no element target required | Shortcuts captured without UIA anchor. |
| **DS-02-AC04** | Target application is a Java app with Java Access Bridge enabled | JAB accessibility events fire | Transcript entry: rawEventType=jab.invoke; elementIdentity includes jab.role, jab.name, jab.stateSet; UIA fallback selector also attempted if window accessible via UIA | JAB events cannot anchor control-relative coordinates alone. |
| **DS-02-AC05** | Author interacts with a target application that has no AutomationId on the element | WinEventHook fires; AutomationId is empty | Transcript entry still created; elementIdentity.uiaAutomationId=null; ancestry chain and ClassName+Name recorded; keyboard-fallback hint recorded if Tab-navigable | Designer marks selector quality=REDUCED; author can improve in DS-11. |
| **DS-02-AC06** | Author performs a drag operation | WH_MOUSE captures BUTTONDOWN, MOVE sequence, BUTTONUP | Transcript entry: rawEventType=ui.drag; sourceElement (UIA), targetElement (UIA if available); offset computed relative to source bounding rectangle | No absolute screen coordinates without UIA anchor — both source and target must resolve via UIA. |
| **DS-02-AC07** | Optional screenshot capture is enabled for a selected checkpoint | DS-05 fires screenshot request | When optional screenshots are enabled and privacy checks allow, capture sanitized evidence around selected events. Otherwise record no screenshotRef and continue recording. | Screenshot is for review only; never for runtime targeting. |

#### Error Outcomes

- WinEventHook registration fails → recording blocked; error logged; session stopped.
- JAB not installed for target Java app → transcript entry marked jab.unavailable; UIA-only fallback attempted.
#### Test Coverage

Unit: WinEventHook event parsing; ancestry chain construction; password-field detection; keyboard shortcut capture. Integration: full capture session against WinForms reference app fixture.

#### Traceability

ARCH §4.2; AD-02; CEC §3 (targets schema); Desktop Runtime Spec §DT-05

### DS-03  —  Browser Event Capture — Playwright CDP Recording

| **Summary** | During a Browser recording session the Designer uses Playwright for .NET to capture CDP events from the browser, building a normalized transcript of navigation, DOM interactions and dialog events. |
| --- | --- |
| **Context** | Playwright is the authoritative browser recording and execution foundation for Tracto. The Designer launches or attaches to a Playwright-managed browser context during recording. Every interaction the author makes is observed via the CDP protocol and translated into transcript entries with multiple selector candidates — prioritizing stable, semantic selectors over fragile XPath expressions. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-03-AC01** | Browser recording session starts | Designer launches Playwright-managed browser context | Browser attached; CDP listener active; navigation events subscribed; state=RECORDING | Browser runs in headed mode during recording. |
| **DS-03-AC02** | Author navigates to a URL | Supported Playwright navigation signal is observed | Record an observed navigation with sanitized URL and page context using supported Playwright instrumentation. Do not require a fictitious page.navigate CDP event. | URL stored; used as browser.urlEquals postcondition candidate. |
| **DS-03-AC03** | Author clicks a DOM element | A supported instrumented user click is observed; DOM target is resolved where possible | Record a user click with locator candidates derived from the accessible DOM/Playwright context (test ID, role/name, stable CSS where supported). If attribution is ambiguous, flag REVIEW_REQUIRED; avoid treating Input.dispatchMouseEvent as a required event source. | Multiple selector candidates harvested; ranked by stability. |
| **DS-03-AC04** | Author types into an input field | An author input interaction is observed through supported browser instrumentation | Record non-sensitive typed input tied to a supported locator. Do not persist password or token plaintext; create a typed reference placeholder. | Password value replaced with secretRef placeholder. |
| **DS-03-AC05** | Browser shows a JavaScript alert, confirm or prompt dialog | CDP Page.javascriptDialogOpening fires | Transcript entry: rawEventType=browser.dialog; dialogType=alert\|confirm\|prompt; message captured; author action (accept/dismiss) recorded | Requires script to handle dialog explicitly. |
| **DS-03-AC06** | Author switches to an iframe | Supported frame-scoped browser instrumentation identifies the action | When an interaction occurs in a frame, capture the frame locator context with a supported Playwright API. Do not depend on a particular CDP runtime event to prove author intent. | Cross-frame selectors generated correctly. |
| **DS-03-AC07** | Author downloads a file | A supported browser download event is observed | Record a download action and safe filename/type metadata, only when available; never place file contents or sensitive download URLs in the transcript. | Download step generated in script without file content. |

#### Error Outcomes

- Playwright browser fails to launch → recording blocked; session stopped with error.
- CDP connection lost during recording → session stopped; partial transcript preserved with connectionLost=true flag.
#### Test Coverage

Unit: CDP event parsing; selector priority ranking; password-field detection; iframe scoping. Integration: full capture session against a reference web application.

#### Traceability

ARCH §4.2; ARCH §5.3; CEC §3 (targets schema); Desktop Runtime Spec §5.3

### DS-04  —  Terminal Session Capture — PTY/ConPTY Adapter

| **Summary** | During a Terminal recording session the Designer captures typed commands and observed output through a PTY/ConPTY session adapter, producing a transcript of command-level interactions. |
| --- | --- |
| **Context** | Terminal automation is purely text-based. The Designer uses a supported terminal adapter to record permitted commands and bounded, scrubbed evidence. Where command boundaries cannot be proved, mark the action for human review. Each command boundary is a transcript entry. The resulting script will use terminal-namespaced action types for execution by the Terminal Runtime worker. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-04-AC01** | Terminal recording session starts; author selects CMD, PowerShell or approved Bash via WSL | Designer spawns ConPTY process wrapping the selected shell | PTY session active; stdin capture armed; stdout/stderr buffered; state=RECORDING | Only policy-approved shells are selectable. |
| **DS-04-AC02** | Author types a command and presses Enter | stdin captures the input; stdout/stderr captured until next prompt | Record an approved command as a candidate terminal action with bounded/sanitized observable output or exit status where available. Command boundaries that cannot be determined reliably must be marked REVIEW_REQUIRED; no guessed exit code. | Command boundary detected by prompt-pattern matching. |
| **DS-04-AC03** | Command produces sensitive output (e.g. password prompt response) | stdin capture detects an approved secret-input indicator is present | Suppress capture of secret entry and potentially sensitive terminal output before persistence. If sensitivity cannot be determined reliably, omit content and require manual author review; no-echo alone is insufficient proof. | Do not assume terminal no-echo proves secret safety. |
| **DS-04-AC04** | Author runs a command using curl, Invoke-WebRequest or a database client | Capture records the command; policy flag raised | Transcript entry created with policyFlag=BUSINESS_API_ESCAPE_RISK; validator will reject in DS-14 if not allowlisted | Author is warned at capture time but recording continues. |
| **DS-04-AC05** | A command exits with non-zero exit code | The approved shell-session adapter reports a verified exit status, when available | Capture a documented process exit code when actually observable. Interactive command output without a reliable per-command status must not be represented as a proven exit code. | Terminal Runtime uses terminal.exitCodeEquals for postcondition. |
| **DS-04-AC06** | Session is STOPPED | Transcript finalized | ConPTY process closed gracefully; all buffered output flushed to transcript; sessionId sealed | Partial commands at session stop are marked inputComplete=false. |

#### Error Outcomes

- Selected shell not on policy allowlist → session start blocked; error: shell not approved.
- ConPTY fails to initialize → session blocked; error logged.
#### Test Coverage

Unit: ConPTY command boundary detection; no-echo masking; exit-code capture; policy flag for disallowed commands. Integration: capture session against CMD and PowerShell reference fixtures.

#### Traceability

ARCH §4.2; ARCH §5.4; CEC §5 (terminal.* predicates); CEC-014

### DS-05  —  Screenshot Capture, Timestamping and Sensitive-Field Masking

| **Summary** | The Designer captures screen evidence at defined trigger points during any recording session, masks password fields and other sensitive regions, and stores screenshots locally with stable references accessible to AI authoring context. |
| --- | --- |
| **Context** | Screenshots serve three purposes: recording review (author sees what was on screen), AI authoring context (Claude Code understands the visual layout), and diagnostic evidence (test failures). They must NEVER be used for runtime element targeting. Sensitive content must be masked before any screenshot is used outside the local session folder. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-05-AC01** | A recording session enters RECORDING state | Screenshot capture armed | Create a baseline sanitized view of the initial app state. Screenshots are captured by default; if deployment policy restricts screenshots for this session, image capture is disabled and the author is notified. Ordinary recording is always available. | Baseline establishes initial app state. |
| **DS-05-AC02** | A significant action event is recorded (click, navigate, command) | Screenshot trigger fires | Capture screenshots at significant action checkpoints (before and after each step). Avoid fixed hard-coded timing guarantees. A missing image never prevents producing a usable draft. | Captures state change evidence. |
| **DS-05-AC03** | A Desktop screenshot is captured and any element with UIA ControlType=Edit and IsPassword=true is visible | Privacy scan runs | Use reliable sensitive-region metadata for masking before persistence; when coverage is uncertain, discard the image entirely rather than storing an unverified screenshot. | No password text ever stored in screenshot. Applied before any further processing. |
| **DS-05-AC04** | A Browser screenshot is captured and any input[type=password] element is visible | Privacy scan runs | Mask sensitive browser regions before persistence; if robust masking cannot be guaranteed, store no image. Never record password field contents. | Applied before storage, not after. |
| **DS-05-AC05** | An AI authoring call (DS-08) requests screenshot context | Privacy check runs before transmission | AI tooling may receive only author-approved and privacy-checked metadata or images. A local MCP connection does not itself prove that an external AI provider receives no data. | Screenshots never transmitted over any network; MCP is localhost only. |
| **DS-05-AC06** | An offline sanitized export is requested (DS-18) | Export privacy pass runs | Each screenshot re-scanned for sensitive content; any image with sensitiveRegions remaining after masking is excluded from the export package; exclusion logged | Export may have fewer screenshots than the full session. |
| **DS-05-AC07** | Author requests a session review | Screenshot gallery shown | Show available sanitized checkpoints in a simple step preview. No multi-image gallery is required for the baseline; absence of screenshots does not block editing. | No upload required to review locally. |

#### Error Outcomes

- Screenshot capture API fails → transcript entry created without screenshotRef; recording continues; author warned.
- Privacy scan fails to detect password field (false negative) → mitigated by: policy requiring password field masking by coordinate from UIA IsPassword property, not by pixel analysis.
#### Test Coverage

Unit: masking algorithm for each engine; before/after trigger timing; screenshotRef path construction. Security: verify no password text survives masking in any screenshot.

#### Traceability

ARCH §4.2 (screenshot for review only, not targeting); ARCH §4.3 (enterprise privacy controls); CEC §10 (secrets isolation)

### DS-06  —  Voice Narration and Local Transcription

| **Summary** | Voice narration is a Designer feature: an approved local offline speech-to-text adapter captures and timestamps narration during recording. If no approved adapter is installed, recording continues without narration and the author is notified. |
| --- | --- |
| **Context** | Voice narration gives Claude Code the author's intent in their own words: "Now I'm clicking Submit to send the invoice to finance." Narration names steps and provides AI context, but never authorizes business control flow. The local transcription adapter operates offline; any export or AI sharing remains subject to authorization and privacy review. If no approved adapter is installed, recording proceeds without narration. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-06-AC01** | Recording session starts with narration enabled | Microphone capture begins alongside event capture | When an approved offline speech-to-text adapter is installed, microphone capture begins automatically at session start alongside event capture. If no approved adapter is installed, narrationEnabled=false, recording continues normally, and the author is notified. | Optional speech model loaded from local package; no network required. |
| **DS-06-AC02** | Recording session stops | Buffered audio finalized | If enabled, finalize local audio securely and produce timestamped text if the adapter succeeds. Speech transcription is never required to create/validate/publish a script. | Transcription runs offline; may take seconds to minutes depending on session length and model size. |
| **DS-06-AC03** | Optional transcription completes | Alignment pass runs | Attach available narration text to nearby event timestamps as optional authoring notes. No inference about business actions or executable predicates is made. | Alignment is timestamp-based; overlapping segments allowed. |
| **DS-06-AC04** | AI authoring call is made (DS-08) | get_transcript() includes narration | Only reviewed and sanitized narration text may be proposed as optional context to an AI authoring client; do not assume AI calls remain offline merely because Designer exposes local IPC. | Privacy: narration text is plain text — author must not dictate secrets. Policy reminder shown before narration-enabled sessions. |
| **DS-06-AC05** | Author starts a session without narration | Microphone capture not started | narrationEnabled=false; session proceeds normally; narration-absent flag noted in AI call; Claude Code works from event transcript alone | Narration is optional for every story downstream. |
| **DS-06-AC06** | Optional transcription produces a segment with low confidence score | Segment retained but flagged | Any uncertain transcription text is displayed for human correction. No hard-coded transcription-confidence threshold or vendor-specific score is required. | Does not block authoring; just advisory. |

#### Error Outcomes

- Microphone not available → narrationEnabled forced to false; recording proceeds without narration; author notified.
- Whisper model not installed → narrationEnabled forced to false; installer warns during setup.
#### Test Coverage

Unit: local transcription with known audio; timestamp alignment; low-confidence flagging. Integration: full narration session → alignment → optional AI adapter includes narration segments.

#### Traceability

ARCH §4.1 (narration with timestamp-aligned transcripts); ARCH §4.3 (enterprise privacy controls)

### DS-07  —  Transcript Normalization — Raw Events to Common Intermediate Format

| **Summary** | The normalization pipeline transforms raw engine-specific events (DS-02/DS-03/DS-04) into a common intermediate transcript format that is engine-agnostic, deduplicated, selector-ranked and narration-enriched. |
| --- | --- |
| **Context** | The normalized transcript is the single source of truth for all downstream processing: deterministic script generation (DS-09), AI authoring (DS-08), and script editing review (DS-10). It must faithfully represent every intentional action while filtering noise (spurious focus events, OS notifications, redundant state changes). |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-07-AC01** | Raw event log from DS-02, DS-03 or DS-04 is available | Normalization pipeline runs | Each raw event mapped to normalized TranscriptEntry: {sequenceId, timestampUtc, engine, actionIntent, elementIdentity, inputData, screenshotRef, narrationSegmentRef, applicationIdentity, postEventState} | TranscriptEntry schema is engine-agnostic; engine-specific fields nested under elementIdentity. |
| **DS-07-AC02** | Multiple redundant focus events for the same element are present (common from UIA) | Deduplication pass runs | Consecutive focus events for the same element within 200ms collapsed into one; only the last state captured | Reduces noise without losing intent. |
| **DS-07-AC03** | Desktop element has multiple selector candidates | Selector ranking pass runs | Candidates ranked: (1) uiaAutomationId alone, (2) uiaClassName+uiaName, (3) ancestry chain, (4) keyboard-fallback, (5) control-relative coordinates only if UIA anchor confirmed; quality score computed per element | Ranking metadata stored; DS-11 shows ranking to author. |
| **DS-07-AC04** | Browser element has multiple selector candidates | Browser selector ranking pass runs | Candidates ranked: (1) data-testid, (2) aria-role+aria-label, (3) CSS id, (4) XPath, (5) visible text match; quality score computed | Same quality scoring approach as Desktop. |
| **DS-07-AC05** | A transcript entry has inputData.sensitiveHint=true | Sensitive-value pass runs | inputData.value replaced with __SECRET_PLACEHOLDER__; an entry added to session.secretHints for DS-12 to convert to secretRef | No plaintext secret survives normalization. |
| **DS-07-AC06** | Normalization encounters an event with rawEventType that cannot be mapped to a known actionIntent | Unknown event handling | Entry created with actionIntent=UNKNOWN; flagged for author review in DS-10; never silently dropped | Author sees all unknown events; can delete or remap. |
| **DS-07-AC07** | Narration segments from DS-06 are available | Alignment injection | Each TranscriptEntry gets narrationSegmentRef from the alignment produced in DS-06-AC03 | If narration absent: narrationSegmentRef=null. |

#### Error Outcomes

- Raw event log is empty → normalization produces empty transcript; author warned; session discarded.
- Engine mismatch between session metadata and event log → normalization fails; error logged; session sealed as INVALID.
#### Test Coverage

Unit: each raw event type → correct normalized entry; deduplication timing; selector ranking; sensitive-value replacement; unknown event flagging. Integration: DS-02 or DS-03 or DS-04 raw log → normalized transcript → DS-09 consumes it.

#### Traceability

ARCH §4.1 step 3 (normalize demonstration events); CEC §3 (script schema); CEC §3.1 (targets)

### DS-08  —  AI-Assisted Script Enhancement

| **Summary** | The Designer includes an MCP-compatible AI authoring bridge. An approved authoring client (Claude Code or equivalent) may enhance the deterministic draft; author review and canonical validation are mandatory. The deterministic no-AI path remains available when no AI connector is configured. |
| --- | --- |
| **Context** | The fundamental problem of AI-assisted script generation is compatibility: an LLM generating scripts blindly will invent syntax the Executor cannot run. The MCP integration solves this by making the Designer's own validator available as a tool. Claude Code can generate a candidate script, call validate_script() to get precise violations, fix them, and repeat until the script passes. The result is a validated DRAFT that the author can review and approve. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-08-AC01** | A normalized transcript is available; author clicks "Enhance with Claude Code" | When enabled, Designer starts the approved local authoring bridge and connects the configured AI client | Designer starts the approved local MCP authoring bridge when the author invokes AI assistance. If no AI authoring connector is configured, show the deterministic draft/editor flow immediately without starting an MCP server or waiting for a connection. | Named local socket only — no network port; no external egress. |
| **DS-08-AC02** | Claude Code calls get_transcript() | MCP server handles the request | Provide only sanitized, minimum-necessary events and author-selected context. Remote-client transmission requires explicit policy permission; do not expose raw secrets, uncontrolled screenshot paths or raw audio. | No raw audio; no network transmission of screenshots. |
| **DS-08-AC03** | Claude Code calls get_capabilities(runtimeType) | MCP server queries the internal capability registry | Returns: complete action catalogue for the requested runtimeType — actionType enum values, required/optional parameters, target schema, supported precondition/postcondition predicates, policy constraints (maxIterations cap, forbidden constructs) | This is how Claude Code knows what syntax is valid. |
| **DS-08-AC04** | Claude Code calls validate_script(candidateScript) | MCP server runs DS-14 validation against the candidate | Returns: {valid: bool, violations: [{stepId, fieldPath, violationType, message, suggestedFix}]}; Claude Code iterates until valid=true | validate_script may be called multiple times in one session; each call is independent. |
| **DS-08-AC05** | AI candidate passes validation; author accepts a reviewed change preview | MCP server applies the enhanced script as the new DRAFT | A valid AI candidate is a proposed new DRAFT only. Show a diff/preview for explicit author acceptance; then apply the approved patch, clear prior test/signature status and rerun canonical Stage 1 validation. AI cannot publish. | update_script only accepts scripts that passed validate_script in the same session. |
| **DS-08-AC06** | Claude Code calls get_draft_script() | MCP server returns current DRAFT script JSON | Returns the current deterministic draft (DS-09 output or previous AI enhancement); Claude Code uses it as the starting point rather than generating from scratch | Avoids regenerating what the Designer already inferred correctly. |
| **DS-08-AC07** | MCP session ends without update_script being called | Designer state unchanged | If assistance is cancelled or fails before author acceptance, retain the original DRAFT unchanged; no partial modifications are applied. | Atomic: either update_script succeeds or nothing changes. |
| **DS-08-AC08** | Author initiates enhancement but no AI authoring provider is configured | MCP server starts but no client connects within timeout | When no AI connector exists, show ordinary deterministic draft/editor flow immediately. Do not start an MCP server or wait for an unconfigured AI client. | Deterministic path always available; AI enhances it when a connector is configured. |

#### Error Outcomes

- MCP server fails to bind socket → enhancement unavailable; DS-09 DRAFT used; author notified.
- validate_script rejects a business logic expression → violation returned with explicit message; Claude Code must fix before update_script is accepted.
- update_script called with a script that did not pass validate_script in this session → rejected; no DRAFT change.
#### Test Coverage

Unit: each MCP tool handler; validate_script integration with DS-14; update_script atomic apply; no-network assertion (socket only). Integration: full Claude Code session — get_transcript → get_capabilities → validate loop → update_script → DRAFT updated.

#### Traceability

ARCH §4.3 (AI authoring integrations; MCP-compatible tools); ARCH §4.1 step 3; CEC-002 (no business expressions via validator)

### DS-09  —  Deterministic Script Generation — Literal No-AI Path

| **Summary** | The Designer generates an initial DRAFT script from the normalized transcript using a purely deterministic algorithm — no AI, no inference, no network. Every transcript entry becomes one script step, with conservative defaults. |
| --- | --- |
| **Context** | The deterministic path is the always-available baseline. It guarantees a syntactically valid CEC-compliant DRAFT exists even when no AI is configured, no network is available, and no Claude Code is running. The AI enhancement (DS-08) then improves this baseline — but the system always has something valid to start from. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-09-AC01** | Normalized transcript is available; deterministic generation is triggered | Generator processes each TranscriptEntry in sequence order | Translate supported captured interactions to CEC action steps in recorded order using fixed mappings. Unsupported/ambiguous events become editor-only review items, not executable action types. | Conservative, literal mapping — no inference. |
| **DS-09-AC02** | TranscriptEntry has inputData.value=__SECRET_PLACEHOLDER__ | Deterministic generator encounters secret placeholder | Convert redacted sensitive input into an unresolved named secretRef/input placeholder according to the published Stage 1 typed-reference grammar; the Designer never resolves or invents plaintext secrets. | DS-12 converts placeholders to proper secretRef names. |
| **DS-09-AC03** | TranscriptEntry has actionIntent=UNKNOWN | Generator encounters unmapped event | Mark unmapped events REVIEW_REQUIRED in the authoring UI; they have no valid executable actionType and must be mapped or removed before validation can pass. | Unknown steps are included in DRAFT; author decides in DS-10. |
| **DS-09-AC04** | Generation completes | DRAFT script assembled | Script has: schemaVersion=current, automationId=session.sessionId (temporary, author renames in DS-10), runtimeType=session.engine, inputs=[], outputs=[], steps=[], policyRefs=[], targets={}; script state=DRAFT; no signing | Temporary automationId; author must rename before publish. |
| **DS-09-AC05** | Generated step uses control-relative coordinates (Desktop only) | Generator checks: UIA anchor confirmed in transcript | Only emit a pointer target when a stable UIA anchor is verified and the action is allowed. If target or meaningful postcondition cannot be confirmed, require author review; do not invent success postconditions. | No absolute coordinates without confirmed UIA anchor. |
| **DS-09-AC06** | Narration segment is aligned to a step | Narration used for step metadata only | If narration exists, display it as optional step notes after author review. It never silently drives the executable script or assumes intent. | Narration improves readability; never drives execution logic. |

#### Error Outcomes

- Transcript has zero entries → empty DRAFT generated; author warned.
- No mapping exists for a rawEventType and no authoring-engine fallback → step generated as review.UNKNOWN.
#### Test Coverage

Unit: actionIntent → actionType mapping for each engine; secret placeholder handling; unknown event step; no-AI guarantee (no network calls during generation). Integration: DS-07 normalized transcript → DS-09 DRAFT → DS-14 validation passes.

#### Traceability

ARCH §4.1 steps 3-4; CEC §3 (script envelope); CEC §4 (step envelope); ARCH §4.3 (production execution without AI)

### DS-10  —  Script Editor — Step Reorder, Name/Param Editing and Human Corrections

| **Summary** | The Designer provides a visual script editor where the author reviews each step, corrects AI or deterministic generation errors, renames steps, adjusts parameters, and removes unwanted steps before validation and publishing. |
| --- | --- |
| **Context** | No generation process — deterministic or AI-assisted — produces a perfect script for every situation. The script editor is where the author exercises final control. It is also where unknown steps (DS-09-AC03) are resolved and where the automationId is renamed from its temporary value. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-10-AC01** | DRAFT script is open in editor | Author drags a step to a new position | Reorder editable steps while keeping stable step IDs; recompute references only where genuinely necessary and reject forward-only-branch violations. Never silently renumber target references. | Backward jumps are not allowed by CEC; editor prevents them. |
| **DS-10-AC02** | Author clicks a step to edit it | Step detail panel opens | Editable fields shown: displayName, actionType (enum picker), target (selector ref picker → DS-11), parameters (typed field editors), preconditions, postconditions, timeoutMs, retryClass, onFailure | Changes are applied to in-memory DRAFT only until saved. |
| **DS-10-AC03** | Author renames the automationId from the temporary session value | AutomationId editor validates the new value | New automationId must be: non-empty, no filesystem path characters, no URL-like values, unique in the local registry (checked against known published IDs); accepted or rejected with explanation | Uniqueness is advisory at edit time; registry enforces at publish. |
| **DS-10-AC04** | Author deletes a step | Referential integrity check runs | If any other step references the deleted stepId in a selectorRef, outputBinding or control.branch stepList → author warned; deletion requires confirmation; references left dangling are flagged in DS-14 | Editor prevents invisible orphan references. |
| **DS-10-AC05** | A step has actionType=review.UNKNOWN (from DS-09-AC03) | Step highlighted in the editor with "Review required" badge | An unmapped recording item is clearly marked REVIEW_REQUIRED. Publishing is blocked until the author maps it to a supported action or removes it; acknowledgement alone is insufficient. | UNKNOWN steps cannot be published. |
| **DS-10-AC06** | Author adds a new step manually | Step insertion UI used | Author selects actionType from engine-filtered enum (only valid types for the script's runtimeType shown); fills required parameters; assigns stepId automatically; step inserted at selected position | Only types valid for the declared runtimeType are offered. |
| **DS-10-AC07** | Author saves the DRAFT | DRAFT written to local storage | Script state remains DRAFT (not VALIDATED); validation status cleared; author prompted to run DS-14 before testing or publishing | Save is local only; no Orchestrator interaction. |

#### Error Outcomes

- Attempt to insert a step with an actionType from a different runtimeType → blocked; error: cross-engine step not allowed (CEC-008).
#### Test Coverage

Unit: drag-to-reorder with backward-jump detection; deletions with reference check; runtimeType-filtered actionType picker. Integration: full editing session followed by DS-14 validation pass.

#### Traceability

ARCH §4.1 step 5 (human corrections at any stage); CEC-008 (no cross-engine steps); CEC §4 (step envelope)

### DS-11  —  Selector Management — Live Test Against Running App

| **Summary** | The Designer provides a selector manager that displays the ranked fallback chain per script target, allows the author to edit and reorder candidates, and live-tests any selector against the currently running target application in the main window. |
| --- | --- |
| **Context** | Selector quality determines whether a script survives UI changes. The live test feature is what makes the Designer's selector work trustworthy: the author can confirm that a selector resolves to the correct element without running the full script. Testing happens in the same session and window that production execution would use — not a subprocess, not a sandbox. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-11-AC01** | Author opens selector management for a target step | Selector panel shows all candidates for that target | Ranked list displayed: each candidate with: selectorType, value, qualityScore, source (UIA / JAB / Playwright / keyboard); author can drag to reorder | Quality score from DS-07 normalization. |
| **DS-11-AC02** | Author clicks "Live Test" for a selector candidate | Designer invokes the selector against the running target application in the main window | Probe the selector read-only against the authoring target using a supported engine-specific adapter. Report unique/not-found/ambiguous; do not assume authoring and production share one session. | Live selector probing requires an accessible authoring target; it does not need to share a production session. |
| **DS-11-AC03** | Live test finds exactly one element | Element highlighted in the running app | Show a simple uniquely-found result and bounds, with optional privacy-safe highlight. No screenshot overlay or execution-side effect is required. | No automation action performed — observation only. |
| **DS-11-AC04** | Live test finds zero elements | Not found result shown | "Not found" shown in selector panel; selector not confirmed; author can edit and retry; original ranking unchanged until author saves | Does not block; author can try another candidate. |
| **DS-11-AC05** | Live test finds multiple elements | Ambiguous result shown | "Ambiguous — N matches" shown; author must add disambiguation (e.g. ancestry constraint, index, process scope); retry | Matches CEC TARGET_AMBIGUOUS error category. |
| **DS-11-AC06** | Author edits a selector value manually | Edit confirmed by re-running live test | Author types new selector value; live test fires automatically after edit; result shown; if confirmed, selector added to top of candidate list | Manual edit only confirmed if live test passes. |
| **DS-11-AC07** | Target application is not running when live test is requested | Live test blocked | "Target application not running. Start the application and retry." No automation sent | Live test requires a live session — by design, same as production. |

#### Error Outcomes

- UIA FindFirst throws COM exception → live test returns error; selector not confirmed; author notified.
- Playwright locator.count() times out → live test returns timeout; selector not confirmed.
#### Test Coverage

Unit: each selector type live-test handler (UIA/Playwright/PTY); ambiguous result handling; not-found handling. Integration: live selector test against WinForms reference app confirms correct element highlighted.

#### Traceability

ARCH §4.2 (UIA targeting; JAB; coordinates); ARCH §4.1 step 5; CEC §3.1 (named targets); DT-05 (UIA element location)

### DS-12  —  Variable and Parameter Extraction — Input Declarations and SecretRef

| **Summary** | The Designer identifies which hardcoded values in the DRAFT should become typed input parameters (caller-supplied at job time) and which contain sensitive data that must become opaque secretRef values — then generates correct CEC input declarations. |
| --- | --- |
| **Context** | A script that hardcodes an invoice number or a username is not reusable. DS-12 makes scripts parameterized and reusable by surfacing the values the author hardcoded during recording and suggesting which ones should be inputs. SecretRef conversion ensures no plaintext secret survives into a published artifact. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-12-AC01** | DRAFT script contains __SECRET_PLACEHOLDER__ values from DS-07 | Secret conversion pass runs | Turn sensitive placeholders into typed input declarations and CEC-compliant references, retaining only opaque secretRef identifiers. No secret plaintext is created or tested inside Designer. | Naming follows the job input schema; author provides meaningful names. |
| **DS-12-AC02** | DRAFT contains repeated literal values (same invoice number in 3 steps) | Parameterization suggestion shown | Author sees: "This value appears 3 times — consider declaring it as an input parameter"; one-click conversion replaces all occurrences with {ref:"inputs.<name>"}; input declaration added | Parameterization is a suggestion; author can decline. |
| **DS-12-AC03** | Author declares a new input parameter | Input declaration editor open | Fields: name (camelCase, no special chars), type (string \| number \| boolean \| secretRef), required (bool), defaultValue (optional, must conform to type); saved to script.inputs | Type system matches CEC typed input declarations. |
| **DS-12-AC04** | Author declares a script output | Output declaration editor open | Fields: name, type, evidenceTelemetryClassification; classification must match a policy-recognized value; unclassified classification → DS-14 will block publish | Outputs surface extracted values to job results. |
| **DS-12-AC05** | Author attempts to set defaultValue to a plaintext secret (e.g. a password string) | Sensitive pattern detection runs | Pattern match flags the value as likely secret; author warned; defaultValue rejected if input type is secretRef; author redirected to use a secretRef reference | Plaintext secrets may not appear in script.inputs.defaultValue. |

#### Error Outcomes

- SecretRef conversion leaves a placeholder unnamed → DS-14 validation blocks publish; error: unnamed secretRef.
- Input type mismatch between declaration and step parameter reference → DS-14 catches; error: type incompatibility.
#### Test Coverage

Unit: secret placeholder conversion; repeated-literal detection; classification validation; sensitive-defaultValue rejection. Integration: DS-07 transcript with sensitive hints → DS-12 conversion → DS-14 validation passes.

#### Traceability

ARCH §8 (script inputs/outputs); CEC §3 (inputs, outputs with classification); CEC §10 (no plaintext secrets); AD-14

### DS-13  —  Control Flow Editor — Branch Conditions and Bounded Loops

| **Summary** | The Designer provides editors for the two CEC-approved control flow primitives: control.branch (conditional step selection based on technical predicates) and control.repeatBounded (fixed-iteration technical loops with an exit predicate). |
| --- | --- |
| **Context** | Most recorded scripts are linear. Control flow is added by the author after recording to handle known variations: "if the dialog appears, dismiss it; otherwise proceed" or "repeat the row scan until the target row is found or maxIterations is reached." The editor enforces the CEC closed grammar — only approved predicates are selectable. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-13-AC01** | Author selects "Add branch" on a step | Branch editor opens | Author selects a predicate type from a closed-grammar picker (engine-filtered); fills predicate arguments; selects thenSteps (must be later step IDs) and elseSteps (must be later step IDs); no backward jump permitted | Closed-grammar picker: only CEC §5 approved predicates shown. |
| **DS-13-AC02** | Author attempts to add a predicate involving domain data (e.g. response.amount > 10000) | Editor rejects the expression | Business expression not available in picker; if typed manually, DS-14 validation catches it; error: business logic expression prohibited (CEC-002) | No arithmetic/relational domain comparison is selectable. |
| **DS-13-AC03** | Author selects "Add loop" on a step block | Repeat-bounded editor opens | Expose a bounded technical repeat with a literal integer limit checked against the current approved deployment policy. No fixed 100-iteration constant is assumed. | maxIterations is a literal integer; no record-derived value. |
| **DS-13-AC04** | Author sets maxIterations above approved policy cap | Editor rejects | Reject any maxIterations exceeding the approved configured policy cap, showing the actual policy value; never silently replace with 100. | Policy cap enforced at edit time, also at DS-14. |
| **DS-13-AC05** | Author adds a control.branch that references a step ID in the elseSteps that comes BEFORE the branch step | Editor detects backward jump | Error: "elseSteps may only reference later step IDs (no backward jumps)"; author must select a later step | Forward-only control flow enforced; matching CEC §4.1. |
| **DS-13-AC06** | Author uses api.outcomeCategoryEquals predicate in a branch | Predicate editor shows the five allowed outcomes | Picker: SUCCESS, BUSINESS_REJECTION, TECHNICAL_FAILURE, TRANSIENT_FAILURE, UNKNOWN_OUTCOME; author selects one; equality check generated | Only equality checks; no ranges or arithmetic. |

#### Error Outcomes

- Attempt to create recursion (step A branches to step B which branches back to step A) → editor detects cycle; rejects.
#### Test Coverage

Unit: closed-grammar picker filter; maxIterations enforcement; backward-jump detection; domain-expression rejection; cycle detection. Integration: branch created in editor → DS-14 validation passes.

#### Traceability

ARCH §8 (script schema); CEC §4.1 (control.branch, control.repeatBounded); CEC-002 (no business expressions); CEC-011 (bounded technical control)

### DS-14  —  Script Validation — CEC Grammar Enforcement and Policy Checks

| **Summary** | The Designer runs a comprehensive validation pass over any DRAFT script, enforcing all CEC v1.0.1 constraints and deployment policy rules, and returning structured diagnostics with step-level, field-level violation details and suggested fixes. |
| --- | --- |
| **Context** | Validation is the gatekeeper. No script may be signed, published or accepted by update_script (DS-08) without passing this validator. The validator is also the engine behind the MCP validate_script tool, ensuring Claude Code and the Designer enforce identical rules. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-14-AC01** | Author triggers validation on the DRAFT | Full validation pass runs | Result: {valid: bool, violations: [{severity, stepId, fieldPath, violationType, message, suggestedFix}]}; ERROR severity blocks publish; WARNING severity allows publish with acknowledgement; result cached until DRAFT changes | Structured output used by both UI and MCP (DS-08). |
| **DS-14-AC02** | Script contains a step with an actionType from a different runtimeType than script.runtimeType | CEC-008 check fires | violation: {severity:ERROR, stepId, fieldPath:"actionType", violationType:"CROSS_ENGINE_STEP", message:"actionType desktop.click is not valid for runtimeType=browser", suggestedFix:"Change runtimeType to desktop or remove this step"} | Mixed-engine scripts are always rejected. |
| **DS-14-AC03** | Script contains a condition with a business expression (amount > 10000) | CEC-002 check fires | violation: {severity:ERROR, violationType:"BUSINESS_EXPRESSION", message:"Relational comparison over domain field is prohibited", suggestedFix:"Use api.outcomeCategoryEquals instead"} | Allowlist parser rejects any construct not in the approved grammar. |
| **DS-14-AC04** | Script contains an automationId that looks like a file path (e.g. "C:\scripts\invoice.json") | CEC §3.1 identity check fires | violation: {severity:ERROR, violationType:"INVALID_AUTOMATION_ID", message:"automationId must be an opaque registry key; file paths, URLs and executable references are rejected"} | Stable opaque ID enforced. |
| **DS-14-AC05** | Script contains a step output declaration with an unrecognized evidenceTelemetryClassification | Classification check fires | violation: {severity:ERROR, violationType:"UNCLASSIFIED_OUTPUT", message:"Classification X is not in the deployment policy; use one of: [list]"} | Unknown classifications are rejected; always. |
| **DS-14-AC06** | Script has a control.repeatBounded step with maxIterations > policy cap | CEC §4.1 cap check fires | Reject repeatBounded when maxIterations exceeds the configured approved policy cap; do not hard-code 100 unless that exact limit is ratified in deployment policy. | Policy cap enforced as a hard error. |
| **DS-14-AC07** | Script has a selectorRef that does not resolve to any entry in script.targets | CEC §3.1 reference check fires | violation: {severity:ERROR, stepId, fieldPath:"target.selectorRef", violationType:"UNRESOLVED_SELECTOR_REF", message:"selectorRef main-window not found in targets"} | All refs must resolve before publication. |
| **DS-14-AC08** | Script passes all ERROR checks; has steps with actionType=review.UNKNOWN remaining | WARNING check | Any unresolved editor-only action/review item is an ERROR and blocks validation and publication. Non-blocking warnings may be acknowledged only where their nature is explicitly authorized by policy. | WARNING blocks publish until acknowledged. |
| **DS-14-AC09** | Script has no violations or all warnings acknowledged | Validation passes | The canonical Stage 1 validator has returned no blocking errors and no unresolved review items; DRAFT becomes VALIDATED. Any subsequent edit invalidates previous validation, test and signature evidence. | VALIDATED state cleared if DRAFT changes after this point. |

#### Error Outcomes

- Validator encounters a step referencing a preceding step output that has no declared output binding → violation: UNRESOLVED_REFERENCE.
- Schema version is unknown major → violation: UNSUPPORTED_SCHEMA; publisher must declare version upgrade.
#### Test Coverage

Unit: each violation type with a crafted minimal script that triggers it; allowlist parser boundary tests; reference resolution tests. Integration: validate_script MCP tool returns identical results to UI validator for same script.

#### Traceability

CEC-001 through CEC-016; CEC-HOST-001; ARCH §8; ARCH §4.2

### DS-15  —  Runtime-Based Script Test Execution

| **Summary** | The Designer requests the matching versioned Runtime/test harness to execute a VALIDATED DRAFT in an authorized test environment — and shows the author a step-by-step trace with outcomes and screenshots. |
| --- | --- |
| **Context** | Testing with the same compatible Runtime reduces mismatches, but cannot guarantee identical production behavior. The Runtime executes the steps; Designer displays the results, giving the author direct visibility into which steps fail and why. This is not a subprocess or a sandbox — it is the real thing. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-15-AC01** | DRAFT is VALIDATED; author clicks "Run Test" | Test execution begins in the main application window | Invoke the versioned matching Runtime or its approved test harness in an authorized test session. Show status and step results using the same contract; Designer itself does not interpret or execute Desktop/Browser/Terminal automation steps. | Use an authorized test session; production identity is not required. |
| **DS-15-AC02** | A step succeeds with its postcondition confirmed | Step marked SUCCEEDED in trace | Display the Runtime-confirmed step result and postcondition evidence. Screenshots are optional and must pass security policy; a timing field is shown when actually reported. | Confirms selector and action work in the real app. |
| **DS-15-AC03** | A step fails because the selector does not resolve | Step marked FAILED in trace | Step result: state=FAILED; errorCategory=TARGET_NOT_FOUND; selector that failed highlighted; author offered direct link to DS-11 to edit the selector | Actionable failure: author knows exactly which selector to fix. |
| **DS-15-AC04** | A step has an irreversible action (e.g. form submit) and postcondition cannot be confirmed | Step marked UNKNOWN | Step result: state=UNKNOWN; unknownReason provided; needsAttention=true; author warned: "This step submitted a form but outcome is unconfirmed — check the application manually" | Matches CEC UNKNOWN semantics. |
| **DS-15-AC05** | All steps complete with SUCCEEDED | Test marked as PASSED | When the Runtime reports that all steps succeeded, mark this test as PASSED and retain sanitized evidence. Publish eligibility still depends on validated script and required approval; TESTED may be optional by policy. | TESTED state is advisory — publish is allowed from VALIDATED. |
| **DS-15-AC06** | Author cancels the test mid-run | Safe cancel applied | Current step completes if it is in a safe cancellation window; resources released; test result=CANCELLED; held input devices and focus released; application left in whatever state it reached | Mirrors CEC-009 (safe cancel). |
| **DS-15-AC07** | Test execution uses a secretRef input | Secret is resolved from local DPAPI in the main Windows session | Provide opaque secretRef references to the authorized Runtime test environment; only that Runtime resolves secrets at point of use. The Designer never resolves DPAPI credentials directly. | AD-14 (Windows DPAPI local resolution). |

#### Error Outcomes

- Target application not running and step 1 is not a launch action → test blocked: "Please start the target application first."
- Test step injects input into wrong window (foreground changed) → step FAILED; evidence shows unexpected foreground window; author redirected to DS-11.
#### Test Coverage

Unit: step state machine for test runs; cancel path; secretRef masking in trace. Integration: test a known-good VALIDATED script against the WinForms reference app; all steps SUCCEEDED.

#### Traceability

ARCH §4.1 step 6 (test with versioned Runtime); ARCH §5.6 (concurrency — one job per worker); CEC-009 (safe cancel); AD-14

### DS-16  —  Artifact Signing and Orchestrator Registry Publishing

| **Summary** | The Designer computes the script artifact hash, signs it with the trusted publisher key, and publishes the signed artifact to the Orchestrator registry via the OC-11 endpoint, receiving back an immutable version reference. |
| --- | --- |
| **Context** | Publishing is the point of no return for a script version. The signed artifact is immutable. The Orchestrator will reject any attempt to overwrite it (OC-11-AC02). The Designer stores the published version reference locally and marks the version as PUBLISHED. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-16-AC01** | DRAFT is VALIDATED (minimum) and author clicks Publish | Pre-publish checks run | Require current canonical CEC validation, resolved author-review items, trusted signer permissions and explicit author approval. If a live test is required by deployment policy, verify its passing evidence; otherwise testing is recommended. | All ERRORs and unacknowledged WARNINGs block publish. |
| **DS-16-AC02** | Pre-publish checks pass | Artifact hash computed | Compute the artifact digest using the canonicalization/signing algorithm defined by the published Stage 1 artifact package. Do not invent independent JSON canonicalization rules. | Canonical form ensures identical scripts produce identical hashes. |
| **DS-16-AC03** | Hash computed | Signing step runs | Sign the exact validated and author-approved immutable payload using the approved publishing identity; never embed private-key material in the script. | Key management is an operational concern; signer identity logged. |
| **DS-16-AC04** | Package assembled | POST to OC-11 endpoint | Submit the signed package through the Stage 1-compatible Orchestrator registry API. The Orchestrator independently checks signature, schema, policy and identity before committing an immutable version. | OC-11 runs its own validation independently. |
| **DS-16-AC05** | OC-11 returns 201 | Version record stored locally | Only after the registry confirms immutable acceptance does the Designer mark the version PUBLISHED. A locally signed but unregistered package remains unpublished and may be reconciled safely. | Local record is the author's receipt. |
| **DS-16-AC06** | Author attempts to publish a version that already exists in OC-11 | OC-11 returns 409 | Designer shows: "This version already exists in the registry — increment the version and republish."; no local state change | Matches OC-11-AC02 (overwrite rejected). |
| **DS-16-AC07** | Network is unavailable (isolated mode; Orchestrator on localhost WSL) | Publish uses localhost OC-11 endpoint | Same HTTP POST; Orchestrator running in local Docker/WSL; same protocol; no behavioral difference | Connected and isolated use identical publish flow. |

#### Error Outcomes

- Signer key not available → publish blocked; error: "Publisher key not found. Contact your administrator."
- OC-11 returns 422 (prohibited expression) → Designer shows violation detail; author must fix and republish.
#### Test Coverage

Unit: canonical JSON serialization; hash computation; signing; 409 handling; localhost endpoint behavior. Integration: full publish to a real or mock OC-11 endpoint; artifact retrieved by a worker and validated.

#### Traceability

ARCH §4.1 step 6; ARCH §9.5; CEC §10 (signed script import); OC-11; CEC-001 (signed artifact validation)

### DS-17  —  Script Version Management — Draft and Published Lifecycle

| **Summary** | The Designer tracks draft and published versions and offers compatibility warnings based on the actual published Stage 1 version catalogue when an older script must be republished under a new major version. |
| --- | --- |
| **Context** | Version management is what prevents accidental overwrites, tracks history, and ensures authors always know what is published vs. what is still a draft. It is also the operational safety net when CEC compatibility windows change. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-17-AC01** | Author opens a previously published script for editing | Designer creates a new DRAFT copy | New DRAFT has: same automationId, version incremented (author chooses patch/minor/major), state=DRAFT; original PUBLISHED version remains immutable and unchanged | Published versions are never modified — only new versions are created. |
| **DS-17-AC02** | Author views version history for an automationId | Version list shown | List: each version with state (DRAFT / VALIDATED / TESTED / PUBLISHED / SUPERSEDED), publishedAt, artifactHash, signerKeyId, schemaVersion; PUBLISHED versions cannot be deleted while active leases reference them (mirrors OC-11 constraint) | Author sees full audit trail. |
| **DS-17-AC03** | Author has a script published under schemaVersion major 1; a new major 2 is released | Migration guidance shown | When a real newer schema major is published, warn about compatibility and run the canonical validator for the chosen target. Do not assume v2 exists or require a schema-migration wizard initially. | Workers support current + previous major (CEC-010). |
| **DS-17-AC04** | Author increments version to major 3 when current support window is 1 and 2 | Designer warns | Reject publication targeting an unsupported future schema major. A warning acknowledgement cannot override worker/registry compatibility checks. | Publish allowed with explicit acknowledgement; workers will reject. |
| **DS-17-AC05** | Author tries to delete a PUBLISHED version that is pinned by an active OC-11 lease | Deletion blocked | Error: "This version is referenced by an active lease. It cannot be deleted until the lease is released." (Mirrors OC-11-AC06) | Immutability of published versions enforced. |

#### Error Outcomes

- Version increment format invalid (non-semver) → Designer rejects; error: "Version must be semver: MAJOR.MINOR.PATCH."
#### Test Coverage

Unit: version state machine; immutable-PUBLISHED guard; migration wizard flag-finding; schema major window warning. Integration: publish v1 → edit → increment to v2 → publish v2 → v1 still retrievable from OC-11.

#### Traceability

ARCH §8 (version compatibility); CEC-010 (backwards schema window); OC-11-AC02 (overwrite rejected); OC-11-AC06 (delete with active lease)

### DS-18  —  Offline Sanitized Recording Export — File-Based ZIP Package

| **Summary** | The Designer exports a sanitized recording package (sanitized action transcript with optional masked screenshots and narration text) as a signed ZIP file for transfer to a connected workstation where AI-assisted authoring can be performed without the original app or environment. |
| --- | --- |
| **Context** | In isolated environments the recording must happen on the air-gapped machine, but AI authoring can happen on a connected machine. The export package contains everything an external Designer instance needs to understand the recording and produce a script — without containing any sensitive data. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-18-AC01** | Author clicks "Export Recording" on a STOPPED session | Pre-export privacy pass runs | Every transcript entry checked: sensitiveHint fields confirmed replaced with placeholders; screenshotRefs re-scanned; any image with unmasked sensitive region excluded; exclusions logged | Privacy pass is mandatory; cannot be bypassed. |
| **DS-18-AC02** | Privacy pass completes | Export package assembled | Build an optional, minimal sanitized recording package: manifest and safe typed events; approved narration/images are included only if present and verified. This is NOT an executable script artifact. | No raw audio included; optional narration is sanitized text only. |
| **DS-18-AC03** | Package assembled | Package signed | Apply the organization-approved package integrity method. The executable signed script publication format remains the Stage 1 contract format and must not be conflated with recording export. | Recipient can verify package integrity before using it. |
| **DS-18-AC04** | Author specifies export destination | File saved | ZIP saved to author-selected local path (or removable media); export audit entry written to local log: {sessionId, exportedAt, destinationHash, signerKeyId} | Destination is local; no network upload — file-based by design. |
| **DS-18-AC05** | The export package is imported on a connected workstation Designer instance | Import verified | Recipient Designer verifies: signature, manifest hash, each file hash; on pass → session loaded in read-only mode for AI authoring; on fail → import rejected; error: "Package integrity check failed" | Integrity check is mandatory before any authoring begins on the imported session. |
| **DS-18-AC06** | AI authoring (DS-08) runs on the imported session on the connected workstation | Claude Code calls get_transcript() | The connected Designer imports a sanitized recording and can optionally use AI; deterministic and manual authoring still work without AI. | The connected workstation Designer operates exactly as if the session were local. |
| **DS-18-AC07** | Author exports the enhanced DRAFT from the connected workstation | Script package produced (DS-16 flow) | The author publishes the final CEC-valid, approved script as a separate signed artifact; it can be transferred for verification and import into isolated mode. | Outbound artifact is a signed script, not a recording; two separate flows. |

#### Error Outcomes

- No screenshots pass the privacy pass (all contain sensitive regions) → export proceeds with no screenshots; author warned; narration and transcript still included.
- Session engine does not match the connected workstation's available capabilities → DS-08 get_capabilities returns reduced set; validation may flag unsupported action types.
#### Test Coverage

Unit: privacy pass algorithm; package assembly; SHA-256 of ZIP; signature; import integrity check. Integration: export on Machine A → import on Machine B → AI authoring → valid DRAFT produced.

#### Traceability

ARCH §9.5 (isolated offline authoring); ARCH §4.3 (enterprise privacy controls); ARCH §15 (Designer exit gate)

### DS-19  —  Signed Script Package Import and Registry Registration

| **Summary** | The Designer accepts a signed script package (from DS-18 return flow or an externally authored package), verifies its integrity and compatibility, requires authorized approval, and atomically registers the script in the Orchestrator registry. |
| --- | --- |
| **Context** | Script package import is the supply-chain boundary for isolated deployments. Every import is an opportunity for a tampered or incompatible script to enter the system. The import is atomic — either the full script is registered or nothing changes — and every import decision is audited. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-19-AC01** | Author initiates script package import | Package verification runs | Checks in order: (1) signer in local trust chain; (2) manifest SHA-256 matches; (3) each declared file SHA-256 matches; (4) schemaVersion is current or previous major; (5) runtimeType matches an installed worker; (6) policyRefs declared are resolvable in deployment config; (7) no prohibited business expressions (DS-14 validation run on import); all must pass | Any single failure → atomic rejection; no file written; error identifies which check failed. |
| **DS-19-AC02** | All verification checks pass | Authorization gate presented | Import requires explicit authorization from a named approver (designated by deployment policy); approver identity logged; without authorization, import is blocked even after verification | Authorization is mandatory; verification alone is not sufficient. |
| **DS-19-AC03** | Authorization granted | Atomic registry registration | After signature, hash, compatibility and explicit approval checks pass, request atomic registry admission of the immutable artifact. A network timeout is reconciled by artifact identity/hash; never claim remote rollback without proof. | Atomic: either fully registered or nothing. |
| **DS-19-AC04** | OC-11 returns 409 (version already exists) | Import rejected | Error: "This automationId + version is already registered. The import would overwrite an immutable published artifact."; no state change; author told to republish with a new version | Cannot overwrite published versions. |
| **DS-19-AC05** | Package contains a script with a business expression that passed a compromised validator | DS-14 validation on import catches it | Import check (7) runs an independent validation; violation found → import rejected regardless of signature | Defense-in-depth: import validator is independent of the exporting machine's validator. |
| **DS-19-AC06** | Package contains a script with schemaVersion from a major not in the support window | Import check (4) fires | Reject any schema major not supported by the currently published Stage 1 version catalogue. During initial v1, do not require a fictional previous major. | CEC-010 enforced at import. |

#### Error Outcomes

- Signer not in local trust chain → import rejected immediately; error: "Signer is not trusted on this system. Update the trust store and retry."
- Network unavailable when trying to reach OC-11 (isolated mode) → OC-11 is localhost; should be reachable; if not, import fails with connectivity error.
#### Test Coverage

Unit: each verification check with a crafted failing package; authorization gate; atomic rollback on OC-11 error; business expression in imported script caught independently. Integration: full import roundtrip from DS-18 export package.

#### Traceability

ARCH §9.5 (signed package import); CEC §10 (signed script package import); CEC-001; CEC-002; CEC-010

### DS-20  —  Audit Trail — Structured Tamper-Evident Log of Designer Lifecycle Events

| **Summary** | The Designer maintains a local, structured audit trail that records all significant lifecycle events — recording start/stop, script validation, signing, publication, MCP tool calls and import/export operations — and exposes a filterable, reverse-chronological view to the author. |
| --- | --- |
| **Context** | Traceability and accountability require that every consequential Designer action be recorded in an immutable, queryable log. The audit trail is the authoritative local record. It is not a debug log; it captures who did what and with what outcome. Two backing stores — an in-memory ring and a local JSONL file — are maintained in tandem via a composite log. Either store may be absent without losing the other. |

#### Acceptance Criteria

| **AC** | **Given** | **When** | **Then** | **Notes** |
| --- | --- | --- | --- | --- |
| **DS-20-AC01** | Any Designer lifecycle event occurs (recording start/stop, validation, signing, publication, MCP call, import, export) | Event handler fires | An `AuditEntry` is written with: unique `Id` (Guid), `Timestamp` (UTC), `Actor`, `Action`, `Category`, `Outcome`, and optional `Details`/`SessionId`. | Every event has a unique Id to allow cross-store deduplication. |
| **DS-20-AC02** | The composite audit log is configured with two backing stores (e.g. InMemory + File) | `Log()` is called | The entry is written to **all** configured backing stores via fan-out; no store is skipped. | Fan-out is synchronous; write failures to a non-primary store are logged but do not propagate to callers. |
| **DS-20-AC03** | `GetEntries()` is called on the composite log | Any ordering of backing stores | All entries from all backing stores are returned, deduplicated by entry `Id`. The first-registered store does not have priority; every store contributes its entries. | Deduplication is by Id: the same entry written to both stores appears once in the merged result. |
| **DS-20-AC04** | The in-memory store has accumulated 10 000 entries | A new entry is logged | The oldest entry is evicted to keep the store at or below 10 000 entries. | Ring-buffer behaviour; the file store retains the full history. |
| **DS-20-AC05** | The author opens the Audit Trail panel | `AuditViewModel.RefreshEntries()` runs | The panel displays at most 50 entries, ordered newest-first (reverse chronological), sourced from `AuditService.GetEntries()`. | The 50-entry cap is a display-level concern; the backing store is not truncated. |
| **DS-20-AC06** | The author selects a category filter in the Audit Trail panel | `CategoryFilter` property is set | Only entries matching the selected category are displayed; filter wiring triggers an automatic refresh without requiring a manual Refresh button click. | Null category = all entries. |
| **DS-20-AC07** | Multiple threads call `InMemoryAuditLog.Log()` concurrently | Parallel writes occur | No entries are lost, no exception is thrown, and the store count does not exceed 10 000. The implementation uses a `ConcurrentQueue`. | Thread-safety is a hard requirement; InMemoryAuditLog is shared across services. |

#### Error Outcomes

- File store write fails (e.g. disk full) → exception swallowed at composite layer; in-memory store continues; author notified via application status bar.
- File store read returns malformed JSONL line → line skipped; remaining lines parsed normally; no exception thrown to callers.

#### Test Coverage

Unit: InMemoryAuditLog log-and-get, category filter, eviction at 10 000, concurrent writes (multi-thread). CompositeAuditLog fan-out, cross-store merge, deduplication, first-store-empty case. AuditService action/outcome per event type. AuditViewModel 50-entry cap, reverse-chronological order, category filter wiring. FileAuditLog roundtrip, malformed-line resilience.

#### Traceability

ARCH §4.3 (enterprise audit); CEC §10 (audit of sensitive operations); DS-16 (sign/publish events); DS-19 (import events)

## 8.  Integration Scenarios DESIGN-IT-01 — DESIGN-IT-08

| **ID** | **Scenario** | **Acceptance condition** | **Dependencies / tests** |
| --- | --- | --- | --- |
| **DESIGN-IT-01** | AI authoring scenario: Desktop record → deterministic draft → AI suggestion → author review → validate → publish | An author-reviewed AI suggestion cannot bypass canonical validation or publish without signing/registry acceptance. Baseline workflow succeeds with AI disabled. | DS-01, DS-02, DS-05, DS-07, DS-09, DS-08, DS-14, DS-16; OC-11. |
| **DESIGN-IT-02** | Browser recording → Playwright events captured → script tested against running browser | Browser recording generates a valid draft; the matching Browser Runtime/test harness executes against a controlled browser fixture and returns the actual results. | DS-01, DS-03, DS-07, DS-09, DS-14, DS-15. |
| **DESIGN-IT-03** | Terminal recording → command sequence captured → validated script has no business-API-escape steps | Terminal record uses approved commands and bounded, redacted evidence; rejected bypass commands never reach a published script. | DS-01, DS-04, DS-07, DS-14. |
| **DESIGN-IT-04** | Live selector test resolves the correct element in the running app | DS-11 live test against a WinForms app: UIA selector returns exactly 1 match; element highlighted; selectorRef stored in script targets; DS-14 validation resolves the ref without error. | DS-02, DS-11, DS-14. |
| **DESIGN-IT-05** | Narration scenario: timestamped local transcription provides review notes for all recording sessions | Author-approved transcription annotates steps; when no adapter is installed, recording proceeds without narration and remaining features are unaffected. | DS-01, DS-02, DS-06, DS-07, DS-08, DS-10. |
| **DESIGN-IT-06** | Offline isolated roundtrip: record offline → export → AI author on connected machine → import → replay offline without network | An approved sanitized recording export and distinct signed executable script import succeed under isolated-policy controls, with no AI requirement. | DS-01, DS-05, DS-07, DS-18, DS-08, DS-14, DS-16, DS-19, DS-15. |
| **DESIGN-IT-07** | A published script runs on Desktop Runtime end-to-end without error | Script published via DS-16 (OC-11 201); Desktop Runtime worker pulls job; validates artifact hash/signature/schema/runtimeType; executes all steps; reports SUCCEEDED; no errorCategory emitted. | DS-16, OC-11, DT-18 (CEC conformance). |
| **DESIGN-IT-08** | Invalid business predicate is rejected by the canonical validator; optional AI tooling must give the same result when enabled. | CEC validator rejects response.amount > 10000 before sign/publish. If AI/MCP is enabled, its validation bridge returns the same authoritative violation; AI disabled does not block baseline. | DS-08, DS-14. |

## 9.  Definition of Done

The Designer is release-ready when all mandatory capabilities below pass; optional narration and AI adapters are certified only if included in the selected release profile:

- All DS capability criteria pass for the declared release profile. DS-06 narration requires an installed approved adapter; DS-08 AI integration requires a configured connector — both degrade gracefully when unavailable. Isolated-only DS-18/19 requirements gate the isolated profile.
- Required Designer-to-Runtime and Designer-to-Orchestrator integration scenarios pass with declared fixtures. DESIGN-IT-01 (AI) and DESIGN-IT-05 (narration) are included release scenarios; offline roundtrip applies when isolated deployment is claimed.
- DS-08 validate_script MCP tool and DS-14 UI validator produce identical results for all conformance test scripts.
- DS-18 offline export and DS-19 import complete a roundtrip without any sensitive data surviving the privacy pass.
- DS-16 publish to OC-11 produces an artifact that the Desktop Runtime worker validates and executes without error (DESIGN-IT-07).
- No script with a CEC-002 prohibited expression reaches DS-16 (validator blocks it at DS-14 or via MCP).
- Voice transcription (DS-06) is a release deliverable; its approved local/offline adapter must be identified, integrated and verified independently before the feature is marked complete.
- CERTIFICATION.md updated with Designer sprint evidence and architecture sign-off.
## Appendix A  Script Schema Reference

The common outer envelope all Designer-published scripts must conform to. Per CEC v1.0.1 §3.

| **schemaVersion** | Major.minor; current and previous major supported. Unknown major rejected. Example: "1.0". |
| --- | --- |
| **automationId** | Opaque registry key; unique; no path/URL/executable characters. Set by author in DS-10. |
| **version** | Semver publication version; immutable after publishing (OC-11). Incremented per DS-17. |
| **runtimeType** | Enum: desktop \| browser \| terminal. Fixed for entire script; governs action vocabulary. |
| **inputs** | Typed declarations: {name: {type, required, defaultValue?}}. No embedded secrets. secretRef type for sensitive inputs. |
| **outputs** | Typed declarations with evidenceTelemetryClassification per field. Unknown classification rejected. |
| **steps** | Ordered array of step envelopes. Each: stepId, actionType, target?, parameters, preconditions, postconditions, timeoutMs, retryClass, outputBindings?, onFailure?. |
| **policyRefs** | Array of approved policy identifiers; resolved at deployment. Not executable. |
| **targets** | Optional named target map; opaque runtime-specific selector definitions; each entry resolvable at runtime. |
| **artifactHash / signer** | Computed and embedded at DS-16 publish time; verified by OC-11 and by workers before execution. |

**Approved common action types (engine-independent)**

- control.branch — conditional step selection via closed-grammar predicate
- control.repeatBounded — bounded technical iteration; literal maxIterations limited by approved deployment policy
- service.invoke — call a registered custom business service (SYNC or ASYNC)
- value.compose — join literals and approved typed references for UI text or file-path values
**Closed technical-condition grammar — permitted predicate types**

- Element/context state: desktop.elementExists, browser.elementVisible, terminal.processExited
- Technical equality: desktop.uiTextEquals, browser.urlEquals, terminal.exitCodeEquals
- API category equality: api.outcomeCategoryEquals(SUCCESS | BUSINESS_REJECTION | TECHNICAL_FAILURE | TRANSIENT_FAILURE | UNKNOWN_OUTCOME)
- Approved action code equality: api.actionCodeEquals(allowlisted code)
- Boolean composition: all / any / not over approved predicates; bounded depth only
## Appendix B  AI/MCP Authoring Adapter Tool Catalogue

MCP authoring adapter tools — required for AI-assisted authoring, available when a connector is configured. Local IPC is not a guarantee that an AI client never communicates externally; enterprise egress and privacy policy apply.

| **Tool** | **Input** | **Returns** | **Notes** |
| --- | --- | --- | --- |
| **get_transcript** | none | TranscriptEntry[] with narration segments; sensitive values redacted; screenshotRef local paths | Privacy scrubbed before serving. No audio bytes. |
| **get_capabilities** | runtimeType: desktop\|browser\|terminal | {actionTypes[], predicateTypes[], policyConstraints} | Returns live capability catalogue for the target engine. Claude Code uses this to know what syntax is valid. |
| **get_draft_script** | none | Current DRAFT script JSON | Starting point for AI enhancement. Claude Code should improve this, not generate from scratch. |
| **validate_script** | candidateScript: JSON string | {valid: bool, violations: [{severity, stepId, fieldPath, violationType, message, suggestedFix}]} | Same validator as DS-14 UI. May be called multiple times per session. Claude Code iterates until valid=true. |
| **update_script** | finalScript: JSON string | {success: bool, message} | A validated candidate remains a proposal until the author accepts its diff. Apply the accepted draft atomically, clear prior test/signature state and revalidate. |

Security invariant: the MCP server binds only to a named local socket. It never listens on a network port. No transcript data, no screenshot bytes, and no script content travels outside the local machine via the MCP integration.
