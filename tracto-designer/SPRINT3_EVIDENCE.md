# Sprint 3 Evidence — Tracto Designer

**Date:** 2026-10-24
**Sprint Goal:** Script Validation (DS-07) + AI Authoring Bridge / MCP Server (DS-08)

## Exit Criteria

| ID | Criterion | Status | Notes |
|----|-----------|--------|-------|
| EC-01 | DS-07 Script Validation | PASS | 8 rules active (VAL-001..005, VAL-010..012); IScriptValidator + ScriptValidator + ValidationViewModel + ValidationPanel.xaml all present |
| EC-02 | DS-08 AI Bridge / MCP | PASS | IMcpAuthoringServer, McpToolHandler (5 tools: get_transcript, get_capabilities, get_draft_script, validate_script, update_script), NamedPipeMcpServer, NullMcpServer, AiAuthoringViewModel all present; author approval gate on update_script enforced |
| EC-03 | All tests pass | PASS | 76/76 (S1+S2+S3) |
| EC-04 | No TargetFramework in .csproj | PASS | Grep across all 5 .csproj files returned zero matches; framework set via Directory.Build.props |

## Build Result

Build succeeded — 0 errors, 0 warnings.
All 5 projects compiled:
- Tracto.Designer.Core
- Tracto.Designer.Infrastructure
- Tracto.Designer.App
- Tracto.Designer.Unit.Tests
- Tracto.Designer.Integration.Tests

## Tests

Total: **76 passed / 0 failed** (S1: ~25 + S2: ~35 + S3: ~16 MCP/Validation tests)

Sprint 3 test classes:
- `Tracto.Designer.Unit.Tests.Mcp.McpToolHandlerTests` — 16 tests covering all 5 MCP tools, author approval gate, candidate-script validation, and session validation-state reset

## New Files (Sprint 3)

**Core — Validation (DS-07)**
- `src/Tracto.Designer.Core/Validation/IScriptValidator.cs`
- `src/Tracto.Designer.Core/Validation/ScriptValidator.cs` — 8 active rules: VAL-001..005 (script-level), VAL-010..012 (step-level)
- `src/Tracto.Designer.Core/Validation/ScriptValidationResult.cs`
- `src/Tracto.Designer.Core/Validation/ValidationError.cs`
- `src/Tracto.Designer.Core/Validation/ValidationSeverity.cs`
- `src/Tracto.Designer.Core/Validation/ValidationResult.cs`

**Core — MCP (DS-08)**
- `src/Tracto.Designer.Core/Mcp/IMcpAuthoringServer.cs`
- `src/Tracto.Designer.Core/Mcp/McpToolHandler.cs` — 5 tools implemented
- `src/Tracto.Designer.Core/Mcp/McpToolResult.cs`
- `src/Tracto.Designer.Core/Mcp/NullMcpServer.cs`

**Infrastructure — MCP (DS-08)**
- `src/Tracto.Designer.Infrastructure/Mcp/NamedPipeMcpServer.cs` — named pipe IPC at `\\.\pipe\tracto-designer-mcp`

**App — ViewModels / Views (DS-07 + DS-08)**
- `src/Tracto.Designer.App/ViewModels/ValidationViewModel.cs`
- `src/Tracto.Designer.App/Views/ValidationPanel.xaml`
- `src/Tracto.Designer.App/Views/ValidationPanel.xaml.cs`
- `src/Tracto.Designer.App/ViewModels/AiAuthoringViewModel.cs`

## Sprint Goal Assessment

**Yes** — Sprint 3 goal met in full.

- DS-07: `ScriptValidator` implements `IScriptValidator` with coverage of VAL-001 through VAL-012 (8 active rules across script-level and step-level checks). `ValidationPanel.xaml` provides the UI surface. `ValidationViewModel` drives it.
- DS-08: `McpToolHandler` implements all 5 required MCP tools. `NamedPipeMcpServer` delivers localhost IPC via a named pipe. The `update_script` tool enforces author approval (`approved: true`) AND requires a prior `validate_script` call returning `valid=true` in the same session (DS-08-AC05). `NullMcpServer` provides the offline/fallback implementation.
- All 76 unit tests across Sprints 1, 2, and 3 pass with zero failures.
- No `TargetFramework` entries exist in any `.csproj`; the `net10.0-windows` target is set centrally via `Directory.Build.props`.
