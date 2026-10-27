# Tracto Parallel Runtime Tracks — Alignment and Delivery Register

**Date:** 24 October 2026  
**Status:** Proposed functional specifications for architect and contract-owner approval; no claim of implemented Runtime certification.

> **REVIEW UPDATE (2026-10-24):** This register has been updated to fix a P1 contradiction on clipboard restoration (INPUT-006), add missing Supervisor integration reference, and add normative Terminal WSL isolation statement. Both parallel specs have been converted to Markdown and updated with all review findings. See `Tracto_Browser_Runtime_Functional_Spec_v1_1_Aligned.md` and `Tracto_Terminal_Runtime_Functional_Spec_v1_1_Aligned.md`.

## Governing sources

1. Frozen Tracto Architecture v1.2A Independent Runtime Amendment (attached in this request).
2. Completed Common Execution & Orchestrator Interface Contract v1.0.1 (existing Stage 1 baseline).
3. Desktop Runtime Functional Specification v1.0 (attached as document-structure model); previously reviewed Desktop v1.1 decisions inform safety and acceptance alignment.
4. Existing Browser and Terminal Runtime v1.0 functional drafts were reused and refined, rather than inventing another feature catalogue.

## Maintained boundaries

| Rule | Browser | Terminal |
|---|---|---|
| Standalone .NET worker and independent certification | Yes | Yes |
| Part A CEC-HOST-001–009 plus CEC-001–016 | Same Stage 1 packages/contracts | Same Stage 1 packages/contracts |
| One active run per registered worker | Yes | Yes |
| Exclusive foreground-input ownership | Headed mode only | Not required |
| Primary technical execution | Playwright .NET DOM/accessibility | Approved CMD / PowerShell / WSL Bash |
| Only registered business service via service.invoke | Yes | Yes |
| Business calculations/authorization | External service | External service |
| Unknown irreversibly effectful action | Stop attempt, UNKNOWN, reconcile | Stop attempt, UNKNOWN, reconcile |
| Runtime LLM agent at execution | Prohibited | Prohibited |
| Mixed browser/terminal/desktop action script | Prohibited | Prohibited |
| Authenticated static continuation via Orchestrator | Supported at control-plane boundary | Supported at control-plane boundary |
| Shared isolated Windows Host Supervisor | Reused | Reused |

## Browser-specific boundaries

- Permitted authorized site navigation is not a general direct HTTP/API integration feature.
- No user-authored `page.evaluate`, arbitrary `fetch`, Playwright `APIRequest`, CAPTCHA/MFA evasion or browser-fingerprint stealth capabilities.
- Headless workers do not claim an interactive Windows session. Headed Browser and Desktop claim mutually exclusive foreground-input ownership for the same session.
- Saved Playwright `storageState` is sensitive and must be controlled as a secret reference; never embedded in published scripts, traces, or Orchestrator payloads.
- In isolated deployments, approved browser binaries must be bundled rather than downloaded at runtime.
- In isolated-host deployments, the Browser Runtime participates in the shared Windows Runtime Host Supervisor's lifecycle responsibilities for host recovery and foreground-input exclusion (see BRW-SUP-001). In connected mode, the Orchestrator owns all worker registration, lease management and heartbeat handling — no additional Browser-to-Supervisor protocol is required.

> **INPUT-006 — Clipboard restoration classification (corrected 2026-10-24):** The Desktop Runtime Functional Spec v1.1 §3.2 classifies clipboard restoration as **internal, policy-controlled Runtime behavior** — not a script-authored sequence. Scripts must not compose clipboard save/restore using public copy/paste actions. The Browser Runtime aligns with this: normal Playwright `FillAsync` operations do not inherently require manipulating the Windows clipboard; where a Browser action does use the OS clipboard, clipboard preservation is an internal Runtime implementation concern, not a mandatory script obligation. A previous version of this register incorrectly reversed this decision; the above is the approved position.

## Terminal-specific boundaries

- Command references and templates are approved inside the signed artifact and deployment policy; not freely composed at runtime.
- No `curl`, `wget`, PowerShell web command, SQL client, or embedded shell script to circumvent the registered custom business-service rule.
- **WSL Bash commands must execute in a separately provisioned customer command distro/container that is explicitly distinct from the WSL distribution hosting the Tracto Orchestrator.** The runtime must reject any `commandRef` that would run in the Orchestrator's WSL environment. This is a normative requirement verified by acceptance test TER-AT-23 and the Mode Certification gate. (Previously this was mentioned only in this register; it is now a formal requirement in ENV-006 of the Terminal spec.)
- Runtime never assumes Linux can use Windows CurrentUser DPAPI or that a terminal job needs a GUI input lease.
- Kill/timeout of an effectful command does not authorize automatic replay; produces terminal UNKNOWN run state.
- **Terminal secret handling:** Secret values (API tokens, passwords, environment keys) must be passed to commands exclusively via approved Windows-side `secretRef` resolution. They must never appear in command audit records, collected evidence, telemetry, or the Orchestrator job payload. The `outputRedaction` field in the command schema (see Terminal spec §9) declares per-step redaction policy and is required; `none` is not a valid option — redaction cannot be disabled by script.

## Changes from Earlier Working Functional Drafts

- Promoted shared references from provisional CEC v1.0 to completed CEC v1.0.1.
- Standardized example failures as canonical `errorCategory` values plus engine-specific `detailCode` examples.
- Made all five service outcomes and terminal UNKNOWN/HANDED_OFF handling explicit.
- Added eight acceptance tests per engine and requirement-to-test traceability for each existing requirement ID.
- Explicitly separated engine fixtures BRW-AT / TER-AT from architecture deployment POC-01–POC-10.
- Separated development-time mock conformance from final real-Orchestrator release testing.
- Preserved each engine's existing functional requirement IDs and action catalogue; no cross-engine dependency introduced.

## Changes from v1.1 Review (2026-10-24)

The following issues were identified during review against Desktop Runtime Functional Spec v1.1 and Architecture Amendment v1.2A and are now incorporated in the updated Markdown specs:

| ID | Severity | Issue | Resolution |
|---|---|---|---|
| F-01 | P1 | Missing §3.2 Action Gap Classification Table in both specs | Added to Browser §3.2 and Terminal §3.2 in MD versions |
| F-02 | P2 | Missing §12 Script Schema Defaults table in both specs | Added as §12 in both MD versions |
| F-03 | P1 | `UNKNOWN_OUTCOME` incorrectly described as triggering `actionCode` branch; used as error code | Fixed: `UNKNOWN_OUTCOME` → terminate as UNKNOWN; actionCode only for definitive responses. Fixed in Browser BRE-007, Terminal TER-006, both Appendix A |
| F-04 | P2 | Browser Supervisor requirement incorrectly made mandatory for connected mode | Fixed: BRW-SUP-001 limited to isolated-host deployment only |
| F-05 | P3 | Missing action gap narrative for DOM-007 | Added classification note to Browser DOM-007 |
| F-06 | P2 | No POC gate note for headed Browser execution | Added to Browser BRW-002 |
| F-07 | P1 | Error taxonomy used incorrect `errorCategory` values (not CEC §6.2 exact values) | Fixed in both Appendix C: restored exact 11 CEC values (`VALIDATION`, `POLICY_DENIED`, `TARGET_NOT_FOUND`, `TARGET_AMBIGUOUS`, `TIMEOUT`, `SESSION_UNAVAILABLE`, `ENVIRONMENT`, `TRANSPORT`, `AUTHENTICATION`, `CANCELLED`, `INTERNAL`) |
| F-08 | P3 | No normative selector schema reference in Browser spec | Added normative `selectorRef` vocabulary in Browser §9 |
| INPUT-006 | P1 | Register (2026-10-24 pass) incorrectly reversed Desktop v1.1 decision, claiming clipboard restore is script-authored | **Reverted:** Desktop v1.1 §3.2 classifies clipboard restoration as internal, policy-controlled Runtime behavior. Browser aligns. |
| TER-NET | P1 | Terminal §9 included an exception allowing otherwise prohibited API/database access as "approved target workflow" | Fixed: exception removed; `service.invoke` is the only programmatic business-service integration; no exception |
| TER-SCHEMA | P1 | Terminal §9 required command-launch fields on all `terminal.*` steps including post-launch and inspection actions | Fixed: command-launch schema fields (commandRef, shell, cwdRef, etc.) apply only to `terminal.process.start`; other actions use CEC envelope only |
| TER-WSL | P2 | Terminal ENV-006 promoted WSL distro/container arrangement to frozen mandatory architectural rule | Fixed: retained control-plane isolation outcome; precise distro/container arrangement is implementation decision per deployment policy |
| TER-SECRET | P2 | Terminal schema included `secretHandle: none` option that could disable mandatory redaction | Fixed: renamed to `outputRedaction`; `none` is not a valid option; minimum policy enforced by deployment policy baseline |

## Engineering gates to verify, not assume

- Verify the exact compiled Stage 1 schemas and protocol endpoints; do not treat illustrative names as an unapproved wire update.
- Select/test actual Playwright/browser binaries, Windows/PowerShell builds and WSL distribution variants in a certified support matrix.
- Test mock conformance, negative security boundaries, retry/fencing, secrets, cancellation and real Orchestrator compatibility.
- For unattended deployment claims, satisfy only those unchanged architecture POC gates applicable to the advertised modes. The draft documents do not certify these gates.

**Recommended parallel execution:** Implement Browser and Terminal simultaneously, using the same frozen contract fixture suite. Do not wait for Desktop to finish. Each runtime releases independently after its own integration and certification gates.
